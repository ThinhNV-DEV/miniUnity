// UnityEditor.Animations authoring API for MiniUnity: AnimatorController / AnimatorStateMachine / AnimatorState /
// AnimatorStateTransition objects keep their data as the serialized YAML of a .controller file (the same nodes the
// runtime model in ControllerModel.cs reads), so a controller built from an editor script plays immediately and is
// written to disk in Unity's format (AnimatorController 91, AnimatorStateMachine 1107, AnimatorState 1102,
// AnimatorStateTransition 1101) with every state, machine and transition as a sub-asset.
// AnimationUtility.SetEditorCurve / SetAnimationClipSettings / GetCurveBindings work on AnimationClip the same way.
using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity;
using MiniUnity.Yaml;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor.Animations
{
    internal static class AnimYaml
    {
        public static YSeq Seq(Object o, string key)
        {
            if (o.NGet(key) is YSeq s) return s;
            var n = new YSeq { Flow = false };
            o.NSet(key, n);
            return n;
        }
        public static YMap Vec3(Vector3 v) => new YMap { Flow = true }.Add("x", YScalar.Of(v.x)).Add("y", YScalar.Of(v.y)).Add("z", YScalar.Of(v.z));
        public static Object Ref(YNode n) => n is YRef r ? (r.Raw ? AssetLoader.ResolveExternal(r) : r.Target) : null;
        /// <summary>Make <paramref name="sub"/> a sub-asset of the asset <paramref name="owner"/> lives in (when it is persistent).</summary>
        public static void Persist(Object sub, Object owner)
        {
            if (owner?.m_Asset != null && sub.m_Asset == null) AssetDatabase.AddObjectToAsset(sub, owner);
            Touch();
        }
        /// <summary>Any edit invalidates the cached runtime models of every controller.</summary>
        public static void Touch() => MiniUnity.Anim.CController.EditGeneration++;
    }

    public sealed partial class AnimatorController
    {
        public AnimatorController()
        {
            NSetI("serializedVersion", 5);
            NSet("m_AnimatorParameters", new YSeq());
            NSet("m_AnimatorLayers", new YSeq());
        }

        public static AnimatorController CreateAnimatorControllerAtPath(string path)
        {
            var c = new AnimatorController { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(c, path);
            c.AddLayer("Base Layer");
            AssetDatabase.SaveAssets();
            return c;
        }

        public static AnimatorController CreateAnimatorControllerAtPathWithClip(string path, AnimationClip clip)
        {
            var c = CreateAnimatorControllerAtPath(path);
            c.AddMotion(clip);
            AssetDatabase.SaveAssets();
            return c;
        }

        public static AnimationClip AllocateAnimatorClip(string name) => new AnimationClip { name = name };

        public static void SetAnimatorController(Animator animator, AnimatorController controller) => animator.runtimeAnimatorController = controller;

        public AnimatorControllerParameter[] parameters
        {
            get => AnimYaml.Seq(this, "m_AnimatorParameters").Items.OfType<YMap>().Select(p => new AnimatorControllerParameter
            {
                name = (p["m_Name"] as YScalar)?.Value ?? "", type = (AnimatorControllerParameterType)((p["m_Type"] as YScalar)?.I ?? 1),
                defaultFloat = (p["m_DefaultFloat"] as YScalar)?.F ?? 0, defaultInt = (p["m_DefaultInt"] as YScalar)?.I ?? 0, defaultBool = (p["m_DefaultBool"] as YScalar)?.B ?? false,
            }).ToArray();
            set
            {
                var s = new YSeq();
                foreach (var p in value ?? Array.Empty<AnimatorControllerParameter>()) s.Items.Add(ParamNode(p));
                NSet("m_AnimatorParameters", s); m_Version++; AnimYaml.Touch();
            }
        }

        YMap ParamNode(AnimatorControllerParameter p) => new YMap().Add("m_Name", p.name).Add("m_Type", YScalar.Of((int)p.type))
            .Add("m_DefaultFloat", YScalar.Of(p.defaultFloat)).Add("m_DefaultInt", YScalar.Of(p.defaultInt)).Add("m_DefaultBool", YScalar.Of(p.defaultBool))
            .Add("m_Controller", new YRef(this));

        public void AddParameter(string name, AnimatorControllerParameterType type) => AddParameter(new AnimatorControllerParameter { name = MakeUniqueParameterName(name), type = type });
        public void AddParameter(AnimatorControllerParameter paramater)
        {
            AnimYaml.Seq(this, "m_AnimatorParameters").Items.Add(ParamNode(paramater));
            m_Version++; AnimYaml.Touch();
        }
        public void RemoveParameter(int index)
        {
            var s = AnimYaml.Seq(this, "m_AnimatorParameters");
            if (index >= 0 && index < s.Items.Count) s.Items.RemoveAt(index);
            m_Version++; AnimYaml.Touch();
        }
        public void RemoveParameter(AnimatorControllerParameter parameter) => RemoveParameter(Array.FindIndex(parameters, p => p.name == parameter.name));
        public string MakeUniqueParameterName(string name)
        {
            var names = new HashSet<string>(parameters.Select(p => p.name));
            if (!names.Contains(name)) return name;
            for (int i = 0; ; i++) if (!names.Contains($"{name} {i}")) return $"{name} {i}";
        }
        public string MakeUniqueLayerName(string name)
        {
            var names = new HashSet<string>(layers.Select(l => l.name));
            if (!names.Contains(name)) return name;
            for (int i = 0; ; i++) if (!names.Contains($"{name} {i}")) return $"{name} {i}";
        }

        public AnimatorControllerLayer[] layers
        {
            get => AnimYaml.Seq(this, "m_AnimatorLayers").Items.OfType<YMap>().Select(l => new AnimatorControllerLayer
            {
                name = (l["m_Name"] as YScalar)?.Value ?? "", stateMachine = AnimYaml.Ref(l["m_StateMachine"]) as AnimatorStateMachine,
                avatarMask = AnimYaml.Ref(l["m_Mask"]) as AvatarMask, blendingMode = (AnimatorLayerBlendingMode)((l["m_BlendingMode"] as YScalar)?.I ?? 0),
                syncedLayerIndex = (l["m_SyncedLayerIndex"] as YScalar)?.I ?? -1, defaultWeight = (l["m_DefaultWeight"] as YScalar)?.F ?? 0,
                iKPass = (l["m_IKPass"] as YScalar)?.B ?? false, syncedLayerAffectsTiming = (l["m_SyncedLayerAffectsTiming"] as YScalar)?.B ?? false,
            }).ToArray();
            set
            {
                var s = new YSeq();
                foreach (var l in value ?? Array.Empty<AnimatorControllerLayer>()) s.Items.Add(LayerNode(l));
                NSet("m_AnimatorLayers", s); m_Version++; AnimYaml.Touch();
            }
        }

        YMap LayerNode(AnimatorControllerLayer l) => new YMap().Add("serializedVersion", "5").Add("m_Name", l.name ?? "")
            .Add("m_StateMachine", new YRef(l.stateMachine)).Add("m_Mask", new YRef(l.avatarMask)).Add("m_Motions", new YSeq { Flow = true })
            .Add("m_Behaviours", new YSeq { Flow = true }).Add("m_BlendingMode", YScalar.Of((int)l.blendingMode)).Add("m_SyncedLayerIndex", YScalar.Of(l.syncedLayerIndex))
            .Add("m_DefaultWeight", YScalar.Of(l.defaultWeight)).Add("m_IKPass", YScalar.Of(l.iKPass)).Add("m_SyncedLayerAffectsTiming", YScalar.Of(l.syncedLayerAffectsTiming))
            .Add("m_Controller", new YRef(this));

        public void AddLayer(string name)
        {
            var sm = new AnimatorStateMachine { name = MakeUniqueLayerName(name) };
            AnimYaml.Persist(sm, this);
            AddLayer(new AnimatorControllerLayer { name = sm.name, stateMachine = sm, defaultWeight = layers.Length == 0 ? 1 : 0 });
        }
        public void AddLayer(AnimatorControllerLayer layer)
        {
            if (layer.stateMachine == null) { layer.stateMachine = new AnimatorStateMachine { name = layer.name }; }
            AnimYaml.Persist(layer.stateMachine, this);
            AnimYaml.Seq(this, "m_AnimatorLayers").Items.Add(LayerNode(layer));
            m_Version++; AnimYaml.Touch();
        }
        public void RemoveLayer(int index)
        {
            var s = AnimYaml.Seq(this, "m_AnimatorLayers");
            if (index >= 0 && index < s.Items.Count) s.Items.RemoveAt(index);
            m_Version++; AnimYaml.Touch();
        }

        public AnimatorState AddMotion(Motion motion) => AddMotion(motion, 0);
        public AnimatorState AddMotion(Motion motion, int layerIndex)
        {
            var sm = layers[layerIndex].stateMachine;
            var st = sm.AddState(motion != null ? motion.name : "New State");
            st.motion = motion;
            return st;
        }
        public AnimatorState CreateBlendTreeInController(string name, out BlendTree tree) => CreateBlendTreeInController(name, out tree, 0);
        public AnimatorState CreateBlendTreeInController(string name, out BlendTree tree, int layerIndex)
        {
            tree = new BlendTree { name = name };
            var p = parameters.FirstOrDefault(x => x.type == AnimatorControllerParameterType.Float);
            if (p == null) { AddParameter("Blend", AnimatorControllerParameterType.Float); tree.blendParameter = "Blend"; }
            else tree.blendParameter = p.name;
            tree.blendParameterY = tree.blendParameter;
            AnimYaml.Persist(tree, this);
            var st = layers[layerIndex].stateMachine.AddState(name);
            st.motion = tree;
            return st;
        }
        public void SetStateEffectiveMotion(AnimatorState state, Motion motion) => state.motion = motion;
        public void SetStateEffectiveMotion(AnimatorState state, Motion motion, int layerIndex) => state.motion = motion;
        public Motion GetStateEffectiveMotion(AnimatorState state) => state.motion;
        public Motion GetStateEffectiveMotion(AnimatorState state, int layerIndex) => state.motion;
    }

    public partial class AnimatorControllerLayer
    {
        public string name { get; set; } = "";
        public AnimatorStateMachine stateMachine { get; set; }
        public AvatarMask avatarMask { get; set; }
        public AnimatorLayerBlendingMode blendingMode { get; set; }
        public int syncedLayerIndex { get; set; } = -1;
        public bool iKPass { get; set; }
        public float defaultWeight { get; set; }
        public bool syncedLayerAffectsTiming { get; set; }
    }

    public partial struct ChildAnimatorState
    {
        public AnimatorState state { get; set; }
        public Vector3 position { get; set; }
    }

    public partial struct ChildAnimatorStateMachine
    {
        public AnimatorStateMachine stateMachine { get; set; }
        public Vector3 position { get; set; }
    }

    public partial struct AnimatorCondition
    {
        public AnimatorConditionMode mode { get; set; }
        public string parameter { get; set; }
        public float threshold { get; set; }
    }

    public sealed partial class AnimatorStateMachine
    {
        public AnimatorStateMachine()
        {
            NSetI("serializedVersion", 6);
            foreach (var k in new[] { "m_ChildStates", "m_ChildStateMachines", "m_AnyStateTransitions", "m_EntryTransitions", "m_StateMachineBehaviours" }) NSet(k, new YSeq());
            NSet("m_StateMachineTransitions", new YMap { Flow = true });
            NSet("m_AnyStatePosition", AnimYaml.Vec3(new Vector3(50, 20, 0)));
            NSet("m_EntryPosition", AnimYaml.Vec3(new Vector3(50, 120, 0)));
            NSet("m_ExitPosition", AnimYaml.Vec3(new Vector3(800, 120, 0)));
            NSet("m_ParentStateMachinePosition", AnimYaml.Vec3(new Vector3(800, 20, 0)));
            NSet("m_DefaultState", new YRef(null));
        }

        public ChildAnimatorState[] states
        {
            get => AnimYaml.Seq(this, "m_ChildStates").Items.OfType<YMap>().Select(m => new ChildAnimatorState { state = AnimYaml.Ref(m["m_State"]) as AnimatorState, position = Serializer.ReadV3(m["m_Position"]) }).ToArray();
            set
            {
                var s = new YSeq();
                foreach (var c in value ?? Array.Empty<ChildAnimatorState>()) s.Items.Add(new YMap().Add("serializedVersion", "1").Add("m_State", new YRef(c.state)).Add("m_Position", AnimYaml.Vec3(c.position)));
                NSet("m_ChildStates", s); AnimYaml.Touch();
            }
        }

        public ChildAnimatorStateMachine[] stateMachines
        {
            get => AnimYaml.Seq(this, "m_ChildStateMachines").Items.OfType<YMap>().Select(m => new ChildAnimatorStateMachine { stateMachine = AnimYaml.Ref(m["m_StateMachine"]) as AnimatorStateMachine, position = Serializer.ReadV3(m["m_Position"]) }).ToArray();
        }

        public AnimatorState defaultState
        {
            get => NRef<AnimatorState>("m_DefaultState");
            set { NSet("m_DefaultState", new YRef(value)); AnimYaml.Touch(); }
        }

        public AnimatorStateTransition[] anyStateTransitions => AnimYaml.Seq(this, "m_AnyStateTransitions").Items.Select(AnimYaml.Ref).OfType<AnimatorStateTransition>().ToArray();
        public AnimatorTransition[] entryTransitions => AnimYaml.Seq(this, "m_EntryTransitions").Items.Select(AnimYaml.Ref).OfType<AnimatorTransition>().ToArray();

        public AnimatorState AddState(string name) => AddState(name, new Vector3(300, 50 + 60 * AnimYaml.Seq(this, "m_ChildStates").Items.Count, 0));
        public AnimatorState AddState(string name, Vector3 position)
        {
            var st = new AnimatorState { name = MakeUniqueStateName(name) };
            AddState(st, position);
            return st;
        }
        public void AddState(AnimatorState state, Vector3 position)
        {
            AnimYaml.Persist(state, this);
            AnimYaml.Seq(this, "m_ChildStates").Items.Add(new YMap().Add("serializedVersion", "1").Add("m_State", new YRef(state)).Add("m_Position", AnimYaml.Vec3(position)));
            if (defaultState == null) defaultState = state;
            AnimYaml.Touch();
        }
        public void RemoveState(AnimatorState state)
        {
            AnimYaml.Seq(this, "m_ChildStates").Items.RemoveAll(i => i is YMap m && AnimYaml.Ref(m["m_State"]) == state);
            if (defaultState == state) defaultState = states.Select(s => s.state).FirstOrDefault();
            AnimYaml.Touch();
        }
        public AnimatorStateMachine AddStateMachine(string name) => AddStateMachine(name, Vector3.zero);
        public AnimatorStateMachine AddStateMachine(string name, Vector3 position)
        {
            var sm = new AnimatorStateMachine { name = name };
            AnimYaml.Persist(sm, this);
            AnimYaml.Seq(this, "m_ChildStateMachines").Items.Add(new YMap().Add("serializedVersion", "1").Add("m_StateMachine", new YRef(sm)).Add("m_Position", AnimYaml.Vec3(position)));
            AnimYaml.Touch();
            return sm;
        }
        public AnimatorStateTransition AddAnyStateTransition(AnimatorState destinationState)
        {
            var t = new AnimatorStateTransition { destinationState = destinationState };
            t.hasExitTime = false; t.duration = 0.25f; t.exitTime = 0.75f;
            AnimYaml.Persist(t, this);
            AnimYaml.Seq(this, "m_AnyStateTransitions").Items.Add(new YRef(t));
            AnimYaml.Touch();
            return t;
        }
        public AnimatorTransition AddEntryTransition(AnimatorState destinationState)
        {
            var t = new AnimatorTransition { destinationState = destinationState };
            AnimYaml.Persist(t, this);
            AnimYaml.Seq(this, "m_EntryTransitions").Items.Add(new YRef(t));
            AnimYaml.Touch();
            return t;
        }
        public string MakeUniqueStateName(string name)
        {
            var names = new HashSet<string>(states.Select(s => s.state?.name));
            if (!names.Contains(name)) return name;
            for (int i = 0; ; i++) if (!names.Contains($"{name} {i}")) return $"{name} {i}";
        }
    }

    public sealed partial class AnimatorState
    {
        public AnimatorState()
        {
            NSetI("serializedVersion", 6);
            NSetF("m_Speed", 1); NSetF("m_CycleOffset", 0);
            NSet("m_Transitions", new YSeq()); NSet("m_StateMachineBehaviours", new YSeq());
            NSet("m_Position", AnimYaml.Vec3(new Vector3(50, 50, 0)));
            NSetB("m_IKOnFeet", false); NSetB("m_WriteDefaultValues", true); NSetB("m_Mirror", false);
            NSetB("m_SpeedParameterActive", false); NSetB("m_MirrorParameterActive", false); NSetB("m_CycleOffsetParameterActive", false); NSetB("m_TimeParameterActive", false);
            NSet("m_Motion", new YRef(null)); NSetS("m_Tag", "");
            NSetS("m_SpeedParameter", ""); NSetS("m_MirrorParameter", ""); NSetS("m_CycleOffsetParameter", ""); NSetS("m_TimeParameter", "");
        }

        public int nameHash => Animator.StringToHash(name);
        public Motion motion { get => NRef<Motion>("m_Motion"); set { NSet("m_Motion", new YRef(value)); AnimYaml.Touch(); } }
        public float speed { get => NFloat("m_Speed", 1); set { NSetF("m_Speed", value); AnimYaml.Touch(); } }
        public float cycleOffset { get => NFloat("m_CycleOffset"); set { NSetF("m_CycleOffset", value); AnimYaml.Touch(); } }
        public bool mirror { get => NBool("m_Mirror"); set { NSetB("m_Mirror", value); AnimYaml.Touch(); } }
        public bool iKOnFeet { get => NBool("m_IKOnFeet"); set { NSetB("m_IKOnFeet", value); AnimYaml.Touch(); } }
        public bool writeDefaultValues { get => NBool("m_WriteDefaultValues", true); set { NSetB("m_WriteDefaultValues", value); AnimYaml.Touch(); } }
        public string tag { get => NStr("m_Tag"); set { NSetS("m_Tag", value ?? ""); AnimYaml.Touch(); } }
        public string speedParameter { get => NStr("m_SpeedParameter"); set { NSetS("m_SpeedParameter", value ?? ""); AnimYaml.Touch(); } }
        public string cycleOffsetParameter { get => NStr("m_CycleOffsetParameter"); set { NSetS("m_CycleOffsetParameter", value ?? ""); AnimYaml.Touch(); } }
        public string mirrorParameter { get => NStr("m_MirrorParameter"); set { NSetS("m_MirrorParameter", value ?? ""); AnimYaml.Touch(); } }
        public string timeParameter { get => NStr("m_TimeParameter"); set { NSetS("m_TimeParameter", value ?? ""); AnimYaml.Touch(); } }
        public bool speedParameterActive { get => NBool("m_SpeedParameterActive"); set { NSetB("m_SpeedParameterActive", value); AnimYaml.Touch(); } }
        public bool cycleOffsetParameterActive { get => NBool("m_CycleOffsetParameterActive"); set { NSetB("m_CycleOffsetParameterActive", value); AnimYaml.Touch(); } }
        public bool mirrorParameterActive { get => NBool("m_MirrorParameterActive"); set { NSetB("m_MirrorParameterActive", value); AnimYaml.Touch(); } }
        public bool timeParameterActive { get => NBool("m_TimeParameterActive"); set { NSetB("m_TimeParameterActive", value); AnimYaml.Touch(); } }
        public AnimatorStateTransition[] transitions
        {
            get => AnimYaml.Seq(this, "m_Transitions").Items.Select(AnimYaml.Ref).OfType<AnimatorStateTransition>().ToArray();
            set { var s = new YSeq(); foreach (var t in value ?? Array.Empty<AnimatorStateTransition>()) s.Items.Add(new YRef(t)); NSet("m_Transitions", s); AnimYaml.Touch(); }
        }
        public StateMachineBehaviour[] behaviours => AnimYaml.Seq(this, "m_StateMachineBehaviours").Items.Select(AnimYaml.Ref).OfType<StateMachineBehaviour>().ToArray();

        public void AddTransition(AnimatorStateTransition transition)
        {
            AnimYaml.Persist(transition, this);
            AnimYaml.Seq(this, "m_Transitions").Items.Add(new YRef(transition));
            AnimYaml.Touch();
        }
        public void RemoveTransition(AnimatorStateTransition transition)
        {
            AnimYaml.Seq(this, "m_Transitions").Items.RemoveAll(i => AnimYaml.Ref(i) == transition);
            AnimYaml.Touch();
        }
        public AnimatorStateTransition AddTransition(AnimatorState destinationState) => AddTransition(destinationState, false);
        public AnimatorStateTransition AddTransition(AnimatorState destinationState, bool defaultExitTime)
        {
            var t = NewTransition(defaultExitTime);
            t.destinationState = destinationState;
            AddTransition(t);
            return t;
        }
        public AnimatorStateTransition AddTransition(AnimatorStateMachine destinationStateMachine) => AddTransition(destinationStateMachine, false);
        public AnimatorStateTransition AddTransition(AnimatorStateMachine destinationStateMachine, bool defaultExitTime)
        {
            var t = NewTransition(defaultExitTime);
            t.destinationStateMachine = destinationStateMachine;
            AddTransition(t);
            return t;
        }
        public AnimatorStateTransition AddExitTransition() => AddExitTransition(false);
        public AnimatorStateTransition AddExitTransition(bool defaultExitTime)
        {
            var t = NewTransition(defaultExitTime);
            t.isExit = true;
            AddTransition(t);
            return t;
        }

        /// <summary>Unity's defaults for a transition made from code: 0.25 s fixed duration; exit time 0.75 when requested.</summary>
        static AnimatorStateTransition NewTransition(bool defaultExitTime)
        {
            var t = new AnimatorStateTransition { hasExitTime = defaultExitTime, exitTime = 0.75f, duration = 0.25f, hasFixedDuration = true };
            return t;
        }

        public StateMachineBehaviour AddStateMachineBehaviour(Type stateMachineBehaviourType)
        {
            var b = (StateMachineBehaviour)ScriptableObject.CreateInstance(stateMachineBehaviourType);
            b.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            AnimYaml.Persist(b, this);
            AnimYaml.Seq(this, "m_StateMachineBehaviours").Items.Add(new YRef(b));
            AnimYaml.Touch();
            return b;
        }
        public T AddStateMachineBehaviour<T>() where T : StateMachineBehaviour => (T)AddStateMachineBehaviour(typeof(T));
    }

    public partial class AnimatorTransitionBase
    {
        protected AnimatorTransitionBase()
        {
            NSet("m_Conditions", new YSeq()); NSet("m_DstStateMachine", new YRef(null)); NSet("m_DstState", new YRef(null));
            NSetB("m_Solo", false); NSetB("m_Mute", false); NSetB("m_IsExit", false);
        }
        public bool solo { get => NBool("m_Solo"); set { NSetB("m_Solo", value); AnimYaml.Touch(); } }
        public bool mute { get => NBool("m_Mute"); set { NSetB("m_Mute", value); AnimYaml.Touch(); } }
        public bool isExit { get => NBool("m_IsExit"); set { NSetB("m_IsExit", value); AnimYaml.Touch(); } }
        public AnimatorStateMachine destinationStateMachine { get => NRef<AnimatorStateMachine>("m_DstStateMachine"); set { NSet("m_DstStateMachine", new YRef(value)); AnimYaml.Touch(); } }
        public AnimatorState destinationState { get => NRef<AnimatorState>("m_DstState"); set { NSet("m_DstState", new YRef(value)); AnimYaml.Touch(); } }
        public AnimatorCondition[] conditions
        {
            get => AnimYaml.Seq(this, "m_Conditions").Items.OfType<YMap>().Select(c => new AnimatorCondition
            {
                mode = (AnimatorConditionMode)((c["m_ConditionMode"] as YScalar)?.I ?? 1), parameter = (c["m_ConditionEvent"] as YScalar)?.Value ?? "", threshold = (c["m_EventTreshold"] as YScalar)?.F ?? 0,
            }).ToArray();
            set
            {
                var s = new YSeq();
                foreach (var c in value ?? Array.Empty<AnimatorCondition>()) s.Items.Add(CondNode(c.mode, c.threshold, c.parameter));
                NSet("m_Conditions", s); AnimYaml.Touch();
            }
        }
        static YMap CondNode(AnimatorConditionMode mode, float threshold, string parameter) =>
            new YMap().Add("m_ConditionMode", YScalar.Of((int)mode)).Add("m_ConditionEvent", parameter ?? "").Add("m_EventTreshold", YScalar.Of(threshold));
        public void AddCondition(AnimatorConditionMode mode, float threshold, string parameter)
        {
            AnimYaml.Seq(this, "m_Conditions").Items.Add(CondNode(mode, threshold, parameter));
            AnimYaml.Touch();
        }
        public void RemoveCondition(AnimatorCondition condition)
        {
            AnimYaml.Seq(this, "m_Conditions").Items.RemoveAll(i => i is YMap m && (m["m_ConditionEvent"] as YScalar)?.Value == condition.parameter
                && (m["m_ConditionMode"] as YScalar)?.I == (int)condition.mode);
            AnimYaml.Touch();
        }
    }

    public partial class AnimatorStateTransition
    {
        public AnimatorStateTransition()
        {
            NSetI("serializedVersion", 3);
            NSetF("m_TransitionDuration", 0.25f); NSetF("m_TransitionOffset", 0); NSetF("m_ExitTime", 0.75f);
            NSetB("m_HasExitTime", false); NSetB("m_HasFixedDuration", true); NSetI("m_InterruptionSource", 0);
            NSetB("m_OrderedInterruption", true); NSetB("m_CanTransitionToSelf", true);
        }
        public float duration { get => NFloat("m_TransitionDuration"); set { NSetF("m_TransitionDuration", value); AnimYaml.Touch(); } }
        public float offset { get => NFloat("m_TransitionOffset"); set { NSetF("m_TransitionOffset", value); AnimYaml.Touch(); } }
        public TransitionInterruptionSource interruptionSource { get => (TransitionInterruptionSource)NInt("m_InterruptionSource"); set { NSetI("m_InterruptionSource", (int)value); AnimYaml.Touch(); } }
        public bool orderedInterruption { get => NBool("m_OrderedInterruption", true); set { NSetB("m_OrderedInterruption", value); AnimYaml.Touch(); } }
        public float exitTime { get => NFloat("m_ExitTime"); set { NSetF("m_ExitTime", value); AnimYaml.Touch(); } }
        public bool hasExitTime { get => NBool("m_HasExitTime"); set { NSetB("m_HasExitTime", value); AnimYaml.Touch(); } }
        public bool hasFixedDuration { get => NBool("m_HasFixedDuration", true); set { NSetB("m_HasFixedDuration", value); AnimYaml.Touch(); } }
        public bool canTransitionToSelf { get => NBool("m_CanTransitionToSelf", true); set { NSetB("m_CanTransitionToSelf", value); AnimYaml.Touch(); } }
    }

    public partial class AnimatorTransition
    {
        public AnimatorTransition() { NSetI("serializedVersion", 1); }
    }
}

namespace UnityEditor
{
    public partial struct EditorCurveBinding
    {
        internal Type m_Type; internal bool m_PPtr, m_Discrete;
        public Type type { get => m_Type; set => m_Type = value; }
        public bool isPPtrCurve => m_PPtr;
        public bool isDiscreteCurve => m_Discrete;
        public bool isSerializeReferenceCurve => false;
        public static EditorCurveBinding FloatCurve(string inPath, Type inType, string inPropertyName) => new() { path = inPath, m_Type = inType, propertyName = inPropertyName };
        public static EditorCurveBinding PPtrCurve(string inPath, Type inType, string inPropertyName) => new() { path = inPath, m_Type = inType, propertyName = inPropertyName, m_PPtr = true };
        public static EditorCurveBinding DiscreteCurve(string inPath, Type inType, string inPropertyName) => new() { path = inPath, m_Type = inType, propertyName = inPropertyName, m_Discrete = true };
        public bool Equals(EditorCurveBinding other) => path == other.path && propertyName == other.propertyName && m_Type == other.m_Type && m_PPtr == other.m_PPtr;
        public override bool Equals(object other) => other is EditorCurveBinding b && Equals(b);
        public override int GetHashCode() => HashCode.Combine(path, propertyName, m_Type);
        public static bool operator ==(EditorCurveBinding lhs, EditorCurveBinding rhs) => lhs.Equals(rhs);
        public static bool operator !=(EditorCurveBinding lhs, EditorCurveBinding rhs) => !lhs.Equals(rhs);
    }

    public partial class AnimationUtility
    {
        public static void SetEditorCurve(AnimationClip clip, EditorCurveBinding binding, AnimationCurve curve) => clip.SetCurve(binding.path, binding.type, binding.propertyName, curve);
        public static AnimationCurve GetEditorCurve(AnimationClip clip, EditorCurveBinding binding)
        {
            foreach (var b in MiniUnity.Anim.ClipData.Of(clip).Floats)
                if (b.Path == binding.path && (b.Attribute == binding.propertyName || b.Attribute == TransformAttribute(binding.propertyName))) return b.Curve;
            return null;
        }
        public static EditorCurveBinding[] GetCurveBindings(AnimationClip clip) =>
            MiniUnity.Anim.ClipData.Of(clip).Floats.Select(b => new EditorCurveBinding { path = b.Path, propertyName = b.Attribute, type = b.ClassId == 4 ? typeof(Transform) : b.ScriptType ?? typeof(Object) }).ToArray();
        public static AnimationClipSettings GetAnimationClipSettings(AnimationClip clip)
        {
            var s = clip.NGet("m_AnimationClipSettings") as YMap;
            float F(string k, float d = 0) => (s?[k] as YScalar)?.F ?? d;
            return new AnimationClipSettings { startTime = F("m_StartTime"), stopTime = F("m_StopTime", clip.length), loopTime = F("m_LoopTime") != 0, loopBlend = F("m_LoopBlend") != 0, cycleOffset = F("m_CycleOffset"),
                hasAdditiveReferencePose = F("m_HasAdditiveReferencePose") != 0, additiveReferencePoseTime = F("m_AdditiveReferencePoseTime"),
                additiveReferencePoseClip = UnityEditor.Animations.AnimYaml.Ref(s?["m_AdditiveReferencePoseClip"]) as AnimationClip, level = F("m_Level"), orientationOffsetY = F("m_OrientationOffsetY") };
        }
        public static void SetAnimationClipSettings(AnimationClip clip, AnimationClipSettings srcClipInfo)
        {
            var s = clip.NGet("m_AnimationClipSettings") as YMap ?? new YMap();
            s["serializedVersion"] = new YScalar("2");
            s["m_StartTime"] = YScalar.Of(srcClipInfo.startTime); s["m_StopTime"] = YScalar.Of(srcClipInfo.stopTime);
            s["m_LoopTime"] = YScalar.Of(srcClipInfo.loopTime); s["m_LoopBlend"] = YScalar.Of(srcClipInfo.loopBlend);
            s["m_CycleOffset"] = YScalar.Of(srcClipInfo.cycleOffset);
            s["m_HasAdditiveReferencePose"] = YScalar.Of(srcClipInfo.hasAdditiveReferencePose); s["m_AdditiveReferencePoseTime"] = YScalar.Of(srcClipInfo.additiveReferencePoseTime);
            s["m_AdditiveReferencePoseClip"] = new YRef(srcClipInfo.additiveReferencePoseClip);
            s["m_Level"] = YScalar.Of(srcClipInfo.level); s["m_OrientationOffsetY"] = YScalar.Of(srcClipInfo.orientationOffsetY);
            clip.NSet("m_AnimationClipSettings", s);
            clip.m_Version++;
        }
        // Keyframe.tangentMode bits (Unity): 0 broken, 1-4 left TangentMode, 5-8 right TangentMode
        public static void SetKeyLeftTangentMode(AnimationCurve curve, int index, TangentMode tangentMode) => EditKey(curve, index, k =>
        {
            k.m_TangentMode = (k.m_TangentMode & ~(0xF << 1)) | ((int)tangentMode << 1);
            if (tangentMode is TangentMode.Linear or TangentMode.Constant or TangentMode.Free) k.m_TangentMode |= 1;
            return k;
        });
        public static void SetKeyRightTangentMode(AnimationCurve curve, int index, TangentMode tangentMode) => EditKey(curve, index, k =>
        {
            k.m_TangentMode = (k.m_TangentMode & ~(0xF << 5)) | ((int)tangentMode << 5);
            if (tangentMode is TangentMode.Linear or TangentMode.Constant or TangentMode.Free) k.m_TangentMode |= 1;
            return k;
        });
        public static void SetKeyBroken(AnimationCurve curve, int index, bool broken) => EditKey(curve, index, k => { k.m_TangentMode = broken ? k.m_TangentMode | 1 : k.m_TangentMode & ~1; return k; });
        public static TangentMode GetKeyLeftTangentMode(AnimationCurve curve, int index) => (TangentMode)((curve[index].m_TangentMode >> 1) & 0xF);
        public static TangentMode GetKeyRightTangentMode(AnimationCurve curve, int index) => (TangentMode)((curve[index].m_TangentMode >> 5) & 0xF);
        public static bool GetKeyBroken(AnimationCurve curve, int index) => (curve[index].m_TangentMode & 1) != 0;

        static void EditKey(AnimationCurve curve, int index, Func<Keyframe, Keyframe> edit)
        {
            if (curve == null || index < 0 || index >= curve.length) return;
            var keys = curve.keys;
            keys[index] = edit(keys[index]);
            for (int i = Math.Max(0, index - 1); i <= Math.Min(keys.Length - 1, index + 1); i++) UpdateTangents(keys, i);
            curve.keys = keys;
        }

        /// <summary>Recompute the tangents a key's modes imply (CurveUtility.UpdateTangentsFromMode).</summary>
        static void UpdateTangents(Keyframe[] keys, int i)
        {
            var k = keys[i];
            var left = (TangentMode)((k.m_TangentMode >> 1) & 0xF); var right = (TangentMode)((k.m_TangentMode >> 5) & 0xF);
            float Slope(int a, int b) => keys[b].time - keys[a].time > 1e-9f ? (keys[b].value - keys[a].value) / (keys[b].time - keys[a].time) : 0;
            float Smooth()
            {
                if (i == 0 || i == keys.Length - 1) return 0;
                return Slope(i - 1, i + 1);
            }
            k.inTangent = left switch { TangentMode.Linear => i > 0 ? Slope(i - 1, i) : 0, TangentMode.Constant => float.PositiveInfinity, TangentMode.Free => k.inTangent, _ => Smooth() };
            k.outTangent = right switch { TangentMode.Linear => i < keys.Length - 1 ? Slope(i, i + 1) : 0, TangentMode.Constant => float.PositiveInfinity, TangentMode.Free => k.outTangent, _ => Smooth() };
            keys[i] = k;
        }

        static string TransformAttribute(string p) => p.StartsWith("localScale.") ? "m_LocalScale." + p.Substring(11) : p.StartsWith("localPosition.") ? "m_LocalPosition." + p.Substring(14) : p;
    }
}

namespace UnityEditor.Animations
{


    public partial struct ChildMotion
    {
        public Motion motion { get; set; }
        public float threshold { get; set; }
        public Vector2 position { get; set; }
        public float timeScale { get; set; }
        public float cycleOffset { get; set; }
        public string directBlendParameter { get; set; }
        public bool mirror { get; set; }
    }

    /// <summary>Blend trees authored from code; stored in the same YAML shape as a .controller's BlendTree (206) documents.</summary>
    public partial class BlendTree
    {
        public BlendTree()
        {
            NSet("m_Childs", new YSeq()); NSetS("m_BlendParameter", "Blend"); NSetS("m_BlendParameterY", "Blend");
            NSetF("m_MinThreshold", 0); NSetF("m_MaxThreshold", 1); NSetB("m_UseAutomaticThresholds", true);
            NSetB("m_NormalizedBlendValues", false); NSetI("m_BlendType", 0);
        }

        public string blendParameter { get => NStr("m_BlendParameter"); set { NSetS("m_BlendParameter", value ?? ""); AnimYaml.Touch(); } }
        public string blendParameterY { get => NStr("m_BlendParameterY"); set { NSetS("m_BlendParameterY", value ?? ""); AnimYaml.Touch(); } }
        public BlendTreeType blendType { get => (BlendTreeType)NInt("m_BlendType"); set { NSetI("m_BlendType", (int)value); AnimYaml.Touch(); } }
        public bool useAutomaticThresholds { get => NBool("m_UseAutomaticThresholds", true); set { NSetB("m_UseAutomaticThresholds", value); Thresholds(); } }
        public float minThreshold { get => NFloat("m_MinThreshold"); set { NSetF("m_MinThreshold", value); Thresholds(); } }
        public float maxThreshold { get => NFloat("m_MaxThreshold", 1); set { NSetF("m_MaxThreshold", value); Thresholds(); } }

        public ChildMotion[] children
        {
            get => AnimYaml.Seq(this, "m_Childs").Items.OfType<YMap>().Select(c => new ChildMotion
            {
                motion = AnimYaml.Ref(c["m_Motion"]) as Motion, threshold = (c["m_Threshold"] as YScalar)?.F ?? 0,
                position = MiniUnity.Serializer.ReadV2(c["m_Position"]), timeScale = (c["m_TimeScale"] as YScalar)?.F ?? 1,
                cycleOffset = (c["m_CycleOffset"] as YScalar)?.F ?? 0, directBlendParameter = (c["m_DirectBlendParameter"] as YScalar)?.Value ?? "",
                mirror = ((c["m_Mirror"] as YScalar)?.I ?? 0) != 0,
            }).ToArray();
            set
            {
                var seq = new YSeq();
                foreach (var c in value ?? Array.Empty<ChildMotion>())
                    seq.Items.Add(new YMap().Add("serializedVersion", YScalar.Of(2)).Add("m_Motion", new YRef(c.motion)).Add("m_Threshold", YScalar.Of(c.threshold))
                        .Add("m_Position", new YMap { Flow = true }.Add("x", YScalar.Of(c.position.x)).Add("y", YScalar.Of(c.position.y)))
                        .Add("m_TimeScale", YScalar.Of(c.timeScale)).Add("m_CycleOffset", YScalar.Of(c.cycleOffset))
                        .Add("m_DirectBlendParameter", new YScalar(c.directBlendParameter ?? "")).Add("m_Mirror", YScalar.Of(c.mirror ? 1 : 0)));
                NSet("m_Childs", seq);
                AnimYaml.Touch();
            }
        }

        void Add(Motion motion, float threshold, Vector2 position)
        {
            var l = children.ToList();
            l.Add(new ChildMotion { motion = motion, threshold = threshold, position = position, timeScale = 1, directBlendParameter = blendParameter });
            children = l.ToArray();
            if (motion is BlendTree sub) AnimYaml.Persist(sub, this);
        }

        /// <summary>Automatic thresholds spread the 1D children evenly over [minThreshold, maxThreshold].</summary>
        void Thresholds()
        {
            if (!useAutomaticThresholds) { AnimYaml.Touch(); return; }
            var c = children;
            for (int i = 0; i < c.Length; i++) c[i].threshold = c.Length < 2 ? minThreshold : Mathf.Lerp(minThreshold, maxThreshold, i / (float)(c.Length - 1));
            children = c;
        }

        public void AddChild(Motion motion) { Add(motion, 0, Vector2.zero); Thresholds(); }
        public void AddChild(Motion motion, Vector2 position) => Add(motion, 0, position);
        public void AddChild(Motion motion, float threshold) { Add(motion, threshold, Vector2.zero); if (useAutomaticThresholds) NSetB("m_UseAutomaticThresholds", false); }
        public void RemoveChild(int index) { var l = children.ToList(); l.RemoveAt(index); children = l.ToArray(); }
        public BlendTree CreateBlendTreeChild(float threshold) { var t = new BlendTree { name = "BlendTree" }; AddChild(t, threshold); return t; }
        public BlendTree CreateBlendTreeChild(Vector2 position) { var t = new BlendTree { name = "BlendTree" }; AddChild(t, position); return t; }
    }
}
