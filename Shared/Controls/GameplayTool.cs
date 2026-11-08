using UnityEngine;
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

        private KoikMenuTool _menu;

        private KoikMenuHandler _menuHandler;

        private TrackpadDirection _lastDirection;

        private GripMove _gripMove;

        internal bool IsGrip => _gripMove != null;

        public override Texture2D Image
        {
            get;
        }
        protected override void OnDisable()
        {
            DestroyGripMove();
            base.OnDisable();
        }
        protected override void OnStart()
        {
            base.OnStart();
            // A clutch for init to avoid null-refs as I'd rather not touch VRGIN's init.
            this.enabled = false;
        }
        protected override void OnDestroy()
        {

        }
        protected override void OnUpdate()
        {
            HandleInput();
            _gripMove?.HandleGrabbing();
        }
        internal void OnRenderModelLoaded()
        {
            this.enabled = true;
            _index = Owner == VR.Mode.Left ? 0 : 1;
            _menu ??= new KoikMenuTool(Owner);
            _menuHandler ??= new KoikMenuHandler(Owner);
        }
        internal void DestroyGripMove()
        {
            _gripMove = null;
            KoikGameInterp.SceneInput.OnGripMove(_index, active: false);
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
                if (!KoikSettings.FrameKoiAllowWorldGrip.Value)
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
                if (!KoikSettings.FrameKoiAllowWorldGrip.Value)
                {
                    // Always pair down/up, regardless of menu proximity.
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
                if (menuInteractable)
                {
                    _menuHandler.SetLastDirection(direction);
                }
                else
                {
                    if (_lastDirection != VRGIN.Controls.Controller.TrackpadDirection.Center)
                    {
                        KoikGameInterp.SceneInput.OnDirectionUp(_index, _lastDirection);
                    }
                    if (direction != VRGIN.Controls.Controller.TrackpadDirection.Center)
                    {
                        KoikGameInterp.SceneInput.OnDirectionDown(_index, direction);
                    }
                }
                _lastDirection = direction;
            }
        }
    }
}
