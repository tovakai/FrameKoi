using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Valve.VR;
using VRGIN.Core;
using VRGIN.Controls;
using VRGIN.Visuals;
using System.Linq;
using VRGIN.Native;
using static VRGIN.Native.WindowsInterop;
using KK_VR.Holders;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using KK_VR.Settings;

namespace KK_VR.Controls
{
    internal class KoikMenuHandler
    {        /// <summary>
             /// Handler that is in charge of the menu interaction with controllers
             /// </summary>
        private readonly Transform _controller;
        private const int MOUSE_STABILIZER_THRESHOLD = 30; // pixels
                                                           //private Controller.Lock _LaserLock = Controller.Lock.Invalid;
        private LineRenderer Laser;
        private Vector2? _mouseDownPosition;
        private GUIQuad _quad;
        private Vector3 _scaleVector;
        private Buttons _pressedButtons;
        private Controller.TrackpadDirection _lastDirection;
        private float? _NextScrollTime; 
        private RaycastHit? _lastPointerHit;
        private static KoikMenuHandler _mouseOwner;
        private static int _mouseReleaseFrame = -1;
        internal bool LaserVisible
        {
            get
            {
                return Laser.gameObject.activeSelf;
            }
            set
            {
                if (Laser == null || Laser.gameObject.activeSelf == value) return;
                // Toggle laser
                Laser.gameObject.SetActive(value);
                //VRPlugin.Logger.LogDebug($"LaserVisible = {value}");
                // Initialize start position
                if (value)
                {
                    Laser.SetPosition(0, Laser.transform.position);
                    Laser.SetPosition(1, Laser.transform.position);
                }
                else
                {
                    _mouseDownPosition = null;
                }
            }
        }

        internal void SetLastDirection(Controller.TrackpadDirection direction) => _lastDirection = direction;

        internal static bool SubmitSelectedUI()
        {
            var system = EventSystem.current;
            var selected = system?.currentSelectedGameObject;
            var selectable = selected != null ? selected.GetComponent<Selectable>() : null;
            if (selectable != null && selectable.IsActive() && selectable.IsInteractable())
            {
                ExecuteEvents.Execute(selected, new BaseEventData(system), ExecuteEvents.submitHandler);
                return true;
            }
            // No selected UI means no action. Never synthesize a world LMB click.
            return false;
        }

        internal KoikMenuHandler(Controller controller)
        {
            //_controller = controller;
            _scaleVector = new Vector2((float)VRGUI.Width / Screen.width, (float)VRGUI.Height / Screen.height);
            _controller = controller.transform;
            var attachPosition = FrameKoiNativeInput.Ready ? _controller : controller.FindAttachPosition("tip");

            if (!attachPosition)
            {
                VRLog.Warn("Legacy laser tip unavailable; using controller transform.");
                attachPosition = _controller;
            }
            Laser = new GameObject("Laser").AddComponent<LineRenderer>();
            Laser.transform.SetParent(_controller, worldPositionStays: false); // (attachPosition, false);
            //Laser.transform.SetParent(controller.transform, false); // (attachPosition, false);
            Laser.transform.SetPositionAndRotation(attachPosition.position, attachPosition.rotation);
            Laser.material = new Material(Shader.Find("Sprites/Default"));
            Laser.material.renderQueue += 5000;
            Laser.startColor = new Color(0f, 1f, 1f, 0f);
            Laser.endColor = Color.cyan;

            if (SteamVR.instance.hmd_TrackingSystemName == "lighthouse")
            {
                Laser.transform.localRotation = Quaternion.Euler(60, 0, 0);
                Laser.transform.position += Laser.transform.forward * 0.06f;
            }
            else
            {
#if KK
                Laser.transform.localRotation *= Quaternion.Euler(-10f, 0, 0);
#else
                Laser.transform.localRotation *= Quaternion.Euler(25f, 0, 0);
#endif
            }
            Laser.SetVertexCount(2);
            Laser.useWorldSpace = true;
            Laser.SetWidth(0.002f, 0.002f);
            LaserVisible = false;
        }

        enum Buttons
        {
            Left = 1,
            Right = 2,
            Middle = 4,
        }

        internal bool CheckMenu()
        {
            if (FrameKoiNativeInput.Ready) return UpdateNativePointer();
            if (LaserVisible)
            {
                CheckInput();
                return UpdateLaser();
            }
            else
            {
                return CheckForNearMenu();
            }
        }

        internal void SetLaserVisibility(bool show)
        {
            LaserVisible = show;
        }

