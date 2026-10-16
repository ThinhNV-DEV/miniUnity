// AudioMixer effects. A mixer whose groups use effects other than the group attenuation (Lowpass / Highpass (Simple), Echo,
// SFX Reverb, Compressor, Duck Volume, Send / Receive) is mixed in blocks: every frame the voices routed to it are rendered
// (source gains and panning, without the group volumes) into the buses of their groups; each group then runs its effect chain
// in order with the parameter values of that moment (snapshot / transition / exposed override, AudioMixer.ParamValue) —
// Attenuation applies the group volume and mute at its position — and feeds its parent; the master's output is the mixer's
// share of the live mix that MixDown adds. Sends deliver to their target (Receive adds the signal, Duck Volume uses it as
// side chain) in the same block when the target group comes later, one block later otherwise.
// Mixers with only attenuation keep the offline path (AudioSystem.MixDown), so their output is unchanged bit for bit.
// DSP: RBJ biquads (as the filter components), feedback delay for Echo, Freeverb (8 combs + 4 all-passes, public domain
// algorithm) for SFX Reverb, peak envelope followers for Compressor (ratio 2.5, FMOD's default) and Duck Volume.
using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity.Yaml;
using UnityEngine;
using UnityEngine.Audio;

namespace MiniUnity.Audio
{
    public sealed class MixerBus
    {
        // ------------------------------------------------------------------ live mix shared by every bus
        public static readonly List<float> LiveL = new(), LiveR = new();
        static readonly Dictionary<AudioMixer, MixerBus> s_Buses = new();
        static long s_BlockA, s_BlockB;

        public static void Reset() { s_Buses.Clear(); LiveL.Clear(); LiveR.Clear(); s_BlockA = s_BlockB = 0; }

        /// <summary>The bus of the mixer a group belongs to, or null when that mixer has no effects to simulate.</summary>
        public static MixerBus For(AudioMixerGroup group)
        {
            var mixer = group?.Mixer;
            if (mixer == null) return null;
            if (!s_Buses.TryGetValue(mixer, out var bus))
            {
                bus = Build(mixer);
                s_Buses[mixer] = bus;
                if (bus != null) { bus.blockA = s_BlockA; bus.Resize((int)(s_BlockB - s_BlockA)); }
            }
            return bus;
        }

        /// <summary>Start of a frame: the block [prev, now) of output samples every bus mixes.</summary>
        public static void BeginFrame(double prevDsp, double nowDsp, int rate)
        {
            s_BlockA = (long)(prevDsp * rate); s_BlockB = (long)(nowDsp * rate);
            foreach (var b in s_Buses.Values) if (b != null) { b.blockA = s_BlockA; b.rate = rate; b.Resize((int)(s_BlockB - s_BlockA)); }
        }

        /// <summary>End of a frame: run every group's chain and add the masters to the live mix.</summary>
        public static void EndFrame()
        {
            int n = (int)(s_BlockB - s_BlockA);
            if (n <= 0) return;
            bool any = false;
            foreach (var b in s_Buses.Values) if (b != null) { any = true; break; }
            if (!any) return;
            while (LiveL.Count < s_BlockB) { LiveL.Add(0); LiveR.Add(0); }
            foreach (var b in s_Buses.Values)
            {
                if (b == null) continue;
                var (l, r) = b.Run(n);
                float outGain = b.mixer.outputAudioMixerGroup is { } og && og.Mixer != b.mixer ? og.Gain() : 1;
                for (int i = 0; i < n; i++) { LiveL[(int)(s_BlockA + i)] += l[i] * outGain; LiveR[(int)(s_BlockA + i)] += r[i] * outGain; }
            }
        }

        /// <summary>Add a voice's stereo samples (interleaved, after its source gains) starting at absolute output sample <paramref name="start"/>.</summary>
        public void Add(AudioMixerGroup group, long start, float[] stereo, int frames)
        {
            if (!groupsByObj.TryGetValue(group, out var g)) g = master;
            for (int i = 0; i < frames; i++)
            {
                long k = start + i - blockA;
                if (k < 0 || k >= g.inL.Length) continue;
                g.inL[k] += stereo[i * 2]; g.inR[k] += stereo[i * 2 + 1];
            }
        }

