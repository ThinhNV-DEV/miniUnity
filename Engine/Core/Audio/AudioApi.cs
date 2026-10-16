// UnityEngine audio API (AudioModule) over MiniUnity.Audio.AudioSystem: AudioClip (data, Create, Get/SetData), AudioSource
// (playback timeline, scheduling, one-shots, 3D settings and rolloff), AudioListener, AudioSettings, AudioMixer parameters.
using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity;
using MiniUnity.Audio;
using MiniUnity.Yaml;

namespace UnityEngine
{
    public partial class AudioClip
    {
        internal float m_Length; internal int m_Samples, m_Frequency = 44100, m_Channels = 1;
        internal string m_SourceFile; internal float[] m_Data; internal bool m_Decoded, m_Stream;
        internal PCMReaderCallback m_Reader; internal PCMSetPositionCallback m_SetPos;
        internal AudioClip() { }

        internal float[] Data()
        {
            if (m_Data != null || m_Decoded) return m_Data;
            m_Decoded = true;
            if (m_SourceFile != null && m_SourceFile.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) m_Data = AudioDecode.DecodeWav(m_SourceFile);
            else if (m_SourceFile != null && (m_SourceFile.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) || m_SourceFile.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)))
                m_Data = AudioDecode.DecodeCompressed(m_SourceFile, m_Channels, m_Frequency, m_Samples);
            return m_Data;
        }

        public float length => m_Length;
        public int samples => m_Samples;
        public int frequency => m_Frequency;
        public int channels => m_Channels;
        public AudioClipLoadType loadType => m_Stream ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
        public bool preloadAudioData => true;
        public bool ambisonic => false;
        public bool loadInBackground => false;
        public AudioDataLoadState loadState => AudioDataLoadState.Loaded;
        public bool isReadyToPlay => true;
        public bool LoadAudioData() => true;
        public bool UnloadAudioData() => true;

        public bool GetData(float[] data, int offsetSamples)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (m_Channels <= 0) { Debug.LogError("AudioClip.GetData failed; AudioClip " + name + " contains no data"); return false; }
            var src = Data();
            if (src == null) { Array.Clear(data, 0, data.Length); return m_SourceFile == null || !m_SourceFile.EndsWith(".wav"); }
            int start = offsetSamples * m_Channels;
            for (int i = 0; i < data.Length; i++)
            {
                int k = start + i;
                data[i] = src.Length == 0 ? 0 : src[((k % src.Length) + src.Length) % src.Length];   // wraps like Unity's looping read
            }
            return true;
        }

        public bool SetData(float[] data, int offsetSamples)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offsetSamples < 0 || offsetSamples >= Math.Max(1, m_Samples)) throw new ArgumentException("AudioClip.SetData failed; invalid offsetSamples");
            var dst = Data() ?? (m_Data = new float[m_Samples * m_Channels]);
            int start = offsetSamples * m_Channels;
            for (int i = 0; i < data.Length; i++) dst[(start + i) % dst.Length] = data[i];
            return true;
        }

        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream) => Create(name, lengthSamples, channels, frequency, stream, null, null);
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream, PCMReaderCallback pcmreadercallback) => Create(name, lengthSamples, channels, frequency, stream, pcmreadercallback, null);
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool _3D, bool stream) => Create(name, lengthSamples, channels, frequency, stream);
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool _3D, bool stream, PCMReaderCallback pcmreadercallback) => Create(name, lengthSamples, channels, frequency, stream, pcmreadercallback, null);
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool _3D, bool stream, PCMReaderCallback pcmreadercallback, PCMSetPositionCallback pcmsetpositioncallback) => Create(name, lengthSamples, channels, frequency, stream, pcmreadercallback, pcmsetpositioncallback);
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream, PCMReaderCallback pcmreadercallback, PCMSetPositionCallback pcmsetpositioncallback)
        {
            if (name == null) throw new NullReferenceException();
            if (lengthSamples <= 0) throw new ArgumentException("Length of created clip must be larger than 0");
            if (channels <= 0) throw new ArgumentException("Number of channels in created clip must be greater than 0");
            if (frequency <= 0) throw new ArgumentException("Frequency in created clip must be greater than 0");
            var c = new AudioClip { m_Samples = lengthSamples, m_Channels = channels, m_Frequency = frequency, m_Length = lengthSamples / (float)frequency, m_Stream = stream, m_Reader = pcmreadercallback, m_SetPos = pcmsetpositioncallback };
            c.m_Name = name; c.m_Decoded = true;
            c.m_Data = new float[lengthSamples * channels];
            if (pcmreadercallback != null)
            {
                // non-streamed clips are filled once through the reader (Unity reads in chunks of 4096 frames)
                pcmsetpositioncallback?.Invoke(0);
                int chunk = 4096 * channels;
                for (int off = 0; off < c.m_Data.Length; off += chunk)
                {
                    var buf = new float[Math.Min(chunk, c.m_Data.Length - off)];
                    pcmreadercallback(buf);
                    Array.Copy(buf, 0, c.m_Data, off, buf.Length);
                }
            }
            return c;
        }
    }

    public partial class AudioBehaviour { public AudioBehaviour() { } }

    public partial class AudioListener
    {
        public AudioListener() { }
        public static float volume { get { AudioSystem.EnsureSettings(); return AudioSystem.ListenerVolume; } set => AudioSystem.ListenerVolume = Mathf.Clamp01(value); }
        public static bool pause { get => AudioSystem.ListenerPause; set => AudioSystem.ListenerPause = value; }
        public AudioVelocityUpdateMode velocityUpdateMode { get; set; }
        public static float[] GetOutputData(int numSamples, int channel) => AudioSystem.OutputData(numSamples, channel);
        public static void GetOutputData(float[] samples, int channel) => AudioSystem.OutputData(samples.Length, channel).CopyTo(samples, 0);
        public static float[] GetSpectrumData(int numSamples, int channel, FFTWindow window) => AudioSystem.Spectrum(AudioSystem.OutputData(numSamples, channel), window);
        public static void GetSpectrumData(float[] samples, int channel, FFTWindow window) => GetSpectrumData(samples.Length, channel, window).CopyTo(samples, 0);
    }

    public partial class AudioSettings
    {
        static AudioConfiguration s_Config;
        public AudioSettings() { }
        public static double dspTime => AudioSystem.DspTime;
        public static int outputSampleRate { get { AudioSystem.EnsureSettings(); return AudioSystem.OutputSampleRate; } set => AudioSystem.OutputSampleRate = value; }
        public static AudioSpeakerMode driverCapabilities => AudioSpeakerMode.Stereo;
        public static AudioSpeakerMode speakerMode { get; set; } = AudioSpeakerMode.Stereo;
        internal static int profilerCaptureFlags => 0;
        internal static bool editingInPlaymode { get; set; }
        internal static bool unityAudioDisabled { get; set; }
        public static AudioSpatialExperience audioSpatialExperience { get; set; }
        public static void GetDSPBufferSize(out int bufferLength, out int numBuffers) { bufferLength = 1024; numBuffers = 4; }
        public static void SetDSPBufferSize(int bufferLength, int numBuffers) { }
        public static string[] GetSpatializerPluginNames() => Array.Empty<string>();
        public static string GetSpatializerPluginName() => "";
        public static void SetSpatializerPluginName(string pluginName) { }
        internal static string GetAmbisonicDecoderPluginName() => "";
        internal static void SetAmbisonicDecoderPluginName(string name) { }
        public static AudioConfiguration GetConfiguration()
        {
            if (s_Config.sampleRate == 0) s_Config = new AudioConfiguration { speakerMode = AudioSpeakerMode.Stereo, dspBufferSize = 1024, sampleRate = outputSampleRate, numRealVoices = 32, numVirtualVoices = 512 };
            return s_Config;
        }
        public static bool Reset(AudioConfiguration config) { s_Config = config; if (config.sampleRate > 0) AudioSystem.OutputSampleRate = config.sampleRate; return true; }
        internal static void InvokeOnAudioConfigurationChanged(bool deviceWasChanged) { }
        internal static void InvokeOnAudioSystemShuttingDown() { }
        internal static void InvokeOnAudioSystemStartedUp() { }
    }

    public partial class AudioSource
    {
        double m_PendingSeek = -1;
        public AudioSource() { }

        // ---------------------------------------------------------------- serialized settings
        public bool playOnAwake { get => NBool("m_PlayOnAwake", true); set => NSetB("m_PlayOnAwake", value); }
        public float volume { get => NFloat("m_Volume", 1); set => NSetF("m_Volume", Mathf.Clamp01(value)); }
        public float pitch { get => NFloat("m_Pitch", 1); set => NSetF("m_Pitch", Mathf.Clamp(value, -3, 3)); }
        public bool loop { get => NBool("Loop"); set { NSetB("Loop", value); var v = AudioSystem.MainVoice(this); if (v != null) v.Loop = value; } }
        public bool mute { get => NBool("Mute"); set => NSetB("Mute", value); }
        public AudioClip clip { get => NRef<AudioClip>("m_audioClip"); set => NSetRef("m_audioClip", value); }
        public Audio.AudioMixerGroup outputAudioMixerGroup { get => NRef<Audio.AudioMixerGroup>("OutputAudioMixerGroup"); set => NSetRef("OutputAudioMixerGroup", value); }
        public int priority { get => NInt("Priority", 128); set => NSetI("Priority", Math.Clamp(value, 0, 256)); }
        public float panStereo { get => NFloat("Pan2D"); set => NSetF("Pan2D", Mathf.Clamp(value, -1, 1)); }
        public float spatialBlend { get => CurveValue("panLevelCustomCurve", 0); set => SetCurveValue("panLevelCustomCurve", Mathf.Clamp01(value)); }
        public float spread { get => CurveValue("spreadCustomCurve", 0); set => SetCurveValue("spreadCustomCurve", Mathf.Clamp(value, 0, 360) / 360f); }
        public float reverbZoneMix { get => CurveValue("reverbZoneMixCustomCurve", 1); set => SetCurveValue("reverbZoneMixCustomCurve", Mathf.Clamp(value, 0, 1.1f)); }
        public float dopplerLevel { get => NFloat("DopplerLevel", 1); set => NSetF("DopplerLevel", Mathf.Clamp(value, 0, 5)); }
        public float minDistance { get => NFloat("MinDistance", 1); set => NSetF("MinDistance", Math.Max(0, value)); }
        public float maxDistance { get => NFloat("MaxDistance", 500); set => NSetF("MaxDistance", Math.Max(minDistance + 0.01f, value)); }
        public AudioRolloffMode rolloffMode { get => (AudioRolloffMode)NInt("rolloffMode"); set => NSetI("rolloffMode", (int)value); }
        public bool spatialize { get => NBool("Spatialize"); set => NSetB("Spatialize", value); }
        public bool spatializePostEffects { get => NBool("SpatializePostEffects"); set => NSetB("SpatializePostEffects", value); }
        public bool bypassEffects { get => NBool("BypassEffects"); set => NSetB("BypassEffects", value); }
        public bool bypassListenerEffects { get => NBool("BypassListenerEffects"); set => NSetB("BypassListenerEffects", value); }
        public bool bypassReverbZones { get => NBool("BypassReverbZones"); set => NSetB("BypassReverbZones", value); }
        public bool ignoreListenerVolume { get => NBool("m_IgnoreListenerVolume"); set => NSetB("m_IgnoreListenerVolume", value); }
        public bool ignoreListenerPause { get => NBool("m_IgnoreListenerPause"); set => NSetB("m_IgnoreListenerPause", value); }
        public AudioVelocityUpdateMode velocityUpdateMode { get; set; }
        public GamepadSpeakerOutputType gamepadSpeakerOutputType { get; set; }
        public bool isVirtual => false;

        float CurveValue(string key, float def)
        {
            var c = NGet(key) is YMap m ? Serializer.ReadCurve(m) : null;
            return c != null && c.length > 0 ? c[0].value : def;
        }
        void SetCurveValue(string key, float v) => NSet(key, Serializer.CurveNode(new AnimationCurve(new Keyframe(0, v))));
        public void SetCustomCurve(AudioSourceCurveType type, AnimationCurve curve) => NSet(CurveKey(type), Serializer.CurveNode(curve));
        public AnimationCurve GetCustomCurve(AudioSourceCurveType type) => NGet(CurveKey(type)) is YMap m ? Serializer.ReadCurve(m) : new AnimationCurve(new Keyframe(0, type == AudioSourceCurveType.ReverbZoneMix ? 1 : 0));
        static string CurveKey(AudioSourceCurveType t) => t switch
        {
            AudioSourceCurveType.CustomRolloff => "rolloffCustomCurve", AudioSourceCurveType.SpatialBlend => "panLevelCustomCurve",
            AudioSourceCurveType.Spread => "spreadCustomCurve", _ => "reverbZoneMixCustomCurve",
        };

        /// <summary>Distance attenuation (Logarithmic = inverse rolloff clamped at maxDistance, Linear, or the custom curve).</summary>
        internal float Attenuation(float dist)
        {
            float min = Math.Max(1e-4f, minDistance), max = Math.Max(min, maxDistance);
            switch (rolloffMode)
            {
                case AudioRolloffMode.Linear: return Mathf.Clamp01(1 - (dist - min) / Math.Max(1e-4f, max - min));
                case AudioRolloffMode.Custom: return Mathf.Clamp01(GetCustomCurve(AudioSourceCurveType.CustomRolloff).Evaluate(dist / max));
                default: return dist <= min ? 1 : min / Math.Min(dist, max);
            }
        }

        // ---------------------------------------------------------------- playback
        Voice Main => AudioSystem.MainVoice(this);
        public bool isPlaying { get { var v = Main; return v != null && !v.Paused && !v.Finished; } }
        public float time
        {
            get { var v = Main; return v != null ? (float)v.ClipPos : m_PendingSeek >= 0 ? (float)m_PendingSeek : 0; }
            set
            {
                var c = clip; float len = c != null ? c.length : 0;
                if (value < 0 || len > 0 && value > len) { Debug.LogWarning($"Attempting to set AudioSource.time to {value} which is outside the clip range"); value = Mathf.Clamp(value, 0, len); }
                var v = Main; if (v != null) v.ClipPos = value; else m_PendingSeek = value;
            }
        }
        public int timeSamples
        {
            get { var c = clip; return c == null ? 0 : (int)Math.Round(time * c.frequency); }
            set { var c = clip; if (c != null) time = value / (float)c.frequency; }
        }

        void Start(double startDsp)
        {
            var c = clip;
            if (!enabled || !gameObject.activeInHierarchy) { Debug.LogWarning("Can not play a disabled audio source"); return; }
            if (c == null)
            {
                // without a clip only an OnAudioFilterRead script on the GameObject makes sound (procedural audio)
                if (!FilterChain.HasFilters(gameObject)) return;
                AudioSystem.StopVoices(this, false, "restart");
                AudioSystem.Voices.Add(new Voice { Source = this, StartDsp = startDsp, Loop = true, LastDsp = AudioSystem.DspTime });
                AudioSystem.Record("play", this, null);
                return;
            }
            AudioSystem.StopVoices(this, false, "restart");
            double offset = m_PendingSeek >= 0 ? m_PendingSeek : pitch < 0 ? c.length : 0;
            m_PendingSeek = -1;
            AudioSystem.Voices.Add(new Voice { Source = this, Clip = c, StartDsp = startDsp, ClipPos = offset, Loop = loop, LastDsp = AudioSystem.DspTime });
            AudioSystem.Record(startDsp > AudioSystem.DspTime + 1e-9 ? "scheduled" : "play", this, c, (float)offset);
        }
        public void Play() => Start(AudioSystem.DspTime);
        public void Play(ulong delay) => Start(AudioSystem.DspTime + delay / 44100.0);
        public void PlayDelayed(float delay) => Start(AudioSystem.DspTime + Math.Max(0, delay));
        public void PlayScheduled(double time) => Start(Math.Max(time, AudioSystem.DspTime));
        public void SetScheduledStartTime(double time) { var v = Main; if (v != null && !v.Started) v.StartDsp = time; }
        public void SetScheduledEndTime(double time) { var v = Main; if (v != null) v.EndDsp = time; }
        public void Stop() => AudioSystem.StopVoices(this, true);
        internal void Stop(bool stopOneShots) => AudioSystem.StopVoices(this, stopOneShots);
        public void Pause() { foreach (var v in AudioSystem.Voices.Where(v => v.Source == this)) { if (!v.Paused) AudioSystem.Record("pause", this, v.Clip, (float)v.ClipPos); v.Paused = true; } }
        public void UnPause() { foreach (var v in AudioSystem.Voices.Where(v => v.Source == this)) { if (v.Paused) AudioSystem.Record("unpause", this, v.Clip, (float)v.ClipPos); v.Paused = false; } }
        public void PlayOneShot(AudioClip clip) => PlayOneShot(clip, 1);
        public void PlayOneShot(AudioClip clip, float volumeScale)
        {
            if (clip == null) { Debug.LogWarning("PlayOneShot was called with a null AudioClip."); return; }
            AudioSystem.Voices.Add(new Voice { Source = this, Clip = clip, OneShot = true, VolumeScale = volumeScale, StartDsp = AudioSystem.DspTime, LastDsp = AudioSystem.DspTime });
        }
        public static void PlayClipAtPoint(AudioClip clip, Vector3 position) => PlayClipAtPoint(clip, position, 1);
        public static void PlayClipAtPoint(AudioClip clip, Vector3 position, float volume)
        {
            var go = new GameObject("One shot audio");
            go.transform.position = position;
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.spatialBlend = 1; s.volume = volume;
            s.Play();
            Object.Destroy(go, clip.length * (Time.timeScale < 0.01f ? 0.01f : Time.timeScale));
        }

        public float[] GetOutputData(int numSamples, int channel) => AudioSystem.OutputData(numSamples, channel, this);
        public void GetOutputData(float[] samples, int channel) => AudioSystem.OutputData(samples.Length, channel, this).CopyTo(samples, 0);
        public float[] GetSpectrumData(int numSamples, int channel, FFTWindow window) => AudioSystem.Spectrum(AudioSystem.OutputData(numSamples, channel, this), window);
        public void GetSpectrumData(float[] samples, int channel, FFTWindow window) => GetSpectrumData(samples.Length, channel, window).CopyTo(samples, 0);
        public bool SetSpatializerFloat(int index, float value) => false;
        public bool GetSpatializerFloat(int index, out float value) { value = 0; return false; }
        public bool GetAmbisonicDecoderFloat(int index, out float value) { value = 0; return false; }
        public bool SetAmbisonicDecoderFloat(int index, float value) => false;
        public bool PlayOnGamepad(int slot) => false;
        public bool DisableGamepadOutput() => false;
        public bool SetGamepadSpeakerMixLevel(int slot, int mixLevel) => false;
        public bool SetGamepadSpeakerMixLevelDefault(int slot) => false;
        public bool SetGamepadSpeakerRestrictedAudio(int slot, bool restricted) => false;
        public static bool GamepadSpeakerSupportsOutputType(GamepadSpeakerOutputType outputType) => false;
    }
}

