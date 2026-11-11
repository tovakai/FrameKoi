using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using KK_VR.Controls;
using KK_VR.Holders;
using KK_VR.Interpreters;
using UnityEngine;
using Valve.VR;
using VRGIN.Core;

sealed class TestTool : GameplayTool
{
    public void Start(VRGIN.Controls.Controller owner) { Owner = owner; OnStart(); }
    public void Tick() { OnUpdate(); }
    public void Disable() { enabled = false; OnDisable(); }
    public void DestroyTool() { OnDestroy(); }
}

static class Program
{
    static readonly Dictionary<ulong, string> Names = new Dictionary<ulong, string>();
    static readonly HashSet<string> Pressed = new HashSet<string>();
    public static bool Active = true;
    public static int Polls, UpdateError, ReadError;
    public static float X, Y;
    static int _assertions;
    static readonly Type Bridge = typeof(FrameKoiNativeInput);
    static FieldInfo Field(string name) => Bridge.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _assertions++;
    }
    public static int Update(uint size, uint count)
    {
        Check(size == 32 && count == 1, "UpdateActionState ABI");
        Polls++;
        return UpdateError;
    }
    public static bool IsPressed(ulong handle) => Pressed.Contains(Names[handle]);
    public static int Read(uint actual, uint expected)
    {
        Check(actual == expected, "GetActionData ABI");
        return ReadError;
    }

    // Bind the production private delegate types to managed mock functions.
    // The structs, polling, active-state filtering and dispatch remain real.
    static void InstallApi()
    {
        FrameKoiNativeInput.Reset();
        var apiField = Field("_api");
        var api = Activator.CreateInstance(apiField.FieldType, true);
        foreach (var name in new[] { "Update", "GetDigital", "GetAnalog" })
        {
            var field = api.GetType().GetField(name);
            var parameters = field.FieldType.GetMethod("Invoke").GetParameters()
                .Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            var body = new List<Expression>();
            if (name == "Update")
                body.Add(Expression.Call(typeof(Program).GetMethod(nameof(Update)), parameters[1], parameters[2]));
            else
            {
                body.Add(Expression.Assign(Expression.Field(parameters[1], "active"),
                    Expression.Field(null, typeof(Program).GetField(nameof(Active)))));
                body.Add(Expression.Assign(Expression.Field(parameters[1], "origin"), Expression.Constant(123UL)));
                if (name == "GetDigital")
                    body.Add(Expression.Assign(Expression.Field(parameters[1], "state"),
                        Expression.Call(typeof(Program).GetMethod(nameof(IsPressed)), parameters[0])));
                else
                {
                    body.Add(Expression.Assign(Expression.Field(parameters[1], "x"), Expression.Field(null, typeof(Program).GetField(nameof(X)))));
                    body.Add(Expression.Assign(Expression.Field(parameters[1], "y"), Expression.Field(null, typeof(Program).GetField(nameof(Y)))));
                }
                body.Add(Expression.Call(typeof(Program).GetMethod(nameof(Read)), parameters[2],
                    Expression.Constant(name == "GetDigital" ? 24U : 48U)));
            }
            field.SetValue(api, Expression.Lambda(field.FieldType, Expression.Block(body), parameters).Compile());
        }
        apiField.SetValue(null, api);
        Field("_ready").SetValue(null, true);
        var handles = (Dictionary<string, ulong>)Field("Handles").GetValue(null);
        var names = new[] { "left_grip", "left_trigger", "left_stick_click", "left_bumper",
            "dpad_up", "dpad_down", "dpad_left", "dpad_right", "left_view",
            "right_grip", "right_trigger", "right_stick_click", "right_bumper", "a", "b", "x", "y", "right_menu",
            "left_stick", "right_stick" };
        ulong id = 0;
        foreach (var name in names) { handles[name] = ++id; Names[id] = name; }
    }
    static void Next(params TestTool[] tools)
    {
        Time.frameCount++;
        Time.unscaledTime += 0.02f;
        foreach (var tool in tools) tool.Tick();
    }
    sealed class SimulatedRuntime : IFrameKoiInputRuntime, IFrameKoiActionSink
    {
        internal bool Menu;
        internal int Interactions, Confirms;
        readonly FrameKoiContextRouter router;
        readonly KoikMenuHandler[] pointers=new KoikMenuHandler[2];
        internal SimulatedRuntime() { router=new FrameKoiContextRouter(this,FrameKoiBindings.Defaults()); }
        void Sync() { router.Change(new FrameKoiContext { Profile=Menu?FrameKoiProfile.DialogueMenu:FrameKoiProfile.Roaming, Owner=KoikGameInterp.SceneInput }); }
        public void TickHand(int hand,KoikMenuHandler pointer)
        {
            pointers[hand]=pointer; Sync();
            if(!FrameKoiNativeInput.Poll()) { router.Reset();return; }
            foreach(var name in Names.Values)
            {
                bool belongs=hand==0 ? name.StartsWith("left_")||name.StartsWith("dpad_") : name.StartsWith("right_")||name.Length==1;
                if(belongs && !name.EndsWith("_stick")) router.Sample(hand,name,FrameKoiNativeInput.Digital(name));
            }
            var scene=KoikGameInterp.SceneInput;
            var axis=FrameKoiNativeInput.Analog(hand==0?"left_stick":"right_stick");
            if(hand==0) scene.SetFrameKoiLeftStick(Menu?Vector2.zero:axis);
            else scene.SetFrameKoiRightStick(Menu?0:axis.x);
        }
        public void Disconnect(int hand) { router.Disconnect(hand); }
        public void Neutralize(FrameKoiContext previous)
        {
            var scene=previous?.Owner as SceneInput;
            scene?.SetFrameKoiLeftStick(Vector2.zero);scene?.SetFrameKoiRightStick(0);
        }
        public Action Begin(FrameKoiAction action,int hand,FrameKoiContext context)
        {
            var scene=(SceneInput)context.Owner;
            if(action==FrameKoiAction.Grab||action==FrameKoiAction.HandAction)
            {
                var button=action==FrameKoiAction.Grab?EVRButtonId.k_EButton_Grip:EVRButtonId.k_EButton_SteamVR_Trigger;
                scene.OnButtonDown(hand,button,VRGIN.Controls.Controller.TrackpadDirection.Center);
                return ()=>scene.OnButtonUp(hand,button,VRGIN.Controls.Controller.TrackpadDirection.Center);
            }
            if(action==FrameKoiAction.SelectUI && pointers[hand].CheckMenu())
            { pointers[hand].OnTrigger(true);return ()=>pointers[hand].OnTrigger(false); }
            if(action==FrameKoiAction.Interact) Interactions++;
            if(action==FrameKoiAction.Confirm) Confirms++;
            return null;
        }
    }
    static void Main()
    {
        FrameKoiNativeInput.ValidateAbi(); InstallApi();
        var runtime=new SimulatedRuntime(); FrameKoiInputRuntime.Current=runtime;
        VR.Mode.Left=new VRGIN.Controls.Controller(); VR.Mode.Right=new VRGIN.Controls.Controller();
        VR.Mode.Left.Tracking.index=7;VR.Mode.Right.Tracking.index=3;
        HandHolder.GetHands.Add(new HandHolder());HandHolder.GetHands.Add(new HandHolder());
        var left=new TestTool();var right=new TestTool();
        left.OnRenderModelLoaded();left.Start(VR.Mode.Left);right.Start(VR.Mode.Right);
        Check(left.enabled&&right.enabled,"render model is not required for native tool activation");
        Next(left,right); Check(Polls==1,"one native action update per frame for both hands");
        var old=KoikGameInterp.SceneInput;
        Pressed.Add("left_grip");Pressed.Add("right_trigger");X=0.7f;Y=-0.4f;
        Next(left,right);
        Check(old.Events.Contains("down:0:k_EButton_Grip") && old.Events.Contains("down:1:k_EButton_SteamVR_Trigger"),"semantic hand ownership follows roles, not device indices");
        Check(old.LeftStick.y==Y&&old.RightStick==X,"analog transport remains independent");
        Check(!left.IsGrip&&!right.IsGrip,"native grips never create world dragging");
        var current=KoikGameInterp.SceneInput=new SceneInput();Next(left,right);
        Check(old.Events.Contains("up:0:k_EButton_Grip"),"scene change releases into original scene");
        Check(!current.Events.Contains("down:0:k_EButton_Grip"),"held grip cannot become a new scene press");
        Pressed.Clear();Next(left,right);Pressed.Add("left_grip");Next(left,right);
        Check(current.Events.Contains("down:0:k_EButton_Grip"),"release rearms grip after context transition");
        Pressed.Clear();runtime.Menu=true;Next(left,right);KoikMenuHandler.MenuInRange=true;
        Pressed.Add("right_bumper");Next(left,right);
        Check(VR.Input.Mouse.Left&&!current.Events.Contains("down:1:k_EButton_SteamVR_Touchpad"),"bumper selects UI without cycling tools");
        Pressed.Clear();Next(left,right);Check(!VR.Input.Mouse.Left,"bumper release pairs UI click");
        Pressed.Add("a");Next(left,right);Check(runtime.Confirms==1&&!VR.Input.Mouse.Left,"menu A is semantic confirmation, not a world click");
        runtime.Menu=false;Next(left,right);Check(runtime.Interactions==0,"held confirm cannot leak into roaming interaction");
        Pressed.Clear();Next(left,right);Pressed.Add("a");Next(left,right);Check(runtime.Interactions==1,"fresh roaming A interacts");
        Pressed.Clear();Next(left,right);Pressed.Add("left_grip");Next(left,right);
        var count=current.Events.Count;VR.Mode.Left.Tracking.isValid=false;Next(left);
        Check(current.Events.Count==count+1,"tracking loss releases held hand action");
        VR.Mode.Left.Tracking.isValid=true;Next(left);Check(current.Events.Count==count+1,"held reconnect input stays quarantined");
        right.Disable();right.OnRenderModelLoaded();Check(!right.enabled,"late render callback cannot steal controller focus");
        Active=false;Next(left);Check(!FrameKoiNativeInput.Digital("left_grip")&&FrameKoiNativeInput.Analog("left_stick").x==0,"inactive native actions are neutral");
        Active=true;ReadError=4;Check(!FrameKoiNativeInput.Digital("left_grip"),"native read errors are neutral");ReadError=0;
        UpdateError=1;Next(left);Check(!FrameKoiNativeInput.Ready,"poll error disables native bridge");
        left.DestroyTool();right.DestroyTool();FrameKoiInputRuntime.Current=null;
        FrameKoiNativeInput.Reset();Check(!FrameKoiNativeInput.Ready&&!FrameKoiNativeInput.Poll(),"native reset is safe");
        Check(!FrameKoiNativeInput.Initialize("missing-actions.json"),"missing manifest retains legacy compatibility");
        InstallApi();UpdateError=0;ReadError=0;Active=true;Pressed.Clear();
        FrameKoiInputRuntime.RoamingAllowed=true;HardwareTests.Run(Check);
        FrameKoiInputDiagnostics.Reset();VRLog.Messages.Clear();
        for(int i=0;i<1000;i++)FrameKoiInputDiagnostics.Log("same","repeat");
        Check(VRLog.Messages.Count==3,"per-event diagnostic limit");
        for(int i=0;i<3000;i++)FrameKoiInputDiagnostics.Log("event-"+i,"event");
        Check(VRLog.Messages.Count==2001,"session diagnostic limit");
        Console.WriteLine($"PASS: {_assertions} assertions, native bridge/semantic transport/game movement simulations. No hardware validation.");
    }
}
