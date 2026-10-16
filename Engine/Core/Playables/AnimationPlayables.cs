// Animation playables over the managed PlayableGraph (PlayableRuntime): AnimationClipPlayable samples its clip at the node's
// local time, AnimationMixerPlayable blends its inputs by input weight (weights below 1 fall back to the default values, as in
// Unity), AnimationLayerMixerPlayable stacks layers like the Animator does (override / additive, AvatarMask), and the
// Timeline-internal offset / motion / remove-scale playables pass their inputs through (the offset moves the root).
// An AnimationPlayableOutput writes the pose to its target Animator's hierarchy. When that Animator also runs a controller
// in Play mode the write waits until the controller has been evaluated (AnimationSystem.Tick), so the graph layers on top of
// it like a later output in Unity; otherwise it is written as soon as the graph is evaluated (edit-mode Evaluate, Timeline).
// Every type here is listed as "Type: *" in config/stubs.manual.cfg: all its members come from this file.
using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity;
using MiniUnity.Anim;
using UnityEngine.Playables;
using Object = UnityEngine.Object;
using RT = MiniUnity.PlayableRuntime;

namespace UnityEngine.Animations
{
    public partial struct AnimationClipPlayable
    {
        PlayableHandle m_Handle;
        public static AnimationClipPlayable Create(PlayableGraph graph, AnimationClip clip)
        {
            var h = RT.CreateNode(graph, typeof(AnimationClipPlayable));
            if (RT.N(h) is { } n)
            {
                n.extra = new PlayableAnimation.ClipNode { clip = clip, data = clip != null ? ClipData.Of(clip) : null };
                if (clip != null) n.duration = clip.length;
            }
            return new AnimationClipPlayable(h);
        }
        internal AnimationClipPlayable(PlayableHandle handle) { m_Handle = handle; }
        PlayableAnimation.ClipNode D => RT.N(m_Handle)?.extra as PlayableAnimation.ClipNode ?? new PlayableAnimation.ClipNode();
        public PlayableHandle GetHandle() => m_Handle;
        public static implicit operator Playable(AnimationClipPlayable playable) => new Playable(playable.m_Handle);
        public static explicit operator AnimationClipPlayable(Playable playable) => new AnimationClipPlayable(playable.GetHandle());
        public bool Equals(AnimationClipPlayable other) => m_Handle == other.m_Handle;
        public AnimationClip GetAnimationClip() => D.clip;
        public bool GetApplyFootIK() => D.footIK;
        public void SetApplyFootIK(bool value) => D.footIK = value;
        public bool GetApplyPlayableIK() => D.playableIK;
        public void SetApplyPlayableIK(bool value) => D.playableIK = value;
        internal bool GetRemoveStartOffset() => D.removeStartOffset;
        internal void SetRemoveStartOffset(bool value) => D.removeStartOffset = value;
        internal bool GetOverrideLoopTime() => D.overrideLoop;
        internal void SetOverrideLoopTime(bool value) => D.overrideLoop = value;
        internal bool GetLoopTime() => D.loop;
        internal void SetLoopTime(bool value) => D.loop = value;
        internal float GetSampleRate() => D.sampleRate;
        internal void SetSampleRate(float value) => D.sampleRate = value;
    }

    public partial struct AnimationMixerPlayable
    {
        PlayableHandle m_Handle;
        public static AnimationMixerPlayable Create(PlayableGraph graph, int inputCount = 0) => Create(graph, inputCount, false);
        public static AnimationMixerPlayable Create(PlayableGraph graph, int inputCount, bool normalizeWeights)
        {
            var h = RT.CreateNode(graph, typeof(AnimationMixerPlayable), inputCount);
            if (RT.N(h) is { } n) n.extra = normalizeWeights;
            return new AnimationMixerPlayable(h);
        }
        internal AnimationMixerPlayable(PlayableHandle handle) { m_Handle = handle; }
        public PlayableHandle GetHandle() => m_Handle;
        public static implicit operator Playable(AnimationMixerPlayable playable) => new Playable(playable.m_Handle);
        public static explicit operator AnimationMixerPlayable(Playable playable) => new AnimationMixerPlayable(playable.GetHandle());
        public bool Equals(AnimationMixerPlayable other) => m_Handle == other.m_Handle;
        public static AnimationMixerPlayable Null => default;
    }

