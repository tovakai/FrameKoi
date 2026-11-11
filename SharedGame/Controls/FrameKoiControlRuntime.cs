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
        private string _roamingBindings, _sceneBindings, _menuBindings;
        private bool _bindingsLoaded;
        private FrameKoiContext _scratchContext = new FrameKoiContext();
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
            _nextPeriodAt = -1f;
            if (_ownsPause) Time.timeScale = _previousTimeScale;
            _ownsPause = false;
            _ui.Dispose();
            if (FrameKoiInputRuntime.Current == this) FrameKoiInputRuntime.Current = null;
            FrameKoiInputRuntime.RoamingAllowed = false;
        }
        // Sync runs every frame (VR: 90+ Hz) on Unity 5.6's non-generational GC,
        // so scene/focus state is read through typed, allocation-free accessors.
#if KK
        private static bool SceneLoading
        {
            get { var scene = Manager.Scene.Instance; return scene != null && (scene.IsNowLoading || scene.IsNowLoadingFade); }
        }
        private static bool SceneOverlap
        {
            get { var scene = Manager.Scene.Instance; return scene != null && scene.IsOverlap; }
        }
        private static string AddedScene
        {
            get { var scene = Manager.Scene.Instance; return scene == null ? "" : scene.AddSceneName ?? ""; }
        }
        private static string LoadedScene
        {
            get { var scene = Manager.Scene.Instance; return scene == null ? null : scene.LoadSceneName; }
        }
#else
        private static readonly Func<bool> IsNowLoading = StaticSceneGetter<bool>("IsNowLoading");
        private static readonly Func<bool> IsNowLoadingFade = StaticSceneGetter<bool>("IsNowLoadingFade");
        private static readonly Func<bool> IsOverlap = StaticSceneGetter<bool>("IsOverlap");
        private static readonly Func<string> AddSceneName = StaticSceneGetter<string>("AddSceneName");
        private static readonly Func<string> LoadSceneName = StaticSceneGetter<string>("LoadSceneName");
        private static Func<T> StaticSceneGetter<T>(string property)
        {
            var getter = typeof(Manager.Scene).GetProperty(property, BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
            if (getter == null || getter.ReturnType != typeof(T))
            {
                VRLog.Warn("FrameKoi: Manager.Scene." + property + " unavailable; treating it as default.");
                return () => default(T);
            }
            return (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), getter);
        }
        private static bool SceneLoading => IsNowLoading() || IsNowLoadingFade();
        private static bool SceneOverlap => IsOverlap();
        private static string AddedScene => AddSceneName() ?? "";
        private static string LoadedScene => LoadSceneName();
