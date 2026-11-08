using System;
using System.Reflection;
using KK_VR.Controls;
using KK_VR.Settings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRGIN.Controls;
using VRGIN.Core;
using VRGIN.Visuals;

static class Program
{
    static int _checks;
    static void Check(bool value,string message) { if(!value) throw new Exception(message); _checks++; }
    static GUIQuad Panel(float distance)
    {
        var go=new GameObject("panel");
        var quad=go.AddComponent<GUIQuad>();
        go.AddComponent<Collider>().distance=distance;
        go.AddComponent<Renderer>();
        go.transform.localScale=new Vector3(0.3f,0.3f,0.3f);
        GUIQuadRegistry.Quads.Add(quad);
        return quad;
    }
    static GUIQuad Target(KoikMenuHandler handler)=>(GUIQuad)typeof(KoikMenuHandler)
        .GetField("_quad",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(handler);
    static LineRenderer Laser(KoikMenuHandler handler)=>(LineRenderer)typeof(KoikMenuHandler)
        .GetField("Laser",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(handler);
    static void Main()
    {
        var right=new Controller();
        var pointer=new KoikMenuHandler(right);
        Check(pointer.CheckMenu()==false && pointer.LaserVisible,"miss keeps aiming laser visible");
        var far=Panel(3f);
        Check(pointer.CheckMenu() && Target(pointer)==far,"small panel is targetable beyond one meter");
        Check(VR.Input.Mouse.MoveCount==1,"cursor moves on first target acquisition");
        Check(Math.Abs(VR.Input.Mouse.CursorX-0.8*65535)<1 &&
            Math.Abs(VR.Input.Mouse.CursorY-0.75*65535)<1,"hit UV maps to desktop cursor immediately");
        Check(Laser(pointer).transform.parent==right.transform &&
            (Laser(pointer).transform.localPosition).sqrMagnitude==0,
            "native ray uses tracked controller without render-model tip");
        var near=Panel(1.5f);
        Check(pointer.CheckMenu() && Target(pointer)==near,"nearest active panel wins regardless of registry order");
        near.gameObject.SetActive(false);
        Check(pointer.CheckMenu() && Target(pointer)==far,"inactive panel cannot capture ray");
        near.gameObject.SetActive(true);
        near.GetComponent<Renderer>().enabled=false;
        Check(pointer.CheckMenu() && Target(pointer)==far,"disabled renderer cannot capture ray");
        near.GetComponent<Renderer>().enabled=true;
        near.GetComponent<Collider>().enabled=false;
        Check(pointer.CheckMenu() && Target(pointer)==far,"disabled collider cannot capture ray");
        near.GetComponent<Collider>().enabled=true;
        pointer.CheckMenu();
        pointer.OnTrigger(true);
        Check(VR.Input.Mouse.Left,"native UI click starts");
        var other=new KoikMenuHandler(new Controller());
        var before=VR.Input.Mouse.MoveCount;
        other.CheckMenu();
        Check(VR.Input.Mouse.MoveCount==before,"other hand cannot move cursor during held click");
        other.OnTrigger(true);
        other.OnTrigger(false);
        Check(VR.Input.Mouse.Left,"second hand cannot release first hand's UI click");
        pointer.OnTrigger(false);
        Check(!VR.Input.Mouse.Left,"owning pointer releases click");
        before=VR.Input.Mouse.MoveCount;
        other.CheckMenu();
        Check(VR.Input.Mouse.MoveCount==before,"click release frame retains cursor ownership");
        Time.frameCount+=2;
        other.CheckMenu();
        Check(VR.Input.Mouse.MoveCount>before,"other hand can acquire cursor after release");
        other.Dispose();
        pointer.CheckMenu();
        pointer.OnTrigger(true);
        pointer.OnTrigger(true);
        pointer.OnTrigger(false);
        Check(VR.Input.Mouse.Left,"A and trigger share a UI hold without premature release");
        pointer.OnTrigger(false);
        Check(!VR.Input.Mouse.Left,"last UI hold releases the button");
        pointer.CheckMenu();
        pointer.OnTrigger(true);
        near.gameObject.SetActive(false);
        far.gameObject.SetActive(false);
        Check(!pointer.CheckMenu() && !VR.Input.Mouse.Left,"panel loss releases held click");
        Check(pointer.LaserVisible,"panel loss leaves acquisition ray visible");
        far.gameObject.SetActive(true);
        far.IsOwned=true;
        far.transform.SetParent(right.transform,false);
        Check(pointer.CheckMenu(),"controller can point at its owned panel");
        pointer.OnTrigger(true);
        pointer.OnTrigger(true);
        pointer.ReleaseInput();
        Check(!VR.Input.Mouse.Left,"cleanup releases overlapping trigger/A holds");
        Check(far.transform.parent==right.transform && far.IsOwned,"native cleanup preserves panel ownership");
        KoikSettings.FrameKoiPointerRange.Value=1f;
        Check(!pointer.CheckMenu(),"configured pointer range is respected");
        KoikSettings.FrameKoiPointerRange.Value=5f;
        KoikSettings.FrameKoiPointerRotation.Value=new Vector3(0,90,0);
        pointer.CheckMenu();
        Check((Laser(pointer).transform.forward-Vector3.right).sqrMagnitude<0.0001f,"live pointer calibration changes ray direction");
        KoikSettings.FrameKoiPointerRotation.Value=Vector3.zero;
        var handRotation=Quaternion.Euler(45,0,0);
        KK_VR.Holders.HandHolder.GetHands.Add(new KK_VR.Holders.HandHolder { Controller=right, UIPointerRotation=handRotation });
        pointer.CheckMenu();
        Check((Laser(pointer).transform.forward-handRotation*Vector3.forward).sqrMagnitude<0.0001f,
            "pointer follows calibrated hand tracking rather than animated item or model tip");
        KK_VR.Holders.HandHolder.GetHands.Clear();
        var nonAimingController=new Controller();
        nonAimingController.transform.position=new Vector3(1,0,0);
        var nonAiming=new KoikMenuHandler(nonAimingController);
        far.GetComponent<Collider>().filter=ray=>ray.origin.x<0.5f;
        Check(KoikMenuHandler.FindTargetedPointer(nonAiming)==pointer,
            "right-hand A can confirm a panel targeted by the other hand");
        right.Tracking.isValid=false;
        Check(KoikMenuHandler.FindTargetedPointer(nonAiming)==null,"lost controller cannot retain UI confirmation target");
        right.Tracking.isValid=true;
        nonAiming.Dispose();
        EventSystem.current=null;
        Check(!KoikMenuHandler.SubmitSelectedUI(),"no UI selection produces no confirm click");
        var selected=new GameObject("button");
        var selectable=selected.AddComponent<Selectable>();
        EventSystem.current=new EventSystem { currentSelectedGameObject=selected };
        Check(KoikMenuHandler.SubmitSelectedUI() && ExecuteEvents.Submissions==1,"selected UI is submitted directly");
        var modalRoot=new GameObject("modal-root");
        Check(!KoikMenuHandler.SubmitSelectedUI(modalRoot.transform),"foreground modal rejects a background selected control");
        selected.transform.SetParent(modalRoot.transform,false);
        Check(KoikMenuHandler.SubmitSelectedUI(modalRoot.transform),"foreground modal accepts its own selected control");
        selectable.interactable=false;
        Check(!KoikMenuHandler.SubmitSelectedUI(),"disabled selectable is not submitted");
        selectable.interactable=true;
        selected.SetActive(false);
        Check(!KoikMenuHandler.SubmitSelectedUI(),"inactive selectable is not submitted");
        FrameKoiUiPolicy.Interactive=false;
        Check(!pointer.CheckMenu()&&!pointer.LaserVisible,"roaming display-only HUD cannot activate laser");
        FrameKoiUiPolicy.Interactive=true;
        var uiButton=new GameObject("direct-button");
        EventSystem.current=new EventSystem();
        EventSystem.current.Results.Add(new RaycastResult {gameObject=uiButton,module=new GraphicRaycaster()});
        KoikSettings.FrameKoiCompatibilityInput.Value=false;
        pointer.CheckMenu();pointer.OnTrigger(true);
        Check(!VR.Input.Mouse.Left&&ExecuteEvents.Downs==1,"uGUI pointer press uses direct events without synthetic mouse");
        pointer.OnTrigger(false);
        Check(ExecuteEvents.Ups==1&&ExecuteEvents.Clicks==1,"direct UI click pairs release and activation");
        pointer.OnTrigger(true);pointer.ReleaseInput();
        Check(ExecuteEvents.Ups==2&&ExecuteEvents.Clicks==1,"direct pointer cleanup cancels rather than clicks");
        EventSystem.current.Results.Clear();
        EventSystem.current.Results.Add(new RaycastResult {gameObject=uiButton,module=new object()});
        pointer.CheckMenu();pointer.OnTrigger(true);pointer.OnTrigger(false);
        Check(!VR.Input.Mouse.Left&&ExecuteEvents.Clicks==1,"physics/world raycasts cannot become UI clicks");
        KoikSettings.FrameKoiCompatibilityInput.Value=true;
        KoikSettings.FrameKoiLegacyUiClicks.Value=false;
        pointer.CheckMenu();pointer.OnTrigger(true);pointer.OnTrigger(false);
        Check(!VR.Input.Mouse.Left&&ExecuteEvents.Clicks==1,"missing UI target does nothing with default mouse compatibility disabled");
        FrameKoiNativeInput.Ready=false;
        KoikSettings.FrameKoiPointerRotation.Value=Vector3.zero;
        far.GetComponent<Collider>().distance=0.1f;
        pointer.SetLaserVisibility(false);
        Check(pointer.CheckMenu(),"legacy near-panel fallback still works");
        pointer.Dispose();
        Console.WriteLine($"PASS: {_checks} assertions against production KoikMenuHandler; simulated Unity/raycast/desktop boundaries.");
    }
}
