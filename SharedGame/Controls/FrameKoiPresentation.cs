using UnityEngine;
using VRGIN.Core;
using VRGIN.Visuals;
using KK_VR.Settings;

namespace KK_VR.Controls
{
    internal enum FrameKoiPanelMode { Hidden, RoamingHUD, Dialogue, SceneControls }

    internal sealed class FrameKoiPresentation
    {
        private GUIQuad _panel;
        private FrameKoiPanelMode _mode;
        private object _owner;
        private bool _ready;
        internal bool HudVisible, SceneMenuVisible;
        internal FrameKoiPanelMode Mode => _mode;
        internal bool Interactive => _mode == FrameKoiPanelMode.Dialogue || _mode == FrameKoiPanelMode.SceneControls;

        internal void Sync(FrameKoiProfile profile, bool modal, bool dialogue, bool ready, object owner, bool suspended)
        {
            var mode = suspended ? FrameKoiPanelMode.Hidden :
                modal || dialogue || profile == FrameKoiProfile.DialogueMenu && !SceneMenuVisible ? FrameKoiPanelMode.Dialogue :
                SceneMenuVisible ? FrameKoiPanelMode.SceneControls :
                profile == FrameKoiProfile.Roaming && HudVisible ? FrameKoiPanelMode.RoamingHUD : FrameKoiPanelMode.Hidden;
            if (_panel == null && mode != FrameKoiPanelMode.Hidden)
            {
                _panel = GUIQuad.Create();
                _panel.name = "FrameKoi original game UI";
                Object.DontDestroyOnLoad(_panel.gameObject);
            }
            KoikMenuTool.SuppressLegacyPanel();
            var changed = mode != _mode || !ReferenceEquals(owner, _owner);
            var settled = !_ready && ready;
            _owner = owner; _ready = ready;
            if (changed)
            {
                _mode = mode;
                FrameKoiInputDiagnostics.Log("presentation-" + mode, "UI presentation: " + mode, 12);
            }
            if (_panel == null) return;
            _panel.gameObject.SetActive(mode != FrameKoiPanelMode.Hidden);
            var collider = _panel.GetComponent<Collider>();
            if (collider != null) collider.enabled = Interactive;
            FrameKoiUiPolicy.Interactive = Interactive;
            if (changed || settled && mode == FrameKoiPanelMode.Dialogue) Recenter();
        }

        internal void Recenter()
        {
            if (_panel == null || _mode == FrameKoiPanelMode.Hidden) return;
            var head = VR.Camera.Head;
            if (_mode == FrameKoiPanelMode.RoamingHUD)
            {
                _panel.transform.SetParent(head, false);
                const float distance = 1.25f;
                var width = 2f * distance * Mathf.Tan(KoikSettings.FrameKoiHudAngle.Value * Mathf.Deg2Rad * 0.5f);
                _panel.transform.localPosition = Vector3.forward * distance;
                _panel.transform.localRotation = Quaternion.identity;
                var height = width * Screen.height / Screen.width;
                _panel.transform.localScale = new Vector3(width, height, 1f);
            }
            else if (_mode == FrameKoiPanelMode.SceneControls && VR.Mode.Right != null)
            {
                _panel.transform.SetParent(VR.Mode.Right.transform, false);
                _panel.transform.localPosition = new Vector3(0f, 0.1f, 0.15f);
                _panel.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
                // Legacy wrist-menu size: read at close range, so text stays sharp.
                const float height = 0.3f;
                _panel.transform.localScale = new Vector3(height * Screen.width / Screen.height, height, 1f);
            }
            else
            {
                // Sample headset pose once. Subsequent head/virtual turns do not
                // move the dialogue panel; Menu/recenter can place it again.
                _panel.transform.SetParent(null, true);
                var configured = KoikSettings.FrameKoiDialogueDistance.Value;
                var distance = DialogueDistance(configured, CharacterAhead?.Invoke(head, configured + 0.5f));
                var scale = configured > 0f ? distance / configured : 1f;
                var yaw = Quaternion.Euler(0f, head.eulerAngles.y + KoikSettings.FrameKoiDialogueSideAngle.Value, 0f);
                var position = head.position + yaw * Vector3.forward * distance +
                    Vector3.up * KoikSettings.FrameKoiDialogueVertical.Value * scale;
                _panel.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
                // Sized by angle so the UI texture is never heavily minified:
                // it has no mipmaps, and squeezing its rows into fewer headset
                // pixels turns text into unreadable aliasing at any distance.
                var height = PanelHeight(distance, KoikSettings.FrameKoiPanelAngle.Value);
                _panel.transform.localScale = new Vector3(height * Screen.width / Screen.height, height, 1f);
            }
            // GUIQuad.Start can update aspect, but preserves the requested height.
            _panel.UpdateAspect();
        }
        // Horizontal distance from the head to the nearest character in front,
        // within the given range; null when none. Supplied by the game runtime.
        internal static System.Func<Transform, float, float?> CharacterAhead;
        internal const float CharacterClearance = 0.25f;
        internal const float MinDialogueDistance = 0.45f;

        // Talk cameras sit well under a metre from the partner, so a fixed
        // distance put the panel behind her. Stay just in front of whoever is
        // ahead, never farther than configured.
        internal static float PanelHeight(float distance, float degrees) =>
            2f * distance * (float)System.Math.Tan(degrees * System.Math.PI / 360.0);

        internal static float DialogueDistance(float configured, float? characterAhead)
        {
            if (!characterAhead.HasValue) return configured;
            return System.Math.Min(configured, System.Math.Max(MinDialogueDistance, characterAhead.Value - CharacterClearance));
        }

        internal void Dispose()
        {
            FrameKoiUiPolicy.Interactive = false;
            if (_panel != null) Object.Destroy(_panel.gameObject);
            _panel = null;
        }
    }
}