    public partial struct AnimationLayerMixerPlayable
    {
        PlayableHandle m_Handle;
        public static AnimationLayerMixerPlayable Create(PlayableGraph graph, int inputCount = 0) => Create(graph, inputCount, true);
        public static AnimationLayerMixerPlayable Create(PlayableGraph graph, int inputCount, bool singleLayerOptimization)
        {
            var h = RT.CreateNode(graph, typeof(AnimationLayerMixerPlayable), inputCount);
            if (RT.N(h) is { } n) n.extra = new PlayableAnimation.LayerNode();
            return new AnimationLayerMixerPlayable(h);
        }
        internal AnimationLayerMixerPlayable(PlayableHandle handle) { m_Handle = handle; }
        PlayableAnimation.LayerNode D => RT.N(m_Handle)?.extra as PlayableAnimation.LayerNode ?? new PlayableAnimation.LayerNode();
        public PlayableHandle GetHandle() => m_Handle;
        public static implicit operator Playable(AnimationLayerMixerPlayable playable) => new Playable(playable.m_Handle);
        public static explicit operator AnimationLayerMixerPlayable(Playable playable) => new AnimationLayerMixerPlayable(playable.GetHandle());
        public bool Equals(AnimationLayerMixerPlayable other) => m_Handle == other.m_Handle;
        void CheckLayer(uint layerIndex)
        {
            int count = RT.N(m_Handle)?.inputs.Count ?? 0;
            if (layerIndex >= count) throw new ArgumentOutOfRangeException(nameof(layerIndex), $"layerIndex {layerIndex} must be in the range of 0 to {count - 1}.");
        }
        public bool IsLayerAdditive(uint layerIndex) { CheckLayer(layerIndex); return D.additive.Contains((int)layerIndex); }
        public void SetLayerAdditive(uint layerIndex, bool value)
        {
            CheckLayer(layerIndex);
            if (value) D.additive.Add((int)layerIndex); else D.additive.Remove((int)layerIndex);
        }
        public void SetLayerMaskFromAvatarMask(uint layerIndex, AvatarMask mask)
        {
            CheckLayer(layerIndex);
            if (mask == null) throw new ArgumentNullException(nameof(mask));
            D.masks[(int)layerIndex] = mask;
        }
        public static AnimationLayerMixerPlayable Null => default;
    }

    internal partial struct AnimationOffsetPlayable
    {
        PlayableHandle m_Handle;
        public static AnimationOffsetPlayable Create(PlayableGraph graph, Vector3 position, Quaternion rotation, int inputCount)
        {
            var h = RT.CreateNode(graph, typeof(AnimationOffsetPlayable), inputCount);
            if (RT.N(h) is { } n) n.extra = new PlayableAnimation.OffsetNode { position = position, rotation = rotation };
            return new AnimationOffsetPlayable(h);
        }
        internal AnimationOffsetPlayable(PlayableHandle handle) { m_Handle = handle; }
        PlayableAnimation.OffsetNode D => RT.N(m_Handle)?.extra as PlayableAnimation.OffsetNode ?? new PlayableAnimation.OffsetNode();
        public PlayableHandle GetHandle() => m_Handle;
        public static implicit operator Playable(AnimationOffsetPlayable playable) => new Playable(playable.m_Handle);
        public static explicit operator AnimationOffsetPlayable(Playable playable) => new AnimationOffsetPlayable(playable.GetHandle());
        public bool Equals(AnimationOffsetPlayable other) => m_Handle == other.m_Handle;
        public Vector3 GetPosition() => D.position;
        public void SetPosition(Vector3 value) => D.position = value;
        public Quaternion GetRotation() => D.rotation;
        public void SetRotation(Quaternion value) => D.rotation = value;
        public static AnimationOffsetPlayable Null => default;
    }

