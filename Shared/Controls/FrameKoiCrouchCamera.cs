using UnityEngine;

namespace KK_VR.Controls
{
    // Own only the virtual button-crouch offset, never physical headset height.
    internal sealed class FrameKoiCrouchCamera
    {
        internal float Offset { get; private set; }

        internal float Step(bool crouching, float height, float deltaTime)
        {
            var target = crouching ? -Mathf.Clamp(height, 0f, 1.5f) : 0f;
            var next = Mathf.MoveTowards(Offset, target, 4f * Mathf.Clamp(deltaTime, 0f, 0.05f));
            var delta = next - Offset;
            Offset = next;
            return delta;
        }

        internal float Reset()
        {
            var delta = -Offset;
            Offset = 0f;
            return delta;
        }
    }
}
