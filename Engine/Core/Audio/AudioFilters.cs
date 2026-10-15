// Audio effect components and script filters for MiniUnity: AudioLowPassFilter / AudioHighPassFilter (resonant biquads),
// AudioEchoFilter (feedback delay line), AudioDistortionFilter and MonoBehaviour.OnAudioFilterRead(float[] data, int channels),
// applied in component order. On an AudioSource's GameObject they process that source (before volume, mixer and 3D gains, like
// Unity's per-source DSP chain): such voices are rendered frame by frame while playing, so OnAudioFilterRead runs during Play
// mode, and an echo keeps ringing after the clip ends. On the AudioListener's GameObject they process the final mixdown.
// Reverb and chorus filters pass audio through unchanged.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UnityEngine
{
    public sealed partial class AudioLowPassFilter
    {
        AnimationCurve m_CustomCurve;
        /// <summary>The serialized lowpassLevelCustomCurve (cutoff / 22 kHz over distance) wins over m_CutoffFrequency, as in Unity.</summary>
        public float cutoffFrequency
        {
            get
            {
                if (NGet("lowpassLevelCustomCurve") is MiniUnity.Yaml.YMap m && MiniUnity.Serializer.ReadCurve(m) is AnimationCurve c && c.length > 0) return c.Evaluate(0) * 22000;
                return NFloat("m_CutoffFrequency", 5000);
            }
            set { NSet("lowpassLevelCustomCurve", null); NSetF("m_CutoffFrequency", Mathf.Clamp(value, 10, 22000)); }
        }
        public float lowpassResonanceQ { get => NFloat("m_LowpassResonanceQ", 1); set => NSetF("m_LowpassResonanceQ", Mathf.Clamp(value, 1, 10)); }
        public AnimationCurve customCutoffCurve { get => m_CustomCurve ?? AnimationCurve.Constant(0, 1, cutoffFrequency / 22000); set => m_CustomCurve = value; }
    }

    public sealed partial class AudioHighPassFilter
    {
        public float cutoffFrequency { get => NFloat("m_CutoffFrequency", 5000); set => NSetF("m_CutoffFrequency", Mathf.Clamp(value, 10, 22000)); }
        public float highpassResonanceQ { get => NFloat("m_HighpassResonanceQ", 1); set => NSetF("m_HighpassResonanceQ", Mathf.Clamp(value, 1, 10)); }
    }

    public sealed partial class AudioEchoFilter
    {
        public float delay { get => NFloat("m_Delay", 500); set => NSetF("m_Delay", Mathf.Clamp(value, 10, 5000)); }
        public float decayRatio { get => NFloat("m_DecayRatio", 0.5f); set => NSetF("m_DecayRatio", Mathf.Clamp01(value)); }
        public float dryMix { get => NFloat("m_DryMix", 1); set => NSetF("m_DryMix", Mathf.Clamp01(value)); }
        public float wetMix { get => NFloat("m_WetMix", 1); set => NSetF("m_WetMix", Mathf.Clamp01(value)); }
    }

    public sealed partial class AudioDistortionFilter
    {
        public float distortionLevel { get => NFloat("m_DistortionLevel", 0.5f); set => NSetF("m_DistortionLevel", Mathf.Clamp01(value)); }
    }
}

namespace MiniUnity.Audio
{
    /// <summary>One stage of a DSP chain with its running state (biquad memory, delay line).</summary>
    public abstract class FilterStage
    {
        public Component Owner;
        public abstract void Process(float[] data, int channels, int rate);
        /// <summary>Seconds the stage keeps producing sound after its input goes silent.</summary>
        public virtual double Tail => 0;
    }

    sealed class Biquad : FilterStage
    {
        readonly bool high;
        double z1L, z2L, z1R, z2R;   // transposed direct form II, per channel (more channels share the right state)
        public Biquad(bool high) { this.high = high; }
        public override void Process(float[] data, int channels, int rate)
        {
            float cutoff, q;
            if (Owner is AudioLowPassFilter lp) { cutoff = lp.cutoffFrequency; q = lp.lowpassResonanceQ; }
            else { var hp = (AudioHighPassFilter)Owner; cutoff = hp.cutoffFrequency; q = hp.highpassResonanceQ; }
            // RBJ cookbook; Unity's Q of 1 is its least resonant setting
            double w = 2 * Math.PI * Math.Min(cutoff, rate * 0.49) / rate, cw = Math.Cos(w), alpha = Math.Sin(w) / (2 * Math.Max(0.1, q));
            double b0 = high ? (1 + cw) / 2 : (1 - cw) / 2, b1 = high ? -(1 + cw) : 1 - cw, b2 = b0, a0 = 1 + alpha, a1 = -2 * cw, a2 = 1 - alpha;
            b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;
            for (int i = 0; i < data.Length; i += channels)
                for (int c = 0; c < channels; c++)
                {
                    ref double z1 = ref c == 0 ? ref z1L : ref z1R; ref double z2 = ref c == 0 ? ref z2L : ref z2R;
                    double x = data[i + c], y = b0 * x + z1;
                    z1 = b1 * x - a1 * y + z2; z2 = b2 * x - a2 * y;
                    data[i + c] = (float)y;
                }
        }
    }

