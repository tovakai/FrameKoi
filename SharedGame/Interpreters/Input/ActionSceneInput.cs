using System;
using System.Collections.Generic;
using System.Text;
using KK_VR.Camera;
using KK_VR.Features;
using KK_VR.Holders;
using KK_VR.Settings;
using UnityEngine;
using VRGIN.Core;
using WindowsInput.Native;
using static KK_VR.Interpreters.ActionSceneInterp;
using static VRGIN.Controls.Controller;

namespace KK_VR.Interpreters
{
    internal class ActionSceneInput : SceneInput
    {
        private bool _standing = true;
        /// <summary>
        /// For button prompted crouch.
        /// </summary>
        private bool _crouching;
        private bool _walking;
        private float _continuousRotation;
        private float _rightStickTurn;
        private Vector2 _leftStickMove;
        private bool _snapStickArmed = true;
        private Pressed _buttons;
        internal Transform _eyes;

        internal Vector3 GetEyesPosition()
        {
            if (_eyes == null)
            {
                _eyes = actionScene.Player.chaCtrl.objHeadBone.transform.Find("cf_J_N_FaceRoot/cf_J_FaceRoot/cf_J_FaceBase/cf_J_FaceUp_ty/cf_J_FaceUp_tz/cf_J_Eye_tz");
            }
            return _eyes.TransformPoint(GameSettings.PositionOffset.Value);
        }

        enum Pressed
        {
            LeftMouse = 1,
            RightMouse = 2,
            Shift = 4,
            Ctrl = 8,
            Z = 16
        }
        private bool IsButtonPressed(Pressed button)
        {
            return (_buttons & button) != 0;
        }
        private void PressButton(Pressed button)
        {
            if (!IsButtonPressed(button))
            {
                _buttons |= button;
                switch (button)
                {
                    case Pressed.LeftMouse:
                        VR.Input.Mouse.LeftButtonDown();
                        break;
                    case Pressed.RightMouse:
                        VR.Input.Mouse.RightButtonDown();
                        break;
                    case Pressed.Shift:
                        VR.Input.Keyboard.KeyDown(VirtualKeyCode.LSHIFT);
                        break;
                    case Pressed.Ctrl:
                        VR.Input.Keyboard.KeyDown(VirtualKeyCode.CONTROL);
                        break;
                    case Pressed.Z:
                        VR.Input.Keyboard.KeyDown(VirtualKeyCode.VK_Z);
                        break;
                }
            }
        }
        private void ReleaseButton(Pressed button)
        {
            if (IsButtonPressed(button))
            {
                _buttons &= ~button;
                switch (button)
                {
                    case Pressed.LeftMouse:
                        VR.Input.Mouse.LeftButtonUp();
                        break;
                    case Pressed.RightMouse:
                        VR.Input.Mouse.RightButtonUp();
                        break;
                    case Pressed.Shift:
                        VR.Input.Keyboard.KeyUp(VirtualKeyCode.LSHIFT);
                        break;
                    case Pressed.Ctrl:
                        VR.Input.Keyboard.KeyUp(VirtualKeyCode.CONTROL);
                        break;
                    case Pressed.Z:
                        VR.Input.Keyboard.KeyUp(VirtualKeyCode.VK_Z);
                        break;
                }
            }
        }

        internal override void OnDisable()
        {
            ResetState();
        }

        internal override void HandleInput()
        {
            base.HandleInput();
            if (KoikSettings.FrameKoiTurnMode.Value == KoikSettings.FrameKoiTurningMode.Smooth)
            {
                if (KoikSettings.FrameKoiRightStickTurning.Value)
                {
                    if (_rightStickTurn != 0f)
                        ContinuousRotation(_rightStickTurn);
                }
                else if (_continuousRotation != 0f)
                {
                    ContinuousRotation(_continuousRotation);
                }
            }
            if (_walking)
            {
                CameraToPlayer(true);
            }
            // This intentionally runs after the legacy camera follow. The native
            // player walk path does not accept a 2D vector, so keep this opt-in
            // until collisions and navigation are tested on real hardware.
            if (KoikSettings.FrameKoiExperimentalStickMove.Value)
            {
                ApplyFrameKoiLeftStickMovement();
            }
            UpdateCrouch();
        }
        internal override bool OnDirectionDown(int index, TrackpadDirection direction)
        {
            switch (direction)
            {
                case TrackpadDirection.Up:
                    if (actionScene.Player.isGateHit 
                        || actionScene.Player.actionTarget != null
                        || actionScene.Player.isActionPointHit)
                    {
                        PressButton(Pressed.RightMouse);
                    }
                    else if (_walking)
                    {
                        ToggleDash();
                    }
                    break;
                case TrackpadDirection.Down:
                    if (!_crouching)
                    {
                        Crouch(buttonPrompt: true);
                    }
                    else
                    {
                        StandUp();
                    }
                    break;
                case TrackpadDirection.Left:
                    if (!KoikSettings.FrameKoiRightStickTurning.Value)
                        Rotation(-GameSettings.RotationAngle.Value);
                    break;
                case TrackpadDirection.Right:
                    if (!KoikSettings.FrameKoiRightStickTurning.Value)
                        Rotation(GameSettings.RotationAngle.Value);
                    break;
            }
            return false;
        }

