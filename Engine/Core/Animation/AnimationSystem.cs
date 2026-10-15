// Per-frame driver of MiniUnity's animation emulation: Animators (Normal / UnscaledTime after Update and coroutines, before
// LateUpdate; AnimatePhysics in the fixed step before physics) and legacy Animation components.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity.Anim
{
    public static class AnimationSystem
    {
        static readonly HashSet<Animator> enabledLast = new();

        public static void Reset() => enabledLast.Clear();

        public static PoseWriter Sampler(GameObject go, ClipData d) { var w = new PoseWriter(go.transform); w.Bind(d); return w; }

        public static void Tick(float dt, float unscaledDt, bool fixedPhase)
        {
            var seen = new HashSet<Animator>();
            foreach (var go in Play.SceneObjects().ToList())
            {
                if (go.m_Destroyed || !HasAnimation(go)) continue;
                foreach (var c in go.m_Components.ToList())
                {
                    if (c is Animator a && !a.m_Destroyed)
                    {
                        bool on = go.activeInHierarchy && a.enabled && a.runtimeAnimatorController != null;
                        if (!on) continue;
                        seen.Add(a);
                        var mode = a.updateMode;
                        if (fixedPhase != (mode == AnimatorUpdateMode.AnimatePhysics)) continue;
                        var cull = a.cullingMode == AnimatorCullingMode.AlwaysAnimate || AnyVisible(a) ? AnimatorCullingMode.AlwaysAnimate : a.cullingMode;
                        if (cull == AnimatorCullingMode.CullCompletely) continue;   // the state machine stops until a renderer is seen again
                        var run = a.Run;
                        if (run != null) run.CullTransforms = cull == AnimatorCullingMode.CullUpdateTransforms;
                        try { run?.Update(mode == AnimatorUpdateMode.UnscaledTime ? unscaledDt : dt); }
                        catch (Exception e) { Play.Report(e, "Animator.Update " + go.name); }
                    }
                    else if (c is Animation an && !an.m_Destroyed && go.activeInHierarchy && an.enabled && fixedPhase == an.animatePhysics)
                    {
                        try { EvaluateLegacy(an, dt, false); }
                        catch (Exception e) { Play.Report(e, "Animation.Update " + go.name); }
                    }
                }
            }
            if (fixedPhase) return;
            // Animators disabled since the last frame: Write Defaults on disable / reset unless keepAnimatorStateOnDisable
            foreach (var a in enabledLast.ToList())
            {
                if (seen.Contains(a)) continue;
                enabledLast.Remove(a);
                if (a == null || a.m_Destroyed) continue;
                if (a.writeDefaultValuesOnDisable) a.m_Run?.WriteDefaults();
                if (!a.keepAnimatorStateOnDisable) a.m_Run = null;
            }
            foreach (var a in seen) enabledLast.Add(a);
        }

        /// <summary>Culling modes look at every renderer under the animator.</summary>
        static bool AnyVisible(Animator a)
        {
            foreach (var r in a.GetComponentsInChildren<Renderer>()) if (r.isVisible) return true;
            return false;
        }

        static bool HasAnimation(GameObject go)
        {
            foreach (var c in go.m_Components) if (c is Animator or Animation) return true;
            return false;
        }

        // one Pose and its two output dictionaries reused by EvaluateLegacy (a nested call, e.g. Sample from a binding, makes new ones)
        static Pose sparePose;
        static Dictionary<string, float> spareFloats;
        static Dictionary<string, Object> spareRefs;

        // ------------------------------------------------------------------ legacy Animation component
        public static void EvaluateLegacy(Animation an, float dt, bool sampleOnly)
        {
            an.InitStates();
            if (!an.m_AutoPlayed) { an.m_AutoPlayed = true; if (an.playAutomatically && an.clip != null && !an.isPlaying && !sampleOnly) an.Play(); }
            if (an.m_Sampler == null)
            {
                an.m_Sampler = new PoseWriter(an.transform);
                foreach (var st in an.m_States) if (st.clip != null) an.m_Sampler.Bind(ClipData.Of(st.clip));
            }
            var pose = sparePose ?? new Pose(); sparePose = null;
            var fired = new List<(AnimationState st, AnimationEvent ev)>();
            foreach (var st in an.m_States.ToList())
            {
                if (!st.m_Enabled || st.clip == null) continue;
                var d = ClipData.Of(st.clip);
                float len = d.Length;
                if (!sampleOnly)
                {
                    st.m_PrevTime = st.m_Time; st.m_Time += dt * st.m_Speed;
                    if (st.m_FadeSpeed > 0)
                    {
                        float step = st.m_FadeSpeed * dt;
                        st.m_Weight = st.m_Weight < st.m_TargetWeight ? Math.Min(st.m_TargetWeight, st.m_Weight + step) : Math.Max(st.m_TargetWeight, st.m_Weight - step);
                        if (st.m_Weight == st.m_TargetWeight) st.m_FadeSpeed = 0;
                    }
                }
                var wrap = st.m_WrapMode == WrapMode.Default ? (d.WrapMode == WrapMode.Default ? WrapMode.Once : d.WrapMode) : st.m_WrapMode;
                float t = st.m_Time; bool finished = false;
                if (len > 0)
                    switch (wrap)
                    {
                        case WrapMode.Loop: t = st.m_Time - MathF.Floor(st.m_Time / len) * len; break;
                        case WrapMode.PingPong: { float m = st.m_Time - MathF.Floor(st.m_Time / (2 * len)) * 2 * len; t = m > len ? 2 * len - m : m; break; }
                        case WrapMode.ClampForever: t = Math.Clamp(st.m_Time, 0, len); break;
                        default: if (st.m_Time >= len || st.m_Time < 0) { finished = true; t = Math.Clamp(st.m_Time, 0, len); } break;
                    }
                if (st.m_Weight > 0) pose.Add(d, d.StartTime + t, st.m_Weight);
                if (!sampleOnly && d.Events.Count > 0 && len > 0)
                {
                    var dl = new ClipData { Length = len, StartTime = d.StartTime, Loop = wrap == WrapMode.Loop };
                    dl.Events.AddRange(d.Events);
                    foreach (var ev in AnimatorRun.EventsBetween(dl, st.m_PrevTime / len, st.m_Time / len, st.m_Fresh)) fired.Add((st, ev));
                }
                st.m_Fresh = false;
                if (finished && !sampleOnly) { st.m_Enabled = false; st.m_Time = 0; st.m_Weight = 0; }
                if (st.m_StopWhenFadedOut && st.m_Weight <= 0) { st.m_Enabled = false; st.m_Time = 0; st.m_StopWhenFadedOut = false; }
            }
            var floats = spareFloats ?? new Dictionary<string, float>(); spareFloats = null;
            var refs = spareRefs ?? new Dictionary<string, Object>(); spareRefs = null;
            foreach (var k in pose.Floats) floats.Add(k.Key, k.Value.sum / Math.Max(k.Value.w, 1e-6f) * Math.Min(1, k.Value.w) + DefaultBlend(an.m_Sampler, k.Key, k.Value.w));
            foreach (var k in pose.Refs) refs.Add(k.Key, k.Value.value);
            pose.Clear(); sparePose = pose;
            an.m_Sampler.Apply(floats, refs);
            floats.Clear(); refs.Clear(); spareFloats = floats; spareRefs = refs;
            foreach (var (st, ev) in fired)
            {
                var e = ev.Clone(); e.m_Source = AnimationEventSource.Legacy; e.m_StateSender = st;
                AnimatorRun.Dispatch(an.gameObject, e);
            }
            // queued animations start when the layer has nothing playing
            if (!sampleOnly && an.m_Queue.Count > 0 && !an.m_States.Any(s => s.m_Enabled && s.m_TargetWeight > 0))
            {
                var (name, fade, mode) = an.m_Queue[0]; an.m_Queue.RemoveAt(0);
                if (fade > 0) an.CrossFade(name, fade, mode); else an.Play(name, mode);
            }
        }

        // total legacy weight below 1 blends with the current (default) value
        static float DefaultBlend(PoseWriter w, string key, float weight)
        {
            if (weight >= 1 || !w.Bindings.TryGetValue(key, out var bp) || bp.Get == null) return 0;
            return bp.Get() * (1 - weight);
        }
    }
}
