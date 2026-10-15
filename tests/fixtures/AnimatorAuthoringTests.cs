#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Controllers built with the UnityEditor.Animations API play at runtime; layer avatar masks filter transforms.</summary>
public class AnimatorAuthoringTests
{
    static AnimationClip Clip(string name, params (string path, float x)[] keys)
    {
        var clip = new AnimationClip { name = name };
        foreach (var (path, x) in keys) clip.SetCurve(path, typeof(Transform), "localPosition.x", AnimationCurve.Constant(0, 1, x));
        return clip;
    }

    static (Animator anim, Transform arm, Transform leg) Rig()
    {
        var root = new GameObject("Rig");
        var arm = new GameObject("Arm").transform; arm.SetParent(root.transform, false);
        var leg = new GameObject("Leg").transform; leg.SetParent(root.transform, false);
        return (root.AddComponent<Animator>(), arm, leg);
    }

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    [UnityTest] public IEnumerator TriggerTransitionBuiltFromCode()
    {
        var (anim, arm, _) = Rig();
        var c = new AnimatorController { name = "Code" };
        c.AddLayer("Base Layer");
        c.AddParameter("Go", AnimatorControllerParameterType.Trigger);
        var sm = c.layers[0].stateMachine;
        var idle = sm.AddState("Idle"); idle.motion = Clip("Idle", ("Arm", 1));
        var moved = sm.AddState("Moved"); moved.motion = Clip("Moved", ("Arm", 3));
        Assert.AreEqual(idle, sm.defaultState, "first state is the default");
        var t = idle.AddTransition(moved); t.duration = 0; t.AddCondition(AnimatorConditionMode.If, 0, "Go");
        Assert.AreEqual(1, idle.transitions.Length);
        Assert.AreEqual(moved, idle.transitions[0].destinationState);
        anim.runtimeAnimatorController = c;
        yield return Frames(2);
        Assert.AreEqual(1, arm.localPosition.x, 1e-4f);
        anim.SetTrigger("Go");
        yield return Frames(2);
        Assert.AreEqual(3, arm.localPosition.x, 1e-4f, "trigger fired the transition");
        Assert.IsTrue(anim.GetCurrentAnimatorStateInfo(0).IsName("Moved"));
    }

    [UnityTest] public IEnumerator LayerMaskOnlyAnimatesListedTransforms()
    {
        var (anim, arm, leg) = Rig();
        var mask = new AvatarMask();
        mask.AddTransformPath(anim.transform, true);
        for (int i = 0; i < mask.transformCount; i++) mask.SetTransformActive(i, mask.GetTransformPath(i) == "Arm");
        Assert.AreEqual(3, mask.transformCount, "root, Arm, Leg");
        var c = new AnimatorController { name = "Masked" };
        c.AddLayer("Base Layer");
        c.layers[0].stateMachine.AddState("Base").motion = Clip("Base", ("Arm", 1), ("Leg", 1));
        var top = new AnimatorControllerLayer { name = "Upper", defaultWeight = 1, avatarMask = mask, stateMachine = new AnimatorStateMachine() };
        c.AddLayer(top);
        c.layers[1].stateMachine.AddState("Wave").motion = Clip("Wave", ("Arm", 5), ("Leg", 5));
        anim.runtimeAnimatorController = c;
        yield return Frames(2);
        Assert.AreEqual(5, arm.localPosition.x, 1e-4f, "Arm is in the mask");
        Assert.AreEqual(1, leg.localPosition.x, 1e-4f, "Leg is masked out of the upper layer");
    }
}
#endif