    internal partial struct AnimationMotionXToDeltaPlayable
    {
        PlayableHandle m_Handle;
        public static AnimationMotionXToDeltaPlayable Create(PlayableGraph graph)
        {
            var h = RT.CreateNode(graph, typeof(AnimationMotionXToDeltaPlayable), 1);
            if (RT.N(h) is { } n) n.extra = false;
            return new AnimationMotionXToDeltaPlayable(h);
        }
        internal AnimationMotionXToDeltaPlayable(PlayableHandle handle) { m_Handle = handle; }
        public PlayableHandle GetHandle() => m_Handle;
        public static implicit operator Playable(AnimationMotionXToDeltaPlayable playable) => new Playable(playable.m_Handle);
        public static explicit operator AnimationMotionXToDeltaPlayable(Playable playable) => new AnimationMotionXToDeltaPlayable(playable.GetHandle());
        public bool Equals(AnimationMotionXToDeltaPlayable other) => m_Handle == other.m_Handle;
        public bool IsAbsoluteMotion() => RT.N(m_Handle)?.extra is true;
        public void SetAbsoluteMotion(bool value) { if (RT.N(m_Handle) is { } n) n.extra = value; }
        public static AnimationMotionXToDeltaPlayable Null => default;
    }

    internal partial struct AnimationRemoveScalePlayable
    {
        PlayableHandle m_Handle;
        public static AnimationRemoveScalePlayable Create(PlayableGraph graph, int inputCount) => new AnimationRemoveScalePlayable(RT.CreateNode(graph, typeof(AnimationRemoveScalePlayable), inputCount));
        internal AnimationRemoveScalePlayable(PlayableHandle handle) { m_Handle = handle; }
        public PlayableHandle GetHandle() => m_Handle;
        public static implicit operator Playable(AnimationRemoveScalePlayable playable) => new Playable(playable.m_Handle);
        public static explicit operator AnimationRemoveScalePlayable(Playable playable) => new AnimationRemoveScalePlayable(playable.GetHandle());
        public bool Equals(AnimationRemoveScalePlayable other) => m_Handle == other.m_Handle;
        public static AnimationRemoveScalePlayable Null => default;
    }

    public partial struct AnimationPlayableOutput
    {
        PlayableOutputHandle m_Handle;
        public static AnimationPlayableOutput Create(PlayableGraph graph, string name, Animator target)
        {
            var o = new AnimationPlayableOutput(RT.CreateOutput(graph, typeof(AnimationPlayableOutput), name));
            o.SetTarget(target);
            return o;
        }
        internal AnimationPlayableOutput(PlayableOutputHandle handle) { m_Handle = handle; }
        public PlayableOutputHandle GetHandle() => m_Handle;
        public static implicit operator PlayableOutput(AnimationPlayableOutput output) => new PlayableOutput(output.m_Handle);
        public static explicit operator AnimationPlayableOutput(PlayableOutput output) => new AnimationPlayableOutput(output.GetHandle());
        public Animator GetTarget() => PlayableAnimation.TargetOf(m_Handle.GetUserData());
        public void SetTarget(Animator value) => m_Handle.SetUserData(value);
        public static AnimationPlayableOutput Null => default;
    }

    public static partial class AnimationPlayableBinding
    {
        public static PlayableBinding Create(string name, Object key)
            => PlayableBinding.CreateInternal(name, key, typeof(Animator), (g, n) => AnimationPlayableOutput.Create(g, n, null));
    }

    internal static partial class AnimationPlayableGraphExtensions
    {
        internal static void SyncUpdateAndTimeMode(this PlayableGraph graph, Animator animator)
        {
            if (animator == null) return;
            graph.SetTimeUpdateMode(animator.updateMode == AnimatorUpdateMode.UnscaledTime ? DirectorUpdateMode.UnscaledGameTime : DirectorUpdateMode.GameTime);
        }
        internal static void DestroyOutput(this PlayableGraph graph, PlayableOutputHandle handle) => graph.DestroyOutput(new PlayableOutput(handle));
    }
}

namespace UnityEngine.Playables
{
    using UnityEngine.Animations;

