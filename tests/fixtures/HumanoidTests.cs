#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Humanoid avatar built from a bone map; OnAnimatorIK with limb IK and look-at.</summary>
public class HumanoidTests
{
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    static readonly Dictionary<string, Transform> bones = new();

    static Transform Bone(string name, Transform parent, Vector3 local)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false); t.localPosition = local;
        bones[name] = t;
        return t;
    }

    // a T-pose stick figure: hips 1 m up, arms 0.3 m + 0.3 m along ±X, legs 0.45 m + 0.45 m down
    static Animator Figure(bool ikPass)
    {
        bones.Clear();
        var root = new GameObject("Figure");
        var hips = Bone("hips", root.transform, new Vector3(0, 1, 0));
        var spine = Bone("spine", hips, new Vector3(0, 0.2f, 0));
        var chest = Bone("chest", spine, new Vector3(0, 0.2f, 0));
        var neck = Bone("neck", chest, new Vector3(0, 0.15f, 0));
        Bone("head", neck, new Vector3(0, 0.1f, 0));
        foreach (var (side, sx) in new[] { ("L", -1f), ("R", 1f) })
        {
            var shoulder = Bone(side + "_shoulder", chest, new Vector3(0.1f * sx, 0.1f, 0));
            var upper = Bone(side + "_upperarm", shoulder, new Vector3(0.1f * sx, 0, 0));
            var lower = Bone(side + "_forearm", upper, new Vector3(0.3f * sx, 0, 0));
            Bone(side + "_hand", lower, new Vector3(0.3f * sx, 0, 0));
            var thigh = Bone(side + "_thigh", hips, new Vector3(0.1f * sx, 0, 0));
            var shin = Bone(side + "_shin", thigh, new Vector3(0, -0.45f, 0));
            Bone(side + "_foot", shin, new Vector3(0, -0.45f, 0));
        }
        var map = new (string human, string bone)[]
        {
            ("Hips", "hips"), ("Spine", "spine"), ("Chest", "chest"), ("Neck", "neck"), ("Head", "head"),
            ("LeftShoulder", "L_shoulder"), ("LeftUpperArm", "L_upperarm"), ("LeftLowerArm", "L_forearm"), ("LeftHand", "L_hand"),
            ("RightShoulder", "R_shoulder"), ("RightUpperArm", "R_upperarm"), ("RightLowerArm", "R_forearm"), ("RightHand", "R_hand"),
            ("LeftUpperLeg", "L_thigh"), ("LeftLowerLeg", "L_shin"), ("LeftFoot", "L_foot"),
            ("RightUpperLeg", "R_thigh"), ("RightLowerLeg", "R_shin"), ("RightFoot", "R_foot"),
        };
        var human = new List<HumanBone>();
        foreach (var (h, b) in map) human.Add(new HumanBone { humanName = h, boneName = b });
        var avatar = AvatarBuilder.BuildHumanAvatar(root, new HumanDescription { human = human.ToArray() });
        var anim = root.AddComponent<Animator>();
        anim.avatar = avatar;
        var c = new AnimatorController { name = "Humanoid" };
        c.AddLayer("Base Layer");
        c.layers[0].stateMachine.AddState("Idle");
        var layers = c.layers; layers[0].iKPass = ikPass; c.layers = layers;
        anim.runtimeAnimatorController = c;
        return anim;
    }

    public class ReachTarget : MonoBehaviour
    {
        public Vector3 target; public Vector3 look; public int calls; public int lastLayer = -1;
        void OnAnimatorIK(int layerIndex)
        {
            calls++; lastLayer = layerIndex;
            var a = GetComponent<Animator>();
            a.SetIKPositionWeight(AvatarIKGoal.RightHand, 1);
            a.SetIKPosition(AvatarIKGoal.RightHand, target);
            a.SetLookAtWeight(1, 0, 1, 0, 0);
            a.SetLookAtPosition(look);
        }
    }

    [Test] public void AvatarFromABoneMap()
    {
        var anim = Figure(false);
        Assert.IsTrue(anim.avatar.isHuman);
        Assert.IsTrue(anim.avatar.isValid, "every required bone is mapped");
        Assert.IsTrue(anim.isHuman);
        Assert.AreEqual(bones["R_hand"], anim.GetBoneTransform(HumanBodyBones.RightHand));
        Assert.AreEqual(bones["head"], anim.GetBoneTransform(HumanBodyBones.Head));
        Assert.IsNull(anim.GetBoneTransform(HumanBodyBones.LeftToes), "unmapped optional bone");
        Assert.AreEqual("Left Thumb Proximal", HumanTrait.BoneName[(int)HumanBodyBones.LeftThumbProximal]);
        Assert.AreEqual((int)HumanBodyBones.LeftLowerArm, HumanTrait.GetParentBone((int)HumanBodyBones.LeftHand));
        Assert.IsTrue(HumanTrait.RequiredBone((int)HumanBodyBones.Hips));
        Assert.IsFalse(HumanTrait.RequiredBone((int)HumanBodyBones.LeftToes));
    }

    [UnityTest] public IEnumerator HandReachesAnIKGoal()
    {
        var anim = Figure(true);
        var reach = anim.gameObject.AddComponent<ReachTarget>();
        reach.target = new Vector3(0.45f, 1.55f, 0.25f);   // within the 0.6 m arm from the shoulder at (0.2, 1.5, 0)
        reach.look = new Vector3(0, 1.55f, 5);
        yield return Frames(3);
        Assert.Greater(reach.calls, 0, "OnAnimatorIK runs for a layer with IK Pass");
        Assert.AreEqual(0, reach.lastLayer);
        Assert.AreEqual(0f, Vector3.Distance(bones["R_hand"].position, reach.target), 0.01f, "hand on the goal");
        Assert.AreEqual(0.3f, Vector3.Distance(bones["R_upperarm"].position, bones["R_forearm"].position), 1e-3f, "bone lengths kept");
        Assert.AreEqual(0.3f, Vector3.Distance(bones["R_forearm"].position, bones["R_hand"].position), 1e-3f);
        Assert.AreEqual(new Vector3(-0.8f, 1.5f, 0).x, bones["L_hand"].position.x, 1e-3f, "the other hand is untouched");
    }

    [UnityTest] public IEnumerator OutOfReachGoalStretchesTheArmTowardIt()
    {
        var anim = Figure(true);
        var reach = anim.gameObject.AddComponent<ReachTarget>();
        reach.target = new Vector3(2, 1.5f, 0);
        yield return Frames(2);
        var dir = (reach.target - bones["R_upperarm"].position).normalized;
        Assert.Greater(Vector3.Dot((bones["R_hand"].position - bones["R_upperarm"].position).normalized, dir), 0.999f, "arm points at the goal");
    }

    [UnityTest] public IEnumerator NoIKPassNoCallback()
    {
        var anim = Figure(false);
        var reach = anim.gameObject.AddComponent<ReachTarget>();
        yield return Frames(3);
        Assert.AreEqual(0, reach.calls);
    }

    [UnityTest] public IEnumerator HeadLooksAtTheTarget()
    {
        var anim = Figure(true);
        var reach = anim.gameObject.AddComponent<ReachTarget>();
        reach.target = bones["R_hand"].position;
        reach.look = new Vector3(5, 1.55f, 5);   // 45° to the right
        yield return Frames(2);
        var fwd = bones["head"].rotation * Vector3.forward;
        Assert.AreEqual(45f, Vector3.SignedAngle(Vector3.forward, fwd, Vector3.up), 1f);
    }
}
#endif
