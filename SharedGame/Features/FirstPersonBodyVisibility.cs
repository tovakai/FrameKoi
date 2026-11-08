using System;
using System.Collections.Generic;
using KK_VR.Interpreters;
using KK_VR.Settings;
using UnityEngine;
using VRGIN.Core;

namespace KK_VR.Features
{
    // Hide renderers only within a headset camera's render scope. Never
    // deactivate character objects/bones or modify animation/IK state.
    internal sealed class FirstPersonBodyVisibility : MonoBehaviour
    {
        private sealed class Scope
        {
            internal UnityEngine.Camera Camera;
            internal readonly Dictionary<Renderer, bool> Saved = new Dictionary<Renderer, bool>();
        }
        private readonly Stack<Scope> _scopes = new Stack<Scope>();
        private readonly Dictionary<Renderer, bool> _baseline = new Dictionary<Renderer, bool>();
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private ChaControl _target;
        private KoikSettings.PlayerBodyMode _mode;
        private float _nextRefresh;
        private bool _warnedMixed;

        private void OnEnable()
        {
            UnityEngine.Camera.onPreCull += BeforeCamera;
            UnityEngine.Camera.onPostRender += AfterCamera;
        }
        private void OnDisable()
        {
            UnityEngine.Camera.onPreCull -= BeforeCamera;
            UnityEngine.Camera.onPostRender -= AfterCamera;
            RestoreAll();
            _hidden.Clear();
            _target = null;
        }
        private void LateUpdate()
        {
            // Recover if a camera was disabled or destroyed before PostRender.
            RestoreAll();
        }
        private void RestoreAll()
        {
            while (_scopes.Count != 0) Restore(_scopes.Pop());
            _baseline.Clear();
        }
        private static void Restore(Scope scope)
        {
            foreach (var pair in scope.Saved)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
        }
        private static bool IsArm(Transform bone)
        {
            for (var t = bone; t != null; t = t.parent)
            {
                var n = t.name.ToLowerInvariant();
                if (n.Contains("_arm") || n.Contains("_hand") || n.Contains("_elbo")) return true;
            }
            return false;
        }
        private void Refresh()
        {
            var mode = KoikSettings.FrameKoiPlayerBody.Value;
            var target = KoikGameInterp.CurrentScene == KoikGameInterp.SceneType.ActionScene &&
                KoikSettings.FrameKoiNativeInputEnabled.Value
                ? ActionSceneInterp.actionScene?.Player?.chaCtrl : null;
            if (_target == target && _mode == mode && Time.unscaledTime < _nextRefresh) return;
            _target = target;
            _mode = mode;
            _nextRefresh = Time.unscaledTime + 2f;
            _hidden.Clear();
            if (target == null || mode == KoikSettings.PlayerBodyMode.FullBody) return;
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            bool mixed = false;
            foreach (var renderer in renderers)
            {
                if (mode == KoikSettings.PlayerBodyMode.HideBody)
                {
                    _hidden.Add(renderer);
                    continue;
                }
                if (IsArm(renderer.transform))
                {
                    _hidden.Add(renderer);
                    continue;
                }
                var skin = renderer as SkinnedMeshRenderer;
                if (skin == null) continue;
                bool arm = false, other = false;
                foreach (var bone in skin.bones)
                {
                    if (bone == null) continue;
                    if (IsArm(bone)) arm = true; else other = true;
                }
                if (arm && other) mixed = true;
                else if (arm) _hidden.Add(renderer);
            }
            // Stock bodies and many clothes combine arms/torso in one skinned
            // mesh. Do not edit shared meshes, bone scales or shaders to hide it.
            if (mixed || (mode == KoikSettings.PlayerBodyMode.HideArms && _hidden.Count == 0))
            {
                _hidden.Clear();
                _hidden.AddRange(renderers);
                if (!_warnedMixed)
                {
                    _warnedMixed = true;
                    VRLog.Info("FrameKoi body: arms share body renderers; using headset-only HideBody fallback.");
                }
            }
        }
        private void BeforeCamera(UnityEngine.Camera camera)
        {
            if (_scopes.Count == 0) Refresh();
            // Spectator/mirror cameras retain the original body. The attached
            // camera is VRGIN's eye camera in both supported game targets.
            var headset = camera == GetComponent<UnityEngine.Camera>() && camera.stereoEnabled;
            if (!headset && _scopes.Count == 0) return;
            if (_scopes.Count == 0)
            {
                _baseline.Clear();
                foreach (var renderer in _hidden)
                    if (renderer != null) _baseline[renderer] = renderer.enabled;
            }
            var scope = new Scope { Camera = camera };
            foreach (var pair in _baseline)
            {
                if (pair.Key == null) continue;
                scope.Saved[pair.Key] = pair.Key.enabled;
                pair.Key.enabled = headset ? false : pair.Value;
            }
            _scopes.Push(scope);
        }
        private void AfterCamera(UnityEngine.Camera camera)
        {
            if (_scopes.Count == 0 || _scopes.Peek().Camera != camera) return;
            Restore(_scopes.Pop());
            if (_scopes.Count == 0) _baseline.Clear();
        }
    }
}