        internal void ReleaseInput()
        {
            if ((_pressedButtons & Buttons.Left) != 0) OnTrigger(false);
            if ((_pressedButtons & Buttons.Middle) != 0) OnTouchpad(false);
            _lastDirection = Controller.TrackpadDirection.Center;
            _NextScrollTime = null;
            if (!FrameKoiNativeInput.Ready && _quad != null && _quad.IsOwned) AbandonGUI();
            if (Laser != null) SetLaserVisibility(false);
            _quad = null;
            _lastPointerHit = null;
        }

        internal void Dispose()
        {
            ReleaseInput();
            if (_mouseOwner == this) _mouseOwner = null;
            if (Laser != null)
            {
                UnityEngine.Object.Destroy(Laser.material);
                UnityEngine.Object.Destroy(Laser.gameObject);
            }
        }

        internal void OnTrigger(bool press)
        {
            if (press)
            {
                if (FrameKoiNativeInput.Ready)
                {
                    if (_lastPointerHit == null || (_mouseOwner != null && _mouseOwner != this &&
                        _mouseOwner._pressedButtons != 0)) return;
                    _mouseOwner = this;
                    _mouseDownPosition = null;
                    UpdateMouse(_lastPointerHit.Value);
                }
                VR.Input.Mouse.LeftButtonDown();
                _pressedButtons |= Buttons.Left;
                _mouseDownPosition = Vector2.Scale(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y), _scaleVector);
            }
            else
            {
                if ((_pressedButtons & Buttons.Left) == 0) return;
                VR.Input.Mouse.LeftButtonUp();
                _pressedButtons &= ~Buttons.Left;
                _mouseDownPosition = null;
                _mouseReleaseFrame = Time.frameCount + 1;
            }
        }
        internal void OnGrip(bool press)
        {
            if (press && !_quad.IsOwned)
            {
                _quad.transform.SetParent(_controller, worldPositionStays: true);
                _quad.IsOwned = true;
            }
            else
            {
                AbandonGUI();
            }
        }
        internal void OnTouchpad(bool press)
        {
            if (press)
            {
                if (_quad.IsOwned && (_pressedButtons & Buttons.Left) != 0)
                {
                    _quad.transform.SetParent(VR.Manager.transform, worldPositionStays: true);
                    _quad.IsOwned = false;
                }
                else
                {

                    VR.Input.Mouse.MiddleButtonDown();
                    _pressedButtons |= Buttons.Middle;
                }
            }
            else if ((_pressedButtons & Buttons.Middle) != 0)
            {
                VR.Input.Mouse.MiddleButtonUp();
                _pressedButtons &= ~Buttons.Middle;
            }
        }
        private void CheckInput()
        {
            switch (_lastDirection)
            {
                case Controller.TrackpadDirection.Up:
                    if (_quad.IsOwned)
                    {
                        if ((_pressedButtons & Buttons.Left) != 0)
                        {
                            MoveGui(Time.deltaTime);
                        }
                        else
                        {
                            ChangeGuiSize(Time.deltaTime);
                        }
                    }
                    else
                    {
                        Scroll(1);
                    }
                    break;
                case Controller.TrackpadDirection.Down:
                    if (_quad.IsOwned)
                    {
                        if ((_pressedButtons & Buttons.Left) != 0)
                        {
                            MoveGui(-Time.deltaTime);
                        }
                        else
                        {
                            ChangeGuiSize(-Time.deltaTime);
                        }
                    }
                    else
                    {
                        Scroll(-1);
                    }
                    break;
                default:
                    _NextScrollTime = null;
                    break;
            }
        }

        private void ChangeGuiSize(float number)
        {
            _quad.transform.localScale *= 1 + number;
        }

        private void MoveGui(float number)
        {
            _quad.transform.position += number * 0.6f * Laser.transform.forward;
        }

        private void Scroll(int amount)
        {
            if (_NextScrollTime == null)
            {
                _NextScrollTime = Time.unscaledTime + 0.5f;
            }
            else if (_NextScrollTime < Time.unscaledTime)
            {
                _NextScrollTime += 0.1f;
            }
            else
            {
                return;
            }
            VR.Input.Mouse.VerticalScroll(amount);
        }

        private bool CheckForNearMenu()
        {
            _quad = GUIQuadRegistry.Quads.FirstOrDefault(IsLaserable);
            if (_quad != null)
            {
                //VRPlugin.Logger.LogDebug($"CheckForNearMenu:Hit");
                return true;
            }
            //VRPlugin.Logger.LogDebug($"CheckForNearMenu:Miss");
            return false;
        }

        private bool IsLaserable(GUIQuad quad)
        {
            return quad != null && quad.gameObject.activeInHierarchy && Raycast(quad, out _);
        }

        private float GetRange(GUIQuad quad)
        {
            return quad.transform.localScale.z;
        }
        private bool IsWithinRange(GUIQuad quad)
        {
            return (quad.transform.position - Laser.transform.position).magnitude < GetRange(quad);
        }