    public static partial class AnimationPlayableUtilities
    {
        public static void Play(Animator animator, Playable playable, PlayableGraph graph)
        {
            var output = AnimationPlayableOutput.Create(graph, "AnimationClip", animator);
            output.SetSourcePlayable(playable, 0);
            graph.SyncUpdateAndTimeMode(animator);
            graph.Play();
        }
        public static AnimationClipPlayable PlayClip(Animator animator, AnimationClip clip, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create();
            var p = AnimationClipPlayable.Create(graph, clip);
            Play(animator, p, graph);
            return p;
        }
        public static AnimationMixerPlayable PlayMixer(Animator animator, int inputCount, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create();
            var p = AnimationMixerPlayable.Create(graph, inputCount);
            Play(animator, p, graph);
            return p;
        }
        public static AnimationLayerMixerPlayable PlayLayerMixer(Animator animator, int inputCount, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create();
            var p = AnimationLayerMixerPlayable.Create(graph, inputCount);
            Play(animator, p, graph);
            return p;
        }
    }
}

namespace MiniUnity.Anim
{
    using UnityEngine;
    using UnityEngine.Animations;

    public static class PlayableAnimation
    {
        public sealed class ClipNode
        {
            public AnimationClip clip; public ClipData data;
            public bool footIK = true, playableIK, removeStartOffset = true, overrideLoop, loop;
            public float sampleRate;
        }
        public sealed class LayerNode { public readonly HashSet<int> additive = new(); public readonly Dictionary<int, AvatarMask> masks = new(); }
        public sealed class OffsetNode { public Vector3 position; public Quaternion rotation = Quaternion.identity; }

        sealed class OutputState { public Animator animator; public PoseWriter writer; public readonly HashSet<ClipData> bound = new(); }
        sealed class Ctx { public PoseWriter W; public bool Fire; public List<(Animator a, AnimationClip clip, AnimationEvent ev, float w)> Events; public Animator A; }

        static readonly List<(OutputState st, Dictionary<string, float> f, Dictionary<string, Object> r, float weight, Ctx ctx)> s_Pending = new();

        public static void Reset() => s_Pending.Clear();

        public static Animator TargetOf(Object o) => o switch
        {
            Animator a => a,
            GameObject go => go.GetComponent<Animator>(),
            Component c => c.GetComponent<Animator>(),
            _ => null,
        };

        const string RootPos = "|4||m_LocalPosition.", RootRot = "|4||m_LocalRotation.", RootScale = "|4||m_LocalScale.";

        /// <summary>Called by PlayableRuntime.Evaluate for every AnimationPlayableOutput after its PrepareFrame / ProcessFrame.</summary>
        public static void Evaluate(PlayableRuntime.Output o, bool seek)
        {
            var a = TargetOf(o.userData);
            if (a == null || a.m_Destroyed || !a.isActiveAndEnabled || o.weight <= 0) return;
            if (o.extra is not OutputState st || st.animator != a) o.extra = st = new OutputState { animator = a, writer = new PoseWriter(a.transform) };
            BindAll(o.source, st, new HashSet<long>());
            var ctx = new Ctx { W = st.writer, Fire = !seek && Play.IsPlaying, Events = new(), A = a };
            var pose = Sample(o.source, o.sourcePort, ctx, false, 1);
            if (pose == null) return;
            var (f, r) = Resolve(pose, st.writer);
            if (Play.IsPlaying && a.runtimeAnimatorController != null) s_Pending.Add((st, f, r, o.weight, ctx));   // after the controller
            else Write(st, f, r, o.weight, ctx);
        }

        /// <summary>Called by AnimationSystem.Tick once the Animators have run: graph outputs layered over a controller.</summary>
        public static void FlushPending()
        {
            if (s_Pending.Count == 0) return;
            var list = s_Pending.ToList(); s_Pending.Clear();
            foreach (var (st, f, r, w, ctx) in list)
                if (st.animator != null && !st.animator.m_Destroyed) Write(st, f, r, w, ctx);
        }

