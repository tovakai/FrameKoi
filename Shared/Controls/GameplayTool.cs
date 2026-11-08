using UnityEngine;
using System.Collections.Generic;
using WindowsInput.Native;
using KK_VR.Camera;
using VRGIN.Controls.Tools;
using VRGIN.Core;
using KK_VR.Interpreters;
using Valve.VR;
using KK_VR.Holders;
using KK_VR.Settings;
using static VRGIN.Controls.Controller;

namespace KK_VR.Controls
{
    public class GameplayTool : Tool
    {
        private int _index;
        private bool _started;
        private bool _initialized;
        private SceneInput _inputScene;

        private KoikMenuTool _menu;

        private KoikMenuHandler _menuHandler;

        private TrackpadDirection _lastDirection;

        private GripMove _gripMove;
        // Keep release routing paired with the mode selected on physical press.
        private bool _handOnlyGripHeld;
        private float _nextAxisDiagnosticAt;
        private readonly HashSet<string> _nativeHeld = new HashSet<string>();
        private bool _nativeTriggerMenuHeld;
        private bool _nativeAOnMenu;
        private static readonly string[] LeftNativeButtons =
        {
            "left_grip", "left_trigger", "left_stick_click", "left_bumper",
            "dpad_up", "dpad_down", "dpad_left", "dpad_right", "left_view"
        };
        private static readonly string[] RightNativeButtons =
        {
            "right_grip", "right_trigger", "right_stick_click", "right_bumper",
            "a", "b", "x", "y", "right_menu"
        };

        internal bool IsGrip => _gripMove != null;

        public override Texture2D Image
        {
            get;
        }
        protected override void OnEnable()
        {
            base.OnEnable();
            FrameKoiInputDiagnostics.Log("tool-enable-" + GetInstanceID(),
                $"GameplayTool activated, started={_started}, initialized={_initialized}");
        }
        protected override void OnDisable()
        {
            ReleaseInput();
            base.OnDisable();
        }

        internal void ReleaseInput()
        {
            FlushNativeButtons();
            // A lost button-up during a scene or tool transition must not
            // leave the previous scene's grasp handler holding something.
            if (_handOnlyGripHeld && _inputScene != null)
            {
                _inputScene.OnButtonUp(
                    _index, EVRButtonId.k_EButton_Grip, TrackpadDirection.Center);
            }
            _handOnlyGripHeld = false;
            DestroyGripMove();
            if (_index == 0) _inputScene?.SetFrameKoiLeftStick(Vector2.zero);
            else _inputScene?.SetFrameKoiRightStick(0f);
            _menuHandler?.ReleaseInput();
            _lastDirection = TrackpadDirection.Center;
            _inputScene = null;
            FrameKoiInputDiagnostics.Log("tool-release-" + _index, "GameplayTool released input for controller " + _index);
        }
        protected override void OnStart()
        {
            base.OnStart();
            _started = true;
            TryInitialize();
        }
        protected override void OnDestroy()
        {
            ReleaseInput();
            _menu?.AbandonGUI();
            _menuHandler?.Dispose();
        }
        protected override void OnUpdate()
        {
            if (!TryInitialize()) return;
            var sceneInput = KoikGameInterp.SceneInput;
            if (_inputScene != sceneInput)
            {
                ReleaseInput();
                _inputScene = sceneInput;
            }
            // Tool enablement belongs to VRGIN (focus locks/tool selection).
            // A render-model event must never override it.
            if (!IsTracking || sceneInput == null)
            {
                FrameKoiInputDiagnostics.Log("tool-wait-" + GetInstanceID(),
                    $"GameplayTool waiting: controller={_index}, tracking={IsTracking}, scene={sceneInput != null}");
                ReleaseInput();
                return;
            }
            if (FrameKoiNativeInput.Ready)
            {
                NativeMenuInteractable();
                HandleNativeInput();
                if (sceneInput != null)
                {
                    if (_index == 1)
                        sceneInput.SetFrameKoiRightStick(FrameKoiNativeInput.Analog("right_stick").x);
                    else
                        sceneInput.SetFrameKoiLeftStick(FrameKoiNativeInput.Analog("left_stick"));
                }
            }
            else
            {
                if (_nativeHeld.Count != 0) ReleaseInput();
                if (HandHolder.GetHands.Count < 2) return;
                HandleInput();
                if (sceneInput != null)
                {
                    if (_index == 1)
                        sceneInput.SetFrameKoiRightStick(Controller.GetAxis().x);
                    else
                        sceneInput.SetFrameKoiLeftStick(Controller.GetAxis());
                }
            }
            if (KoikSettings.FrameKoiAxisDiagnostics.Value &&
                Time.unscaledTime >= _nextAxisDiagnosticAt)
            {
                _nextAxisDiagnosticAt = Time.unscaledTime + 1f;
                var axis = FrameKoiNativeInput.Ready
                    ? FrameKoiNativeInput.Analog(_index == 0 ? "left_stick" : "right_stick")
                    : Controller.GetAxis();
                FrameKoiInputDiagnostics.Log("axis-" + _index,
                    $"controller {_index} axis x={axis.x:F2}, y={axis.y:F2}, native={FrameKoiNativeInput.Ready}", 15);
            }
            _gripMove?.HandleGrabbing();
        }

