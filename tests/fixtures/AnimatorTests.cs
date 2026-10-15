using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AnimatorTests
{
    class EventSink : MonoBehaviour
    {
        public List<string> got = new List<string>();
        void OnApex(AnimationEvent e) => got.Add($"{e.functionName}:{e.floatParameter}:{e.intParameter}:{e.stringParameter}:{e.isFiredByAnimator}");
    }

    static T Load<T>(string path) where T : Object
    {
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
#else
        return null;
#endif
    }

    static (Animator anim, Transform body, Transform arm) Hero()
    {
        var go = new GameObject("Hero");
        var body = new GameObject("Body").transform; body.SetParent(go.transform, false);
        var arm = new GameObject("Arm").transform; arm.SetParent(go.transform, false);
        var a = go.AddComponent<Animator>();
        a.runtimeAnimatorController = Load<RuntimeAnimatorController>("Assets/AnimTest/Hero.controller");
        Assert.IsNotNull(a.runtimeAnimatorController, "controller asset");
        return (a, body, arm);
    }

    static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end - 1e-4f) yield return null; }

    [UnityTest] public IEnumerator DefaultStateSamplesClipOverTime()
    {
        var (a, body, _) = Hero();
        yield return null;
        Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).IsName("Idle"));
        Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"));
        Assert.AreEqual(Animator.StringToHash("Base Layer.Idle"), a.GetCurrentAnimatorStateInfo(0).fullPathHash);
        Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).IsTag("Ground"));
        Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).loop);
        yield return Seconds(0.5f);
        var info = a.GetCurrentAnimatorStateInfo(0);
        Assert.AreEqual(info.normalizedTime, body.localPosition.x, 0.02f, "Idle moves Body.x 0→1 per loop");
        yield return Seconds(0.75f);
        info = a.GetCurrentAnimatorStateInfo(0);
        Assert.Greater(info.normalizedTime, 1, "normalizedTime keeps counting loops");
        Assert.AreEqual(info.normalizedTime - Mathf.Floor(info.normalizedTime), body.localPosition.x, 0.02f, "looping clip wraps");
        Assert.AreEqual(1, a.GetCurrentAnimatorClipInfo(0).Length);
        Assert.AreEqual("Idle", a.GetCurrentAnimatorClipInfo(0)[0].clip.name);
    }

    [UnityTest] public IEnumerator FloatConditionCrossFadesIntoBlendTree()
    {
        var (a, body, _) = Hero();
        yield return null;
        a.SetFloat("Speed", 1);
        yield return null;
        Assert.IsTrue(a.IsInTransition(0), "Idle → Run cross-fade (0.25 s)");
        Assert.IsTrue(a.GetNextAnimatorStateInfo(0).IsName("Run"));
        yield return Seconds(0.3f);
        Assert.IsFalse(a.IsInTransition(0));
        Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).IsName("Run"));
        Assert.AreEqual(3, body.localScale.x, 1e-3f, "Speed 1 → Run clip");
        a.SetFloat("Speed", 0.5f);
        yield return null;
        Assert.AreEqual(2, body.localScale.x, 1e-3f, "1D blend tree halfway between Walk (1) and Run (3)");
        Assert.AreEqual(2, a.GetCurrentAnimatorClipInfo(0).Length);
        Assert.AreEqual(0, body.localPosition.x, 1e-4f, "Write Defaults: Idle's Body.x curve not in Run → default");
        a.SetFloat("Speed", 0);
        yield return Seconds(0.4f);
        Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).IsName("Idle"));
    }

    [UnityTest] public IEnumerator TriggerAnyStateEventAndBehaviour()
    {
        AnimTestBehaviour.Log.Clear();
        var (a, body, _) = Hero();
        var sink = a.gameObject.AddComponent<EventSink>();
        yield return null;
        a.SetTrigger("Jump");
        yield return null;
        Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).IsName("Jump"), "AnyState → Jump, 0 s");
        CollectionAssert.Contains(AnimTestBehaviour.Log, "enter:jump:True");
        yield return null;
        Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).IsName("Jump"), "trigger consumed: no re-entry (canTransitionToSelf off)");
        yield return Seconds(0.2f);
        Assert.Greater(body.localPosition.y, 1.2f, "jump arc");
        CollectionAssert.Contains(AnimTestBehaviour.Log, "update:jump");
        yield return Seconds(0.15f);
        CollectionAssert.AreEqual(new[] { "OnApex:7.5:3:apex:True" }, sink.got, "event at 0.25 s fired once");
        yield return Seconds(0.35f);
        Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "exit time 1 → Idle");
        CollectionAssert.Contains(AnimTestBehaviour.Log, "exit:jump");
        Assert.AreEqual(0, body.localPosition.y, 1e-3f, "Write Defaults restores y");
        Assert.AreEqual(1, sink.got.Count);
    }

    [UnityTest] public IEnumerator SecondLayerOverridesWithWeight()
    {
        var (a, _, arm) = Hero();
        yield return null;
        Assert.AreEqual(2, a.layerCount);
        Assert.AreEqual(1, a.GetLayerIndex("Upper"));
        a.SetInteger("Mode", 1);
        a.SetLayerWeight(1, 1);
        yield return Seconds(0.5f);
        Assert.IsTrue(a.GetCurrentAnimatorStateInfo(1).IsName("Wave"));
        Assert.AreEqual(45, arm.localEulerAngles.z, 2f, "Wave: arm z 0→90 over 1 s");
        a.SetLayerWeight(1, 0.5f);
        yield return null;
        float z = arm.localEulerAngles.z;
        Assert.Less(z, 30, "half layer weight halves the override");
    }

    [UnityTest] public IEnumerator PlayCrossFadeAndSpeed()
    {
        var (a, body, _) = Hero();
        yield return null;
        a.Play("Jump", 0, 0.5f);
        yield return null;
        Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).IsName("Jump"));
        Assert.AreEqual(0.5f, a.GetCurrentAnimatorStateInfo(0).normalizedTime, 0.05f);
        a.CrossFadeInFixedTime("Idle", 0.2f);
        yield return null;
        Assert.IsTrue(a.IsInTransition(0));
        Assert.AreEqual(DurationUnit.Fixed, a.GetAnimatorTransitionInfo(0).durationUnit);
        yield return Seconds(0.3f);
        Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).IsName("Idle"));
        a.speed = 2;
        float n0 = a.GetCurrentAnimatorStateInfo(0).normalizedTime;
        yield return Seconds(0.25f);
        Assert.AreEqual(0.5f, a.GetCurrentAnimatorStateInfo(0).normalizedTime - n0, 0.05f, "Animator.speed scales time");
    }

    [UnityTest] public IEnumerator SpriteSwapControllerFromUnitySample()
    {
        var go = new GameObject("player");
        var child = new GameObject("player_0"); child.transform.SetParent(go.transform, false);
        var sr = child.AddComponent<SpriteRenderer>();
        var a = go.AddComponent<Animator>();
        a.runtimeAnimatorController = Load<RuntimeAnimatorController>("Assets/RoboHero/Animations/player model.controller");
        yield return null;
        Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).IsName("player_idle"), "default state of the sample controller");
        a.SetBool("moving", true);
        yield return null; yield return null;
        Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).IsName("player_walk"));
        var seen = new HashSet<Sprite>();
        for (int i = 0; i < 40; i++) { yield return null; if (sr.sprite != null) seen.Add(sr.sprite); }
        Assert.GreaterOrEqual(seen.Count, 3, "walk cycle swaps the SpriteRenderer sprite (PPtr curve)");
        a.SetBool("alive", false);
        yield return null; yield return null;
        Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).IsName("player_die"));
    }

    [UnityTest] public IEnumerator ClipFromScriptAndLegacyAnimation()
    {
        var go = new GameObject("Legacy");
        var clip = new AnimationClip { legacy = true, name = "slide" };
        clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1, 10));
        clip.AddEvent(new AnimationEvent { time = 0.5f, functionName = "Half", intParameter = 2 });
        var anim = go.AddComponent<Animation>();
        anim.AddClip(clip, "slide");
        var sink = go.AddComponent<HalfSink>();
        Assert.IsTrue(anim.Play("slide"));
        yield return Seconds(0.5f);
        Assert.AreEqual(5, go.transform.localPosition.x, 0.3f);
        Assert.IsTrue(anim.IsPlaying("slide"));
        yield return Seconds(0.7f);
        Assert.IsFalse(anim.isPlaying, "WrapMode.Once stops at the end");
        Assert.AreEqual(1, sink.count, "legacy events fire");
        Assert.AreEqual(1, clip.length, 1e-5f);
        var probe = new GameObject("Probe");
        clip.SampleAnimation(probe, 0.25f);
        Assert.AreEqual(2.5f, probe.transform.localPosition.x, 1e-3f, "SampleAnimation");
    }

    class HalfSink : MonoBehaviour { public int count; void Half(int i) { if (i == 2) count++; } }
}
