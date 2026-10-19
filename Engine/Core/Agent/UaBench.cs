// `ua bench`: fixed synthetic workloads (physics 3D/2D, raycasts, legacy Animation, software rendering) built in memory, timed
// per frame. Gives the baseline the optimisations in ROADMAP phase 3 are measured against; --scene times a project scene too.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using UnityEngine;

namespace MiniUnity.Agent
{
    public static partial class UaCommands
    {
        static readonly string[] BenchAll = { "physics3d", "physics2d", "raycast", "animation", "render3d" };

        static void RegisterBench()
        {
            Reg("bench", "bench [--only physics3d,physics2d,raycast,animation,render3d] [--bodies 500] [--frames 300] [--render-frames 20] [--size 1920x1080] [--scene s.unity] [--png render.png]   time fixed workloads built in memory (ms per frame: mean / p50 / p95 / max, bytes allocated); --scene also times stepping and rendering a project scene", a =>
            {
                var only = a.O("only")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? BenchAll;
                foreach (var o in only) if (!BenchAll.Contains(o)) throw new UaError($"--only: unknown workload '{o}' (one of {string.Join(", ", BenchAll)})");
                int bodies = a.I("bodies", 500), frames = a.I("frames", 300), renderFrames = a.I("render-frames", 20);
                if (bodies <= 0 || frames <= 0 || renderFrames <= 0) throw new UaError("--bodies / --frames / --render-frames must be positive");
                var (w, h) = Ua.Size(a.O("size") ?? "1920x1080", "--size");
                var res = new JsonObject();
                var total = Stopwatch.StartNew();
                if (Environment.GetEnvironmentVariable("MU_VERIFY_DARK") == "1") { res["darkByteMismatches"] = MiniUnity.TextureData.VerifyDarkBytes(); res["verifySeconds"] = total.Elapsed.TotalSeconds; return res; }
                if (a.O("scene") is string scene) res["scene"] = BenchScene(a, Ua.Rel(scene), frames, renderFrames, w, h);
                if (a.O("scene") == null || a.Has("only"))
                    foreach (var o in only)
                        res[o] = o switch
                        {
                            "physics3d" => BenchPhysics3D(bodies, frames),
                            "physics2d" => BenchPhysics2D(bodies, frames),
                            "raycast" => BenchRaycast(bodies),
                            "animation" => BenchAnimation(bodies, frames),
                            _ => BenchRender(bodies, renderFrames, w, h, a.O("png")),
                        };
                Play.IsPlaying = false;
                res["seconds"] = Math.Round(total.Elapsed.TotalSeconds, 2);
                res["cpus"] = Environment.ProcessorCount;
                return res;
            });
        }

        // ------------------------------------------------------------------ helpers

        static void BenchWorld()
        {
            Play.Reset();
            new SceneData { Name = "Bench" }.MakeOnly();
            Play.FrameDelta = 1f / 60;
        }

        /// <summary>Times <paramref name="frame"/> over <paramref name="n"/> frames after <paramref name="warmup"/> untimed ones.</summary>
        static JsonObject Timed(int n, Action frame, int warmup = 5)
        {
            for (int i = 0; i < warmup; i++) frame();
            var ms = new double[n];
            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            int gc0 = GC.CollectionCount(0);
            var sw = new Stopwatch();
            for (int i = 0; i < n; i++) { sw.Restart(); frame(); ms[i] = sw.Elapsed.TotalMilliseconds; }
            long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
            var sorted = ms.OrderBy(x => x).ToArray();
            double P(double q) => sorted[Math.Min(n - 1, (int)(q * n))];
            return new JsonObject
            {
                ["frames"] = n, ["msMean"] = Math.Round(ms.Average(), 3), ["msP50"] = Math.Round(P(0.5), 3), ["msP95"] = Math.Round(P(0.95), 3),
                ["msMax"] = Math.Round(sorted[^1], 3), ["msTotal"] = Math.Round(ms.Sum(), 1),
                ["allocKBPerFrame"] = Math.Round(alloc / 1024.0 / n, 1), ["gen0"] = GC.CollectionCount(0) - gc0,
            };
        }