        // ------------------------------------------------------------------ model
        sealed class Effect
        {
            public string name; public bool bypass; public string mixLevel; public long fileId, sendTarget;
            public readonly Dictionary<string, string> param = new();   // normalized parameter name → GUID
            public float[] sideL = Array.Empty<float>(), sideR = Array.Empty<float>();   // received / side-chain input of the block
            public object dsp;
        }
        sealed class Group
        {
            public AudioMixerGroup obj; public long fileId; public bool bypassEffects;
            public readonly List<Effect> effects = new(); public readonly List<Group> children = new();
            public float[] inL = Array.Empty<float>(), inR = Array.Empty<float>();
        }

        readonly AudioMixer mixer;
        Group master;
        readonly Dictionary<AudioMixerGroup, Group> groupsByObj = new();
        readonly Dictionary<long, Effect> effectsById = new();
        long blockA; int rate = 48000;
        static readonly HashSet<string> s_Warned = new();

        MixerBus(AudioMixer m) { mixer = m; }

        static string Norm(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        static MixerBus Build(AudioMixer mixer)
        {
            var path = mixer.m_Asset?.Path;
            if (path == null) return null;
            List<YDoc> docs;
            try { docs = YamlReader.ReadFile(AssetIndex.Abs(path)); } catch { return null; }
            var byId = docs.ToDictionary(d => d.FileId, d => d);
            var bus = new MixerBus(mixer);
            bool real = false;
            foreach (var d in docs.Where(d => d.ClassId == 244))
            {
                var b = d.Body;
                var e = new Effect
                {
                    fileId = d.FileId, name = (b["m_EffectName"] as YScalar)?.Value ?? "", bypass = (b["m_Bypass"] as YScalar)?.I == 1,
                    mixLevel = (b["m_MixLevel"] as YScalar)?.Value, sendTarget = (b["m_SendTarget"] as YRef)?.FileID ?? 0,
                };
                if (b["m_Parameters"] is YSeq ps)
                    foreach (var p in ps.Items.OfType<YMap>())
                        if (p["m_ParameterName"] is YScalar pn && p["m_GUID"] is YScalar pg) e.param[Norm(pn.Value)] = pg.Value;
                bus.effectsById[e.fileId] = e;
                if (e.name != "Attenuation") real = true;
            }
            if (!real) return null;
            var groups = mixer.AllGroups().ToList();
            Group Make(long id)
            {
                if (!byId.TryGetValue(id, out var d) || d.ClassId != 243) return null;
                var obj = groups.FirstOrDefault(x => x.m_Asset?.FileID == id);
                var g = new Group { obj = obj, fileId = id, bypassEffects = (d.Body["m_BypassEffects"] as YScalar)?.I == 1 };
                if (d.Body["m_Effects"] is YSeq es)
                    foreach (var r in es.Items.OfType<YRef>()) if (bus.effectsById.TryGetValue(r.FileID, out var e)) g.effects.Add(e);
                if (d.Body["m_Children"] is YSeq cs)
                    foreach (var r in cs.Items.OfType<YRef>()) if (Make(r.FileID) is Group c) g.children.Add(c);
                if (obj != null) bus.groupsByObj[obj] = g;
                return g;
            }
            var masterId = mixer.Master?.m_Asset?.FileID ?? 0;
            bus.master = Make(masterId);
            return bus.master == null ? null : bus;
        }

        void Resize(int n)
        {
            n = Math.Max(0, n);
            void R(Group g)
            {
                if (g.inL.Length != n) { g.inL = new float[n]; g.inR = new float[n]; } else { Array.Clear(g.inL); Array.Clear(g.inR); }
                foreach (var c in g.children) R(c);
            }
            if (master != null) R(master);
        }

        // ------------------------------------------------------------------ processing
        float P(Effect e, string name, float def) => e.param.TryGetValue(Norm(name), out var g) ? mixer.ParamValue(g, def) : def;
        static float Db(float db) => db <= -80 ? 0 : MathF.Pow(10, db / 20);

        (float[] l, float[] r) Run(int n)
        {
            bool solo = mixer.AnySolo();
            return Process(master, n, solo);
        }

        static bool SoloRelevant(AudioMixerGroup g)
        {
            if (g == null) return true;
            for (var p = g; p != null; p = p.Parent) if (p.NBool("m_Solo")) return true;
            bool Down(AudioMixerGroup x) => x.NBool("m_Solo") || (x.Mixer?.AllGroups().Where(c => c.Parent == x).Any(Down) ?? false);
            return Down(g);
        }

        (float[] l, float[] r) Process(Group g, int n, bool solo)
        {
            var L = new float[n]; var R = new float[n];
            Array.Copy(g.inL, L, Math.Min(n, g.inL.Length)); Array.Copy(g.inR, R, Math.Min(n, g.inR.Length));
            foreach (var c in g.children)
            {
                var (cl, cr) = Process(c, n, solo);
                for (int i = 0; i < n; i++) { L[i] += cl[i]; R[i] += cr[i]; }
            }
            if (solo && !SoloRelevant(g.obj)) { Array.Clear(L); Array.Clear(R); }
            bool attenuated = false;
            foreach (var e in g.effects)
            {
                if (e.name == "Attenuation") { Attenuate(g, L, R); attenuated = true; continue; }
                bool off = e.bypass || g.bypassEffects;
                switch (e.name)
                {
                    case "Send":
                        if (off) break;
                        if (effectsById.TryGetValue(e.sendTarget, out var target))
                        {
                            float lvl = Db(e.mixLevel != null ? mixer.ParamValue(e.mixLevel, -80) : -80);
                            if (target.sideL.Length != n) { var nl = new float[n]; var nr = new float[n]; Array.Copy(target.sideL, nl, Math.Min(n, target.sideL.Length)); Array.Copy(target.sideR, nr, Math.Min(n, target.sideR.Length)); target.sideL = nl; target.sideR = nr; }
                            for (int i = 0; i < n; i++) { target.sideL[i] += L[i] * lvl; target.sideR[i] += R[i] * lvl; }
                        }
                        break;
                    case "Receive":
                        if (!off) for (int i = 0; i < Math.Min(n, e.sideL.Length); i++) { L[i] += e.sideL[i]; R[i] += e.sideR[i]; }
                        ClearSide(e);
                        break;
                    case "Duck Volume":
                        if (!off) Duck(e, L, R, n);
                        ClearSide(e);
                        break;
                    default:
                        if (!off) Apply(e, L, R, n);
                        break;
                }
            }
            if (!attenuated) Attenuate(g, L, R);
            return (L, R);
        }

        static void ClearSide(Effect e) { Array.Clear(e.sideL); Array.Clear(e.sideR); }

        void Attenuate(Group g, float[] L, float[] R)
        {
            if (g.obj == null) return;
            float gain = g.obj.NBool("m_Mute") ? 0 : Db(mixer.ParamValue(g.obj.VolumeGuid, 0));
            if (gain == 1) return;
            for (int i = 0; i < L.Length; i++) { L[i] *= gain; R[i] *= gain; }
        }

        void Apply(Effect e, float[] L, float[] R, int n)
        {
            switch (e.name)
            {
                case "Lowpass": case "Lowpass Simple": case "Highpass": case "Highpass Simple":
                {
                    var bq = e.dsp as Biquad2 ?? (Biquad2)(e.dsp = new Biquad2());
                    bool high = e.name.StartsWith("Highpass");
                    bq.Run(L, R, n, P(e, "Cutoff freq", 5000), e.name.EndsWith("Simple") ? 1 : P(e, "Resonance", 1), high, rate);
                    break;
                }
                case "Echo":
                {
                    var ec = e.dsp as Delay ?? (Delay)(e.dsp = new Delay());
                    ec.Run(L, R, n, P(e, "Delay", 500) / 1000f, P(e, "Decay", 50) / 100f, P(e, "Drymix", 100) / 100f, P(e, "Wetmix", 100) / 100f, rate);
                    break;
                }
                case "SFX Reverb":
                {
                    var fv = e.dsp as Freeverb ?? (Freeverb)(e.dsp = new Freeverb(rate));
                    float dry = MathF.Pow(10, P(e, "Dry Level", 0) / 2000f);
                    float wet = MathF.Pow(10, (P(e, "Room", -10000) + P(e, "Reverb", 0)) / 2000f);
                    fv.Run(L, R, n, P(e, "Decay Time", 1), P(e, "Decay HF Ratio", .5f), dry, wet);
                    break;
                }
                case "Compressor":
                {
                    var c = e.dsp as Envelope ?? (Envelope)(e.dsp = new Envelope());
                    c.Compress(L, R, n, P(e, "Threshold", 0), 2.5f, P(e, "Attack", 50), P(e, "Release", 50), P(e, "Make up gain", 0), rate);
                    break;
                }
                default:
                    if (s_Warned.Add(e.name)) Debug.LogWarning($"[AudioMixer] effect '{e.name}' is not simulated (passes the signal through)");
                    break;
            }
        }

        void Duck(Effect e, float[] L, float[] R, int n)
        {
            var env = e.dsp as Envelope ?? (Envelope)(e.dsp = new Envelope());
            var side = new float[n];
            for (int i = 0; i < Math.Min(n, e.sideL.Length); i++) side[i] = Math.Max(Math.Abs(e.sideL[i]), Math.Abs(e.sideR[i])) * P(e, "Sidechain Mix", 100) / 100f;
            env.Duck(L, R, side, n, P(e, "Threshold", -10), Math.Max(1, P(e, "Ratio", 200) / 100f), P(e, "Attack Time", 100), P(e, "Release Time", 100), P(e, "Make-up Gain", 0), rate);
        }

        // ------------------------------------------------------------------ DSP
        sealed class Biquad2
        {
            double z1L, z2L, z1R, z2R;
            public void Run(float[] L, float[] R, int n, float cutoff, float q, bool high, int rate)
            {
                double w = 2 * Math.PI * Math.Clamp(cutoff, 10, rate * 0.49) / rate, cw = Math.Cos(w), alpha = Math.Sin(w) / (2 * Math.Max(0.1, q));
                double b0 = high ? (1 + cw) / 2 : (1 - cw) / 2, b1 = high ? -(1 + cw) : 1 - cw, b2 = b0, a0 = 1 + alpha, a1 = -2 * cw, a2 = 1 - alpha;
                b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;
                for (int i = 0; i < n; i++)
                {
                    double x = L[i], y = b0 * x + z1L; z1L = b1 * x - a1 * y + z2L; z2L = b2 * x - a2 * y; L[i] = (float)y;
                    x = R[i]; y = b0 * x + z1R; z1R = b1 * x - a1 * y + z2R; z2R = b2 * x - a2 * y; R[i] = (float)y;
                }
            }
        }

        sealed class Delay
        {
            float[] l = Array.Empty<float>(), r = Array.Empty<float>(); int pos;
            public void Run(float[] L, float[] R, int n, float seconds, float decay, float dry, float wet, int rate)
            {
                int frames = Math.Max(1, (int)(seconds * rate));
                if (frames != l.Length) { l = new float[frames]; r = new float[frames]; pos = 0; }
                for (int i = 0; i < n; i++)
                {
                    float dl = l[pos], dr = r[pos];
                    l[pos] = L[i] + dl * decay; r[pos] = R[i] + dr * decay;
                    L[i] = L[i] * dry + dl * wet; R[i] = R[i] * dry + dr * wet;
                    if (++pos >= frames) pos = 0;
                }
            }
        }

        /// <summary>Freeverb (Jezar at Dreampoint, public domain): 8 parallel lowpass-feedback combs into 4 series all-passes per channel.</summary>
        sealed class Freeverb
        {
            static readonly int[] Combs = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 }, Alls = { 556, 441, 341, 225 };
            const int Spread = 23;
            readonly float[][] cl, cr, al, ar; readonly int[] cpl, cpr, apl, apr; readonly float[] fl, fr, fbl = new float[8], fbr = new float[8];
            readonly float scale;
            public Freeverb(int rate)
            {
                scale = rate / 44100f;
                int S(int v) => Math.Max(1, (int)(v * scale));
                cl = Combs.Select(c => new float[S(c)]).ToArray(); cr = Combs.Select(c => new float[S(c + Spread)]).ToArray();
                al = Alls.Select(c => new float[S(c)]).ToArray(); ar = Alls.Select(c => new float[S(c + Spread)]).ToArray();
                cpl = new int[8]; cpr = new int[8]; apl = new int[4]; apr = new int[4]; fl = new float[8]; fr = new float[8];
            }
            public void Run(float[] L, float[] R, int n, float decayTime, float hfRatio, float dry, float wet)
            {
                float damp = Math.Clamp(1 - hfRatio, 0, 0.95f);
                const float gain = 0.015f;
                // feedback of each comb for a 60 dB decay over decayTime seconds at its length
                for (int k = 0; k < 8; k++)
                {
                    fbl[k] = MathF.Pow(10, -3f * cl[k].Length / (44100f * scale * Math.Max(0.1f, decayTime)));
                    fbr[k] = MathF.Pow(10, -3f * cr[k].Length / (44100f * scale * Math.Max(0.1f, decayTime)));
                }
                for (int i = 0; i < n; i++)
                {
                    float input = (L[i] + R[i]) * gain, ol = 0, or = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        ol += Comb(cl[k], ref cpl[k], ref fl[k], input, fbl[k], damp);
                        or += Comb(cr[k], ref cpr[k], ref fr[k], input, fbr[k], damp);
                    }
                    for (int k = 0; k < 4; k++) { ol = All(al[k], ref apl[k], ol); or = All(ar[k], ref apr[k], or); }
                    L[i] = L[i] * dry + ol * wet; R[i] = R[i] * dry + or * wet;
                }
            }
            static float Comb(float[] buf, ref int p, ref float filt, float x, float fb, float damp)
            {
                float y = buf[p];
                filt = y * (1 - damp) + filt * damp;
                buf[p] = x + filt * fb;
                if (++p >= buf.Length) p = 0;
                return y;
            }
            static float All(float[] buf, ref int p, float x)
            {
                float b = buf[p], y = -x + b;
                buf[p] = x + b * 0.5f;
                if (++p >= buf.Length) p = 0;
                return y;
            }
        }