        private void HandleNativeInput()
        {
            if (!FrameKoiNativeInput.Poll())
            {
                FlushNativeButtons();
                return;
            }

            var buttons = _index == 0 ? LeftNativeButtons : RightNativeButtons;
            foreach (var button in buttons)
            {
                bool pressed = FrameKoiNativeInput.Digital(button);
                bool wasPressed = _nativeHeld.Contains(button);
                if (pressed && !wasPressed)
                {
                    _nativeHeld.Add(button);
                    OnNativeButton(button, true);
                }
                else if (!pressed && wasPressed)
                {
                    _nativeHeld.Remove(button);
                    OnNativeButton(button, false);
                }
            }
        }

        private void FlushNativeButtons()
        {
            var held = new List<string>(_nativeHeld);
            _nativeHeld.Clear();
            foreach (var name in held)
                OnNativeButton(name, false);
            _nativeTriggerMenuHeld = false;
            _nativeAOnMenu = false;
        }

        private bool NativeMenuInteractable()
        {
            if (_menu.IsAttached || !_menuHandler.CheckMenu()) return false;
            var handler = HandHolder.GetHands.Count > _index ? HandHolder.GetHand(_index).Handler : null;
            var interactable = _inputScene != null && !_inputScene.IsBusy && (handler == null || !handler.IsBusy);
            _menuHandler.SetLaserVisibility(interactable);
            return interactable;
        }

        private void OnNativeButton(string name, bool down)
        {
            var scene = _inputScene;
            FrameKoiInputDiagnostics.Log("dispatch-" + name,
                $"controller {_index} dispatch {name} {(down ? "down" : "up")} scene={scene?.GetType().Name ?? "none"}", 12);
            // Global/menu releases still need to run when the scene has gone away.
            if (scene == null && down) return;
            if (HandHolder.GetHands.Count < 2 &&
                (name.Contains("grip") || name.Contains("trigger") || name.Contains("bumper") ||
                 name.StartsWith("dpad_") || name == "left_stick_click")) return;
            switch (name)
            {
                case "left_grip":
                case "right_grip":
                    // Never starts GripMove: grips belong exclusively to the hands.
                    if (down)
                        scene.OnButtonDown(_index, EVRButtonId.k_EButton_Grip, TrackpadDirection.Center);
                    else
                        scene?.OnButtonUp(_index, EVRButtonId.k_EButton_Grip, TrackpadDirection.Center);
                    break;
                case "left_trigger":
                case "right_trigger":
                    if (down)
                    {
                        _nativeTriggerMenuHeld = NativeMenuInteractable();
                        if (_nativeTriggerMenuHeld)
                            _menuHandler.OnTrigger(true);
                        else
                            scene.OnButtonDown(_index, EVRButtonId.k_EButton_SteamVR_Trigger, TrackpadDirection.Center);
                    }
                    else
                    {
                        if (_nativeTriggerMenuHeld)
                            _menuHandler.OnTrigger(false);
                        else
                            scene?.OnButtonUp(_index, EVRButtonId.k_EButton_SteamVR_Trigger, TrackpadDirection.Center);
                        _nativeTriggerMenuHeld = false;
                    }
                    break;
                case "left_bumper":
                case "right_bumper":
                    // Existing scene-specific secondary / item interaction.
                    if (down)
                        scene.OnButtonDown(_index, EVRButtonId.k_EButton_SteamVR_Touchpad, TrackpadDirection.Center);
                    else
                        scene?.OnButtonUp(_index, EVRButtonId.k_EButton_SteamVR_Touchpad, TrackpadDirection.Center);
                    break;
                case "left_stick_click":
                    if (down) scene.FrameKoiSprintClick();
                    break;
                case "right_stick_click":
                    if (down && SmoothMover.Instance != null) SmoothMover.Instance.MakeUpright();
                    break;
                case "dpad_up":
                case "dpad_down":
                case "dpad_left":
                case "dpad_right":
                    TrackpadDirection direction;
                    switch (name)
                    {
                        case "dpad_up": direction = TrackpadDirection.Up; break;
                        case "dpad_down": direction = TrackpadDirection.Down; break;
                        case "dpad_left": direction = TrackpadDirection.Left; break;
                        default: direction = TrackpadDirection.Right; break;
                    }
                    if (down) scene.OnDirectionDown(_index, direction);
                    else scene?.OnDirectionUp(_index, direction);
                    break;
                case "a":
                    if (down)
                    {
                        _nativeAOnMenu = NativeMenuInteractable();
                        if (_nativeAOnMenu) _menuHandler.OnTrigger(true);
                        else VR.Input.Mouse.LeftButtonDown();
                    }
                    else
                    {
                        if (_nativeAOnMenu) _menuHandler.OnTrigger(false);
                        else VR.Input.Mouse.LeftButtonUp();
                        _nativeAOnMenu = false;
                    }
                    break;
                case "b":
                    if (down) VR.Input.Keyboard.KeyDown(VirtualKeyCode.ESCAPE);
                    else VR.Input.Keyboard.KeyUp(VirtualKeyCode.ESCAPE);
                    break;
                case "x":
                    if (down) VR.Input.Mouse.RightButtonDown();
                    else VR.Input.Mouse.RightButtonUp();
                    break;
                case "y":
                case "left_view":
                case "right_menu":
                    if (down) KoikMenuTool.ToggleState();
                    break;
            }
        }

