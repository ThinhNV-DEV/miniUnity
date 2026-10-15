// UnityEngine animation API (AnimationModule) over MiniUnity.Anim: Animator (Mecanim emulation), AnimationClip (curves,
// events, SampleAnimation), AnimatorOverrideController, StateMachineBehaviour, legacy Animation / AnimationState.
// Public surface follows the Unity 2022.3 bindings (UnityCsReference Modules/Animation).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MiniUnity;
using MiniUnity.Anim;
using MiniUnity.Yaml;

namespace UnityEngine
{
    // ====================================================================================================== info structs
    public partial struct AnimatorStateInfo
    {
        internal int m_Name, m_Path, m_FullPath, m_Tag, m_Loop; internal float m_NormalizedTime, m_Length, m_Speed, m_SpeedMultiplier;
        public bool IsName(string name) { int h = Animator.StringToHash(name); return h == m_FullPath || h == m_Name || h == m_Path; }
        public int fullPathHash => m_FullPath;
        public int nameHash => m_Path;
        public int shortNameHash => m_Name;
        public float normalizedTime => m_NormalizedTime;
        public float length => m_Length;
        public float speed => m_Speed;
        public float speedMultiplier => m_SpeedMultiplier;
        public int tagHash => m_Tag;
        public bool IsTag(string tag) => Animator.StringToHash(tag) == m_Tag;
        public bool loop => m_Loop != 0;
    }

    public partial struct AnimatorTransitionInfo
    {
        internal int m_FullPath, m_UserName, m_Name, m_TransitionType; internal bool m_HasFixedDuration, m_AnyState; internal float m_Duration, m_NormalizedTime;
        public bool IsName(string name) => Animator.StringToHash(name) == m_Name || Animator.StringToHash(name) == m_FullPath;
        public bool IsUserName(string name) => Animator.StringToHash(name) == m_UserName;
        public int fullPathHash => m_FullPath;
        public int nameHash => m_Name;
        public int userNameHash => m_UserName;
        public DurationUnit durationUnit => m_HasFixedDuration ? DurationUnit.Fixed : DurationUnit.Normalized;
        public float duration => m_Duration;
        public float normalizedTime => m_NormalizedTime;
        public bool anyState => m_AnyState;
        internal bool entry => (m_TransitionType & 2) != 0;
        internal bool exit => (m_TransitionType & 4) != 0;
    }

    public partial struct AnimatorClipInfo
    {
        internal AnimationClip m_ClipRef; internal float m_Weight;
        public AnimationClip clip => m_ClipRef;
        public float weight => m_Weight;
    }

    public partial class AnimatorControllerParameter
    {
        public AnimatorControllerParameter() { }
        public string name { get => m_Name; set => m_Name = value; }
        public int nameHash => Animator.StringToHash(m_Name);
        public AnimatorControllerParameterType type { get => m_Type; set => m_Type = value; }
        public float defaultFloat { get => m_DefaultFloat; set => m_DefaultFloat = value; }
        public int defaultInt { get => m_DefaultInt; set => m_DefaultInt = value; }
        public bool defaultBool { get => m_DefaultBool; set => m_DefaultBool = value; }
        public override bool Equals(object o) => o is AnimatorControllerParameter p && m_Name == p.m_Name && m_Type == p.m_Type && m_DefaultFloat == p.m_DefaultFloat && m_DefaultInt == p.m_DefaultInt && m_DefaultBool == p.m_DefaultBool;
        public override int GetHashCode() => name?.GetHashCode() ?? 0;
    }

    public partial class TrackedReference
    {
        public static bool operator ==(TrackedReference x, TrackedReference y) => ReferenceEquals(x, y) || (x is null ? !(y is object) : y is null ? false : ReferenceEquals(x, y));
        public static bool operator !=(TrackedReference x, TrackedReference y) => !(x == y);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static implicit operator bool(TrackedReference exists) => exists is object;
    }

