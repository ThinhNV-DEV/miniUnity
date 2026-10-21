// `record <root> <n> [--lang en] [--fps 30] [--size 1920x1080] [--from s] [--to s] [--out file.mp4] [--crf 18] [--subtitles 0|1] [--jobs N]`
// (a host verb, e.g. MiniUnity.Video's): MiniUnity's Unity Recorder. The host's RecordSource builds item <n> in memory (for the
// VideoRecreate host: video n and its localization); the recorder then enters Play mode (Awake / Start / Update / physics / Animator /
// coroutines + the PlayableDirector advancing on game time, like VideoExport with Time.captureFramerate), renders every
// camera each frame with the software renderer (cameras with a target texture first, by depth – e.g. a game stage drawn
// into the RenderTexture of a RawImage – then the rest into the frame) and pipes the frames to ffmpeg (libx264).
// Audio: every unmuted Timeline AudioTrack (narration of the chosen language, BGM) mixed offline from its clips (clip-in,
// loop, clip ease-in/out, clip × track × AudioSource volume), plus the AudioSources the game played during the run
// (AudioSystem voices: one-shots, music), resampled to 48 kHz stereo and muxed as AAC.
// --jobs N (default: CPU count): N child processes each play the run from the start without rendering up to their share
// of frames, then render it (the player loop is deterministic, so the parts join seamlessly); the parent runs the whole
// play once without rendering for the game audio and concatenates the parts.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MiniUnity
{
    /// <summary>What a host records: an id (output name), the length in seconds, how to build it in memory and how to apply a
    /// language / subtitle choice to the built scene.</summary>
    public sealed class RecordSource
    {
        public string Id;
        public double Duration;
        public Action Build;
        public Action<string, bool> Localize;
    }

    public static class Recorder
    {
        const int Rate = 48000;

        /// <param name="args">record &lt;root&gt; &lt;n&gt; [options]; child render jobs are started with the same arguments</param>
        /// <param name="source">the host's item n</param>
        public static int Run(string[] args, Func<int, RecordSource> source)
        {
            string Opt(string name, string def) { int i = Array.IndexOf(args, "--" + name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            int n = int.Parse(args[2]);
            string lang = Opt("lang", "en");
            int fps = int.Parse(Opt("fps", "30"));
            var size = Opt("size", "1920x1080").Split('x');
            int W = int.Parse(size[0]), H = int.Parse(size[1]);
            double from = double.Parse(Opt("from", "0"), inv);
            int crf = int.Parse(Opt("crf", "18"));
            bool subtitles = Opt("subtitles", "1") != "0";
            HashOnly = Array.IndexOf(args, "--hash") >= 0;

            SafeIO.DryRun = true;   // build in memory only
            Screen.Width = W; Screen.Height = H;
            var vp = source(n);
            double to = double.Parse(Opt("to", (vp.Duration + 0.5).ToString(inv)), inv);
            string outFile = Path.GetFullPath(Opt("out", Path.Combine("Exports", vp.Id, $"{vp.Id}_{lang}.mp4")));
            Directory.CreateDirectory(Path.GetDirectoryName(outFile));
            var sw = Stopwatch.StartNew();
            // builders regenerate shared art (Art/Generated/*.png) with plain file writes: one build at a time across jobs
            using (var buildLock = AcquireLock(Path.Combine(AssetIndex.Root, "Library", "miniunity-record.lock")))
                MainThread.Run(() => { vp.Build(); return null; });
            Console.Error.WriteLine($"[record] built {vp.Id} in {sw.Elapsed.TotalSeconds:0.0}s");

            vp.Localize?.Invoke(lang, subtitles);
            var director = Object.FindObjectsOfType<PlayableDirector>().FirstOrDefault(d => d.playableAsset != null);
            if (director == null) { Console.Error.WriteLine("[record] no PlayableDirector"); return 1; }

            // ---------------------------------------------------------------- audio of the Timeline (offline)
            var mix = new float[(int)Math.Ceiling((to - from) * Rate) * 2 + 2];
            MixTimelineAudio(director, from, to, mix);

            // ---------------------------------------------------------------- play + render
            int frames = (int)Math.Round((to - from) * fps);
            int jobs = HashOnly ? 1 : int.Parse(Opt("jobs", Environment.ProcessorCount.ToString()));
            int segment = int.Parse(Opt("segment", "-1"));
            if (segment >= 0)
            {
                // child: simulate silently up to the segment, render it to a video-only part
                int segments = int.Parse(Opt("segments", "1"));
                int f0 = frames * segment / segments, f1 = frames * (segment + 1) / segments;
                return RenderFrames(director, from, fps, W, H, crf, f0, f1, Opt("part", outFile + $".part{segment}.mp4"));
            }
            string videoOnly = outFile + ".video.mp4";
            var children = new List<Process>();
            var parts = new List<string>();
            if (jobs > 1 && frames > fps * 4)
            {
                var self = Assembly.GetEntryAssembly()?.Location ?? typeof(Recorder).Assembly.Location;   // the host that runs `record`
                for (int i = 0; i < jobs; i++)
                {
                    string part = outFile + $".part{i}.mp4";
                    parts.Add(part);
                    var argList = string.Join(" ", args.Select(x => "\"" + x + "\"")) + $" --segment {i} --segments {jobs} --part \"{part}\" --from {from.ToString(inv)} --to {to.ToString(inv)}";
                    var psi = new ProcessStartInfo(Environment.ProcessPath, $"\"{self}\" {argList}") { UseShellExecute = false, RedirectStandardError = true };
                    var pr = Process.Start(psi);
                    int index = i;
                    pr.ErrorDataReceived += (_, e) => { if (e.Data != null && e.Data.StartsWith("[record]")) Console.Error.WriteLine($"[record:{index}] {e.Data.Substring(9)}"); };
                    pr.BeginErrorReadLine();
                    children.Add(pr);
                }
                // meanwhile: the whole run without rendering, for the sounds the game plays
                Simulate(director, from, fps, frames);
                foreach (var pr in children) pr.WaitForExit();
                if (children.Any(pr => pr.ExitCode != 0)) { Console.Error.WriteLine("[record] a render job failed"); return 1; }
                File.WriteAllText(outFile + ".parts.txt", string.Join("\n", parts.Select(p => $"file '{p}'")));
                var cat = StartFfmpeg($"-y -loglevel error -f concat -safe 0 -i \"{outFile}.parts.txt\" -c copy \"{videoOnly}\"");
                cat.StandardInput.Close(); cat.WaitForExit();
                foreach (var p in parts) File.Delete(p);
                File.Delete(outFile + ".parts.txt");
                if (cat.ExitCode != 0) { Console.Error.WriteLine("[record] concat failed: " + cat.StandardError.ReadToEnd()); return 1; }
            }
            else
            {
                int rc = RenderFrames(director, from, fps, W, H, crf, 0, frames, videoOnly);
                if (rc != 0 || HashOnly) return rc;
            }

            // ---------------------------------------------------------------- game audio (AudioSources played during the run)
            string gameWav = outFile + ".game.wav";
            Audio.AudioSystem.MixDown(gameWav, Rate);
            AddWav(gameWav, mix);
            File.Delete(gameWav);
            string wav = outFile + ".wav";
            WriteWav(wav, mix);
            var mux = StartFfmpeg($"-y -loglevel error -i \"{videoOnly}\" -i \"{wav}\" -c:v copy -af alimiter=limit=0.89:level=false -c:a aac -b:a 192k -shortest \"{outFile}\"");
            mux.StandardInput.Close();
            mux.WaitForExit();
            if (mux.ExitCode != 0) { Console.Error.WriteLine("[record] mux failed: " + mux.StandardError.ReadToEnd()); return 1; }
            File.Delete(videoOnly); File.Delete(wav);
            Play.IsPlaying = false;
            Console.Error.WriteLine($"[record] {outFile} · {frames} frames {W}x{H}@{fps} · {sw.Elapsed.TotalMinutes:0.0} min; errors {Debug.ErrorCount}, exceptions {Play.Exceptions.Count}");
            foreach (var (ex, where) in Play.Exceptions.Take(5)) Console.Error.WriteLine($"  {where}: {ex.GetType().Name}: {ex.Message}");
            if (Play.Exceptions.Count > 0) Console.Error.WriteLine(Play.Exceptions[0].Item1.StackTrace);
            return 0;
        }

        static void StartPlay(PlayableDirector director, double from, int fps)
        {
            Play.Reset();
            Play.FrameDelta = 1f / fps;
            Play.MaximumDeltaTime = Math.Max(Play.MaximumDeltaTime, 1f / fps);   // like Time.captureFramerate: game time follows the video at any fps
            director.playOnAwake = false;
            Audio.PlayableAudio.Enabled = false;   // Timeline audio tracks are mixed offline below, not through AudioSources
            Play.Begin();
            director.time = from;
            director.Play();
        }

        /// <summary>The whole run without rendering (game audio).</summary>
        static void Simulate(PlayableDirector director, double from, int fps, int frames)
        {
            StartPlay(director, from, fps);
            for (int f = 0; f < frames; f++) Play.Step();
        }

        /// <summary>--hash: render without encoding and print a SHA-256 of every frame's pixels (renderer regression checks).</summary>
        static bool HashOnly;

        /// <summary>Play from the start, render frames [f0, f1) to a video-only file.</summary>
        static int RenderFrames(PlayableDirector director, double from, int fps, int W, int H, int crf, int f0, int f1, string file)
        {
            StartPlay(director, from, fps);
            for (int f = 0; f < f0; f++) Play.Step();
            var frameRt = new RenderTexture(W, H, 24);
            if (HashOnly)
            {
                using var all = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
                var hsw = Stopwatch.StartNew();
                for (int f = f0; f < f1; f++) { Play.Step(); RenderAll(frameRt); all.AppendData(frameRt.m_Color); }
                Console.Error.WriteLine($"[record] frames {f0}-{f1} hash {Convert.ToHexString(all.GetHashAndReset())[..16].ToLowerInvariant()} · render {hsw.Elapsed.TotalSeconds:0.0}s");
                return 0;
            }
            var ff = StartFfmpeg($"-y -loglevel error -f rawvideo -pix_fmt rgba -s {W}x{H} -r {fps} -i - -c:v libx264 -preset medium -crf {crf} -pix_fmt yuv420p \"{file}\"");
            var stdin = ff.StandardInput.BaseStream;
            var row = new byte[W * 4];
            var t0 = Stopwatch.StartNew();
            for (int f = f0; f < f1; f++)
            {
                Play.Step();
                RenderAll(frameRt);
                var c = frameRt.m_Color;
                for (int y = H - 1; y >= 0; y--)   // rows are stored bottom-up
                {
                    Buffer.BlockCopy(c, y * W * 4, row, 0, row.Length);
                    stdin.Write(row, 0, row.Length);
                }
                int done = f - f0 + 1;
                if (done % (fps * 10) == 0 || f == f1 - 1)
                {
                    double el = t0.Elapsed.TotalSeconds, k = (double)done / (f1 - f0);
                    Console.Error.WriteLine($"[record] {from + (double)(f + 1) / fps:0.0}s · {done / Math.Max(el, 1e-6):0.0} fps · eta {el / k - el:0}s");
                }
            }
            stdin.Close();
            ff.WaitForExit();
            if (ff.ExitCode != 0) { Console.Error.WriteLine("[record] ffmpeg failed: " + ff.StandardError.ReadToEnd()); return 1; }
            return 0;
        }

        /// <summary>Render every enabled camera by depth: cameras with a target texture into it, the others into <paramref name="frame"/>.</summary>
        public static void RenderAll(RenderTexture frame)
        {
            var cams = ObjectRegistry.SceneObjects(typeof(Camera), false).Cast<Camera>()
                .Where(c => c.enabled && c.gameObject.activeInHierarchy).OrderBy(c => c.targetTexture == null ? 1 : 0).ThenBy(c => c.depth).ToList();
            foreach (var c in cams)
            {
                if (c.targetTexture != null) { c.Render(); continue; }
                c.targetTexture = frame;
                c.Render();
                c.targetTexture = null;
            }
            UIRenderer.RenderOverlay(frame);
        }

        static FileStream AcquireLock(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            while (true)
            {
                try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { System.Threading.Thread.Sleep(200); }
            }
        }

        static Process StartFfmpeg(string arguments)
        {
            var psi = new ProcessStartInfo("ffmpeg", arguments) { RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false };
            return Process.Start(psi);
        }

        // ------------------------------------------------------------------ audio
        static readonly FieldInfo ClipProps = typeof(AudioPlayableAsset).GetField("m_ClipProperties", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo TrackProps = typeof(AudioTrack).GetField("m_TrackProperties", BindingFlags.Instance | BindingFlags.NonPublic);

        static float Volume(object props) => props?.GetType().GetField("volume", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(props) is float v ? v : 1f;

        static void MixTimelineAudio(PlayableDirector director, double from, double to, float[] mix)
        {
            if (director.playableAsset is not TimelineAsset timeline) return;
            foreach (var track in timeline.GetOutputTracks().OfType<AudioTrack>())
            {
                if (track.muted) continue;
                var source = director.GetGenericBinding(track) as AudioSource;
                if (source != null && (source.mute || !source.enabled)) continue;
                float trackVol = Volume(TrackProps?.GetValue(track)) * (source != null ? source.volume : 1f);
                foreach (var clip in track.GetClips())
                {
                    if (clip.asset is not AudioPlayableAsset ap || ap.clip == null) continue;
                    var data = ap.clip.Data();
                    if (data == null || data.Length == 0) continue;
                    int ch = Math.Max(1, ap.clip.channels), freq = ap.clip.frequency;
                    int frames = data.Length / ch;
                    float vol = trackVol * Volume(ClipProps?.GetValue(ap));
                    double start = Math.Max(clip.start, from), end = Math.Min(clip.end, to);
                    for (long i = (long)Math.Ceiling((start - from) * Rate); i < (long)((end - from) * Rate); i++)
                    {
                        double t = from + (double)i / Rate;                 // timeline time
                        double local = (t - clip.start) * clip.timeScale + clip.clipIn;   // seconds into the audio clip
                        double pos = local * freq;
                        if (ap.loop && frames > 0) pos %= frames;
                        int p0 = (int)pos; if (p0 < 0 || p0 >= frames) continue;
                        int p1 = Math.Min(p0 + 1, frames - 1); float k = (float)(pos - p0);
                        float w = vol * Ease(clip, t);
                        float l = data[p0 * ch] * (1 - k) + data[p1 * ch] * k;
                        float r = ch > 1 ? data[p0 * ch + 1] * (1 - k) + data[p1 * ch + 1] * k : l;
                        long o = i * 2;
                        if (o + 1 >= mix.Length) break;
                        mix[o] += l * w; mix[o + 1] += r * w;
                    }
                }
            }
        }

        static float Ease(TimelineClip clip, double t)
        {
            float w = 1;
            if (clip.easeInDuration > 0 && t < clip.start + clip.easeInDuration) w *= (float)((t - clip.start) / clip.easeInDuration);
            if (clip.easeOutDuration > 0 && t > clip.end - clip.easeOutDuration) w *= (float)((clip.end - t) / clip.easeOutDuration);
            return Mathf.Clamp01(w);
        }

        static void AddWav(string path, float[] mix)
        {
            if (!File.Exists(path)) return;
            var data = Audio.AudioDecode.DecodeWav(path);
            if (!Audio.AudioDecode.WavInfo(path, out int ch, out int rate, out _, out _, out _, out _) || data == null) return;
            int frames = data.Length / Math.Max(1, ch);
            for (int i = 0; i < mix.Length / 2; i++)
            {
                double src = (double)i * rate / Rate;
                int p = (int)src; if (p >= frames) break;
                mix[i * 2] += data[p * ch];
                mix[i * 2 + 1] += data[p * ch + (ch > 1 ? 1 : 0)];
            }
        }

        static void WriteWav(string path, float[] mix)
        {
            using var w = new BinaryWriter(File.Create(path));
            int bytes = mix.Length * 2;
            w.Write("RIFF".ToCharArray()); w.Write(36 + bytes); w.Write("WAVEfmt ".ToCharArray());
            w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(bytes);
            foreach (var s in mix) w.Write((short)Math.Clamp((int)Math.Round(s * 32767f), -32768, 32767));
        }
    }
}
