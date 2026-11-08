using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Valve.VR;
using VRGIN.Core;
using KK_VR.Camera;
using KK_VR.Holders;
using KK_VR.Interpreters;
using KK_VR.Settings;
using static VRGIN.Controls.Controller;

namespace KK_VR.Controls
{
    internal sealed class FrameKoiControlRuntime : MonoBehaviour, IFrameKoiInputRuntime, IFrameKoiActionSink
    {
        private static readonly string[][] Buttons =
        {
            new[] { "left_grip", "left_trigger", "left_bumper", "left_stick_click", "dpad_up", "dpad_down", "dpad_left", "dpad_right", "left_view" },
            new[] { "right_grip", "right_trigger", "right_bumper", "right_stick_click", "a", "b", "x", "y", "right_menu" }
        };
        private FrameKoiContextRouter _router;
        private readonly FrameKoiPresentation _ui = new FrameKoiPresentation();
        private readonly KoikMenuHandler[] _pointers = new KoikMenuHandler[2];
        private readonly Vector2[] _axes = new Vector2[2];
        private readonly bool[] _axisBlocked = new bool[2];
        private readonly Dictionary<SceneInput, int[,]> _leases = new Dictionary<SceneInput, int[,]>();
        private string _bindingText;
        private bool _ownsPause;
        private float _previousTimeScale;
        private Transform _modalRoot;
        private float _uiRepeatAt;
        private float _sceneTurn;
        private bool _snapArmed = true;
        private readonly Dictionary<string, bool> _digitalPrevious = new Dictionary<string, bool>();
        private float _rootRefreshAt;
        private readonly bool[] _reconnecting = { true, true };

