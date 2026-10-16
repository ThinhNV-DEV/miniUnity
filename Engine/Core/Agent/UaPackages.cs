// Package Manager without Unity: Packages/manifest.json edits, dependency resolution (registry metadata, git, local, embedded),
// packages-lock.json, and fetching package sources into Library/PackageCache (like Unity) so scripts / GUIDs / sources are available.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MiniUnity.Agent
{
    public static partial class UaCommands
    {
        static partial void RegisterExtra()
        {
            RegisterPackages();
            RegisterAddressables();
            RegisterBuild();
            RegisterTestCommands();
            RegisterApiGap();
            RegisterSerialized();
            RegisterEditorWindows();
        }
        static partial void RegisterBuild();

        const string ManifestRel = "Packages/manifest.json", LockRel = "Packages/packages-lock.json";
        static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        static JsonObject ReadJson(string rel) => File.Exists(Ua.Abs(rel)) ? JsonNode.Parse(File.ReadAllText(Ua.Abs(rel)))!.AsObject() : null;
        static void WriteJson(string rel, JsonNode n) => Ua.Write(Ua.Abs(rel), n.ToJsonString(Pretty).Replace("\r\n", "\n") + "\n");

        static string RegistryUrl(JsonObject manifest, string name)
        {
            // scoped registries from the manifest win for their scopes
            if (manifest?["scopedRegistries"] is JsonArray regs)
                foreach (var r in regs.OfType<JsonObject>())
                    if (r["scopes"] is JsonArray sc && sc.Any(s => name.StartsWith(s!.GetValue<string>())))
                        return r["url"]!.GetValue<string>().TrimEnd('/');
            return "https://packages.unity.com";
        }

        static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
        static readonly Dictionary<string, JsonObject> metaCache = new();
        static JsonObject RegistryMeta(string registry, string name)
        {
            var key = registry + "/" + name;
            if (metaCache.TryGetValue(key, out var m)) return m;
            try { m = JsonNode.Parse(http.GetStringAsync($"{registry}/{name}").GetAwaiter().GetResult())!.AsObject(); }
            catch (Exception e) { throw new UaError($"registry {registry}: {name}: {e.Message}"); }
            return metaCache[key] = m;
        }

        // ------------------------------------------------------------------ versions
        public static int CompareVersions(string a, string b)
        {
            static (int[] nums, string pre) P(string v)
            {
                var dash = v.IndexOf('-');
                var core = dash >= 0 ? v[..dash] : v;
                var nums = core.Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).Concat(new[] { 0, 0, 0 }).Take(3).ToArray();
                return (nums, dash >= 0 ? v[(dash + 1)..] : null);
            }
            var (na, pa) = P(a); var (nb, pb) = P(b);
            for (int i = 0; i < 3; i++) if (na[i] != nb[i]) return na[i].CompareTo(nb[i]);
            if (pa == null && pb == null) return 0;
            if (pa == null) return 1;
            if (pb == null) return -1;
            var xa = pa.Split('.'); var xb = pb.Split('.');
            for (int i = 0; i < Math.Min(xa.Length, xb.Length); i++)
            {
                bool ia = int.TryParse(xa[i], out var ai), ib = int.TryParse(xb[i], out var bi);
                int c = ia && ib ? ai.CompareTo(bi) : string.CompareOrdinal(xa[i], xb[i]);
                if (c != 0) return c;
            }
            return xa.Length.CompareTo(xb.Length);
        }

        static bool IsGit(string v) => v.StartsWith("git") || v.Contains(".git") || v.StartsWith("https://github.com") || v.StartsWith("ssh://");
        static bool IsLocal(string v) => v.StartsWith("file:");
        static bool IsBuiltinModule(string name) => name.StartsWith("com.unity.modules.") || name.StartsWith("com.unity.feature.");   // modules + feature sets ship with the Editor

        // ------------------------------------------------------------------ resolution
        public sealed class Resolved
        {
            public string Name, Version, Source, Url, Hash; public int Depth;
            public SortedDictionary<string, string> Deps = new(StringComparer.Ordinal);
        }

        /// <summary>Resolve the manifest into the full package set (Unity rules: direct dependencies win, otherwise the highest requested version).</summary>
        public static SortedDictionary<string, Resolved> Resolve(bool useLock, List<string> warnings)
        {
            var manifest = ReadJson(ManifestRel) ?? throw new UaError("no " + ManifestRel);
            var lockDeps = useLock ? ReadJson(LockRel)?["dependencies"] as JsonObject : null;
            var direct = (manifest["dependencies"] as JsonObject ?? new JsonObject()).ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>());
            var embedded = Directory.Exists(Ua.Abs("Packages"))
                ? Directory.EnumerateDirectories(Ua.Abs("Packages")).Where(d => File.Exists(Path.Combine(d, "package.json")))
                    .ToDictionary(d => JsonNode.Parse(File.ReadAllText(Path.Combine(d, "package.json")))!["name"]!.GetValue<string>(), d => d)
                : new Dictionary<string, string>();
            var res = new SortedDictionary<string, Resolved>(StringComparer.Ordinal);
            var requested = new Dictionary<string, List<string>>();
            var queue = new Queue<(string name, string ver, int depth)>();
            foreach (var kv in direct) queue.Enqueue((kv.Key, kv.Value, 0));
            foreach (var kv in embedded) if (!direct.ContainsKey(kv.Key)) queue.Enqueue((kv.Key, "file:" + Path.GetFileName(kv.Value), 0));
            int guard = 0;
            while (queue.Count > 0 && guard++ < 5000)
            {
                var (name, ver, depth) = queue.Dequeue();
                if (!requested.TryGetValue(name, out var reqs)) requested[name] = reqs = new();
                reqs.Add(ver);
                // choose: embedded > direct > highest requested
                string chosen = embedded.ContainsKey(name) ? "file:" + Path.GetFileName(embedded[name]) : direct.TryGetValue(name, out var dv) ? dv
                    : reqs.Where(r => !IsGit(r) && !IsLocal(r)).OrderBy(r => r, Comparer<string>.Create(CompareVersions)).LastOrDefault() ?? reqs.Last();
                if (res.TryGetValue(name, out var have) && have.Version == chosen && have.Depth <= depth) continue;
                var r = new Resolved { Name = name, Version = chosen, Depth = res.TryGetValue(name, out var prev) ? Math.Min(prev.Depth, depth) : depth };
                var lockEntry = lockDeps?[name] as JsonObject;
                if (embedded.TryGetValue(name, out var edir))
                {
                    r.Source = "embedded"; r.Version = "file:" + Path.GetFileName(edir);
                    foreach (var d in (JsonNode.Parse(File.ReadAllText(Path.Combine(edir, "package.json")))!["dependencies"] as JsonObject ?? new JsonObject())) r.Deps[d.Key] = d.Value!.GetValue<string>();
                }
                else if (IsBuiltinModule(name) || lockEntry?["source"]?.GetValue<string>() == "builtin"
                         || (UnityEditorInstall.BuiltInPackages is string bip && Directory.Exists(Path.Combine(bip, name)) && !direct.ContainsKey(name + "#registry")))
                {
                    r.Source = "builtin";
                    if (lockEntry?["dependencies"] is JsonObject ld) foreach (var d in ld) r.Deps[d.Key] = d.Value!.GetValue<string>();
                }
                else if (IsGit(chosen) || IsLocal(chosen))
                {
                    r.Source = IsGit(chosen) ? "git" : "local";
                    if (lockEntry != null && lockEntry["version"]?.GetValue<string>() == chosen)
                    {
                        r.Hash = lockEntry["hash"]?.GetValue<string>();
                        if (lockEntry["dependencies"] is JsonObject ld) foreach (var d in ld) r.Deps[d.Key] = d.Value!.GetValue<string>();
                    }
                    else if (PackageJsonOf(name, chosen) is JsonObject pj && pj["dependencies"] is JsonObject pd)
                        foreach (var d in pd) r.Deps[d.Key] = d.Value!.GetValue<string>();
                    else warnings.Add($"{name}: dependencies unknown until fetched ({chosen})");
                }
                else
                {
                    r.Source = "registry";
                    r.Url = RegistryUrl(manifest, name);
                    if (lockEntry != null && lockEntry["version"]?.GetValue<string>() == chosen && lockEntry["dependencies"] is JsonObject ld)
                        foreach (var d in ld) r.Deps[d.Key] = d.Value!.GetValue<string>();
                    else
                    {
                        JsonObject meta = null;
                        try { meta = RegistryMeta(r.Url, name); } catch (UaError e) { warnings.Add(e.Message); }
                        var vinfo = meta?["versions"]?[chosen] as JsonObject;
                        if (vinfo == null) { warnings.Add($"{name}@{chosen}: version not on {r.Url}"); }
                        else if (vinfo["dependencies"] is JsonObject vd) foreach (var d in vd) r.Deps[d.Key] = d.Value!.GetValue<string>();
                    }
                }
                res[name] = r;
                foreach (var d in r.Deps) queue.Enqueue((d.Key, d.Value, r.Depth + 1));
            }
            return res;
        }

        static JsonObject PackageJsonOf(string name, string version)
        {
            var dir = CacheDir(name, version, null);
            var pj = dir != null ? Path.Combine(dir, "package.json") : null;
            return pj != null && File.Exists(pj) ? JsonNode.Parse(File.ReadAllText(pj))!.AsObject() : null;
        }

        static string CacheDir(string name, string version, string hash)
        {
            var root = Ua.Abs("Library/PackageCache");
            if (!Directory.Exists(root)) return null;
            if (!IsGit(version) && !IsLocal(version)) { var d = Path.Combine(root, name + "@" + version); return Directory.Exists(d) ? d : null; }
            return Directory.EnumerateDirectories(root, name + "@*").FirstOrDefault(d => hash == null || d.EndsWith("@" + hash[..Math.Min(10, hash.Length)]) || d.EndsWith("@" + hash));
        }

        static JsonObject LockJson(SortedDictionary<string, Resolved> res)
        {
            var deps = new JsonObject();
            foreach (var r in res.Values)
            {
                var o = new JsonObject { ["version"] = r.Version, ["depth"] = r.Depth, ["source"] = r.Source, ["dependencies"] = new JsonObject(r.Deps.Select(d => new KeyValuePair<string, JsonNode>(d.Key, d.Value))) };
                if (r.Hash != null) o["hash"] = r.Hash;
                if (r.Url != null) o["url"] = r.Url;
                deps[r.Name] = o;
            }
            return new JsonObject { ["dependencies"] = deps };
        }

        // ------------------------------------------------------------------ fetching
        static string Fetch(Resolved r, List<string> log)
        {
            var root = Ua.Abs("Library/PackageCache");
            if (r.Source is "builtin" or "embedded") return null;
            if (r.Source == "registry")
            {
                var dir = Path.Combine(root, r.Name + "@" + r.Version);
                if (Directory.Exists(dir)) return dir;
                var meta = RegistryMeta(r.Url, r.Name);
                var tarball = meta["versions"]?[r.Version]?["dist"]?["tarball"]?.GetValue<string>() ?? $"{r.Url}/{r.Name}/-/{r.Name}-{r.Version}.tgz";
                var tmp = Path.Combine(Path.GetTempPath(), $"ua_{r.Name}_{r.Version}.tgz");
                using (var s = http.GetStreamAsync(tarball).GetAwaiter().GetResult())
                using (var fs = File.Create(tmp)) s.CopyTo(fs);
                SafeIO.Check(dir);
                Directory.CreateDirectory(dir);
                Run("tar", $"-xzf \"{tmp}\" -C \"{dir}\" --strip-components=1");
                File.Delete(tmp);
                log.Add($"{r.Name}@{r.Version} ← {tarball}");
                return dir;
            }
            if (r.Source == "git")
            {
                // url[?path=sub][#rev]
                var m = Regex.Match(r.Version, @"^(?<url>[^?#]+)(\?path=(?<path>[^#]+))?(#(?<rev>.+))?$");
                string url = m.Groups["url"].Value.Replace("git+", ""), sub = m.Groups["path"].Value.Trim('/'), rev = r.Hash ?? (m.Groups["rev"].Success ? m.Groups["rev"].Value : null);
                var tmp = Path.Combine(Path.GetTempPath(), "ua_git_" + Guid.NewGuid().ToString("N")[..8]);
                Run("git", $"clone --quiet --filter=blob:none --no-checkout \"{url}\" \"{tmp}\"");
                if (rev != null) Run("git", $"-C \"{tmp}\" checkout --quiet {rev}"); else Run("git", $"-C \"{tmp}\" checkout --quiet");
                var hash = Run("git", $"-C \"{tmp}\" rev-parse HEAD").Trim();
                r.Hash ??= hash;
                var dir = Path.Combine(root, r.Name + "@" + hash[..10]);
                if (!Directory.Exists(dir))
                {
                    SafeIO.Check(dir);
                    CopyDir(Path.Combine(tmp, sub), dir);
                }
                try { Directory.Delete(tmp, true); } catch { }
                if (File.Exists(Path.Combine(dir, "package.json")))
                    foreach (var d in (JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "package.json")))!["dependencies"] as JsonObject ?? new JsonObject()))
                        r.Deps[d.Key] = d.Value!.GetValue<string>();
                log.Add($"{r.Name} ← {url}{(sub.Length > 0 ? " /" + sub : "")} @{hash[..10]}");
                return dir;
            }
            if (r.Source == "local") return Path.GetFullPath(Path.Combine(Ua.Abs("Packages"), r.Version[5..]));
            return null;
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, f);
                if (rel.StartsWith(".git")) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dst, rel))!);
                File.Copy(f, Path.Combine(dst, rel), true);
            }
        }

        public static string Run(string exe, string args, string cwd = null)
        {
            var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = cwd ?? Ua.Root };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd(); var e = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new UaError($"{exe} {args}: {e.Trim()}");
            return o;
        }

        // ------------------------------------------------------------------ commands
        static void RegisterPackages()
        {
            Reg("pkg.list", "pkg.list [--resolved]   manifest dependencies (+ lock: version, source, depth)", a =>
            {
                var manifest = ReadJson(ManifestRel) ?? throw new UaError("no " + ManifestRel);
                var lk = ReadJson(LockRel)?["dependencies"] as JsonObject;
                var direct = manifest["dependencies"] as JsonObject ?? new JsonObject();
                var list = new JsonArray();
                IEnumerable<string> names = a.Has("resolved") && lk != null ? lk.Select(k => k.Key) : direct.Select(k => k.Key);
                foreach (var n in names)
                {
                    var l = lk?[n] as JsonObject;
                    var o = new JsonObject { ["name"] = n, ["requested"] = direct[n]?.GetValue<string>(), ["version"] = l?["version"]?.GetValue<string>(), ["source"] = l?["source"]?.GetValue<string>(), ["depth"] = l?["depth"]?.GetValue<int>() };
                    o["cached"] = l != null && CacheDir(n, l["version"]!.GetValue<string>(), l["hash"]?.GetValue<string>()) != null;
                    list.Add(o);
                }
                return new JsonObject { ["count"] = list.Count, ["packages"] = list, ["scopedRegistries"] = manifest["scopedRegistries"]?.DeepClone() };
            });
            Reg("pkg.info", "pkg.info <name> [--registry]   lock entry, cached package.json, registry versions", a =>
            {
                var name = a.P(0, "name");
                var manifest = ReadJson(ManifestRel);
                var l = ReadJson(LockRel)?["dependencies"]?[name] as JsonObject;
                var o = new JsonObject { ["name"] = name, ["requested"] = manifest?["dependencies"]?[name]?.GetValue<string>(), ["lock"] = l?.DeepClone() };
                if (l != null && CacheDir(name, l["version"]!.GetValue<string>(), l["hash"]?.GetValue<string>()) is string dir)
                {
                    o["cachePath"] = Ua.Rel(dir);
                    var pj = Path.Combine(dir, "package.json");
                    if (File.Exists(pj)) o["packageJson"] = JsonNode.Parse(File.ReadAllText(pj));
                }
                if (a.Has("registry") && !IsBuiltinModule(name))
                {
                    var meta = RegistryMeta(RegistryUrl(manifest, name), name);
                    var vers = (meta["versions"] as JsonObject)?.Select(v => v.Key).OrderBy(v => v, Comparer<string>.Create(CompareVersions)).ToList() ?? new List<string>();
                    o["latest"] = meta["dist-tags"]?["latest"]?.GetValue<string>();
                    o["versions"] = Arr(vers.TakeLast(30), J);
                    o["description"] = meta["description"]?.GetValue<string>();
                }
                return o;
            });
            Reg("pkg.search", "pkg.search <text>   packages whose name / display name / description contain the text (lock, Library/PackageCache, Editor built-ins; an exact package name is also looked up on the registry)", a =>
            {
                // the Unity registry has no search API (npm's /-/v1/search answers 404), so search what is known locally
                var text = a.P(0, "text");
                var found = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
                void Add(string name, string version, string display, string description, string source)
                {
                    if (name == null || found.ContainsKey(name)) return;
                    bool hit = new[] { name, display, description }.Any(x => x != null && x.Contains(text, StringComparison.OrdinalIgnoreCase));
                    if (hit) found[name] = new JsonObject { ["name"] = name, ["version"] = version, ["displayName"] = display, ["description"] = description, ["source"] = source };
                }
                void FromFolder(string dir, string source)
                {
                    if (!Directory.Exists(dir)) return;
                    foreach (var pj in Directory.EnumerateDirectories(dir).Select(d => Path.Combine(d, "package.json")).Where(File.Exists))
                        try
                        {
                            var j = JsonNode.Parse(File.ReadAllText(pj));
                            Add(j?["name"]?.ToString(), j?["version"]?.ToString(), j?["displayName"]?.ToString(), j?["description"]?.ToString(), source);
                        }
                        catch (System.Text.Json.JsonException) { }
                }
                FromFolder(Ua.Abs("Library/PackageCache"), "cache");
                FromFolder(Ua.Abs("Packages"), "embedded");
                if (UnityEditorInstall.BuiltInPackages != null) FromFolder(UnityEditorInstall.BuiltInPackages, "builtin");
                if (File.Exists(Ua.Abs("Packages/packages-lock.json")) && JsonNode.Parse(File.ReadAllText(Ua.Abs("Packages/packages-lock.json")))?["dependencies"] is JsonObject deps)
                    foreach (var (name, v) in deps) Add(name, v?["version"]?.ToString(), null, null, "lock");
                if (File.Exists(Ua.Abs("Packages/manifest.json")) && JsonNode.Parse(File.ReadAllText(Ua.Abs("Packages/manifest.json")))?["dependencies"] is JsonObject mdeps)
                    foreach (var (name, v) in mdeps) Add(name, v?.ToString(), null, null, "manifest");
                if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^[a-z0-9-]+(\.[a-z0-9-]+){2,}$") && !found.ContainsKey(text))
                    try
                    {
                        var manifest = File.Exists(Ua.Abs("Packages/manifest.json")) ? JsonNode.Parse(File.ReadAllText(Ua.Abs("Packages/manifest.json"))) as JsonObject : null;
                        var meta = RegistryMeta(RegistryUrl(manifest, text), text);
                        var latest = meta["dist-tags"]?["latest"]?.ToString();
                        var vj = latest != null ? meta["versions"]?[latest] : null;
                        found[text] = new JsonObject { ["name"] = text, ["version"] = latest, ["displayName"] = vj?["displayName"]?.DeepClone(), ["description"] = vj?["description"]?.DeepClone(), ["source"] = "registry" };
                    }
                    catch (UaError) { }
                return new JsonObject
                {
                    ["count"] = found.Count, ["results"] = new JsonArray(found.Values.Select(x => (JsonNode)x).ToArray()),
                    ["note"] = "the Unity registry has no search API: results come from packages known locally; pass a full package name (com.unity.x) to look it up on the registry",
                };
            });
            Reg("pkg.add", "pkg.add <name>[@version] | <name> <git-url|file:path> [--no-resolve]   add / change a manifest dependency", a =>
            {
                var spec = a.P(0, "name");
                string name = spec, ver = a.P(1);
                int at = spec.IndexOf('@');
                if (at > 0) { name = spec[..at]; ver = spec[(at + 1)..]; }
                var manifest = ReadJson(ManifestRel) ?? throw new UaError("no " + ManifestRel);
                if (ver == null)
                {
                    if (IsBuiltinModule(name)) ver = "1.0.0";
                    else ver = RegistryMeta(RegistryUrl(manifest, name), name)["dist-tags"]?["latest"]?.GetValue<string>() ?? throw new UaError("no latest version for " + name);
                }
                else if (!IsGit(ver) && !IsLocal(ver) && !IsBuiltinModule(name))
                {
                    var meta = RegistryMeta(RegistryUrl(manifest, name), name);
                    if (meta["versions"]?[ver] == null) throw new UaError($"{name}@{ver} not found in the registry");
                }
                var deps = manifest["dependencies"] as JsonObject ?? new JsonObject();
                var old = deps[name]?.GetValue<string>();
                var sorted = deps.Select(kv => (kv.Key, kv.Value!.GetValue<string>())).Where(kv => kv.Key != name).Append((name, ver)).OrderBy(kv => kv.Item1, StringComparer.Ordinal).ToList();
                var nd = new JsonObject(); foreach (var (k, v) in sorted) nd[k] = v;
                manifest["dependencies"] = nd;
                WriteJson(ManifestRel, manifest);
                var o = new JsonObject { ["name"] = name, ["version"] = ver, ["previous"] = old };
                if (!a.Has("no-resolve"))
                {
                    var warn = new List<string>();
                    var res = Resolve(true, warn);
                    o["addedTransitive"] = Arr(res.Values.Where(r => r.Depth > 0 && (ReadJson(LockRel)?["dependencies"]?[r.Name] == null)), r => J(r.Name + "@" + r.Version));
                    if (warn.Count > 0) o["warnings"] = Arr(warn, J);
                    o["note"] = "packages-lock.json is refreshed by `pkg.resolve --write-lock` (Unity rewrites it on the next open)";
                }
                return o;
            });
            Reg("pkg.remove", "pkg.remove <name>", a =>
            {
                var name = a.P(0, "name");
                var manifest = ReadJson(ManifestRel) ?? throw new UaError("no " + ManifestRel);
                var deps = manifest["dependencies"] as JsonObject;
                if (deps?[name] == null) throw new UaError(name + " is not a direct dependency");
                deps.Remove(name);
                WriteJson(ManifestRel, manifest);
                var lk = ReadJson(LockRel)?["dependencies"] as JsonObject;
                var stillNeeded = lk?.Where(kv => kv.Key != name && kv.Value?["dependencies"]?[name] != null).Select(kv => kv.Key).ToList() ?? new List<string>();
                return new JsonObject { ["removed"] = name, ["stillRequiredBy"] = Arr(stillNeeded, J) };
            });
            Reg("pkg.deps", "pkg.deps <name> [--reverse]   dependency tree (from the lock / resolution)", a =>
            {
                var name = a.P(0, "name");
                var warn = new List<string>();
                var res = Resolve(true, warn);
                if (a.Has("reverse"))
                    return new JsonObject { ["name"] = name, ["requiredBy"] = Arr(res.Values.Where(r => r.Deps.ContainsKey(name)), r => new JsonObject { ["name"] = r.Name, ["requests"] = r.Deps[name] }) };
                JsonNode Tree(string n, HashSet<string> seen)
                {
                    if (!res.TryGetValue(n, out var r)) return new JsonObject { ["name"] = n, ["missing"] = true };
                    var o = new JsonObject { ["name"] = n, ["version"] = r.Version, ["source"] = r.Source };
                    if (!seen.Add(n)) { o["cycle"] = true; return o; }
                    if (r.Deps.Count > 0) o["dependencies"] = Arr(r.Deps.Keys, d => Tree(d, new HashSet<string>(seen)));
                    return o;
                }
                return Tree(name, new HashSet<string>()) as JsonObject;
            });
            Reg("pkg.resolve", "pkg.resolve [--fresh] [--fetch] [--write-lock]   resolve the manifest; --fetch downloads sources to Library/PackageCache", a =>
            {
                var warn = new List<string>();
                var res = Resolve(!a.Has("fresh"), warn);
                var log = new List<string>();
                var fetched = new JsonArray();
                if (a.Has("fetch"))
                {
                    foreach (var r in res.Values.ToList())
                    {
                        try { var dir = Fetch(r, log); if (dir != null) fetched.Add(new JsonObject { ["name"] = r.Name, ["path"] = Ua.Rel(dir) }); }
                        catch (Exception e) { warn.Add($"{r.Name}: {e.Message}"); }
                    }
                    AssetDb.Reset();
                }
                if (a.Has("write-lock")) WriteJson(LockRel, LockJson(res));
                return new JsonObject
                {
                    ["count"] = res.Count, ["packages"] = Arr(res.Values, r => new JsonObject { ["name"] = r.Name, ["version"] = r.Version, ["source"] = r.Source, ["depth"] = r.Depth }),
                    ["fetched"] = fetched, ["log"] = Arr(log, J), ["warnings"] = Arr(warn, J),
                };
            });
        }
    }
}
