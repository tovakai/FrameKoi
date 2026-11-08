using System;
using UnityEngine;

namespace KK_VR.Controls
{
    internal static class FrameKoiPoseMath
    {
        internal static bool IsFrameController(string controllerType)
        {
            return string.Equals(controllerType, "frame_controller", StringComparison.OrdinalIgnoreCase);
        }

        internal static Quaternion HandRotation(Quaternion tracked, Vector3 calibration)
        {
            // Controller-local correction, before item animation/visual offsets.
            return tracked * Quaternion.Euler(calibration);
        }
    }
}
