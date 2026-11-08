using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRGIN.Controls;
using UnityEngine;
using static Illusion.Utils;
using VRGIN.Visuals;
using VRGIN.Core;
using KK_VR.Interpreters;
using KK_VR.Settings;

namespace KK_VR.Controls
{
    internal class KoikMenuTool
    {
        internal bool IsAttached => _attached;
        private bool _attached;
        private static GUIQuad _gui;
        internal KoikMenuTool(Controller controller)
        {
            if (FrameKoiNativeInput.Ready) return;
            if (!_gui 
                && ((KoikSettings.MainHand.Value == KoikSettings.Handedness.Left && controller == VR.Mode.Left)
                || (KoikSettings.MainHand.Value == KoikSettings.Handedness.Right && controller == VR.Mode.Right)))
            {
                _gui = GUIQuad.Create();
                _gui.transform.parent = controller.transform;
                _gui.transform.localScale = Vector3.one * 0.3f;
                _gui.transform.localPosition = new Vector3(0, 0.05f, -0.06f);
                _gui.transform.localRotation = Quaternion.Euler(90, 0, 0);
                _gui.IsOwned = true;
                _gui.gameObject.SetActive(true);
                _attached = true;
            }
        }
        internal static void ToggleState()
        {
            if (_gui != null) _gui.gameObject.SetActive(!_gui.gameObject.activeSelf);
        }
        internal static void SuppressLegacyPanel()
        {
            if (_gui != null) _gui.gameObject.SetActive(false);
        }
        //internal void TakeGUI(GUIQuad quad)
        //{
        //    if (quad && !Gui && !quad.IsOwned)
        //    {
        //        Gui = quad;
        //        //Gui.transform.parent = transform;
        //        Gui.transform.SetParent(transform, worldPositionStays: true);

        //        quad.IsOwned = true;
        //    }
        //    VRLog.Debug($"TakeGui:{Gui}:{quad.IsOwned}");
        //}

        internal static void TakeGui()
        {
            if (_gui != null && !_gui.transform.parent.name.Contains("Controller") && _gui.transform.parent != VR.Camera.Origin)
            {
                var head = VR.Camera.Head;
                var origin = VR.Camera.Origin;

                _gui.transform.SetParent(origin, worldPositionStays: true);

                // If no menu in proximity after the scene load. (was abandoned beforehand)
                if (Vector3.Distance(_gui.transform.position, head.position) > 3f)
                {
                    _gui.transform.SetPositionAndRotation(
                        head.position + (origin.rotation * Quaternion.Euler(0f, 60f, 0f)) * head.forward,
                        head.rotation * Quaternion.Euler(0f, 90f, 0f)
                        );
                }
            }
        }

        internal void AbandonGUI()
        {
            if (_attached && _gui != null)
            {
                //timeAbandoned = Time.unscaledTime;
                _gui.IsOwned = false;
                _gui.transform.SetParent(VR.Camera.Origin, true);
                _attached = false;
            }
        }
    }
}

