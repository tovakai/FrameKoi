using System;
using System.Collections.Generic;
using UnityEngine;

// Rendering, raycast and desktop-input boundaries. KoikMenuHandler itself is real.
namespace UnityEngine
{
    public struct Ray
    {
        public Vector3 origin, direction;
        public Ray(Vector3 origin, Vector3 direction) { this.origin=origin; this.direction=direction; }
    }
    public struct RaycastHit { public float distance; public Vector3 point; public Vector2 textureCoord; }
    public class Collider : Component
    {
        public bool enabled=true, hit=true;
        public float distance=2f;
        public Vector2 uv=new Vector2(0.8f,0.25f);
        public bool Raycast(Ray ray,out RaycastHit result,float range)
        {
            result=new RaycastHit { distance=distance, point=ray.origin+ray.direction*distance, textureCoord=uv };
            return enabled && hit && distance>=0 && distance<=range;
        }
    }
    public class Shader : Object { public static Shader Find(string name)=>new Shader(); }
    public class Material : Object
    {
        public int renderQueue;
        public Material(Shader shader) { }
    }
    public struct Color
    {
        public Color(float r,float g,float b,float a) { }
        public static Color cyan=>new Color();
    }
    public class LineRenderer : Renderer
    {
        public Material material;
        public Color startColor, endColor;
        public bool useWorldSpace;
        public Vector3[] points=new Vector3[2];
        public void SetPosition(int i,Vector3 position) { points[i]=position; }
        public void SetVertexCount(int count) { }
        public void SetWidth(float start,float end) { }
    }
}
namespace UnityEngine.UI
{
    public class Selectable : Component
    {
        public bool interactable=true;
        public bool IsActive()=>gameObject.activeInHierarchy;
        public bool IsInteractable()=>interactable;
    }
}
namespace UnityEngine.EventSystems
{
    public class EventSystem { public static EventSystem current; public GameObject currentSelectedGameObject; }
    public class BaseEventData { public BaseEventData(EventSystem system) { } }
    public static class ExecuteEvents
    {
        public static object submitHandler=new object();
        public static int Submissions;
        public static bool Execute(GameObject target,BaseEventData data,object handler) { Submissions++; return true; }
    }
}
public class SteamVR
{
    public static SteamVR instance=new SteamVR();
    public string hmd_TrackingSystemName="test";
}
namespace VRGIN.Core
{
    public static class VRGUI { public static int Width=1600, Height=900; }
}
namespace VRGIN.Native
{
    public static class WindowsInterop { }
    public struct Rect { public int Left,Top,Right,Bottom; }
    public static class WindowManager
    {
        public static Rect GetClientRect()=>new Rect {Right=1600,Bottom=900};
        public static Rect GetVirtualScreenRect()=>new Rect {Right=1600,Bottom=900};
    }
}
namespace VRGIN.Visuals
{
    public class GUIQuad : Component { public bool IsOwned; }
    public static class GUIQuadRegistry { public static readonly List<GUIQuad> Quads=new List<GUIQuad>(); }
}
namespace KK_VR.Controls
{
    public static class FrameKoiNativeInput { public static bool Ready=true; }
    public static class FrameKoiInputDiagnostics
    {
        public static void Log(string key,string message,int limit=3) { }
    }
}
