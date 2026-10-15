// Humanoid avatars and IK. An Avatar built with AvatarBuilder.BuildHumanAvatar maps Unity's human bones to transforms of
// the character; Animator.GetBoneTransform, OnAnimatorIK (layers with IK Pass), IK goals / hints and look-at work on that
// map. IK is solved on the evaluated pose: two-bone limb IK (analytic, bend plane from the hint or the current pose) and a
// look-at that turns spine / head / eyes by their weights. Not simulated: muscle-space retargeting of humanoid clips.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MiniUnity;
using MiniUnity.Anim;

namespace UnityEngine
{
    public partial struct HumanBone
    {
        string m_BoneName, m_HumanName;
        public string boneName { get => m_BoneName; set => m_BoneName = value; }
        public string humanName { get => m_HumanName; set => m_HumanName = value; }
    }

    public partial struct HumanLimit
    {
        Vector3 m_Min, m_Max, m_Center; float m_AxisLength; int m_UseDefaultValues;
        public bool useDefaultValues { get => m_UseDefaultValues != 0; set => m_UseDefaultValues = value ? 1 : 0; }
        public Vector3 min { get => m_Min; set => m_Min = value; }
        public Vector3 max { get => m_Max; set => m_Max = value; }
        public Vector3 center { get => m_Center; set => m_Center = value; }
        public float axisLength { get => m_AxisLength; set => m_AxisLength = value; }
    }

    public partial struct HumanDescription
    {
        public float upperArmTwist { get => m_ArmTwist; set => m_ArmTwist = value; }
        public float lowerArmTwist { get => m_ForeArmTwist; set => m_ForeArmTwist = value; }
        public float upperLegTwist { get => m_UpperLegTwist; set => m_UpperLegTwist = value; }
        public float lowerLegTwist { get => m_LegTwist; set => m_LegTwist = value; }
        public float armStretch { get => m_ArmStretch; set => m_ArmStretch = value; }
        public float legStretch { get => m_LegStretch; set => m_LegStretch = value; }
        public float feetSpacing { get => m_FeetSpacing; set => m_FeetSpacing = value; }
        public bool hasTranslationDoF { get => m_HasTranslationDoF; set => m_HasTranslationDoF = value; }
    }

    public partial class HumanTrait
    {
        static string[] s_Names;

        /// <summary>Unity's human bone names: body bones as in HumanBodyBones, fingers as "Left Thumb Proximal".</summary>
        public static string[] BoneName => s_Names ??= Enumerable.Range(0, (int)HumanBodyBones.LastBone).Select(i => Display((HumanBodyBones)i)).ToArray();
        public static int BoneCount => (int)HumanBodyBones.LastBone;

        static string Display(HumanBodyBones b)
        {
            var n = b.ToString();
            foreach (var f in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                int at = n.IndexOf(f, StringComparison.Ordinal);
                if (at > 0 && (n.EndsWith("Proximal") || n.EndsWith("Intermediate") || n.EndsWith("Distal")))
                {
                    var side = n.Substring(0, at); var rest = n.Substring(at + f.Length);
                    return $"{side} {f} {rest}";
                }
            }
            return n;
        }

