using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using VRGIN.Core;

namespace KK_VR.Controls
{
    /// <summary>
    /// Experimental SteamVR Input bridge for modern Frame controllers.
    /// Self-contained because the VRGIN-bundled OpenVR C# wrapper predates IVRInput.
    /// Not enabled by default; requires a manifest installed on disk and a call to Initialize.
    /// GameplayTool dispatches these actions when enabled; legacy controls remain
    /// available if initialization fails or native input is disabled.
    /// </summary>
    internal static class FrameKoiNativeInput
    {
        private const string ActionPrefix = "/actions/framekoi/in/";
        private const string ActionSet = "/actions/framekoi";
        private const ulong InvalidHandle = 0;
        private static readonly Dictionary<string, ulong> Handles = new Dictionary<string, ulong>();
        private static bool _ready;
        private static int _lastFrame = -1;
        private static ulong _setHandle;
        private static InputFunctions _api;

        internal static bool Ready { get { return _ready; } }

        [DllImport("openvr_api", EntryPoint = "VR_GetGenericInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr VR_GetGenericInterface(
            [MarshalAs(UnmanagedType.LPStr)] string version, ref int error);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetManifestDelegate(IntPtr path);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetHandleDelegate(IntPtr name, ref ulong handle);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int UpdateActionsDelegate(
            [In, Out] ActiveActionSet[] sets, uint size, uint count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDigitalDelegate(
            ulong handle, ref DigitalState state, uint size, ulong restrictToDevice);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetAnalogDelegate(
            ulong handle, ref AnalogState state, uint size, ulong restrictToDevice);

        [StructLayout(LayoutKind.Sequential)]
        private struct ActiveActionSet
        {
            public ulong actionSet;
            public ulong restrictedToDevice;
            public ulong secondaryActionSet;
            public uint padding;
            public int priority;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct DigitalState
        {
            [MarshalAs(UnmanagedType.I1)] public bool active;
            public ulong origin;
            [MarshalAs(UnmanagedType.I1)] public bool state;
            [MarshalAs(UnmanagedType.I1)] public bool changed;
            public float updateTime;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct AnalogState
        {
            [MarshalAs(UnmanagedType.I1)] public bool active;
            public ulong origin;
            public float x;
            public float y;
            public float z;
            public float deltaX;
            public float deltaY;
            public float deltaZ;
            public float updateTime;
        }

        private sealed class InputFunctions
        {
            public SetManifestDelegate SetManifest;
            public GetHandleDelegate GetSetHandle;
            public GetHandleDelegate GetActionHandle;
            public UpdateActionsDelegate Update;
            public GetDigitalDelegate GetDigital;
            public GetAnalogDelegate GetAnalog;
        }

        // IVRInput function table slots 0..6, based on Valve's OpenVR C# headers.
        // Slot 3 is GetInputSourceHandle, which isn't used in this bridge.
        private static T FunctionAt<T>(IntPtr table, int slot) where T : class
        {
            var function = Marshal.ReadIntPtr(table, slot * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(function, typeof(T));
        }

        private static IntPtr Utf8(string value)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value + "\0");
            var ptr = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return ptr;
        }

        private static int CallWithUtf8(string value, Func<IntPtr, int> callback)
        {
            var ptr = Utf8(value);
            try { return callback(ptr); }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        internal static bool Initialize(string manifestPath)
        {
            _ready = false;
            Handles.Clear();
            _lastFrame = -1;
            if (String.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
            {
                VRLog.Warn("FrameKoi native input manifest missing: " + manifestPath);
                return false;
            }
            try
            {
                int error = 0;
                var table = VR_GetGenericInterface("FnTable:IVRInput_011", ref error);
                if (table == IntPtr.Zero || error != 0)
                {
                    VRLog.Warn("FrameKoi IVRInput_011 unavailable (error " + error + "). Keeping legacy input.");
                    return false;
                }
                _api = new InputFunctions
                {
                    SetManifest = FunctionAt<SetManifestDelegate>(table, 0),
                    GetSetHandle = FunctionAt<GetHandleDelegate>(table, 1),
                    GetActionHandle = FunctionAt<GetHandleDelegate>(table, 2),
                    Update = FunctionAt<UpdateActionsDelegate>(table, 4),
                    GetDigital = FunctionAt<GetDigitalDelegate>(table, 5),
                    GetAnalog = FunctionAt<GetAnalogDelegate>(table, 6)
                };
                var absolutePath = Path.GetFullPath(manifestPath);
                var result = CallWithUtf8(absolutePath, p => _api.SetManifest(p));
                if (result != 0)
                {
                    VRLog.Warn("FrameKoi SetActionManifestPath failed: " + result);
                    return false;
                }
                result = CallWithUtf8(ActionSet, p => _api.GetSetHandle(p, ref _setHandle));
                if (result != 0 || _setHandle == InvalidHandle)
                {
                    VRLog.Warn("FrameKoi action set unavailable: " + result);
                    return false;
                }
                _ready = true;
                VRLog.Info("FrameKoi native input action set initialized; using native controller dispatcher.");
                return true;
            }
            catch (Exception ex)
            {
                _ready = false;
                VRLog.Warn("FrameKoi native input initialization failed: " + ex.Message);
                return false;
            }
        }

        private static bool ActionHandle(string name, out ulong handle)
        {
            handle = 0;
            if (!_ready || String.IsNullOrEmpty(name)) return false;
            if (Handles.TryGetValue(name, out handle)) return handle != InvalidHandle;
            ulong resolved = 0;
            var result = CallWithUtf8(ActionPrefix + name,
                p => _api.GetActionHandle(p, ref resolved));
            handle = result == 0 ? resolved : InvalidHandle;
            Handles[name] = handle;
            return handle != InvalidHandle;
        }

        internal static bool Poll()
        {
            if (!_ready) return false;
            if (_lastFrame == Time.frameCount) return true;
            var sets = new[] { new ActiveActionSet { actionSet = _setHandle } };
            try
            {
                var status = _api.Update(sets, (uint)Marshal.SizeOf(typeof(ActiveActionSet)), 1);
                if (status != 0)
                {
                    VRLog.Warn("FrameKoi action polling failed (" + status + "); disabling native mode.");
                    _ready = false;
                    return false;
                }
                _lastFrame = Time.frameCount;
                return true;
            }
            catch (Exception ex)
            {
                VRLog.Warn("FrameKoi native input update failed: " + ex.Message);
                _ready = false;
                return false;
            }
        }

        internal static bool Digital(string name)
        {
            try
            {
                ulong handle;
                if (!Poll() || !ActionHandle(name, out handle)) return false;
                var state = new DigitalState();
                return _api.GetDigital(handle, ref state, (uint)Marshal.SizeOf(typeof(DigitalState)), 0) == 0
                    && state.active && state.state;
            }
            catch (Exception ex)
            {
                VRLog.Warn("FrameKoi native digital read failed: " + ex.Message);
                _ready = false;
                return false;
            }
        }

        internal static Vector2 Analog(string name)
        {
            try
            {
                ulong handle;
                if (!Poll() || !ActionHandle(name, out handle)) return Vector2.zero;
                var state = new AnalogState();
                return _api.GetAnalog(handle, ref state, (uint)Marshal.SizeOf(typeof(AnalogState)), 0) == 0
                    && state.active ? new Vector2(state.x, state.y) : Vector2.zero;
            }
            catch (Exception ex)
            {
                VRLog.Warn("FrameKoi native analog read failed: " + ex.Message);
                _ready = false;
                return Vector2.zero;
            }
        }

        internal static void Reset()
        {
            _ready = false;
            Handles.Clear();
            _api = null;
            _lastFrame = -1;
            _setHandle = 0;
        }
    }
}
