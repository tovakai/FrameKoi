using System;
using System.Reflection;
using HarmonyLib;
using KK_VR.Controls;
using KK_VR.Features;
using KK_VR.Interpreters;
using KK_VR.Settings;
using UnityEngine;
using VRGIN.Core;
using static VRGIN.Controls.Controller;
using WindowsInput.Native;

static class HardwareTests
{
    static void Invoke(object value,string name) => value.GetType().GetMethod(name,
        BindingFlags.NonPublic|BindingFlags.Instance).Invoke(value,null);
    static bool Near(Vector3 a,Vector3 b) => (a-b).sqrMagnitude < 0.00001f;

    internal static void Run(Action<bool,string> check)
    {
        check(FrameKoiPoseMath.IsFrameController("FRAME_CONTROLLER") &&
            !FrameKoiPoseMath.IsFrameController("knuckles") && !FrameKoiPoseMath.IsFrameController(null),
            "Frame profile detection is strict and case-insensitive");
        check(Near(FrameKoiPoseMath.HandRotation(Quaternion.identity,new Vector3(-90,0,0))*Vector3.forward,Vector3.up),
            "hand calibration X axis");
        check(Near(FrameKoiPoseMath.HandRotation(Quaternion.identity,new Vector3(0,90,0))*Vector3.forward,Vector3.right),
            "hand calibration Y axis");
        check(Near(FrameKoiPoseMath.HandRotation(Quaternion.identity,new Vector3(0,0,90))*Vector3.right,Vector3.up),
            "hand calibration Z axis");
        var tracked=Quaternion.Euler(0,90,0);
        var calibration=new Vector3(0,0,90);
        check(Near(FrameKoiPoseMath.HandRotation(tracked,calibration)*Vector3.right,
            tracked*(Quaternion.Euler(calibration)*Vector3.right)), "calibration composes in controller-local space");
        check(!Near(FrameKoiPoseMath.HandRotation(tracked,calibration)*Vector3.right,
            Quaternion.Euler(calibration)*(tracked*Vector3.right)), "local correction must not become a world correction");
        check(FrameKoiStickMath.Deadzone(new Vector2(0.1f,0.1f),0.2f).sqrMagnitude==0, "radial deadzone");
        var diagonal=FrameKoiStickMath.Deadzone(new Vector2(1,1),0.2f);
        check(Math.Abs(diagonal.magnitude-1)<0.0001f && Math.Abs(diagonal.x-diagonal.y)<0.0001f, "normalized diagonal");
        var partial=FrameKoiStickMath.Deadzone(new Vector2(0,0.6f),0.2f);
        check(Math.Abs(partial.y-0.5f)<0.0001f, "proportional analog speed");
        check(FrameKoiStickMath.Deadzone(new Vector2(float.NaN,1),0.2f).sqrMagnitude==0, "invalid axis is neutral");

        TestBody(check);
        TestMover(check);
    }
    static void TestBody(Action<bool,string> check)
    {
        KoikGameInterp.CurrentScene=KoikGameInterp.SceneType.ActionScene;
        var character=ActionSceneInterp.actionScene.Player.chaCtrl;
        var body=new GameObject("torso").AddComponent<Renderer>();
        var disabled=new GameObject("already-hidden").AddComponent<Renderer>(); disabled.enabled=false;
        var npc=new GameObject("NPC").AddComponent<Renderer>();
        var hand=new GameObject("tracked-hand").AddComponent<Renderer>();
        character.Renderers=new[] {body,disabled};
        var go=new GameObject("VR-eye");
        var eye=go.AddComponent<UnityEngine.Camera>(); eye.stereoEnabled=true;
        var component=go.AddComponent<FirstPersonBodyVisibility>();
        var spectator=new GameObject("spectator").AddComponent<UnityEngine.Camera>();
        Invoke(component,"OnEnable");
        eye.Render(()=>
        {
            check(!body.enabled && !disabled.enabled && npc.enabled && hand.enabled,"only original protagonist renderers hidden");
            spectator.Render(()=>check(body.enabled && !disabled.enabled,"nested spectator sees original states"));
            check(!body.enabled,"nested spectator restores outer headset scope");
        });
        check(body.enabled && !disabled.enabled,"PostRender restores exact original states");
        eye.Render(()=>{},post:false);
        Invoke(component,"LateUpdate");
        check(body.enabled && !disabled.enabled,"missing PostRender recovers");
        KoikSettings.FrameKoiPlayerBody.Value=KoikSettings.PlayerBodyMode.FullBody;
        eye.Render(()=>check(body.enabled,"FullBody disables concealment"));
        KoikSettings.FrameKoiPlayerBody.Value=KoikSettings.PlayerBodyMode.HideArms;
        var arm=new GameObject("cf_j_arm00_R").AddComponent<Renderer>();
        character.Renderers=new[] {body,arm};
        eye.Render(()=>check(body.enabled && !arm.enabled,"separate arm renderer can be hidden without torso"));
        Time.unscaledTime+=3;
        var mixed=new GameObject("combined-body").AddComponent<SkinnedMeshRenderer>();
        mixed.bones=new[] {body.transform,arm.transform};
        character.Renderers=new Renderer[] {mixed,body};
        eye.Render(()=>check(!mixed.enabled && !body.enabled,"combined skin uses safe HideBody fallback"));
        check(mixed.enabled && body.enabled,"combined-skin fallback is reversible");
        KoikGameInterp.CurrentScene=KoikGameInterp.SceneType.OtherScene;
        eye.Render(()=>check(mixed.enabled && body.enabled,"leaving roaming restores visibility"));
        Invoke(component,"OnDisable");
        check(mixed.enabled && body.enabled,"component disable restores visibility");
        KoikSettings.FrameKoiPlayerBody.Value=KoikSettings.PlayerBodyMode.HideBody;
    }
    static void TestMover(Action<bool,string> check)
    {
        var harmony=new Harmony("FrameKoi.HardwareSimulation");
        harmony.PatchAll(typeof(FrameKoiPlayerMoverPatch).Assembly);
        check(FrameKoiGamepadLocomotion.Available,"guarded game-mover transpiler installs");
        KoikGameInterp.CurrentScene=KoikGameInterp.SceneType.ActionScene;
        var input=new ActionSceneInput();
        KoikGameInterp.SceneInput=input;
        var player=ActionSceneInterp.actionScene.Player;
        player.RawPosition=Vector3.zero;
        player.DirectPositionWrites=0;
        VR.Camera=new CameraRig();
        VR.Mode.Moves=0;
        FrameKoiGamepadLocomotion.RebaseFollow();
        input.SetFrameKoiLeftStick(new Vector2(1,1));
        var mover=new ActionGame.Chara.Mover.Main();
        mover.ThrowInUpdate=true;
        try { mover.Update(); } catch (InvalidOperationException) { }
        check(!FrameKoiGamepadLocomotion.InPlayerUpdate,"exception restores game-update input scope");
        mover.ThrowInUpdate=false;
        Time.frameCount++;
        mover.Update();
        check(player.RawPosition.x>0 && player.RawPosition.z>0 && player.agent.Moves>0,"stick reaches original NavMeshAgent path");
        var accepted=player.RawPosition;
        FrameKoiGamepadLocomotion.FollowPlayer();
        check(Near(VR.Camera.Origin.position,accepted),"headset follows accepted player displacement");
        var roomScale=new Vector3(0.2f,1.4f,0.1f);
        VR.Camera.Head.localPosition=roomScale;
        check(Near(VR.Camera.Head.position-VR.Camera.Origin.position,roomScale),"room-scale offset preserved");
        var before=player.RawPosition;
        mover.AllowMove=false;
        mover.Update();
        check(Near(before,player.RawPosition),"game regulation gate is not bypassed");
        mover.AllowMove=true;
        player.agent.isOnNavMesh=false;
        mover.Update();
        check(Near(before,player.RawPosition),"off-navmesh does not fall back to transform movement");
        player.agent.isOnNavMesh=true;
        VR.Camera.Head.rotation=Quaternion.Euler(90,0,0);
        input.SetFrameKoiLeftStick(new Vector2(0,1));
        var verticalBefore=player.RawPosition;
        mover.Update();
        check(!Near(verticalBefore,player.RawPosition),"vertical head pose retains gated stick movement");
        VR.Camera.Head.rotation=Quaternion.identity;
        input.SetFrameKoiLeftStick(new Vector2(1,1));
        for(int i=0;i<100;i++) mover.Update();
        check(player.RawPosition.x<=0.5f && player.RawPosition.z<=0.5f,"simulated navigation boundary retained");
        input.FrameKoiSprintClick();
        check(input.StickSprint && !ActionGame.ActionInput.isWalk,"stick click changes game's walk/run modifier");
        input.OnDirectionDown(0,TrackpadDirection.Down);
        check(input.RequestedCrouch && ActionGame.ActionInput.isCrouch,"D-pad sets explicit native crouch state");
        mover.Update();
        input.OnDirectionDown(0,TrackpadDirection.Down);
        check(!input.RequestedCrouch && !ActionGame.ActionInput.isCrouch,"can uncrouch after movement begins");
        var events=VR.Input.Mouse.Left;
        input.OnButtonDown(0,Valve.VR.EVRButtonId.k_EButton_SteamVR_Trigger,TrackpadDirection.Center);
        check(VR.Input.Mouse.Left==events && player.DirectPositionWrites==0,"native trigger does not start walking or teleport player");
        player.isGateHit=true;
        input.OnButtonDown(1,Valve.VR.EVRButtonId.k_EButton_SteamVR_Trigger,TrackpadDirection.Center);
        check(VR.Input.Mouse.Right,"trigger retains contextual interaction");
        input.OnButtonUp(0,Valve.VR.EVRButtonId.k_EButton_SteamVR_Trigger,TrackpadDirection.Center);
        check(VR.Input.Mouse.Right,"one trigger release preserves the other held contextual trigger");
        input.OnButtonUp(1,Valve.VR.EVRButtonId.k_EButton_SteamVR_Trigger,TrackpadDirection.Center);
        check(!VR.Input.Mouse.Right,"last trigger releases contextual input");
        player.isGateHit=false;
        input.SetFrameKoiRightStick(0);
        KoikSettings.FrameKoiRightStickTurning.Value = false;
        GameSettings.CrouchByCameraPos.Value = true;
        var standingHead = VR.Camera.Head.position;
        input.OnDirectionDown(0,TrackpadDirection.Down);
        for (int i=0;i<20;i++) input.HandleInput();
        var crouchedHead = VR.Camera.Head.position;
        var expectedDrop = GameSettings.StandingCameraPos.Value - GameSettings.CrouchingCameraPos.Value;
        check(Math.Abs(standingHead.y-crouchedHead.y-expectedDrop)<0.001f &&
            Math.Abs(standingHead.x-crouchedHead.x)<0.001f && Math.Abs(standingHead.z-crouchedHead.z)<0.001f,
            "button crouch lowers only virtual headset height");
        input.OnDirectionDown(0,TrackpadDirection.Down);
        for (int i=0;i<20;i++) input.HandleInput();
        check(Near(standingHead,VR.Camera.Head.position) && !input.RequestedCrouch,
            "uncrouch restores headset without physical-crouch feedback");
        input.OnDirectionDown(0,TrackpadDirection.Down);
        input.HandleInput();
        input.ReleaseController(0);
        check(!input.RequestedCrouch && !input.StickSprint,"controller release clears crouch/sprint");
        check(Near(standingHead,VR.Camera.Head.position), "controller release removes partial crouch-camera offset");
        VR.Camera.Head.localPosition += new Vector3(0,-0.7f,0);
        var physicalHead = VR.Camera.Head.position;
        input.HandleInput();
        check(Near(physicalHead,VR.Camera.Head.position) && input.RequestedCrouch, "physical crouch is not lowered twice");
        VR.Camera.Head.localPosition += new Vector3(0,0.7f,0);
        input.HandleInput();
        check(!input.RequestedCrouch, "physical uncrouch still works");
        input.OnDirectionDown(0,TrackpadDirection.Down);
        input.HandleInput();
        input.OnDisable();
        check(Near(standingHead,VR.Camera.Head.position), "scene teardown removes virtual crouch offset");
        GameSettings.CrouchByCameraPos.Value = false;
        input.SetFrameKoiLeftStick(Vector2.zero);
        KoikSettings.FrameKoiRightStickTurning.Value=true;
        input.SetFrameKoiRightStick(1);
        var headBefore=VR.Camera.Head.position;
        var forwardBefore=VR.Camera.Head.forward;
        input.HandleInput();
        check(!Near(forwardBefore,VR.Camera.Head.forward) && Near(headBefore,VR.Camera.Head.position),
            "smooth turning retains headset pivot");
        check(player.DirectPositionWrites==0,"smooth yaw does not teleport protagonist under native game movement");
        var pausePosition=player.RawPosition;
        Time.timeScale=0;
        mover.Update();
        check(Near(pausePosition,player.RawPosition),"pause suppresses native stick displacement");
        Time.timeScale=1;
        KoikSettings.FrameKoiGameLocomotion.Value=false;
        input.Crouch(true);
        Manager.Config.ActData.CrouchCtrlKey=!Manager.Config.ActData.CrouchCtrlKey;
        input.StandUp();
        check(!VR.Input.Keyboard.Held.Contains(VirtualKeyCode.VK_Z) && !VR.Input.Keyboard.Held.Contains(VirtualKeyCode.CONTROL),
            "uncrouch reconciles both keys after config change");
        input.OnDisable();
        KoikSettings.FrameKoiGameLocomotion.Value=true;
        var transpiler=typeof(FrameKoiPlayerMoverPatch).GetMethod("Transpiler",BindingFlags.NonPublic|BindingFlags.Static);
        transpiler.Invoke(null,new object[] { new System.Collections.Generic.List<CodeInstruction>() });
        check(!FrameKoiGamepadLocomotion.Available && !FrameKoiGamepadLocomotion.Enabled,
            "unknown mover pattern fails closed even after a previous successful install");
        input.OnButtonDown(0,Valve.VR.EVRButtonId.k_EButton_SteamVR_Trigger,TrackpadDirection.Center);
        check(VR.Input.Mouse.Left,"legacy trigger walking remains available on hook mismatch");
        input.OnButtonUp(0,Valve.VR.EVRButtonId.k_EButton_SteamVR_Trigger,TrackpadDirection.Center);
        check(!VR.Input.Mouse.Left,"legacy fallback releases synthetic walking input");
        input.OnDisable();
        harmony.UnpatchAll(harmony.Id);
    }
}