        internal override void OnDirectionUp(int index, TrackpadDirection direction)
        {
            StopRotation();
        }

        protected override bool OnTrigger(int index, bool press)
        {
            if (press)
            {
                _pressedButtons[index, 0] = true;
                StartWalking();
            }
            else
            {
                _pressedButtons[index, 0] = false;
                ResetState();
            }
            return false;
        }

        /// <summary>
        /// Feed the actual right-controller analog X axis. The caller supplies this
        /// every frame, including zero, so center/release arms snap turning again.
        /// Separate from touchpad directional events used by other scenes.
        /// </summary>
        internal override void SetFrameKoiRightStick(float rawX)
        {
            if (!KoikSettings.FrameKoiRightStickTurning.Value)
            {
                _rightStickTurn = 0f;
                _snapStickArmed = true;
                return;
            }

            var deadzone = KoikSettings.FrameKoiStickDeadzone.Value;
            var magnitude = Mathf.Abs(rawX);
            if (magnitude <= deadzone)
            {
                _rightStickTurn = 0f;
                _snapStickArmed = true;
                return;
            }

            var mode = KoikSettings.FrameKoiTurnMode.Value;
            if (mode == KoikSettings.FrameKoiTurningMode.Smooth)
            {
                _rightStickTurn = Mathf.Sign(rawX) *
                    Mathf.Clamp01((magnitude - deadzone) / (1f - deadzone));
            }
            else
            {
                _rightStickTurn = 0f;
                if (mode == KoikSettings.FrameKoiTurningMode.Snap &&
                    _snapStickArmed && magnitude >= 0.75f)
                {
                    SnapRotation(Mathf.Sign(rawX) * GameSettings.RotationAngle.Value);
                    _snapStickArmed = false;
                }
            }
        }

        internal override void SetFrameKoiLeftStick(Vector2 rawAxis)
        {
            if (!KoikSettings.FrameKoiExperimentalStickMove.Value)
            {
                _leftStickMove = Vector2.zero;
                return;
            }

            // Radial deadzone preserves diagonals and proportional movement.
            var length = Mathf.Min(rawAxis.magnitude, 1f);
            var deadzone = KoikSettings.FrameKoiMoveDeadzone.Value;
            _leftStickMove = length <= deadzone ? Vector2.zero :
                rawAxis.normalized * ((length - deadzone) / (1f - deadzone));
        }

        private void ApplyFrameKoiLeftStickMovement()
        {
            if (_leftStickMove.sqrMagnitude < 0.0001f || actionScene?.Player == null)
                return;

            var headForward = VR.Camera.Head.forward;
            headForward.y = 0f;
            if (headForward.sqrMagnitude < 0.001f)
                return;
            headForward.Normalize();

            var right = Vector3.Cross(Vector3.up, headForward);
            var movement = (right * _leftStickMove.x + headForward * _leftStickMove.y) *
                (KoikSettings.FrameKoiMoveSpeed.Value * Mathf.Min(Time.unscaledDeltaTime, 0.05f));

            // Direct origin translation may bypass the game's collision checks;
            // it is therefore disabled by default and explicitly experimental.
            VR.Camera.Origin.position += movement;
            PlayerToCamera();
        }

        private void Rotation(float degrees)
        {
            switch (KoikSettings.FrameKoiTurnMode.Value)
            {
                case KoikSettings.FrameKoiTurningMode.Smooth:
                    _continuousRotation = Mathf.Sign(degrees);
                    break;
                case KoikSettings.FrameKoiTurningMode.Snap:
                    _continuousRotation = 0f;
                    SnapRotation(degrees);
                    break;
                default:
                    _continuousRotation = 0f;
                    break;
            }
        }

        private void StopRotation()
        {
            _continuousRotation = 0f;
        }

