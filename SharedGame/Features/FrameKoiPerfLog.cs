using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRGIN.Core;
using KK_VR.Controls;
using KK_VR.Interpreters;
using KK_VR.Settings;

namespace KK_VR.Features
{
    /// <summary>
    /// Frame-time diagnostics for "choppy" reports. SteamVR's per-session
    /// reprojection totals hide uneven frames; this records the distribution,
    /// and logs each hitch with what happened that frame (GC, allocation,
    /// scene, loading) so the cause can be attributed. Allocation-free per frame.
    /// </summary>
    internal sealed class FrameKoiPerfLog : MonoBehaviour
    {
        private const float SummarySeconds = 30f;
        private const float HitchMs = 25f;
        private const int MaxHitchLines = 300;

        private readonly float[] _frames = new float[8192];
        private float[] _sorted = new float[0];
        private int _count;
        private int _loadingFrames;
        private int _over11, _over16, _over25;
        private float _max;
        private int _gcAtStart, _lastGc;
        private long _lastMemory;
        private long _allocated;
        private float _windowStart = -1f;
        private bool _skipNext = true;
        private int _hitchLines;
        private bool _reportedEnvironment;

        private void OnEnable() => _skipNext = true;

        private void Update()
        {
            if (!KoikSettings.FrameKoiPerfLog.Value) { _windowStart = -1f; return; }
            if (!_reportedEnvironment) ReportEnvironment();

            var now = Time.unscaledTime;
            var gc = GC.CollectionCount(0);
            var memory = GC.GetTotalMemory(false);
            if (_windowStart < 0f) StartWindow(now, gc, memory);
            if (_skipNext)
            {
                // First frame after enabling or after the summary's own scene query.
                _skipNext = false;
                _lastGc = gc; _lastMemory = memory;
                return;
            }

            var ms = Time.unscaledDeltaTime * 1000f;
            var gcThisFrame = gc - _lastGc;
            var allocatedThisFrame = Math.Max(0L, memory - _lastMemory);
            _lastGc = gc; _lastMemory = memory;
            _allocated += allocatedThisFrame;

            if (FrameKoiControlRuntime.SceneLoading)
            {
                _loadingFrames++;
            }
            else
            {
                if (_count < _frames.Length) _frames[_count++] = ms;
                if (ms > 11.1f) _over11++;
                if (ms > 16.7f) _over16++;
                if (ms > HitchMs) _over25++;
                if (ms > _max) _max = ms;
                if (ms > HitchMs && _hitchLines < MaxHitchLines)
                {
                    _hitchLines++;
                    VRLog.Info($"FrameKoi perf: hitch {ms:F1} ms, gc={gcThisFrame}, alloc={allocatedThisFrame / 1024} KB, " +
                        $"scene={KoikGameInterp.CurrentScene}/{SceneManager.GetActiveScene().name}" +
                        (_hitchLines == MaxHitchLines ? " (hitch line limit reached)" : ""));
                }
            }

            if (now - _windowStart >= SummarySeconds) Summarize(now, gc, memory);
        }

        private void StartWindow(float now, int gc, long memory)
        {
            _windowStart = now;
            _gcAtStart = gc;
            _lastGc = gc;
            _lastMemory = memory;
            _allocated = 0;
            _count = 0; _loadingFrames = 0;
            _over11 = _over16 = _over25 = 0;
            _max = 0f;
        }

        private void Summarize(float now, int gc, long memory)
        {
            var seconds = now - _windowStart;
            if (_count > 0)
            {
                if (_sorted.Length < _count) _sorted = new float[_frames.Length];
                Array.Copy(_frames, _sorted, _count);
                Array.Sort(_sorted, 0, _count);
                // One query per window; its own cost is excluded via _skipNext.
                var characters = FindObjectsOfType<ChaControl>().Length;
                VRLog.Info($"FrameKoi perf {seconds:F0}s: {_count / seconds:F1} fps | ms p50 {Percentile(0.5f):F1} " +
                    $"p95 {Percentile(0.95f):F1} p99 {Percentile(0.99f):F1} max {_max:F1} | " +
                    $">11.1ms {_over11}, >16.7ms {_over16}, >25ms {_over25} of {_count} | " +
                    $"GC {gc - _gcAtStart}, alloc {_allocated / seconds / (1024f * 1024f):F2} MB/s | " +
                    $"loading frames {_loadingFrames} | characters {characters} | " +
                    $"scene {KoikGameInterp.CurrentScene}/{SceneManager.GetActiveScene().name}");
                _skipNext = true;
            }
            StartWindow(now, gc, memory);
        }

        private float Percentile(float p) => _sorted[Math.Min(_count - 1, (int)(p * _count))];

        private void ReportEnvironment()
        {
            _reportedEnvironment = true;
#if KK
            var refresh = UnityEngine.VR.VRDevice.refreshRate;
#else
            var refresh = UnityEngine.XR.XRDevice.refreshRate;
#endif
            VRLog.Info($"FrameKoi perf: headset {refresh:F0} Hz (frame budget {1000f / Math.Max(1f, refresh):F1} ms), " +
                $"physics {1f / Time.fixedDeltaTime:F0} Hz, quality level {QualitySettings.GetQualityLevel()}, " +
                $"vSync {QualitySettings.vSyncCount}, plugins {BepInEx.Bootstrap.Chainloader.PluginInfos.Count}");
        }
    }
}