        internal void OnRenderModelLoaded()
        {
            // Optional notification only. It can arrive before Tool.OnStart,
            // more than once, or while VRGIN intentionally disables this tool.
            FrameKoiInputDiagnostics.Log("render-model-" + GetInstanceID(),
                "GameplayTool received optional render-model callback");
        }

        private bool TryInitialize()
        {
            if (_initialized) return true;
            if (!_started || Owner == null || VR.Mode == null ||
                (Owner != VR.Mode.Left && Owner != VR.Mode.Right))
            {
                FrameKoiInputDiagnostics.Log("waiting-owner-" + GetInstanceID(),
                    "GameplayTool waiting for controller ownership");
                return false;
            }
            // Device indices are assigned by SteamVR and are not hand indices.
            _index = Owner == VR.Mode.Left ? 0 : 1;
            _menu ??= new KoikMenuTool(Owner);
            _menuHandler ??= new KoikMenuHandler(Owner);
            _initialized = true;
            FrameKoiInputDiagnostics.Log("tool-init-" + GetInstanceID(),
                $"GameplayTool initialized controller {_index}, device={Tracking.index}, enabled={enabled}, native={FrameKoiNativeInput.Ready}; render model not required");
            return true;
        }
        internal void DestroyGripMove()
        {
            if (_gripMove == null) return;
            _gripMove = null;
            _inputScene?.OnGripMove(_index, active: false);
        }
        internal void LazyGripMove(int avgFrame)
        {
            // In all honesty tho, the proper name would be retarded, not lazy as it does way more in this mode and lags behind.
            _gripMove?.StartLag(avgFrame);
        }
        internal void AttachGripMove(Transform attachPoint)
        {
            _gripMove?.AttachGripMove(attachPoint);
        }
        internal void UnlazyGripMove()
        {
            _gripMove?.StopLag();
        }
        internal void HideLaser()
        {
            _menuHandler?.SetLaserVisibility(false);
        }