namespace UnityEngine.Audio
{
    /// <summary>
    /// AudioMixer asset (.mixer): groups with their volume / mute / solo, snapshots holding parameter values keyed by
    /// parameter GUID, exposed parameters (SetFloat overrides the snapshot until ClearFloat), timed snapshot transitions
    /// on the DSP clock, and the group gain an AudioSource's output goes through (product of the group chain, dB → linear).
    /// Solo silences the groups outside the soloed ones' paths. Effects inside the mixer (Lowpass / Highpass, Echo, SFX Reverb,
    /// Compressor, Duck Volume, Send / Receive) are mixed per group by Audio/MixerBus.cs.
    /// </summary>
    public partial class AudioMixer
    {
        readonly Dictionary<string, float> m_Values = new();
        internal Dictionary<string, float> m_From, m_To; internal double m_TransStart, m_TransEnd;
        internal AudioMixerSnapshot m_Current;
        internal AudioMixer() { }

        internal IEnumerable<(string guid, string name)> ExposedParams()
        {
            if (NGet("m_ExposedParameters") is YSeq s)
                foreach (var i in s.Items) if (i is YMap m && m["name"] is YScalar n) yield return ((m["guid"] as YScalar)?.Value ?? "", n.Value);
        }
        IEnumerable<string> Exposed() => ExposedParams().Select(p => p.name);
        string GuidOfExposed(string name) => ExposedParams().FirstOrDefault(p => p.name == name).guid;