    // ====================================================================================================== StateMachineBehaviour
    public abstract partial class StateMachineBehaviour
    {
        protected StateMachineBehaviour() { }
        public virtual void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
        public virtual void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
        public virtual void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
        public virtual void OnStateMove(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
        public virtual void OnStateIK(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
        public virtual void OnStateMachineEnter(Animator animator, int stateMachinePathHash) { }
        public virtual void OnStateMachineExit(Animator animator, int stateMachinePathHash) { }
        public virtual void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex, Animations.AnimatorControllerPlayable controller) { }
        public virtual void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex, Animations.AnimatorControllerPlayable controller) { }
        public virtual void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex, Animations.AnimatorControllerPlayable controller) { }
        public virtual void OnStateMove(Animator animator, AnimatorStateInfo stateInfo, int layerIndex, Animations.AnimatorControllerPlayable controller) { }
        public virtual void OnStateIK(Animator animator, AnimatorStateInfo stateInfo, int layerIndex, Animations.AnimatorControllerPlayable controller) { }
        public virtual void OnStateMachineEnter(Animator animator, int stateMachinePathHash, Animations.AnimatorControllerPlayable controller) { }
        public virtual void OnStateMachineExit(Animator animator, int stateMachinePathHash, Animations.AnimatorControllerPlayable controller) { }
    }

    // ====================================================================================================== assets
    public partial class Motion
    {
        protected Motion() { }
        public float averageDuration => this is AnimationClip c ? c.length : 0;
        public float averageAngularSpeed => 0;
        public Vector3 averageSpeed => Vector3.zero;
        public float apparentSpeed => 0;
        public bool isLooping => this is AnimationClip c && ClipData.Of(c).Loop;
        public bool legacy => this is AnimationClip c && c.m_LegacyFlag;
        public bool isHumanMotion => false;
    }

    public partial class RuntimeAnimatorController
    {
        internal int m_Version;
        protected RuntimeAnimatorController() { }
        public AnimationClip[] animationClips => CController.Of(this)?.Clips.ToArray() ?? Array.Empty<AnimationClip>();
    }

    public partial class AnimatorOverrideController
    {
        RuntimeAnimatorController m_ControllerRef; bool m_ControllerSet;
        readonly Dictionary<AnimationClip, AnimationClip> m_ScriptOverrides = new();
        public AnimatorOverrideController() { }
        public AnimatorOverrideController(RuntimeAnimatorController controller) { runtimeAnimatorController = controller; }
        public RuntimeAnimatorController runtimeAnimatorController
        {
            get => m_ControllerSet ? m_ControllerRef : NRef<RuntimeAnimatorController>("m_Controller");
            set { m_ControllerRef = value; m_ControllerSet = true; m_Version++; }
        }
        internal IEnumerable<(AnimationClip original, AnimationClip over)> OverridePairs()
        {
            var map = new Dictionary<AnimationClip, AnimationClip>();
            if (NGet("m_Clips") is YSeq s)
                foreach (var i in s.Items)
                    if (i is YMap m)
                    {
                        var o = (m["m_OriginalClip"] as YRef) is YRef ro ? (ro.Raw ? AssetLoader.ResolveExternal(ro) : ro.Target) as AnimationClip : null;
                        var n = (m["m_OverrideClip"] as YRef) is YRef rn ? (rn.Raw ? AssetLoader.ResolveExternal(rn) : rn.Target) as AnimationClip : null;
                        if (o != null) map[o] = n;
                    }
            foreach (var (k, v) in m_ScriptOverrides) map[k] = v;
            return map.Select(kv => (kv.Key, kv.Value));
        }
        IEnumerable<AnimationClip> BaseClips() => runtimeAnimatorController is RuntimeAnimatorController rac && rac != this ? CController.Of(rac)?.Clips ?? new List<AnimationClip>() : new List<AnimationClip>();
        public AnimationClip this[string name]
        {
            get { var c = BaseClips().FirstOrDefault(x => x.name == name); return c == null ? null : this[c]; }
            set { var c = BaseClips().FirstOrDefault(x => x.name == name); if (c != null) this[c] = value; }
        }
        public AnimationClip this[AnimationClip clip]
        {
            get { foreach (var (o, n) in OverridePairs()) if (o == clip) return n ?? clip; return clip; }
            set { m_ScriptOverrides[clip] = value; m_Version++; }
        }
        public int overridesCount => BaseClips().Count();
        public AnimationClipPair[] clips
        {
            get => BaseClips().Select(c => new AnimationClipPair { originalClip = c, overrideClip = this[c] == c ? null : this[c] }).ToArray();
            set { foreach (var p in value ?? Array.Empty<AnimationClipPair>()) if (p.originalClip != null) m_ScriptOverrides[p.originalClip] = p.overrideClip; m_Version++; }
        }
        public void GetOverrides(List<KeyValuePair<AnimationClip, AnimationClip>> overrides)
        {
            overrides.Clear();
            foreach (var c in BaseClips()) { var o = this[c]; overrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(c, o == c ? null : o)); }
        }
        public void ApplyOverrides(IList<KeyValuePair<AnimationClip, AnimationClip>> overrides)
        {
            foreach (var kv in overrides) if (kv.Key != null) m_ScriptOverrides[kv.Key] = kv.Value;
            m_Version++;
        }
        internal void PerformOverrideClipListCleanup() { }
    }

    public partial class AnimationClip
    {
        internal int m_Version;
        internal readonly List<FloatBinding> m_ScriptCurves = new();
        internal readonly List<AnimationEvent> m_ScriptEvents = new();
        internal bool m_EventsOverridden, m_LegacyFlagSet, m_LegacyValue;
        internal bool m_LegacyFlag => m_LegacyFlagSet ? m_LegacyValue : NBool("m_Legacy");
        public AnimationClip() { }

        public float length => ClipData.Of(this).Length;
        internal float startTime => ClipData.Of(this).StartTime;
        internal float stopTime => ClipData.Of(this).StopTime;
        public float frameRate { get => NFloat("m_SampleRate", 60); set { NSetF("m_SampleRate", value); m_Version++; } }
        public WrapMode wrapMode { get => (WrapMode)NInt("m_WrapMode"); set { NSetI("m_WrapMode", (int)value); m_Version++; } }
        public Bounds localBounds { get; set; }
        public bool legacy { get => m_LegacyFlag; set { m_LegacyFlagSet = true; m_LegacyValue = value; NSetB("m_Legacy", value); m_Version++; } }
        public bool humanMotion => false;
        public bool empty => ClipData.Of(this).Floats.Count == 0 && ClipData.Of(this).PPtrs.Count == 0;
        public bool hasGenericRootTransform => NBool("m_HasGenericRootTransform");
        public bool hasMotionFloatCurves => NBool("m_HasMotionFloatCurves");
        public bool hasMotionCurves => false;
        public bool hasRootCurves => ClipData.Of(this).HasRootCurves;
        internal bool hasRootMotion => hasGenericRootTransform;
        public AnimationEvent[] events
        {
            get => ClipData.Of(this).Events.Select(e => e.Clone()).ToArray();
            set { m_ScriptEvents.Clear(); if (value != null) m_ScriptEvents.AddRange(value.Select(e => e.Clone())); m_EventsOverridden = true; NSet("m_Events", new YSeq()); m_Version++; }
        }
        public void AddEvent(AnimationEvent evt) { if (evt == null) throw new ArgumentNullException(nameof(evt)); m_ScriptEvents.Add(evt.Clone()); m_Version++; }

        public void SetCurve(string relativePath, Type type, string propertyName, AnimationCurve curve)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            relativePath ??= "";
            string attr = propertyName;
            int classId;
            Type script = null;
            if (typeof(Transform).IsAssignableFrom(type))
            {
                classId = 4;
                attr = propertyName switch
                {
                    var p when p.StartsWith("localPosition.") => "m_LocalPosition." + p.Substring(14),
                    var p when p.StartsWith("localRotation.") => "m_LocalRotation." + p.Substring(14),
                    var p when p.StartsWith("localScale.") => "m_LocalScale." + p.Substring(11),
                    var p when p.StartsWith("localEulerAngles.") => "localEulerAnglesRaw." + p.Substring(17),
                    var p when p.StartsWith("localEulerAnglesRaw.") => p,
                    _ => propertyName,
                };
            }
            else if (type == typeof(GameObject)) classId = 1;
            else if (typeof(MonoBehaviour).IsAssignableFrom(type)) { classId = 114; script = type; }
            else classId = Serializer.ClassOf(type).id;
            m_ScriptCurves.RemoveAll(b => b.Path == relativePath && b.Attribute == attr && b.ClassId == classId && b.ScriptType == script);
            if (curve != null) m_ScriptCurves.Add(new FloatBinding { Path = relativePath, Attribute = attr, ClassId = classId, ScriptType = script, Curve = curve });
            m_Version++;
        }
        /// <summary>
        /// Write the curves set from script into the clip's serialized arrays the way the Unity editor saves an .anim:
        /// m_EditorCurves for every binding, and the runtime arrays (m_PositionCurves / m_ScaleCurves for complete xyz
        /// transform curves, m_FloatCurves for the rest). Called when the clip is written to disk.
        /// </summary>
        internal void BakeScriptCurves()
        {
            if (m_ScriptCurves.Count == 0) return;
            YSeq S(string k) { if (NGet(k) is YSeq q) return q; var n = new YSeq(); NSet(k, n); return n; }
            YNode ScriptRef(Type t) => t != null ? ScriptRegistry.ScriptRef(t) : new YRef(null);
            float maxKey = 0;
            foreach (var b in m_ScriptCurves)
            {
                if (b.Curve.length > 0) maxKey = Math.Max(maxKey, b.Curve[b.Curve.length - 1].time);
                S("m_EditorCurves").Items.Add(new YMap().Add("curve", Serializer.CurveNode(b.Curve)).Add("attribute", b.Attribute).Add("path", b.Path)
                    .Add("classID", YScalar.Of(b.ClassId)).Add("script", ScriptRef(b.ScriptType)));
            }
            var used = new HashSet<FloatBinding>();
            foreach (var (prefix, key) in new[] { ("m_LocalPosition.", "m_PositionCurves"), ("m_LocalScale.", "m_ScaleCurves") })
                foreach (var g in m_ScriptCurves.Where(b => b.ClassId == 4 && b.Attribute.StartsWith(prefix)).GroupBy(b => b.Path))
                {
                    var x = g.FirstOrDefault(b => b.Attribute.EndsWith(".x")); var y = g.FirstOrDefault(b => b.Attribute.EndsWith(".y")); var z = g.FirstOrDefault(b => b.Attribute.EndsWith(".z"));
                    if (x == null || y == null || z == null) continue;
                    used.Add(x); used.Add(y); used.Add(z);
                    var times = new[] { x, y, z }.SelectMany(b => b.Curve.keys.Select(k => k.time)).Distinct().OrderBy(t => t).ToList();
                    var keys = new YSeq();
                    YMap V(float a, float b2, float c) => new YMap { Flow = true }.Add("x", YScalar.Of(a)).Add("y", YScalar.Of(b2)).Add("z", YScalar.Of(c));
                    float Slope(AnimationCurve c, float t, bool outSide)
                    {
                        foreach (var k in c.keys) if (Math.Abs(k.time - t) < 1e-6f) return outSide ? k.outTangent : k.inTangent;
                        const float h = 1e-3f; return (c.Evaluate(t + h) - c.Evaluate(t - h)) / (2 * h);
                    }
                    foreach (var t in times)
                        keys.Items.Add(new YMap().Add("serializedVersion", "3").Add("time", YScalar.Of(t))
                            .Add("value", V(x.Curve.Evaluate(t), y.Curve.Evaluate(t), z.Curve.Evaluate(t)))
                            .Add("inSlope", V(Slope(x.Curve, t, false), Slope(y.Curve, t, false), Slope(z.Curve, t, false)))
                            .Add("outSlope", V(Slope(x.Curve, t, true), Slope(y.Curve, t, true), Slope(z.Curve, t, true)))
                            .Add("tangentMode", YScalar.Of(0)).Add("weightedMode", YScalar.Of(0))
                            .Add("inWeight", V(1 / 3f, 1 / 3f, 1 / 3f)).Add("outWeight", V(1 / 3f, 1 / 3f, 1 / 3f)));
                    var curve = new YMap().Add("serializedVersion", "2").Add("m_Curve", keys).Add("m_PreInfinity", YScalar.Of(2)).Add("m_PostInfinity", YScalar.Of(2)).Add("m_RotationOrder", YScalar.Of(4));
                    S(key).Items.Add(new YMap().Add("curve", curve).Add("path", g.Key));
                }
            foreach (var b in m_ScriptCurves.Where(b => !used.Contains(b)))
                S("m_FloatCurves").Items.Add(new YMap().Add("curve", Serializer.CurveNode(b.Curve)).Add("attribute", b.Attribute).Add("path", b.Path)
                    .Add("classID", YScalar.Of(b.ClassId)).Add("script", ScriptRef(b.ScriptType)));
            var settings = NGet("m_AnimationClipSettings") as YMap;
            if (settings == null)
            {
                settings = new YMap().Add("serializedVersion", "2").Add("m_StartTime", YScalar.Of(0f)).Add("m_StopTime", YScalar.Of(maxKey))
                    .Add("m_LoopTime", YScalar.Of(false)).Add("m_LoopBlend", YScalar.Of(false)).Add("m_CycleOffset", YScalar.Of(0f));
                NSet("m_AnimationClipSettings", settings);
            }
            if (NGet("m_SampleRate") == null) NSetF("m_SampleRate", 60);
            m_ScriptCurves.Clear();
            m_Version++;
        }

        public void ClearCurves()
        {
            m_ScriptCurves.Clear();
            foreach (var k in new[] { "m_PositionCurves", "m_RotationCurves", "m_EulerCurves", "m_ScaleCurves", "m_FloatCurves", "m_PPtrCurves", "m_EditorCurves", "m_EulerEditorCurves" })
                if (NGet(k) != null) NSet(k, new YSeq());
            m_Version++;
        }
        public void EnsureQuaternionContinuity() { }

        /// <summary>Samples the clip at <paramref name="time"/> into the GameObject's hierarchy (no blending).</summary>
        public void SampleAnimation(GameObject go, float time)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            var d = ClipData.Of(this);
            var run = AnimationSystem.Sampler(go, d);
            var pose = new MiniUnity.Anim.Pose(); pose.Add(d, time, 1);
            run.Apply(pose.Floats.ToDictionary(k => k.Key, k => k.Value.sum / k.Value.w), pose.Refs.ToDictionary(k => k.Key, k => k.Value.value));
        }
        internal static void SampleAnimation(GameObject go, AnimationClip clip, float inTime, WrapMode wrapMode) => clip.SampleAnimation(go, inTime);
    }

    public partial class AnimationEvent
    {
        public AnimationEvent() { m_FunctionName = ""; m_StringParameter = ""; }
        internal AnimationEvent Clone() => (AnimationEvent)MemberwiseClone();
        internal int GetHash() => HashCode.Combine(m_FunctionName, m_Time);
        public string data { get => m_StringParameter; set => m_StringParameter = value; }
        public string stringParameter { get => m_StringParameter; set => m_StringParameter = value; }
        public float floatParameter { get => m_FloatParameter; set => m_FloatParameter = value; }
        public int intParameter { get => m_IntParameter; set => m_IntParameter = value; }
        public Object objectReferenceParameter { get => m_ObjectReferenceParameter; set => m_ObjectReferenceParameter = value; }
        public string functionName { get => m_FunctionName; set => m_FunctionName = value; }
        public float time { get => m_Time; set => m_Time = value; }
        public SendMessageOptions messageOptions { get => (SendMessageOptions)m_MessageOptions; set => m_MessageOptions = (int)value; }
        public bool isFiredByLegacy => m_Source == AnimationEventSource.Legacy;
        public bool isFiredByAnimator => m_Source == AnimationEventSource.Animator;
        public AnimationState animationState { get { if (!isFiredByLegacy) Debug.LogError("AnimationEvent was not fired by Animation component, you shouldn't use AnimationEvent.animationState"); return m_StateSender; } }
        public AnimatorStateInfo animatorStateInfo { get { if (!isFiredByAnimator) Debug.LogError("AnimationEvent was not fired by Animator component, you shouldn't use AnimationEvent.animatorStateInfo"); return m_AnimatorStateInfo; } }
        public AnimatorClipInfo animatorClipInfo { get { if (!isFiredByAnimator) Debug.LogError("AnimationEvent was not fired by Animator component, you shouldn't use AnimationEvent.animatorClipInfo"); return m_AnimatorClipInfo; } }
    }

    // ====================================================================================================== Animator
    public partial struct MatchTargetWeightMask
    {
        Vector3 m_PositionXYZWeight; float m_RotationWeight;
        public MatchTargetWeightMask(Vector3 positionXYZWeight, float rotationWeight) { m_PositionXYZWeight = positionXYZWeight; m_RotationWeight = rotationWeight; }
        public Vector3 positionXYZWeight { get => m_PositionXYZWeight; set => m_PositionXYZWeight = value; }
        public float rotationWeight { get => m_RotationWeight; set => m_RotationWeight = value; }
    }

    public partial class Animator
    {
        internal AnimatorRun m_Run; RuntimeAnimatorController m_RunController;
        float m_Speed = 1; bool m_SpeedSet; bool m_FireEvents = true, m_LogWarnings = true;
        float m_PlaybackTime; bool m_Recording;
        public Animator() { }

        static readonly uint[] crcTable = Enumerable.Range(0, 256).Select(i => { uint c = (uint)i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; return c; }).ToArray();
        /// <summary>CRC32 of the UTF-8 string (Mecanim's name hash).</summary>
        public static int StringToHash(string name)
        {
            if (name == null) return 0;
            uint crc = 0xFFFFFFFFu;
            foreach (var b in System.Text.Encoding.UTF8.GetBytes(name)) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return (int)(crc ^ 0xFFFFFFFFu);
        }

        internal AnimatorRun Run
        {
            get
            {
                var rac = runtimeAnimatorController;
                if (rac == null) { m_Run = null; m_RunController = null; return null; }
                var model = CController.Of(rac);
                if (m_Run == null || m_RunController != rac || m_Run.C != model) { m_Run = new AnimatorRun(this, model); m_RunController = rac; }
                return m_Run;
            }
        }

        public RuntimeAnimatorController runtimeAnimatorController
        {
            get => NRef<RuntimeAnimatorController>("m_Controller");
            set { NSetRef("m_Controller", value); m_Run = null; }
        }
        public Avatar avatar { get => NRef<Avatar>("m_Avatar"); set => NSetRef("m_Avatar", value); }
        public bool applyRootMotion { get => NBool("m_ApplyRootMotion"); set => NSetB("m_ApplyRootMotion", value); }
        public AnimatorUpdateMode updateMode { get => (AnimatorUpdateMode)NInt("m_UpdateMode"); set => NSetI("m_UpdateMode", (int)value); }
        public bool animatePhysics { get => updateMode == AnimatorUpdateMode.AnimatePhysics; set => updateMode = value ? AnimatorUpdateMode.AnimatePhysics : AnimatorUpdateMode.Normal; }
        public AnimatorCullingMode cullingMode { get => (AnimatorCullingMode)NInt("m_CullingMode"); set => NSetI("m_CullingMode", (int)value); }
        public bool linearVelocityBlending { get => NBool("m_LinearVelocityBlending"); set => NSetB("m_LinearVelocityBlending", value); }
        public bool stabilizeFeet { get => NBool("m_StabilizeFeet"); set => NSetB("m_StabilizeFeet", value); }
        public bool keepAnimatorStateOnDisable { get => NBool("m_KeepAnimatorStateOnDisable") || NBool("m_KeepAnimatorControllerStateOnDisable"); set => NSetB("m_KeepAnimatorStateOnDisable", value); }
        public bool keepAnimatorControllerStateOnDisable { get => keepAnimatorStateOnDisable; set => keepAnimatorStateOnDisable = value; }
        public bool writeDefaultValuesOnDisable { get => NBool("m_WriteDefaultValuesOnDisable"); set => NSetB("m_WriteDefaultValuesOnDisable", value); }
        public float speed { get => m_Speed; set => m_Speed = value; }
        public bool fireEvents { get => m_FireEvents; set => m_FireEvents = value; }
        public bool logWarnings { get => m_LogWarnings; set => m_LogWarnings = value; }
        public bool isInitialized => m_Run != null && m_Run.Initialized;
        public bool hasBoundPlayables => runtimeAnimatorController != null;
        public bool isOptimizable => false;
        public bool hasRootMotion => Run?.C.Clips.Any(c => ClipData.Of(c).HasRootCurves) ?? false;
        internal bool isRootPositionOrRotationControlledByCurves => hasRootMotion;
        public bool hasTransformHierarchy => true;
        public float gravityWeight => 0;
        public Vector3 deltaPosition => m_Run?.DeltaPosition ?? Vector3.zero;
        public Quaternion deltaRotation => m_Run?.DeltaRotation ?? Quaternion.identity;
        public Vector3 velocity => Time.deltaTime > 0 ? deltaPosition / Time.deltaTime : Vector3.zero;
        public Vector3 angularVelocity { get { deltaRotation.ToAngleAxis(out float a, out var ax); return Time.deltaTime > 0 && !float.IsNaN(ax.x) ? ax * (a * Mathf.Deg2Rad / Time.deltaTime) : Vector3.zero; } }
        public Vector3 rootPosition { get => transform.position; set => transform.position = value; }
        public Quaternion rootRotation { get => transform.rotation; set => transform.rotation = value; }
        public Vector3 bodyPosition { get => transform.position; set { } }
        public Quaternion bodyRotation { get => transform.rotation; set { } }
        public int layerCount => Run?.Layers.Count ?? 0;
        public AnimatorControllerParameter[] parameters => Run?.C.Params.Select(p => new AnimatorControllerParameter { m_Name = p.Name, m_Type = p.Type, m_DefaultFloat = p.DefFloat, m_DefaultInt = p.DefInt, m_DefaultBool = p.DefBool }).ToArray() ?? Array.Empty<AnimatorControllerParameter>();
        public int parameterCount => Run?.C.Params.Count ?? 0;
        public AnimatorControllerParameter GetParameter(int index)
        {
            var ps = parameters;
            if (index < 0 || index >= ps.Length) throw new IndexOutOfRangeException("Index must be between 0 and " + ps.Length);
            return ps[index];
        }
        public float playbackTime { get => m_PlaybackTime; set => m_PlaybackTime = value; }
        public float recorderStartTime { get; set; }
        public float recorderStopTime { get; set; }
        public AnimatorRecorderMode recorderMode => AnimatorRecorderMode.Offline;
        public Transform avatarRoot => transform;
        public bool isMatchingTarget => m_Run?.Match != null;
        public Vector3 targetPosition => transform.position;
        public Quaternion targetRotation => transform.rotation;
        public float feetPivotActive { get; set; }
        public float pivotWeight => 0;
        public Vector3 pivotPosition => transform.position;
        public bool layersAffectMassCenter { get; set; }
        public float leftFeetBottomHeight => 0;
        public float rightFeetBottomHeight => 0;
        internal bool supportsOnAnimatorMove => true;

        // ---------------------------------------------------------------- parameters
        void Warn(string msg) { if (m_LogWarnings) Debug.LogWarning(msg); }
        CParam P(int id, AnimatorControllerParameterType t, string name) { var r = Run; if (r == null) { Warn("Animator is not playing an AnimatorController"); return null; } return r.Param(id, t, name); }
        public float GetFloat(string name) => GetFloatImpl(StringToHash(name), name);
        public float GetFloat(int id) => GetFloatImpl(id, null);
        float GetFloatImpl(int id, string name) { var p = P(id, AnimatorControllerParameterType.Float, name); return p == null ? 0 : m_Run.Floats[p.Hash]; }
        public void SetFloat(string name, float value) => SetFloatImpl(StringToHash(name), value, name);
        public void SetFloat(int id, float value) => SetFloatImpl(id, value, null);
        void SetFloatImpl(int id, float value, string name) { var p = P(id, AnimatorControllerParameterType.Float, name); if (p != null) m_Run.Floats[p.Hash] = value; }
        public void SetFloat(string name, float value, float dampTime, float deltaTime) => SetFloat(StringToHash(name), value, dampTime, deltaTime);
        public void SetFloat(int id, float value, float dampTime, float deltaTime)
        {
            var p = P(id, AnimatorControllerParameterType.Float, null); if (p == null) return;
            float cur = m_Run.Floats[p.Hash];
            // Mecanim damper: exponential approach, value += (target − value) · dt / (damp + dt)
            m_Run.Floats[p.Hash] = dampTime > 0 && deltaTime >= 0 ? cur + (value - cur) * (deltaTime / (dampTime + deltaTime)) : value;
        }
        public bool GetBool(string name) { var p = P(StringToHash(name), AnimatorControllerParameterType.Bool, name); return p != null && m_Run.Bools[p.Hash]; }
        public bool GetBool(int id) { var p = P(id, AnimatorControllerParameterType.Bool, null); return p != null && m_Run.Bools[p.Hash]; }
        public void SetBool(string name, bool value) { var p = P(StringToHash(name), AnimatorControllerParameterType.Bool, name); if (p != null) m_Run.Bools[p.Hash] = value; }
        public void SetBool(int id, bool value) { var p = P(id, AnimatorControllerParameterType.Bool, null); if (p != null) m_Run.Bools[p.Hash] = value; }
        public int GetInteger(string name) { var p = P(StringToHash(name), AnimatorControllerParameterType.Int, name); return p == null ? 0 : m_Run.Ints[p.Hash]; }
        public int GetInteger(int id) { var p = P(id, AnimatorControllerParameterType.Int, null); return p == null ? 0 : m_Run.Ints[p.Hash]; }
        public void SetInteger(string name, int value) { var p = P(StringToHash(name), AnimatorControllerParameterType.Int, name); if (p != null) m_Run.Ints[p.Hash] = value; }
        public void SetInteger(int id, int value) { var p = P(id, AnimatorControllerParameterType.Int, null); if (p != null) m_Run.Ints[p.Hash] = value; }
        public void SetTrigger(string name) { var p = P(StringToHash(name), AnimatorControllerParameterType.Trigger, name); if (p != null) m_Run.Bools[p.Hash] = true; }
        public void SetTrigger(int id) { var p = P(id, AnimatorControllerParameterType.Trigger, null); if (p != null) m_Run.Bools[p.Hash] = true; }
        public void ResetTrigger(string name) { var p = P(StringToHash(name), AnimatorControllerParameterType.Trigger, name); if (p != null) m_Run.Bools[p.Hash] = false; }
        public void ResetTrigger(int id) { var p = P(id, AnimatorControllerParameterType.Trigger, null); if (p != null) m_Run.Bools[p.Hash] = false; }
        public bool IsParameterControlledByCurve(string name) => false;
        public bool IsParameterControlledByCurve(int id) => false;
        public Vector3 GetVector(string name) => Vector3.zero;
        public Vector3 GetVector(int id) => Vector3.zero;
        public void SetVector(string name, Vector3 value) { }
        public void SetVector(int id, Vector3 value) { }
        public Quaternion GetQuaternion(string name) => Quaternion.identity;
        public Quaternion GetQuaternion(int id) => Quaternion.identity;
        public void SetQuaternion(string name, Quaternion value) { }
        public void SetQuaternion(int id, Quaternion value) { }

        // ---------------------------------------------------------------- layers / state info
        LayerRun L(int layerIndex)
        {
            var r = Run; if (r == null) return null;
            if (layerIndex < 0 || layerIndex >= r.Layers.Count) { Warn($"Invalid Layer Index '{layerIndex}'"); return null; }
            return r.Layers[layerIndex];
        }
        public string GetLayerName(int layerIndex) => L(layerIndex)?.Layer.Name;
        public int GetLayerIndex(string layerName) { var r = Run; if (r == null) return -1; for (int i = 0; i < r.Layers.Count; i++) if (r.Layers[i].Layer.Name == layerName) return i; return -1; }
        public float GetLayerWeight(int layerIndex) => layerIndex == 0 ? (L(0) != null ? 1 : 0) : L(layerIndex)?.Weight ?? 0;
        public void SetLayerWeight(int layerIndex, float weight) { var l = L(layerIndex); if (l != null && layerIndex > 0) l.Weight = Mathf.Clamp01(weight); }
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layerIndex) { var l = L(layerIndex); return l == null ? default : m_Run.Info(l, l.Current); }
        public AnimatorStateInfo GetNextAnimatorStateInfo(int layerIndex) { var l = L(layerIndex); return l?.Next == null ? default : m_Run.Info(l, l.Next); }
        public AnimatorTransitionInfo GetAnimatorTransitionInfo(int layerIndex) { var l = L(layerIndex); return l == null ? default : m_Run.TransitionInfo(l); }
        public bool IsInTransition(int layerIndex) => L(layerIndex)?.Next != null;
        internal int GetAnimatorClipInfoCount(int layerIndex, bool current) { var l = L(layerIndex); return l == null ? 0 : m_Run.ClipInfo(l, current).Count; }
        public int GetCurrentAnimatorClipInfoCount(int layerIndex) => GetAnimatorClipInfoCount(layerIndex, true);
        public int GetNextAnimatorClipInfoCount(int layerIndex) => GetAnimatorClipInfoCount(layerIndex, false);
        public AnimatorClipInfo[] GetCurrentAnimatorClipInfo(int layerIndex) { var l = L(layerIndex); return l == null ? Array.Empty<AnimatorClipInfo>() : m_Run.ClipInfo(l, true).ToArray(); }
        public AnimatorClipInfo[] GetNextAnimatorClipInfo(int layerIndex) { var l = L(layerIndex); return l == null ? Array.Empty<AnimatorClipInfo>() : m_Run.ClipInfo(l, false).ToArray(); }
        public void GetCurrentAnimatorClipInfo(int layerIndex, List<AnimatorClipInfo> clips) { clips.Clear(); clips.AddRange(GetCurrentAnimatorClipInfo(layerIndex)); }
        public void GetNextAnimatorClipInfo(int layerIndex, List<AnimatorClipInfo> clips) { clips.Clear(); clips.AddRange(GetNextAnimatorClipInfo(layerIndex)); }
        public bool HasState(int layerIndex, int stateID) => Run?.FindState(layerIndex, stateID) != null;
        internal string GetCurrentStateName(int layerIndex) => L(layerIndex)?.Current?.State.FullPath ?? "";
        internal string GetNextStateName(int layerIndex) => L(layerIndex)?.Next?.State.FullPath ?? "";
        internal string ResolveHash(int hash)
        {
            var r = Run; if (r == null) return "";
            foreach (var p in r.C.Params) if (p.Hash == hash) return p.Name;
            foreach (var l in r.Layers) foreach (var s in l.Layer.Root.AllStates()) { if (s.FullHash == hash) return s.FullPath; if (s.ShortHash == hash) return s.Name; }
            return "";
        }

        // ---------------------------------------------------------------- Play / CrossFade
        CState State(int hash, ref int layer)
        {
            var r = Run; if (r == null) { Warn("Animator is not playing an AnimatorController"); return null; }
            var s = r.FindState(layer, hash);
            if (s == null) { Warn($"Animator.GotoState: State could not be found"); return null; }
            if (layer < 0) layer = r.LayerOf(s);
            return s;
        }
        public void Play(string stateName) => Play(StringToHash(stateName), -1, float.NegativeInfinity);
        public void Play(string stateName, int layer) => Play(StringToHash(stateName), layer, float.NegativeInfinity);
        public void Play(string stateName, int layer, float normalizedTime) => Play(StringToHash(stateName), layer, normalizedTime);
        public void Play(int stateNameHash) => Play(stateNameHash, -1, float.NegativeInfinity);
        public void Play(int stateNameHash, int layer) => Play(stateNameHash, layer, float.NegativeInfinity);
        public void Play(int stateNameHash, int layer, float normalizedTime)
        {
            var s = State(stateNameHash, ref layer); if (s == null) return;
            m_Run.Play(layer, s, float.IsNegativeInfinity(normalizedTime) ? 0 : normalizedTime, false);
        }
        public void PlayInFixedTime(string stateName) => PlayInFixedTime(StringToHash(stateName), -1, float.NegativeInfinity);
        public void PlayInFixedTime(string stateName, int layer) => PlayInFixedTime(StringToHash(stateName), layer, float.NegativeInfinity);
        public void PlayInFixedTime(string stateName, int layer, float fixedTime) => PlayInFixedTime(StringToHash(stateName), layer, fixedTime);
        public void PlayInFixedTime(int stateNameHash) => PlayInFixedTime(stateNameHash, -1, float.NegativeInfinity);
        public void PlayInFixedTime(int stateNameHash, int layer) => PlayInFixedTime(stateNameHash, layer, float.NegativeInfinity);
        public void PlayInFixedTime(int stateNameHash, int layer, float fixedTime)
        {
            var s = State(stateNameHash, ref layer); if (s == null) return;
            m_Run.Play(layer, s, float.IsNegativeInfinity(fixedTime) ? 0 : fixedTime, true);
        }
        public void CrossFade(string stateName, float normalizedTransitionDuration) => CrossFade(StringToHash(stateName), normalizedTransitionDuration, -1, float.NegativeInfinity, 0);
        public void CrossFade(string stateName, float normalizedTransitionDuration, int layer) => CrossFade(StringToHash(stateName), normalizedTransitionDuration, layer, float.NegativeInfinity, 0);
        public void CrossFade(string stateName, float normalizedTransitionDuration, int layer, float normalizedTimeOffset) => CrossFade(StringToHash(stateName), normalizedTransitionDuration, layer, normalizedTimeOffset, 0);
        public void CrossFade(string stateName, float normalizedTransitionDuration, int layer, float normalizedTimeOffset, float normalizedTransitionTime) => CrossFade(StringToHash(stateName), normalizedTransitionDuration, layer, normalizedTimeOffset, normalizedTransitionTime);
        public void CrossFade(int stateHashName, float normalizedTransitionDuration) => CrossFade(stateHashName, normalizedTransitionDuration, -1, float.NegativeInfinity, 0);
        public void CrossFade(int stateHashName, float normalizedTransitionDuration, int layer) => CrossFade(stateHashName, normalizedTransitionDuration, layer, float.NegativeInfinity, 0);
        public void CrossFade(int stateHashName, float normalizedTransitionDuration, int layer, float normalizedTimeOffset) => CrossFade(stateHashName, normalizedTransitionDuration, layer, normalizedTimeOffset, 0);
        public void CrossFade(int stateHashName, float normalizedTransitionDuration, int layer, float normalizedTimeOffset, float normalizedTransitionTime)
        {
            var s = State(stateHashName, ref layer); if (s == null) return;
            m_Run.CrossFade(layer, s, normalizedTransitionDuration, false, float.IsNegativeInfinity(normalizedTimeOffset) ? 0 : normalizedTimeOffset, false);
        }
        public void CrossFadeInFixedTime(string stateName, float fixedTransitionDuration) => CrossFadeInFixedTime(StringToHash(stateName), fixedTransitionDuration, -1, 0, 0);
        public void CrossFadeInFixedTime(string stateName, float fixedTransitionDuration, int layer) => CrossFadeInFixedTime(StringToHash(stateName), fixedTransitionDuration, layer, 0, 0);
        public void CrossFadeInFixedTime(string stateName, float fixedTransitionDuration, int layer, float fixedTimeOffset) => CrossFadeInFixedTime(StringToHash(stateName), fixedTransitionDuration, layer, fixedTimeOffset, 0);
        public void CrossFadeInFixedTime(string stateName, float fixedTransitionDuration, int layer, float fixedTimeOffset, float normalizedTransitionTime) => CrossFadeInFixedTime(StringToHash(stateName), fixedTransitionDuration, layer, fixedTimeOffset, normalizedTransitionTime);
        public void CrossFadeInFixedTime(int stateHashName, float fixedTransitionDuration) => CrossFadeInFixedTime(stateHashName, fixedTransitionDuration, -1, 0, 0);
        public void CrossFadeInFixedTime(int stateHashName, float fixedTransitionDuration, int layer) => CrossFadeInFixedTime(stateHashName, fixedTransitionDuration, layer, 0, 0);
        public void CrossFadeInFixedTime(int stateHashName, float fixedTransitionDuration, int layer, float fixedTimeOffset) => CrossFadeInFixedTime(stateHashName, fixedTransitionDuration, layer, fixedTimeOffset, 0);
        public void CrossFadeInFixedTime(int stateHashName, float fixedTransitionDuration, int layer, float fixedTimeOffset, float normalizedTransitionTime)
        {
            var s = State(stateHashName, ref layer); if (s == null) return;
            m_Run.CrossFade(layer, s, fixedTransitionDuration, true, fixedTimeOffset, true);
        }
        public void ForceStateNormalizedTime(float normalizedTime) { var l = L(0); if (l?.Current != null) l.Current.NormalizedTime = l.Current.PrevNormalizedTime = normalizedTime; }

        // ---------------------------------------------------------------- behaviours
        public T GetBehaviour<T>() where T : StateMachineBehaviour => Run?.AllBehaviours().OfType<T>().FirstOrDefault();
        public T[] GetBehaviours<T>() where T : StateMachineBehaviour => Run?.AllBehaviours().OfType<T>().Distinct().ToArray() ?? Array.Empty<T>();
        internal ScriptableObject[] InternalGetBehaviours(Type type) => Run?.AllBehaviours().Where(type.IsInstanceOfType).Distinct().Cast<ScriptableObject>().ToArray() ?? Array.Empty<ScriptableObject>();
        public StateMachineBehaviour[] GetBehaviours(int fullPathHash, int layerIndex)
        {
            var l = L(layerIndex); if (l == null) return Array.Empty<StateMachineBehaviour>();
            var s = l.Layer.Root.AllStates().FirstOrDefault(x => x.FullHash == fullPathHash);
            return s == null ? Array.Empty<StateMachineBehaviour>() : m_Run.BehavioursOf(l, s).Distinct().ToArray();
        }
        internal ScriptableObject[] InternalGetBehavioursByKey(int fullPathHash, int layerIndex, Type type) => GetBehaviours(fullPathHash, layerIndex).Where(type.IsInstanceOfType).Cast<ScriptableObject>().ToArray();

        // ---------------------------------------------------------------- evaluation
        /// <summary>Evaluates the controller by <paramref name="deltaTime"/> right now (like Unity's manual Animator.Update).</summary>
        public void Update(float deltaTime) { var r = Run; r?.Update(deltaTime); }
        public void Rebind() { m_Run = null; _ = Run; }
        public void WriteDefaultValues() => Run?.WriteDefaults();
        internal void WriteDefaultPose() => WriteDefaultValues();
        internal void EvaluateController() => Update(0);
        public void ApplyBuiltinRootMotion() { var r = m_Run; if (r == null) return; transform.position += r.DeltaPosition; transform.rotation = r.DeltaRotation * transform.rotation; }
        public void StartPlayback() { }
        public void StopPlayback() { }
        public void StartRecording(int frameCount) { m_Recording = true; }
        public void StopRecording() { m_Recording = false; }
        internal void ClearInternalControllerPlayable() { m_Run = null; }
        internal string GetStats() => $"layers {layerCount}, params {parameterCount}";
        internal void OnUpdateModeChanged() { }
        internal void OnCullingModeChanged() { }

        // ---------------------------------------------------------------- humanoid: Humanoid.cs (IK, bones); no clip retargeting
        public void SetTarget(AvatarTarget targetIndex, float targetNormalizedTime) { }
        public void MatchTarget(Vector3 matchPosition, Quaternion matchRotation, AvatarTarget targetBodyPart, MatchTargetWeightMask weightMask, float startNormalizedTime) => MatchTarget(matchPosition, matchRotation, targetBodyPart, weightMask, startNormalizedTime, 1, true);
        public void MatchTarget(Vector3 matchPosition, Quaternion matchRotation, AvatarTarget targetBodyPart, MatchTargetWeightMask weightMask, float startNormalizedTime, float targetNormalizedTime) => MatchTarget(matchPosition, matchRotation, targetBodyPart, weightMask, startNormalizedTime, targetNormalizedTime, true);
        public void MatchTarget(Vector3 matchPosition, Quaternion matchRotation, AvatarTarget targetBodyPart, MatchTargetWeightMask weightMask, float startNormalizedTime, float targetNormalizedTime, bool completeMatch)
        {
            if (isMatchingTarget) return;   // Unity keeps the running match
            Run?.StartMatch(matchPosition, matchRotation, targetBodyPart, weightMask, startNormalizedTime, targetNormalizedTime, completeMatch);
        }
        public void InterruptMatchTarget() => InterruptMatchTarget(true);
        public void InterruptMatchTarget(bool completeMatch) => m_Run?.InterruptMatch(completeMatch);
    }

    // ====================================================================================================== legacy Animation
    public partial class AnimationState
    {
        internal Animation m_Owner; internal AnimationClip m_Clip; internal string m_StateName;
        internal bool m_Enabled; internal float m_Weight, m_Time, m_PrevTime, m_Speed = 1, m_TargetWeight, m_FadeSpeed; internal int m_Layer;
        internal WrapMode m_WrapMode; internal AnimationBlendMode m_BlendMode; internal bool m_Fresh, m_StopWhenFadedOut;
        public AnimationState() { }
        public bool enabled { get => m_Enabled; set { if (value && !m_Enabled) m_Fresh = true; m_Enabled = value; } }
        public float weight { get => m_Weight; set { m_Weight = value; m_TargetWeight = value; m_FadeSpeed = 0; } }
        public WrapMode wrapMode { get => m_WrapMode; set => m_WrapMode = value; }
        public float time { get => m_Time; set { m_Time = value; m_PrevTime = value; m_Fresh = true; } }
        public float normalizedTime { get => length > 0 ? m_Time / length : 0; set => time = value * length; }
        public float speed { get => m_Speed; set => m_Speed = value; }
        public float normalizedSpeed { get => length > 0 ? m_Speed / length : 0; set => m_Speed = value * length; }
        public float length => m_Clip != null ? m_Clip.length : 0;
        public int layer { get => m_Layer; set => m_Layer = value; }
        public AnimationClip clip => m_Clip;
        public string name { get => m_StateName; set => m_StateName = value; }
        public AnimationBlendMode blendMode { get => m_BlendMode; set => m_BlendMode = value; }
        public void AddMixingTransform(Transform mix) { }
        public void AddMixingTransform(Transform mix, bool recursive) { }
        public void RemoveMixingTransform(Transform mix) { }
    }

    public partial class Animation
    {
        internal readonly List<AnimationState> m_States = new(); bool m_StatesInit;
        internal readonly List<(string name, float fade, PlayMode mode)> m_Queue = new();
        internal PoseWriter m_Sampler; internal bool m_AutoPlayed;
        public Animation() { }

        internal void InitStates()
        {
            if (m_StatesInit) return;
            m_StatesInit = true;
            var clips = new List<AnimationClip>();
            if (NGet("m_Animations") is YSeq s) foreach (var i in s.Items) if (i is YRef r && (r.Raw ? AssetLoader.ResolveExternal(r) : r.Target) is AnimationClip c) clips.Add(c);
            var def = clip; if (def != null && !clips.Contains(def)) clips.Insert(0, def);
            foreach (var c in clips) AddState(c, c.name);
        }

        AnimationState AddState(AnimationClip c, string name)
        {
            var st = m_States.FirstOrDefault(x => x.name == name);
            if (st == null) { st = new AnimationState { m_Owner = this, m_StateName = name }; m_States.Add(st); }
            st.m_Clip = c;
            st.m_WrapMode = wrapMode != WrapMode.Default ? wrapMode : c.wrapMode;
            m_Sampler = null;
            return st;
        }

        public AnimationClip clip { get => NRef<AnimationClip>("m_Animation"); set => NSetRef("m_Animation", value); }
        public bool playAutomatically { get => NBool("m_PlayAutomatically", true); set => NSetB("m_PlayAutomatically", value); }
        public WrapMode wrapMode { get => (WrapMode)NInt("m_WrapMode"); set { NSetI("m_WrapMode", (int)value); foreach (var s in m_States) s.m_WrapMode = value; } }
        public bool animatePhysics { get => NBool("m_AnimatePhysics"); set => NSetB("m_AnimatePhysics", value); }
        public bool animateOnlyIfVisible { get => NBool("m_AnimateOnlyIfVisible"); set => NSetB("m_AnimateOnlyIfVisible", value); }
        public AnimationCullingType cullingType { get => (AnimationCullingType)NInt("m_CullingType"); set => NSetI("m_CullingType", (int)value); }
        public Bounds localBounds { get; set; }
        public bool isPlaying { get { InitStates(); return m_States.Any(s => s.m_Enabled); } }
        public AnimationState this[string name] { get { InitStates(); return m_States.FirstOrDefault(s => s.name == name); } }
        internal AnimationState GetState(string name) => this[name];
        internal AnimationState GetStateAtIndex(int index) { InitStates(); return m_States[index]; }
        internal int GetStateCount() { InitStates(); return m_States.Count; }
        public int GetClipCount() { InitStates(); return m_States.Count; }
        public AnimationClip GetClip(string name) => this[name]?.clip;
        public IEnumerator GetEnumerator() { InitStates(); return m_States.ToList().GetEnumerator(); }
        public void AddClip(AnimationClip clip, string newName) { InitStates(); if (clip == null) throw new NullReferenceException(); AddState(clip, newName); }
        public void AddClip(AnimationClip clip, string newName, int firstFrame, int lastFrame) => AddClip(clip, newName);
        public void AddClip(AnimationClip clip, string newName, int firstFrame, int lastFrame, bool addLoopFrame) => AddClip(clip, newName);
        public void RemoveClip(AnimationClip clip) { InitStates(); m_States.RemoveAll(s => s.clip == clip); m_Sampler = null; }
        public void RemoveClip(string clipName) { InitStates(); m_States.RemoveAll(s => s.name == clipName); m_Sampler = null; }

        public void Stop() { InitStates(); foreach (var s in m_States) { s.m_Enabled = false; s.m_Time = 0; s.m_Weight = 0; } m_Queue.Clear(); }
        public void Stop(string name) { var s = this[name]; if (s != null) { s.m_Enabled = false; s.m_Time = 0; s.m_Weight = 0; } }
        public void Rewind() { InitStates(); foreach (var s in m_States) s.time = 0; }
        public void Rewind(string name) { var s = this[name]; if (s != null) s.time = 0; }
        public void Sample() { InitStates(); AnimationSystem.EvaluateLegacy(this, 0, true); }
        public bool IsPlaying(string name) => this[name]?.m_Enabled ?? false;
        public bool Play() => Play(PlayMode.StopSameLayer);
        public bool Play(PlayMode mode) { var c = clip; return c != null && Play(c.name, mode); }
        public bool Play(string animation) => Play(animation, PlayMode.StopSameLayer);
        public bool Play(string animation, PlayMode mode)
        {
            InitStates();
            var st = this[animation];
            if (st == null) { Debug.LogWarning($"The animation state {animation} could not be played because it couldn't be found!\nPlease attach an animation clip with the name '{animation}' or call this function only for existing animations."); return false; }
            foreach (var o in m_States) if (o != st && (mode == PlayMode.StopAll || o.layer == st.layer)) { o.m_Enabled = false; o.m_Weight = 0; o.m_Time = 0; }
            if (!st.m_Enabled) { st.m_Time = st.m_Speed < 0 ? st.length : 0; st.m_PrevTime = st.m_Time; st.m_Fresh = true; }
            st.m_Enabled = true; st.m_Weight = 1; st.m_TargetWeight = 1; st.m_FadeSpeed = 0; st.m_StopWhenFadedOut = false;
            return true;
        }
        public bool Play(AnimationPlayMode mode) => Play(mode == AnimationPlayMode.Mix ? PlayMode.StopSameLayer : PlayMode.StopSameLayer);
        public bool Play(string animation, AnimationPlayMode mode) => Play(animation);
        public void CrossFade(string animation) => CrossFade(animation, 0.3f, PlayMode.StopSameLayer);
        public void CrossFade(string animation, float fadeLength) => CrossFade(animation, fadeLength, PlayMode.StopSameLayer);
        public void CrossFade(string animation, float fadeLength, PlayMode mode)
        {
            InitStates();
            var st = this[animation]; if (st == null) return;
            if (fadeLength <= 0) { Play(animation, mode); return; }
            foreach (var o in m_States)
                if (o != st && (mode == PlayMode.StopAll || o.layer == st.layer) && o.m_Enabled) { o.m_TargetWeight = 0; o.m_FadeSpeed = Math.Max(o.m_Weight, 1e-4f) / fadeLength; o.m_StopWhenFadedOut = true; }
            if (!st.m_Enabled) { st.m_Time = 0; st.m_PrevTime = 0; st.m_Fresh = true; st.m_Weight = 0; }
            st.m_Enabled = true; st.m_TargetWeight = 1; st.m_FadeSpeed = 1 / fadeLength; st.m_StopWhenFadedOut = false;
        }
        public void Blend(string animation) => Blend(animation, 1, 0.3f);
        public void Blend(string animation, float targetWeight) => Blend(animation, targetWeight, 0.3f);
        public void Blend(string animation, float targetWeight, float fadeLength)
        {
            InitStates();
            var st = this[animation]; if (st == null) return;
            if (!st.m_Enabled) { st.m_Enabled = true; st.m_Time = 0; st.m_Weight = 0; st.m_Fresh = true; }
            st.m_TargetWeight = targetWeight; st.m_FadeSpeed = fadeLength > 0 ? Math.Abs(targetWeight - st.m_Weight) / fadeLength : float.MaxValue;
        }
        public AnimationState CrossFadeQueued(string animation) => CrossFadeQueued(animation, 0.3f, QueueMode.CompleteOthers, PlayMode.StopSameLayer);
        public AnimationState CrossFadeQueued(string animation, float fadeLength) => CrossFadeQueued(animation, fadeLength, QueueMode.CompleteOthers, PlayMode.StopSameLayer);
        public AnimationState CrossFadeQueued(string animation, float fadeLength, QueueMode queue) => CrossFadeQueued(animation, fadeLength, queue, PlayMode.StopSameLayer);
        public AnimationState CrossFadeQueued(string animation, float fadeLength, QueueMode queue, PlayMode mode)
        {
            InitStates();
            if (queue == QueueMode.PlayNow || !isPlaying) { CrossFade(animation, fadeLength, mode); return this[animation]; }
            m_Queue.Add((animation, fadeLength, mode)); return this[animation];
        }
        public AnimationState PlayQueued(string animation) => PlayQueued(animation, QueueMode.CompleteOthers, PlayMode.StopSameLayer);
        public AnimationState PlayQueued(string animation, QueueMode queue) => PlayQueued(animation, queue, PlayMode.StopSameLayer);
        public AnimationState PlayQueued(string animation, QueueMode queue, PlayMode mode) => CrossFadeQueued(animation, 0, queue, mode);
        public void SyncLayer(int layer) { }
    }
}