    sealed class Echo : FilterStage
    {
        float[] line = Array.Empty<float>(); int pos, lineFrames, lineChannels;
        public override double Tail
        {
            get { var e = (AudioEchoFilter)Owner; float d = e.decayRatio; return d <= 0 ? e.delay / 1000.0 : Math.Min(10, e.delay / 1000.0 * (1 + Math.Log(1e-3) / Math.Log(Math.Min(d, 0.999)))); }
        }
        public override void Process(float[] data, int channels, int rate)
        {
            var e = (AudioEchoFilter)Owner;
            int frames = Math.Max(1, (int)(e.delay / 1000.0 * rate));
            if (frames != lineFrames || channels != lineChannels) { line = new float[frames * channels]; lineFrames = frames; lineChannels = channels; pos = 0; }
            float decay = e.decayRatio, dry = e.dryMix, wet = e.wetMix;
            for (int i = 0; i < data.Length; i += channels)
            {
                for (int c = 0; c < channels; c++)
                {
                    int k = pos * channels + c;
                    float x = data[i + c], d = line[k];
                    line[k] = x + d * decay;
                    data[i + c] = x * dry + d * wet;
                }
                if (++pos >= lineFrames) pos = 0;
            }
        }
    }

    sealed class Distortion : FilterStage
    {
        public override void Process(float[] data, int channels, int rate)
        {
            float level = Math.Min(0.999f, ((AudioDistortionFilter)Owner).distortionLevel), k = 2 * level / (1 - level);
            for (int i = 0; i < data.Length; i++) { float x = data[i]; data[i] = (1 + k) * x / (1 + k * Math.Abs(x)); }
        }
    }

    sealed class ScriptFilter : FilterStage
    {
        public MethodInfo Method;
        public override void Process(float[] data, int channels, int rate)
        {
            try { Method.Invoke(Owner, new object[] { data, channels }); }
            catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
        }
    }

    /// <summary>The DSP chain of a GameObject: its enabled filter components and OnAudioFilterRead scripts, in component order,
    /// with each stage's state kept for as long as the component stays in the chain.</summary>
    public sealed class FilterChain
    {
        readonly Dictionary<Component, FilterStage> stages = new();
        public readonly List<FilterStage> Active = new();
        static readonly Dictionary<Type, MethodInfo> scriptMethods = new();

        public static MethodInfo ScriptMethod(Type t)
        {
            lock (scriptMethods)
            {
                if (scriptMethods.TryGetValue(t, out var m)) return m;
                for (var c = t; c != null && c != typeof(MonoBehaviour) && m == null; c = c.BaseType)
                    m = c.GetMethod("OnAudioFilterRead", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, new[] { typeof(float[]), typeof(int) }, null);
                return scriptMethods[t] = m;
            }
        }

        public static bool HasFilters(GameObject go)
        {
            if (go == null) return false;
            foreach (var c in go.m_Components)
                if (c is AudioLowPassFilter or AudioHighPassFilter or AudioEchoFilter or AudioDistortionFilter || c is MonoBehaviour mb && ScriptMethod(mb.GetType()) != null) return true;
            return false;
        }

        /// <summary>Refresh the active stages from the GameObject's components; false when nothing is in the chain.</summary>
        public bool Update(GameObject go)
        {
            Active.Clear();
            if (go == null || go.m_Destroyed || !go.activeInHierarchy) return false;
            foreach (var c in go.m_Components)
            {
                if (c.m_Destroyed || c is Behaviour b && !b.enabled) continue;
                if (!stages.TryGetValue(c, out var st))
                {
                    st = c switch
                    {
                        AudioLowPassFilter => new Biquad(false), AudioHighPassFilter => new Biquad(true), AudioEchoFilter => new Echo(), AudioDistortionFilter => new Distortion(),
                        MonoBehaviour mb when ScriptMethod(mb.GetType()) is MethodInfo m => new ScriptFilter { Method = m },
                        _ => null,
                    };
                    if (st != null) st.Owner = c;
                    stages[c] = st;
                }
                if (st != null) Active.Add(st);
            }
            return Active.Count > 0;
        }

        public void Process(float[] data, int channels, int rate) { foreach (var s in Active) s.Process(data, channels, rate); }
        public double Tail { get { double t = 0; foreach (var s in Active) t = Math.Max(t, s.Tail); return t; } }
        public bool HasScript { get { foreach (var s in Active) if (s is ScriptFilter) return true; return false; } }
    }
}