        internal IEnumerable<AudioMixerGroup> AllGroups()
        {
            var la = m_Asset?.Path != null ? AssetLoader.Peek(m_Asset.Path) : null;
            return la != null ? la.Objects.Values.OfType<AudioMixerGroup>() : Enumerable.Empty<AudioMixerGroup>();
        }
        internal IEnumerable<AudioMixerSnapshot> AllSnapshots()
        {
            var la = m_Asset?.Path != null ? AssetLoader.Peek(m_Asset.Path) : null;
            return la != null ? la.Objects.Values.OfType<AudioMixerSnapshot>() : Enumerable.Empty<AudioMixerSnapshot>();
        }
        internal AudioMixerGroup Master => NRef<AudioMixerGroup>("m_MasterGroup");
        internal AudioMixerSnapshot Current => m_Current ??= NRef<AudioMixerSnapshot>("m_StartSnapshot") ?? AllSnapshots().FirstOrDefault();

        /// <summary>Current value of a parameter GUID: exposed override, else the (transitioning) snapshot value, else <paramref name="def"/>.</summary>
        internal float ParamValue(string guid, float def)
        {
            if (string.IsNullOrEmpty(guid)) return def;
            foreach (var (g, n) in ExposedParams()) if (g == guid && m_Values.TryGetValue(n, out var ov)) return ov;
            float target = Current != null ? Current.Value(guid, def) : def;
            if (m_To != null)
            {
                double now = MiniUnity.Audio.AudioSystem.DspTime;
                if (now >= m_TransEnd) { m_From = m_To = null; }
                else
                {
                    float a = m_From.TryGetValue(guid, out var fv) ? fv : def, b = m_To.TryGetValue(guid, out var tv) ? tv : def;
                    float t = (float)((now - m_TransStart) / Math.Max(1e-9, m_TransEnd - m_TransStart));
                    return a + (b - a) * Mathf.Clamp01(t);
                }
            }
            return target;
        }