        static void Write(OutputState st, Dictionary<string, float> f, Dictionary<string, Object> r, float weight, Ctx ctx)
        {
            if (weight < 1)
                foreach (var k in f.Keys.ToList())
                    if (st.writer.Bindings.TryGetValue(k, out var bp) && bp.Get != null) f[k] = Mathf.LerpUnclamped(bp.Get(), f[k], weight);
            st.writer.Apply(f, r);
            foreach (var (a, clip, ev, w) in ctx.Events)
            {
                var e = ev.Clone(); e.m_Source = AnimationEventSource.Animator;
                e.m_AnimatorClipInfo = new AnimatorClipInfo { m_ClipRef = clip, m_Weight = w };
                AnimatorRun.Dispatch(a.gameObject, e);
            }
        }

        static void BindAll(PlayableHandle h, OutputState st, HashSet<long> seen)
        {
            var n = PlayableRuntime.N(h);
            if (n == null || !seen.Add((long)h.m_Handle)) return;
            if (n.extra is ClipNode { data: { } d } && st.bound.Add(d)) st.writer.Bind(d);
            foreach (var (src, _) in n.inputs) BindAll(src, st, seen);
        }

        // ---------------------------------------------------------------- sampling: (weighted sum, weight) per bound value
        static Pose Sample(PlayableHandle h, int port, Ctx ctx, bool reference, float effectiveWeight)
        {
            var n = PlayableRuntime.N(h);
            if (n == null) return null;
            if (n.type == typeof(AnimationClipPlayable)) return SampleClip(n, ctx, reference, effectiveWeight);
            if (n.type == typeof(AnimationLayerMixerPlayable)) return SampleLayers(n, ctx, reference, effectiveWeight);
            var pose = Mix(n, port, ctx, reference, effectiveWeight);
            if (pose == null) return null;
            if (n.type == typeof(AnimationOffsetPlayable) && n.extra is OffsetNode off) Offset(pose, off);
            else if (n.type == typeof(AnimationRemoveScalePlayable))
                foreach (var c in "xyz") pose.Floats.Remove(RootScale + c);
            return pose;
        }

        static Pose SampleClip(PlayableRuntime.Node n, Ctx ctx, bool reference, float effectiveWeight)
        {
            if (n.extra is not ClipNode { data: { } d } c) return null;
            bool loop = c.overrideLoop ? c.loop : d.Loop;
            float len = d.Length, t = (float)n.time;
            float local = reference || len <= 0 ? 0 : loop ? t - MathF.Floor(t / len) * len : Math.Clamp(t, 0, len);
            var pose = new Pose();
            pose.Add(d, d.StartTime + local, 1);
            if (!reference && ctx.Fire && effectiveWeight > 0 && d.Events.Count > 0 && len > 0 && n.time > n.prevTime)
            {
                var dl = new ClipData { Length = len, StartTime = d.StartTime, Loop = loop };
                dl.Events.AddRange(d.Events);
                foreach (var ev in AnimatorRun.EventsBetween(dl, (float)n.prevTime / len, t / len, n.prevTime <= 0))
                    ctx.Events.Add((ctx.A, c.clip, ev, effectiveWeight));
            }
            return pose;
        }

        /// <summary>Weighted sum of the inputs (a passthrough playable forwards the one input of its port).</summary>
        static Pose Mix(PlayableRuntime.Node n, int port, Ctx ctx, bool reference, float effectiveWeight)
        {
            if (n.traversal == PlayableTraversalMode.Passthrough && n.type != typeof(AnimationMixerPlayable))
                return port >= 0 && port < n.inputs.Count ? Sample(n.inputs[port].src, 0, ctx, reference, effectiveWeight) : null;
            float total = 0;
            foreach (var (_, w) in n.inputs) total += Math.Max(0, w);
            bool normalize = n.extra is true && n.type == typeof(AnimationMixerPlayable) && total > 0;
            Pose res = null;
            foreach (var (src, w0) in n.inputs)
            {
                float w = normalize ? w0 / total : w0;
                if (w <= 0) continue;
                var p = Sample(src, 0, ctx, reference, effectiveWeight * w);
                if (p == null) continue;
                res ??= new Pose();
                Accumulate(res, p, w);
            }
            return res;
        }

