// Mecanim emulation for one Animator: parameters, per-layer state machine (exit time / conditions / solo-mute / AnyState /
// sub-state machines / interruptions), cross-fades, blend trees (1D, 2D gradient band, direct), layer blending (override /
// additive) with Write Defaults, StateMachineBehaviour callbacks and AnimationEvents. Semantics follow the Unity manual
// (Animation transitions, exit time, interruption sources) and the Animator script API in UnityCsReference.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity.Anim
{
    public sealed class StateRun
    {
        public CState State; public CMotion Motion;
        public float NormalizedTime, PrevNormalizedTime; public bool Fresh = true;
        public float Length;                       // seconds of one loop at speed 1
        public float Speed, SpeedMultiplier = 1;
        public bool Loop;
        public List<(AnimationClip clip, ClipData data, float weight, float normalizedTime, float prevNormalizedTime)> Clips = new();
        public List<(float timeScale, float cycle)> ClipTimeScales = new();
    }

    public sealed class LayerRun
    {
        public CLayer Layer; public int Index; public float Weight;
        public StateRun Current, Next;
        public CTransition Transition; public float TransElapsed, TransDuration; public bool TransAnyState;
        public StateRun Interrupted; public float InterruptedWeight;
        public List<StateRun> EnteredThisFrame = new(), ExitedThisFrame = new();
        public float TransitionProgress => Transition == null || TransDuration <= 0 ? 1 : Math.Clamp(TransElapsed / TransDuration, 0, 1);
    }

    public sealed class AnimatorRun
    {
        public readonly Animator Anim; public readonly CController C;
        public readonly Dictionary<int, CParam> ParamsByHash = new();
        public readonly Dictionary<int, float> Floats = new(); public readonly Dictionary<int, int> Ints = new(); public readonly Dictionary<int, bool> Bools = new();
        public readonly List<LayerRun> Layers = new();
        public readonly PoseWriter Writer;
        public Dictionary<string, BoundProperty> Bindings => Writer.Bindings;
        readonly Dictionary<StateMachineBehaviour, StateMachineBehaviour> behaviourInstances = new();
        public bool Initialized;
        public Vector3 DeltaPosition; public Quaternion DeltaRotation = Quaternion.identity;
        Vector3? lastRootPos; Quaternion? lastRootRot;

        public AnimatorRun(Animator a, CController c)
        {
            Anim = a; C = c;
            foreach (var p in c.Params)
            {
                ParamsByHash[p.Hash] = p;
                Floats[p.Hash] = p.DefFloat; Ints[p.Hash] = p.DefInt; Bools[p.Hash] = p.DefBool && p.Type != AnimatorControllerParameterType.Trigger ? p.DefBool : false;
                if (p.Type == AnimatorControllerParameterType.Bool) Bools[p.Hash] = p.DefBool;
            }
            for (int i = 0; i < c.Layers.Count; i++) Layers.Add(new LayerRun { Layer = c.Layers[i], Index = i, Weight = c.Layers[i].DefaultWeight });
            // bindings: all curves of all clips, default values captured once (Write Defaults)
            Writer = new PoseWriter(a.transform);
            foreach (var clip in C.Clips) Writer.Bind(ClipData.Of(clip));
        }

        public void WriteDefaults() => Writer.WriteDefaults();
        void Apply(Dictionary<string, float> pose, Dictionary<string, Object> refs) => Writer.Apply(pose, refs);

        // ------------------------------------------------------------------ parameters
        public CParam Param(int hash, AnimatorControllerParameterType? type, string nameForLog)
        {
            if (ParamsByHash.TryGetValue(hash, out var p))
            {
                if (type != null && p.Type != type.Value && !(type == AnimatorControllerParameterType.Bool && p.Type == AnimatorControllerParameterType.Trigger))
                {
                    if (Anim.logWarnings) Debug.LogWarning($"Parameter '{nameForLog ?? p.Name}' does not match the type {type}.");
                    return null;
                }
                return p;
            }
            if (Anim.logWarnings) Debug.LogWarning(nameForLog != null ? $"Parameter '{nameForLog}' does not exist." : $"Parameter hash {hash} does not exist.");
            return null;
        }

        float ParamF(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            int h = Animator.StringToHash(name);
            if (!ParamsByHash.TryGetValue(h, out var p)) return 0;
            return p.Type switch { AnimatorControllerParameterType.Float => Floats[h], AnimatorControllerParameterType.Int => Ints[h], _ => Bools[h] ? 1 : 0 };
        }

        bool Condition(CCondition c)
        {
            int h = Animator.StringToHash(c.Param);
            if (!ParamsByHash.TryGetValue(h, out var p)) return false;
            switch (c.Mode)
            {
                case 1: return Bools[h];                                   // If (bool / trigger)
                case 2: return !Bools[h];                                  // IfNot
                case 3: return p.Type == AnimatorControllerParameterType.Int ? Ints[h] > c.Threshold : Floats[h] > c.Threshold;   // Greater
                case 4: return p.Type == AnimatorControllerParameterType.Int ? Ints[h] < c.Threshold : Floats[h] < c.Threshold;   // Less
                case 6: return Ints[h] == (int)c.Threshold;               // Equals
                case 7: return Ints[h] != (int)c.Threshold;               // NotEqual
                default: return false;
            }
        }

        void ConsumeTriggers(CTransition t)
        {
            foreach (var c in t.Conditions)
            {
                int h = Animator.StringToHash(c.Param);
                if (ParamsByHash.TryGetValue(h, out var p) && p.Type == AnimatorControllerParameterType.Trigger) Bools[h] = false;
            }
        }

        // ------------------------------------------------------------------ state runs
        CMotion MotionOf(LayerRun lr, CState s)
        {
            if (lr.Layer.SyncedIndex >= 0) return lr.Layer.SyncedMotions.TryGetValue(s, out var m) ? m : null;
            return s.Motion;
        }

        AnimationClip Override(AnimationClip c) => c != null && C.Overrides.TryGetValue(c, out var o) ? o : c;

        StateRun NewRun(LayerRun lr, CState s, float normalizedTime)
        {
            var r = new StateRun { State = s, Motion = MotionOf(lr, s), NormalizedTime = normalizedTime, PrevNormalizedTime = normalizedTime };
            Refresh(r);
            return r;
        }

        /// <summary>Clip weights, length and speed of a state for the current parameters.</summary>
        void Refresh(StateRun r)
        {
            r.Clips.Clear();
            var clips = new List<(AnimationClip clip, float weight, float timeScale, float cycle)>();
            Collect(r.Motion, 1, 1, 0, clips);
            float len = 0, wsum = 0; bool loop = false;
            foreach (var (clip, w, ts, _) in clips)
            {
                var d = ClipData.Of(clip);
                float l = d.Length / Math.Max(Math.Abs(ts), 1e-6f);
                len += w * l; wsum += w; loop |= d.Loop;
            }
            r.Length = wsum > 0 ? len / wsum : 1;
            if (r.Length <= 0) r.Length = clips.Count > 0 ? 0 : 1;
            r.Loop = loop || clips.Count == 0;
            var s = r.State;
            r.SpeedMultiplier = s.SpeedParamActive ? ParamF(s.SpeedParam) : 1;
            r.Speed = s.Speed;
            foreach (var (clip, w, ts, cyc) in clips) r.Clips.Add((clip, ClipData.Of(clip), w, 0, 0));
            r.ClipTimeScales = clips.Select(c => (c.timeScale, c.cycle)).ToList();
        }

        void Collect(CMotion m, float weight, float timeScale, float cycle, List<(AnimationClip, float, float, float)> res)
        {
            if (m == null || weight <= 0) return;
            if (m.Clip != null) { res.Add((Override(m.Clip), weight, timeScale, cycle)); return; }
            var t = m.Tree; if (t == null || t.Children.Count == 0) return;
            var w = BlendWeights(t);
            for (int i = 0; i < t.Children.Count; i++)
                if (w[i] > 0) Collect(t.Children[i].Motion, weight * w[i], timeScale * t.Children[i].TimeScale, cycle + t.Children[i].CycleOffset, res);
        }

        float[] BlendWeights(CBlendTree t)
        {
            int n = t.Children.Count; var w = new float[n];
            switch (t.Type)
            {
                case 0:   // Simple 1D
                {
                    float p = ParamF(t.Param);
                    var order = Enumerable.Range(0, n).OrderBy(i => t.Children[i].Threshold).ToArray();
                    if (p <= t.Children[order[0]].Threshold) { w[order[0]] = 1; break; }
                    if (p >= t.Children[order[^1]].Threshold) { w[order[^1]] = 1; break; }
                    for (int k = 0; k + 1 < n; k++)
                    {
                        float a = t.Children[order[k]].Threshold, b = t.Children[order[k + 1]].Threshold;
                        if (p >= a && p <= b) { float f = b > a ? (p - a) / (b - a) : 0; w[order[k]] = 1 - f; w[order[k + 1]] = f; break; }
                    }
                    break;
                }
                case 4:   // Direct
                    for (int i = 0; i < n; i++) w[i] = Math.Max(0, ParamF(t.Children[i].DirectParam));
                    return w;
                default:  // 2D: gradient band interpolation (cartesian for Freeform Cartesian, polar for the directional types)
                {
                    var p = new Vector2(ParamF(t.Param), ParamF(t.ParamY));
                    bool polar = t.Type == 1 || t.Type == 2;
                    for (int i = 0; i < n; i++)
                    {
                        var pi = t.Children[i].Position; float h = 1;
                        for (int j = 0; j < n && h > 0; j++)
                        {
                            if (i == j) continue;
                            var pj = t.Children[j].Position;
                            Vector2 vij, vip;
                            if (polar) { vij = PolarDelta(pi, pj); vip = PolarDelta(pi, p, pj); }
                            else { vij = pj - pi; vip = p - pi; }
                            float l2 = vij.sqrMagnitude;
                            if (l2 < 1e-12f) continue;
                            h = Math.Min(h, 1 - Vector2.Dot(vip, vij) / l2);
                        }
                        w[i] = Math.Max(0, h);
                    }
                    break;
                }
            }
            float s = w.Sum();
            if (s > 0) for (int i = 0; i < n; i++) w[i] /= s;
            return w;
        }

        // Johansen's polar gradient band space: (magnitude difference relative to the average, angle difference · 2)
        static Vector2 PolarDelta(Vector2 a, Vector2 b, Vector2? reference = null)
        {
            var r = reference ?? b;
            float ma = a.magnitude, mb = b.magnitude, mr = r.magnitude;
            float avg = (ma + mr) * 0.5f;
            float ang = (ma < 1e-5f || mb < 1e-5f) ? 0 : Vector2.SignedAngle(a, b) * Mathf.Deg2Rad;
            if (ma < 1e-5f && mb >= 1e-5f && reference != null) ang = 0;
            return new Vector2(avg > 1e-5f ? (mb - ma) / avg : 0, ang * 2);
        }

        // ------------------------------------------------------------------ transitions
        static bool ExitTimeCrossed(float prev, float cur, float exit, bool fresh)
        {
            if (fresh) prev -= 1e-5f;
            if (cur < prev) return false;
            if (exit < 1)
            {
                for (float n = MathF.Floor(prev); n <= MathF.Floor(cur); n++) { float t = n + exit; if (t > prev && t <= cur) return true; }
                return false;
            }
            return exit > prev && exit <= cur;
        }

        bool Passes(CTransition t, StateRun from)
        {
            if (t.Mute) return false;
            if (t.HasExitTime && (from == null || !ExitTimeCrossed(from.PrevNormalizedTime, from.NormalizedTime, t.ExitTime, from.Fresh))) return false;
            if (!t.HasExitTime && t.Conditions.Count == 0) return false;   // Unity ignores transitions with no condition and no exit time
            foreach (var c in t.Conditions) if (!Condition(c)) return false;
            return true;
        }

        static IEnumerable<CTransition> SoloFilter(List<CTransition> ts) => ts.Any(t => t.Solo) ? ts.Where(t => t.Solo) : ts;

        CTransition FindTransition(LayerRun lr, StateRun from, bool includeAny, bool includeState)
        {
            if (includeAny)
                foreach (var m in lr.Layer.Root.SelfAndAll())
                    foreach (var t in SoloFilter(m.AnyState))
                    {
                        if (t.Dst == from?.State && !t.CanTransitionToSelf) continue;
                        if (Passes(t, from)) return t;
                    }
            if (includeState && from != null)
                foreach (var t in SoloFilter(from.State.Transitions)) if (Passes(t, from)) return t;
            return null;
        }

        /// <summary>Resolve the state a transition leads to (sub-state machine entry, exit to the parent machine).</summary>
        CState Resolve(LayerRun lr, CTransition t, CState from, List<(CMachine m, bool enter)> machineEvents)
        {
            if (t.Dst != null) return t.Dst;
            if (t.DstMachine != null) return EnterMachine(t.DstMachine, machineEvents);
            if (t.IsExit && from != null)
            {
                var m = from.Machine;
                machineEvents.Add((m, false));
                var parent = m.Parent;
                if (parent != null && parent.MachineTransitions.TryGetValue(m, out var outs))
                    foreach (var o in outs) if (o.Conditions.All(Condition)) { ConsumeTriggers(o); return Resolve(lr, o, from, machineEvents); }
                return EnterMachine(parent ?? m, machineEvents, parent == null);
            }
            return null;
        }

        CState EnterMachine(CMachine m, List<(CMachine m, bool enter)> machineEvents, bool reEnterRoot = false)
        {
            if (!reEnterRoot) machineEvents.Add((m, true));
            foreach (var t in SoloFilter(m.Entry))
                if (!t.Mute && t.Conditions.All(Condition))
                {
                    ConsumeTriggers(t);
                    if (t.Dst != null) return t.Dst;
                    if (t.DstMachine != null) return EnterMachine(t.DstMachine, machineEvents);
                }
            if (m.Default != null) return m.Default;
            foreach (var sub in m.Machines) { var s = EnterMachine(sub, machineEvents); if (s != null) return s; }
            return null;
        }

        void Start(LayerRun lr, CTransition t, CState dst, bool fromAny)
        {
            ConsumeTriggers(t);
            var src = lr.Current;
            var next = NewRun(lr, dst, t.Offset);
            float dur = t.FixedDuration ? t.Duration : t.Duration * (src != null ? SecondsPerLoop(src) : 0);
            if (lr.Transition != null && lr.Next != null)
            {
                // interruption: the blended pose freezes into the interrupted state, the new transition starts from it
                lr.Interrupted = lr.Next; lr.InterruptedWeight = lr.TransitionProgress;
                lr.ExitedThisFrame.Add(lr.Next);
            }
            if (dur <= 1e-6f)
            {
                if (src != null) lr.ExitedThisFrame.Add(src);
                if (lr.Interrupted != null) lr.ExitedThisFrame.Add(lr.Interrupted);
                lr.Current = next; lr.Next = null; lr.Transition = null; lr.Interrupted = null;
            }
            else { lr.Next = next; lr.Transition = t; lr.TransElapsed = 0; lr.TransDuration = dur; lr.TransAnyState = fromAny; }
            lr.EnteredThisFrame.Add(next);
        }

        static float SecondsPerLoop(StateRun r) => r.Length / Math.Max(Math.Abs(r.Speed * r.SpeedMultiplier), 1e-6f);

        // ------------------------------------------------------------------ Play / CrossFade
        public void Play(int layer, CState s, float normalizedTime, bool fixedTime)
        {
            var lr = Layers[layer];
            if (lr.Current != null) lr.ExitedThisFrame.Add(lr.Current);
            if (lr.Next != null) lr.ExitedThisFrame.Add(lr.Next);
            var r = NewRun(lr, s, 0);
            if (!float.IsNegativeInfinity(normalizedTime)) r.NormalizedTime = r.PrevNormalizedTime = fixedTime ? normalizedTime / Math.Max(r.Length, 1e-6f) : normalizedTime;
            lr.Current = r; lr.Next = null; lr.Transition = null; lr.Interrupted = null;
            lr.EnteredThisFrame.Add(r);
            Initialized = true;
        }

        public void CrossFade(int layer, CState s, float duration, bool fixedDuration, float offset, bool fixedOffset)
        {
            var lr = Layers[layer];
            var t = new CTransition { Dst = s, Duration = duration, FixedDuration = fixedDuration, Offset = 0, Name = "" };
            if (lr.Current == null) { Play(layer, s, 0, false); return; }
            Start(lr, t, s, false);
            if (lr.Next != null)
            {
                if (fixedOffset) lr.Next.NormalizedTime = lr.Next.PrevNormalizedTime = offset / Math.Max(lr.Next.Length, 1e-6f);
                else if (!float.IsNegativeInfinity(offset)) lr.Next.NormalizedTime = lr.Next.PrevNormalizedTime = offset;
            }
        }

        public CState FindState(int layer, int hash)
        {
            IEnumerable<LayerRun> ls = layer >= 0 && layer < Layers.Count ? new[] { Layers[layer] } : Layers;
            foreach (var lr in ls)
                foreach (var s in lr.Layer.Root.AllStates())
                    if (s.FullHash == hash || s.ShortHash == hash || Animator.StringToHash(s.Machine.Name + "." + s.Name) == hash) return s;
            return null;
        }

        public int LayerOf(CState s)
        {
            for (int i = 0; i < Layers.Count; i++) if (Layers[i].Layer.Root.AllStates().Contains(s)) return i;
            return 0;
        }

        // ------------------------------------------------------------------ update
        public void Update(float dt)
        {
            var machineEvents = new List<(CMachine, bool, int)>();
            if (!Initialized)
            {
                Initialized = true;
                foreach (var lr in Layers)
                {
                    var me = new List<(CMachine m, bool enter)>();
                    var s = EnterMachine(lr.Layer.Root, me, true);
                    if (s == null) continue;
                    lr.Current = NewRun(lr, s, 0);
                    lr.EnteredThisFrame.Add(lr.Current);
                }
            }
            foreach (var lr in Layers)
            {
                if (lr.Layer.SyncedIndex >= 0) continue;
                Advance(lr, dt);
                if (lr.Current == null) continue;
                // transition progress / completion and interruptions
                if (lr.Transition != null)
                {
                    lr.TransElapsed += dt * Math.Abs(Anim.speed);
                    var interrupt = Interruption(lr);
                    if (interrupt.t != null)
                    {
                        var me = new List<(CMachine m, bool enter)>();
                        var dst = Resolve(lr, interrupt.t, interrupt.from?.State, me);
                        if (dst != null) Start(lr, interrupt.t, dst, interrupt.t.AnyState);
                    }
                    else if (lr.TransElapsed >= lr.TransDuration)
                    {
                        lr.ExitedThisFrame.Add(lr.Current);
                        if (lr.Interrupted != null) { lr.ExitedThisFrame.Add(lr.Interrupted); lr.Interrupted = null; }
                        lr.Current = lr.Next; lr.Next = null; lr.Transition = null;
                    }
                }
                if (lr.Transition == null)
                {
                    // instant transitions may chain within one frame (Unity evaluates once per update; at most one hop here)
                    var t = FindTransition(lr, lr.Current, true, true);
                    if (t != null)
                    {
                        var me = new List<(CMachine m, bool enter)>();
                        var dst = Resolve(lr, t, lr.Current.State, me);
                        if (dst != null) { Start(lr, t, dst, t.AnyState); foreach (var e in me) machineEvents.Add((e.m, e.enter, lr.Index)); }
                    }
                }
            }
            // synced layers follow their source layer's state
            foreach (var lr in Layers.Where(l => l.Layer.SyncedIndex >= 0 && l.Layer.SyncedIndex < Layers.Count))
            {
                var src = Layers[lr.Layer.SyncedIndex];
                lr.Current = Mirror(lr, src.Current); lr.Next = Mirror(lr, src.Next);
                lr.Transition = src.Transition; lr.TransElapsed = src.TransElapsed; lr.TransDuration = src.TransDuration;
            }
            Callbacks(machineEvents);
            Evaluate();
            FireEvents();
            foreach (var lr in Layers)
            {
                foreach (var r in new[] { lr.Current, lr.Next }) if (r != null) r.Fresh = false;
                lr.EnteredThisFrame.Clear(); lr.ExitedThisFrame.Clear();
            }
        }

        StateRun Mirror(LayerRun lr, StateRun src)
        {
            if (src == null) return null;
            var r = new StateRun { State = src.State, Motion = MotionOf(lr, src.State), NormalizedTime = src.NormalizedTime, PrevNormalizedTime = src.PrevNormalizedTime, Fresh = src.Fresh };
            Refresh(r);
            return r;
        }

        (CTransition t, StateRun from) Interruption(LayerRun lr)
        {
            var tr = lr.Transition;
            if (tr == null || tr.InterruptionSource == 0) return (null, null);
            var any = FindTransition(lr, lr.Current, true, false);
            if (any != null && any != tr) return (any, lr.Current);
            CTransition FromSource()
            {
                foreach (var t in SoloFilter(lr.Current.State.Transitions))
                {
                    if (t == tr && tr.OrderedInterruption) break;   // ordered: only transitions with higher priority
                    if (t != tr && Passes(t, lr.Current)) return t;
                }
                return null;
            }
            CTransition FromDest() { foreach (var t in SoloFilter(lr.Next.State.Transitions)) if (Passes(t, lr.Next)) return t; return null; }
            CTransition found = tr.InterruptionSource switch
            {
                1 => FromSource(),
                2 => FromDest(),
                3 => FromSource() ?? FromDest(),
                4 => FromDest() ?? FromSource(),
                _ => null,
            };
            return (found, found != null && tr.InterruptionSource == 2 ? lr.Next : lr.Current);
        }

        void Advance(LayerRun lr, float dt)
        {
            foreach (var r in new[] { lr.Current, lr.Next })
            {
                if (r == null) continue;
                Refresh(r);
                r.PrevNormalizedTime = r.NormalizedTime;
                var s = r.State;
                if (s.TimeParamActive) { r.NormalizedTime = ParamF(s.TimeParam); continue; }
                float speed = r.Speed * r.SpeedMultiplier * Anim.speed;
                if (r.Length > 0) r.NormalizedTime += dt * speed / r.Length;
            }
        }

        // ------------------------------------------------------------------ StateMachineBehaviour callbacks
        StateMachineBehaviour Instance(StateMachineBehaviour asset)
        {
            if (asset == null) return null;
            if (asset.GetType().GetCustomAttribute<SharedBetweenAnimatorsAttribute>() != null) return asset;
            if (!behaviourInstances.TryGetValue(asset, out var inst)) behaviourInstances[asset] = inst = Object.Instantiate(asset);
            return inst;
        }

        public IEnumerable<StateMachineBehaviour> BehavioursOf(LayerRun lr, CState s)
        {
            if (lr.Layer.SyncedIndex >= 0) yield break;
            foreach (var b in s.Behaviours) yield return Instance(b);
            foreach (var m in s.Machine.SelfAndParents()) foreach (var b in m.Behaviours) yield return Instance(b);
        }

        public IEnumerable<StateMachineBehaviour> AllBehaviours()
        {
            foreach (var lr in Layers)
                foreach (var s in lr.Layer.Root.AllStates())
                    foreach (var b in BehavioursOf(lr, s)) yield return b;
        }

        void Callbacks(List<(CMachine m, bool enter, int layer)> machineEvents)
        {
            foreach (var lr in Layers)
            {
                foreach (var r in lr.ExitedThisFrame.Distinct())
                    foreach (var b in BehavioursOf(lr, r.State).Distinct()) Invoke(b, "OnStateExit", Info(lr, r), lr.Index);
                foreach (var (m, enter, layer) in machineEvents.Where(e => e.layer == lr.Index))
                    foreach (var b in m.Behaviours.Select(Instance)) InvokeMachine(b, enter ? "OnStateMachineEnter" : "OnStateMachineExit", m.PathHash);
                foreach (var r in lr.EnteredThisFrame.Distinct())
                {
                    if (r != lr.Current && r != lr.Next) continue;
                    foreach (var b in BehavioursOf(lr, r.State).Distinct()) Invoke(b, "OnStateEnter", Info(lr, r), lr.Index);
                }
                foreach (var r in new[] { lr.Current, lr.Next })
                {
                    if (r == null || lr.EnteredThisFrame.Contains(r)) continue;
                    foreach (var b in BehavioursOf(lr, r.State).Distinct()) Invoke(b, "OnStateUpdate", Info(lr, r), lr.Index);
                }
            }
        }

        void Invoke(StateMachineBehaviour b, string name, AnimatorStateInfo info, int layer)
        {
            if (b == null) return;
            try
            {
                switch (name)
                {
                    case "OnStateEnter": b.OnStateEnter(Anim, info, layer); b.OnStateEnter(Anim, info, layer, default); break;
                    case "OnStateUpdate": b.OnStateUpdate(Anim, info, layer); b.OnStateUpdate(Anim, info, layer, default); break;
                    case "OnStateExit": b.OnStateExit(Anim, info, layer); b.OnStateExit(Anim, info, layer, default); break;
                    case "OnStateMove": b.OnStateMove(Anim, info, layer); b.OnStateMove(Anim, info, layer, default); break;
                }
            }
            catch (Exception e) { MiniUnity.Play.Report(e, $"{b.GetType().Name}.{name}"); }
        }

        void InvokeMachine(StateMachineBehaviour b, string name, int hash)
        {
            if (b == null) return;
            try
            {
                if (name == "OnStateMachineEnter") { b.OnStateMachineEnter(Anim, hash); b.OnStateMachineEnter(Anim, hash, default); }
                else { b.OnStateMachineExit(Anim, hash); b.OnStateMachineExit(Anim, hash, default); }
            }
            catch (Exception e) { MiniUnity.Play.Report(e, $"{b.GetType().Name}.{name}"); }
        }

        public AnimatorStateInfo Info(LayerRun lr, StateRun r)
        {
            if (r == null) return default;
            var s = r.State;
            return new AnimatorStateInfo
            {
                m_Name = s.ShortHash, m_Path = s.FullHash, m_FullPath = s.FullHash, m_NormalizedTime = r.NormalizedTime,
                m_Length = r.Length / Math.Max(Math.Abs(r.Speed * r.SpeedMultiplier), 1e-6f), m_Speed = r.Speed, m_SpeedMultiplier = r.SpeedMultiplier,
                m_Tag = s.TagHash, m_Loop = r.Loop ? 1 : 0,
            };
        }

        public AnimatorTransitionInfo TransitionInfo(LayerRun lr)
        {
            var t = lr.Transition;
            if (t == null) return default;
            return new AnimatorTransitionInfo
            {
                m_FullPath = t.FullPathHash, m_UserName = t.UserNameHash, m_Name = t.NameHash, m_HasFixedDuration = t.FixedDuration,
                m_Duration = t.FixedDuration ? t.Duration : t.Duration, m_NormalizedTime = lr.TransitionProgress, m_AnyState = lr.TransAnyState,
                m_TransitionType = t.Entry ? 2 : t.IsExit ? 4 : 1,
            };
        }

        public List<AnimatorClipInfo> ClipInfo(LayerRun lr, bool current)
        {
            var r = current ? lr.Current : lr.Next;
            var res = new List<AnimatorClipInfo>();
            if (r == null) return res;
            float scale = current ? (lr.Next != null ? 1 - lr.TransitionProgress : 1) : lr.TransitionProgress;
            foreach (var c in r.Clips) res.Add(new AnimatorClipInfo { m_ClipRef = c.clip, m_Weight = c.weight * scale });
            return res;
        }

        // ------------------------------------------------------------------ pose evaluation
        void SampleRun(StateRun r, Pose pose, float weight)
        {
            if (r == null || weight <= 0) return;
            for (int i = 0; i < r.Clips.Count; i++)
            {
                var (clip, d, w, _, _) = r.Clips[i];
                var (ts, cyc) = r.ClipTimeScales[i];
                float nt = r.NormalizedTime + r.State.CycleOffset + (r.State.CycleOffsetParamActive ? ParamF(r.State.CycleOffsetParam) : 0) + d.CycleOffset + cyc;
                pose.Add(d, d.TimeAt(nt, d.Loop), w * weight);
            }
        }

        void Evaluate()
        {
            var final = new Dictionary<string, float>(); var finalRefs = new Dictionary<string, Object>();
            bool writeDefaultsBase = true;
            for (int li = 0; li < Layers.Count; li++)
            {
                var lr = Layers[li];
                if (lr.Current == null) continue;
                float layerWeight = li == 0 ? 1 : lr.Weight;
                float t = lr.Next != null ? lr.TransitionProgress : 0;
                var a = new Pose(); var b = new Pose(); var c = new Pose();
                SampleRun(lr.Current, a, 1);
                if (lr.Next != null) SampleRun(lr.Next, b, 1);
                if (lr.Interrupted != null) SampleRun(lr.Interrupted, c, 1);
                b.AlignRotationsTo(a); c.AlignRotationsTo(a);   // transitions blend rotations on one hemisphere
                bool additive = li > 0 && lr.Layer.Blending == 1;
                Pose ar = null, br = null, cr = null;
                if (additive)
                {
                    // additive layers add the difference to each clip's reference pose (its first frame, or the reference clip / time)
                    ar = new Pose(); br = new Pose(); cr = new Pose();
                    SampleRef(lr.Current, ar); if (lr.Next != null) SampleRef(lr.Next, br); if (lr.Interrupted != null) SampleRef(lr.Interrupted, cr);
                    br.AlignRotationsTo(ar); cr.AlignRotationsTo(ar);
                }
                var layerVals = new Dictionary<string, float>(); var refVals = additive ? new Dictionary<string, float>() : null;
                bool wdA = lr.Current.State.WriteDefaults, wdB = lr.Next?.State.WriteDefaults ?? wdA;
                if (li == 0) writeDefaultsBase = lr.Next != null ? wdB : wdA;
                var keys = new HashSet<string>(a.Floats.Keys); keys.UnionWith(b.Floats.Keys); keys.UnionWith(c.Floats.Keys);
                var mask = lr.Layer.Mask;
                foreach (var k in keys)
                {
                    if (!Bindings.TryGetValue(k, out var bp)) continue;
                    if (mask != null && !mask.Allows(Writer.Root, bp.Target ?? bp.Go?.transform)) continue;
                    float cur = Value(bp);
                    float Va(Pose p, bool wd) => p.Floats.TryGetValue(k, out var acc) && acc.w > 0 ? acc.sum / acc.w : (wd ? bp.Default : cur);
                    float Mix(Pose pa, Pose pb, Pose pc)
                    {
                        if (lr.Next == null) return Va(pa, wdA);
                        float from = lr.Interrupted != null ? Lerp(Va(pa, wdA), Va(pc, wdA), lr.InterruptedWeight) : Va(pa, wdA);
                        return Lerp(from, Va(pb, wdB), t);
                    }
                    layerVals[k] = Mix(a, b, c);
                    if (additive) refVals[k] = Mix(ar, br, cr);
                }
                // rotations are combined as quaternions (the four components of a group together), the rest per value
                var rotGroups = new HashSet<string>();
                foreach (var k in layerVals.Keys) if (Pose.IsRotation(k, out var g, out _)) rotGroups.Add(g);
                foreach (var (k, v) in layerVals)
                {
                    if (Pose.IsRotation(k, out var g, out _) && layerVals.ContainsKey(g + "x") && layerVals.ContainsKey(g + "y") && layerVals.ContainsKey(g + "z") && layerVals.ContainsKey(g + "w")) continue;
                    var bp = Bindings[k];
                    float baseVal = final.TryGetValue(k, out var fv) ? fv : bp.Default;
                    if (li == 0 || !final.ContainsKey(k) && !additive) final[k] = li == 0 ? v : Lerp(baseVal, v, layerWeight);
                    else if (additive) final[k] = baseVal + (v - refVals[k]) * layerWeight;
                    else final[k] = Lerp(baseVal, v, layerWeight);
                }
                foreach (var g in rotGroups)
                {
                    if (!(layerVals.ContainsKey(g + "x") && layerVals.ContainsKey(g + "y") && layerVals.ContainsKey(g + "z") && layerVals.ContainsKey(g + "w"))) continue;
                    var q = new Quaternion(layerVals[g + "x"], layerVals[g + "y"], layerVals[g + "z"], layerVals[g + "w"]);
                    Quaternion Def() => new Quaternion(Bindings[g + "x"].Default, Bindings[g + "y"].Default, Bindings[g + "z"].Default, Bindings[g + "w"].Default);
                    bool hasBase = final.ContainsKey(g + "w");
                    var baseQ = hasBase ? new Quaternion(final[g + "x"], final[g + "y"], final[g + "z"], final[g + "w"]) : Def();
                    Quaternion res;
                    if (li == 0) res = q;
                    else if (additive)
                    {
                        var refQ = new Quaternion(refVals[g + "x"], refVals[g + "y"], refVals[g + "z"], refVals[g + "w"]);
                        var delta = Quaternion.Inverse(Norm(refQ)) * Norm(q);
                        res = Norm(baseQ) * Quaternion.Slerp(Quaternion.identity, delta, layerWeight);
                    }
                    else
                    {
                        if (baseQ.x * q.x + baseQ.y * q.y + baseQ.z * q.z + baseQ.w * q.w < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        res = new Quaternion(Lerp(baseQ.x, q.x, layerWeight), Lerp(baseQ.y, q.y, layerWeight), Lerp(baseQ.z, q.z, layerWeight), Lerp(baseQ.w, q.w, layerWeight));
                    }
                    final[g + "x"] = res.x; final[g + "y"] = res.y; final[g + "z"] = res.z; final[g + "w"] = res.w;
                }
                var rkeys = new HashSet<string>(a.Refs.Keys); rkeys.UnionWith(b.Refs.Keys);
                foreach (var k in rkeys)
                {
                    if (mask != null && Bindings.TryGetValue(k, out var rbp) && !mask.Allows(Writer.Root, rbp.Target ?? rbp.Go?.transform)) continue;
                    var src = lr.Next != null && t >= 0.5f && b.Refs.ContainsKey(k) ? b : a.Refs.ContainsKey(k) ? a : b;
                    if (layerWeight >= 0.5f || li == 0) finalRefs[k] = src.Refs[k].value;
                }
            }
            if (writeDefaultsBase)
                foreach (var (k, bp) in Bindings)
                {
                    if (bp.IsPPtr) { if (!finalRefs.ContainsKey(k)) finalRefs[k] = bp.DefaultRef; }
                    else if (!final.ContainsKey(k)) final[k] = bp.Default;
                }
            RootMotion(final);
            if (CullTransforms) return;   // cullingMode CullUpdateTransforms with no visible renderer: root motion only
            var ikLayers = new List<int>();
            for (int li = 0; li < Layers.Count; li++) if (Layers[li].Layer.IKPass) ikLayers.Add(li);
            Anim.RunIK(ikLayers);
            Apply(final, finalRefs);
            ApplyMatch();
        }

        /// <summary>Set by the animation system each update: the animator's renderers are all invisible and its culling mode is
        /// CullUpdateTransforms (state machine and root motion run, IK and the transform writes do not).</summary>
        public bool CullTransforms;

        // ------------------------------------------------------------------ MatchTarget
        public sealed class MatchRequest
        {
            public Vector3 Position; public Quaternion Rotation; public AvatarTarget Part; public MatchTargetWeightMask Mask;
            public float Start, End, Base; public bool Complete; public StateRun Run;
        }
        public MatchRequest Match;

        public void StartMatch(Vector3 pos, Quaternion rot, AvatarTarget part, MatchTargetWeightMask mask, float start, float end, bool complete)
        {
            var lr = Layers.Count > 0 ? Layers[0] : null;
            var r = lr?.Current;
            if (r == null || lr.Transition != null) return;   // Unity ignores the call during a transition
            float b = r.Loop ? Mathf.Floor(r.NormalizedTime) : 0;
            if (r.NormalizedTime - b > end) return;            // the window has already passed in this loop
            Match = new MatchRequest { Position = pos, Rotation = rot, Part = part, Mask = mask, Start = start, End = end, Base = b, Complete = complete, Run = r };
        }

        public void InterruptMatch(bool complete)
        {
            var m = Match; Match = null;
            if (m != null && complete) MoveRootBy(m, 1);
        }

        Transform MatchPart(AvatarTarget part)
        {
            HumanBodyBones? bone = part switch
            {
                AvatarTarget.Body => HumanBodyBones.Hips, AvatarTarget.LeftFoot => HumanBodyBones.LeftFoot, AvatarTarget.RightFoot => HumanBodyBones.RightFoot,
                AvatarTarget.LeftHand => HumanBodyBones.LeftHand, AvatarTarget.RightHand => HumanBodyBones.RightHand, _ => null,
            };
            return (bone is HumanBodyBones hb && Anim.isHuman ? Anim.GetBoneTransform(hb) : null) ?? Anim.transform;
        }

        /// <summary>Moves the root so that fraction f of the remaining gap between the target body part and the match target closes.</summary>
        void MoveRootBy(MatchRequest m, float f)
        {
            var root = Anim.transform; var part = MatchPart(m.Part);
            var gap = m.Position - part.position; var pw = m.Mask.positionXYZWeight;
            root.position += new Vector3(gap.x * pw.x, gap.y * pw.y, gap.z * pw.z) * f;
            float rw = m.Mask.rotationWeight * f;
            if (rw > 0)
            {
                var dr = m.Rotation * Quaternion.Inverse(part.rotation);
                var turn = Quaternion.Slerp(Quaternion.identity, dr, rw);
                var pivot = part.position;
                root.rotation = turn * root.rotation;
                if (part != root) root.position += pivot - part.position;   // turn about the body part, not the root
            }
        }

        /// <summary>Spreads the correction over [start, end] of the state's normalized time: each frame closes the gap in proportion to
        /// the time left, so the body part reaches the target exactly at end.</summary>
        void ApplyMatch()
        {
            var m = Match;
            if (m == null) return;
            var lr = Layers[0];
            if (lr.Current != m.Run) { Match = null; if (m.Complete) MoveRootBy(m, 1); return; }
            if (lr.Transition != null) return;
            float n = m.Run.NormalizedTime - m.Base, prev = Math.Max(m.Run.PrevNormalizedTime - m.Base, m.Start);
            if (n < m.Start) return;
            float f = n >= m.End || m.End <= prev ? 1 : (n - prev) / (m.End - prev);
            MoveRootBy(m, Mathf.Clamp01(f));
            if (n >= m.End) Match = null;
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static Quaternion Norm(Quaternion q) { float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w); return m > 1e-8f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity; }

        /// <summary>The reference pose of a state's clips (additive layers).</summary>
        void SampleRef(StateRun r, Pose pose)
        {
            if (r == null) return;
            foreach (var (clip, d, w, _, _) in r.Clips)
            {
                if (d.HasAdditiveReference && d.AdditiveReferenceClip != null) { var rd = ClipData.Of(d.AdditiveReferenceClip); pose.Add(rd, rd.StartTime + d.AdditiveReferenceTime, w); }
                else pose.Add(d, d.HasAdditiveReference ? d.StartTime + d.AdditiveReferenceTime : d.StartTime, w);
            }
        }
        static float Value(BoundProperty bp) => bp.Get?.Invoke() ?? 0;

        /// <summary>Root motion: with applyRootMotion the root transform's own curves become deltas (or go to OnAnimatorMove).</summary>
        void RootMotion(Dictionary<string, float> pose)
        {
            DeltaPosition = Vector3.zero; DeltaRotation = Quaternion.identity;
            bool hasMove = Anim.gameObject.m_Components.OfType<MonoBehaviour>().Any(m => m.enabled && m.GetType().GetMethod("OnAnimatorMove", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null);
            if (!Anim.applyRootMotion && !hasMove) return;
            string P(char c) => $"|4||m_LocalPosition.{c}"; string R(char c) => $"|4||m_LocalRotation.{c}";
            bool hasPos = pose.ContainsKey(P('x')) || pose.ContainsKey(P('y')) || pose.ContainsKey(P('z'));
            bool hasRot = pose.ContainsKey(R('w'));
            var t = Anim.transform;
            var pos = new Vector3(pose.GetValueOrDefault(P('x'), t.localPosition.x), pose.GetValueOrDefault(P('y'), t.localPosition.y), pose.GetValueOrDefault(P('z'), t.localPosition.z));
            var rot = hasRot ? new Quaternion(pose[R('x')], pose[R('y')], pose[R('z')], pose[R('w')]).normalized : t.localRotation;
            if (hasPos && lastRootPos is Vector3 lp) DeltaPosition = t.rotation * Quaternion.Inverse(lastRootRot ?? rot) * (pos - lp);
            if (hasRot && lastRootRot is Quaternion lq) DeltaRotation = rot * Quaternion.Inverse(lq);
            lastRootPos = pos; lastRootRot = rot;
            // root curves no longer drive the transform directly
            foreach (var c in "xyz") pose.Remove(P(c));
            foreach (var c in "xyzw") pose.Remove(R(c));
            if (hasMove) PhysicsMessages.Send(Anim.gameObject, "OnAnimatorMove", null, false);
            else { t.position += DeltaPosition; t.rotation = DeltaRotation * t.rotation; }
        }

        // ------------------------------------------------------------------ animation events
        void FireEvents()
        {
            if (!Anim.fireEvents) return;
            foreach (var lr in Layers)
            {
                float lw = lr.Index == 0 ? 1 : lr.Weight;
                if (lw <= 0) continue;
                foreach (var (r, w) in new[] { (lr.Current, lr.Next != null ? 1 - lr.TransitionProgress : 1f), (lr.Next, lr.TransitionProgress) })
                {
                    if (r == null || r.Clips.Count == 0) continue;
                    // blend trees: every clip with weight fires its events; the receiver sees the clip's weight (animatorClipInfo)
                    var info = Info(lr, r);
                    foreach (var (clip, d, cw, _, _) in r.Clips)
                    {
                        if (cw <= 0 || d.Events.Count == 0 || d.Length <= 0) continue;
                        var ci = new AnimatorClipInfo { m_ClipRef = clip, m_Weight = cw * w };
                        foreach (var ev in EventsBetween(d, r.PrevNormalizedTime, r.NormalizedTime, r.Fresh))
                        {
                            var e = ev.Clone(); e.m_Source = AnimationEventSource.Animator; e.m_AnimatorStateInfo = info; e.m_AnimatorClipInfo = ci;
                            Dispatch(Anim.gameObject, e);
                        }
                    }
                }
            }
        }

        public static IEnumerable<AnimationEvent> EventsBetween(ClipData d, float prevN, float curN, bool fresh)
        {
            if (d.Length <= 0 || curN < prevN) yield break;
            float L = d.Length;
            if (!d.Loop) { prevN = Math.Min(prevN, 1); curN = Math.Min(curN, 1); }
            float a = prevN * L, b = curN * L;
            for (float loop = MathF.Floor(prevN); loop <= MathF.Floor(curN) + 1e-6f; loop++)
                foreach (var ev in d.Events)
                {
                    float t = loop * L + (ev.time - d.StartTime);
                    if ((t > a || (fresh && t >= a && a <= 1e-6f)) && t <= b) yield return ev;   // time-0 events fire when the state starts at 0
                    else if (!d.Loop && loop > 0) break;
                }
        }

        public static void Dispatch(GameObject go, AnimationEvent e)
        {
            bool received = false;
            foreach (var c in go.m_Components.ToList())
            {
                if (c is not MonoBehaviour mb || mb.m_Destroyed || !mb.enabled) continue;
                MethodInfo m = null;
                for (var t = mb.GetType(); t != null && m == null && t != typeof(MonoBehaviour); t = t.BaseType)
                    m = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).FirstOrDefault(x => x.Name == e.functionName && x.GetParameters().Length <= 1);
                if (m == null) continue;
                received = true;
                var ps = m.GetParameters();
                object arg = null;
                if (ps.Length == 1)
                {
                    var pt = ps[0].ParameterType;
                    arg = pt == typeof(AnimationEvent) ? e : pt == typeof(float) ? e.floatParameter : pt == typeof(int) ? e.intParameter : pt == typeof(string) ? e.stringParameter
                        : typeof(Object).IsAssignableFrom(pt) ? e.objectReferenceParameter : pt.IsEnum ? Enum.ToObject(pt, e.intParameter) : null;
                }
                try { var r = m.Invoke(mb, ps.Length == 0 ? null : new[] { arg }); if (r is System.Collections.IEnumerator en && MiniUnity.Play.IsPlaying) MiniUnity.Play.StartRoutine(mb, en, e.functionName); }
                catch (TargetInvocationException tie) { MiniUnity.Play.Report(tie.InnerException ?? tie, $"{mb.GetType().Name}.{e.functionName}"); }
            }
            if (!received && e.messageOptions == SendMessageOptions.RequireReceiver)
                Debug.LogError($"'{go.name}' AnimationEvent '{e.functionName}' on animation '{e.m_AnimatorClipInfo.clip?.name ?? e.m_StateSender?.name}' has no receiver! Are you missing a component?");
        }
    }

    /// <summary>Bound properties of a hierarchy and the writer for blended poses (shared by Animator, Animation, SampleAnimation).</summary>
    public sealed class PoseWriter
    {
        public readonly Transform Root;
        public readonly Dictionary<string, BoundProperty> Bindings = new();
        public PoseWriter(Transform root) { Root = root; }

        public void Bind(ClipData d)
        {
            foreach (var b in d.Floats)
            {
                if (Bindings.ContainsKey(b.Key)) continue;
                var bp = Binder.Bind(Root, b.Path, b.ClassId, b.ScriptType, b.Attribute, false);
                if (bp != null) { bp.Key = b.Key; Bindings[b.Key] = bp; }
            }
            foreach (var b in d.PPtrs)
            {
                if (Bindings.ContainsKey(b.Key)) continue;
                var bp = Binder.Bind(Root, b.Path, b.ClassId, b.ScriptType, b.Attribute, true);
                if (bp != null) { bp.Key = b.Key; Bindings[b.Key] = bp; }
            }
        }

        public void WriteDefaults()
        {
            var pose = new Dictionary<string, float>(); var refs = new Dictionary<string, Object>();
            foreach (var (k, bp) in Bindings) { if (bp.IsPPtr) refs[k] = bp.DefaultRef; else pose[k] = bp.Default; }
            Apply(pose, refs);
        }

        /// <summary>Write the final pose (transform channels grouped per object, quaternions normalized).</summary>
        public void Apply(Dictionary<string, float> pose, Dictionary<string, Object> refs)
        {
            var tf = new Dictionary<Transform, (Vector3? p, float?[] q, float?[] e, Vector3? s, float?[] pc, float?[] sc)>();
            foreach (var (k, v) in pose)
            {
                if (!Bindings.TryGetValue(k, out var bp)) continue;
                if (bp.TransformChannel < 0) { try { bp.Set(v); } catch (Exception e) { Debug.LogWarning($"[Animator] {bp.Attribute}: {e.Message}"); } continue; }
                tf.TryGetValue(bp.Target, out var acc);
                acc.pc ??= new float?[3]; acc.q ??= new float?[4]; acc.e ??= new float?[3]; acc.sc ??= new float?[3];
                switch (bp.TransformChannel)
                {
                    case 0: acc.pc[bp.Component4] = v; break;
                    case 1: acc.q[bp.Component4] = v; break;
                    case 2: acc.e[bp.Component4] = v; break;
                    default: acc.sc[bp.Component4] = v; break;
                }
                tf[bp.Target] = acc;
            }
            foreach (var (t, acc) in tf)
            {
                if (acc.pc.Any(x => x.HasValue)) { var p = t.localPosition; t.localPosition = new Vector3(acc.pc[0] ?? p.x, acc.pc[1] ?? p.y, acc.pc[2] ?? p.z); }
                if (acc.q.Any(x => x.HasValue))
                {
                    var q = t.localRotation;
                    var nq = new Quaternion(acc.q[0] ?? q.x, acc.q[1] ?? q.y, acc.q[2] ?? q.z, acc.q[3] ?? q.w);
                    float m = Mathf.Sqrt(nq.x * nq.x + nq.y * nq.y + nq.z * nq.z + nq.w * nq.w);
                    if (m > 1e-8f) t.localRotation = new Quaternion(nq.x / m, nq.y / m, nq.z / m, nq.w / m);
                }
                if (acc.e.Any(x => x.HasValue)) { var e = t.localEulerAngles; t.localEulerAngles = new Vector3(acc.e[0] ?? e.x, acc.e[1] ?? e.y, acc.e[2] ?? e.z); }
                if (acc.sc.Any(x => x.HasValue)) { var s = t.localScale; t.localScale = new Vector3(acc.sc[0] ?? s.x, acc.sc[1] ?? s.y, acc.sc[2] ?? s.z); }
            }
            foreach (var (k, v) in refs)
                if (Bindings.TryGetValue(k, out var bp) && bp.SetRef != null)
                {
                    try { if (bp.GetRef() != v) bp.SetRef(v); } catch (Exception e) { Debug.LogWarning($"[Animator] {bp.Attribute}: {e.Message}"); }
                }
        }

    }

    static class MachineExt
    {
        public static IEnumerable<CMachine> SelfAndAll(this CMachine m)
        {
            yield return m;
            foreach (var c in m.Machines) foreach (var x in c.SelfAndAll()) yield return x;
        }
    }
}
