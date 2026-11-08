// Unity/game boundary for production pose, render-scope and locomotion tests.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using WindowsInput.Native;

namespace UnityEngine
{
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        public readonly Transform transform;
        public bool activeSelf = true, activeInHierarchy = true;
        public string name;
        public readonly List<Component> Components = new List<Component>();
        public GameObject(string name = "") { this.name=name; transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : Component,new()
        { var c = new T { gameObject=this }; Components.Add(c); return c; }
        public T GetComponent<T>() where T : Component => Components.Find(c=> c is T) as T;
        public void SetActive(bool value) { activeSelf = activeInHierarchy = value; }
    }
    public class Transform : Object
    {
        public GameObject gameObject;
        public string name => gameObject?.name ?? "";
        public Transform parent;
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 position
        {
            get => parent == null ? localPosition : parent.TransformPoint(localPosition);
            set { localPosition = parent == null ? value : Quaternion.Inverse(parent.rotation)*(value-parent.position); }
        }
        public Quaternion rotation
        {
            get => parent == null ? localRotation : parent.rotation * localRotation;
            set { localRotation = parent == null ? value : Quaternion.Inverse(parent.rotation)*value; }
        }
        public Vector3 eulerAngles
        {
            get => new Vector3(0f, (float)(Math.Atan2(forward.x,forward.z)*180/Math.PI),0f);
            set { rotation = Quaternion.Euler(value); }
        }
        public Vector3 forward => rotation * Vector3.forward;
        public Vector3 TransformPoint(Vector3 point) => position + rotation*point;
        public Transform Find(string path) => this;
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 zero => new Vector3();
        public static Vector3 up => new Vector3(0,1,0);
        public static Vector3 forward => new Vector3(0,0,1);
        public static Vector3 right => new Vector3(1,0,0);
        public float sqrMagnitude => x*x+y*y+z*z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized { [MethodImpl(MethodImplOptions.NoInlining)] get => magnitude > 0 ? this*(1f/magnitude) : zero; }
        public void Normalize() { this=normalized; }
        public static Vector3 Cross(Vector3 a,Vector3 b) => new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
        public static Vector3 operator +(Vector3 a,Vector3 b) => new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float s) => new Vector3(a.x*s,a.y*s,a.z*s);
    }
    public struct Quaternion
    {
        internal System.Numerics.Quaternion q;
        public static Quaternion identity => new Quaternion { q=System.Numerics.Quaternion.Identity };
        public static Quaternion Euler(float x,float y,float z) => Euler(new Vector3(x,y,z));
        public static Quaternion Euler(Vector3 v)
        {
            const float d=(float)Math.PI/180;
            return new Quaternion { q = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY,v.y*d)*
                System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX,v.x*d)*
                System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ,v.z*d) };
        }
        public static Quaternion AngleAxis(float degrees,Vector3 axis) => new Quaternion
        { q = System.Numerics.Quaternion.CreateFromAxisAngle(new System.Numerics.Vector3(axis.x,axis.y,axis.z),degrees*(float)Math.PI/180) };
        public static Quaternion Inverse(Quaternion v) => new Quaternion { q=System.Numerics.Quaternion.Inverse(v.q) };
        public static Quaternion operator *(Quaternion a,Quaternion b) => new Quaternion { q=a.q*b.q };
        public static Vector3 operator *(Quaternion a,Vector3 v)
        { var r=System.Numerics.Vector3.Transform(new System.Numerics.Vector3(v.x,v.y,v.z),a.q); return new Vector3(r.X,r.Y,r.Z); }
    }
    public static class Mathf
    {
        public static float Abs(float v)=>Math.Abs(v);
        public static float Min(float a,float b)=>Math.Min(a,b);
        public static float Sign(float v)=>Math.Sign(v);
        public static float Clamp(float v,float min,float max)=>Math.Max(min,Math.Min(max,v));
        public static float Clamp01(float v)=>Clamp(v,0,1);
    }
    public class Renderer : Component { public bool enabled = true; }
    public class SkinnedMeshRenderer : Renderer { public Transform[] bones = new Transform[0]; }
    public class Camera : Component
    {
        public bool stereoEnabled;
        public static event Action<Camera> onPreCull, onPostRender;
        public void Render(Action during, bool post=true)
        { onPreCull?.Invoke(this); during(); if(post) onPostRender?.Invoke(this); }
    }
}
namespace UnityEngine.AI
{
    public class NavMeshAgent
    {
        public bool enabled=true, isOnNavMesh=true;
        public float speed=2f;
        public ActionGame.Chara.Player Owner;
        public int Moves;
        public void Move(Vector3 delta)
        {
            Moves++;
            var p=Owner.RawPosition+delta;
            // Simulated navigation boundary; actual collision behavior remains a headset test.
            Owner.RawPosition=new Vector3(Mathf.Clamp(p.x,-0.5f,0.5f),p.y,Mathf.Clamp(p.z,-0.5f,0.5f));
        }
    }
}
public class ChaControl : Component
{
    public GameObject objHeadBone = new GameObject("head");
    public GameObject objTop = new GameObject("body");
    public Renderer[] Renderers=new Renderer[0];
    public T[] GetComponentsInChildren<T>(bool inactive) where T : Renderer => Array.ConvertAll(Renderers,r=>(T)r);
}
namespace VRGIN.Core
{
    public class CameraRig
    {
        public Transform Origin = new GameObject("origin").transform;
        public Transform Head;
        public Transform transform => Head;
        public CameraRig() { Head = new GameObject("head").transform; Head.parent = Origin; Head.localPosition = new Vector3(0,1.4f,0); }
    }
}
namespace KK_VR.Settings
{
    public class ValueSetting<T>
    {
        public T Value;
        public ValueSetting(T value) { Value=value; }
    }
    public static partial class KoikSettings
    {
        public enum FrameKoiTurningMode { Smooth, Snap, Off }
        public enum PlayerBodyMode { FullBody, HideArms, HideBody }
        public static ValueSetting<FrameKoiTurningMode> FrameKoiTurnMode = new ValueSetting<FrameKoiTurningMode>(FrameKoiTurningMode.Smooth);
        public static ValueSetting<float> FrameKoiSmoothTurnSpeed = new ValueSetting<float>(90);
        public static ValueSetting<float> FrameKoiStickDeadzone = new ValueSetting<float>(0.2f);
        public static ValueSetting<float> FrameKoiMoveDeadzone = new ValueSetting<float>(0.2f);
        public static ValueSetting<float> FrameKoiMoveSpeed = new ValueSetting<float>(1.5f);
        public static ValueSetting<float> FrameKoiGameMoveScale = new ValueSetting<float>(1f);
        public static Setting FrameKoiGameLocomotion = new Setting { Value=true };
        public static Setting FrameKoiNativeInputEnabled = new Setting { Value=true };
        public static ValueSetting<PlayerBodyMode> FrameKoiPlayerBody = new ValueSetting<PlayerBodyMode>(PlayerBodyMode.HideBody);
    }
    public static class GameSettings
    {
        public static ValueSetting<Vector3> PositionOffset=new ValueSetting<Vector3>(Vector3.zero);
        public static Setting UsingHeadPos=new Setting { Value=true };
        public static Setting CrouchByCameraPos=new Setting();
        public static ValueSetting<float> StandingCameraPos=new ValueSetting<float>(1.4f);
        public static ValueSetting<float> CrouchingCameraPos=new ValueSetting<float>(0.8f);
        public static ValueSetting<float> RotationAngle=new ValueSetting<float>(45f);
    }
}
namespace KK_VR.Camera
{
    public class CameraMover
    {
        public static CameraMover Instance=new CameraMover();
        public void MoveTo(Vector3 p,Quaternion r) { }
    }
}
namespace KK_VR.Features { public static class HideMaleHead { public static bool ForceHideHead; } }
namespace Manager
{
    public class ActionConfig { public bool CrouchCtrlKey; }
    public static class Config { public static ActionConfig ActData = new ActionConfig(); }
}
public class ActionScene
{
    public ActionGame.Chara.Player Player=new ActionGame.Chara.Player();
}
namespace KK_VR.Interpreters
{
    public static class ActionSceneInterp { public static ActionScene actionScene = new ActionScene(); }
}
namespace ActionGame.Chara
{
    public class Player
    {
        public ChaControl chaCtrl;
        public UnityEngine.AI.NavMeshAgent agent;
        public Vector3 RawPosition;
        public int DirectPositionWrites;
        public Vector3 position { get=>RawPosition; set { DirectPositionWrites++; RawPosition=value; } }
        public Quaternion rotation=Quaternion.identity;
        public bool isGateHit, isActionPointHit;
        public object actionTarget;
        public Player()
        {
            var go=new GameObject("player"); chaCtrl=go.AddComponent<ChaControl>();
            chaCtrl.objHeadBone.transform.localPosition=new Vector3(0,1.4f,0);
            agent=new UnityEngine.AI.NavMeshAgent { Owner=this };
        }
    }
}
namespace ActionGame
{
    public static class ActionInput
    {
        public static bool MouseMoving;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void PlayerMove(ref bool moving) { moving=MouseMoving; }
        public static bool isWalk { [MethodImpl(MethodImplOptions.NoInlining)] get=>true; }
        public static bool isCrouch { [MethodImpl(MethodImplOptions.NoInlining)] get=>false; }
    }
}
namespace ActionGame.Chara.Mover
{
    public class Main
    {
        public bool mouseMoving, AllowMove=true, ThrowInUpdate;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Update()
        {
            if (ThrowInUpdate) throw new InvalidOperationException("simulated game exception");
            ActionInput.PlayerMove(ref mouseMoving);
            mouseMoving=mouseMoving && AllowMove;
            var direction=mouseMoving ? VRGIN.Core.VR.Camera.Head.forward : Vector3.zero;
            direction.y=0;
            var velocity=direction.normalized*KK_VR.Interpreters.ActionSceneInterp.actionScene.Player.agent.speed;
            KK_VR.Interpreters.ActionSceneInterp.actionScene.Player.agent.Move(velocity*Time.deltaTime);
        }
    }
}