        static void Accumulate(Pose dst, Pose src, float w)
        {
            var flip = new HashSet<string>();
            foreach (var k in src.Floats.Keys)
            {
                if (!Pose.IsRotation(k, out var g, out var ci) || ci != 3 || !dst.Floats.ContainsKey(g + "w")) continue;
                float dot = 0;
                foreach (var c in "xyzw") dot += (dst.Floats.TryGetValue(g + c, out var x) ? x.sum : 0) * (src.Floats.TryGetValue(g + c, out var y) ? y.sum : 0);
                if (dot < 0) flip.Add(g);
            }
            foreach (var (k, (sum, sw)) in src.Floats)
            {
                float s = Pose.IsRotation(k, out var g, out _) && flip.Contains(g) ? -sum : sum;
                dst.Floats.TryGetValue(k, out var acc);
                dst.Floats[k] = (acc.sum + s * w, acc.w + sw * w);
            }
            foreach (var (k, (v, rw)) in src.Refs)
                if (!dst.Refs.TryGetValue(k, out var cur) || rw * w > cur.w) dst.Refs[k] = (v, rw * w);
        }

        /// <summary>Values of a pose: full weight → the weighted mean, less → the rest of the weight goes to the default value.</summary>
        static (Dictionary<string, float> f, Dictionary<string, Object> r) Resolve(Pose p, PoseWriter w)
        {
            var f = new Dictionary<string, float>();
            var groups = new HashSet<string>();
            foreach (var (k, (sum, wt)) in p.Floats)
            {
                if (Pose.IsRotation(k, out var g, out _)) { groups.Add(g); continue; }
                float def = w.Bindings.TryGetValue(k, out var bp) ? bp.Default : 0;
                f[k] = wt >= 1 ? sum / wt : sum + (1 - wt) * def;
            }
            foreach (var g in groups)
            {
                float Def(char c) => w.Bindings.TryGetValue(g + c, out var bp) ? bp.Default : c == 'w' ? 1 : 0;
                float dot = 0;
                foreach (var c in "xyzw") dot += (p.Floats.TryGetValue(g + c, out var a) ? a.sum : 0) * Def(c);
                float sign = dot < 0 ? -1 : 1;
                foreach (var c in "xyzw")
                {
                    if (!p.Floats.TryGetValue(g + c, out var a)) continue;
                    f[g + c] = a.w >= 1 ? a.sum / a.w : a.sum + (1 - a.w) * sign * Def(c);
                }
            }
            var r = new Dictionary<string, Object>();
            foreach (var (k, (v, _)) in p.Refs) r[k] = v;
            return (f, r);
        }

