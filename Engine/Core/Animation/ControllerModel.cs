// AnimatorController asset model for MiniUnity's Mecanim emulation: parameters, layers, state machines (states, sub-state
// machines, AnyState / Entry / Exit transitions), blend trees, StateMachineBehaviours — read from the serialized controller
// (.controller YAML: AnimatorController 91, AnimatorStateMachine 1107, AnimatorState 1102, AnimatorStateTransition 1101,
// AnimatorTransition 1109, BlendTree 206) and AnimatorOverrideController (221).
using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity.Yaml;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity.Anim
{
    public sealed class CParam { public string Name; public int Hash; public AnimatorControllerParameterType Type; public float DefFloat; public int DefInt; public bool DefBool; }
    public sealed class CCondition { public int Mode; public string Param; public float Threshold; }

    public sealed class CTransition
    {
        public string Name; public List<CCondition> Conditions = new();
        public CState Dst; public CMachine DstMachine; public bool IsExit, Solo, Mute, HasExitTime, FixedDuration, CanTransitionToSelf = true, OrderedInterruption = true;
        public float Duration, Offset, ExitTime; public int InterruptionSource;
        public CMachine Owner; public CState Source; public bool AnyState, Entry;
        public int NameHash, FullPathHash, UserNameHash;
    }

    public sealed class CMotion
    {
        public AnimationClip Clip; public CBlendTree Tree;
        public static CMotion Of(Object o, Func<Object, CMotion> tree) => o switch { AnimationClip c => new CMotion { Clip = c }, null => null, _ => tree(o) };
    }

    public sealed class CBlendChild { public CMotion Motion; public float Threshold, TimeScale = 1, CycleOffset; public Vector2 Position; public string DirectParam; }

    public sealed class CBlendTree
    {
        public string Name, Param, ParamY; public int Type; public bool NormalizedBlendValues;
        public List<CBlendChild> Children = new();
    }

    public sealed class CState
    {
        public string Name, FullPath, Tag; public int ShortHash, FullHash, TagHash;
        public float Speed = 1, CycleOffset; public bool WriteDefaults = true, Mirror;
        public CMotion Motion;
        public string SpeedParam, CycleOffsetParam, TimeParam; public bool SpeedParamActive, CycleOffsetParamActive, TimeParamActive;
        public List<CTransition> Transitions = new();
        public List<StateMachineBehaviour> Behaviours = new();
        public CMachine Machine;
        public Object Source;
    }

    public sealed class CMachine
    {
        public string Name, Path; public int PathHash; public CMachine Parent;
        public List<CState> States = new(); public List<CMachine> Machines = new();
        public List<CTransition> AnyState = new(), Entry = new();
        public Dictionary<CMachine, List<CTransition>> MachineTransitions = new();
        public CState Default; public Object DefaultStateSource;
        public List<StateMachineBehaviour> Behaviours = new();
        public IEnumerable<CState> AllStates() { foreach (var s in States) yield return s; foreach (var m in Machines) foreach (var s in m.AllStates()) yield return s; }
        public IEnumerable<CMachine> SelfAndParents() { for (var m = this; m != null; m = m.Parent) yield return m; }
    }

    public sealed class CLayer
    {
        public string Name; public CMachine Root; public float DefaultWeight; public int Blending; public int SyncedIndex = -1; public bool SyncedAffectsTiming, IKPass;
        public Dictionary<CState, CMotion> SyncedMotions = new();
        public Dictionary<CState, List<StateMachineBehaviour>> SyncedBehaviours = new();
        public AvatarMask Mask;
    }

    public sealed class CController
    {
        public List<CParam> Params = new(); public List<CLayer> Layers = new();
        public Dictionary<AnimationClip, AnimationClip> Overrides = new();
        public List<AnimationClip> Clips = new();
        public Object Asset;

        static readonly Dictionary<Object, (int version, CController c)> cache = new();
        /// <summary>Bumped by every UnityEditor.Animations edit (states and transitions do not know their controller).</summary>
        public static int EditGeneration;

        public static CController Of(RuntimeAnimatorController rac)
        {
            if (rac == null) return null;
            int ver = rac.m_Version * 7919 + EditGeneration;
            if (rac is AnimatorOverrideController aoc && aoc.runtimeAnimatorController != null) ver = ver * 31 + aoc.runtimeAnimatorController.m_Version;
            if (cache.TryGetValue(rac, out var c) && c.version == ver) return c.c;
            var built = Build(rac);
            cache[rac] = (ver, built);
            return built;
        }

        static IEnumerable<YNode> Seq(YNode n) => n is YSeq s ? s.Items : Enumerable.Empty<YNode>();
        static string Str(YNode n) => (n as YScalar)?.Value ?? "";
        static int Int(YNode n, int def = 0) => n is YScalar s && s.Value.Length > 0 ? (int)s.L : def;
        static float F(YNode n, float def = 0) => n is YScalar s && s.Value.Length > 0 ? s.F : def;
        /// <summary>The serialized m_Name, or the object's name for objects built from editor scripts.</summary>
        static string NameOf(Object o, YMap n) => n?["m_Name"] is YScalar s ? s.Value : o.name ?? "";
        static Object Ref(YNode n) => n is YRef r ? (r.Raw ? AssetLoader.ResolveExternal(r) : r.Target) : null;

        static CController Build(RuntimeAnimatorController rac)
        {
            var overrides = new Dictionary<AnimationClip, AnimationClip>();
            var src = rac;
            if (rac is AnimatorOverrideController aoc)
            {
                foreach (var (o, ov) in aoc.OverridePairs()) if (o != null && ov != null) overrides[o] = ov;
                src = aoc.runtimeAnimatorController;
            }
            var c = new CController { Overrides = overrides, Asset = rac };
            if (src?.m_Native is YMap n) BuildNative(c, n);
            c.Clips = c.Layers.SelectMany(l => l.Root.AllStates().SelectMany(s => MotionClips(s.Motion)).Concat(l.SyncedMotions.Values.SelectMany(MotionClips)))
                .Select(cl => overrides.TryGetValue(cl, out var o) ? o : cl).Distinct().ToList();
            return c;
        }

        static IEnumerable<AnimationClip> MotionClips(CMotion m)
        {
            if (m == null) yield break;
            if (m.Clip != null) yield return m.Clip;
            if (m.Tree != null) foreach (var ch in m.Tree.Children) foreach (var c in MotionClips(ch.Motion)) yield return c;
        }

        // ------------------------------------------------------------------ serialized controller
        static void BuildNative(CController c, YMap n)
        {
            foreach (var p in Seq(n["m_AnimatorParameters"]))
            {
                if (p is not YMap pm) continue;
                var name = Str(pm["m_Name"]);
                c.Params.Add(new CParam
                {
                    Name = name, Hash = Animator.StringToHash(name), Type = (AnimatorControllerParameterType)Int(pm["m_Type"], 1),
                    DefFloat = F(pm["m_DefaultFloat"]), DefInt = Int(pm["m_DefaultInt"]), DefBool = Int(pm["m_DefaultBool"]) != 0,
                });
            }
            var states = new Dictionary<Object, CState>(); var machines = new Dictionary<Object, CMachine>(); var trees = new Dictionary<Object, CBlendTree>();
            foreach (var l in Seq(n["m_AnimatorLayers"]))
            {
                if (l is not YMap lm) continue;
                var layer = new CLayer
                {
                    Name = Str(lm["m_Name"]), DefaultWeight = F(lm["m_DefaultWeight"]), Blending = Int(lm["m_BlendingMode"]),
                    SyncedIndex = Int(lm["m_SyncedLayerIndex"], -1), SyncedAffectsTiming = Int(lm["m_SyncedLayerAffectsTiming"]) != 0, IKPass = Int(lm["m_IKPass"]) != 0,
                    Mask = Ref(lm["m_Mask"]) as AvatarMask,
                };
                if (c.Layers.Count == 0) layer.DefaultWeight = 1;
                var smo = Ref(lm["m_StateMachine"]);
                layer.Root = smo != null ? Machine(smo, null, layer.Name, states, machines, trees) : new CMachine { Name = layer.Name, Path = layer.Name };
                c.Layers.Add(layer);
                foreach (var mo in Seq(lm["m_Motions"]))
                {
                    if (mo is not YMap mm) continue;
                    // synced layer motions refer to states of the source layer (resolved after all layers are built)
                    layer.SyncedMotions[new CState { Source = Ref(mm["m_State"]) }] = CMotion.Of(Ref(mm["m_Motion"]), o => new CMotion { Tree = Tree(o, trees) });
                }
            }
            // synced layers: map the placeholder keys to the source layer's states
            foreach (var layer in c.Layers.Where(l => l.SyncedIndex >= 0 && l.SyncedIndex < c.Layers.Count))
            {
                var map = new Dictionary<CState, CMotion>();
                foreach (var (k, v) in layer.SyncedMotions) if (k.Source != null && states.TryGetValue(k.Source, out var st)) map[st] = v;
                layer.SyncedMotions = map;
                layer.Root = c.Layers[layer.SyncedIndex].Root;
            }
            // transitions need every state and machine
            foreach (var (o, st) in states) Transitions(o, st, states, machines);
            foreach (var (o, m) in machines) MachineTransitions(o, m, states, machines);
        }

        static CMachine Machine(Object o, CMachine parent, string layerName, Dictionary<Object, CState> states, Dictionary<Object, CMachine> machines, Dictionary<Object, CBlendTree> trees)
        {
            if (machines.TryGetValue(o, out var existing)) return existing;
            var n = o.m_Native ?? new YMap();
            var name = NameOf(o, n);
            if (parent == null && string.IsNullOrEmpty(name)) name = layerName;
            var m = new CMachine { Name = name, Parent = parent };
            m.Path = parent == null ? name : parent.Path + "." + name;
            m.PathHash = Animator.StringToHash(m.Path);
            machines[o] = m;
            foreach (var cs in Seq(n["m_ChildStates"]))
            {
                if (cs is not YMap cm || Ref(cm["m_State"]) is not Object so) continue;
                var sn = so.m_Native ?? new YMap();
                var st = new CState
                {
                    Name = NameOf(so, sn), Machine = m, Source = so, Speed = F(sn["m_Speed"], 1), CycleOffset = F(sn["m_CycleOffset"]),
                    WriteDefaults = Int(sn["m_WriteDefaultValues"], 1) != 0, Mirror = Int(sn["m_Mirror"]) != 0, Tag = Str(sn["m_Tag"]),
                    SpeedParam = Str(sn["m_SpeedParameter"]), SpeedParamActive = Int(sn["m_SpeedParameterActive"]) != 0,
                    CycleOffsetParam = Str(sn["m_CycleOffsetParameter"]), CycleOffsetParamActive = Int(sn["m_CycleOffsetParameterActive"]) != 0,
                    TimeParam = Str(sn["m_TimeParameter"]), TimeParamActive = Int(sn["m_TimeParameterActive"]) != 0,
                };
                st.FullPath = m.Path + "." + st.Name;
                st.ShortHash = Animator.StringToHash(st.Name); st.FullHash = Animator.StringToHash(st.FullPath); st.TagHash = Animator.StringToHash(st.Tag);
                st.Motion = CMotion.Of(Ref(sn["m_Motion"]), mo => new CMotion { Tree = Tree(mo, trees) });
                foreach (var b in Seq(sn["m_StateMachineBehaviours"])) if (Ref(b) is StateMachineBehaviour smb) st.Behaviours.Add(smb);
                states[so] = st; m.States.Add(st);
            }
            foreach (var cm in Seq(n["m_ChildStateMachines"]))
                if (cm is YMap cmm && Ref(cmm["m_StateMachine"]) is Object mo) m.Machines.Add(Machine(mo, m, layerName, states, machines, trees));
            foreach (var b in Seq(n["m_StateMachineBehaviours"])) if (Ref(b) is StateMachineBehaviour smb) m.Behaviours.Add(smb);
            if (Ref(n["m_DefaultState"]) is Object ds) m.DefaultStateSource = ds;
            return m;
        }

        static CBlendTree Tree(Object o, Dictionary<Object, CBlendTree> trees)
        {
            if (o == null) return null;
            if (trees.TryGetValue(o, out var t)) return t;
            var n = o.m_Native ?? new YMap();
            t = new CBlendTree
            {
                Name = NameOf(o, n), Param = Str(n["m_BlendParameter"]), ParamY = Str(n["m_BlendParameterY"]), Type = Int(n["m_BlendType"]),
                NormalizedBlendValues = Int(n["m_NormalizedBlendValues"]) != 0,
            };
            trees[o] = t;
            foreach (var ch in Seq(n["m_Childs"]))
            {
                if (ch is not YMap cm) continue;
                t.Children.Add(new CBlendChild
                {
                    Motion = CMotion.Of(Ref(cm["m_Motion"]), mo => new CMotion { Tree = Tree(mo, trees) }),
                    Threshold = F(cm["m_Threshold"]), Position = Serializer.ReadV2(cm["m_Position"]), TimeScale = F(cm["m_TimeScale"], 1),
                    CycleOffset = F(cm["m_CycleOffset"]), DirectParam = Str(cm["m_DirectBlendParameter"]),
                });
            }
            return t;
        }

        static CTransition Transition(Object o, Dictionary<Object, CState> states, Dictionary<Object, CMachine> machines)
        {
            var n = o.m_Native ?? new YMap();
            var t = new CTransition
            {
                Name = NameOf(o, n), Solo = Int(n["m_Solo"]) != 0, Mute = Int(n["m_Mute"]) != 0, IsExit = Int(n["m_IsExit"]) != 0,
                Duration = F(n["m_TransitionDuration"]), Offset = F(n["m_TransitionOffset"]), ExitTime = F(n["m_ExitTime"]),
                HasExitTime = Int(n["m_HasExitTime"]) != 0, FixedDuration = Int(n["m_HasFixedDuration"], 1) != 0,
                InterruptionSource = Int(n["m_InterruptionSource"]), OrderedInterruption = Int(n["m_OrderedInterruption"], 1) != 0,
                CanTransitionToSelf = Int(n["m_CanTransitionToSelf"], 1) != 0,
            };
            foreach (var cn in Seq(n["m_Conditions"]))
                if (cn is YMap cm) t.Conditions.Add(new CCondition { Mode = Int(cm["m_ConditionMode"]), Param = Str(cm["m_ConditionEvent"]), Threshold = F(cm["m_EventTreshold"]) });
            if (Ref(n["m_DstState"]) is Object ds && states.TryGetValue(ds, out var st)) t.Dst = st;
            if (Ref(n["m_DstStateMachine"]) is Object dm && machines.TryGetValue(dm, out var mm)) t.DstMachine = mm;
            t.UserNameHash = Animator.StringToHash(t.Name);
            return t;
        }

        static void Transitions(Object o, CState st, Dictionary<Object, CState> states, Dictionary<Object, CMachine> machines)
        {
            foreach (var tr in Seq(o.m_Native?["m_Transitions"]))
            {
                if (Ref(tr) is not Object to) continue;
                var t = Transition(to, states, machines);
                t.Source = st; t.Owner = st.Machine;
                var dstName = t.Dst != null ? t.Dst.Name : t.IsExit ? "Exit" : t.DstMachine?.Name ?? "";
                t.NameHash = Animator.StringToHash(st.Name + " -> " + dstName);
                t.FullPathHash = Animator.StringToHash(st.FullPath + " -> " + (t.Dst?.FullPath ?? dstName));
                st.Transitions.Add(t);
            }
        }

        static void MachineTransitions(Object o, CMachine m, Dictionary<Object, CState> states, Dictionary<Object, CMachine> machines)
        {
            var n = o.m_Native ?? new YMap();
            foreach (var tr in Seq(n["m_AnyStateTransitions"]))
            {
                if (Ref(tr) is not Object to) continue;
                var t = Transition(to, states, machines); t.AnyState = true; t.Owner = m;
                t.NameHash = Animator.StringToHash("AnyState -> " + (t.Dst?.Name ?? ""));
                t.FullPathHash = Animator.StringToHash("AnyState -> " + (t.Dst?.FullPath ?? ""));
                m.AnyState.Add(t);
            }
            foreach (var tr in Seq(n["m_EntryTransitions"]))
            {
                if (Ref(tr) is not Object to) continue;
                var t = Transition(to, states, machines); t.Entry = true; t.Owner = m;
                m.Entry.Add(t);
            }
            foreach (var e in Seq(n["m_StateMachineTransitions"]))
            {
                if (e is not YMap em || Ref(em["first"]) is not Object fo || !machines.TryGetValue(fo, out var from)) continue;
                var list = new List<CTransition>();
                foreach (var tr in Seq(em["second"])) if (Ref(tr) is Object to) { var t = Transition(to, states, machines); t.Owner = m; list.Add(t); }
                m.MachineTransitions[from] = list;
            }
            if (m.DefaultStateSource != null && states.TryGetValue(m.DefaultStateSource, out var d)) m.Default = d;
            m.Default ??= m.States.FirstOrDefault();
        }
    }
}
