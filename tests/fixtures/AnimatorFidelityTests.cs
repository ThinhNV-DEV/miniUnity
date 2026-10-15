#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Animator blending details: rotations blend the short way round, additive layers add (clip - reference pose), every
/// weighted blend-tree clip fires its events, cullingMode follows renderer visibility, MatchTarget lands a body part on a target.</summary>
public class AnimatorFidelityTests
{
    class Sink : MonoBehaviour
    {
        public List<string> got = new List<string>();
        void OnA(AnimationEvent e) => got.Add("A:" + e.animatorClipInfo.weight.ToString("0.00"));
        void OnB(AnimationEvent e) => got.Add("B:" + e.animatorClipInfo.weight.ToString("0.00"));
    }

    static AnimationClip Clip(string name, float length = 1) => new AnimationClip { name = name };

    static void Rot(AnimationClip c, string path, Quaternion a, Quaternion b, float len = 1)
    {
        c.SetCurve(path, typeof(Transform), "localRotation.x", AnimationCurve.Linear(0, a.x, len, b.x));
        c.SetCurve(path, typeof(Transform), "localRotation.y", AnimationCurve.Linear(0, a.y, len, b.y));
        c.SetCurve(path, typeof(Transform), "localRotation.z", AnimationCurve.Linear(0, a.z, len, b.z));
        c.SetCurve(path, typeof(Transform), "localRotation.w", AnimationCurve.Linear(0, a.w, len, b.w));
    }

    static (Animator anim, Transform arm) Rig(AnimatorController c)
    {
        var root = new GameObject("Rig");
        var arm = new GameObject("Arm").transform; arm.SetParent(root.transform, false);
        var a = root.AddComponent<Animator>();
        a.runtimeAnimatorController = c;
        return (a, arm);
    }

    static AnimatorController Controller(Motion m)
    {
        var c = new AnimatorController { name = "Fidelity" };
        c.AddLayer("Base Layer");
        c.layers[0].stateMachine.AddState("Main").motion = m;
        return c;
    }

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    [UnityTest] public IEnumerator RotationsBlendTheShortWayRound()
    {
        var a = Clip("A"); Rot(a, "Arm", Quaternion.Euler(0, 0, 170), Quaternion.Euler(0, 0, 170));
        var b = Clip("B"); Rot(b, "Arm", Quaternion.Euler(0, 0, -170), Quaternion.Euler(0, 0, -170));
        Assert.Greater(Quaternion.Euler(0, 0, -170).w * Quaternion.Euler(0, 0, 170).w, 0, "both keyed with w > 0: opposite hemispheres along z");
        var c = new AnimatorController { name = "Blend" };
        c.AddLayer("Base Layer");
        c.CreateBlendTreeInController("Turn", out var tree);
        tree.AddChild(a); tree.AddChild(b);
        Assert.AreEqual(1f, tree.children[1].threshold, "automatic thresholds 0 and 1");
        var (anim, arm) = Rig(c);
        anim.SetFloat(tree.blendParameter, 0.5f);
        yield return Frames(3);
        Assert.Less(Quaternion.Angle(arm.localRotation, Quaternion.Euler(0, 0, 180)), 0.5f, "half way between 170 and 190 degrees is 180, not 0");
    }

