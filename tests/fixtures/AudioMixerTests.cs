using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;

public class AudioMixerTests
{
    static T Load<T>(string path) where T : Object
    {
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
#else
        return null;
#endif
    }

    static float Rms(float[] d, int from, int to)
    {
        double s = 0; for (int i = from; i < to; i++) s += d[i] * d[i];
        return (float)System.Math.Sqrt(s / (to - from));
    }

    static float ListenerRms()
    {
        var d = AudioListener.GetOutputData(1024, 0);
        return Rms(d, 0, d.Length);
    }

    static AudioSource Playing(AudioClip clip, AudioMixerGroup group)
    {
        var s = new GameObject("Src").AddComponent<AudioSource>();
        s.playOnAwake = false; s.clip = clip; s.loop = true; s.outputAudioMixerGroup = group; s.Play();
        return s;
    }

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    [Test] public void OggAndMp3ClipsDecode([Values("ogg", "mp3")] string ext)
    {
        var clip = Load<AudioClip>("Assets/AudioTest/tone440." + ext);
        Assert.IsNotNull(clip);
        Assert.AreEqual(1f, clip.length, 0.001f, "header length (MP3: gapless, without the Info frame, encoder delay and padding)");
        Assert.AreEqual(44100, clip.frequency);
        var data = new float[clip.samples * clip.channels];
        Assert.IsTrue(clip.GetData(data, 0));
        Assert.AreEqual(0.5f / Mathf.Sqrt(2), Rms(data, 4410, 30000), 0.02f, "decoded 0.5-amplitude sine");
        double err = 0;
        for (int i = 4410; i < 8820; i++) { double d = data[i * clip.channels] - 0.5 * System.Math.Sin(2 * System.Math.PI * 440 * i / 44100.0); err += d * d; }
        Assert.Less(System.Math.Sqrt(err / 4410), 0.02, "in phase with the source: the encoder delay is trimmed");
    }

    [UnityTest] public IEnumerator GroupVolumesSnapshotsAndExposedParameters()
    {
        var mixer = Load<AudioMixer>("Assets/AudioTest/TestMixer.mixer");
        Assert.IsNotNull(mixer);
        var tone = Load<AudioClip>("Assets/AudioTest/tone440.wav");
        var master = mixer.FindMatchingGroups("Master")[0];
        var music = mixer.FindMatchingGroups("Master/Music").Single();
        var sfx = mixer.FindMatchingGroups("SFX").Single();
        Assert.AreEqual(3, mixer.FindMatchingGroups("Master").Length, "the master path matches its children too");
        Assert.AreEqual(mixer, music.audioMixer);
        Assert.IsTrue(mixer.GetFloat("MusicVol", out float mv)); Assert.AreEqual(-6, mv, 1e-4f, "start snapshot value");
        Assert.IsFalse(mixer.GetFloat("Nope", out _));

        var src = Playing(tone, master);
        yield return Frames(3);
        float full = ListenerRms();
        Assert.Greater(full, 0.2f);
        src.outputAudioMixerGroup = music;
        yield return Frames(1);
        Assert.AreEqual(full * Mathf.Pow(10, -6 / 20f), ListenerRms(), 0.02f, "Music group at -6 dB");
        Assert.IsTrue(mixer.SetFloat("MusicVol", -20));
        yield return Frames(1);
        Assert.AreEqual(full * 0.1f, ListenerRms(), 0.01f, "exposed parameter overrides the snapshot");
        mixer.ClearFloat("MusicVol");
        yield return Frames(1);
        Assert.AreEqual(full * Mathf.Pow(10, -6 / 20f), ListenerRms(), 0.02f, "ClearFloat returns control to the snapshot");

        src.outputAudioMixerGroup = sfx;
        var quiet = mixer.FindSnapshot("Quiet");
        Assert.IsNotNull(quiet);
        quiet.TransitionTo(1f);
        yield return new WaitForSecondsRealtime(0.5f);
        Assert.AreEqual(full * Mathf.Pow(10, -40 / 20f), ListenerRms(), 0.01f, "halfway through a 1 s transition: -40 dB");
        yield return new WaitForSecondsRealtime(0.6f);
        Assert.Less(ListenerRms(), full * 0.001f, "Quiet snapshot silences SFX");
    }
}
