using UnityEngine;
using VRGIN.Core;
using KK_VR.Settings;

namespace KK_VR.Features
{
    /// <summary>
    /// Applies the FrameKoi VR anti-aliasing setting to QualitySettings, which
    /// Unity's native VR eye textures follow. The game calls SetQualityLevel at
    /// startup and from its graphics options, which resets antiAliasing, so the
    /// override is re-asserted whenever it changes. GameDefault restores the
    /// value the game itself last chose.
    /// </summary>
    internal sealed class FrameKoiAntiAliasing : MonoBehaviour
    {
        private int _gameValue = -1;
        private int _applied = -1;
        private bool _reported;

        internal static int Samples(KoikSettings.FrameKoiMsaaMode mode)
        {
            switch (mode)
            {
                case KoikSettings.FrameKoiMsaaMode.Off: return 0;
                case KoikSettings.FrameKoiMsaaMode.X2: return 2;
                case KoikSettings.FrameKoiMsaaMode.X4: return 4;
                case KoikSettings.FrameKoiMsaaMode.X8: return 8;
                default: return -1;
            }
        }

        private void Update()
        {
            var current = QualitySettings.antiAliasing;
            // Anything we did not set ourselves is the game's own choice.
            if (_applied < 0 || current != _applied)
            {
                _gameValue = current;
                _applied = -1;
            }
            if (!_reported) Report();

            var wanted = Samples(KoikSettings.FrameKoiMsaa.Value);
            if (wanted < 0)
            {
                if (_applied >= 0)
                {
                    QualitySettings.antiAliasing = _gameValue;
                    _applied = -1;
                    VRLog.Info("FrameKoi anti-aliasing: restored game value " + _gameValue + "x");
                }
                return;
            }
            if (current != wanted)
            {
                QualitySettings.antiAliasing = wanted;
                // Unity can clamp the request; remember what actually stuck.
                _applied = QualitySettings.antiAliasing;
                VRLog.Info("FrameKoi anti-aliasing: " + _applied + "x (game value " + _gameValue + "x)");
            }
            else _applied = current;
        }

        private void Report()
        {
            var camera = GetComponent<UnityEngine.Camera>();
            if (camera == null) return;
            _reported = true;
            // MSAA only affects forward rendering; deferred silently ignores it.
            VRLog.Info("FrameKoi anti-aliasing: game quality value " + _gameValue + "x, headset camera path " +
                camera.actualRenderingPath + (camera.actualRenderingPath == RenderingPath.DeferredShading ||
                camera.actualRenderingPath == RenderingPath.DeferredLighting ? " (MSAA will have no effect)" : ""));
        }
    }
}
