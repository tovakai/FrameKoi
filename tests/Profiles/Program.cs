using System;
using System.Collections.Generic;
using KK_VR.Controls;
using KK_VR.Settings;
using UnityEngine;
using VRGIN.Core;
using VRGIN.Visuals;

static class Program
{
    static int checks;
    static void Check(bool value,string message) { if(!value)throw new Exception(message);checks++; }
    sealed class Sink : IFrameKoiActionSink
    {
        internal readonly List<string> Events=new List<string>();
        internal Action OnBegin;
        public Action Begin(FrameKoiAction action,int hand,FrameKoiContext context)
        {
            Events.Add("down:"+hand+":"+action);
            OnBegin?.Invoke();
            return ()=>Events.Add("up:"+hand+":"+action);
        }
        public void Neutralize(FrameKoiContext previous) { Events.Add("neutral"); }
    }
    static FrameKoiContext Context(FrameKoiProfile profile,object owner,bool blocking=false,bool suspended=false,string menu="") =>
        new FrameKoiContext {Profile=profile,Owner=owner,Blocking=blocking,Suspended=suspended,Menu=menu};
    static void Main()
    {
        var bindings=FrameKoiBindings.Defaults();
        Check(FrameKoiContext.Resolve(true,false,false,true,false)==FrameKoiProfile.DialogueMenu,"blocking UI precedes roaming");
        Check(FrameKoiContext.Resolve(false,true,false,false,false)==FrameKoiProfile.DialogueMenu,"dialogue precedes scene");
        Check(FrameKoiContext.Resolve(false,false,true,false,false)==FrameKoiProfile.DialogueMenu,"focused scene menu captures context");
        Check(FrameKoiContext.Resolve(false,false,false,false,false)==FrameKoiProfile.Scene,"special scene profile");
        Check(FrameKoiContext.Resolve(false,false,false,true,false)==FrameKoiProfile.Roaming,"roaming profile");
        foreach(FrameKoiProfile profile in Enum.GetValues(typeof(FrameKoiProfile)))
        {
            Check(bindings.Resolve(profile,"left_grip")==FrameKoiAction.Grab&&bindings.Resolve(profile,"right_grip")==FrameKoiAction.Grab,"symmetric grips");
            Check(bindings.Resolve(profile,"left_bumper")==FrameKoiAction.SelectUI&&bindings.Resolve(profile,"right_bumper")==FrameKoiAction.SelectUI,"bumpers are UI-only");
            Check(bindings.Resolve(profile,"b")==FrameKoiAction.Cancel&&bindings.Resolve(profile,"x")==FrameKoiAction.Pause,"cancel and pause are distinct");
            Check(bindings.Resolve(profile,"right_stick_click")==FrameKoiAction.Recenter,"universal recenter");
        }
        Check(bindings.Resolve(FrameKoiProfile.Roaming,"a")==FrameKoiAction.Interact,"roaming A");
        Check(bindings.Resolve(FrameKoiProfile.Roaming,"y")==FrameKoiAction.Crouch,"roaming Y");
        Check(bindings.Resolve(FrameKoiProfile.Roaming,"dpad_up")==FrameKoiAction.QuickTravel,"quick travel");
        Check(bindings.Resolve(FrameKoiProfile.Roaming,"dpad_right")==FrameKoiAction.NextPeriod,"next period");
        Check(bindings.Resolve(FrameKoiProfile.Roaming,"dpad_down")==FrameKoiAction.None&&bindings.Resolve(FrameKoiProfile.Roaming,"dpad_left")==FrameKoiAction.None,"reserved shortcuts");
        Check(bindings.Resolve(FrameKoiProfile.Roaming,"left_view")==FrameKoiAction.ToggleHUD,"View HUD");
        Check(bindings.Resolve(FrameKoiProfile.Scene,"a")==FrameKoiAction.Primary&&bindings.Resolve(FrameKoiProfile.Scene,"y")==FrameKoiAction.Secondary,"scene primary/secondary");
        Check(bindings.Resolve(FrameKoiProfile.DialogueMenu,"a")==FrameKoiAction.Confirm,"dialogue A");
        bindings.Override(FrameKoiProfile.Roaming,"y=Recenter;bad=Invalid;dpad_left=None");
        Check(bindings.Resolve(FrameKoiProfile.Roaming,"y")==FrameKoiAction.Recenter,"config remap");
        Check(bindings.Resolve(FrameKoiProfile.Scene,"y")==FrameKoiAction.Secondary,"remap profile isolation");

        var sink=new Sink();var router=new FrameKoiContextRouter(sink,FrameKoiBindings.Defaults());
        var scene=new object();
        router.Change(Context(FrameKoiProfile.Roaming,scene));
        router.Sample(0,"left_grip",true);router.Sample(1,"right_grip",true);
        Check(sink.Events.Contains("down:0:Grab")&&sink.Events.Contains("down:1:Grab"),"separate hand events");
        var count=sink.Events.Count;router.Sample(0,"left_grip",true);Check(sink.Events.Count==count,"held input does not repeat");
        router.Change(Context(FrameKoiProfile.DialogueMenu,scene,true,menu:"CheckScene"));
        Check(sink.Events.Contains("up:0:Grab")&&sink.Events.Contains("up:1:Grab"),"context transition pairs releases");
        count=sink.Events.Count;router.Sample(0,"left_grip",true);Check(sink.Events.Count==count,"held grip quarantined");
        router.Sample(0,"left_grip",false);router.Sample(0,"left_grip",true);
        Check(sink.Events.Count==count,"modal suppresses new grabs");
        router.Sample(1,"a",true);Check(sink.Events.Contains("down:1:Confirm"),"modal confirm");
        router.Change(Context(FrameKoiProfile.Roaming,scene));
        count=sink.Events.Count;router.Sample(1,"a",true);Check(sink.Events.Count==count,"held confirm cannot become interaction");
        router.Sample(1,"a",false);router.Sample(1,"a",true);Check(sink.Events.Contains("down:1:Interact"),"release rearms new context");
        router.Change(Context(FrameKoiProfile.Scene,new object()));
        router.Seed(1,"right_trigger",true);count=sink.Events.Count;
        router.Sample(1,"right_trigger",true);Check(sink.Events.Count==count,"held reconnect seeded without press");
        router.Sample(1,"right_trigger",false);router.Sample(1,"right_trigger",true);
        Check(sink.Events.Contains("down:1:HandAction"),"reconnect release rearms trigger");
        router.Disconnect(1);Check(sink.Events.Contains("up:1:HandAction"),"disconnect releases original action");
        count=sink.Events.Count;router.Sample(1,"right_trigger",true);Check(sink.Events.Count==count,"disconnect suppresses held reconnect");
        router.Change(Context(FrameKoiProfile.Scene,new object(),suspended:true));
        count=sink.Events.Count;router.Sample(1,"x",true);Check(sink.Events.Count==count,"SteamVR focus/loading suppresses presses");
        router.Change(Context(FrameKoiProfile.Scene,new object()));
        count=sink.Events.Count;router.Sample(1,"x",true);Check(sink.Events.Count==count,"resume does not reuse held pause");
        router.Sample(1,"x",false);router.Sample(1,"x",true);Check(sink.Events.Contains("down:1:Pause"),"new pause after focus regain");
        var next=new object();sink.OnBegin=()=>{sink.OnBegin=null;router.Change(Context(FrameKoiProfile.DialogueMenu,next));};
        router.Sample(1,"x",false);router.Sample(1,"a",true);
        Check(sink.Events[sink.Events.Count-1]=="up:1:Primary","synchronous scene switch releases newly returned activation");
        router.Reset();
        TestPresentation();
        Console.WriteLine($"PASS: {checks} semantic mapping/context/presentation assertions. Simulated runtime; no hardware validation.");
    }
    static bool Near(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<0.0001f;
    static void TestPresentation()
    {
        VR.Camera=new CameraRig();
        var ui=new FrameKoiPresentation();var owner=new object();
        ui.Sync(FrameKoiProfile.Roaming,false,false,true,owner,false);
        Check(ui.Mode==FrameKoiPanelMode.Hidden,"roaming HUD starts hidden");
        ui.HudVisible=true;ui.Sync(FrameKoiProfile.Roaming,false,false,true,owner,false);
        var panel=GUIQuad.Last;
        Check(ui.Mode==FrameKoiPanelMode.RoamingHUD&&panel.transform.parent==VR.Camera.Head,"single headset-facing HUD");
        Check(!panel.GetComponent<Collider>().enabled&&!ui.Interactive,"HUD cannot receive laser selection");
        ui.Sync(FrameKoiProfile.DialogueMenu,false,true,false,owner,false);
        Check(ui.Mode==FrameKoiPanelMode.Dialogue&&panel.transform.parent==null,"dialogue automatically opens stable world panel");
        Check(panel.GetComponent<Collider>().enabled&&ui.Interactive,"dialogue is interactive");
        var position=panel.transform.position;
        Check(position.x>VR.Camera.Head.position.x&&position.y<VR.Camera.Head.position.y,"dialogue offset below and to preferred side");
        VR.Camera.Head.position+=new Vector3(0.3f,0,0);VR.Camera.Head.rotation=Quaternion.Euler(0,40,0);
        ui.Sync(FrameKoiProfile.DialogueMenu,false,true,false,owner,false);
        Check(Near(panel.transform.position,position),"head motion does not drag dialogue panel");
        ui.Sync(FrameKoiProfile.DialogueMenu,false,true,true,owner,false);
        Check(!Near(panel.transform.position,position),"camera setup completion recenters once");
        position=panel.transform.position;VR.Camera.Head.position+=new Vector3(0.2f,0,0);
        ui.Sync(FrameKoiProfile.DialogueMenu,false,true,true,owner,false);
        Check(Near(panel.transform.position,position),"settled dialogue remains world-stable");
        ui.Recenter();Check(!Near(panel.transform.position,position),"explicit panel recenter");
        ui.Sync(FrameKoiProfile.Roaming,false,false,true,owner,false);
        Check(ui.Mode==FrameKoiPanelMode.RoamingHUD&&!panel.GetComponent<Collider>().enabled,"previous HUD restored after dialogue");
        ui.HudVisible=false;ui.Sync(FrameKoiProfile.Roaming,false,false,true,owner,false);
        Check(!panel.gameObject.activeSelf,"View hide removes HUD completely");
        ui.Sync(FrameKoiProfile.DialogueMenu,true,false,true,owner,true);
        Check(ui.Mode==FrameKoiPanelMode.Hidden&&!panel.gameObject.activeSelf,"loading/focus suspension hides presentation");
        ui.Dispose();
    }
}