        /// <summary>Exact (bitwise) hash of every object's world pose: optimisations that must not change results keep it.</summary>
        static string Digest()
        {
            ulong h = 1469598103934665603;
            void Mix(float f) { h = (h ^ (uint)BitConverter.SingleToInt32Bits(f)) * 1099511628211; }
            foreach (var g in Play.SceneObjects().OrderBy(g => g.name, StringComparer.Ordinal))
            {
                var t = g.transform; var p = t.position; var q = t.rotation; var s = t.localScale;
                Mix(p.x); Mix(p.y); Mix(p.z); Mix(q.x); Mix(q.y); Mix(q.z); Mix(q.w); Mix(s.x); Mix(s.y); Mix(s.z);
            }
            return h.ToString("x16");
        }

        static GameObject Ground3D()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Ground"; g.transform.localScale = new Vector3(200, 1, 200); g.transform.position = new Vector3(0, -0.5f, 0);
            return g;
        }

        /// <summary>Boxes and spheres in a loose grid above the ground, a few per column so they land on each other.</summary>
        static void DropBodies3D(int n)
        {
            var rnd = new System.Random(1);
            int side = (int)Math.Ceiling(Math.Sqrt(n / 4.0));
            for (int i = 0; i < n; i++)
            {
                var g = GameObject.CreatePrimitive(i % 3 == 0 ? PrimitiveType.Sphere : PrimitiveType.Cube);
                g.name = "Body" + i;
                int col = i % (side * side), layer = i / (side * side);
                g.transform.position = new Vector3((col % side - side / 2f) * 1.6f + (float)rnd.NextDouble() * 0.2f, 1 + layer * 1.3f, (col / side - side / 2f) * 1.6f);
                g.transform.rotation = Quaternion.Euler((float)rnd.NextDouble() * 30, (float)rnd.NextDouble() * 360, 0);
                g.AddComponent<Rigidbody>();
            }
        }

        // ------------------------------------------------------------------ workloads

        static JsonObject BenchPhysics3D(int n, int frames)
        {
            BenchWorld();
            Ground3D();
            DropBodies3D(n);
            Play.IsPlaying = true; Play.Begin();
            var r = Timed(frames, Play.Step);
            r["bodies"] = n;
            r["sleeping"] = Play.SceneObjects().Select(g => g.GetComponent<Rigidbody>()).Count(b => b != null && b.IsSleeping());
            r["lowestY"] = Math.Round(Play.SceneObjects().Where(g => g.GetComponent<Rigidbody>() != null).Min(g => g.transform.position.y), 3);
            r["digest"] = Digest();
            return r;
        }

