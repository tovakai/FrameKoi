using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using KK_VR.Interpreters;
using KK_VR.Settings;

namespace KK_VR.Controls
{
    internal static class FrameKoiGameActions
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal static bool Invoke(object target, string name)
        {
            if (target == null) return false;
            var method = target.GetType().GetMethod(name, Flags, null, Type.EmptyTypes, null);
            if (method == null)
            { FrameKoiInputDiagnostics.Log("api-" + name, "game API unavailable: " + name); return false; }
            try { method.Invoke(target, null); return true; }
            catch (TargetInvocationException ex)
            {
                FrameKoiInputDiagnostics.Log("api-" + name, name + " unavailable: " + ex.InnerException?.Message);
                return false;
            }
        }
        internal static bool RoamingCommand(string name) => Invoke(ActionSceneInterp.actionScene, name);

        internal static Action Interact()
        {
            var scene = ActionSceneInterp.actionScene;
            if (scene == null || scene.Player == null) return null;
            // Deliver the game's own action input (right mouse) rather than starting
            // ActionScene._SceneEvent directly. The game's handler applies guards the
            // coroutine itself does not: time period, ADV/overlay/regulation state,
            // Player.isActionNow, the NPC's own isAction, and gate/action-point
            // precedence. Bypassing them can force-start an event the game refuses.
            var input = KoikGameInterp.SceneInput;
            if (input != null && KoikSettings.FrameKoiCompatibilityInput.Value &&
                (scene.Player.isGateHit || scene.Player.actionTarget != null || scene.Player.isActionPointHit))
            {
                input.OnDirectionDown(0, VRGIN.Controls.Controller.TrackpadDirection.Up);
                return () => input.OnDirectionUp(0, VRGIN.Controls.Controller.TrackpadDirection.Up);
            }
            return null;
        }
        internal static bool AdvanceDialogue()
        {
            var adv = TalkSceneInterp.advScene;
            if (adv != null && adv.isActiveAndEnabled && !adv.scenario.isChoice)
                return Invoke(adv.scenario, "RequestNextLine");
            return false;
        }
        internal static bool Submit(Transform root)
        {
            if (root == null && KoikGameInterp.CurrentScene == KoikGameInterp.SceneType.TalkScene)
                return KoikGameInterp.SceneInput.ConfirmUI();
            if (KoikMenuHandler.SubmitSelectedUI(root)) return true;
            return false;
        }
        internal static bool PauseMenu()
        {
            var scene = ActionSceneInterp.actionScene;
            if (scene == null) return false;
            var menu = scene.GetType().GetField("actionMenu", Flags)?.GetValue(scene);
            if (menu == null) return false;
            foreach (var field in menu.GetType().GetFields(Flags))
            {
                if (!typeof(Button).IsAssignableFrom(field.FieldType) ||
                    field.Name.IndexOf("config", StringComparison.OrdinalIgnoreCase) < 0 &&
                    field.Name.IndexOf("pause", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var button = field.GetValue(menu) as Button;
                if (button != null && button.IsActive() && button.IsInteractable())
                { button.onClick.Invoke(); return true; }
            }
            return false;
        }
        internal static void Navigate(Transform root, int x, int y)
        {
            if (root == null && KoikGameInterp.CurrentScene == KoikGameInterp.SceneType.TalkScene &&
                KoikGameInterp.SceneInput.NavigateUI(x, y)) return;
            var system = EventSystem.current;
            var current = system?.currentSelectedGameObject?.GetComponent<Selectable>();
            if (current != null && root != null && !current.transform.IsChildOf(root)) current = null;
            var next = current == null ? null : y > 0 ? current.FindSelectableOnUp() :
                y < 0 ? current.FindSelectableOnDown() : x > 0 ? current.FindSelectableOnRight() : current.FindSelectableOnLeft();
            if (next != null && root != null && !next.transform.IsChildOf(root)) next = null;
            if (next == null && root != null)
                next = root.GetComponentsInChildren<Selectable>().FirstOrDefault(s => s.IsActive() && s.IsInteractable());
            if (next != null && next.IsActive() && next.IsInteractable()) next.Select();
        }
        internal static bool Cancel(Transform root)
        {
            var system = EventSystem.current;
            var selected = system?.currentSelectedGameObject;
            if (root == null)
                return KoikGameInterp.CurrentScene == KoikGameInterp.SceneType.TalkScene && KoikGameInterp.SceneInput.CancelUI();
            var config = root.GetComponentsInParent<MonoBehaviour>().FirstOrDefault(b => b.GetType().Name == "ConfigScene");
            if (config != null && Invoke(config, "Unload")) return true;
            if (selected != null && selected.transform.IsChildOf(root) &&
                ExecuteEvents.ExecuteHierarchy(selected, new BaseEventData(system), ExecuteEvents.cancelHandler) != null) return true;
            // Invoke the game's own visible back/cancel control, never arbitrary
            // scene unload or an affirmative response to a confirmation modal.
            var button = root.GetComponentsInChildren<Button>().FirstOrDefault(b => b.IsActive() && b.IsInteractable() &&
                new[] { "cancel", "back", "return", "close", "no" }.Contains(
                    b.name.ToLowerInvariant().Replace("button", "").Replace("btn", "").Replace("_", "").Replace("-", "").Replace(" ", "")));
            if (button == null) return false;
            button.onClick.Invoke();
            return true;
        }
        internal static Transform ModalRoot()
        {
            var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>()
                .Where(c => c.isActiveAndEnabled && c.gameObject.activeInHierarchy &&
                    !c.name.StartsWith("FrameKoi", StringComparison.Ordinal));
            // Scene stack detection decides whether this is a modal. Canvas
            // sorting selects only that foreground UI, not an arbitrary NPC.
            return canvases.OrderByDescending(c => c.sortingOrder).FirstOrDefault()?.transform;
        }
    }
}
