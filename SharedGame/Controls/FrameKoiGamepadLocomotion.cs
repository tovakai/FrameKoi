using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KK_VR.Controls;
using KK_VR.Holders;
using KK_VR.Interpreters;
using KK_VR.Settings;
using UnityEngine;
using VRGIN.Core;

namespace KK_VR.Controls
{
    internal static class FrameKoiGamepadLocomotion
    {
        internal static bool Available;
        internal static FieldInfo MoveRequestField;
        [ThreadStatic] internal static bool InPlayerUpdate;
        private static ChaControl _followTarget;
        private static Vector3 _lastPlayerPosition;
        private static int _followFrame = -1;
        internal static ActionSceneInput Input => KoikGameInterp.SceneInput as ActionSceneInput;
        internal static bool Enabled => Available && FrameKoiNativeInput.Ready &&
            FrameKoiInputRuntime.RoamingAllowed &&
            KoikSettings.FrameKoiGameLocomotion.Value &&
            KoikGameInterp.CurrentScene == KoikGameInterp.SceneType.ActionScene && Input != null;

        // Called only after the game's cursor/event/regulation gates have
        // resolved mouseMoving. A vertical head pose must not erase a valid
        // move request merely because the old camera-forward vector is vertical.
        internal static Vector3 Direction(ref Vector3 original, object moverState)
        {
            if (!InPlayerUpdate || !Enabled) return original.normalized;
            var player = ActionSceneInterp.actionScene?.Player;
            if (!(bool)MoveRequestField.GetValue(moverState) || player?.agent == null ||
                !player.agent.enabled || !player.agent.isOnNavMesh || Time.timeScale == 0f)
                return Vector3.zero;
            var axis = Input.MoveAxis;
            var forward = VR.Camera.Head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = VR.Camera.Origin.forward;
                forward.y = 0f;
            }
            forward.Normalize();
            return (Vector3.Cross(Vector3.up, forward) * axis.x + forward * axis.y) *
                KoikSettings.FrameKoiGameMoveScale.Value;
        }

        internal static void FollowPlayer()
        {
            if (!Enabled)
            {
                _followTarget = null;
                return;
            }
            var player = ActionSceneInterp.actionScene?.Player;
            if (player?.chaCtrl == null) return;
            if (_followTarget != player.chaCtrl)
            {
                _followTarget = player.chaCtrl;
                _lastPlayerPosition = player.position;
                _followFrame = Time.frameCount;
                return;
            }
            if (_followFrame == Time.frameCount) return;
            _followFrame = Time.frameCount;
            // Follow only displacement already accepted by the game, retaining
            // the headset's physical room-scale offset. Never write player.position.
            var delta = player.position - _lastPlayerPosition;
            _lastPlayerPosition = player.position;
            if (delta.sqrMagnitude < 4f)
            {
                VR.Camera.Origin.position += delta;
                HandHolder.ShiftWithOrigin(delta);
            }
            // Large scene/door warps use the existing ResetCamera/reload path.
        }