        static JsonObject BenchPhysics2D(int n, int frames)
        {
            BenchWorld();
            var ground = new GameObject("Ground");
            ground.transform.position = new Vector3(0, -1, 0);
            ground.AddComponent<BoxCollider2D>().size = new Vector2(400, 2);
            var rnd = new System.Random(2);
            int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(n * 2.0)));
            for (int i = 0; i < n; i++)
            {
                var g = new GameObject("Body" + i);
                g.transform.position = new Vector3((i % cols - cols / 2f) * 1.2f + (float)rnd.NextDouble() * 0.1f, 1 + i / cols * 1.2f, 0);
                if (i % 2 == 0) g.AddComponent<BoxCollider2D>(); else g.AddComponent<CircleCollider2D>().radius = 0.5f;
                g.AddComponent<Rigidbody2D>();
            }
            Play.IsPlaying = true; Play.Begin();
            var r = Timed(frames, Play.Step);
            r["bodies"] = n;
            r["lowestY"] = Math.Round(Play.SceneObjects().Where(g => g.GetComponent<Rigidbody2D>() != null).Min(g => g.transform.position.y), 3);
            r["digest"] = Digest();
            return r;
        }

        static JsonObject BenchRaycast(int n)
        {
            BenchWorld();
            Ground3D();
            DropBodies3D(n);
            Play.IsPlaying = true; Play.Begin();
            for (int i = 0; i < 120; i++) Play.Step();   // let the pile settle
            const int rays = 10000;
            var rnd = new System.Random(3);
            var origins = new Vector3[rays]; var dirs = new Vector3[rays];
            for (int i = 0; i < rays; i++)
            {
                origins[i] = new Vector3((float)rnd.NextDouble() * 60 - 30, 20, (float)rnd.NextDouble() * 60 - 30);
                dirs[i] = new Vector3((float)rnd.NextDouble() - 0.5f, -1, (float)rnd.NextDouble() - 0.5f).normalized;
            }
            int hits = 0, allHits = 0;
            var r = Timed(10, () =>
            {
                hits = 0; allHits = 0;
                for (int i = 0; i < rays; i++) if (Physics.Raycast(origins[i], dirs[i], out _, 100f)) hits++;
                for (int i = 0; i < rays / 10; i++) allHits += Physics.RaycastAll(origins[i], dirs[i], 100f).Length;
            }, warmup: 1);
            r["raycastsPerFrame"] = rays; r["raycastAllPerFrame"] = rays / 10;
            r["hits"] = hits; r["raycastAllHits"] = allHits; r["digest"] = Digest();
            r["usPerRaycast"] = Math.Round(r["msMean"]!.GetValue<double>() * 1000 / (rays * 1.1), 3);
            return r;
        }

        static JsonObject BenchAnimation(int n, int frames)
        {
            BenchWorld();
            var clip = new AnimationClip { name = "Bob", legacy = true, wrapMode = WrapMode.Loop };
            clip.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.EaseInOut(0, 0, 1, 2));
            clip.SetCurve("", typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.Linear(0, 0, 1, 360));
            clip.SetCurve("Child", typeof(Transform), "localScale.x", AnimationCurve.Linear(0, 1, 1, 2));
            clip.SetCurve("Child", typeof(Transform), "localScale.y", AnimationCurve.Linear(0, 1, 1, 2));
            for (int i = 0; i < n; i++)
            {
                var g = new GameObject("Anim" + i);
                g.transform.position = new Vector3(i % 20, 0, i / 20);
                new GameObject("Child").transform.SetParent(g.transform, false);
                var an = g.AddComponent<Animation>();
                an.AddClip(clip, clip.name); an.clip = clip; an.playAutomatically = true;
            }
            Play.IsPlaying = true; Play.Begin();
            var r = Timed(frames, Play.Step);
            r["objects"] = n; r["curves"] = 4;
            r["sampleY"] = Math.Round(Play.SceneObjects().First(g => g.name == "Anim0").transform.localPosition.y, 4);
            r["digest"] = Digest();
            return r;
        }

        /// <summary>Hash of the rendered pixels (PNG bytes; written to <paramref name="png"/> when given).</summary>
        static string ImageDigest(RenderTexture rt, string png)
        {
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
            var bytes = ImageConversion.EncodeToPNG(tex);
            if (png != null) System.IO.File.WriteAllBytes(Ua.Abs(png), bytes);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16].ToLowerInvariant();
        }

        static JsonObject BenchRender(int n, int frames, int w, int h, string png)
        {
            BenchWorld();
            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.transform.position = new Vector3(0, 18, -28); cam.transform.rotation = Quaternion.Euler(30, 0, 0);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.2f, 0.25f, 0.3f);
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            Ground3D();
            DropBodies3D(n);
            Play.IsPlaying = true; Play.Begin();
            for (int i = 0; i < 60; i++) Play.Step();
            var rt = new RenderTexture(w, h, 24);
            MiniUnity.WorldRenderer.Triangles = 0;
            var r = Timed(frames, () => MiniUnity.Recorder.RenderAll(rt), warmup: 2);
            r["objects"] = n + 1; r["size"] = $"{w}x{h}"; r["shadows"] = true;
            r["triangles"] = MiniUnity.WorldRenderer.Triangles / (frames + 2);
            r["digest"] = ImageDigest(rt, png);
            return r;
        }

        static JsonObject BenchScene(UaArgs a, string scene, int frames, int renderFrames, int w, int h)
        {
            var (m, build, _) = RuntimeHost.CompileAndLoad(a, x => !x.Editor);
            if (build.Errors > 0) throw new UaError("compile errors: fix them first (ua compile)");
            ScriptRegistry.ExtraScriptDirs.AddRange(m.PackageDirs.Values);
            ScriptRegistry.Reset();
            Play.Reset();
            Play.FrameDelta = 1f / 60;
            var load = Stopwatch.StartNew();
            RuntimeHost.LoadScene(scene).MakeOnly();
            MiniUnity.Screen.Width = w; MiniUnity.Screen.Height = h;
            Play.IsPlaying = true; Play.Begin();
            double loadMs = load.Elapsed.TotalMilliseconds;
            var r = new JsonObject { ["scene"] = scene, ["loadMs"] = Math.Round(loadMs, 1), ["objects"] = Play.SceneObjects().Count() };
            r["step"] = Timed(frames, Play.Step);
            var rt = new RenderTexture(w, h, 24);
            r["render"] = Timed(renderFrames, () => MiniUnity.Recorder.RenderAll(rt), warmup: 2);
            r["render"]!["size"] = $"{w}x{h}";
            r["render"]!["digest"] = ImageDigest(rt, a.O("png"));
            return r;
        }
    }
}
