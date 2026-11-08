using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using KK_VR.Controls;
using KK_VR.Holders;
using KK_VR.Interpreters;
using UnityEngine;
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
    static void Main()
    {
        FrameKoiNativeInput.ValidateAbi();
        InstallApi();
        VR.Mode.Left = new VRGIN.Controls.Controller();
        VR.Mode.Right = new VRGIN.Controls.Controller();
        // Deliberately unrelated to hand indices.
        VR.Mode.Left.Tracking.index = 7;
        VR.Mode.Right.Tracking.index = 3;
        var left = new TestTool();
        var right = new TestTool();
        left.OnRenderModelLoaded(); // callback before Tool.OnStart must be harmless
        left.Start(VR.Mode.Left);
        right.Start(VR.Mode.Right); // no render callback at all
        Check(left.enabled && right.enabled, "tools must remain enabled after startup");
        Check(KoikMenuHandler.Created == 2 && KoikMenuTool.Created == 2, "one menu/handler per owner");
        Next(left, right);
        Check(Polls == 1, "both hands update the native action set once per frame");
        Pressed.Add("left_view");
        Pressed.Add("right_menu");
        Next(left, right);
        Check(KoikMenuTool.Toggles == 2, "both menus dispatch without hand models or render callbacks");
        Next(left, right);
        Check(KoikMenuTool.Toggles == 2, "held buttons must not repeat down");
        Pressed.Clear();
        Next(left, right);

        HandHolder.GetHands.Add(new HandHolder());
        HandHolder.GetHands.Add(new HandHolder());
        var old = KoikGameInterp.SceneInput;
        Pressed.Add("left_grip");
        Pressed.Add("right_trigger");
        X = 0.7f; Y = -0.4f;
        Next(left, right);
        Check(old.Events.Contains("down:0:k_EButton_Grip"), "left ownership follows controller role");
        Check(old.Events.Contains("down:1:k_EButton_SteamVR_Trigger"), "right ownership follows controller role");
        Check(old.LeftStick.y == Y && old.RightStick == X, "analog dispatch");
        Check(!left.IsGrip && !right.IsGrip, "native grips never create GripMove");

        // Same order as the production StartScene teardown hook.
        left.ReleaseInput(); right.ReleaseInput();
        Check(old.Events.Contains("up:0:k_EButton_Grip") && old.Events.Contains("up:1:k_EButton_SteamVR_Trigger"),
            "scene teardown pairs releases with old scene");
        Check(old.LeftStick.x == 0 && old.RightStick == 0, "scene teardown clears axes");
        var current = KoikGameInterp.SceneInput = new SceneInput();
        Next(left, right);
        Check(current.Events.Contains("down:0:k_EButton_Grip"), "held input enters new scene consistently");

        Pressed.Add("b"); Pressed.Add("a"); Pressed.Add("x");
        Next(left, right);
        Check(VR.Input.Keyboard.Escape && !VR.Input.Mouse.Left && VR.Input.Mouse.Right && KoikMenuHandler.Submissions == 1,
            "A routes to UI submission without injecting a world click");
        right.Disable();
        Check(!VR.Input.Keyboard.Escape && !VR.Input.Mouse.Left && !VR.Input.Mouse.Right, "disable flushes desktop releases");
        right.OnRenderModelLoaded();
        Check(!right.enabled && KoikMenuHandler.Created == 2, "late callbacks preserve focus and do not duplicate handlers");
        VR.Mode.Left.Tracking.isValid = false;
        Next(left);
        Check(current.Events.Contains("up:0:k_EButton_Grip") && current.LeftStick.x == 0, "disconnect releases input");
        VR.Mode.Left.Tracking.isValid = true;
        Active = false;
        Next(left);
        Check(!FrameKoiNativeInput.Digital("left_grip") && FrameKoiNativeInput.Analog("left_stick").x == 0, "inactive actions produce no input");
        Active = true;
        ReadError = 4;
        Check(!FrameKoiNativeInput.Digital("left_grip") && FrameKoiNativeInput.Analog("left_stick").x == 0, "read errors produce no input");
        ReadError = 0;
        UpdateError = 1;
        Next(left);
        Check(!FrameKoiNativeInput.Ready, "polling failure enables legacy fallback");
        Next(left); // exercise legacy update without any render callback
        left.DestroyTool(); right.DestroyTool();
        FrameKoiNativeInput.Reset();
        Check(!FrameKoiNativeInput.Ready && !FrameKoiNativeInput.Poll(), "reset is safe");
        Check(!FrameKoiNativeInput.Initialize("missing-actions.json"), "missing manifest preserves fallback");
        InstallApi();
        UpdateError = 0; ReadError = 0; Active = true; Pressed.Clear();
        var menuTool = new TestTool();
        menuTool.Start(VR.Mode.Right);
        KoikMenuHandler.MenuInRange = true;
        Pressed.Add("a");
        Next(menuTool);
        Check(VR.Input.Mouse.Left, "A retains targeted-menu click behavior");
        Pressed.Clear();
        Next(menuTool);
        Check(!VR.Input.Mouse.Left, "targeted-menu click releases cleanly");
        KoikMenuHandler.MenuInRange = false;
        menuTool.DestroyTool();
        HardwareTests.Run(Check);
        FrameKoiInputDiagnostics.Reset();
        VRLog.Messages.Clear();
        for (int i = 0; i < 1000; i++) FrameKoiInputDiagnostics.Log("same", "repeat");
        Check(VRLog.Messages.Count == 3, "per-event diagnostics are bounded");
        for (int i = 0; i < 1000; i++) FrameKoiInputDiagnostics.Log("event-" + i, "event");
        Check(VRLog.Messages.Count == 301, "session diagnostics are bounded");
        Console.WriteLine($"PASS: {_assertions} assertions, real GameplayTool/native bridge, simulated runtime. No hardware validation.");
    }
}