        internal static void RebaseFollow()
        {
            _followTarget = null;
            FollowPlayer();
        }
    }

    [HarmonyPatch]
    internal static class FrameKoiPlayerMoverPatch
    {
        private static bool Prepare() => AccessTools.Method(AccessTools.TypeByName("ActionGame.Chara.Mover.Main"), "Update") != null;
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var method = AccessTools.Method(AccessTools.TypeByName("ActionGame.Chara.Mover.Main"), "Update");
            if (method != null) yield return method;
            else VRLog.Warn("FrameKoi game mover unavailable; retaining legacy trigger walking.");
        }
        private static void Prefix(ref bool __state)
        {
            __state = FrameKoiGamepadLocomotion.InPlayerUpdate;
            FrameKoiGamepadLocomotion.InPlayerUpdate = FrameKoiGamepadLocomotion.Enabled;
        }
        private static Exception Finalizer(Exception __exception, bool __state)
        {
            FrameKoiGamepadLocomotion.InPlayerUpdate = __state;
            return __exception;
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FrameKoiGamepadLocomotion.Available = false;
            var code = instructions.ToList();
            var normalize = AccessTools.PropertyGetter(typeof(Vector3), "normalized");
            var inputType = AccessTools.TypeByName("ActionGame.ActionInput");
            var playerMove = AccessTools.Method(inputType, "PlayerMove");
            var move = AccessTools.Method(typeof(UnityEngine.AI.NavMeshAgent), "Move");
            var request = AccessTools.Field(AccessTools.TypeByName("ActionGame.Chara.Mover.Main"), "mouseMoving");
            // Fail closed if another game version/mod changed the known pipeline.
            // The original mover remains intact, with legacy controls available.
            if (code.Count(i => i.Calls(normalize)) != 1 || code.Count(i => i.Calls(playerMove)) != 1 ||
                code.Count(i => i.Calls(move)) != 1 ||
                request == null || request.FieldType != typeof(bool) ||
                AccessTools.PropertyGetter(inputType, "isWalk") == null ||
                AccessTools.PropertyGetter(inputType, "isCrouch") == null)
            {
                VRLog.Warn("FrameKoi game mover pattern mismatch; retaining legacy trigger walking.");
                return code;
            }
            var patched = new List<CodeInstruction>();
            foreach (var instruction in code)
            {
                if (instruction.Calls(normalize))
                {
                    var loadState = new CodeInstruction(OpCodes.Ldarg_0);
                    loadState.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    loadState.blocks.AddRange(instruction.blocks);
                    instruction.blocks.Clear();
                    patched.Add(loadState);
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(FrameKoiGamepadLocomotion), nameof(FrameKoiGamepadLocomotion.Direction));
                }
                patched.Add(instruction);
            }
            FrameKoiGamepadLocomotion.MoveRequestField = request;
            FrameKoiGamepadLocomotion.Available = true;
            VRLog.Info("FrameKoi game locomotion hook installed; original game movement gates and NavMeshAgent retained.");
            return patched;
        }
    }

    [HarmonyPatch]
    internal static class FrameKoiMoveRequestPatch
    {
        private static bool Prepare() => AccessTools.Method(AccessTools.TypeByName("ActionGame.ActionInput"), "PlayerMove") != null;
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var method = AccessTools.Method(AccessTools.TypeByName("ActionGame.ActionInput"), "PlayerMove");
            if (method != null) yield return method;
        }
        private static bool Prefix(ref bool __0)
        {
            if (!FrameKoiGamepadLocomotion.InPlayerUpdate) return true;
            __0 = FrameKoiGamepadLocomotion.Input.MoveAxis.sqrMagnitude > 0.0001f && Time.timeScale != 0f;
            return false; // no mouse injection, including MoveLook toggle mode
        }
    }

    [HarmonyPatch]
    internal static class FrameKoiMovementModifiersPatch
    {
        private static bool Prepare() => AccessTools.PropertyGetter(AccessTools.TypeByName("ActionGame.ActionInput"), "isWalk") != null &&
            AccessTools.PropertyGetter(AccessTools.TypeByName("ActionGame.ActionInput"), "isCrouch") != null;
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type = AccessTools.TypeByName("ActionGame.ActionInput");
            foreach (var name in new[] { "isWalk", "isCrouch" })
            {
                var method = AccessTools.PropertyGetter(type, name);
                if (method != null) yield return method;
            }
        }
        private static bool Prefix(MethodBase __originalMethod, ref bool __result)
        {
            if (!FrameKoiGamepadLocomotion.Enabled) return true;
            var input = FrameKoiGamepadLocomotion.Input;
            __result = __originalMethod.Name == "get_isWalk" ? !input.StickSprint : input.RequestedCrouch;
            return false;
        }
    }
}