        private bool Raycast(GUIQuad quad, out RaycastHit hit)
        {
            hit = new RaycastHit();
            if (quad == null || !quad.gameObject.activeInHierarchy) return false;
            var collider = quad.GetComponent<Collider>();
            var renderer = quad.GetComponent<Renderer>();
            if (renderer != null && !renderer.enabled) return false;
            var range = FrameKoiNativeInput.Ready ? KoikSettings.FrameKoiPointerRange.Value : GetRange(quad);
            return collider != null && collider.enabled &&
                collider.Raycast(new Ray(Laser.transform.position, Laser.transform.forward), out hit, range);
        }

        private bool UpdateNativePointer()
        {
            // Modern controllers need no legacy render-model attachment or
            // lighthouse-specific angle. Do not rotate the physical hand here.
            Laser.transform.localPosition = Vector3.zero;
            Laser.transform.localRotation = Quaternion.Euler(KoikSettings.FrameKoiPointerRotation.Value);
            GUIQuad closest = null;
            var bestHit = new RaycastHit();
            var distance = KoikSettings.FrameKoiPointerRange.Value;
            foreach (var quad in GUIQuadRegistry.Quads)
            {
                if (Raycast(quad, out var hit) && hit.distance <= distance)
                {
                    closest = quad;
                    bestHit = hit;
                    distance = hit.distance;
                }
            }
            if (_quad != closest)
            {
                // A new panel must never inherit an old drag/click.
                ReleasePointerButtons();
                _mouseDownPosition = null;
                FrameKoiInputDiagnostics.Log("pointer-target-" + _controller.GetInstanceID(),
                    closest != null ? "native UI pointer acquired active panel" : "native UI pointer has no panel target", 8);
            }
            _quad = closest;
            _lastPointerHit = closest != null ? (RaycastHit?)bestHit : null;
            // Keep the aiming ray visible on a miss so the user can acquire UI.
            LaserVisible = true;
            Laser.SetPosition(0, Laser.transform.position);
            Laser.SetPosition(1, closest != null ? bestHit.point :
                Laser.transform.position + Laser.transform.forward * KoikSettings.FrameKoiPointerRange.Value);
            if (closest == null) return false;
            // Position the cursor on the very first hit, before button dispatch.
            UpdateMouse(bestHit);
            CheckInput();
            return true;
        }

        private void UpdateMouse(RaycastHit hit)
        {
            // Two rays may be visible, but a held UI click owns the OS cursor
            // through its release frame. The other hand must not drag it away.
            if (FrameKoiNativeInput.Ready && _mouseOwner != null && _mouseOwner != this &&
                (_mouseOwner._pressedButtons != 0 || Time.frameCount <= _mouseReleaseFrame)) return;
            _scaleVector = new Vector2((float)VRGUI.Width / Screen.width, (float)VRGUI.Height / Screen.height);
            var newPos = new Vector2(hit.textureCoord.x * VRGUI.Width, (1 - hit.textureCoord.y) * VRGUI.Height);
            if (!_mouseDownPosition.HasValue || Vector2.Distance(_mouseDownPosition.Value, newPos) > MOUSE_STABILIZER_THRESHOLD)
            {
                SetMousePosition(newPos);
                _mouseDownPosition = null;
            }
        }

        private bool UpdateLaser()
        {
            if (_quad && _quad.gameObject.activeInHierarchy
                && Raycast(_quad, out var hit))
            {
                Laser.SetPosition(0, Laser.transform.position);
                Laser.SetPosition(1, hit.point);

                UpdateMouse(hit);
                //VRPlugin.Logger.LogDebug($"UpdateLaser:On");
                //VRPlugin.Logger.LogDebug($"MenuHandler:UpdateLaser:Success");
                return true;
            }
            else
            {
                // May day, may day -- window is gone!

                //VRPlugin.Logger.LogDebug($"UpdateLaser:Off");
                LaserVisible = false;
                ClearPresses();
                return false;
            }
        }

        private void ClearPresses()
        {
            AbandonGUI();
            ReleasePointerButtons();
        }

        private void ReleasePointerButtons()
        {
            if ((_pressedButtons & Buttons.Left) != 0)
            {
                OnTrigger(false);
            }
            if ((_pressedButtons & Buttons.Right) != 0)
            {
                VR.Input.Mouse.RightButtonUp();
            }
            if ((_pressedButtons & Buttons.Middle) != 0)
            {
                VR.Input.Mouse.MiddleButtonUp();
            }
            _pressedButtons = 0;
            _NextScrollTime = null;
        }

        private void AbandonGUI()
        {
            if (_quad && _quad.transform.parent == _controller.transform)
            {
                _quad.transform.SetParent(VR.Camera.Origin, true);
                _quad.IsOwned = false;
            }
        }

        


