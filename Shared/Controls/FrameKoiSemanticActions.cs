using System;
using System.Collections.Generic;

namespace KK_VR.Controls
{
    internal enum FrameKoiProfile { Roaming, Scene, DialogueMenu }
    internal enum FrameKoiAction
    {
        None, Grab, HandAction, SelectUI, Interact, Confirm, Cancel, Pause,
        Sprint, Recenter, Crouch, QuickTravel, NextPeriod, Primary, Secondary,
        Up, Down, Left, Right, ToggleHUD, SceneMenu, RecenterUI
    }

    internal sealed class FrameKoiContext
    {
        internal static FrameKoiProfile Resolve(bool modal, bool dialogue, bool focusedMenu, bool roaming, bool otherMenu)
        {
            if (modal || dialogue || focusedMenu || otherMenu) return FrameKoiProfile.DialogueMenu;
            return roaming ? FrameKoiProfile.Roaming : FrameKoiProfile.Scene;
        }
        internal FrameKoiProfile Profile;
        internal bool Blocking, Suspended;
        internal object Owner;
        internal string Menu;
        internal bool Same(FrameKoiContext other) => other != null && Profile == other.Profile &&
            Blocking == other.Blocking && Suspended == other.Suspended &&
            ReferenceEquals(Owner, other.Owner) && Menu == other.Menu;
    }

    internal interface IFrameKoiActionSink
    {
        // Capture the receiving scene/UI on press; release never consults a new scene.
        Action Begin(FrameKoiAction action, int hand, FrameKoiContext context);
        void Neutralize(FrameKoiContext previous);
    }

    internal sealed class FrameKoiBindings
    {
        private readonly Dictionary<string, FrameKoiAction> _values = new Dictionary<string, FrameKoiAction>();
        private static string Key(FrameKoiProfile profile, string physical) => profile + ":" + physical;
        internal FrameKoiAction Resolve(FrameKoiProfile profile, string physical)
        {
            FrameKoiAction action;
            return _values.TryGetValue(Key(profile, physical), out action) ? action : FrameKoiAction.None;
        }
        internal void Set(FrameKoiProfile profile, string physical, FrameKoiAction action) => _values[Key(profile, physical)] = action;
        internal void Override(FrameKoiProfile profile, string value)
        {
            foreach (var pair in (value ?? "").Split(';'))
            {
                var parts = pair.Split('=');
                if (parts.Length != 2) continue;
                FrameKoiAction action;
                try { action = (FrameKoiAction)Enum.Parse(typeof(FrameKoiAction), parts[1].Trim(), true); }
                catch (ArgumentException) { continue; }
                if (Enum.IsDefined(typeof(FrameKoiAction), action)) Set(profile, parts[0].Trim(), action);
            }
        }
        internal static FrameKoiBindings Defaults()
        {
            var bindings = new FrameKoiBindings();
            foreach (FrameKoiProfile profile in Enum.GetValues(typeof(FrameKoiProfile)))
            {
                foreach (var side in new[] { "left", "right" })
                {
                    bindings.Set(profile, side + "_grip", FrameKoiAction.Grab);
                    bindings.Set(profile, side + "_trigger", FrameKoiAction.HandAction);
                    bindings.Set(profile, side + "_bumper", FrameKoiAction.SelectUI);
                }
                bindings.Set(profile, "b", FrameKoiAction.Cancel);
                bindings.Set(profile, "x", FrameKoiAction.Pause);
                bindings.Set(profile, "right_stick_click", FrameKoiAction.Recenter);
                bindings.Set(profile, "dpad_up", FrameKoiAction.Up);
                bindings.Set(profile, "dpad_down", FrameKoiAction.Down);
                bindings.Set(profile, "dpad_left", FrameKoiAction.Left);
                bindings.Set(profile, "dpad_right", FrameKoiAction.Right);
                bindings.Set(profile, "left_view", FrameKoiAction.SceneMenu);
                bindings.Set(profile, "right_menu", FrameKoiAction.SceneMenu);
            }
            bindings.Set(FrameKoiProfile.Roaming, "a", FrameKoiAction.Interact);
            bindings.Set(FrameKoiProfile.Roaming, "y", FrameKoiAction.Crouch);
            bindings.Set(FrameKoiProfile.Roaming, "left_stick_click", FrameKoiAction.Sprint);
            bindings.Set(FrameKoiProfile.Roaming, "dpad_up", FrameKoiAction.QuickTravel);
            bindings.Set(FrameKoiProfile.Roaming, "dpad_right", FrameKoiAction.NextPeriod);
            bindings.Set(FrameKoiProfile.Roaming, "dpad_left", FrameKoiAction.None);
            bindings.Set(FrameKoiProfile.Roaming, "dpad_down", FrameKoiAction.None);
            bindings.Set(FrameKoiProfile.Roaming, "left_view", FrameKoiAction.ToggleHUD);
            bindings.Set(FrameKoiProfile.Scene, "a", FrameKoiAction.Primary);
            bindings.Set(FrameKoiProfile.Scene, "y", FrameKoiAction.Secondary);
            bindings.Set(FrameKoiProfile.DialogueMenu, "a", FrameKoiAction.Confirm);
            bindings.Set(FrameKoiProfile.DialogueMenu, "right_menu", FrameKoiAction.RecenterUI);
            bindings.Set(FrameKoiProfile.DialogueMenu, "left_view", FrameKoiAction.RecenterUI);
            return bindings;
        }
    }