        sealed class Envelope
        {
            float env;   // dB above silence of the detected level
            float Coef(float ms, int rate) => ms <= 0 ? 0 : MathF.Exp(-1f / (ms / 1000f * rate));
            float Follow(float level, float attack, float release)
            {
                float db = level > 1e-6f ? 20 * MathF.Log10(level) : -120;
                env = db > env ? attack * env + (1 - attack) * db : release * env + (1 - release) * db;
                return env;
            }
            public void Compress(float[] L, float[] R, int n, float threshold, float ratio, float attackMs, float releaseMs, float makeup, int rate)
            {
                float a = Coef(attackMs, rate), r = Coef(releaseMs, rate), mk = MathF.Pow(10, makeup / 20);
                for (int i = 0; i < n; i++)
                {
                    float e = Follow(Math.Max(Math.Abs(L[i]), Math.Abs(R[i])), a, r);
                    float over = e - threshold, g = over > 0 ? MathF.Pow(10, -over * (1 - 1 / ratio) / 20) : 1;
                    L[i] *= g * mk; R[i] *= g * mk;
                }
            }
            public void Duck(float[] L, float[] R, float[] side, int n, float threshold, float ratio, float attackMs, float releaseMs, float makeup, int rate)
            {
                float a = Coef(attackMs, rate), r = Coef(releaseMs, rate), mk = MathF.Pow(10, makeup / 20);
                for (int i = 0; i < n; i++)
                {
                    float e = Follow(side[i], a, r);
                    float over = e - threshold, g = over > 0 ? MathF.Pow(10, -over * (1 - 1 / ratio) / 20) : 1;
                    L[i] *= g * mk; R[i] *= g * mk;
                }
            }
        }
    }
}
