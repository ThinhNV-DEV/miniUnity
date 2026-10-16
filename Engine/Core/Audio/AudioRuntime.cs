// Audio emulation for MiniUnity: a DSP clock (AudioSettings.dspTime, unaffected by Time.timeScale), voices for AudioSource
// playback (Play / PlayDelayed / PlayScheduled / PlayOneShot / loop / pitch / pause / scheduled end), distance attenuation
// and panning at the AudioListener, a play log of every start / stop / end, offline mixdown to a WAV file and
// GetOutputData / GetSpectrumData from the voices that are playing. Clip data: PCM / float WAV decoded on demand;
// Ogg Vorbis and MP3 clips are decoded in managed code (NVorbis, NLayer). Sources with filter components or OnAudioFilterRead
// scripts are rendered through that chain while they play (AudioFilters.cs).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity.Audio
{
    public sealed class Voice
    {
        public AudioSource Source; public AudioClip Clip; public bool OneShot; public float VolumeScale = 1;
        public double StartDsp, EndDsp = double.MaxValue; public bool Started, Paused, Finished;
        public double ClipPos;                  // seconds into the clip
        public bool Loop;
        public double LastDsp;
        public readonly List<Snap> Snaps = new();   // for mixdown
        public struct Snap { public double Dsp, ClipPos; public float Pitch, GainL, GainR; public bool Audible; }
        // voices whose source has a DSP chain (filters / OnAudioFilterRead) are rendered while they play: stereo samples after
        // the chain and the gains, starting at output sample RenderStart
        public FilterChain Chain; public List<float> Rendered; public long RenderStart, RenderNext; public float LastGainL = -1, LastGainR;
        public double TailUntil;
        public bool FromPlayable; public float Pan;   // driven by an AudioClipPlayable (PlayableAudio.cs)
        public MixerBus Bus; public UnityEngine.Audio.AudioMixerGroup BusGroup;   // routed to a mixer with effects (MixerBus.cs)
    }

    public sealed class AudioEvent
    {
        public double Dsp, Time; public int Frame; public string Source, Clip, Action; public float Volume, Pitch, ClipTime; public Vector3 Position;
    }

    public static class AudioSystem
    {
        public static double DspTime;
        public static int OutputSampleRate = 48000;
        public static float ListenerVolume = 1; public static bool ListenerPause;
        public static readonly List<Voice> Voices = new();
        public static readonly List<Voice> Finished = new();
        /// <summary>Finished voices whose filter chain is still ringing (echo).</summary>
        public static readonly List<Voice> Tails = new();
        public static readonly List<AudioEvent> Log = new();
        public static bool Recording = true;
        static readonly HashSet<AudioSource> enabledSources = new();
        static bool settingsLoaded;

        public static void Reset()
        {
            DspTime = 0; ListenerVolume = 1; ListenerPause = false;
            PlayableAudio.Reset();
            Voices.Clear(); Finished.Clear(); Tails.Clear(); Log.Clear(); enabledSources.Clear(); settingsLoaded = false;
            MixerBus.Reset();
        }

        public static void EnsureSettings()
        {
            if (settingsLoaded) return;
            settingsLoaded = true;
            try
            {
                var path = Path.Combine(AssetIndex.Root ?? ".", "ProjectSettings/AudioManager.asset");
                if (!File.Exists(path)) return;
                var m = Yaml.YamlReader.ReadFile(path).FirstOrDefault()?.Body as Yaml.YMap;
                if (m?["m_SampleRate"] is Yaml.YScalar sr && sr.L > 0) OutputSampleRate = (int)sr.L;
                if (m?["m_Volume"] is Yaml.YScalar v && v.Value.Length > 0) ListenerVolume = v.F;
            }
            catch { }
        }

        static string PathOf(Component c) => c == null ? "" : Hier.Path(c.transform);

        public static void Record(string action, AudioSource src, AudioClip clip, float clipTime = 0, float volume = -1, double dsp = -1)
        {
            if (!Recording) return;
            Log.Add(new AudioEvent
            {
                Dsp = dsp >= 0 ? dsp : DspTime, Time = Play.Time, Frame = Play.Frame, Source = PathOf(src), Clip = clip != null ? clip.name : null, Action = action,
                Volume = volume >= 0 ? volume : src != null ? src.volume : 1, Pitch = src != null ? src.pitch : 1, ClipTime = clipTime,
                Position = src != null ? src.transform.position : Vector3.zero,
            });
        }

        public static Voice MainVoice(AudioSource s) => Voices.FirstOrDefault(v => v.Source == s && !v.OneShot && !v.FromPlayable);

        public static void StopPlayableVoice(Voice v)
        {
            if (!Voices.Contains(v)) return;
            if (v.Started) Record("playable stop", v.Source, v.Clip, (float)v.ClipPos);
            Finish(v);
        }

        public static void StopVoices(AudioSource s, bool oneShots, string action = "stop")
        {
            foreach (var v in Voices.Where(v => v.Source == s && !v.FromPlayable && (oneShots || !v.OneShot)).ToList())
            {
                if (v.Started) Record(action, s, v.Clip, (float)v.ClipPos);
                Finish(v);
            }
        }

        static void Finish(Voice v)
        {
            v.Finished = true; Voices.Remove(v); Finished.Add(v);
            if ((v.Rendered != null || v.Bus != null) && v.Chain != null) { double tail = v.Chain.Tail; if (tail > 0) { v.TailUntil = DspTime + tail; Tails.Add(v); } }
        }

        /// <summary>AudioSources that became enabled since the last frame: playOnAwake starts them (Unity does this on enable).</summary>
        public static void CheckPlayOnAwake()
        {
            EnsureSettings();
            var now = new HashSet<AudioSource>();
            foreach (var go in Play.SceneObjects().ToList())
            {
                if (go.m_Destroyed || !go.activeInHierarchy) continue;
                foreach (var c in go.m_Components)
                    if (c is AudioSource a && !a.m_Destroyed && a.enabled)
                    {
                        now.Add(a);
                        if (!enabledSources.Contains(a) && a.playOnAwake && a.clip != null) a.Play();
                    }
            }
            // disabled / destroyed sources stop playing
            foreach (var a in enabledSources) if (!now.Contains(a)) StopVoices(a, true, "stop (disabled)");
            enabledSources.Clear(); enabledSources.UnionWith(now);
        }

        static AudioListener Listener()
        {
            foreach (var go in Play.SceneObjects())
                if (!go.m_Destroyed && go.activeInHierarchy)
                    foreach (var c in go.m_Components) if (c is AudioListener l && l.enabled) return l;
            return null;
        }

        /// <summary>Gains at the listener: volume · distance rolloff (blended by spatialBlend) and stereo pan.</summary>
        public static (float l, float r) Gains(Voice v, AudioListener listener, bool includeGroup = true)
        {
            var s = v.Source;
            if (s == null || s.m_Destroyed) return (v.VolumeScale, v.VolumeScale);
            float vol = s.mute ? 0 : s.volume * v.VolumeScale * (s.ignoreListenerVolume ? 1 : ListenerVolume);
            var group = s.outputAudioMixerGroup;
            if (group != null && includeGroup) vol *= group.Gain();
            float blend = Mathf.Clamp01(s.spatialBlend);
            float pan = Mathf.Clamp(s.panStereo + v.Pan, -1, 1) * (1 - blend);
            if (blend > 0 && listener != null)
            {
                var lt = listener.transform;
                var d = s.transform.position - lt.position;
                float dist = d.magnitude;
                float att = s.Attenuation(dist);
                vol *= Mathf.Lerp(1, att, blend);
                if (dist > 1e-5f) pan += Mathf.Clamp(Vector3.Dot(lt.InverseTransformDirection(d / dist), Vector3.right), -1, 1) * blend;
            }
            pan = Mathf.Clamp(pan, -1, 1);
            return (vol * Math.Min(1, 1 - pan), vol * Math.Min(1, 1 + pan));
        }

        /// <summary>Advance the DSP clock by one frame of real time and every voice with it.</summary>
        public static void Update(float unscaledDt)
        {
            EnsureSettings();
            double prev = DspTime;
            DspTime += unscaledDt;
            MixerBus.BeginFrame(prev, DspTime, OutputSampleRate);
            var listener = Listener();
            foreach (var v in Voices.ToList())
            {
                var s = v.Source;
                if (s != null && s.m_Destroyed) { Finish(v); continue; }
                bool paused = v.Paused || ListenerPause && !(s != null && s.ignoreListenerPause);
                float pitch = s != null ? s.pitch : 1;
                double from = Math.Max(prev, v.StartDsp), to = Math.Min(DspTime, v.EndDsp);
                if (!v.Started && DspTime >= v.StartDsp && !paused)
                {
                    v.Started = true;
                    Record(v.FromPlayable ? "playable" : v.OneShot ? "oneshot" : "start", s, v.Clip, (float)v.ClipPos, s != null ? s.volume * v.VolumeScale : v.VolumeScale, Math.Max(v.StartDsp, prev));
                }
                // a voice routed to a mixer with effects is mixed live into that mixer's group bus (gains without the group volumes)
                var group = s != null && !s.m_Destroyed ? s.outputAudioMixerGroup : null;
                var bus = group != null ? MixerBus.For(group) : null;
                if (bus != null) { v.Bus = bus; v.BusGroup = group; }
                var (gl, gr) = Gains(v, listener, includeGroup: bus == null);
                if (v.Started && !paused && to > from)
                {
                    if (bus == null) v.Snaps.Add(new Voice.Snap { Dsp = from, ClipPos = v.ClipPos, Pitch = pitch, GainL = gl, GainR = gr, Audible = true });
                    if (s != null && (v.Chain != null || FilterChain.HasFilters(s.gameObject))) v.Chain ??= new FilterChain();
                    if (v.Chain != null || bus != null) RenderLive(v, from, to, pitch, gl, gr);
                    double len = v.Clip != null ? v.Clip.length : 0;
                    if (v.Clip == null && v.Chain != null && v.Chain.HasScript) { v.LastDsp = DspTime; continue; }   // procedural: the script makes the sound
                    v.ClipPos += (to - from) * pitch;
                    if (len > 0)
                    {
                        if (v.Loop && !v.OneShot) { if (v.ClipPos >= len || v.ClipPos < 0) v.ClipPos -= Math.Floor(v.ClipPos / len) * len; }
                        else if (v.ClipPos >= len || v.ClipPos < 0)
                        {
                            v.ClipPos = Math.Clamp(v.ClipPos, 0, len);
                            Record(v.FromPlayable ? "playable end" : v.OneShot ? "oneshot end" : "end", s, v.Clip, (float)v.ClipPos);
                            Finish(v);
                            continue;
                        }
                    }
                    else { Finish(v); continue; }
                }
                if (v.Started && DspTime >= v.EndDsp)
                {
                    Record("scheduled end", s, v.Clip, (float)v.ClipPos);
                    Finish(v);
                }
                v.LastDsp = DspTime;
            }
            // echo tails: silence through the chain until it dies out
            foreach (var v in Tails.ToList())
            {
                double to = Math.Min(DspTime, v.TailUntil);
                if (to > prev) RenderLive(v, prev, to, 0, v.LastGainL, v.LastGainR, silent: true);
                if (DspTime >= v.TailUntil) Tails.Remove(v);
            }
            MixerBus.EndFrame();
        }

        /// <summary>Render [from, to) of a voice through its source's filter chain (input before gains, gains ramped from the last
        /// frame's), appending to <see cref="Voice.Rendered"/> at the output rate.</summary>
        static void RenderLive(Voice v, double from, double to, float pitch, float gl, float gr, bool silent = false)
        {
            int rate = OutputSampleRate;
            if (v.Bus != null) { if (v.RenderNext == 0 && v.RenderStart == 0) v.RenderStart = v.RenderNext = (long)(from * rate); }
            else if (v.Rendered == null) { v.Rendered = new List<float>(); v.RenderStart = v.RenderNext = (long)(from * rate); }
            long a = v.RenderNext, b = (long)(to * rate);
            if (b <= a) return;
            int n = (int)(b - a);
            var buf = new float[n * 2];
            var clip = silent ? null : v.Clip;
            double len = clip != null ? clip.length : 0;
            if (clip != null)
                for (int i = 0; i < n; i++)
                {
                    double cp = v.ClipPos + ((a + i) / (double)rate - from) * pitch;
                    if (v.Loop && !v.OneShot && len > 0) cp -= Math.Floor(cp / len) * len;
                    else if (cp < 0 || cp > len) continue;
                    float sl = Sample(clip, cp, 0);
                    buf[i * 2] = sl; buf[i * 2 + 1] = clip.m_Channels > 1 ? Sample(clip, cp, 1) : sl;
                }
            var go = v.Source != null && !v.Source.m_Destroyed ? v.Source.gameObject : null;
            if (v.Chain != null && v.Chain.Update(go)) v.Chain.Process(buf, 2, rate);
            float l0 = v.LastGainL < 0 ? gl : v.LastGainL, r0 = v.LastGainL < 0 ? gr : v.LastGainR;
            if (v.Bus != null)
            {
                for (int i = 0; i < n; i++)
                {
                    float f = (i + 1) / (float)n;
                    buf[i * 2] *= l0 + (gl - l0) * f; buf[i * 2 + 1] *= r0 + (gr - r0) * f;
                }
                v.Bus.Add(v.BusGroup, a, buf, n);
            }
            else
                for (int i = 0; i < n; i++)
                {
                    float f = (i + 1) / (float)n;
                    v.Rendered.Add(buf[i * 2] * (l0 + (gl - l0) * f));
                    v.Rendered.Add(buf[i * 2 + 1] * (r0 + (gr - r0) * f));
                }
            v.LastGainL = gl; v.LastGainR = gr;
            v.RenderNext = b;
        }

        // ------------------------------------------------------------------ sample access
        public static float Sample(AudioClip clip, double seconds, int channel)
        {
            var data = clip?.Data();
            if (data == null || clip.m_Channels <= 0 || clip.m_Frequency <= 0) return 0;
            double pos = seconds * clip.m_Frequency;
            int frames = data.Length / clip.m_Channels;
            if (frames == 0) return 0;
            int i0 = (int)Math.Floor(pos); float f = (float)(pos - i0);
            int ch = Math.Min(channel, clip.m_Channels - 1);
            float S(int i) => i < 0 || i >= frames ? 0 : data[i * clip.m_Channels + ch];
            return S(i0) + (S(i0 + 1) - S(i0)) * f;
        }

        /// <summary>The last <paramref name="n"/> output samples of the voices playing now (optionally only one source).</summary>
        public static float[] OutputData(int n, int channel, AudioSource only = null)
        {
            var res = new float[n];
            var listener = Listener();
            if (only == null && MixerBus.LiveL.Count > 0)
            {
                // the mixers with effects: the last n samples of their live mix
                var live = channel == 0 ? MixerBus.LiveL : MixerBus.LiveR; int end = live.Count;
                for (int k = 0; k < n; k++) { int i = end - n + k; if (i >= 0) res[k] += live[i]; }
            }
            foreach (var v in Voices.Concat(Tails))
            {
                if (!v.Started || v.Paused || only != null && v.Source != only) continue;
                if (only == null && v.Bus != null) continue;
                if (v.Rendered != null)
                {
                    int have = v.Rendered.Count / 2;
                    for (int k = 0; k < n; k++) { int i = have - n + k; if (i >= 0) res[k] += v.Rendered[i * 2 + Math.Min(channel, 1)]; }
                    continue;
                }
                var (gl, gr) = only != null ? (only.volume * v.VolumeScale, only.volume * v.VolumeScale) : Gains(v, listener);
                float g = channel == 0 ? gl : gr;
                float pitch = v.Source != null ? v.Source.pitch : 1;
                for (int k = 0; k < n; k++)
                {
                    double t = v.ClipPos - (n - 1 - k) / (double)OutputSampleRate * pitch;
                    if (t < 0 && !v.Loop) continue;
                    if (v.Loop && v.Clip.length > 0) t -= Math.Floor(t / v.Clip.length) * v.Clip.length;
                    res[k] += g * Sample(v.Clip, t, channel);
                }
            }
            return res;
        }

        public static float[] Spectrum(float[] samples, FFTWindow window)
        {
            int n = samples.Length;
            if (n < 64 || n > 8192 || (n & (n - 1)) != 0) throw new ArgumentException("Length of samples must be a power of two between 64 and 8192.");
            int size = 2 * n;   // Unity returns numSamples bins from a 2·numSamples FFT
            var re = new double[size]; var im = new double[size];
            for (int i = 0; i < n; i++) re[i] = samples[i] * Window(window, i, n);
            Fft(re, im);
            var res = new float[n];
            for (int k = 0; k < n; k++) res[k] = (float)(Math.Sqrt(re[k] * re[k] + im[k] * im[k]) / n);
            return res;
        }

        static double Window(FFTWindow w, int i, int n)
        {
            double x = i / (double)(n - 1);
            return w switch
            {
                FFTWindow.Triangle => 1 - Math.Abs(2 * x - 1),
                FFTWindow.Hamming => 0.54 - 0.46 * Math.Cos(2 * Math.PI * x),
                FFTWindow.Hanning => 0.5 - 0.5 * Math.Cos(2 * Math.PI * x),
                FFTWindow.Blackman => 0.42 - 0.5 * Math.Cos(2 * Math.PI * x) + 0.08 * Math.Cos(4 * Math.PI * x),
                FFTWindow.BlackmanHarris => 0.35875 - 0.48829 * Math.Cos(2 * Math.PI * x) + 0.14128 * Math.Cos(4 * Math.PI * x) - 0.01168 * Math.Cos(6 * Math.PI * x),
                _ => 1,
            };
        }

        static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                for (int i = 0; i < n; i += len)
                    for (int k = 0; k < len / 2; k++)
                    {
                        double wr = Math.Cos(ang * k), wi = Math.Sin(ang * k);
                        double ur = re[i + k], ui = im[i + k];
                        double vr = re[i + k + len / 2] * wr - im[i + k + len / 2] * wi, vi = re[i + k + len / 2] * wi + im[i + k + len / 2] * wr;
                        re[i + k] = ur + vr; im[i + k] = ui + vi; re[i + k + len / 2] = ur - vr; im[i + k + len / 2] = ui - vi;
                    }
            }
        }

        /// <summary>Filters on the AudioListener's GameObject process the whole mix (in 1024-frame blocks, as Unity's DSP buffers).</summary>
        static void ListenerChain(float[] L, float[] R, int rate)
        {
            var listener = Listener();
            if (listener == null || !FilterChain.HasFilters(listener.gameObject)) return;
            var chain = new FilterChain();
            if (!chain.Update(listener.gameObject)) return;
            var block = new float[1024 * 2];
            for (int start = 0; start < L.Length; start += 1024)
            {
                int n = Math.Min(1024, L.Length - start);
                if (n * 2 != block.Length) block = new float[n * 2];
                for (int i = 0; i < n; i++) { block[i * 2] = L[start + i]; block[i * 2 + 1] = R[start + i]; }
                chain.Process(block, 2, rate);
                for (int i = 0; i < n; i++) { L[start + i] = block[i * 2]; R[start + i] = block[i * 2 + 1]; }
            }
        }

        // ------------------------------------------------------------------ offline mixdown
        /// <summary>Render everything played so far to a 16-bit stereo WAV (gains / pitch per frame, linear interpolation).</summary>
        public static (int voices, double seconds, float peak) MixDown(string path, int rate = 0)
        {
            rate = rate > 0 ? rate : OutputSampleRate;
            int total = (int)Math.Ceiling(DspTime * rate);
            var L = new float[total]; var R = new float[total];
            int used = 0;
            foreach (var v in Finished.Concat(Voices))
            {
                if (v.Bus != null) { used++; continue; }   // mixed live through its mixer's effects (MixerBus.LiveL / LiveR below)
                if (v.Rendered != null)
                {
                    // rendered live through the source's filter chain (at the output rate; resampled linearly for another rate)
                    used++;
                    int frames = v.Rendered.Count / 2;
                    double scale = OutputSampleRate / (double)rate;
                    int k0 = (int)(v.RenderStart / scale), k1 = Math.Min(total, (int)Math.Ceiling((v.RenderStart + frames) / scale));
                    for (int k = Math.Max(0, k0); k < k1; k++)
                    {
                        double p = k * scale - v.RenderStart; int i = (int)Math.Floor(p); float f = (float)(p - i);
                        float R0(int j, int c) => j < 0 || j >= frames ? 0 : v.Rendered[j * 2 + c];
                        L[k] += R0(i, 0) + (R0(i + 1, 0) - R0(i, 0)) * f; R[k] += R0(i, 1) + (R0(i + 1, 1) - R0(i, 1)) * f;
                    }
                    continue;
                }
                if (v.Snaps.Count == 0 || v.Clip == null) continue;
                used++;
                double len = v.Clip.length;
                for (int si = 0; si < v.Snaps.Count; si++)
                {
                    var sn = v.Snaps[si];
                    double end = si + 1 < v.Snaps.Count ? v.Snaps[si + 1].Dsp : v.Finished ? sn.Dsp + (len > 0 ? Math.Max(0, (len - sn.ClipPos) / Math.Max(1e-6, Math.Abs(sn.Pitch))) : 0) : DspTime;
                    if (si + 1 < v.Snaps.Count && v.Snaps[si + 1].Dsp - sn.Dsp > 0.5) end = Math.Min(end, sn.Dsp + 0.5);
                    var next = si + 1 < v.Snaps.Count ? v.Snaps[si + 1] : sn;
                    int a = (int)(sn.Dsp * rate), b = Math.Min(total, (int)(end * rate));
                    for (int k = a; k < b; k++)
                    {
                        double t = k / (double)rate; float f = b > a ? (k - a) / (float)(b - a) : 0;
                        double cp = sn.ClipPos + (t - sn.Dsp) * sn.Pitch;
                        if (v.Loop && len > 0) cp -= Math.Floor(cp / len) * len;
                        else if (cp < 0 || cp > len) continue;
                        float gl = sn.GainL + (next.GainL - sn.GainL) * f, gr = sn.GainR + (next.GainR - sn.GainR) * f;
                        float sl = Sample(v.Clip, cp, 0), sr = v.Clip.m_Channels > 1 ? Sample(v.Clip, cp, 1) : sl;
                        L[k] += sl * gl; R[k] += sr * gr;
                    }
                }
            }
            if (MixerBus.LiveL.Count > 0)
            {
                double scale = OutputSampleRate / (double)rate; int have = MixerBus.LiveL.Count;
                for (int k = 0; k < total; k++)
                {
                    double p = k * scale; int i = (int)p; float f = (float)(p - i);
                    float S(List<float> x, int j) => j < 0 || j >= have ? 0 : x[j];
                    L[k] += S(MixerBus.LiveL, i) + (S(MixerBus.LiveL, i + 1) - S(MixerBus.LiveL, i)) * f;
                    R[k] += S(MixerBus.LiveR, i) + (S(MixerBus.LiveR, i + 1) - S(MixerBus.LiveR, i)) * f;
                }
            }
            ListenerChain(L, R, rate);
            float peak = 0;
            for (int i = 0; i < total; i++) peak = Math.Max(peak, Math.Max(Math.Abs(L[i]), Math.Abs(R[i])));
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            int dataLen = total * 4;
            w.Write("RIFF".ToCharArray()); w.Write(36 + dataLen); w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(dataLen);
            for (int i = 0; i < total; i++)
            {
                w.Write((short)Math.Clamp((int)Math.Round(L[i] * 32767), -32768, 32767));
                w.Write((short)Math.Clamp((int)Math.Round(R[i] * 32767), -32768, 32767));
            }
            return (used, total / (double)rate, peak);
        }
    }

    // ===================================================================================================== decoders
    public static class AudioDecode
    {
        /// <summary>WAV header → format; returns the byte offset / length of the data chunk.</summary>
        public static bool WavInfo(string path, out int channels, out int rate, out int bits, out int format, out long dataOffset, out long dataLen)
        {
            channels = 1; rate = 44100; bits = 16; format = 1; dataOffset = 0; dataLen = 0;
            try
            {
                using var f = File.OpenRead(path);
                using var r = new BinaryReader(f);
                if (new string(r.ReadChars(4)) != "RIFF") return false;
                r.ReadInt32(); if (new string(r.ReadChars(4)) != "WAVE") return false;
                while (f.Position <= f.Length - 8)
                {
                    var id = new string(r.ReadChars(4)); int len = r.ReadInt32();
                    if (id == "fmt ")
                    {
                        format = r.ReadInt16(); channels = r.ReadInt16(); rate = r.ReadInt32(); r.ReadInt32(); r.ReadInt16(); bits = r.ReadInt16();
                        if (len >= 40 && format == unchecked((short)0xFFFE)) { r.ReadInt16(); r.ReadInt16(); r.ReadInt32(); format = r.ReadInt16(); r.ReadBytes(len - 26); }
                        else if (len > 16) r.ReadBytes(len - 16);
                    }
                    else if (id == "data") { dataOffset = f.Position; dataLen = Math.Min(len, f.Length - f.Position); return true; }
                    else r.ReadBytes(len + (len & 1));
                }
            }
            catch { }
            return false;
        }

        /// <summary>Decode an Ogg Vorbis / MP3 file to interleaved floats at <paramref name="channels"/> × <paramref name="rate"/>,
        /// trimmed / padded to <paramref name="frames"/>: NVorbis / NLayer in process, ffmpeg only if they cannot read the file.</summary>
        public static float[] DecodeCompressed(string path, int channels, int rate, long frames)
        {
            try
            {
                float[] pcm; int ch, sr;
                if (path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                {
                    using var vr = new NVorbis.VorbisReader(path);
                    ch = vr.Channels; sr = vr.SampleRate;
                    pcm = ReadAll((b, o, c) => vr.ReadSamples(b, o, c), ch, vr.TotalSamples);
                }
                else
                {
                    using var mp = new NLayer.MpegFile(path);
                    ch = mp.Channels; sr = mp.SampleRate;
                    pcm = ReadAll((b, o, c) => mp.ReadSamples(b, o, c), ch, 0);
                    // gapless: drop the encoder delay (+ 529 samples of decoder delay) the LAME tag records, like ffmpeg / FMOD
                    if (Mp3Gapless(File.ReadAllBytes(path), out int delay, out _) && delay >= 0)
                    {
                        int skip = Math.Min(pcm.Length / ch, delay + 529) * ch;
                        pcm = pcm.AsSpan(skip).ToArray();
                    }
                }
                return Conform(pcm, ch, sr, channels, rate, frames);
            }
            catch { return DecodeWithFfmpeg(path, channels, rate, frames); }
        }

        static float[] ReadAll(Func<float[], int, int, int> read, int channels, long hint)
        {
            var all = new List<float>(hint > 0 ? (int)Math.Min(int.MaxValue / 2, hint * channels) : 1 << 16);
            var buf = new float[4096 * channels];
            int n;
            while ((n = read(buf, 0, buf.Length)) > 0) for (int i = 0; i < n; i++) all.Add(buf[i]);
            return all.ToArray();
        }

        /// <summary>Channel count (mono ↔ stereo / first channels), sample rate (linear) and length to the clip's header values.</summary>
        static float[] Conform(float[] pcm, int ch, int sr, int channels, int rate, long frames)
        {
            int inFrames = pcm.Length / Math.Max(1, ch);
            long outFrames = frames > 0 ? frames : (long)Math.Round(inFrames * (double)rate / sr);
            var res = new float[outFrames * channels];
            double step = sr / (double)rate;
            for (long k = 0; k < outFrames; k++)
            {
                double p = k * step; long i = (long)p; float f = (float)(p - i);
                for (int c = 0; c < channels; c++)
                {
                    float S(long j)
                    {
                        if (j >= inFrames) return 0;
                        if (ch == channels || c < ch && channels < ch) return pcm[j * ch + c];
                        if (ch == 1) return pcm[j];
                        if (channels == 1) { float sum = 0; for (int q = 0; q < ch; q++) sum += pcm[j * ch + q]; return sum / ch; }
                        return pcm[j * ch + Math.Min(c, ch - 1)];
                    }
                    res[k * channels + c] = sr == rate ? S(i) : S(i) + (S(i + 1) - S(i)) * f;
                }
            }
            return res;
        }

        static float[] DecodeWithFfmpeg(string path, int channels, int rate, long frames)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                };
                foreach (var a in new[] { "-v", "error", "-nostdin", "-i", path, "-f", "f32le", "-acodec", "pcm_f32le", "-ac", channels.ToString(), "-ar", rate.ToString(), "-" })
                    psi.ArgumentList.Add(a);
                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return null;
                var errTask = p.StandardError.ReadToEndAsync();
                using var ms = new MemoryStream();
                p.StandardOutput.BaseStream.CopyTo(ms);
                p.WaitForExit();
                _ = errTask.Result;
                if (p.ExitCode != 0) return null;
                var bytes = ms.ToArray();
                long n = frames > 0 ? frames * channels : bytes.Length / 4;
                var res = new float[n];
                Buffer.BlockCopy(bytes, 0, res, 0, (int)Math.Min(bytes.Length, n * 4));
                return res;
            }
            catch { return null; }
        }

        public static float[] DecodeWav(string path)
        {
            if (!WavInfo(path, out int ch, out _, out int bits, out int fmt, out long off, out long len)) return null;
            var bytes = new byte[len];
            using (var f = File.OpenRead(path)) { f.Position = off; f.Read(bytes, 0, (int)len); }
            int bps = bits / 8; int n = (int)(len / Math.Max(1, bps));
            var res = new float[n];
            for (int i = 0; i < n; i++)
            {
                int o = i * bps;
                res[i] = (fmt, bits) switch
                {
                    (3, 32) => BitConverter.ToSingle(bytes, o),
                    (_, 8) => (bytes[o] - 128) / 128f,
                    (_, 16) => BitConverter.ToInt16(bytes, o) / 32768f,
                    (_, 24) => ((bytes[o] | bytes[o + 1] << 8 | (sbyte)bytes[o + 2] << 16)) / 8388608f,
                    (_, 32) => BitConverter.ToInt32(bytes, o) / 2147483648f,
                    _ => 0,
                };
            }
            return res;
        }

        /// <summary>Ogg Vorbis: channels / rate from the identification header, length from the last page's granule position.</summary>
        public static bool OggInfo(string path, out int channels, out int rate, out long frames)
        {
            channels = 2; rate = 44100; frames = 0;
            try
            {
                var b = File.ReadAllBytes(path);
                if (b.Length < 64 || b[0] != 'O' || b[1] != 'g' || b[2] != 'g' || b[3] != 'S') return false;
                int segs = b[26]; int hdr = 27 + segs;
                if (b[hdr] == 1 && b[hdr + 1] == 'v') { channels = b[hdr + 11]; rate = BitConverter.ToInt32(b, hdr + 12); }
                for (int i = b.Length - 14; i >= 0; i--)
                    if (b[i] == 'O' && b[i + 1] == 'g' && b[i + 2] == 'g' && b[i + 3] == 'S') { frames = BitConverter.ToInt64(b, i + 6); break; }
                return frames > 0;
            }
            catch { return false; }
        }

        static readonly int[,] mp3Bitrates = { { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 }, { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 }, { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 } };
        static readonly int[,] mp3Bitrates2 = { { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 }, { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 }, { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 } };

        static int SamplesPerFrame(byte[] b)
        {
            int i = b.Length > 10 && b[0] == 'I' && b[1] == 'D' && b[2] == '3' ? 10 + (b[6] << 21 | b[7] << 14 | b[8] << 7 | b[9]) : 0;
            while (i + 4 <= b.Length && !(b[i] == 0xFF && (b[i + 1] & 0xE0) == 0xE0)) i++;
            if (i + 4 > b.Length) return 1152;
            int ver = (b[i + 1] >> 3) & 3, lay = 4 - ((b[i + 1] >> 1) & 3);
            return lay == 1 ? 384 : lay == 3 && ver != 3 ? 576 : 1152;
        }

        /// <summary>The Xing / Info frame an encoder writes first: it carries no audio, and its LAME tag (when present) holds the
        /// encoder delay and end padding in samples (-1 when there is no tag).</summary>
        public static bool Mp3Gapless(byte[] b, out int delay, out int padding)
        {
            delay = padding = -1;
            int i = b.Length > 10 && b[0] == 'I' && b[1] == 'D' && b[2] == '3' ? 10 + (b[6] << 21 | b[7] << 14 | b[8] << 7 | b[9]) : 0;
            while (i + 4 <= b.Length && !(b[i] == 0xFF && (b[i + 1] & 0xE0) == 0xE0)) i++;
            if (i + 200 > b.Length) return false;
            int x = -1;
            for (int k = i + 4; k < i + 64; k++)
                if ((b[k] == 'X' && b[k + 1] == 'i' && b[k + 2] == 'n' && b[k + 3] == 'g') || (b[k] == 'I' && b[k + 1] == 'n' && b[k + 2] == 'f' && b[k + 3] == 'o')) { x = k; break; }
            if (x < 0) return false;
            int flags = b[x + 7], pos = x + 8;
            if ((flags & 1) != 0) pos += 4;
            if ((flags & 2) != 0) pos += 4;
            if ((flags & 4) != 0) pos += 100;
            if ((flags & 8) != 0) pos += 4;
            if (pos + 24 <= b.Length && b[pos] >= 'A' && b[pos] <= 'Z')   // "LAME3.100", "Lavc58.x" …
            {
                delay = b[pos + 21] << 4 | b[pos + 22] >> 4;
                padding = (b[pos + 22] & 0xF) << 8 | b[pos + 23];
            }
            return true;
        }

        /// <summary>MP3: walk the frame headers (skipping ID3v2) and count samples (gapless: without the Xing / Info frame, the
        /// encoder delay and the padding).</summary>
        public static bool Mp3Info(string path, out int channels, out int rate, out long frames)
        {
            channels = 2; rate = 44100; frames = 0;
            try
            {
                var b = File.ReadAllBytes(path);
                int i = 0;
                if (b.Length > 10 && b[0] == 'I' && b[1] == 'D' && b[2] == '3') i = 10 + (b[6] << 21 | b[7] << 14 | b[8] << 7 | b[9]);
                bool first = true;
                while (i + 4 <= b.Length)
                {
                    if (b[i] != 0xFF || (b[i + 1] & 0xE0) != 0xE0) { i++; continue; }
                    int ver = (b[i + 1] >> 3) & 3, layer = (b[i + 1] >> 1) & 3, br = b[i + 2] >> 4, sr = (b[i + 2] >> 2) & 3, pad = (b[i + 2] >> 1) & 1;
                    if (ver == 1 || layer == 0 || br == 0 || br == 15 || sr == 3) { i++; continue; }
                    int[] rates = ver == 3 ? new[] { 44100, 48000, 32000 } : ver == 2 ? new[] { 22050, 24000, 16000 } : new[] { 11025, 12000, 8000 };
                    int fr = rates[sr]; int lay = 4 - layer;
                    int kbps = ver == 3 ? mp3Bitrates[lay - 1, br] : mp3Bitrates2[lay - 1, br];
                    int spf = lay == 1 ? 384 : lay == 3 && ver != 3 ? 576 : 1152;
                    int size = lay == 1 ? (12 * kbps * 1000 / fr + pad) * 4 : spf / 8 * kbps * 1000 / fr + pad;
                    if (size <= 4) { i++; continue; }
                    if (first) { rate = fr; channels = (b[i + 3] >> 6) == 3 ? 1 : 2; first = false; }
                    frames += spf; i += size;
                }
                if (frames > 0 && Mp3Gapless(b, out int delay, out int padding))
                {
                    frames -= SamplesPerFrame(b);   // the Info frame itself
                    if (delay >= 0) frames = Math.Max(0, frames - delay - padding);
                }
                return frames > 0;
            }
            catch { return false; }
        }
    }
}
