using System;
using UnityEngine;

namespace KK_VR.Controls
{
    internal static class FrameKoiStickMath
    {
        internal static Vector2 Deadzone(Vector2 axis, float deadzone)
        {
            if (float.IsNaN(axis.x) || float.IsNaN(axis.y) ||
                float.IsInfinity(axis.x) || float.IsInfinity(axis.y)) return Vector2.zero;
            deadzone = Mathf.Clamp(deadzone, 0f, 0.95f);
            var length = Mathf.Min(axis.magnitude, 1f);
            return length <= deadzone ? Vector2.zero :
                axis.normalized * ((length - deadzone) / (1f - deadzone));
        }
    }
}