        private static void SetMousePosition(Vector2 newPos)
        {
            int x = (int)Mathf.Round(newPos.x);
            int y = (int)Mathf.Round(newPos.y);
            var clientRect = WindowManager.GetClientRect();
            var virtualScreenRect = WindowManager.GetVirtualScreenRect();
            if (virtualScreenRect.Right <= virtualScreenRect.Left || virtualScreenRect.Bottom <= virtualScreenRect.Top)
            {
                FrameKoiInputDiagnostics.Log("pointer-desktop", "UI pointer cannot map an empty virtual desktop rectangle");
                return;
            }
            VR.Input.Mouse.MoveMouseToPositionOnVirtualDesktop(
                (clientRect.Left + x - virtualScreenRect.Left) * 65535.0 / (virtualScreenRect.Right - virtualScreenRect.Left),
                (clientRect.Top + y - virtualScreenRect.Top) * 65535.0 / (virtualScreenRect.Bottom - virtualScreenRect.Top));
        }

        //class ResizeHandler : ProtectedBehaviour
        //{
        //    GUIQuad _Gui;
        //    Vector3? _StartLeft;
        //    Vector3? _StartRight;
        //    Vector3? _StartScale;
        //    Quaternion? _StartRotation;
        //    Vector3? _StartPosition;
        //    Quaternion _StartRotationController;
        //    Vector3? _OffsetFromCenter;

        //    public bool IsDragging { get; private set; }
        //    protected override void OnStart()
        //    {
        //        base.OnStart();
        //        _Gui = GetComponent<GUIQuad>();
        //    }

        //    protected override void OnUpdate()
        //    {
        //        base.OnUpdate();
        //        IsDragging = GetDevice(VR.Mode.Left).GetPress(EVRButtonId.k_EButton_Grip) &&
        //               GetDevice(VR.Mode.Right).GetPress(EVRButtonId.k_EButton_Grip);

        //        if (IsDragging)
        //        {
        //            if (_StartScale == null)
        //            {
        //                Initialize();
        //            }
        //            var newLeft = VR.Mode.Left.transform.position;
        //            var newRight = VR.Mode.Right.transform.position;

        //            var distance = Vector3.Distance(newLeft, newRight);
        //            var originalDistance = Vector3.Distance(_StartLeft.Value, _StartRight.Value);
        //            var newDirection = newRight - newLeft;
        //            var newCenter = newLeft + newDirection * 0.5f;

        //            // It would probably be easier than that but Quaternions have never been a strength of mine...
        //            var inverseOriginRot = Quaternion.Inverse(VR.Camera.SteamCam.origin.rotation);
        //            var avgRot = GetAverageRotation();
        //            var rotation = (inverseOriginRot * avgRot) * Quaternion.Inverse(inverseOriginRot * _StartRotationController);

        //            _Gui.transform.localScale = (distance / originalDistance) * _StartScale.Value;
        //            _Gui.transform.localRotation = rotation * _StartRotation.Value;
        //            _Gui.transform.position = newCenter + (avgRot * Quaternion.Inverse(_StartRotationController)) * _OffsetFromCenter.Value;

        //        }
        //        else
        //        {
        //            _StartScale = null;
        //        }
        //    }

        //    private Quaternion GetAverageRotation()
        //    {
        //        var leftPos = VR.Mode.Left.transform.position;
        //        var rightPos = VR.Mode.Right.transform.position;

        //        var right = (rightPos - leftPos).normalized;
        //        var up = Vector3.Lerp(VR.Mode.Left.transform.forward, VR.Mode.Right.transform.forward, 0.5f);
        //        var forward = Vector3.Cross(right, up).normalized;

        //        return Quaternion.LookRotation(forward, up);
        //    }
        //    private void Initialize()
        //    {
        //        _StartLeft = VR.Mode.Left.transform.position;
        //        _StartRight = VR.Mode.Right.transform.position;
        //        _StartScale = _Gui.transform.localScale;
        //        _StartRotation = _Gui.transform.localRotation;
        //        _StartPosition = _Gui.transform.position;
        //        _StartRotationController = GetAverageRotation();

        //        var originalDistance = Vector3.Distance(_StartLeft.Value, _StartRight.Value);
        //        var originalDirection = _StartRight.Value - _StartLeft.Value;
        //        var originalCenter = _StartLeft.Value + originalDirection * 0.5f;
        //        _OffsetFromCenter = transform.position - originalCenter;
        //    }


        //    private SteamVR_Controller.Device GetDevice(Controller controller)
        //    {
        //        return SteamVR_Controller.Input((int)controller.Tracking.index);
        //    }
        //}
    }
}