        private void OnEnable()
        {
            _router = new FrameKoiContextRouter(this, FrameKoiBindings.Defaults());
            FrameKoiInputRuntime.Current = this;
            FrameKoiUiPolicy.Interactive = false;
        }
        private void OnDisable()
        {
            _router?.Reset();
            if (_ownsPause) Time.timeScale = _previousTimeScale;
            _ownsPause = false;
            _ui.Dispose();
            if (FrameKoiInputRuntime.Current == this) FrameKoiInputRuntime.Current = null;
            FrameKoiInputRuntime.RoamingAllowed = false;
        }
        private static object ReadScene(string property)
        {
#if KK
            var target = Manager.Scene.Instance;
            return target == null ? null : target.GetType().GetProperty(property)?.GetValue(target, null);
#else
            return typeof(Manager.Scene).GetProperty(property, BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
#endif
        }
        private static bool SceneFlag(string property) => ReadScene(property) is bool value && value;
        private static bool SteamFocusLost()
        {
            var system = OpenVR.System;
            if (system == null) return true;
            var method = system.GetType().GetMethod("IsInputAvailable");
            if (method != null) return !(bool)method.Invoke(system, null);
            method = system.GetType().GetMethod("IsInputFocusCapturedByAnotherProcess");
            return method != null && (bool)method.Invoke(system, null);
        }
        private void Update() { if (FrameKoiNativeInput.Ready) Sync(); }
        private void Sync()
        {
            var input = KoikGameInterp.SceneInput;
            if (_router.Context != null && !ReferenceEquals(_router.Context.Owner, input)) _ui.SceneMenuVisible = false;
            var type = KoikGameInterp.CurrentScene;
            var loading = SceneFlag("IsNowLoading") || SceneFlag("IsNowLoadingFade");
            var added = ReadScene("AddSceneName") as string ?? "";
            var dialogue = type == KoikGameInterp.SceneType.TalkScene ||
                added.IndexOf("Talk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                added.IndexOf("ADV", StringComparison.OrdinalIgnoreCase) >= 0;
            var baseScene = added.IndexOf("Talk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                added.IndexOf("ADV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                added.IndexOf("HScene", StringComparison.OrdinalIgnoreCase) >= 0 ||
                added.IndexOf("Action", StringComparison.OrdinalIgnoreCase) >= 0;
            var modal = !baseScene && added.Length != 0 && (SceneFlag("IsOverlap") ||
                added != (ReadScene("LoadSceneName") as string));
            var suspended = loading || SteamFocusLost();
            if (suspended) { _reconnecting[0] = true; _reconnecting[1] = true; }
            var profile = FrameKoiContext.Resolve(modal, dialogue, _ui.SceneMenuVisible,
                type == KoikGameInterp.SceneType.ActionScene, type == KoikGameInterp.SceneType.OtherScene);
            var next = new FrameKoiContext { Profile = profile, Blocking = modal || _ownsPause, Suspended = suspended,
                Owner = input, Menu = _ownsPause ? "FrameKoiPause" : modal ? added : _ui.SceneMenuVisible ? "SceneControls" : "" };
            if (_ownsPause) next.Profile = FrameKoiProfile.DialogueMenu;
            var contextChanged = !next.Same(_router.Context);
            if (contextChanged)
            {
                FrameKoiInputDiagnostics.Log("context-" + next.Profile + "-" + next.Menu,
                    $"context={next.Profile}, modal={next.Blocking}, suspended={suspended}, menu={next.Menu}", 12);
                _router.Change(next);
                _reconnecting[0] = true; _reconnecting[1] = true;
            }
            var bindingText = KoikSettings.FrameKoiRoamingBindings.Value + "|" + KoikSettings.FrameKoiSceneBindings.Value + "|" + KoikSettings.FrameKoiMenuBindings.Value;
            if (bindingText != _bindingText)
            {
                if (_bindingText != null) _router.Reset();
                _bindingText = bindingText;
                var bindings = FrameKoiBindings.Defaults();
                bindings.Override(FrameKoiProfile.Roaming, KoikSettings.FrameKoiRoamingBindings.Value);
                bindings.Override(FrameKoiProfile.Scene, KoikSettings.FrameKoiSceneBindings.Value);
                bindings.Override(FrameKoiProfile.DialogueMenu, KoikSettings.FrameKoiMenuBindings.Value);
                _router.Bindings = bindings;
            }
            var ready = !(KoikGameInterp.SceneInterpreter is TalkSceneInterp talk) || !talk.IsStart;
            _ui.Sync(next.Profile, modal || _ownsPause, dialogue, ready, input, suspended);
            FrameKoiUiPolicy.Interactive = _ui.Interactive && !_ownsPause && !suspended;
            FrameKoiInputRuntime.RoamingAllowed = next.Profile == FrameKoiProfile.Roaming && !next.Blocking && !suspended;
            if (modal && (contextChanged || Time.unscaledTime >= _rootRefreshAt))
            { _modalRoot = FrameKoiGameActions.ModalRoot(); _rootRefreshAt = Time.unscaledTime + 0.2f; }
            else if (!modal) _modalRoot = null;
            FrameKoiUiPolicy.FocusRoot = _modalRoot;
        }
        public void TickHand(int hand, KoikMenuHandler pointer)
        {
            _pointers[hand] = pointer;
            Sync();
            if (!FrameKoiNativeInput.Poll()) { _router.Reset(); _reconnecting[0] = true; _reconnecting[1] = true; return; }
            if (_router.Context.Suspended) return;
            pointer.CheckMenu();
            var reconnecting = _reconnecting[hand];
            foreach (var button in Buttons[hand])
            {
                var down = FrameKoiNativeInput.Digital(button);
                bool previous;
                _digitalPrevious.TryGetValue(button, out previous);
                _digitalPrevious[button] = down;
                if (_reconnecting[hand]) _router.Seed(hand, button, down);
                else _router.Sample(hand, button, down);
                if (previous != down) Sync(); // synchronous UI/game callbacks
            }
            _axes[hand] = FrameKoiNativeInput.Analog(hand == 0 ? "left_stick" : "right_stick");
            if (reconnecting || _reconnecting[hand]) _axisBlocked[hand] = _axes[hand].sqrMagnitude > 0.04f;
            _reconnecting[hand] = false;
            if (_axes[hand].sqrMagnitude <= 0.04f) _axisBlocked[hand] = false;
            var axis = _axisBlocked[hand] || _router.Context.Suspended || _router.Context.Blocking && hand == 1 ? Vector2.zero : _axes[hand];
            var input = KoikGameInterp.SceneInput;
            if (hand == 0)
            {
                input?.SetFrameKoiLeftStick(_router.Context.Profile == FrameKoiProfile.Roaming ? axis : Vector2.zero);
                if (_router.Context.Profile == FrameKoiProfile.Scene) input?.SetSceneMovement(axis);
                if (_router.Context.Profile == FrameKoiProfile.DialogueMenu && (!_router.Context.Blocking || _modalRoot != null) && Time.unscaledTime >= _uiRepeatAt && axis.sqrMagnitude > 0.3f)
                {
                    FrameKoiGameActions.Navigate(_modalRoot, Mathf.Abs(axis.x) > Mathf.Abs(axis.y) ? (int)Mathf.Sign(axis.x) : 0,
                        Mathf.Abs(axis.y) >= Mathf.Abs(axis.x) ? (int)Mathf.Sign(axis.y) : 0);
                    _uiRepeatAt = Time.unscaledTime + 0.3f;
                }
            }
            else if (_router.Context.Profile == FrameKoiProfile.Roaming) input?.SetFrameKoiRightStick(axis.x);
            else _sceneTurn = axis.x;
        }
        public void Disconnect(int hand)
        {
            _router?.Disconnect(hand);
            _reconnecting[hand] = true;
            _axisBlocked[hand] = true;
            _axes[hand] = Vector2.zero;
            if (hand == 0) KoikGameInterp.SceneInput?.SetFrameKoiLeftStick(Vector2.zero);
            else { KoikGameInterp.SceneInput?.SetFrameKoiRightStick(0f); _sceneTurn = 0; }
        }
        public void Neutralize(FrameKoiContext previous)
        {
            var input = previous?.Owner as SceneInput;
            input?.SetFrameKoiLeftStick(Vector2.zero);
            input?.SetFrameKoiRightStick(0f);
            input?.SetSceneMovement(Vector2.zero);
            input?.ReleaseController(0); input?.ReleaseController(1);
            foreach (var pointer in _pointers) pointer?.ReleaseInput();
            for (int i = 0; i < 2; i++) _axisBlocked[i] = _axes[i].sqrMagnitude > 0.04f;
            _sceneTurn = 0; _snapArmed = true;
        }
        private Action Button(SceneInput input, int hand, EVRButtonId button)
        {
            if (input == null || HandHolder.GetHands.Count < 2) return null;
            int[,] counts;
            if (!_leases.TryGetValue(input, out counts)) _leases[input] = counts = new int[2, 3];
            int slot = button == EVRButtonId.k_EButton_Grip ? 0 : button == EVRButtonId.k_EButton_SteamVR_Trigger ? 1 : 2;
            if (++counts[hand, slot] == 1) input.OnButtonDown(hand, button, TrackpadDirection.Center);
            return () =>
            {
                if (--counts[hand, slot] == 0) input.OnButtonUp(hand, button, TrackpadDirection.Center);
                bool any = false;
                foreach (var count in counts) if (count != 0) any = true;
                if (!any) _leases.Remove(input);
            };
        }
        public Action Begin(FrameKoiAction action, int hand, FrameKoiContext context)
        {
            var input = context.Owner as SceneInput;
            FrameKoiInputDiagnostics.Log("semantic-" + action, $"semantic {action}, hand={hand}, profile={context.Profile}", 10);
            switch (action)
            {
                case FrameKoiAction.Grab:
                    if (context.Profile == FrameKoiProfile.DialogueMenu && input is ActionSceneInput) break;
                    return Button(input, hand, EVRButtonId.k_EButton_Grip);
                case FrameKoiAction.HandAction:
                    if (context.Profile == FrameKoiProfile.DialogueMenu && input is ActionSceneInput) break;
                    return Button(input, hand, EVRButtonId.k_EButton_SteamVR_Trigger);
                case FrameKoiAction.SelectUI:
                    var pointer = _pointers[hand];
                    if (FrameKoiUiPolicy.Interactive && pointer != null && pointer.CheckMenu())
                    { pointer.OnTrigger(true); return () => pointer.OnTrigger(false); }
                    break;
                case FrameKoiAction.Interact: return FrameKoiGameActions.Interact();
                case FrameKoiAction.Confirm:
                    if (context.Menu != "FrameKoiPause" && (!context.Blocking || _modalRoot != null)) FrameKoiGameActions.Submit(_modalRoot);
                    break;
                case FrameKoiAction.Primary: return Button(input, hand, EVRButtonId.k_EButton_SteamVR_Trigger);
                case FrameKoiAction.Secondary: return Button(input, hand, EVRButtonId.k_EButton_SteamVR_Touchpad);
                case FrameKoiAction.Crouch:
                    if (input is ActionSceneInput roaming) roaming.ToggleButtonCrouch();
                    break;
                case FrameKoiAction.Sprint: input?.FrameKoiSprintClick(); break;
                case FrameKoiAction.QuickTravel: FrameKoiGameActions.RoamingCommand("OpenMapMoveAllMenu"); break;
                case FrameKoiAction.NextPeriod: FrameKoiGameActions.RoamingCommand("NextCycle"); break;
                case FrameKoiAction.ToggleHUD: _ui.HudVisible = !_ui.HudVisible; _ui.Recenter(); break;
                case FrameKoiAction.SceneMenu:
                    if (context.Profile == FrameKoiProfile.Roaming) FrameKoiGameActions.RoamingCommand("OpenStatus");
                    else _ui.SceneMenuVisible = !_ui.SceneMenuVisible;
                    break;
                case FrameKoiAction.Recenter:
                    SmoothMover.Instance?.MakeUpright();
                    if (_ui.Interactive) _ui.Recenter();
                    break;
                case FrameKoiAction.RecenterUI: _ui.Recenter(); break;
                case FrameKoiAction.Cancel:
                    if (_ownsPause) TogglePause();
                    else if (_ui.SceneMenuVisible) _ui.SceneMenuVisible = false;
                    else if (!FrameKoiGameActions.Cancel(_modalRoot) && context.Profile == FrameKoiProfile.Scene) _router.Reset();
                    break;
                case FrameKoiAction.Pause: TogglePause(); break;
                case FrameKoiAction.Up:
                case FrameKoiAction.Down:
                case FrameKoiAction.Left:
                case FrameKoiAction.Right:
                    var direction = action == FrameKoiAction.Up ? TrackpadDirection.Up : action == FrameKoiAction.Down ? TrackpadDirection.Down :
                        action == FrameKoiAction.Left ? TrackpadDirection.Left : TrackpadDirection.Right;
                    if (context.Profile == FrameKoiProfile.DialogueMenu)
                    {
                        if (!context.Blocking || _modalRoot != null) FrameKoiGameActions.Navigate(_modalRoot, direction == TrackpadDirection.Left ? -1 : direction == TrackpadDirection.Right ? 1 : 0,
                            direction == TrackpadDirection.Up ? 1 : direction == TrackpadDirection.Down ? -1 : 0);
                    }
                    else
                    {
                        input?.OnDirectionDown(hand, direction);
                        return () => input?.OnDirectionUp(hand, direction);
                    }
                    break;
            }
            return null;
        }
        private void TogglePause()
        {
            if (_ownsPause) { Time.timeScale = _previousTimeScale; _ownsPause = false; }
            else if (_router.Context.Blocking &&
                (_router.Context.Menu.IndexOf("Config", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 _router.Context.Menu.IndexOf("Exit", StringComparison.OrdinalIgnoreCase) >= 0))
                FrameKoiGameActions.Cancel(_modalRoot);
            else if (!_router.Context.Blocking && !_router.Context.Suspended && Time.timeScale > 0f)
            {
                if (FrameKoiGameActions.PauseMenu()) return;
                _previousTimeScale = Time.timeScale;
                Time.timeScale = 0f; _ownsPause = true;
                FrameKoiInputDiagnostics.Log("pause-fallback", "using owned Unity timescale pause; no game pause-menu control was exposed");
            }
            else FrameKoiInputDiagnostics.Log("pause-blocked", "pause belongs to a game modal/loading state; leaving it unchanged");
        }
        internal void ApplySceneTurn()
        {
            if (_router?.Context == null || _router.Context.Profile == FrameKoiProfile.Roaming || _router.Context.Suspended || _router.Context.Blocking) return;
            var axis = _sceneTurn;
            if (Mathf.Abs(axis) <= KoikSettings.FrameKoiStickDeadzone.Value) { _snapArmed = true; return; }
            float degrees = 0;
            if (KoikSettings.FrameKoiTurnMode.Value == KoikSettings.FrameKoiTurningMode.Smooth)
                degrees = Mathf.Sign(axis) * Mathf.Clamp01((Mathf.Abs(axis) - KoikSettings.FrameKoiStickDeadzone.Value) /
                    (1f - KoikSettings.FrameKoiStickDeadzone.Value)) * KoikSettings.FrameKoiSmoothTurnSpeed.Value * Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            else if (KoikSettings.FrameKoiTurnMode.Value == KoikSettings.FrameKoiTurningMode.Snap && _snapArmed && Mathf.Abs(axis) >= 0.75f)
            { degrees = Mathf.Sign(axis) * GameSettings.RotationAngle.Value; _snapArmed = false; }
            var head = VR.Camera.Head; var position = head.position;
            VR.Camera.Origin.rotation = Quaternion.Euler(0, degrees, 0) * VR.Camera.Origin.rotation;
            VR.Camera.Origin.position += position - head.position;
        }
    }
}
