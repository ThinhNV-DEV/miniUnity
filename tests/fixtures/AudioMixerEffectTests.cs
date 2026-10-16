using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;

// Effects inside an AudioMixer (FxMixer from audioassets.py): Music has a 100 Hz Lowpass, SFX sends at 0 dB to the Reverb
// group (Receive → SFX Reverb with the dry signal off and a 2 s decay).
public class AudioMixerEffectTests
{
    static T Load<T>(string path) where T : Object
    {
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
#else
        return null;
#endif
    }

    static float ListenerRms()
    {
        var d = AudioListener.GetOutputData(1024, 0);
        double s = 0; foreach (var x in d) s += x * x;
        return (float)System.Math.Sqrt(s / d.Length);
    }

    static AudioSource Playing(AudioClip clip, AudioMixerGroup group, bool loop = true)
    {
        var s = new GameObject("Src").AddComponent<AudioSource>();
        s.playOnAwake = false; s.clip = clip; s.loop = loop; s.outputAudioMixerGroup = group; s.Play();
        return s;
    }

    static IEnumerator Seconds(float t) { float end = Time.time + t; while (Time.time < end) yield return null; }

    [UnityTest] public IEnumerator LowpassOnTheGroupRemovesA440HzTone()
    {
        if (Object.FindObjectOfType<AudioListener>() == null) new GameObject("Listener").AddComponent<AudioListener>();
        var tone = Load<AudioClip>("Assets/AudioTest/tone440.wav");
        var plain = Load<AudioMixer>("Assets/AudioTest/TestMixer.mixer").FindMatchingGroups("Master/SFX").Single();
        var music = Load<AudioMixer>("Assets/AudioTest/FxMixer.mixer").FindMatchingGroups("Master/Music").Single();
        var a = Playing(tone, plain);
        yield return Seconds(0.3f);
        float open = ListenerRms();
        Object.Destroy(a.gameObject);
        yield return Seconds(0.1f);
        var b = Playing(tone, music);
        yield return Seconds(0.3f);
        float filtered = ListenerRms();
        Object.Destroy(b.gameObject);
        Assert.Greater(open, 0.2f, "0.5-amplitude sine through a plain group");
        Assert.Less(filtered, open * 0.1f, $"100 Hz lowpass: {filtered} vs {open}");
    }

    [UnityTest] public IEnumerator SendToReverbLeavesATail()
    {
        if (Object.FindObjectOfType<AudioListener>() == null) new GameObject("Listener").AddComponent<AudioListener>();
        var tone = Load<AudioClip>("Assets/AudioTest/tone440.wav");
        var sfx = Load<AudioMixer>("Assets/AudioTest/FxMixer.mixer").FindMatchingGroups("Master/SFX").Single();
        var s = Playing(tone, sfx);
        yield return Seconds(0.3f);
        s.Stop();
        yield return Seconds(0.3f);
        float tail = ListenerRms();
        Object.Destroy(s.gameObject);
        Assert.Greater(tail, 0.001f, "the reverb keeps sounding after the source stopped");
    }
}