        private void HandleInput()
        {
            var direction = _gripMove != null ? TrackpadDirection.Center : Owner.GetTrackpadDirection();
            var menuInteractable = !_menu.IsAttached && _menuHandler.CheckMenu();

            if (menuInteractable && !_menuHandler.LaserVisible)
            {
                // Don't show laser if something of interest is going on.
                var handler = HandHolder.GetHand(_index).Handler;
                if (KoikGameInterp.SceneInput.IsBusy || (handler != null && handler.IsBusy))
                {
                    menuInteractable = false;
                }
                else
                {
                    _menuHandler.SetLaserVisibility(true);
                }
            }

            if (Controller.GetPressDown(EVRButtonId.k_EButton_ApplicationMenu))
            {
                if (!KoikGameInterp.SceneInput.OnButtonDown(_index, EVRButtonId.k_EButton_ApplicationMenu, direction))
                {
                    KoikMenuTool.ToggleState();
                }
            }

            if (Controller.GetPressDown(EVRButtonId.k_EButton_SteamVR_Trigger))
            {
                if (menuInteractable)
                {
                    _menuHandler.OnTrigger(true);
                }
                else if (!KoikGameInterp.SceneInput.OnButtonDown(_index, EVRButtonId.k_EButton_SteamVR_Trigger, direction))
                {
                    _gripMove?.OnTrigger(true);
                }

            }
            else if (Controller.GetPressUp(EVRButtonId.k_EButton_SteamVR_Trigger))
            {
                if (menuInteractable)
                {
                    _menuHandler.OnTrigger(false);
                }
                else
                {
                    _gripMove?.OnTrigger(false);
                    KoikGameInterp.SceneInput.OnButtonUp(_index, EVRButtonId.k_EButton_SteamVR_Trigger, direction);
                }
            }

            if (Controller.GetPressDown(EVRButtonId.k_EButton_Grip))
            {
                _handOnlyGripHeld = !KoikSettings.FrameKoiAllowWorldGrip.Value;
                if (_handOnlyGripHeld)
                {
                    // FrameKoi hand-first policy: always deliver the grip to the scene,
                    // even near the floating menu. Never start world dragging.
                    KoikGameInterp.SceneInput.OnButtonDown(_index, EVRButtonId.k_EButton_Grip, direction);
                }
                else if (_menu.IsAttached)
                {
                    _menu.AbandonGUI();
                }
                else if (menuInteractable)
                {
                    _menuHandler.OnGrip(true);
                }
                else if (!KoikGameInterp.SceneInput.OnButtonDown(_index, EVRButtonId.k_EButton_Grip, direction))
                {
                    _gripMove = new GripMove(HandHolder.GetHand(_index), HandHolder.GetHand(_index == 0 ? 1 : 0));
                    if (Controller.GetPress(EVRButtonId.k_EButton_SteamVR_Trigger)) _gripMove.OnTrigger(true);
                    if (Controller.GetPress(EVRButtonId.k_EButton_SteamVR_Touchpad)) _gripMove.OnTouchpad(true);
                    KoikGameInterp.SceneInput.OnGripMove(_index, active: true);
                }
            }
            else if (Controller.GetPressUp(EVRButtonId.k_EButton_Grip))
            {
                if (_handOnlyGripHeld)
                {
                    // Always pair down/up, regardless of menu proximity or settings changes.
                    KoikGameInterp.SceneInput.OnButtonUp(_index, EVRButtonId.k_EButton_Grip, direction);
                }
                else if (menuInteractable)
                {
                    _menuHandler.OnGrip(false);
                }
                else
                {
                    KoikGameInterp.SceneInput.OnButtonUp(_index, EVRButtonId.k_EButton_Grip, direction);
                    if (_gripMove != null)
                    {
                        DestroyGripMove();
                    }
                }
                _handOnlyGripHeld = false;
            }

            if (Controller.GetPressDown(EVRButtonId.k_EButton_SteamVR_Touchpad))
            {
                if (menuInteractable)
                {
                    _menuHandler.OnTouchpad(true);
                }
                else if (!KoikGameInterp.SceneInput.OnButtonDown(_index, EVRButtonId.k_EButton_SteamVR_Touchpad, direction))
                {
                    _gripMove?.OnTouchpad(true);
                }
            }
            else if (Controller.GetPressUp(EVRButtonId.k_EButton_SteamVR_Touchpad))
            {
                if (menuInteractable)
                {
                    _menuHandler.OnTouchpad(false);
                }
                else
                {
                    _gripMove?.OnTouchpad(false);
                    KoikGameInterp.SceneInput.OnButtonUp(_index, EVRButtonId.k_EButton_SteamVR_Touchpad, direction);
                }
            }

            if (_lastDirection != direction)
            {
                // When analog sticks are assigned dedicated gameplay roles, their
                // legacy touchpad direction callbacks must not also crouch,
                // interact, turn, or trigger dash. Keep menu UI input unchanged.
                var roaming = KoikGameInterp.CurrentScene == KoikGameInterp.SceneType.ActionScene;
                var suppressStickDirections = roaming &&
                    ((_index == 1 && KoikSettings.FrameKoiRightStickTurning.Value) ||
                     (_index == 0 && KoikSettings.FrameKoiExperimentalStickMove.Value));
                if (menuInteractable)
                {
                    _menuHandler.SetLastDirection(direction);
                }
                else
                {
                    if (!suppressStickDirections)
                    {
                        if (_lastDirection != TrackpadDirection.Center)
                        {
                            KoikGameInterp.SceneInput.OnDirectionUp(_index, _lastDirection);
                        }
                        if (direction != TrackpadDirection.Center)
                        {
                            KoikGameInterp.SceneInput.OnDirectionDown(_index, direction);
                        }
                    }
                }
                _lastDirection = direction;
            }
        }
    }
}

