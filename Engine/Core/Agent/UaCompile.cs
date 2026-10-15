// C# compilation of a Unity project without the Editor, with Unity's own rules:
//   assemblies from .asmdef / .asmref (+ defineConstraints, versionDefines, references by name or GUID, overrideReferences),
//   predefined Assembly-CSharp(-firstpass)(-Editor), package assemblies (embedded, Library/PackageCache, Editor built-ins),
//   references = Unity Editor managed DLLs (exact API surface) + .NET Standard 2.1 + precompiled plugin DLLs,
//   defines = Unity version / platform / scripting define symbols / input handler / test framework.
// Output: per-assembly diagnostics (file, line, column, id, message) and DLL + PDB in Library/MiniUnity/ScriptAssemblies.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using MiniUnity.Yaml;

namespace MiniUnity.Agent
{
    public sealed class UAssembly
    {
        public string Name, Dir, AsmdefPath, Guid;
        public bool Editor, Unsafe, AutoReferenced = true, NoEngineRefs, OverrideRefs, Predefined, FromPackage, IsTest;
        public string Package;  // package name when inside a package
        public List<string> RefNames = new(), PrecompiledRefs = new(), DefineConstraints = new(), Files = new();
        public List<(string name, string expr, string define)> VersionDefines = new();
        public List<string> IncludePlatforms = new(), ExcludePlatforms = new();
        public List<UAssembly> Refs = new();
        public string OutputPath;
        public List<Diagnostic> Diagnostics = new();
        public bool Excluded; public string ExcludedWhy;
        public bool Built, Failed, Cached;
        public string Skipped;   // not compiled because this referenced assembly failed
        public List<JsonObject> CachedDiagnostics = new();
    }

