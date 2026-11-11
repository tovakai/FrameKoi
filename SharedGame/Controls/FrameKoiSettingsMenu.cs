using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using VRGIN.Core;

namespace KK_VR.Controls
{
    /// <summary>
    /// Opens BepInEx ConfigurationManager (the F1 plugin settings window) from VR.
    /// In KK its F1 hotkey is disabled and the window is only reachable from the
    /// game's Config screen, neither of which works in the headset. Bound by
    /// reflection so ConfigurationManager stays an optional dependency.
    /// </summary>
    internal static class FrameKoiSettingsMenu
    {
        private const string Guid = "com.bepis.bepinex.configurationmanager";
        private static Func<bool> _isOpen;
        private static Action<bool> _setOpen;
        private static float _nextAttempt;
        private static bool _warned;

        private static bool Bind()
        {
            if (_isOpen != null) return true;
            // Plugins may still be loading when first asked; retry, at most once per second.
            if (Time.unscaledTime < _nextAttempt) return false;
            _nextAttempt = Time.unscaledTime + 1f;
            PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out info) || info.Instance == null) return Warn("not installed");
            var property = info.Instance.GetType().GetProperty("DisplayingWindow", BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.PropertyType != typeof(bool) || property.GetSetMethod() == null)
                return Warn("has no DisplayingWindow property");
            _isOpen = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), info.Instance, property.GetGetMethod());
            _setOpen = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), info.Instance, property.GetSetMethod());
            return true;
        }

        private static bool Warn(string reason)
        {
            if (!_warned) VRLog.Warn("FrameKoi settings menu unavailable: ConfigurationManager " + reason + ".");
            _warned = true;
            return false;
        }

        internal static bool IsOpen => Bind() && _isOpen();

        internal static void Toggle()
        {
            if (!Bind()) return;
            _setOpen(!_isOpen());
            FrameKoiInputDiagnostics.Log("settings-menu", "plugin settings window " + (_isOpen() ? "opened" : "closed"), 6);
        }

        internal static void Close()
        {
            if (IsOpen) _setOpen(false);
        }
    }
}
