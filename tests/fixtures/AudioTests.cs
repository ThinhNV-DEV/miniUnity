using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AudioTests
{
    static AudioClip Tone()
    {
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AudioTest/tone440.wav");
#else
        return null;
#endif
    }

    static AudioSource Source(AudioClip clip, Vector3 pos = default)
    {
        var go = new GameObject("Speaker"); go.transform.position = pos;
        var s = go.AddComponent<AudioSource>(); s.playOnAwake = false; s.clip = clip;
        return s;
    }

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    [UnityTest] public IEnumerator ClipImportAndPlaybackTimeline()
    {
        var clip = Tone();
        Assert.IsNotNull(clip);
        Assert.AreEqual(1f, clip.length, 1e-4f); Assert.AreEqual(44100, clip.frequency); Assert.AreEqual(1, clip.channels); Assert.AreEqual(44100, clip.samples);
        var s = Source(clip);
        s.Play();
        Assert.IsTrue(s.isPlaying);
        double dsp0 = AudioSettings.dspTime;
        yield return Frames(30);
        Assert.AreEqual(AudioSettings.dspTime - dsp0, s.time, 0.02, "clip time follows the DSP clock");
        s.pitch = 2;
        float t0 = s.time;
        yield return Frames(6);
        Assert.AreEqual(t0 + 2 * 6 / 60f, s.time, 0.02f, "pitch 2 plays twice as fast");
        yield return Frames(30);
        Assert.IsFalse(s.isPlaying, "non-looping clip ends");
        s.pitch = 1; s.loop = true; s.Play();
        yield return Frames(75);
        Assert.IsTrue(s.isPlaying);
        Assert.AreEqual(0.25f, s.time, 0.03f, "looping clip wraps");
        Assert.AreEqual((int)(s.time * 44100), s.timeSamples, 50);
    }

    [UnityTest] public IEnumerator TimeScaleDoesNotStopAudioButPauseDoes()
    {
        var s = Source(Tone()); s.loop = true; s.Play();
        Time.timeScale = 0;
        yield return Frames(12);
        Assert.AreEqual(0.2f, s.time, 0.03f, "audio runs on the DSP clock");
        Time.timeScale = 1;
        s.Pause();
        Assert.IsFalse(s.isPlaying);
        float t = s.time;
        yield return Frames(12);
        Assert.AreEqual(t, s.time, 1e-5f);
        s.UnPause();
        AudioListener.pause = true;
        yield return Frames(12);
        Assert.AreEqual(t, s.time, 1e-5f, "AudioListener.pause");
        AudioListener.pause = false;
        yield return Frames(6);
        Assert.Greater(s.time, t);
    }

    [UnityTest] public IEnumerator DelayedAndScheduledPlayback()
    {
        var a = Source(Tone()); var b = Source(Tone());
        a.PlayDelayed(0.5f);
        b.PlayScheduled(AudioSettings.dspTime + 0.25);
        Assert.IsTrue(a.isPlaying, "isPlaying while scheduled");
        yield return Frames(20);
        Assert.AreEqual(0, a.time, 1e-5f, "not started yet");
        Assert.Greater(b.time, 0.05f, "scheduled at dsp + 0.25");
        yield return Frames(25);
        Assert.Greater(a.time, 0.15f);
        b.SetScheduledEndTime(AudioSettings.dspTime + 0.1);
        yield return Frames(10);
        Assert.IsFalse(b.isPlaying, "scheduled end");
    }

    [UnityTest] public IEnumerator SpatialAttenuationAndSpectrum()
    {
        var lis = new GameObject("Listener"); lis.AddComponent<AudioListener>();
        var near = Source(Tone(), new Vector3(0, 0, 1)); near.spatialBlend = 1; near.loop = true;
        near.Play();
        yield return Frames(5);
        float peakNear = Peak(AudioListener.GetOutputData(1024, 0));
        near.transform.position = new Vector3(0, 0, 4);
        yield return null;
        float peakFar = Peak(AudioListener.GetOutputData(1024, 0));
        Assert.AreEqual(0.5f, peakNear, 0.02f, "within minDistance: full volume");
        Assert.AreEqual(peakNear / 4, peakFar, 0.02f, "logarithmic rolloff: minDistance / distance");
        near.rolloffMode = AudioRolloffMode.Linear; near.maxDistance = 8;
        yield return null;
        Assert.AreEqual(0.5f * (1 - 3f / 7f), Peak(AudioListener.GetOutputData(1024, 0)), 0.02f, "linear rolloff");
        near.transform.position = new Vector3(4, 0, 0);
        yield return null;
        var right = AudioListener.GetOutputData(1024, 1); var left = AudioListener.GetOutputData(1024, 0);
        Assert.Greater(Peak(right), Peak(left), "source on the right pans right");
        var spec = near.GetSpectrumData(1024, 0, FFTWindow.BlackmanHarris);
        int best = 0; for (int i = 1; i < spec.Length; i++) if (spec[i] > spec[best]) best = i;
        float hzPerBin = AudioSettings.outputSampleRate / 2f / 1024;
        Assert.AreEqual(440, best * hzPerBin, 2 * hzPerBin, "spectrum peak at 440 Hz");
    }

    static float Peak(float[] d) { float m = 0; foreach (var x in d) m = Mathf.Max(m, Mathf.Abs(x)); return m; }

    [UnityTest] public IEnumerator OneShotsCreateAndPlayOnAwake()
    {
        var s = Source(Tone());
        s.PlayOneShot(Tone(), 0.5f);
        yield return Frames(3);
        Assert.IsFalse(s.isPlaying, "one-shots don't make the source 'playing'");
        Assert.Greater(Peak(s.GetOutputData(256, 0)), 0.1f, "but they are audible");
        s.Stop();
        Assert.AreEqual(0, Peak(s.GetOutputData(256, 0)), 1e-6f, "Stop() also stops one-shots");

        int pos = 0;
        var gen = AudioClip.Create("saw", 4800, 1, 48000, false, data => { for (int i = 0; i < data.Length; i++, pos++) data[i] = (pos % 100) / 100f; });
        Assert.AreEqual(0.1f, gen.length, 1e-5f);
        var buf = new float[10]; gen.GetData(buf, 150);
        Assert.AreEqual(0.5f, buf[0], 1e-5f, "PCMReaderCallback filled the clip");
        gen.SetData(new[] { 1f, 1f }, 0);
        gen.GetData(buf, 0);
        Assert.AreEqual(1f, buf[1], 1e-6f);

        var go = new GameObject("Auto"); go.SetActive(false);
        var auto = go.AddComponent<AudioSource>(); auto.clip = Tone(); auto.playOnAwake = true;
        go.SetActive(true);
        yield return null;
        Assert.IsTrue(auto.isPlaying, "playOnAwake starts on enable");
        go.SetActive(false);
        yield return null;
        Assert.IsFalse(auto.isPlaying, "disabling stops playback");

        AudioSource.PlayClipAtPoint(Tone(), Vector3.one);
        yield return null;
        Assert.IsNotNull(GameObject.Find("One shot audio"));
        yield return Frames(70);
        Assert.IsNull(GameObject.Find("One shot audio"), "temporary source destroyed after the clip length");
    }
}