        public bool SetFloat(string name, float value)
        {
            var ex = Exposed().ToList();
            if (!ex.Contains(name)) { Debug.LogWarning($"Exposed name does not exist: {name}"); return false; }
            m_Values[name] = value; return true;
        }
        public bool ClearFloat(string name) => m_Values.Remove(name);
        public bool GetFloat(string name, out float value)
        {
            if (m_Values.TryGetValue(name, out value)) return true;
            var g = GuidOfExposed(name);
            if (g == null) { value = 0; return false; }
            value = ParamValue(g, 0); return true;
        }
        public AudioMixerSnapshot FindSnapshot(string name) => AllSnapshots().FirstOrDefault(s => s.name == name);
        public AudioMixerGroup[] FindMatchingGroups(string subPath)
        {
            subPath = (subPath ?? "").Trim('/');
            return AllGroups().Where(g => subPath.Length == 0 || g.Path.Equals(subPath, StringComparison.OrdinalIgnoreCase)
                || g.Path.StartsWith(subPath + "/", StringComparison.OrdinalIgnoreCase) || g.Path.Split('/').Contains(subPath, StringComparer.OrdinalIgnoreCase)).ToArray();
        }
        internal void TransitionToSnapshot(AudioMixerSnapshot snapshot, float timeToReach) => TransitionToSnapshots(new[] { snapshot }, new[] { 1f }, timeToReach);
        /// <summary>Weighted blend of snapshots reached linearly over <paramref name="timeToReach"/> seconds of DSP time.</summary>
        public void TransitionToSnapshots(AudioMixerSnapshot[] snapshots, float[] weights, float timeToReach)
        {
            var keys = AllSnapshots().SelectMany(s => s.Values().Keys).Concat(AllGroups().Select(g => g.VolumeGuid)).Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();
            var from = keys.ToDictionary(k => k, k => ParamValue(k, 0));
            float wsum = weights.Sum();
            var to = keys.ToDictionary(k => k, k => wsum > 0 ? snapshots.Select((s, i) => s.Value(k, 0) * weights[i]).Sum() / wsum : 0);
            var best = snapshots.Select((s, i) => (s, w: weights[i])).OrderByDescending(x => x.w).First().s;
            m_Current = best;
            if (timeToReach <= 0) { m_From = m_To = null; m_Current = new AudioMixerSnapshot { m_Mixer = this, m_Blend = to, name = best.name }; return; }
            m_From = from; m_To = to; m_TransStart = MiniUnity.Audio.AudioSystem.DspTime; m_TransEnd = m_TransStart + timeToReach;
            m_Current = new AudioMixerSnapshot { m_Mixer = this, m_Blend = to, name = best.name };
        }
        internal bool AnySolo() { foreach (var g in AllGroups()) if (g.NBool("m_Solo")) return true; return false; }
        internal float GetAbsoluteAudibilityFromGroup(AudioMixerGroup group) => group != null ? group.Gain() : 1;
        public AudioMixerGroup outputAudioMixerGroup { get => NRef<AudioMixerGroup>("m_OutputGroup"); set => NSetRef("m_OutputGroup", value); }
        public AudioMixerUpdateMode updateMode { get => (AudioMixerUpdateMode)NInt("m_UpdateMode"); set => NSetI("m_UpdateMode", (int)value); }
    }

