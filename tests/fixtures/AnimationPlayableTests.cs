using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.TestTools;

// Animation playables (clip / mixer / layer mixer → AnimationPlayableOutput) on the AnimTest clips:
// Idle moves Body.x 0→1 over its 1 s loop, Walk sets Body scale.x = 1, Run sets it to 3.
public class AnimationPlayableTests
{
    static AnimationClip Clip(string name)
    {
#if UNITY_EDITOR
        var c = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/AnimTest/{name}.anim");
#else
        AnimationClip c = null;
#endif
        Assert.IsNotNull(c, name);
        return c;
    }

    static (Animator a, Transform body) Rig(bool controller = false)
    {
        var go = new GameObject("Rig");
        var body = new GameObject("Body").transform; body.SetParent(go.transform, false);
        new GameObject("Arm").transform.SetParent(go.transform, false);
        var a = go.AddComponent<Animator>();
#if UNITY_EDITOR
        if (controller) a.runtimeAnimatorController = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/AnimTest/Hero.controller");
#endif
        return (a, body);
    }

    static PlayableGraph Graph(Animator a, Playable root)
    {
        var g = PlayableGraph.Create("test");
        g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        AnimationPlayableOutput.Create(g, "anim", a).SetSourcePlayable(root);
        return g;
    }

    [Test] public void ClipPlayableSamplesAtItsTimeAndLoops()
    {
        var (a, body) = Rig();
        var g = PlayableGraph.Create("clip");
        var clip = AnimationClipPlayable.Create(g, Clip("Idle"));
        var output = AnimationPlayableOutput.Create(g, "anim", a);
        output.SetSourcePlayable(clip);
        Assert.AreEqual(a, output.GetTarget());
        Assert.AreEqual("Idle", clip.GetAnimationClip().name);
        clip.SetTime(0.25); g.Evaluate();
        Assert.AreEqual(0.25f, body.localPosition.x, 1e-3f);
        clip.SetTime(1.5); g.Evaluate();
        Assert.AreEqual(0.5f, body.localPosition.x, 1e-3f, "looping clip wraps");
        g.Evaluate(0.25f);
        Assert.AreEqual(0.75f, body.localPosition.x, 1e-3f, "Evaluate(dt) advances the clip");
        g.Destroy();
    }

    [Test] public void MixerBlendsInputsByWeight()
    {
        var (a, body) = Rig();
        var g = PlayableGraph.Create("mix");
        var mixer = AnimationMixerPlayable.Create(g, 2);
        g.Connect(AnimationClipPlayable.Create(g, Clip("Walk")), 0, mixer, 0);
        g.Connect(AnimationClipPlayable.Create(g, Clip("Run")), 0, mixer, 1);
        AnimationPlayableOutput.Create(g, "anim", a).SetSourcePlayable(mixer);
        mixer.SetInputWeight(0, 0.5f); mixer.SetInputWeight(1, 0.5f);
        g.Evaluate();
        Assert.AreEqual(2f, body.localScale.x, 1e-3f, "50/50 of 1 and 3");
        mixer.SetInputWeight(0, 0); mixer.SetInputWeight(1, 0.5f);
        g.Evaluate();
        Assert.AreEqual(2f, body.localScale.x, 1e-3f, "missing weight goes to the default value (scale 1)");
        mixer.SetInputWeight(0, 0.25f); mixer.SetInputWeight(1, 0.75f);
        g.Evaluate();
        Assert.AreEqual(2.5f, body.localScale.x, 1e-3f);
        g.Destroy();
    }

    [Test] public void LayerMixerOverridesAndAdds()
    {
        var (a, body) = Rig();
        var g = PlayableGraph.Create("layers");
        var layers = AnimationLayerMixerPlayable.Create(g, 2);
        g.Connect(AnimationClipPlayable.Create(g, Clip("Run")), 0, layers, 0);
        g.Connect(AnimationClipPlayable.Create(g, Clip("Walk")), 0, layers, 1);
        AnimationPlayableOutput.Create(g, "anim", a).SetSourcePlayable(layers);
        layers.SetInputWeight(0, 1); layers.SetInputWeight(1, 0.5f);
        g.Evaluate();
        Assert.AreEqual(2f, body.localScale.x, 1e-3f, "override layer at 0.5 lerps 3 → 1");

        var mask = new AvatarMask();
        mask.AddTransformPath(a.transform, true);
        for (int i = 0; i < mask.transformCount; i++) if (mask.GetTransformPath(i) == "Body") mask.SetTransformActive(i, false);
        layers.SetLayerMaskFromAvatarMask(1, mask);
        g.Evaluate();
        Assert.AreEqual(3f, body.localScale.x, 1e-3f, "masked-out Body ignores layer 1");
        g.Destroy();

        var g2 = PlayableGraph.Create("additive");
        var add = AnimationLayerMixerPlayable.Create(g2, 2);
        var baseClip = AnimationClipPlayable.Create(g2, Clip("Idle"));
        var addClip = AnimationClipPlayable.Create(g2, Clip("Idle"));
        g2.Connect(baseClip, 0, add, 0); g2.Connect(addClip, 0, add, 1);
        add.SetInputWeight(0, 1); add.SetInputWeight(1, 1);
        add.SetLayerAdditive(1, true);
        Assert.IsTrue(add.IsLayerAdditive(1));
        AnimationPlayableOutput.Create(g2, "anim", a).SetSourcePlayable(add);
        baseClip.SetTime(0.25); addClip.SetTime(0.5);
        g2.Evaluate();
        Assert.AreEqual(0.75f, body.localPosition.x, 1e-3f, "additive adds 0.5 - reference(0)");
        g2.Destroy();
    }

    [UnityTest] public IEnumerator PlayClipAdvancesWithGameTime()
    {
        var (a, body) = Rig();
        AnimationPlayableUtilities.PlayClip(a, Clip("Idle"), out var g);
        Assert.IsTrue(g.IsPlaying());
        float start = Time.time;
        while (Time.time - start < 0.5f - 1e-4f) yield return null;
        Assert.AreEqual(0.5f, body.localPosition.x, 0.05f);
        g.Destroy();
    }

    [UnityTest] public IEnumerator GraphOutputLayersOverController()
    {
        var (a, body) = Rig(controller: true);
        Assert.IsNotNull(a.runtimeAnimatorController, "controller asset");
        yield return null;
        yield return null;
        Assert.Greater(body.localPosition.x, 0, "controller's Idle state moves Body.x");
        AnimationPlayableUtilities.PlayClip(a, Clip("Jump"), out var g);
        yield return null;
        yield return null;
        Assert.AreEqual(0f, body.localPosition.x, 1e-3f, "the graph's Jump pose replaces the controller's");
        Assert.Greater(body.localPosition.y, 0f, "Jump lifts Body");
        g.Destroy();
    }
}
