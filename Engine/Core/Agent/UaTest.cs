// Agent commands that execute project code: `play` (run a scene for N frames, collect logs / exceptions / watched values),
// `test` (NUnit EditMode + PlayMode tests through a reflection runner: [Test], [TestCase], [UnityTest], SetUp/TearDown,
// OneTimeSetUp/TearDown, UnitySetUp/UnityTearDown, Ignore/Explicit/Category, LogAssert via the Test Framework's LogScope),
// `log.analyze` (Unity Editor.log / Player.log / test output: compile errors, exceptions, grouped messages).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MiniUnity.Agent
{
    public static partial class UaCommands
    {

        /// <summary>Project-relative path for project sources; absolute for anything outside (Editor built-in packages).</summary>
        static string SrcPath(string file) { var r = Ua.Rel(file); return r.StartsWith("../") ? Path.GetFullPath(file) : r; }

        static JsonObject ExceptionJson(Exception e, string where = null)
        {
            var frames = new JsonArray();
            var st = new StackTrace(e, true);
            foreach (var fr in st.GetFrames() ?? Array.Empty<StackFrame>())
            {
                var m = fr.GetMethod();
                if (m == null) continue;
                var file = fr.GetFileName();
                var o = new JsonObject { ["method"] = $"{m.DeclaringType?.FullName}.{m.Name}" };
                if (file != null) { o["file"] = SrcPath(file); o["line"] = fr.GetFileLineNumber(); }
                frames.Add(o);
            }
            var res = new JsonObject { ["type"] = e.GetType().FullName, ["message"] = e.Message, ["where"] = where, ["stack"] = frames };
            var first = st.GetFrames()?.FirstOrDefault(f => f.GetFileName() != null && !f.GetFileName()!.Contains("/MiniUnity/"));
            if (first != null) { res["file"] = SrcPath(first.GetFileName()!); res["line"] = first.GetFileLineNumber(); }
            if (e.InnerException != null) res["inner"] = ExceptionJson(e.InnerException);   // TypeInitializationException, TargetInvocationException…
            var root = e; while (root.InnerException != null) root = root.InnerException;
            if (root is NotImplementedException) res["miniunity"] = "API not implemented by MiniUnity (works in Unity): " + root.Message;
            else if (root is MissingMemberException or TypeLoadException && root.Message.Contains("Unity")) res["miniunity"] = "Unity API missing in MiniUnity (works in Unity): " + root.Message;
            return res;
        }

        static UnityEngine.GameObject FindLive(string path)
        {
            var segs = path.Trim('/').Split('/');
            IEnumerable<UnityEngine.GameObject> level = SceneData.Active?.Roots ?? new List<UnityEngine.GameObject>();
            UnityEngine.GameObject cur = null;
            foreach (var s in segs)
            {
                cur = level.FirstOrDefault(g => g.m_Name == s);
                if (cur == null) return null;
                level = cur.m_Transform.m_Children.Select(c => c.gameObject);
            }
            return cur;
        }

        /// <summary>"Player/Health.current" or "Player/Transform.position" → live value (fields, properties).</summary>
        static JsonNode Watch(string spec)
        {
            int dot = spec.LastIndexOf('.'); int slash = spec.LastIndexOf('/', dot < 0 ? spec.Length - 1 : dot);
            if (dot < 0 || slash < 0) return JsonValue.Create("bad watch spec (use Path/To/Object/Component.member)");
            var go = FindLive(spec[..slash]);
            if (go == null) return JsonValue.Create("GameObject not found");
            var compName = spec[(slash + 1)..dot]; var member = spec[(dot + 1)..];
            object target = compName == "GameObject" ? go : go.m_Components.FirstOrDefault(c => c.GetType().Name == compName);
            if (target == null) return JsonValue.Create("component not found");
            var t = target.GetType();
            object v = null;
            var f = t.GetField(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) v = f.GetValue(target);
            else
            {
                var p = t.GetProperty(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null) return JsonValue.Create("member not found");
                try { v = p.GetValue(target); } catch (TargetInvocationException e) { return JsonValue.Create("threw " + e.InnerException?.GetType().Name + ": " + e.InnerException?.Message); }
            }
            return v switch
            {
                null => null,
                string s => JsonValue.Create(s),
                bool b => JsonValue.Create(b),
                int or long or float or double or short or byte => JsonValue.Create(Convert.ToDouble(v)),
                UnityEngine.Object uo => JsonValue.Create(uo.m_Destroyed ? "(destroyed)" : uo.m_Name + " (" + uo.GetType().Name + ")"),
                _ => JsonValue.Create(v.ToString()),
            };
        }

        static JsonObject PlaySummary(int maxLogs)
        {
            var logs = Play.Log;
            return new JsonObject
            {
                ["frames"] = Play.Frame, ["time"] = Math.Round(Play.Time, 3),
                ["errors"] = logs.Count(l => l.Type is "Error" or "Exception" or "Assert"), ["warnings"] = logs.Count(l => l.Type == "Warning"),
                ["exceptions"] = Arr(Play.Exceptions.GroupBy(x => x.ex.GetType().FullName + "|" + x.ex.Message + "|" + x.where).Take(30),
                    g => { var o = ExceptionJson(g.First().ex, g.First().where); o["count"] = g.Count(); return o; }),
                ["logs"] = Arr(logs.GroupBy(l => l.Type + "|" + l.Message).Take(maxLogs), g => new JsonObject
                {
                    ["type"] = g.First().Type, ["message"] = g.First().Message, ["count"] = g.Count(), ["firstFrame"] = g.First().Frame,
                }),
            };
        }

        static void RegisterTestCommands()
        {
            Reg("play", "play <scene.unity> [--frames n | --seconds s] [--fps 60] [--watch Path/Comp.member,…] [--input Space@10,mouse0@5-8,Horizontal=1@20-40] [--max-logs n] [--audio-out mix.wav] [--screenshot out.png[@seconds,…] --size 1080x1920] [--call Path/Comp.Method@seconds,…] [--interactive [--serve port]]   run the scene in MiniUnity's player loop (physics, animation, audio timeline); screenshots render every camera with the software renderer; --interactive keeps the session open for JSON commands on stdin (step / input / hold / mouse / screenshot / watch / state / call / quit, replies on lines starting with \"@mu \") and --serve adds a browser viewer to play it", a =>
            {
                var scene = Ua.Rel(a.P(0, "scene"));
                var (m, build, _) = RuntimeHost.CompileAndLoad(a, x => !x.Editor);
                if (build.Errors > 0) return new JsonObject { ["compiled"] = false, ["errors"] = build.Errors, ["hint"] = "fix compile errors first (ua compile)" };
                ScriptRegistry.ExtraScriptDirs.AddRange(m.PackageDirs.Values);
                ScriptRegistry.Reset();
                Play.Reset();
                int fps = a.I("fps", 60);
                if (fps <= 0) throw new UaError($"--fps: expected a positive integer, got {fps}");
                var (width, height) = Ua.Size(a.O("size") ?? "1920x1080", "--size");
                Play.FrameDelta = 1f / fps;
                if (a.O("input") is string input) Play.Input.Parse(input);
                RuntimeHost.LoadScene(scene);
                int frames = a.Has("seconds") ? (int)Math.Ceiling(Ua.Num(a.O("seconds"), "--seconds") / Play.FrameDelta) : a.I("frames", 60);
                var sw = Stopwatch.StartNew();
                MiniUnity.Screen.Width = width; MiniUnity.Screen.Height = height;   // before Awake/Start: games read Screen.width there
                Play.Begin();
                // --screenshot file.png (end of the run) or file.png@1.5,3 (one file per time: file_1.5s.png); --call Obj/Comp.Method@t
                var shots = new List<(int frame, string file)>();
                var shotFiles = new JsonArray();
                if (a.O("screenshot") is string shot)
                {
                    var parts = shot.Split('@');
                    if (parts.Length == 1) shots.Add((frames, parts[0]));
                    else
                        foreach (var ts in parts[1].Split(','))
                        {
                            double sec = Ua.Num(ts, "--screenshot time");
                            shots.Add(((int)Math.Round(sec / Play.FrameDelta), Path.ChangeExtension(parts[0], null) + $"_{ts}s.png"));
                        }
                }
                var calls = new List<(int frame, string path)>();
                if (a.O("call") is string callSpec)
                    foreach (var c in callSpec.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var cp = c.Split('@');
                        calls.Add(((int)Math.Round(Ua.Num(cp.Length > 1 ? cp[1] : "0", "--call time") / Play.FrameDelta), cp[0]));
                    }
                RenderTexture shotRt = null;
                void Shoot(string file)
                {
                    shotRt ??= new RenderTexture(width, height, 24);
                    MiniUnity.Recorder.RenderAll(shotRt);
                    var tex = new Texture2D(shotRt.width, shotRt.height, TextureFormat.RGB24, false);
                    RenderTexture.active = shotRt; tex.ReadPixels(new Rect(0, 0, shotRt.width, shotRt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
                    File.WriteAllBytes(Ua.Abs(file), ImageConversion.EncodeToPNG(tex));   // may be outside the project (/tmp/shot.png)
                    shotFiles.Add(JsonValue.Create(file));
                }
                if (a.Has("interactive") || a.Has("serve"))
                {
                    byte[] Grab()
                    {
                        shotRt ??= new RenderTexture(width, height, 24);
                        MiniUnity.Recorder.RenderAll(shotRt);
                        var tex = new Texture2D(shotRt.width, shotRt.height, TextureFormat.RGB24, false);
                        RenderTexture.active = shotRt; tex.ReadPixels(new Rect(0, 0, shotRt.width, shotRt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
                        return ImageConversion.EncodeToPNG(tex);
                    }
                    PlaySession.Run(Grab, Watch, CallMethod, a.I("serve", 0), width, height);
                }
                else
                for (int i = 0; i < frames; i++)
                {
                    foreach (var (f, path) in calls) if (f == i) CallMethod(path);
                    Play.Step();
                    foreach (var (f, file) in shots) if (f == i + 1) Shoot(file);
                }
                var res = PlaySummary(a.I("max-logs", 60));
                if (shotFiles.Count > 0) res["screenshots"] = shotFiles;
                res["scene"] = scene; res["seconds"] = Math.Round(sw.Elapsed.TotalSeconds, 2);
                res["objects"] = Play.SceneObjects().Count();
                if (a.O("watch") is string w) res["watch"] = new JsonObject(w.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => new KeyValuePair<string, JsonNode>(s, Watch(s.Trim()))));
                if (MiniUnity.Audio.AudioSystem.Log.Count > 0)
                    res["audio"] = Arr(MiniUnity.Audio.AudioSystem.Log.Take(a.I("max-logs", 60) * 4), e => new JsonObject
                    {
                        ["dsp"] = Math.Round(e.Dsp, 4), ["frame"] = e.Frame, ["source"] = e.Source, ["clip"] = e.Clip, ["action"] = e.Action,
                        ["volume"] = Math.Round(e.Volume, 3), ["pitch"] = Math.Round(e.Pitch, 3), ["clipTime"] = Math.Round(e.ClipTime, 3),
                    });
                if (a.O("audio-out") is string wav)
                {
                    var (voices, secs, peak) = MiniUnity.Audio.AudioSystem.MixDown(Ua.Abs(wav));
                    res["audioOut"] = new JsonObject { ["file"] = wav, ["voices"] = voices, ["seconds"] = Math.Round(secs, 3), ["peak"] = Math.Round(peak, 4) };
                }
                res["note"] = "MiniUnity simulates the player loop with physics (3D/2D), Animator/Animation and the audio timeline; frames are rendered (software renderer) only for --screenshot";
                Play.IsPlaying = false;
                return res;
            });
            static void CallMethod(string path)
            {
                // "Object/Path/Component.Method" (no arguments) on the first match in the scene
                int dot = path.LastIndexOf('.'); int slash = path.LastIndexOf('/', dot);
                string goPath = path.Substring(0, slash), comp = path.Substring(slash + 1, dot - slash - 1), method = path.Substring(dot + 1);
                var go = Play.SceneObjects().FirstOrDefault(g => Hier.Path(g.transform) == goPath || g.name == goPath);
                var c = go?.m_Components.FirstOrDefault(x => x != null && x.GetType().Name == comp);
                c?.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null)?.Invoke(c, null);
            }
            Reg("test", "test [--mode editmode|playmode|all] [--assembly a,b] [--filter text] [--category c] [--list] [--frames-timeout n] [--input spec]   run NUnit tests (Unity Test Framework style); --input scripts legacy Input for every PlayMode test (frames from the test start)", a =>
            {
                TestRunner.s_TestInput = a.O("input");
                var only = a.O("assembly")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
                var mode = a.O("mode", "all");
                var (m, build, asms) = RuntimeHost.CompileAndLoad(a, x => only != null ? only.Contains(x.Name) : !x.FromPackage && (x.IsTest || x.Refs.Any(r => r.Name == "UnityEngine.TestRunner")));
                ScriptRegistry.ExtraScriptDirs.AddRange(m.PackageDirs.Values);
                var compileErrors = build.Order.Where(x => x.Failed && x.Skipped == null).Select(x => x.Name).ToList();
                var results = new List<JsonObject>();
                var listOnly = a.Has("list");
                foreach (var asm in asms)
                {
                    var ua = m.Assemblies.First(x => x.Name == asm.GetName().Name);
                    bool editMode = ua.Editor;
                    if (mode == "editmode" && !editMode || mode == "playmode" && editMode) continue;
                    foreach (var r in TestRunner.Run(asm, editMode, a.O("filter"), a.O("category"), listOnly, a.I("frames-timeout", 6000))) results.Add(r);
                }
                int Count(string s) => results.Count(r => r["result"]?.GetValue<string>() == s);
                return new JsonObject
                {
                    ["total"] = results.Count, ["passed"] = Count("Passed"), ["failed"] = Count("Failed"), ["skipped"] = Count("Skipped"), ["inconclusive"] = Count("Inconclusive"),
                    ["compileErrors"] = Arr(compileErrors, J), ["testAssemblies"] = Arr(asms.Select(x => x.GetName().Name), J),
                    ["tests"] = Arr(listOnly ? results : results.OrderBy(r => r["result"]?.GetValue<string>() == "Failed" ? 0 : 1), (JsonNode r) => r),
                };
            });
            Reg("log.analyze", "log.analyze <Editor.log|Player.log|file> [--max n]   compile errors, exceptions (with first project frame), errors / warnings grouped", a =>
            {
                var path = a.P(0, "log");
                if (!File.Exists(path)) path = Ua.Abs(path);
                if (!File.Exists(path)) throw new UaError("log not found: " + a.P(0));
                return LogAnalyzer.Analyze(File.ReadAllLines(path), a.I("max", 50));
            });
        }
    }

    public static class TestRunner
    {
        // attribute data first (no instantiation: attributes whose constructors MiniUnity lacks must not break discovery)
        static bool Has(MemberInfo m, string attr)
        {
            try { return m.GetCustomAttributesData().Any(x => x.AttributeType.Name == attr || x.AttributeType.Name == attr + "Attribute"); }
            catch { return false; }
        }
        static IEnumerable<object> Attrs(MemberInfo m, string attr)
        {
            if (!Has(m, attr)) return Array.Empty<object>();
            try { return m.GetCustomAttributes(true).Where(x => x.GetType().Name == attr + "Attribute").ToList(); }
            catch { return Array.Empty<object>(); }
        }

        public static IEnumerable<JsonObject> Run(Assembly asm, bool editMode, string filter, string category, bool listOnly, int frameTimeout)
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
            foreach (var t in types.Where(t => t.IsClass && !t.IsAbstract && t.IsPublic || t.IsNestedPublic).OrderBy(t => t.FullName))
            {
                var tests = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
                    .Where(m => Has(m, "Test") || Has(m, "TestCase") || Has(m, "UnityTest") || Has(m, "TestCaseSource")).ToList();
                if (tests.Count == 0) continue;
                var cases = new List<(MethodInfo m, object[] args, string name)>();
                foreach (var m in tests)
                {
                    var tc = Attrs(m, "TestCase").ToList();
                    if (tc.Count > 0)
                        foreach (var c in tc)
                        {
                            var args = (object[])c.GetType().GetProperty("Arguments")!.GetValue(c)!;
                            cases.Add((m, args, $"{m.Name}({string.Join(",", args.Select(x => x?.ToString() ?? "null"))})"));
                        }
                    else if (Attrs(m, "TestCaseSource").FirstOrDefault() is object src)
                    {
                        var srcName = src.GetType().GetProperty("SourceName")?.GetValue(src) as string;
                        var mem = srcName == null ? null : (MemberInfo)t.GetField(srcName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                            ?? (MemberInfo)t.GetProperty(srcName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ?? t.GetMethod(srcName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        object val = mem switch { FieldInfo fi => fi.GetValue(null), PropertyInfo pi => pi.GetValue(null), MethodInfo mi => mi.Invoke(null, null), _ => null };
                        if (val is IEnumerable en)
                            foreach (var item in en)
                            {
                                var args = item is object[] arr ? arr : item?.GetType().GetProperty("Arguments")?.GetValue(item) as object[] ?? new[] { item };
                                cases.Add((m, args, $"{m.Name}({string.Join(",", args.Select(x => x?.ToString() ?? "null"))})"));
                            }
                    }
                    else if (m.GetParameters().Length > 0 && ParameterValues(m) is List<object[]> combos)
                        foreach (var args in combos) cases.Add((m, args, $"{m.Name}({string.Join(",", args.Select(x => x?.ToString() ?? "null"))})"));
                    else cases.Add((m, Array.Empty<object>(), m.Name));
                }
                string Full(string n) => t.FullName + "." + n;
                cases = cases.Where(c => filter == null || Full(c.name).Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                if (category != null)
                    cases = cases.Where(c => Attrs(c.m, "Category").Concat(Attrs(t, "Category")).Any(x => (x.GetType().GetProperty("Name")?.GetValue(x) as string) == category)).ToList();
                if (cases.Count == 0) continue;
                if (listOnly)
                {
                    foreach (var c in cases) yield return new JsonObject { ["name"] = Full(c.name), ["assembly"] = asm.GetName().Name, ["mode"] = editMode ? "EditMode" : "PlayMode" };
                    continue;
                }
                foreach (var r in RunFixture(t, cases, editMode, frameTimeout)) { r["assembly"] = asm.GetName().Name; r["mode"] = editMode ? "EditMode" : "PlayMode"; yield return r; }
            }
        }

        /// <summary>NUnit [Values] / [Range] on parameters: the combinatorial product (empty [Values] on an enum or bool = all values).</summary>
        static List<object[]> ParameterValues(MethodInfo m)
        {
            var per = new List<object[]>();
            foreach (var p in m.GetParameters())
            {
                var attrs = p.GetCustomAttributes(true);
                object[] vals = null;
                foreach (var a in attrs)
                {
                    var an = a.GetType().Name;
                    if (an == "ValuesAttribute")
                    {
                        var data = a.GetType().GetField("data", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(a) as object[];
                        if (data != null && data.Length > 0) vals = data.Select(v => v != null && p.ParameterType.IsEnum && !p.ParameterType.IsInstanceOfType(v) ? Enum.ToObject(p.ParameterType, v) : Convert.ChangeType(v, Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType)).ToArray();
                        else if (p.ParameterType.IsEnum) vals = Enum.GetValues(p.ParameterType).Cast<object>().ToArray();
                        else if (p.ParameterType == typeof(bool)) vals = new object[] { true, false };
                    }
                    else if (an == "RangeAttribute")
                    {
                        // NUnit stores from / to / step as private fields of the attribute
                        object F(string n) => a.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(a);
                        double from = Convert.ToDouble(F("_from") ?? F("from")), to = Convert.ToDouble(F("_to") ?? F("to")), step = Convert.ToDouble(F("_step") ?? F("step") ?? 1.0);
                        if (step == 0) step = 1;
                        var list = new List<object>();
                        for (double v = from; step > 0 ? v <= to + 1e-9 : v >= to - 1e-9; v += step) list.Add(Convert.ChangeType(v, p.ParameterType));
                        vals = list.ToArray();
                    }
                }
                if (vals == null || vals.Length == 0) return null;
                per.Add(vals);
            }
            var res = new List<object[]> { Array.Empty<object>() };
            foreach (var vals in per) res = res.SelectMany(prefix => vals.Select(v => prefix.Append(v).ToArray())).ToList();
            return res;
        }

        static MethodInfo[] Marked(Type t, string attr) =>
            t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where(m => Has(m, attr)).ToArray();

        static IEnumerable<JsonObject> RunFixture(Type t, List<(MethodInfo m, object[] args, string name)> cases, bool editMode, int frameTimeout)
        {
            var list = new List<JsonObject>();
            object fixture = null;
            string fixtureError = null;
            if (Has(t, "Ignore")) { foreach (var c in cases) list.Add(Result(t, c.name, "Skipped", "fixture ignored", 0)); return list; }
            try { fixture = Activator.CreateInstance(t); } catch (Exception e) { fixtureError = "fixture constructor: " + (e.InnerException ?? e).Message; }
            if (fixtureError == null)
                foreach (var m in Marked(t, "OneTimeSetUp"))
                {
                    var err = Invoke(fixture, m, Array.Empty<object>(), editMode, frameTimeout);
                    if (err != null) { fixtureError = "OneTimeSetUp: " + err.Value.message; break; }
                }
            foreach (var c in cases)
            {
                if (fixtureError != null) { list.Add(Result(t, c.name, "Failed", fixtureError, 0)); continue; }
                if (Has(c.m, "Ignore")) { list.Add(Result(t, c.name, "Skipped", "ignored", 0)); continue; }
                if (Has(c.m, "Explicit")) { list.Add(Result(t, c.name, "Skipped", "explicit", 0)); continue; }
                list.Add(RunCase(fixture, t, c, editMode, frameTimeout));
            }
            if (fixture != null) foreach (var m in Marked(t, "OneTimeTearDown")) Invoke(fixture, m, Array.Empty<object>(), editMode, frameTimeout);
            return list;
        }

        static JsonObject Result(Type t, string name, string result, string message, double ms, JsonObject ex = null)
        {
            var o = new JsonObject { ["name"] = t.FullName + "." + name, ["result"] = result, ["ms"] = Math.Round(ms, 1) };
            if (message != null) o["message"] = message;
            if (ex != null) { o["file"] = ex["file"]?.DeepClone(); o["line"] = ex["line"]?.DeepClone(); o["stack"] = ex["stack"]?.DeepClone(); }
            return o;
        }

        internal static string s_TestInput;

        static JsonObject RunCase(object fixture, Type t, (MethodInfo m, object[] args, string name) c, bool editMode, int frameTimeout)
        {
            var sw = Stopwatch.StartNew();
            // fresh world for every test: empty active scene; PlayMode tests run inside the player loop
            Play.Reset();
            if (!editMode && s_TestInput != null) Play.Input.Parse(s_TestInput);
            SceneData.Active = new SceneData { Name = "TestScene" };
            if (!editMode) { Play.IsPlaying = true; Play.Begin(); }
            var scope = LogScopeBegin();
            (string kind, string message, Exception ex)? err = null;
            foreach (var m in Marked(t, "SetUp").Concat(Marked(t, "UnitySetUp")))
                if ((err = Invoke(fixture, m, Array.Empty<object>(), editMode, frameTimeout)) != null) break;
            if (err == null)
            {
                var args = ConvertArgs(c.m, c.args);
                err = Invoke(fixture, c.m, args, editMode, frameTimeout);
            }
            foreach (var m in Marked(t, "TearDown").Concat(Marked(t, "UnityTearDown")))
            {
                var e2 = Invoke(fixture, m, Array.Empty<object>(), editMode, frameTimeout);
                err ??= e2;
            }
            var logFailure = LogScopeEnd(scope);
            Play.IsPlaying = false;
            var ms = sw.Elapsed.TotalMilliseconds;
            if (err == null && logFailure != null) return Result(t, c.name, "Failed", logFailure, ms);
            if (err == null) return Result(t, c.name, "Passed", null, ms);
            var (kind, message, ex) = err.Value;
            return Result(t, c.name, kind, message, ms, ex != null ? UaCommands_ExceptionJson(ex) : null);
        }

        static JsonObject UaCommands_ExceptionJson(Exception e) => (JsonObject)typeof(UaCommands).GetMethod("ExceptionJson", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { e, null })!;

        static object[] ConvertArgs(MethodInfo m, object[] args)
        {
            var ps = m.GetParameters();
            var res = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                var v = i < args.Length ? args[i] : (ps[i].HasDefaultValue ? ps[i].DefaultValue : null);
                if (v != null && !ps[i].ParameterType.IsInstanceOfType(v))
                    try { v = Convert.ChangeType(v, ps[i].ParameterType, System.Globalization.CultureInfo.InvariantCulture); } catch { }
                res[i] = v;
            }
            return res;
        }

        /// <summary>Call a test / setup method; IEnumerator results are driven frame by frame. Returns null on success.</summary>
        static (string kind, string message, Exception ex)? Invoke(object fixture, MethodInfo m, object[] args, bool editMode, int frameTimeout)
        {
            try
            {
                var r = m.Invoke(m.IsStatic ? null : fixture, args);
                if (r is IEnumerator e) Drive(e, editMode, frameTimeout);
                return null;
            }
            catch (TargetInvocationException tie) { return Classify(tie.InnerException ?? tie); }
            catch (Exception e) { return Classify(e); }
        }

        static void Drive(IEnumerator root, bool editMode, int frameTimeout)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(root);
            int frames = 0;
            while (stack.Count > 0)
            {
                if (++frames > frameTimeout) throw new TimeoutException($"[UnityTest] did not finish within {frameTimeout} frames");
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                var y = top.Current;
                if (y is IEnumerator nested && y is not CustomYieldInstruction) { stack.Push(nested); continue; }
                if (y is WaitForSeconds ws && Play.IsPlaying)
                {
                    double until = Play.Time + ws.m_Seconds;
                    while (Play.Time < until && frames++ < frameTimeout) Play.Step();
                    continue;
                }
                if (y is WaitForFixedUpdate && Play.IsPlaying)
                {
                    // resumes after the next fixed step (FixedUpdate + physics), stepping frames until one ran
                    long target = Play.FixedSteps + 1;
                    while (Play.FixedSteps < target && frames++ < frameTimeout) Play.Step();
                    continue;
                }
                if (y is AsyncOperation ao)
                {
                    // resumes on the first frame after it completed; real I/O (UnityWebRequest) runs on wall time, so frames are paced
                    while (!ao.isDone && frames++ < frameTimeout) { if (Play.IsPlaying) Play.Step(); System.Threading.Thread.Sleep(1); }
                    if (Play.IsPlaying) Play.Step();
                    continue;
                }
                if (y is CustomYieldInstruction cy)
                {
                    while (cy.keepWaiting && frames++ < frameTimeout) { if (Play.IsPlaying) Play.Step(); }
                    continue;
                }
                if (Play.IsPlaying) Play.Step();
            }
        }

        static (string kind, string message, Exception ex) Classify(Exception e)
        {
            var n = e.GetType().Name;
            return n switch
            {
                "SuccessException" => ("Passed", null, null),
                "IgnoreException" => ("Skipped", e.Message, null),
                "InconclusiveException" => ("Inconclusive", e.Message, null),
                "AssertionException" or "MultipleAssertException" => ("Failed", e.Message.Trim(), e),
                _ => ("Failed", $"{e.GetType().FullName}: {e.Message}", e),
            };
        }

        // ---- LogAssert: the Test Framework's LogScope does the bookkeeping (expected logs, unhandled errors)
        static object LogScopeBegin()
        {
            var t = TypeCache.FindType("UnityEngine.TestTools.Logging.LogScope");
            if (t == null) return new List<string>();
            try { return Activator.CreateInstance(t, true); } catch { return new List<string>(); }
        }

        static string LogScopeEnd(object scope)
        {
            if (scope is List<string>)
            {
                var errors = Play.Log.Where(l => l.Type is "Error" or "Exception").ToList();
                return errors.Count > 0 ? "Unhandled log message: '" + errors[0].Message + "'" : null;
            }
            var t = scope.GetType();
            string fail = null;
            try
            {
                t.GetMethod("ProcessExpectedLogs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(scope, null);
                if (t.GetMethod("AnyFailingLogs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(scope, null) is true)
                {
                    var failing = t.GetProperty("FailingLogs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(scope) as IEnumerable;
                    var first = failing?.Cast<object>().FirstOrDefault();
                    fail = "Unhandled log message: " + first;
                }
                if (fail == null && t.GetProperty("ExpectedLogs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(scope) is ICollection exp && exp.Count > 0)
                    fail = "Expected log did not appear: " + exp.Cast<object>().First();
            }
            catch (Exception e) { fail = "LogScope: " + (e.InnerException ?? e).Message; }
            finally { try { (scope as IDisposable)?.Dispose(); } catch { } }
            return fail;
        }
    }

    public static class LogAnalyzer
    {
        static readonly Regex CompileError = new(@"^(?<file>[^(]+\.cs)\((?<line>\d+),(?<col>\d+)\): (?<sev>error|warning) (?<id>CS\d+): (?<msg>.*)$");
        static readonly Regex ExceptionHead = new(@"^(?<type>[A-Za-z_][\w.]*(Exception|Error))(: (?<msg>.*))?$");
        static readonly Regex StackFrame = new(@"\(at (?<file>[^:)]+):(?<line>\d+)\)");

        public static JsonObject Analyze(string[] lines, int max)
        {
            var compile = new List<JsonObject>();
            var exceptions = new Dictionary<string, (JsonObject o, int count)>();
            int errors = 0, warnings = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i];
                var cm = CompileError.Match(l);
                if (cm.Success)
                {
                    if (compile.Any(c => c["file"]!.GetValue<string>() == cm.Groups["file"].Value && c["line"]!.GetValue<int>() == int.Parse(cm.Groups["line"].Value) && c["id"]!.GetValue<string>() == cm.Groups["id"].Value)) continue;
                    compile.Add(new JsonObject { ["severity"] = cm.Groups["sev"].Value, ["id"] = cm.Groups["id"].Value, ["file"] = cm.Groups["file"].Value, ["line"] = int.Parse(cm.Groups["line"].Value), ["column"] = int.Parse(cm.Groups["col"].Value), ["message"] = cm.Groups["msg"].Value });
                    continue;
                }
                var em = ExceptionHead.Match(l.Trim());
                if (em.Success && l.Length < 2000)
                {
                    var stack = new JsonArray(); string firstFile = null; int firstLine = 0;
                    for (int k = i + 1; k < Math.Min(lines.Length, i + 40); k++)
                    {
                        var s = lines[k].Trim();
                        if (s.Length == 0) break;
                        if (!(s.Contains('(') || s.StartsWith("at ") || s.Contains("UnityEngine.") || s.Contains(":"))) break;
                        stack.Add(JsonValue.Create(s));
                        var fm = StackFrame.Match(s);
                        if (fm.Success && firstFile == null && fm.Groups["file"].Value.StartsWith("Assets/")) { firstFile = fm.Groups["file"].Value; firstLine = int.Parse(fm.Groups["line"].Value); }
                    }
                    if (stack.Count == 0 && !l.Contains("Exception")) continue;
                    var key = em.Groups["type"].Value + "|" + em.Groups["msg"].Value + "|" + firstFile + firstLine;
                    if (exceptions.TryGetValue(key, out var have)) { exceptions[key] = (have.o, have.count + 1); continue; }
                    var o = new JsonObject { ["type"] = em.Groups["type"].Value, ["message"] = em.Groups["msg"].Value, ["file"] = firstFile, ["line"] = firstFile != null ? JsonValue.Create(firstLine) : null, ["stack"] = stack };
                    exceptions[key] = (o, 1);
                    continue;
                }
                if (Regex.IsMatch(l, @"\b(error|Error|ERROR)\b|^Failed to |^Assertion failed")) errors++;
                else if (Regex.IsMatch(l, @"\b(warning|Warning|WARNING)\b")) warnings++;
            }
            return new JsonObject
            {
                ["lines"] = lines.Length,
                ["compileErrors"] = new JsonArray(compile.Where(c => c["severity"]!.GetValue<string>() == "error").Take(max).Select(c => (JsonNode)c).ToArray()),
                ["compileWarnings"] = compile.Count(c => c["severity"]!.GetValue<string>() == "warning"),
                ["exceptions"] = new JsonArray(exceptions.Values.OrderByDescending(v => v.count).Take(max).Select(v => { v.o["count"] = v.count; return (JsonNode)v.o; }).ToArray()),
                ["errorLines"] = errors, ["warningLines"] = warnings,
            };
        }
    }
}
