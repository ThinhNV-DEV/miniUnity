// Audio playables: after a graph evaluates an AudioPlayableOutput, every AudioClipPlayable it reaches (playing, with weight) gets a
// voice on the output's AudioSource. The voice follows the node: clip position from its time (re-synced on seeks or drift), gain
// = clip volume × the input weights on the path (an AudioMixerPlayable created with normalizeInputVolumes divides by their sum),
// pan = the clip's stereo pan. Voices stop when the node pauses, its weight drops to 0, a non-looping clip runs out or the output
// goes away. The Recorder turns this off: it mixes Timeline audio tracks itself.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Playables;
using RT = MiniUnity.PlayableRuntime;

namespace MiniUnity.Audio
{
    public static class PlayableAudio
    {
        public static bool Enabled = true;
        static readonly Dictionary<(long output, long node), Voice> voices = new();

        public static void Reset() => voices.Clear();

        public static bool IsPlaying(PlayableHandle h)
        {
            foreach (var ((_, node), v) in voices) if (node == (long)h.m_Handle && v.Started && !v.Finished) return true;
            return false;
        }

        static void Collect(PlayableHandle h, int port, float w, Dictionary<long, (RT.Node n, float w)> live, HashSet<long> seen)
        {
            var n = RT.N(h);
            if (n == null || n.state != PlayState.Playing || w <= 0 || !seen.Add((long)h.m_Handle)) return;
            if (n.extra is AudioClipPlayable.Data) { live[(long)h.m_Handle] = (n, w); return; }
            float norm = 1;
            if (n.type == typeof(AudioMixerPlayable) && n.extra is true)
            {
                float sum = 0; foreach (var (src, iw) in n.inputs) if (RT.N(src) != null) sum += iw;
                if (sum > 1) norm = 1 / sum;
            }
            if (n.traversal == PlayableTraversalMode.Passthrough)
            {
                if (port >= 0 && port < n.inputs.Count) Collect(n.inputs[port].src, 0, w, live, seen);
                return;
            }
            foreach (var (src, iw) in n.inputs) Collect(src, 0, w * iw * norm, live, seen);
        }

        public static void Sync(RT.Output o, PlayableOutputHandle oh)
        {
            long oid = (long)oh.m_Handle;
            var live = new Dictionary<long, (RT.Node n, float w)>();
            var src = o.userData as AudioSource;
            if (Enabled && src != null && !src.m_Destroyed && src.enabled && src.gameObject.activeInHierarchy)
                Collect(o.source, o.sourcePort, o.weight, live, new HashSet<long>());
            foreach (var key in voices.Keys.Where(k => k.output == oid && !live.ContainsKey(k.node)).ToList())
            {
                var v = voices[key]; voices.Remove(key);
                if (!v.Finished) AudioSystem.StopPlayableVoice(v);
            }
            foreach (var (id, (n, w)) in live)
            {
                var d = (AudioClipPlayable.Data)n.extra;
                var clip = d.clip;
                if (clip == null || clip.length <= 0) continue;
                double len = clip.length, t = n.time;
                bool inRange = d.loop || t >= 0 && t < len;
                voices.TryGetValue((oid, id), out var v);
                if (!inRange)
                {
                    if (v != null) { voices.Remove((oid, id)); if (!v.Finished) AudioSystem.StopPlayableVoice(v); }
                    continue;
                }
                double pos = d.loop ? t - Math.Floor(t / len) * len : t;
                if (v != null && (v.Source != src || v.Clip != clip)) { AudioSystem.StopPlayableVoice(v); v = null; }
                if (v != null && v.Finished) v = null;
                if (v == null)
                {
                    v = new Voice { Source = src, Clip = clip, StartDsp = AudioSystem.DspTime + d.startDelay, ClipPos = pos, Loop = d.loop, LastDsp = AudioSystem.DspTime, FromPlayable = true };
                    AudioSystem.Voices.Add(v);
                    voices[(oid, id)] = v;
                }
                else
                {
                    double drift = Math.Abs(v.ClipPos - pos);
                    if (d.loop) drift = Math.Min(drift, len - drift);
                    if (drift > 0.1) v.ClipPos = pos;   // seek
                }
                v.Loop = d.loop; v.VolumeScale = d.volume * w; v.Pan = d.pan;
            }
        }
    }
}