#endif
        private static object _focusSystem;
        private static Func<bool> _focusQuery;
        private static bool _focusQueryInverted;
        private static bool SteamFocusLost()
        {
            var system = OpenVR.System;
            if (system == null) return true;
            if (!ReferenceEquals(system, _focusSystem))
            {
                // Resolve once per OpenVR system instance; the KK and KKS
                // wrappers expose different methods.
                _focusSystem = system;
                _focusQuery = null;
                var method = system.GetType().GetMethod("IsInputAvailable", Type.EmptyTypes);
                _focusQueryInverted = method != null;
                if (method == null) method = system.GetType().GetMethod("IsInputFocusCapturedByAnotherProcess", Type.EmptyTypes);
                if (method != null && method.ReturnType == typeof(bool))
                    _focusQuery = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), system, method);
            }
            if (_focusQuery == null) return false;
            return _focusQueryInverted ? !_focusQuery() : _focusQuery();
        }
        private int _syncFrame = -1;
        // ActionScene.NextCycle ends the period at once; the game itself never
        // exposes it without a "go home?" confirmation. Require a deliberate hold.
        private const float NextPeriodHoldSeconds = 1f;
        private float _nextPeriodAt = -1f;
        private void Update()
        {
            if (!FrameKoiNativeInput.Ready) return;
            Sync();
            if (_nextPeriodAt >= 0f && Time.unscaledTime >= _nextPeriodAt)
            {
                _nextPeriodAt = -1f;
                if (_router.Context != null && _router.Context.Profile == FrameKoiProfile.Roaming && !_router.Context.Blocking)
                    FrameKoiGameActions.RoamingCommand("NextCycle");
            }
        }
        // Both hands and Update share one context evaluation per frame; a
        // button transition forces a re-sync for synchronous scene/UI changes.
        private void Sync(bool force = false)
        {
            if (!force && _syncFrame == Time.frameCount) return;
            _syncFrame = Time.frameCount;
            var input = KoikGameInterp.SceneInput;
            if (_router.Context != null && !ReferenceEquals(_router.Context.Owner, input))
            {
                _ui.SceneMenuVisible = false;
                if (_ownsPause) { Time.timeScale = _previousTimeScale; _ownsPause = false; }
            }
            var type = KoikGameInterp.CurrentScene;
            var loading = SceneLoading;
            var added = AddedScene;
            var dialogue = type == KoikGameInterp.SceneType.TalkScene ||
                added.IndexOf("Talk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                added.IndexOf("ADV", StringComparison.OrdinalIgnoreCase) >= 0;
            var baseScene = added.IndexOf("Talk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                added.IndexOf("ADV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                added.IndexOf("HScene", StringComparison.OrdinalIgnoreCase) >= 0 ||
                added.IndexOf("Action", StringComparison.OrdinalIgnoreCase) >= 0;
            var modal = !baseScene && added.Length != 0 && (SceneOverlap || added != LoadedScene);
            var suspended = loading || SteamFocusLost();
            if (suspended) { _reconnecting[0] = true; _reconnecting[1] = true; }
            var profile = FrameKoiContext.Resolve(modal, dialogue, _ui.SceneMenuVisible,
                type == KoikGameInterp.SceneType.ActionScene, type == KoikGameInterp.SceneType.OtherScene);
            // Reuse a scratch context; only a real change hands it to the router.
            var next = _scratchContext;
            next.Profile = _ownsPause ? FrameKoiProfile.DialogueMenu : profile;
            next.Blocking = modal || _ownsPause; next.Suspended = suspended; next.Owner = input;
            next.Menu = _ownsPause ? "FrameKoiPause" : modal ? added : _ui.SceneMenuVisible ? "SceneControls" : "";
            var contextChanged = !next.Same(_router.Context);
            if (contextChanged)
            {
                _scratchContext = new FrameKoiContext();
                FrameKoiInputDiagnostics.Log("context-" + next.Profile + "-" + next.Menu,
                    $"context={next.Profile}, modal={next.Blocking}, suspended={suspended}, menu={next.Menu}", 12);
                _router.Change(next);
                _reconnecting[0] = true; _reconnecting[1] = true;
            }
            var roamingBindings = KoikSettings.FrameKoiRoamingBindings.Value;
            var sceneBindings = KoikSettings.FrameKoiSceneBindings.Value;
            var menuBindings = KoikSettings.FrameKoiMenuBindings.Value;
            if (!_bindingsLoaded || roamingBindings != _roamingBindings || sceneBindings != _sceneBindings || menuBindings != _menuBindings)
            {
                if (_bindingsLoaded) _router.Reset();
                _bindingsLoaded = true;
                _roamingBindings = roamingBindings; _sceneBindings = sceneBindings; _menuBindings = menuBindings;
                var bindings = FrameKoiBindings.Defaults();
                bindings.Override(FrameKoiProfile.Roaming, roamingBindings);
                bindings.Override(FrameKoiProfile.Scene, sceneBindings);
                bindings.Override(FrameKoiProfile.DialogueMenu, menuBindings);
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
                if (previous != down) Sync(force: true); // synchronous UI/game callbacks
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
            _pointers[hand]?.ReleaseInput();
            (_router?.Context?.Owner as SceneInput)?.CancelPendingActions(hand);
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
            input?.CancelPendingActions(0); input?.CancelPendingActions(1);
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
                case FrameKoiAction.NextPeriod:
                    // Fires from Update after the hold; release or a context change cancels it.
                    _nextPeriodAt = Time.unscaledTime + NextPeriodHoldSeconds;
                    return () => _nextPeriodAt = -1f;
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
