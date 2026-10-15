using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;

// Mixer solo, filter components on an AudioSource (lowpass, highpass, echo with its tail, distortion) and OnAudioFilterRead
// scripts (processing a clip, and generating sound on a source without one).
public class AudioEffectsTests
{
    static T Load<T>(string path) where T : Object
    {
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
#else
        return null;
#endif
    }

    static AudioClip Tone() => Load<AudioClip>("Assets/AudioTest/tone440.wav");

    static float Rms(float[] d) { double s = 0; foreach (var x in d) s += x * x; return (float)System.Math.Sqrt(s / d.Length); }
    static float ListenerRms() => Rms(AudioListener.GetOutputData(1024, 0));

    static AudioSource Source(AudioClip clip, bool loop = true)
    {
        var s = new GameObject("Src").AddComponent<AudioSource>();
        s.playOnAwake = false; s.clip = clip; s.loop = loop;
        return s;
    }

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    class Gain : MonoBehaviour
    {
        public float gain = 0; public int calls, channels;
        void OnAudioFilterRead(float[] data, int ch) { calls++; channels = ch; for (int i = 0; i < data.Length; i++) data[i] *= gain; }
    }

    class Synth : MonoBehaviour
    {
        double phase; int rate;
        void Awake() { rate = AudioSettings.outputSampleRate; }
        void OnAudioFilterRead(float[] data, int ch)
        {
            for (int i = 0; i < data.Length; i += ch)
            {
                float v = 0.25f * Mathf.Sin((float)phase); phase += 2 * System.Math.PI * 220 / rate;
                for (int c = 0; c < ch; c++) data[i + c] = v;
            }
        }
    }

    [UnityTest] public IEnumerator SoloSilencesTheOtherGroups()
    {
        var mixer = Load<AudioMixer>("Assets/AudioTest/SoloMixer.mixer");
        Assert.IsNotNull(mixer);
        var s = Source(Tone());
        s.outputAudioMixerGroup = mixer.FindMatchingGroups("SFX").Single();
        s.Play();
        yield return Frames(3);
        Assert.Greater(ListenerRms(), 0.2f, "the soloed group plays");
        s.outputAudioMixerGroup = mixer.FindMatchingGroups("Music").Single();
        yield return Frames(1);
        Assert.Less(ListenerRms(), 1e-6f, "a group beside the soloed one is silent");
        s.outputAudioMixerGroup = mixer.FindMatchingGroups("Master")[0];
        yield return Frames(1);
        Assert.Less(ListenerRms(), 1e-6f, "so is a parent the source plays into directly");
    }

    [UnityTest] public IEnumerator LowPassAndHighPassFilters()
    {
        var s = Source(Tone());
        s.Play();
        yield return Frames(3);
        float dry = ListenerRms();
        Assert.Greater(dry, 0.2f);

        var lp = s.gameObject.AddComponent<AudioLowPassFilter>();
        Assert.AreEqual(5000, lp.cutoffFrequency); Assert.AreEqual(1, lp.lowpassResonanceQ);
        yield return Frames(3);
        Assert.AreEqual(dry, ListenerRms(), dry * 0.1f, "440 Hz passes a 5 kHz lowpass");
        lp.cutoffFrequency = 60;
        yield return Frames(5);
        Assert.Less(ListenerRms(), dry * 0.1f, "but not a 60 Hz one");
        lp.enabled = false;
        yield return Frames(3);
        Assert.AreEqual(dry, ListenerRms(), dry * 0.1f, "a disabled filter is bypassed");

        var hp = s.gameObject.AddComponent<AudioHighPassFilter>();
        hp.cutoffFrequency = 5000;
        yield return Frames(3);
        Assert.Less(ListenerRms(), dry * 0.1f, "5 kHz highpass removes the 440 Hz tone");
        hp.cutoffFrequency = 20;
        yield return Frames(5);
        Assert.AreEqual(dry, ListenerRms(), dry * 0.1f);
    }

    [UnityTest] public IEnumerator EchoRingsAfterTheClipEnds()
    {
        int rate = 44100;
        var burst = AudioClip.Create("Burst", rate / 10, 1, rate, false);
        var pcm = new float[rate / 10];
        for (int i = 0; i < pcm.Length; i++) pcm[i] = 0.5f * Mathf.Sin(2 * Mathf.PI * 440 * i / rate);
        burst.SetData(pcm, 0);
        var s = Source(burst, false);
        var echo = s.gameObject.AddComponent<AudioEchoFilter>();
        echo.delay = 300; echo.decayRatio = 0.5f; echo.wetMix = 1; echo.dryMix = 1;
        s.Play();
        yield return new WaitForSecondsRealtime(0.2f);
        Assert.IsFalse(s.isPlaying, "the clip itself has ended");
        Assert.Less(ListenerRms(), 1e-4f, "between the burst and its first echo");
        yield return new WaitForSecondsRealtime(0.15f);
        Assert.Greater(ListenerRms(), 0.05f, "first echo (300 ms)");
        yield return new WaitForSecondsRealtime(4f);
        Assert.Less(ListenerRms(), 1e-3f, "the echo has died out");
    }

    [UnityTest] public IEnumerator DistortionClipsPeaks()
    {
        var s = Source(Tone());
        s.Play();
        yield return Frames(3);
        float clean = AudioListener.GetOutputData(1024, 0).Max();
        var d = s.gameObject.AddComponent<AudioDistortionFilter>();
        d.distortionLevel = 0.9f;
        yield return Frames(3);
        var data = AudioListener.GetOutputData(1024, 0);
        Assert.Greater(data.Max(), clean * 1.5f, "soft clipping with gain pushes the 0.5 sine towards 1");
        Assert.LessOrEqual(data.Max(), 1f);
    }

    [UnityTest] public IEnumerator OnAudioFilterReadProcessesTheSource()
    {
        var s = Source(Tone());
        var g = s.gameObject.AddComponent<Gain>();
        s.Play();
        yield return Frames(3);
        Assert.Greater(g.calls, 0, "called while the source plays");
        Assert.AreEqual(2, g.channels);
        Assert.Less(ListenerRms(), 1e-6f, "the script zeroed the samples");
        g.gain = 1;
        yield return Frames(3);
        Assert.Greater(ListenerRms(), 0.2f);
    }

    [UnityTest] public IEnumerator OnAudioFilterReadGeneratesSoundWithoutAClip()
    {
        var s = Source(null);
        s.gameObject.AddComponent<Synth>();
        s.Play();
        Assert.IsTrue(s.isPlaying, "a clipless source with a filter script plays");
        yield return Frames(5);
        Assert.AreEqual(0.25f / Mathf.Sqrt(2), ListenerRms(), 0.02f, "the synth's 220 Hz sine");
        s.Stop();
        yield return Frames(2);
        Assert.Less(ListenerRms(), 1e-6f);
    }
}