        /// <summary>
        /// Rotate the camera. If we are in Roaming, rotate the protagonist as well.
        /// </summary>
        private void SnapRotation(float degrees)
        {
            //VRLog.Debug("Rotating {0} degrees", degrees);
            CameraToPlayer(true);

            var camera = VR.Camera.transform;
            var newRotation = Quaternion.AngleAxis(degrees, Vector3.up) * camera.rotation;
            CameraMover.Instance.MoveTo(camera.position, newRotation);
            PlayerToCamera();
        }
        private void ContinuousRotation(float degrees)
        {
            var origin = VR.Camera.Origin;
            var head = VR.Camera.Head;
            var oldPos = head.position;
            var deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            origin.rotation = Quaternion.Euler(0f, degrees * KoikSettings.FrameKoiSmoothTurnSpeed.Value * deltaTime, 0f) * origin.rotation;
            origin.position += oldPos - head.position;

            if (!_walking)
            {
                PlayerToCamera();
            }
        }

        public void CameraToPlayer(bool onlyPosition = false)
        {

            //var headCam = VR.Camera.transform;

            var pos = GetEyesPosition();
            if (!GameSettings.UsingHeadPos.Value)
            {
                var player = actionScene.Player;
                pos.y = player.position.y + (_standing ? GameSettings.StandingCameraPos.Value : GameSettings.CrouchingCameraPos.Value);
            }

            VR.Mode.MoveToPosition(pos, onlyPosition ? Quaternion.Euler(0f, VR.Camera.transform.eulerAngles.y, 0f) : _eyes.rotation, false);
            //VRMover.Instance.MoveTo(
            //    //pos + cf * 0.23f, // 首が見えるとうざいのでほんの少し前目にする
            //    pos,
            //    onlyPosition ? headCam.rotation : _eyes.rotation,
            //    false,
            //    quiet);
        }

        public void PlayerToCamera()
        {
            var player = actionScene.Player;
            var head = VR.Camera.Head;

            var vec = player.position - GetEyesPosition();
            if (!GameSettings.UsingHeadPos.Value)
            {
                var attachPoint = player.position;
                attachPoint.y = _standing ? GameSettings.StandingCameraPos.Value : GameSettings.CrouchingCameraPos.Value;
                vec = player.position - attachPoint;
            }
            player.rotation = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
            player.position = head.position + vec;
        }

        private void UpdateCrouch()
        {
            if (!_crouching && actionScene.Player.chaCtrl.objTop != null)
            {
                var objTop = actionScene.Player.chaCtrl.objTop;
                if (GameSettings.CrouchByCameraPos.Value && objTop.activeInHierarchy == true)
                {
                    var delta_y = VR.Camera.transform.position.y - objTop.transform.position.y;

                    if (_standing && delta_y < 0.8f)
                    {
                        Crouch(buttonPrompt: false);
                    }
                    else if (!_standing && delta_y > 1f)
                    {
                        StandUp();
                    }
                }
            }
        }

        internal void StartWalking()
        {
            PlayerToCamera();
            PressButton(Pressed.Shift);
            PressButton(Pressed.LeftMouse);
            _walking = true;
            HideMaleHead.ForceHideHead = true;
        }

        private void ToggleDash()
        {
            if (IsButtonPressed(Pressed.Shift))
            {
                ReleaseButton(Pressed.Shift);
            }
            else
            {
                PressButton(Pressed.Shift);
            }
        }

        internal void ResetState()
        {
            foreach (Pressed button in Enum.GetValues(typeof(Pressed)))
            {
                ReleaseButton(button);
            }
            _walking = false;
            _continuousRotation = 0f;
            _rightStickTurn = 0f;
            _leftStickMove = Vector2.zero;
            _snapStickArmed = true;
            _standing = true;
            _crouching = false;
            HideMaleHead.ForceHideHead = false;
        }

        internal void Crouch(bool buttonPrompt)
        {
            if (_standing)
            {
                _standing = false;
                _crouching = buttonPrompt;
                if (!Manager.Config.ActData.CrouchCtrlKey)
                {
                    PressButton(Pressed.Z);
                }
                else
                {
                    PressButton(Pressed.Ctrl);
                }
            }
        }
        internal void StandUp()
        {
            if (!_standing)
            {
                _standing = true;
                _crouching = false;
                if (_walking)
                {
                    // Don't run after standing up.
                    PressButton(Pressed.Shift);
                }
                if (!Manager.Config.ActData.CrouchCtrlKey)
                {
                    ReleaseButton(Pressed.Z);
                }
                else
                {
                    ReleaseButton(Pressed.Ctrl);
                }
            }
        }
    }
}
