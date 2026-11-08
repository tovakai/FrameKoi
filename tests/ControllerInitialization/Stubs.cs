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
    }
    public class Transform : Object { }
    public class Texture2D : Object { }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2();
    }
    public static class Time
    {
        public static int frameCount;
        public static float unscaledTime;
    }
}
namespace Valve.VR
{
    public enum EVRButtonId { k_EButton_Grip, k_EButton_ApplicationMenu, k_EButton_SteamVR_Trigger, k_EButton_SteamVR_Touchpad }
}
namespace WindowsInput.Native { public enum VirtualKeyCode { ESCAPE } }
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
        public enum TrackpadDirection { Center, Up, Down, Left, Right }
        public SteamVR_TrackedObject Tracking = new SteamVR_TrackedObject();
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
    public class Mode { public Controller Left, Right; }
    public static class VR
    {
        public static Mode Mode = new Mode();
        public static Inputs Input = new Inputs();
    }
    public class Inputs { public Mouse Mouse = new Mouse(); public Keyboard Keyboard = new Keyboard(); }
    public class Mouse
    {
        public bool Left, Right;
        public void LeftButtonDown() { Left = true; }
        public void LeftButtonUp() { Left = false; }
        public void RightButtonDown() { Right = true; }
        public void RightButtonUp() { Right = false; }
    }
    public class Keyboard
    {
        public bool Escape;
        public void KeyDown(WindowsInput.Native.VirtualKeyCode key) { Escape = true; }
        public void KeyUp(WindowsInput.Native.VirtualKeyCode key) { Escape = false; }
    }
    public static class VRLog
    {
        public static List<string> Messages = new List<string>();
        public static void Info(string message, params object[] args) { Messages.Add(message); }
        public static void Warn(string message) { Messages.Add(message); }
    }
}
namespace KK_VR.Settings
{
    public class Setting { public bool Value; }
    public static class KoikSettings
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
        public static List<HandHolder> GetHands = new List<HandHolder>();
        public static HandHolder GetHand(int index) => GetHands[index];
        public Handler Handler;
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
        public bool IsBusy;
        public List<string> Events = new List<string>();
        public Vector2 LeftStick;
        public float RightStick;
        public void SetFrameKoiLeftStick(Vector2 value) { LeftStick = value; }
        public void SetFrameKoiRightStick(float value) { RightStick = value; }
        public void FrameKoiSprintClick() { Events.Add("sprint"); }
        public bool OnButtonDown(int hand, EVRButtonId button, TrackpadDirection direction)
        { Events.Add($"down:{hand}:{button}"); return false; }
        public void OnButtonUp(int hand, EVRButtonId button, TrackpadDirection direction)
        { Events.Add($"up:{hand}:{button}"); }
        public void OnDirectionDown(int hand, TrackpadDirection direction) { Events.Add($"direction-down:{hand}:{direction}"); }
        public void OnDirectionUp(int hand, TrackpadDirection direction) { Events.Add($"direction-up:{hand}:{direction}"); }
        public void OnGripMove(int hand, bool active) { }
    }
    public static class KoikGameInterp
    {
        public enum SceneType { ActionScene }
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
    }
    public class KoikMenuHandler
    {
        public static int Created;
        public static bool MenuInRange;
        public bool LaserVisible;
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
}