        internal static int IndexOf(string humanName)
        {
            if (string.IsNullOrEmpty(humanName)) return -1;
            var key = humanName.Replace(" ", "");
            for (int i = 0; i < BoneCount; i++)
                if (string.Equals(((HumanBodyBones)i).ToString(), key, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        static readonly HumanBodyBones[] required =
        {
            HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.Spine, HumanBodyBones.Head, HumanBodyBones.LeftUpperArm,
            HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        };
        public static bool RequiredBone(int i) => required.Contains((HumanBodyBones)i);
        public static int RequiredBoneCount => required.Length;

        public static int GetParentBone(int i) => (HumanBodyBones)i switch
        {
            HumanBodyBones.Hips => -1,
            HumanBodyBones.LeftUpperLeg or HumanBodyBones.RightUpperLeg or HumanBodyBones.Spine => (int)HumanBodyBones.Hips,
            HumanBodyBones.LeftLowerLeg => (int)HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightLowerLeg => (int)HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftFoot => (int)HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightFoot => (int)HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftToes => (int)HumanBodyBones.LeftFoot, HumanBodyBones.RightToes => (int)HumanBodyBones.RightFoot,
            HumanBodyBones.Chest => (int)HumanBodyBones.Spine, HumanBodyBones.UpperChest => (int)HumanBodyBones.Chest,
            HumanBodyBones.Neck => (int)HumanBodyBones.UpperChest, HumanBodyBones.Head => (int)HumanBodyBones.Neck,
            HumanBodyBones.LeftEye or HumanBodyBones.RightEye or HumanBodyBones.Jaw => (int)HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder or HumanBodyBones.RightShoulder => (int)HumanBodyBones.UpperChest,
            HumanBodyBones.LeftUpperArm => (int)HumanBodyBones.LeftShoulder, HumanBodyBones.RightUpperArm => (int)HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftLowerArm => (int)HumanBodyBones.LeftUpperArm, HumanBodyBones.RightLowerArm => (int)HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftHand => (int)HumanBodyBones.LeftLowerArm, HumanBodyBones.RightHand => (int)HumanBodyBones.RightLowerArm,
            var b when b >= HumanBodyBones.LeftThumbProximal && b <= HumanBodyBones.LeftLittleDistal =>
                ((int)b - (int)HumanBodyBones.LeftThumbProximal) % 3 == 0 ? (int)HumanBodyBones.LeftHand : i - 1,
            var b when b >= HumanBodyBones.RightThumbProximal && b <= HumanBodyBones.RightLittleDistal =>
                ((int)b - (int)HumanBodyBones.RightThumbProximal) % 3 == 0 ? (int)HumanBodyBones.RightHand : i - 1,
            _ => -1,
        };
    }

    public partial class Avatar
    {
        internal bool m_IsHuman, m_IsValid;
        internal HumanDescription m_Description;
        internal string[] m_BoneNames = new string[(int)HumanBodyBones.LastBone];   // human bone → transform name

        internal Avatar() { }

        public bool isValid => m_IsValid;
        public bool isHuman => m_IsHuman;
        public HumanDescription humanDescription => m_Description;
    }

    public partial class AvatarBuilder
    {
        public AvatarBuilder() { }

        /// <summary>Human avatar from a bone map: valid when every required bone is mapped to a transform under <paramref name="go"/>.</summary>
        public static Avatar BuildHumanAvatar(GameObject go, HumanDescription humanDescription)
        {
            var avatar = new Avatar { name = go != null ? go.name : "", m_IsHuman = true, m_Description = humanDescription };
            var names = new HashSet<string>(go != null ? go.GetComponentsInChildren<Transform>(true).Select(t => t.name) : Array.Empty<string>());
            foreach (var hb in humanDescription.human ?? Array.Empty<HumanBone>())
            {
                int i = HumanTrait.IndexOf(hb.humanName);
                if (i >= 0 && names.Contains(hb.boneName ?? "")) avatar.m_BoneNames[i] = hb.boneName;
            }
            avatar.m_IsValid = Enumerable.Range(0, HumanTrait.BoneCount).All(i => !HumanTrait.RequiredBone(i) || avatar.m_BoneNames[i] != null);
            if (!avatar.m_IsValid) Debug.LogError("[AvatarBuilder] human description is missing required bones");
            return avatar;
        }

        public static Avatar BuildGenericAvatar(GameObject go, string rootMotionTransformName) =>
            new Avatar { name = go != null ? go.name : "", m_IsHuman = false, m_IsValid = go != null };
    }

    public partial class Animator
    {
        // IK goals (LeftFoot, RightFoot, LeftHand, RightHand), hints (LeftKnee, RightKnee, LeftElbow, RightElbow), look-at
        internal readonly Vector3[] m_IKPos = new Vector3[4]; internal readonly Quaternion[] m_IKRot = { Quaternion.identity, Quaternion.identity, Quaternion.identity, Quaternion.identity };
        internal readonly float[] m_IKPosW = new float[4], m_IKRotW = new float[4];
        internal readonly bool[] m_IKPosSet = new bool[4], m_IKRotSet = new bool[4];
        internal readonly Vector3[] m_HintPos = new Vector3[4]; internal readonly float[] m_HintW = new float[4];
        internal Vector3 m_LookAt; internal float m_LookW, m_LookBodyW, m_LookHeadW = 1, m_LookEyesW, m_LookClampW = 0.5f;
        internal readonly Dictionary<HumanBodyBones, Quaternion> m_BoneLocal = new();
        Dictionary<int, Transform> m_BoneCache;

        public bool isHuman => avatar != null && avatar.isHuman;
        public float humanScale
        {
            get
            {
                var hips = GetBoneTransform(HumanBodyBones.Hips);
                return hips != null ? Mathf.Max(1e-3f, (hips.position.y - transform.position.y) / 1f) : 1f;
            }
        }

        public Transform GetBoneTransform(HumanBodyBones humanBoneId)
        {
            if (humanBoneId < 0 || humanBoneId >= HumanBodyBones.LastBone) throw new IndexOutOfRangeException("humanBoneId must be between 0 and " + HumanBodyBones.LastBone);
            return GetBoneTransformInternal((int)humanBoneId);
        }

        internal Transform GetBoneTransformInternal(int humanBoneId)
        {
            var av = avatar;
            if (av == null || !av.isHuman) return null;
            m_BoneCache ??= new Dictionary<int, Transform>();
            if (m_BoneCache.TryGetValue(humanBoneId, out var t) && t != null) return t;
            var name = av.m_BoneNames[humanBoneId];
            if (name == null) return null;
            t = transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
            m_BoneCache[humanBoneId] = t;
            return t;
        }

        internal bool IsBoneTransform(Transform t) => isHuman && Enumerable.Range(0, HumanTrait.BoneCount).Any(i => GetBoneTransformInternal(i) == t);

        static int Goal(AvatarIKGoal g) => (int)g;
        Transform GoalBone(AvatarIKGoal g) => GetBoneTransform(g switch
        {
            AvatarIKGoal.LeftFoot => HumanBodyBones.LeftFoot, AvatarIKGoal.RightFoot => HumanBodyBones.RightFoot,
            AvatarIKGoal.LeftHand => HumanBodyBones.LeftHand, _ => HumanBodyBones.RightHand,
        });

        public Vector3 GetIKPosition(AvatarIKGoal goal) => m_IKPosSet[Goal(goal)] ? m_IKPos[Goal(goal)] : GoalBone(goal)?.position ?? transform.position;
        public void SetIKPosition(AvatarIKGoal goal, Vector3 goalPosition) { m_IKPos[Goal(goal)] = goalPosition; m_IKPosSet[Goal(goal)] = true; }
        public Quaternion GetIKRotation(AvatarIKGoal goal) => m_IKRotSet[Goal(goal)] ? m_IKRot[Goal(goal)] : GoalBone(goal)?.rotation ?? transform.rotation;
        public void SetIKRotation(AvatarIKGoal goal, Quaternion goalRotation) { m_IKRot[Goal(goal)] = goalRotation; m_IKRotSet[Goal(goal)] = true; }
        public float GetIKPositionWeight(AvatarIKGoal goal) => m_IKPosW[Goal(goal)];
        public void SetIKPositionWeight(AvatarIKGoal goal, float value) => m_IKPosW[Goal(goal)] = Mathf.Clamp01(value);
        public float GetIKRotationWeight(AvatarIKGoal goal) => m_IKRotW[Goal(goal)];
        public void SetIKRotationWeight(AvatarIKGoal goal, float value) => m_IKRotW[Goal(goal)] = Mathf.Clamp01(value);
        public Vector3 GetIKHintPosition(AvatarIKHint hint) => m_HintPos[(int)hint];
        public void SetIKHintPosition(AvatarIKHint hint, Vector3 hintPosition) => m_HintPos[(int)hint] = hintPosition;
        public float GetIKHintPositionWeight(AvatarIKHint hint) => m_HintW[(int)hint];
        public void SetIKHintPositionWeight(AvatarIKHint hint, float value) => m_HintW[(int)hint] = Mathf.Clamp01(value);
        public void SetLookAtPosition(Vector3 lookAtPosition) => m_LookAt = lookAtPosition;
        public void SetLookAtWeight(float weight) => SetLookAtWeight(weight, 0, 1, 0, 0.5f);
        public void SetLookAtWeight(float weight, float bodyWeight) => SetLookAtWeight(weight, bodyWeight, 1, 0, 0.5f);
        public void SetLookAtWeight(float weight, float bodyWeight, float headWeight) => SetLookAtWeight(weight, bodyWeight, headWeight, 0, 0.5f);
        public void SetLookAtWeight(float weight, float bodyWeight, float headWeight, float eyesWeight) => SetLookAtWeight(weight, bodyWeight, headWeight, eyesWeight, 0.5f);
        public void SetLookAtWeight(float weight, float bodyWeight, float headWeight, float eyesWeight, float clampWeight)
        {
            m_LookW = Mathf.Clamp01(weight); m_LookBodyW = Mathf.Clamp01(bodyWeight); m_LookHeadW = Mathf.Clamp01(headWeight);
            m_LookEyesW = Mathf.Clamp01(eyesWeight); m_LookClampW = Mathf.Clamp01(clampWeight);
        }
        public void SetBoneLocalRotation(HumanBodyBones humanBoneId, Quaternion rotation) => m_BoneLocal[humanBoneId] = rotation;

        /// <summary>After the controller's pose is written: OnAnimatorIK(layer) for layers with IK Pass, then the IK solve.</summary>
        internal void RunIK(IReadOnlyList<int> ikLayers)
        {
            if (!isHuman || !avatar.isValid || ikLayers.Count == 0) return;
            foreach (int layer in ikLayers)
            {
                m_BoneLocal.Clear();
                foreach (var mb in gameObject.m_Components.OfType<MonoBehaviour>().ToList())
                {
                    if (!mb.enabled) continue;
                    var m = mb.GetType().GetMethod("OnAnimatorIK", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
                    if (m == null) continue;
                    try { m.Invoke(mb, new object[] { layer }); }
                    catch (TargetInvocationException e) { MiniUnity.Play.Report(e.InnerException ?? e, mb.GetType().Name + ".OnAnimatorIK"); }
                }
                foreach (var (bone, rot) in m_BoneLocal) { var t = GetBoneTransform(bone); if (t != null) t.localRotation = rot; }
                SolveLookAt();
                for (int g = 0; g < 4; g++) SolveLimb((AvatarIKGoal)g);
            }
        }

        void SolveLimb(AvatarIKGoal goal)
        {
            int gi = Goal(goal);
            float wp = m_IKPosW[gi], wr = m_IKRotW[gi];
            if (wp <= 0 && wr <= 0) return;
            HumanBodyBones upper, lower, end;
            switch (goal)
            {
                case AvatarIKGoal.LeftFoot: upper = HumanBodyBones.LeftUpperLeg; lower = HumanBodyBones.LeftLowerLeg; end = HumanBodyBones.LeftFoot; break;
                case AvatarIKGoal.RightFoot: upper = HumanBodyBones.RightUpperLeg; lower = HumanBodyBones.RightLowerLeg; end = HumanBodyBones.RightFoot; break;
                case AvatarIKGoal.LeftHand: upper = HumanBodyBones.LeftUpperArm; lower = HumanBodyBones.LeftLowerArm; end = HumanBodyBones.LeftHand; break;
                default: upper = HumanBodyBones.RightUpperArm; lower = HumanBodyBones.RightLowerArm; end = HumanBodyBones.RightHand; break;
            }
            Transform a = GetBoneTransform(upper), b = GetBoneTransform(lower), c = GetBoneTransform(end);
            if (a == null || b == null || c == null) return;
            var endRot = c.rotation;
            if (wp > 0)
            {
                var target = Vector3.Lerp(c.position, GetIKPosition(goal), wp);
                var hint = m_HintW[gi] > 0 ? Vector3.Lerp(b.position, m_HintPos[gi], m_HintW[gi]) : b.position;
                TwoBone(a, b, c, target, hint);
            }
            c.rotation = wr > 0 ? Quaternion.Slerp(endRot, GetIKRotation(goal), wr) : endRot;
        }

        /// <summary>Analytic two-bone IK: bend the middle joint for the reach (law of cosines), then aim the chain at the target in the hint's plane.</summary>
        static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
        {
            var pa = a.position; var pb = b.position; var pc = c.position;
            float lab = (pb - pa).magnitude, lbc = (pc - pb).magnitude;
            if (lab < 1e-5f || lbc < 1e-5f) return;
            float lat = Mathf.Clamp((target - pa).magnitude, Mathf.Abs(lab - lbc) + 1e-4f, lab + lbc - 1e-4f);
            // current and wanted interior angle at b
            float cur = Vector3.Angle(pa - pb, pc - pb);
            float want = Mathf.Acos(Mathf.Clamp((lab * lab + lbc * lbc - lat * lat) / (2 * lab * lbc), -1, 1)) * Mathf.Rad2Deg;
            var bendAxis = Vector3.Cross(pc - pb, pa - pb);
            if (bendAxis.sqrMagnitude < 1e-10f) bendAxis = Vector3.Cross(target - pa, hint - pa);
            if (bendAxis.sqrMagnitude < 1e-10f) bendAxis = Vector3.Cross(pc - pa, Vector3.up);
            if (bendAxis.sqrMagnitude < 1e-10f) bendAxis = Vector3.right;
            b.rotation = Quaternion.AngleAxis(cur - want, bendAxis.normalized) * b.rotation;
            // swing the chain so the end points at the target
            pc = c.position;
            a.rotation = Quaternion.FromToRotation(pc - pa, target - pa) * a.rotation;
            // twist about the reach axis so the middle joint lies on the hint side
            var axis = (target - pa).normalized;
            var bNow = b.position - pa; var toHint = hint - pa;
            var bp = bNow - axis * Vector3.Dot(bNow, axis); var hp = toHint - axis * Vector3.Dot(toHint, axis);
            if (bp.sqrMagnitude > 1e-8f && hp.sqrMagnitude > 1e-8f)
                a.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(bp, hp, axis), axis) * a.rotation;
        }

        void SolveLookAt()
        {
            if (m_LookW <= 0) return;
            var head = GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;
            float maxAngle = Mathf.Lerp(180, 0, m_LookClampW);   // clamp 0 = free, 1 = no rotation
            void Turn(Transform t, float w)
            {
                if (t == null || w <= 0) return;
                var fwd = transform.forward;
                var want = m_LookAt - head.position;
                if (want.sqrMagnitude < 1e-8f) return;
                var cur = t.rotation * Quaternion.Inverse(transform.rotation) * fwd;
                var full = Quaternion.FromToRotation(cur, want);
                full.ToAngleAxis(out float ang, out var ax);
                if (ang > 180) ang -= 360;
                ang = Mathf.Clamp(ang, -maxAngle, maxAngle);
                if (float.IsNaN(ax.x)) return;
                t.rotation = Quaternion.AngleAxis(ang * w * m_LookW, ax) * t.rotation;
            }
            // body weight is shared by the spine chain, then the head, then the eyes
            var spine = new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest }.Select(GetBoneTransform).Where(t => t != null).ToList();
            foreach (var s in spine) Turn(s, m_LookBodyW / Math.Max(1, spine.Count));
            Turn(head, m_LookHeadW);
            Turn(GetBoneTransform(HumanBodyBones.LeftEye), m_LookEyesW);
            Turn(GetBoneTransform(HumanBodyBones.RightEye), m_LookEyesW);
        }
    }
}
