// Small runtime boundary for exercising the production tool and native bridge.
using System;
using System.Collections.Generic;
using UnityEngine;
using VRGIN.Controls;
using Valve.VR;
using static VRGIN.Controls.Controller;

namespace UnityEngine
{
    public class Object
    {
        public int GetInstanceID() => GetHashCode();
        public static implicit operator bool(Object value) => value != null;
        public static void Destroy(Object value) { }
        public static void DontDestroyOnLoad(Object value) { }
    }
    public class Texture2D : Object { }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2();
        public float sqrMagnitude => x*x + y*y;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector2 normalized => magnitude > 0 ? new Vector2(x/magnitude, y/magnitude) : zero;
        public static Vector2 operator *(Vector2 v, float s) => new Vector2(v.x*s,v.y*s);
        public static Vector2 Scale(Vector2 a, Vector2 b) => new Vector2(a.x*b.x,a.y*b.y);
        public static float Distance(Vector2 a, Vector2 b) => new Vector2(a.x-b.x,a.y-b.y).magnitude;
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x,v.y,0);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x,v.y);
    }
    public static class Time
    {
        public static int frameCount;
        public static float unscaledTime;
        public static float deltaTime = 0.02f, unscaledDeltaTime = 0.02f, timeScale = 1f;
    }
}
namespace Valve.VR
{
    public enum EVRButtonId { k_EButton_Grip, k_EButton_ApplicationMenu, k_EButton_SteamVR_Trigger, k_EButton_SteamVR_Touchpad }
}
namespace WindowsInput.Native { public enum VirtualKeyCode { ESCAPE, LSHIFT, CONTROL, VK_Z } }
public class SteamVR_TrackedObject { public int index; public bool isValid = true; }
public class SteamVR_Controller
{
    public class Device
    {
        public bool GetPressDown(EVRButtonId button) => false;
        public bool GetPressUp(EVRButtonId button) => false;
        public bool GetPress(EVRButtonId button) => false;
        public Vector2 GetAxis() => Vector2.zero;
    }
}
namespace VRGIN.Controls
{
    public class Controller : UnityEngine.Object
    {
        private readonly GameObject _object = new GameObject("Controller");
        public Transform transform => _object.transform;
        public Transform FindAttachPosition(params string[] names) => null;
        public enum TrackpadDirection { Center, Up, Down, Left, Right }
        public SteamVR_TrackedObject Tracking = new SteamVR_TrackedObject();
        public bool IsTracking => Tracking.isValid;
        public TrackpadDirection GetTrackpadDirection() => TrackpadDirection.Center;
    }
}
namespace VRGIN.Controls.Tools
{
    public abstract class Tool : UnityEngine.Object
    {
        protected Controller Owner;
        protected SteamVR_TrackedObject Tracking;
        protected SteamVR_Controller.Device Controller = new SteamVR_Controller.Device();
        public bool enabled = true;
        public abstract Texture2D Image { get; }
        protected bool IsTracking => Tracking != null && Tracking.isValid;
        protected virtual void OnStart() { Tracking = Owner.Tracking; }
        protected abstract void OnDestroy();
        protected virtual void OnUpdate() { }
        protected virtual void OnEnable() { }
        protected virtual void OnDisable() { }
    }
}
namespace VRGIN.Core
{
    public class Mode
    {
        public Controller Left, Right;
        public int Moves;
        public void MoveToPosition(Vector3 pos, Quaternion rot, bool ignore) { Moves++; }
    }
    public static class VR
    {
        public static Mode Mode = new Mode();
        public static Inputs Input = new Inputs();
        public static CameraRig Camera = new CameraRig();
        public static GameObject Manager = new GameObject("manager");
    }
    public class Inputs { public Mouse Mouse = new Mouse(); public Keyboard Keyboard = new Keyboard(); }
    public class Mouse
    {
        public bool Left, Right;
        public bool Middle;
        public int MoveCount;
        public double CursorX, CursorY;
        public void MiddleButtonDown() { Middle=true; }
        public void MiddleButtonUp() { Middle=false; }
        public void VerticalScroll(int amount) { }
        public void MoveMouseToPositionOnVirtualDesktop(double x,double y)
        {
            MoveCount++; CursorX=x; CursorY=y;
            UnityEngine.Input.mousePosition = new Vector3((float)(x/65535*Screen.width),
                Screen.height-(float)(y/65535*Screen.height),0);
        }
        public void LeftButtonDown() { Left = true; }
        public void LeftButtonUp() { Left = false; }
        public void RightButtonDown() { Right = true; }
        public void RightButtonUp() { Right = false; }
    }
    public class Keyboard
    {
        public bool Escape;
        public readonly HashSet<WindowsInput.Native.VirtualKeyCode> Held = new HashSet<WindowsInput.Native.VirtualKeyCode>();
        public void KeyDown(WindowsInput.Native.VirtualKeyCode key) { Held.Add(key); Escape = Held.Contains(WindowsInput.Native.VirtualKeyCode.ESCAPE); }
        public void KeyUp(WindowsInput.Native.VirtualKeyCode key) { Held.Remove(key); Escape = Held.Contains(WindowsInput.Native.VirtualKeyCode.ESCAPE); }
    }
    public static class VRLog
    {
        public static List<string> Messages = new List<string>();
        public static void Info(string message, params object[] args) { Messages.Add(message); }
        public static void Warn(string message) { Messages.Add(message); }
        public static void Error(string message) { Messages.Add(message); }
    }
}
namespace KK_VR.Settings
{
    public class Setting { public bool Value; }
    public static partial class KoikSettings
    {
        public static Setting FrameKoiAxisDiagnostics = new Setting();
        public static Setting FrameKoiAllowWorldGrip = new Setting();
        public static Setting FrameKoiRightStickTurning = new Setting();
        public static Setting FrameKoiExperimentalStickMove = new Setting();
    }
}
namespace KK_VR.Holders
{
    public class HandHolder
    {
        public VRGIN.Controls.Controller Controller;
        public Quaternion UIPointerRotation = Quaternion.identity;
        public static List<HandHolder> GetHands = new List<HandHolder>();
        public static HandHolder GetHand(int index) => GetHands[index];
        public Handler Handler;
        public static Vector3 Shifted;
        internal static void ShiftWithOrigin(Vector3 delta) { Shifted += delta; }
    }
    public class Handler { public bool IsBusy; }
}
namespace KK_VR.Camera
{
    public class SmoothMover
    {
        public static SmoothMover Instance;
        public void MakeUpright() { }
    }
}
namespace KK_VR.Interpreters
{
    public class SceneInput
    {
        protected bool[,] _pressedButtons = new bool[2,3];
        public bool IsTriggerPress(int i) => _pressedButtons[i,0];
        public bool IsBusy;
        public int UIConfirms;
        internal virtual bool ConfirmUI() { UIConfirms++; return true; }
        internal virtual bool NavigateUI(int x,int y) => false;
        internal virtual void SetSceneMovement(Vector2 axis) { }
        public List<string> Events = new List<string>();
        public Vector2 LeftStick;
        public float RightStick;
        internal virtual void SetFrameKoiLeftStick(Vector2 value) { LeftStick = value; }
        internal virtual void SetFrameKoiRightStick(float value) { RightStick = value; }
        internal virtual void ReleaseController(int index) { }
        internal virtual void FrameKoiSprintClick() { Events.Add("sprint"); }
        internal virtual void OnDisable() { }
        internal virtual void HandleInput() { }
        protected virtual bool OnTrigger(int index, bool press) => false;
        public bool OnButtonDown(int hand, EVRButtonId button, TrackpadDirection direction)
        { Events.Add($"down:{hand}:{button}"); return button == EVRButtonId.k_EButton_SteamVR_Trigger && OnTrigger(hand,true); }
        public void OnButtonUp(int hand, EVRButtonId button, TrackpadDirection direction)
        { Events.Add($"up:{hand}:{button}"); if(button == EVRButtonId.k_EButton_SteamVR_Trigger) OnTrigger(hand,false); }
        internal virtual bool OnDirectionDown(int hand, TrackpadDirection direction) { Events.Add($"direction-down:{hand}:{direction}"); return false; }
        internal virtual void OnDirectionUp(int hand, TrackpadDirection direction) { Events.Add($"direction-up:{hand}:{direction}"); }
        public void OnGripMove(int hand, bool active) { }
    }
    public static class KoikGameInterp
    {
        public enum SceneType { OtherScene, ActionScene }
        public static SceneType CurrentScene;
        public static SceneInput SceneInput = new SceneInput();
    }
}
namespace KK_VR.Controls
{
    public class GripMove
    {
        public GripMove(KK_VR.Holders.HandHolder hand, KK_VR.Holders.HandHolder other) { }
        public void HandleGrabbing() { }
        public void StartLag(int frame) { }
        public void AttachGripMove(Transform transform) { }
        public void StopLag() { }
        public void OnTrigger(bool value) { }
        public void OnTouchpad(bool value) { }
    }
    public class KoikMenuTool
    {
        public static int Created, Toggles;
        public bool IsAttached;
        public KoikMenuTool(Controller owner) { Created++; }
        public static void ToggleState() { Toggles++; }
        public void AbandonGUI() { }
        public static void SuppressLegacyPanel() { }
    }
#if !REAL_MENU
    public class KoikMenuHandler
    {
        public static int Created;
        public static bool MenuInRange;
        public bool LaserVisible;
        public static int Submissions;
        public static bool SubmitSelectedUI() { Submissions++; return false; }
        public static KoikMenuHandler FindTargetedPointer(KoikMenuHandler preferred) => MenuInRange ? preferred : null;
        private bool _pressed;
        public KoikMenuHandler(Controller owner) { Created++; }
        public bool CheckMenu() => MenuInRange;
        public void ReleaseInput() { if (_pressed) OnTrigger(false); }
        public void Dispose() { }
        public void SetLaserVisibility(bool visible) { LaserVisible = visible; }
        public void OnTrigger(bool down)
        { _pressed = down; if (down) VRGIN.Core.VR.Input.Mouse.LeftButtonDown(); else VRGIN.Core.VR.Input.Mouse.LeftButtonUp(); }
        public void OnGrip(bool down) { }
        public void OnTouchpad(bool down) { }
        public void SetLastDirection(TrackpadDirection direction) { }
    }
#endif
}