    internal sealed class FrameKoiContextRouter
    {
        private sealed class Hold { internal int Hand; internal Action Release; }
        private readonly IFrameKoiActionSink _sink;
        private readonly Dictionary<string, Hold> _holds = new Dictionary<string, Hold>();
        private readonly HashSet<string> _physical = new HashSet<string>();
        private readonly HashSet<string> _quarantine = new HashSet<string>();
        internal FrameKoiBindings Bindings;
        internal FrameKoiContext Context { get; private set; }
        internal FrameKoiContextRouter(IFrameKoiActionSink sink, FrameKoiBindings bindings) { _sink = sink; Bindings = bindings; }
        // Every button is sampled every frame; build each "hand:button" key once.
        private readonly Dictionary<string, string>[] _keys = { new Dictionary<string, string>(), new Dictionary<string, string>() };
        private string Key(int hand, string button)
        {
            string key;
            var keys = _keys[hand];
            if (!keys.TryGetValue(button, out key)) keys[button] = key = hand + ":" + button;
            return key;
        }

        internal void Change(FrameKoiContext next)
        {
            if (next.Same(Context)) return;
            foreach (var key in _physical) _quarantine.Add(key);
            var previous = Context;
            Context = next;
            _sink.Neutralize(previous);
            ReleaseAll();
        }
        internal void Sample(int hand, string button, bool down)
        {
            var key = Key(hand, button);
            if (!down)
            {
                _physical.Remove(key); _quarantine.Remove(key);
                Release(key);
                return;
            }
            if (!_physical.Add(key) || _quarantine.Contains(key) || Context == null || Context.Suspended) return;
            var action = Bindings.Resolve(Context.Profile, button);
            if (action == FrameKoiAction.None) return;
            if (Context.Blocking && (action == FrameKoiAction.Grab || action == FrameKoiAction.HandAction)) return;
            var context = Context;
            var hold = new Hold { Hand = hand };
            _holds[key] = hold;
            var release = _sink.Begin(action, hand, context);
            // Begin may synchronously open a menu/change scenes.
            if (ReferenceEquals(Context, context) && _holds.ContainsKey(key)) hold.Release = release;
            else release?.Invoke();
        }
        internal void Disconnect(int hand)
        {
            var keys = new List<string>();
            foreach (var key in _physical)
                if (key.StartsWith(hand + ":", StringComparison.Ordinal)) { _quarantine.Add(key); keys.Add(key); }
            foreach (var key in keys) Release(key);
        }
        internal void Seed(int hand, string button, bool down)
        {
            var key = Key(hand, button);
            if (down) { _physical.Add(key); _quarantine.Add(key); }
            else Sample(hand, button, false);
        }
        private void Release(string key)
        {
            Hold hold;
            if (!_holds.TryGetValue(key, out hold)) return;
            _holds.Remove(key);
            hold.Release?.Invoke();
        }
        private void ReleaseAll()
        {
            foreach (var key in new List<string>(_holds.Keys)) Release(key);
        }
        internal void Reset()
        {
            foreach (var key in _physical) _quarantine.Add(key);
            _sink.Neutralize(Context);
            ReleaseAll();
        }
    }

    internal interface IFrameKoiInputRuntime
    {
        void TickHand(int hand, KoikMenuHandler pointer);
        void Disconnect(int hand);
    }
    internal static class FrameKoiInputRuntime
    {
        internal static IFrameKoiInputRuntime Current;
        internal static bool RoamingAllowed = true;
    }
    internal static class FrameKoiUiPolicy
    {
        internal static bool Interactive = true;
        internal static object FocusRoot;
    }
}