    public static class UnityEditorInstall
    {
        public static string Root
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("UNITY_EDITOR");
                var v = EditorVersion();
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var cache = Environment.GetEnvironmentVariable("MU_CACHE");   // setup.sh --download-unity puts the Editor subset here
                foreach (var c in new[] { env, cache == null ? null : Path.Combine(cache, "unity", v, "Editor"), DownloadedNextToBuild(v), Path.Combine(home, "Unity/Hub/Editor", v, "Editor"),
                                          "/opt/unity/Editor", "/Applications/Unity/Hub/Editor/" + v + "/Unity.app/Contents", "C:/Program Files/Unity/Hub/Editor/" + v + "/Editor" })
                    if (!string.IsNullOrEmpty(c) && (Directory.Exists(Path.Combine(c, "Data/Managed")) || Directory.Exists(Path.Combine(c, "Managed")))) return c;
                return null;
            }
        }
        /// <summary>The Editor subset of setup.sh --download-unity, found from the running build when MU_CACHE is not set (tests and
        /// tools that start the DLL directly): MiniUnity/Engine/bin/… or a host's bin/… → MiniUnity/.cache/unity/&lt;version&gt;/Editor.</summary>
        static string DownloadedNextToBuild(string version)
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                foreach (var c in new[] { Path.Combine(d.FullName, ".cache/unity", version, "Editor"), Path.Combine(d.FullName, "MiniUnity/.cache/unity", version, "Editor") })
                    if (Directory.Exists(Path.Combine(c, "Data/Managed"))) return c;
            return null;
        }

        public static string Data => Root == null ? null : Directory.Exists(Path.Combine(Root, "Data")) ? Path.Combine(Root, "Data") : Root;
        public static string EditorVersion()
        {
            var pv = Path.Combine(Ua.Root, "ProjectSettings/ProjectVersion.txt");
            if (!File.Exists(pv)) return "2022.3.0f1";
            var m = Regex.Match(File.ReadAllText(pv), @"m_EditorVersion: (\S+)");
            return m.Success ? m.Groups[1].Value : "2022.3.0f1";
        }
        public static string BuiltInPackages => Data == null ? null : Path.Combine(Data, "Resources/PackageManager/BuiltInPackages");
    }

    public static class ProjectCompiler
    {
        public static string OutDir => Ua.Abs("Library/MiniUnity/ScriptAssemblies");

        // ------------------------------------------------------------------ discovery
        public sealed class ProjectModel
        {
            public List<UAssembly> Assemblies = new();
            public Dictionary<string, string> PackageDirs = new(StringComparer.Ordinal);   // name → abs dir
            public Dictionary<string, string> PackageVersions = new(StringComparer.Ordinal);
            public HashSet<string> Defines = new(StringComparer.Ordinal);
            public List<string> PluginDlls = new();                         // auto-referenced
            public List<(string path, string package)> ExplicitPlugins = new(); // isExplicitlyReferenced: 1
            public List<string> Warnings = new();
            public string Platform;
            public HashSet<string> Testables = new();
            public bool Net48;   // PlayerSettings apiCompatibilityLevel = .NET Framework
        }

        public static ProjectModel Discover(string platform = null)
        {
            var m = new ProjectModel { Platform = platform ?? "Standalone" };
            var warn = new List<string>();
            SortedDictionary<string, UaCommands.Resolved> pkgs;
            try { pkgs = UaCommands.Resolve(true, warn); } catch (Exception e) { pkgs = new(); warn.Add("packages: " + e.Message); }
            m.Warnings.AddRange(warn);
            var manifest = JsonNode.Parse(File.Exists(Ua.Abs("Packages/manifest.json")) ? File.ReadAllText(Ua.Abs("Packages/manifest.json")) : "{}");
            if (manifest?["testables"] is JsonArray ta) foreach (var t in ta) m.Testables.Add(t!.GetValue<string>());
            foreach (var p in pkgs.Values)
            {
                string dir = null;
                if (p.Source == "embedded") dir = Path.Combine(Ua.Abs("Packages"), p.Version[5..]);
                else if (p.Source == "local") dir = Path.GetFullPath(Path.Combine(Ua.Abs("Packages"), p.Version[5..]));
                else if (p.Source == "builtin" && UnityEditorInstall.BuiltInPackages is string bi && Directory.Exists(Path.Combine(bi, p.Name))) dir = Path.Combine(bi, p.Name);
                else
                {
                    var cache = Ua.Abs("Library/PackageCache");
                    if (Directory.Exists(cache)) dir = Directory.EnumerateDirectories(cache, p.Name + "@*").OrderByDescending(d => d == Path.Combine(cache, p.Name + "@" + p.Version)).FirstOrDefault();
                }
                if (dir == null) { if (p.Source != "builtin") m.Warnings.Add($"package {p.Name}@{p.Version} has no sources (run: pkg.resolve --fetch)"); continue; }
                m.PackageDirs[p.Name] = dir; m.PackageVersions[p.Name] = p.Version;
            }
            BaseDefines(m);

            // asmdefs + asmrefs
            var asmdefs = new List<(string file, string pkg)>();
            var asmrefs = new List<(string file, string pkg)>();
            void Scan(string root, string pkg)
            {
                foreach (var f in Directory.EnumerateFiles(root, "*.asmdef", SearchOption.AllDirectories)) if (!Hidden(root, f)) asmdefs.Add((f, pkg));
                foreach (var f in Directory.EnumerateFiles(root, "*.asmref", SearchOption.AllDirectories)) if (!Hidden(root, f)) asmrefs.Add((f, pkg));
            }
            Scan(Ua.Abs("Assets"), null);
            foreach (var kv in m.PackageDirs) Scan(kv.Value, kv.Key);
            var byDir = new Dictionary<string, UAssembly>(StringComparer.Ordinal);
            foreach (var (file, pkg) in asmdefs)
            {
                var j = LooseJson(File.ReadAllText(file));
                var a = new UAssembly { Name = j["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(file), Dir = Path.GetDirectoryName(file)!, AsmdefPath = file, Package = pkg, FromPackage = pkg != null };
                a.Guid = MetaGuid(file);
                foreach (var r in j["references"] as JsonArray ?? new JsonArray()) a.RefNames.Add(r!.GetValue<string>());
                foreach (var r in j["precompiledReferences"] as JsonArray ?? new JsonArray()) a.PrecompiledRefs.Add(r!.GetValue<string>());
                foreach (var r in j["defineConstraints"] as JsonArray ?? new JsonArray()) a.DefineConstraints.Add(r!.GetValue<string>());
                foreach (var r in j["includePlatforms"] as JsonArray ?? new JsonArray()) a.IncludePlatforms.Add(r!.GetValue<string>());
                foreach (var r in j["excludePlatforms"] as JsonArray ?? new JsonArray()) a.ExcludePlatforms.Add(r!.GetValue<string>());
                foreach (var v in j["versionDefines"] as JsonArray ?? new JsonArray())
                    a.VersionDefines.Add((v!["name"]!.GetValue<string>(), v["expression"]?.GetValue<string>() ?? "", v["define"]!.GetValue<string>()));
                a.Unsafe = j["allowUnsafeCode"]?.GetValue<bool>() ?? false;
                a.AutoReferenced = j["autoReferenced"]?.GetValue<bool>() ?? true;
                a.NoEngineRefs = j["noEngineReferences"]?.GetValue<bool>() ?? false;
                a.OverrideRefs = j["overrideReferences"]?.GetValue<bool>() ?? false;
                a.Editor = a.IncludePlatforms.Count == 1 && a.IncludePlatforms[0] == "Editor";
                a.IsTest = a.DefineConstraints.Contains("UNITY_INCLUDE_TESTS")
                           || (j["optionalUnityReferences"] as JsonArray)?.Any(x => x?.GetValue<string>() == "TestAssemblies") == true;
                m.Assemblies.Add(a);
                byDir[a.Dir] = a;
            }
            var byName = m.Assemblies.GroupBy(a => a.Name).ToDictionary(g => g.Key, g => g.First());
            var byGuid = m.Assemblies.Where(a => a.Guid != null).ToDictionary(a => a.Guid, a => a);
            foreach (var (file, pkg) in asmrefs)
            {
                var r = LooseJson(File.ReadAllText(file))["reference"]?.GetValue<string>() ?? "";
                var target = r.StartsWith("GUID:") ? byGuid.GetValueOrDefault(r[5..]) : byName.GetValueOrDefault(r);
                if (target != null) byDir[Path.GetDirectoryName(file)!] = target; else m.Warnings.Add($"{Ua.Rel(file)}: unknown assembly {r}");
            }

            // predefined assemblies
            UAssembly Pre(string name, bool editor) { var a = new UAssembly { Name = name, Editor = editor, Predefined = true, Dir = Ua.Abs("Assets") }; m.Assemblies.Add(a); return a; }
            var csharp = Pre("Assembly-CSharp", false); var csharpEditor = Pre("Assembly-CSharp-Editor", true);
            var first = Pre("Assembly-CSharp-firstpass", false); var firstEditor = Pre("Assembly-CSharp-Editor-firstpass", true);

            // scripts → assemblies (nearest asmdef / asmref folder wins)
            void Assign(string root, string pkg)
            {
                foreach (var f in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    if (Hidden(root, f)) continue;
                    UAssembly owner = null;
                    for (var d = Path.GetDirectoryName(f); d != null && d.Length >= root.Length; d = Path.GetDirectoryName(d))
                        if (byDir.TryGetValue(d, out owner)) break;
                    if (owner == null)
                    {
                        if (pkg != null) continue;   // package scripts outside an asmdef are not compiled
                        var rel = Ua.Rel(f);
                        bool editor = rel.Split('/').Contains("Editor");
                        bool firstpass = rel.StartsWith("Assets/Plugins/") || rel.StartsWith("Assets/Standard Assets/") || rel.StartsWith("Assets/Pro Standard Assets/");
                        owner = firstpass ? (editor ? firstEditor : first) : (editor ? csharpEditor : csharp);
                    }
                    owner.Files.Add(f);
                }
            }
            Assign(Ua.Abs("Assets"), null);
            foreach (var kv in m.PackageDirs) Assign(kv.Value, kv.Key);

            // precompiled plugin DLLs (managed only)
            foreach (var (root, pkg) in new[] { (Ua.Abs("Assets"), (string)null) }.Concat(m.PackageDirs.Select(kv => (kv.Value, kv.Key))))
                foreach (var dll in Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories))
                {
                    if (Hidden(root, dll) || !IsManaged(dll) || !PluginUsable(dll, out bool explicitRef)) continue;
                    if (explicitRef) m.ExplicitPlugins.Add((dll, pkg)); else m.PluginDlls.Add(dll);
                }

            // exclusions: constraints / platforms / untestable package tests / empty
            foreach (var a in m.Assemblies)
            {
                var defs = AssemblyDefines(m, a);
                if (a.Files.Count == 0) { a.Excluded = true; a.ExcludedWhy = "no scripts"; continue; }
                if (!a.Predefined && a.IncludePlatforms.Count > 0 && !a.IncludePlatforms.Contains("Editor")) { a.Excluded = true; a.ExcludedWhy = "not compiled for the Editor (includePlatforms)"; continue; }
                if (a.ExcludePlatforms.Contains("Editor")) { a.Excluded = true; a.ExcludedWhy = "excludePlatforms: Editor"; continue; }
                var bad = a.DefineConstraints.FirstOrDefault(c => !EvalConstraint(c, defs));
                if (bad != null) { a.Excluded = true; a.ExcludedWhy = "defineConstraints: " + bad; continue; }
                if (a.IsTest && a.Package != null && !m.Testables.Contains(a.Package)) { a.Excluded = true; a.ExcludedWhy = "package tests (not in manifest testables)"; }
            }
            // references
            var live = m.Assemblies.Where(a => !a.Excluded).ToList();
            var liveByName = live.GroupBy(a => a.Name).ToDictionary(g => g.Key, g => g.First());
            var liveByGuid = live.Where(a => a.Guid != null).GroupBy(a => a.Guid).ToDictionary(g => g.Key, g => g.First());
            foreach (var a in live)
            {
                if (a.Predefined)
                {
                    a.Refs.AddRange(live.Where(x => !x.Predefined && x.AutoReferenced && (a.Editor || !x.Editor) && !x.IsTest));
                    if (a == csharp || a == csharpEditor) a.Refs.Add(first);
                    if (a == csharpEditor) { a.Refs.Add(csharp); a.Refs.Add(firstEditor); }
                    if (a == firstEditor) a.Refs.Add(first);
                    a.Refs.RemoveAll(r => r.Excluded || r == a);
                }
                else
                    foreach (var r in a.RefNames)
                    {
                        var t = r.StartsWith("GUID:") ? liveByGuid.GetValueOrDefault(r[5..]) : liveByName.GetValueOrDefault(r);
                        if (t != null && t != a) a.Refs.Add(t);
                        else if (t == null && !m.Assemblies.Any(x => x.Name == r || (r.StartsWith("GUID:") && x.Guid == r[5..])))
                            m.Warnings.Add($"{a.Name}: reference {r} not found");
                    }
                // uGUI is referenced implicitly by every assembly that has engine references (as in the Editor)
                if (!a.NoEngineRefs)
                    foreach (var ui in a.Editor ? new[] { "UnityEngine.UI", "UnityEditor.UI" } : new[] { "UnityEngine.UI" })
                        if (liveByName.TryGetValue(ui, out var uia) && uia != a && !a.Refs.Contains(uia) && !uia.Refs.Contains(a) && !(uia.Name == "UnityEditor.UI" && a.Name == "UnityEngine.UI"))
                            a.Refs.Add(uia);
                // Editor assemblies also see the Test Framework API implicitly (UnityEditor.TestTools.TestRunner.Api …)
                if (a.Editor && !a.NoEngineRefs && a.Package != "com.unity.test-framework" && a.Package != "com.unity.ext.nunit")
                    foreach (var tn in new[] { "UnityEngine.TestRunner", "UnityEditor.TestRunner" })
                        if (liveByName.TryGetValue(tn, out var tr) && !a.Refs.Contains(tr) && !DependsOn(tr, a)) a.Refs.Add(tr);
                // test assemblies get the test framework + nunit
                if (a.IsTest || a.RefNames.Contains("UnityEngine.TestRunner") || a.RefNames.Contains("UnityEditor.TestRunner"))
                    foreach (var tn in new[] { "UnityEngine.TestRunner", "UnityEditor.TestRunner" })
                        if (liveByName.TryGetValue(tn, out var tr) && (a.Editor || tn == "UnityEngine.TestRunner") && !a.Refs.Contains(tr) && tr != a) a.Refs.Add(tr);
            }
            return m;
        }

        /// <summary>JSON as Unity's parser accepts it: comments, trailing commas, duplicate keys (last wins).</summary>
        public static JsonObject LooseJson(string text)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text, new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
            JsonNode Conv(System.Text.Json.JsonElement e) => e.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Object => Obj(e),
                System.Text.Json.JsonValueKind.Array => new JsonArray(e.EnumerateArray().Select(Conv).ToArray()),
                _ => JsonNode.Parse(e.GetRawText()),
            };
            JsonObject Obj(System.Text.Json.JsonElement e) { var o = new JsonObject(); foreach (var p in e.EnumerateObject()) o[p.Name] = Conv(p.Value); return o; }
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object ? Obj(doc.RootElement) : new JsonObject();
        }

        static bool DependsOn(UAssembly a, UAssembly target, HashSet<UAssembly> seen = null)
        {
            seen ??= new HashSet<UAssembly>();
            if (!seen.Add(a)) return false;
            return a.Refs.Any(r => r == target || DependsOn(r, target, seen));
        }

        static bool Hidden(string root, string path)
        {
            var rel = Path.GetRelativePath(root, path);
            return rel.Split(Path.DirectorySeparatorChar).Any(s => s.StartsWith(".") || s.EndsWith("~") || s == "cvs");
        }

        static string MetaGuid(string file)
        {
            var meta = file + ".meta";
            if (!File.Exists(meta)) return null;
            var m = Regex.Match(File.ReadAllText(meta), @"guid: ([0-9a-f]{32})");
            return m.Success ? m.Groups[1].Value : null;
        }

        static bool IsManaged(string dll)
        {
            try { using var s = File.OpenRead(dll); using var pe = new PEReader(s); return pe.HasMetadata; } catch { return false; }
        }

        /// <summary>Plugin importer: usable in the Editor and auto-referenced (validateReferences / isExplicitlyReferenced honoured loosely).</summary>
        static bool PluginUsable(string dll, out bool explicitRef)
        {
            explicitRef = false;
            var meta = dll + ".meta";
            if (!File.Exists(meta)) return true;
            var t = File.ReadAllText(meta);
            explicitRef = Regex.IsMatch(t, @"isExplicitlyReferenced: 1");
            var ed = Regex.Match(t, @"- first:\s*\n\s*Editor: Editor\s*\n\s*second:\s*\n\s*enabled: (\d)");
            var any = Regex.Match(t, @"- first:\s*\n\s*Any: \s*\n\s*second:\s*\n\s*enabled: (\d)");
            if (ed.Success) return ed.Groups[1].Value == "1" || (any.Success && any.Groups[1].Value == "1");
            return true;
        }

        // ------------------------------------------------------------------ defines
        static void BaseDefines(ProjectModel m)
        {
            var d = m.Defines;
            var ver = UnityEditorInstall.EditorVersion();
            var vm = Regex.Match(ver, @"^(\d+)\.(\d+)\.(\d+)");
            int major = int.Parse(vm.Groups[1].Value), minor = int.Parse(vm.Groups[2].Value), patch = int.Parse(vm.Groups[3].Value);
            d.Add($"UNITY_{major}"); d.Add($"UNITY_{major}_{minor}"); d.Add($"UNITY_{major}_{minor}_{patch}");
            foreach (var mi in new[] { 3, 4, 5, 6 }) d.Add($"UNITY_5_{mi}_OR_NEWER");
            for (int y = 2017; y <= major && y < 3000; y++)
                for (int mi = 1; mi <= (y == major ? minor : y >= 2020 ? 3 : 4); mi++) d.Add($"UNITY_{y}_{mi}_OR_NEWER");
            foreach (var x in new[] { "UNITY_EDITOR", "UNITY_ASSERTIONS", "ENABLE_MONO", "NET_STANDARD_2_1", "NET_STANDARD", "NETSTANDARD2_1", "NETSTANDARD", "CSHARP_7_3_OR_NEWER",
                "DEBUG", "TRACE", "UNITY_SERVER_FALSE", "ENABLE_AUDIO", "ENABLE_CLOTH", "ENABLE_PHYSICS", "ENABLE_UNITYEVENTS", "ENABLE_CLOUD_SERVICES", "ENABLE_UNET",
                "UNITY_TEAM_LICENSE", "ENABLE_MANAGED_JOBS", "ENABLE_BURST_AOT", "ENABLE_UNITY_COLLECTIONS_CHECKS", "ENABLE_TEXTURE_STREAMING", "ENABLE_RUNTIME_GI" }) d.Add(x);
            d.Add(OperatingSystem.IsWindows() ? "UNITY_EDITOR_WIN" : OperatingSystem.IsMacOS() ? "UNITY_EDITOR_OSX" : "UNITY_EDITOR_LINUX");
            var plat = m.Platform.ToLowerInvariant();
            string group = "Standalone";
            if (plat.StartsWith("android")) { d.Add("UNITY_ANDROID"); d.Add("UNITY_ANDROID_API"); group = "Android"; }
            else if (plat.StartsWith("ios")) { d.Add("UNITY_IOS"); d.Add("UNITY_IPHONE"); group = "iPhone"; }
            else if (plat.StartsWith("webgl")) { d.Add("UNITY_WEBGL"); group = "WebGL"; }
            else { d.Add("UNITY_STANDALONE"); d.Add(OperatingSystem.IsWindows() ? "UNITY_STANDALONE_WIN" : OperatingSystem.IsMacOS() ? "UNITY_STANDALONE_OSX" : "UNITY_STANDALONE_LINUX"); }
            if (plat.StartsWith("android") || plat.StartsWith("ios")) d.Add("UNITY_MOBILE");
            // PlayerSettings: scripting define symbols for the group, input handler
            var ps = Ua.Abs("ProjectSettings/ProjectSettings.asset");
            if (File.Exists(ps))
            {
                var body = YamlReader.ReadFile(ps).FirstOrDefault()?.Body;
                if (body?["scriptingDefineSymbols"] is YMap sds && sds[group] is YScalar sym)
                    foreach (var s in sym.Value.Split(';', StringSplitOptions.RemoveEmptyEntries)) d.Add(s.Trim());
                m.Net48 = Serializer.Int(body?["apiCompatibilityLevel"], 6) == 3;
                int input = Serializer.Int(body?["activeInputHandler"], 0);
                if (input is 0 or 2) d.Add("ENABLE_LEGACY_INPUT_MANAGER");
                if (input is 1 or 2) d.Add("ENABLE_INPUT_SYSTEM");
            }
            if (m.PackageDirs.ContainsKey("com.unity.test-framework")) d.Add("UNITY_INCLUDE_TESTS");
        }

        public static HashSet<string> AssemblyDefines(ProjectModel m, UAssembly a)
        {
            var d = new HashSet<string>(m.Defines);
            foreach (var (name, expr, define) in a.VersionDefines)
            {
                string have = name == "Unity" ? Regex.Match(UnityEditorInstall.EditorVersion(), @"^[\d.]+").Value : m.PackageVersions.GetValueOrDefault(name);
                if (have == null && name != "Unity") continue;
                if (have != null && (have.StartsWith("file:") || have.Contains(".git") || VersionInRange(have, expr))) d.Add(define);
            }
            return d;
        }

        /// <summary>Unity version define expressions: "1.2.3" (≥), "[1.0,2.0)", "(,3.0]", "[1.5]".</summary>
        public static bool VersionInRange(string v, string expr)
        {
            expr = expr.Trim();
            if (expr.Length == 0) return true;
            int C(string a, string b) => UaCommands.CompareVersions(a.Trim(), b.Trim());
            if (expr[0] is '[' or '(')
            {
                bool lo = expr[0] == '[', hi = expr[^1] == ']';
                var inner = expr[1..^1];
                var parts = inner.Split(',');
                if (parts.Length == 1) return C(v, parts[0]) == 0;
                if (parts[0].Trim().Length > 0 && (lo ? C(v, parts[0]) < 0 : C(v, parts[0]) <= 0)) return false;
                if (parts[1].Trim().Length > 0 && (hi ? C(v, parts[1]) > 0 : C(v, parts[1]) >= 0)) return false;
                return true;
            }
            return C(v, expr) >= 0;
        }

        public static bool EvalConstraint(string expr, HashSet<string> defs)
        {
            // "A || B", "!A" (Unity supports only || and !)
            foreach (var alt in expr.Split("||"))
            {
                var t = alt.Trim();
                bool neg = t.StartsWith("!");
                var name = neg ? t[1..].Trim() : t;
                if (defs.Contains(name) != neg) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ references
        static List<MetadataReference> engineRuntime, engineEditor, netstd, net48;
        static MetadataReference codegen;
        static readonly Dictionary<string, MetadataReference> refCache = new();
        static MetadataReference Ref(string path) => refCache.TryGetValue(path, out var r) ? r : refCache[path] = MetadataReference.CreateFromFile(path);

        static void EnsureEngineRefs()
        {
            if (netstd != null) return;
            var data = UnityEditorInstall.Data ?? throw new UaError("Unity Editor not found (set UNITY_EDITOR=<…/Editor>): needed for the UnityEngine / UnityEditor reference assemblies");
            var managed = Path.Combine(data, "Managed/UnityEngine");
            var all = Directory.EnumerateFiles(managed, "*.dll").ToList();
            engineRuntime = all.Where(f => Path.GetFileName(f).StartsWith("UnityEngine.")).Select(Ref).ToList();
            engineEditor = all.Where(f => Path.GetFileName(f).StartsWith("UnityEngine.") || Path.GetFileName(f).StartsWith("UnityEditor.")).Select(Ref).ToList();
            var cpc = Path.Combine(data, "Managed/Unity.CompilationPipeline.Common.dll");
            codegen = File.Exists(cpc) ? Ref(cpc) : null;
            var ue = Path.Combine(data, "Managed/UnityEditor.Graphs.dll");
            if (File.Exists(ue)) engineEditor.Add(Ref(ue));
            // runtime assemblies (.NET Standard 2.1 profile): netstandard + compat shims, like Unity's generated .csproj
            netstd = new List<MetadataReference> { Ref(Path.Combine(data, "NetStandard/ref/2.1.0/netstandard.dll")) };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "netstandard.dll" };
            foreach (var dir in new[] { "NetStandard/compat/2.1.0/shims/netfx", "NetStandard/compat/2.1.0/shims/netstandard", "NetStandard/Extensions/2.0.0" })
                if (Directory.Exists(Path.Combine(data, dir)))
                    foreach (var f in Directory.EnumerateFiles(Path.Combine(data, dir), "*.dll")) if (seen.Add(Path.GetFileName(f))) netstd.Add(Ref(f));
            // Editor assemblies (and NET_Unity_4_8 projects): Unity's .NET Framework 4.8 reference set
            var api48 = Path.Combine(data, "UnityReferenceAssemblies/unity-4.8-api");
            if (!Directory.Exists(api48)) api48 = Path.Combine(data, "MonoBleedingEdge/lib/mono/4.7.1-api");
            net48 = new List<MetadataReference>();
            var seen48 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // the subset Unity puts in its generated .csproj (not every Mono assembly: e.g. cscompmgd.dll would make CompilerError ambiguous)
            var core48 = new[] { "mscorlib", "System", "System.Core", "System.Runtime.Serialization", "System.Xml", "System.Xml.Linq", "System.Numerics",
                "System.Numerics.Vectors", "System.Net.Http", "System.IO.Compression", "Microsoft.CSharp", "System.Data", "System.Data.DataSetExtensions",
                "System.Drawing", "System.IO.Compression.FileSystem", "System.ComponentModel.Composition", "System.Transactions", "System.Configuration",
                "System.Security", "System.ServiceModel.Internals", "System.Web", "System.Runtime", "System.Net" };
            foreach (var n in core48) { var f = Path.Combine(api48, n + ".dll"); if (File.Exists(f) && seen48.Add(n + ".dll")) net48.Add(Ref(f)); }
            if (Directory.Exists(Path.Combine(api48, "Facades")))
                foreach (var f in Directory.EnumerateFiles(Path.Combine(api48, "Facades"), "*.dll")) if (seen48.Add(Path.GetFileName(f))) net48.Add(Ref(f));
        }

        // ------------------------------------------------------------------ build
        public sealed class BuildResult
        {
            public List<UAssembly> Order = new();
            public int Errors, Warnings;
            public double Seconds;
        }

        public static BuildResult Build(ProjectModel m, IEnumerable<string> only = null, bool emit = true)
        {
            EnsureEngineRefs();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var live = m.Assemblies.Where(a => !a.Excluded).ToList();
            // topological order
            var order = new List<UAssembly>();
            var state = new Dictionary<UAssembly, int>();
            void Visit(UAssembly a)
            {
                if (state.TryGetValue(a, out var s)) { if (s == 1) m.Warnings.Add("cyclic assembly reference at " + a.Name); return; }
                state[a] = 1;
                foreach (var r in a.Refs) Visit(r);
                state[a] = 2; order.Add(a);
            }
            var targets = only != null ? live.Where(a => only.Contains(a.Name)).ToList() : live;
            foreach (var a in targets) Visit(a);
            Directory.CreateDirectory(OutDir);
            var res = new BuildResult { Order = order };
            var pluginRefs = m.PluginDlls.Select(Ref).ToList();
            foreach (var a in order)
            {
                var defs = AssemblyDefines(m, a);
                bool editorProfile = a.Editor || (a.Predefined && a.Name.Contains("Editor")) || m.Net48;
                var refs = new List<MetadataReference>(editorProfile ? net48 : netstd);
                if (editorProfile) { defs.Remove("NET_STANDARD_2_1"); defs.Remove("NET_STANDARD"); defs.Remove("NETSTANDARD2_1"); defs.Remove("NETSTANDARD"); defs.Add("NET_4_6"); defs.Add("NET_UNITY_4_8"); }
                if (!a.NoEngineRefs) refs.AddRange(engineEditor);   // Editor compilation: runtime assemblies see UnityEditor too (#if UNITY_EDITOR)
                // ILPostProcessor assemblies (*.CodeGen) compile against Unity.CompilationPipeline.Common
                if (a.Name.EndsWith(".CodeGen") && codegen != null) refs.Add(codegen);
                var allPlugins = m.PluginDlls.Concat(m.ExplicitPlugins.Select(e => e.path));
                if (a.OverrideRefs) refs.AddRange(allPlugins.Where(p => a.PrecompiledRefs.Contains(Path.GetFileName(p))).Select(Ref));
                else
                {
                    refs.AddRange(pluginRefs);
                    // explicitly referenced plugins: by name, or shipped in the same package
                    refs.AddRange(m.ExplicitPlugins.Where(e => a.PrecompiledRefs.Contains(Path.GetFileName(e.path)) || (e.package != null && e.package == a.Package)).Select(e => Ref(e.path)));
                }
                // using the Test Framework brings NUnit with it
                if (a.Refs.Any(r => r.Name is "UnityEngine.TestRunner" or "UnityEditor.TestRunner") || a.IsTest)
                    foreach (var e in m.ExplicitPlugins.Where(e => Path.GetFileName(e.path) == "nunit.framework.dll")) if (!refs.Contains(Ref(e.path))) refs.Add(Ref(e.path));
                var brokenDep = a.Refs.FirstOrDefault(r => r.Failed || r.OutputPath == null || !File.Exists(r.OutputPath));
                if (brokenDep != null)
                {
                    // like Unity: dependents of a failed assembly are not compiled (no cascade of follow-up errors)
                    a.Failed = true; a.Skipped = brokenDep.Name; a.Built = true;
                    continue;
                }
                foreach (var r in a.Refs) refs.Add(MetadataReference.CreateFromFile(r.OutputPath));
                // incremental: same sources + references + defines → reuse the previous output and its diagnostics
                a.OutputPath = Path.Combine(OutDir, a.Name + ".dll");
                var stamp = Stamp(a, refs, defs);
                var stampFile = Path.ChangeExtension(a.OutputPath, ".stamp.json");
                if (emit && File.Exists(a.OutputPath) && File.Exists(stampFile))
                {
                    try
                    {
                        var st = JsonNode.Parse(File.ReadAllText(stampFile))!;
                        if (st["hash"]?.GetValue<string>() == stamp)
                        {
                            a.Cached = true; a.Built = true;
                            foreach (var dj in st["diagnostics"] as JsonArray ?? new JsonArray()) a.CachedDiagnostics.Add(dj!.AsObject());
                            res.Warnings += a.CachedDiagnostics.Count;
                            continue;
                        }
                    }
                    catch { }
                }
                var parse = new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: defs);
                var trees = a.Files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, path: f, encoding: Encoding.UTF8)).ToList();
                // Unity grants packages access to engine internals ([InternalsVisibleTo]); in MiniUnity's runtime those members live in
                // MiniUnity.dll, so the emitted DLL asks the runtime to skip access checks against it (ignored by Unity's own compiler)
                if (emit) trees.Add(CSharpSyntaxTree.ParseText(AccessShim, parse, path: AccessShimPath, encoding: Encoding.UTF8));
                var opts = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: a.Unsafe || a.Predefined, optimizationLevel: OptimizationLevel.Debug,
                    specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["CS1701"] = ReportDiagnostic.Suppress, ["CS1702"] = ReportDiagnostic.Suppress, ["CS1705"] = ReportDiagnostic.Suppress },
                    assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default, nullableContextOptions: NullableContextOptions.Disable);
                // deterministic: the same sources give the same bytes, so an unchanged API keeps dependents' stamps (see Stamp)
                var comp = CSharpCompilation.Create(a.Name, trees, refs, opts.WithDeterministic(true));
                a.OutputPath = Path.Combine(OutDir, a.Name + ".dll");
                if (emit)
                {
                    using var pe = new MemoryStream(); using var pdb = new MemoryStream(); using var api = new MemoryStream();
                    var emitOpts = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: a.Name + ".pdb", includePrivateMembers: false);
                    var er = comp.Emit(pe, pdb, metadataPEStream: api, options: emitOpts);
                    if (!er.Success && er.Diagnostics.Any(d => d.Id == "CS8357"))   // [AssemblyVersion("1.0.*")] is not deterministic
                    {
                        comp = comp.WithOptions(opts);
                        pe.SetLength(0); pdb.SetLength(0); api.SetLength(0);
                        er = comp.Emit(pe, pdb, metadataPEStream: api, options: emitOpts);
                    }
                    a.Diagnostics.AddRange(er.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning && d.Location.SourceTree?.FilePath != AccessShimPath && !(d.Id == "CS0436" && d.GetMessage().Contains("IgnoresAccessChecksTo"))));
                    if (er.Success)
                    {
                        SafeIO.Check(a.OutputPath);
                        File.WriteAllBytes(a.OutputPath, pe.ToArray());
                        File.WriteAllBytes(Path.ChangeExtension(a.OutputPath, ".pdb"), pdb.ToArray());
                        var st = new JsonObject
                        {
                            ["hash"] = stamp,
                            ["api"] = Convert.ToHexString(SHA256.HashData(api.ToArray())),   // reference assembly: what dependents compile against
                            ["diagnostics"] = new JsonArray(a.Diagnostics.Select(d => (JsonNode)DiagJson(d, a.Name)).ToArray()),
                        };
                        File.WriteAllText(stampFile, st.ToJsonString());
                    }
                    else a.Failed = true;   // the last good DLL stays (as Unity keeps Library/ScriptAssemblies)
                }
                else
                {
                    a.Diagnostics.AddRange(comp.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
                    a.Failed = a.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
                }
                a.Built = true;
                res.Errors += a.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
                res.Warnings += a.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
            }
            SourceHashes.Save();
            res.Seconds = sw.Elapsed.TotalSeconds;
            return res;
        }

        const string AccessShimPath = "MiniUnity/IgnoresAccessChecksTo.g.cs";
        const string AccessShim = "[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo(\"MiniUnity\")]\n" +
            "namespace System.Runtime.CompilerServices { [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)] " +
            "internal sealed class IgnoresAccessChecksToAttribute : System.Attribute { public IgnoresAccessChecksToAttribute(string assemblyName) { } } }\n";

        /// <summary>Inputs of an assembly build: source contents (not their mtimes: a touch or a branch switch with the same text
        /// rebuilds nothing), defines, and references: for a project assembly its API hash (reference assembly), so that a change
        /// inside a method body does not rebuild every dependent; other DLLs by size + mtime.</summary>
        static string Stamp(UAssembly a, List<MetadataReference> refs, HashSet<string> defs)
        {
            var sb = new StringBuilder("v3|");
            foreach (var f in a.Files.OrderBy(x => x, StringComparer.Ordinal)) sb.Append(f).Append('|').Append(SourceHashes.Of(f)).Append('\n');
            foreach (var d in defs.OrderBy(x => x, StringComparer.Ordinal)) sb.Append(d).Append(';');
            foreach (var r in refs.OfType<PortableExecutableReference>().Select(r => r.FilePath).OrderBy(x => x, StringComparer.Ordinal))
            {
                sb.Append(r).Append('|');
                string api = null;
                var st = Path.ChangeExtension(r, ".stamp.json");
                if (r.StartsWith(OutDir, StringComparison.Ordinal) && File.Exists(st))
                    try { api = (JsonNode.Parse(File.ReadAllText(st))?["api"] as JsonValue)?.GetValue<string>(); } catch (System.Text.Json.JsonException) { }
                if (api != null) sb.Append("api:").Append(api);
                else { var fi = new FileInfo(r); sb.Append(fi.Length).Append('|').Append(fi.LastWriteTimeUtc.Ticks); }
                sb.Append('\n');
            }
            sb.Append(a.Unsafe).Append(a.Editor);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        /// <summary>SHA-256 of source files, cached by size + mtime (Library/MiniUnity/ScriptAssemblies/sources.json).</summary>
        static class SourceHashes
        {
            static Dictionary<string, (long size, long mtime, string sha)> map;
            static bool dirty;
            static string FilePath => Path.Combine(OutDir, "sources.json");

            public static string Of(string file)
            {
                if (map == null)
                {
                    map = new(StringComparer.Ordinal);
                    try
                    {
                        if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject o)
                            foreach (var (k, v) in o) if (v is JsonArray arr && arr.Count == 3) map[k] = (arr[0]!.GetValue<long>(), arr[1]!.GetValue<long>(), arr[2]!.GetValue<string>());
                    }
                    catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidOperationException) { map.Clear(); }
                }
                var fi = new FileInfo(file);
                if (!fi.Exists) return "missing";
                long size = fi.Length, mtime = fi.LastWriteTimeUtc.Ticks;
                if (map.TryGetValue(file, out var c) && c.size == size && c.mtime == mtime) return c.sha;
                var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
                map[file] = (size, mtime, sha);
                dirty = true;
                return sha;
            }

            public static void Save()
            {
                if (!dirty || map == null) return;
                try
                {
                    var o = new JsonObject();
                    foreach (var (k, v) in map.Where(kv => File.Exists(kv.Key))) o[k] = new JsonArray(v.size, v.mtime, v.sha);
                    Directory.CreateDirectory(OutDir);
                    SafeIO.AtomicWriteText(FilePath, o.ToJsonString());
                    dirty = false;
                }
                catch (IOException) { }
            }
        }

        public static JsonObject DiagJson(Diagnostic d, string assembly)
        {
            var o = new JsonObject { ["severity"] = d.Severity.ToString().ToLowerInvariant(), ["id"] = d.Id, ["message"] = d.GetMessage(), ["assembly"] = assembly };
            if (d.Location.IsInSource)
            {
                var span = d.Location.GetLineSpan();
                o["file"] = Ua.Rel(span.Path); o["line"] = span.StartLinePosition.Line + 1; o["column"] = span.StartLinePosition.Character + 1;
            }
            return o;
        }
    }
}
