using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Playables;
using UnityEngine.TestTools;

// PlayableGraph in Play mode: Play() evaluates every frame, every playing playable's time advances by its speed × its parents'
// speeds, duration → IsDone, input weights in FrameData, and AudioClipPlayables sounding through an AudioPlayableOutput.
public class PlayableTests
{
    class Probe : PlayableBehaviour
    {
        public int frames; public float weight = -1, effectiveSpeed;
        public override void ProcessFrame(Playable playable, FrameData info, object playerData) { frames++; weight = info.weight; effectiveSpeed = info.effectiveSpeed; }
    }

    static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end - 1e-4f) yield return null; }

    static AudioClip Tone()
    {
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AudioTest/tone440.wav");
#else
        return null;
#endif
    }

    static float Rms(float[] d) { double s = 0; foreach (var x in d) s += x * x; return (float)System.Math.Sqrt(s / d.Length); }

    [UnityTest] public IEnumerator PlayingGraphAdvancesEveryNodeBySpeed()
    {
        var graph = PlayableGraph.Create("Test");
        var root = ScriptPlayable<Probe>.Create(graph, 2);
        var fast = ScriptPlayable<Probe>.Create(graph);
        var slow = ScriptPlayable<Probe>.Create(graph);
        graph.Connect(fast, 0, root, 0); graph.Connect(slow, 0, root, 1);
        root.SetInputWeight(0, 0.25f); root.SetInputWeight(1, 0.75f);
        root.SetSpeed(2); fast.SetSpeed(1.5); slow.SetSpeed(0.5);
        slow.SetDuration(0.3);
        var output = ScriptPlayableOutput.Create(graph, "out");
        output.SetSourcePlayable(root);
        graph.Play();
        Assert.IsTrue(graph.IsPlaying());
        float t0 = Time.time;
        yield return Seconds(0.5f);
        float dt = Time.time - t0;
        Assert.Greater(root.GetBehaviour().frames, 10, "a playing graph is evaluated every frame");
        Assert.AreEqual(2 * dt, root.GetTime(), 0.05, "root: speed 2");
        Assert.AreEqual(3 * dt, fast.GetTime(), 0.05, "child: its speed × the root's");
        Assert.AreEqual(1 * dt, slow.GetTime(), 0.05);
        Assert.IsTrue(slow.IsDone(), "time passed its duration");
        Assert.IsFalse(fast.IsDone());
        Assert.AreEqual(0.25f, fast.GetBehaviour().weight, 1e-6f, "ProcessFrame sees the input weight");
        Assert.AreEqual(0.75f, slow.GetBehaviour().weight, 1e-6f);
        Assert.AreEqual(3f, fast.GetBehaviour().effectiveSpeed, 1e-5f);

        fast.Pause();
        double ft = fast.GetTime();
        yield return null; yield return null;
        Assert.AreEqual(ft, fast.GetTime(), "a paused playable keeps its time");
        graph.Stop();
        int frames = root.GetBehaviour().frames;
        yield return null;
        Assert.AreEqual(frames, root.GetBehaviour().frames, "a stopped graph is not evaluated");
        graph.Destroy();
    }

    [UnityTest] public IEnumerator AudioClipPlayablesSoundThroughTheirOutput()
    {
        var src = new GameObject("Out").AddComponent<AudioSource>();
        src.playOnAwake = false;
        var graph = PlayableGraph.Create("Audio");
        var mixer = AudioMixerPlayable.Create(graph, 2);
        var a = AudioClipPlayable.Create(graph, Tone(), true);
        var b = AudioClipPlayable.Create(graph, Tone(), true);
        graph.Connect(a, 0, mixer, 0); graph.Connect(b, 0, mixer, 1);
        mixer.SetInputWeight(0, 1); mixer.SetInputWeight(1, 0);
        var output = AudioPlayableOutput.Create(graph, "audio", src);
        output.SetSourcePlayable(mixer);
        graph.Play();
        yield return null; yield return null; yield return null;
        float one = Rms(AudioListener.GetOutputData(1024, 0));
        Assert.Greater(one, 0.2f, "the weighted clip plays on the output's AudioSource");
        Assert.IsTrue(a.IsChannelPlaying()); Assert.IsFalse(b.IsChannelPlaying(), "weight 0: silent");
        Assert.IsFalse(src.isPlaying, "the source's own clip is not playing");

        mixer.SetInputWeight(0, 0.5f);
        yield return null;
        Assert.AreEqual(one * 0.5f, Rms(AudioListener.GetOutputData(1024, 0)), one * 0.05f, "input weight scales the volume");

        var left = AudioListener.GetOutputData(1024, 0); var right = AudioListener.GetOutputData(1024, 1);
        Assert.AreEqual(Rms(left), Rms(right), 1e-4f);
        mixer.SetInputWeight(0, 1);
        a.Pause();
        yield return null;
        Assert.Less(Rms(AudioListener.GetOutputData(1024, 0)), 1e-6f, "a paused clip playable stops");
        graph.Destroy();
    }
}
