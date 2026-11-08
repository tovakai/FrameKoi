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
            if (_initialized) FrameKoiInputRuntime.Current?.Disconnect(_index);
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
            _inputScene?.ReleaseController(_index);
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
                if (FrameKoiInputRuntime.Current != null)
                    FrameKoiInputRuntime.Current.TickHand(_index, _menuHandler);
                else
                    FrameKoiInputDiagnostics.Log("router-wait", "waiting for semantic input runtime");
            }
            else
            {
                if (HandHolder.GetHands.Count < 2) return;
                HandleInput();
                if (_index == 1) sceneInput.SetFrameKoiRightStick(Controller.GetAxis().x);
                else sceneInput.SetFrameKoiLeftStick(Controller.GetAxis());
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
#if KKS
            var deviceIndex = Tracking.GetDeviceIndex();
#else
            var deviceIndex = Tracking.index;
#endif
            FrameKoiInputDiagnostics.Log("tool-init-" + GetInstanceID(),
                $"GameplayTool initialized controller {_index}, device={deviceIndex}, enabled={enabled}, native={FrameKoiNativeInput.Ready}; render model not required");
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