    public partial class AudioMixerGroup
    {
        internal AudioMixer Mixer => NRef<AudioMixer>("m_AudioMixer") ?? (m_Asset != null ? AssetLoader.Peek(m_Asset.Path)?.Main as AudioMixer : null);
        public AudioMixer audioMixer => Mixer;
        internal string VolumeGuid => NStr("m_Volume");
        internal AudioMixerGroup Parent => Mixer?.AllGroups().FirstOrDefault(g => g.NGet("m_Children") is YSeq s && s.Items.OfType<YRef>().Any(r => r.Target == this));
        internal string Path => Parent != null ? Parent.Path + "/" + name : name;
        /// <summary>Linear gain of this group and its parents (and the mixer's output group when it routes into another mixer).</summary>
        internal float Gain()
        {
            var mixer = Mixer;
            // solo: while any group of the mixer is soloed, only soloed groups, their sub-groups and the groups they pass through sound
            if (mixer != null && mixer.AnySolo() && !SoloPath()) return 0;
            return Chain();
        }

        bool SoloPath()
        {
            for (var g = this; g != null; g = g.Parent) if (g.NBool("m_Solo")) return true;
            return false;
        }

        float Chain()
        {
            var mixer = Mixer;
            float g = NBool("m_Mute") ? 0 : Mathf.Pow(10, (mixer?.ParamValue(VolumeGuid, 0) ?? 0) / 20f);
            var parent = Parent;
            if (parent != null) return g * parent.Chain();
            var outGroup = mixer?.outputAudioMixerGroup;
            return outGroup != null && outGroup != this ? g * outGroup.Gain() : g;
        }
    }

    public partial class AudioMixerSnapshot
    {
        internal AudioMixer m_Mixer; internal Dictionary<string, float> m_Blend;
        public AudioMixer audioMixer => m_Mixer ?? NRef<AudioMixer>("m_AudioMixer") ?? (m_Asset != null ? AssetLoader.Peek(m_Asset.Path)?.Main as AudioMixer : null);
        internal Dictionary<string, float> Values()
        {
            if (m_Blend != null) return m_Blend;
            var d = new Dictionary<string, float>();
            if (NGet("m_FloatValues") is YMap m) foreach (var (k, v) in m.Entries) if (v is YScalar sc) d[k] = sc.F;
            return d;
        }
        internal float Value(string guid, float def) => Values().TryGetValue(guid, out var v) ? v : def;
        public void TransitionTo(float timeToReach) => audioMixer?.TransitionToSnapshot(this, timeToReach);
    }
}