    [UnityTest] public IEnumerator AdditiveLayerAddsTheDifferenceFromTheReferencePose()
    {
        var baseClip = Clip("Base");
        baseClip.SetCurve("Arm", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0, 1, 2));
        Rot(baseClip, "Arm", Quaternion.Euler(0, 0, 30), Quaternion.Euler(0, 0, 30));
        var nod = Clip("Nod");
        nod.SetCurve("Arm", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 5, 1, 6));
        Rot(nod, "Arm", Quaternion.identity, Quaternion.Euler(0, 0, 40));
        var c = Controller(baseClip);
        c.AddLayer(new AnimatorControllerLayer { name = "Add", defaultWeight = 1, blendingMode = AnimatorLayerBlendingMode.Additive, stateMachine = new AnimatorStateMachine() });
        c.layers[1].stateMachine.AddState("Nod").motion = nod;
        var (anim, arm) = Rig(c);
        yield return Frames(20);
        float t = Mathf.Min(anim.GetCurrentAnimatorStateInfo(1).normalizedTime, 1);
        Assert.Greater(t, 0.1f);
        Assert.AreEqual(2 + t, arm.localPosition.x, 0.02f, "base 2 plus (5 + t) - 5 from the first frame");
        Assert.AreEqual(30 + 40 * t, arm.localEulerAngles.z, 1.5f, "rotation added on top of the base 30 degrees");

        anim.SetLayerWeight(1, 0.5f);
        yield return null;
        t = Mathf.Min(anim.GetCurrentAnimatorStateInfo(1).normalizedTime, 1);
        Assert.AreEqual(2 + t * 0.5f, arm.localPosition.x, 0.02f, "layer weight scales the difference");

        var settings = AnimationUtility.GetAnimationClipSettings(nod);
        settings.hasAdditiveReferencePose = true; settings.additiveReferencePoseTime = 1;
        AnimationUtility.SetAnimationClipSettings(nod, settings);
        Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(nod).hasAdditiveReferencePose);
        anim.SetLayerWeight(1, 1);
        yield return null;
        t = Mathf.Min(anim.GetCurrentAnimatorStateInfo(1).normalizedTime, 1);
        Assert.AreEqual(2 + (t - 1), arm.localPosition.x, 0.02f, "reference pose taken at 1 s");
    }

    [UnityTest] public IEnumerator EveryWeightedBlendTreeClipFiresItsEvents()
    {
        AnimationClip Evented(string name, string fn)
        {
            var clip = Clip(name);
            clip.SetCurve("Arm", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0, 0, 1, 1));
            clip.AddEvent(new AnimationEvent { time = 0.2f, functionName = fn });
            return clip;
        }
        var c = new AnimatorController { name = "Events" };
        c.AddLayer("Base Layer");
        c.CreateBlendTreeInController("Tree", out var tree);
        tree.AddChild(Evented("A", "OnA")); tree.AddChild(Evented("B", "OnB"));
        var (anim, _) = Rig(c);
        var sink = anim.gameObject.AddComponent<Sink>();
        anim.SetFloat(tree.blendParameter, 0.25f);
        yield return Frames(30);
        CollectionAssert.AreEquivalent(new[] { "A:0.75", "B:0.25" }, sink.got);
    }

    static GameObject Visible(Animator anim)
    {
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.transform.SetParent(anim.transform, false);
        var cam = new GameObject("Cam").AddComponent<Camera>();
        cam.transform.position = new Vector3(0, 0, -10);
        return body;
    }

    [Test] public void FrustumPlanesAndRendererVisibility()
    {
        var cam = new GameObject("Cam").AddComponent<Camera>();
        cam.transform.position = new Vector3(0, 0, -10);
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        Assert.AreEqual(6, planes.Length);
        Assert.IsTrue(GeometryUtility.TestPlanesAABB(planes, new Bounds(Vector3.zero, Vector3.one)));
        Assert.IsFalse(GeometryUtility.TestPlanesAABB(planes, new Bounds(new Vector3(0, 0, -20), Vector3.one)), "behind the camera");
        Assert.IsFalse(GeometryUtility.TestPlanesAABB(planes, new Bounds(new Vector3(500, 0, 0), Vector3.one)), "off to the side");
        Assert.AreEqual(10 - cam.nearClipPlane, planes[4].GetDistanceToPoint(Vector3.zero), 1e-3f, "near plane faces into the frustum");
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<Renderer>();
        Assert.IsTrue(cube.isVisible);
        cube.transform.position = new Vector3(500, 0, 0);
        Assert.IsFalse(cube.isVisible);
        Assert.IsTrue(GeometryUtility.TryCreatePlaneFromPolygon(new[] { Vector3.zero, Vector3.right, Vector3.forward }, out var p));
        Assert.AreEqual(1, Mathf.Abs(p.normal.y), 1e-5f);
    }

    [UnityTest] public IEnumerator CullingModesFollowRendererVisibility()
    {
        var move = Clip("Move");
        move.SetCurve("Arm", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 10, 10));
        var (anim, arm) = Rig(Controller(move));
        Visible(anim);
        anim.cullingMode = AnimatorCullingMode.CullCompletely;
        anim.transform.position = new Vector3(1000, 0, 0);
        yield return Frames(10);
        Assert.AreEqual(0f, anim.GetCurrentAnimatorStateInfo(0).normalizedTime, 1e-6f, "off screen: the state machine does not run");
        Assert.AreEqual(0f, arm.localPosition.x);

        anim.transform.position = Vector3.zero;
        yield return Frames(10);
        float n = anim.GetCurrentAnimatorStateInfo(0).normalizedTime;
        Assert.Greater(n, 0);
        Assert.AreEqual(n * 10, arm.localPosition.x, 0.05f, "on screen it animates");

        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        anim.transform.position = new Vector3(1000, 0, 0);
        float x = arm.localPosition.x;
        yield return Frames(10);
        Assert.Greater(anim.GetCurrentAnimatorStateInfo(0).normalizedTime, n + 0.01f, "time keeps running");
        Assert.AreEqual(x, arm.localPosition.x, "but transforms are not written");

        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        yield return Frames(2);
        Assert.Greater(arm.localPosition.x, x);
    }

    [UnityTest] public IEnumerator MatchTargetLandsTheRootOnTheTarget()
    {
        var clip = Clip("Climb");
        clip.SetCurve("Arm", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0, 0, 1, 1));
        var (anim, _) = Rig(Controller(clip));
        yield return null;
        var target = new Vector3(4, 2, 0);
        anim.MatchTarget(target, Quaternion.Euler(0, 90, 0), AvatarTarget.Root, new MatchTargetWeightMask(new Vector3(1, 0, 1), 1), 0.2f, 0.6f);
        Assert.IsTrue(anim.isMatchingTarget);
        while (anim.GetCurrentAnimatorStateInfo(0).normalizedTime < 0.1f) yield return null;
        Assert.AreEqual(Vector3.zero, anim.transform.position, "nothing before the start of the window");
        while (anim.GetCurrentAnimatorStateInfo(0).normalizedTime < 0.4f) yield return null;
        Assert.That(anim.transform.position.x, Is.InRange(1f, 3.5f), "part of the way there in the middle of the window");
        while (anim.GetCurrentAnimatorStateInfo(0).normalizedTime < 0.6f) yield return null;
        Assert.IsFalse(anim.isMatchingTarget);
        Assert.AreEqual(4f, anim.transform.position.x, 1e-3f);
        Assert.AreEqual(0f, anim.transform.position.y, 1e-6f, "y is masked out");
        Assert.Less(Quaternion.Angle(anim.transform.rotation, Quaternion.Euler(0, 90, 0)), 0.1f);
    }
}
#endif