        /// <summary>Layers in order: the first blends from the default values by its weight, the next ones override (lerp) or add
        /// (difference to the layer's reference pose, its clips at time 0) by theirs, restricted to their AvatarMask.</summary>
        static Pose SampleLayers(PlayableRuntime.Node n, Ctx ctx, bool reference, float effectiveWeight)
        {
            var info = n.extra as LayerNode ?? new LayerNode();
            var acc = new Dictionary<string, float>(); var refs = new Dictionary<string, Object>();
            var root = ctx.W.Root;
            bool any = false;
            for (int i = 0; i < n.inputs.Count; i++)
            {
                var (src, w) = n.inputs[i];
                if (w <= 0) continue;
                var p = Sample(src, 0, ctx, reference, effectiveWeight * w);
                if (p == null) continue;
                any = true;
                var (vals, rv) = Resolve(p, ctx.W);
                bool additive = i > 0 && info.additive.Contains(i);
                Dictionary<string, float> refVals = null;
                if (additive) { var rp = Sample(src, 0, ctx, true, 0); refVals = rp != null ? Resolve(rp, ctx.W).f : new(); }
                info.masks.TryGetValue(i, out var mask);
                bool Allowed(string k) => mask == null || !ctx.W.Bindings.TryGetValue(k, out var bp) || mask.Allows(root, bp.Target ?? bp.Go?.transform);
                float Def(string k) => ctx.W.Bindings.TryGetValue(k, out var bp) ? bp.Default : 0;
                var groups = new HashSet<string>();
                foreach (var (k, v) in vals)
                {
                    if (!Allowed(k)) continue;
                    if (Pose.IsRotation(k, out var g, out _) && vals.ContainsKey(g + "x") && vals.ContainsKey(g + "y") && vals.ContainsKey(g + "z") && vals.ContainsKey(g + "w")) { groups.Add(g); continue; }
                    float b = acc.TryGetValue(k, out var bv) ? bv : Def(k);
                    acc[k] = additive ? b + (v - (refVals.TryGetValue(k, out var rv0) ? rv0 : v)) * w : Mathf.LerpUnclamped(b, v, w);
                }
                foreach (var g in groups)
                {
                    var q = Q(vals, g);
                    var baseQ = acc.ContainsKey(g + "w") ? Q(acc, g) : new Quaternion(Def(g + "x"), Def(g + "y"), Def(g + "z"), ctx.W.Bindings.ContainsKey(g + "w") ? Def(g + "w") : 1);
                    Quaternion res;
                    if (additive)
                    {
                        var refQ = refVals.ContainsKey(g + "w") ? Q(refVals, g) : q;
                        res = Norm(baseQ) * Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(Norm(refQ)) * Norm(q), w);
                    }
                    else
                    {
                        if (baseQ.x * q.x + baseQ.y * q.y + baseQ.z * q.z + baseQ.w * q.w < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        res = new Quaternion(Mathf.LerpUnclamped(baseQ.x, q.x, w), Mathf.LerpUnclamped(baseQ.y, q.y, w), Mathf.LerpUnclamped(baseQ.z, q.z, w), Mathf.LerpUnclamped(baseQ.w, q.w, w));
                    }
                    acc[g + "x"] = res.x; acc[g + "y"] = res.y; acc[g + "z"] = res.z; acc[g + "w"] = res.w;
                }
                foreach (var (k, v) in rv) if (Allowed(k) && (w >= 0.5f || !refs.ContainsKey(k))) refs[k] = v;
            }
            if (!any) return null;
            var pose = new Pose();
            foreach (var (k, v) in acc) pose.Floats[k] = (v, 1);
            foreach (var (k, v) in refs) pose.Refs[k] = (v, 1);
            return pose;
        }

        static Quaternion Q(Dictionary<string, float> d, string g) => new(d[g + "x"], d[g + "y"], d[g + "z"], d[g + "w"]);

        static Quaternion Norm(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m > 1e-8f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
        }

        /// <summary>Track / clip offsets of Timeline: the root's animated position and rotation are moved into the offset's space.</summary>
        static void Offset(Pose pose, OffsetNode off)
        {
            if (off.position == Vector3.zero && off.rotation == Quaternion.identity) return;
            float Get(string k) => pose.Floats.TryGetValue(k, out var a) && a.w > 0 ? a.sum / a.w : 0;
            void Set(string k, float v) { var w = pose.Floats[k].w; pose.Floats[k] = (v * w, w); }
            if (pose.Floats.ContainsKey(RootPos + "x") && pose.Floats.ContainsKey(RootPos + "y") && pose.Floats.ContainsKey(RootPos + "z"))
            {
                var p = off.rotation * new Vector3(Get(RootPos + "x"), Get(RootPos + "y"), Get(RootPos + "z")) + off.position;
                Set(RootPos + "x", p.x); Set(RootPos + "y", p.y); Set(RootPos + "z", p.z);
            }
            if (pose.Floats.ContainsKey(RootRot + "w"))
            {
                var q = off.rotation * Norm(new Quaternion(Get(RootRot + "x"), Get(RootRot + "y"), Get(RootRot + "z"), Get(RootRot + "w")));
                foreach (var (c, v) in new[] { ('x', q.x), ('y', q.y), ('z', q.z), ('w', q.w) }) if (pose.Floats.ContainsKey(RootRot + c)) Set(RootRot + c, v);
            }
        }
    }
}
