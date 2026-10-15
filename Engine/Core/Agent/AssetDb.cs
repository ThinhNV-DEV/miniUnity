// Project asset database for the agent tools: GUID ⇄ path over Assets/ and Packages/ (+ Library/PackageCache when present),
// main-object references per importer, reverse references, missing references.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MiniUnity.Yaml;

namespace MiniUnity.Agent
{
    public static class Ua
    {
        public static string Root => AssetIndex.Root;

        /// <summary>Normalize a user path to project-relative with forward slashes ("Assets/…", "Packages/…", "ProjectSettings/…").</summary>
        public static string Rel(string p)
        {
            if (p == null) return null;
            p = p.Replace('\\', '/');
            if (Path.IsPathRooted(p)) p = Path.GetRelativePath(Root, p).Replace('\\', '/');
            return p.TrimEnd('/');
        }
        public static string Abs(string rel) => Path.Combine(Root, Rel(rel));

        /// <summary>Glob with `*` and `?`, whole string, case-insensitive (asset.find --name, go.find --name).</summary>
        public static Regex Glob(string pattern) =>
            new("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Invariant-culture number option (`--seconds 1.5`); a bad value is a UaError naming the option.</summary>
        public static double Num(string s, string what) =>
            double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)
                ? d : throw new UaError($"{what}: expected a number, got '{s}'");

        /// <summary>"1080x1920" → (1080, 1920).</summary>
        public static (int w, int h) Size(string s, string what)
        {
            var p = s.Split('x', 'X');
            if (p.Length == 2 && int.TryParse(p[0], out var w) && int.TryParse(p[1], out var h) && w > 0 && h > 0) return (w, h);
            throw new UaError($"{what}: expected WIDTHxHEIGHT (e.g. 1080x1920), got '{s}'");
        }

        public static bool DryRun;
        public static readonly List<string> Written = new();

        /// <summary>`--diff` (and `--dry-run`): collect a unified diff of every text file written.</summary>
        public static bool ShowDiff;
        public static readonly System.Text.Json.Nodes.JsonArray Diffs = new();

        static void Diff(string abs, string text, bool binary, long size)
        {
            if (!(DryRun || ShowDiff)) return;
            var rel = Rel(abs);
            bool existed = File.Exists(abs);
            var o = new System.Text.Json.Nodes.JsonObject { ["path"] = rel, ["change"] = existed ? "modified" : "created" };
            if (binary) { o["binary"] = true; o["bytes"] = size; }
            else
            {
                var d = UaDiff.Unified(rel, existed ? File.ReadAllText(abs) : "", text);
                if (d.Length == 0) o["change"] = "unchanged"; else o["diff"] = d;
            }
            Diffs.Add(o);
        }

        public static void Write(string abs, string text)
        {
            Diff(abs, text, false, 0);
            Written.Add(Rel(abs));
            if (DryRun) return;
            SafeIO.Check(abs);
            EnsureDir(Path.GetDirectoryName(abs));
            UaJournal.BeforeWrite(abs);
            SafeIO.AtomicWriteText(abs, text);
        }
        public static void WriteBytes(string abs, byte[] data)
        {
            Diff(abs, null, true, data.Length);
            Written.Add(Rel(abs));
            if (DryRun) return;
            SafeIO.Check(abs);
            EnsureDir(Path.GetDirectoryName(abs));
            UaJournal.BeforeWrite(abs);
            SafeIO.AtomicWriteBytes(abs, data);
        }

        /// <summary>Create a folder and its missing parents, each recorded for undo.</summary>
        public static void EnsureDir(string absDir)
        {
            if (string.IsNullOrEmpty(absDir) || Directory.Exists(absDir) || DryRun) return;
            EnsureDir(Path.GetDirectoryName(absDir));
            SafeIO.Check(absDir);
            Directory.CreateDirectory(absDir);
            UaJournal.MadeDir(absDir);
        }

        /// <summary>Delete a file or folder (recorded for undo).</summary>
        public static void Delete(string abs)
        {
            if (DryRun || ShowDiff) Diffs.Add(new System.Text.Json.Nodes.JsonObject { ["path"] = Rel(abs), ["change"] = "deleted" });
            if (DryRun) return;
            SafeIO.Check(abs);
            UaJournal.BeforeDelete(abs);
            if (Directory.Exists(abs)) Directory.Delete(abs, true); else if (File.Exists(abs)) File.Delete(abs);
        }

        /// <summary>Move a file or folder (recorded for undo).</summary>
        public static void Move(string fromAbs, string toAbs)
        {
            if (DryRun || ShowDiff) Diffs.Add(new System.Text.Json.Nodes.JsonObject { ["path"] = Rel(fromAbs), ["change"] = "moved", ["to"] = Rel(toAbs) });
            if (DryRun) return;
            SafeIO.Check(fromAbs); SafeIO.Check(toAbs);
            if (Directory.Exists(fromAbs)) Directory.Move(fromAbs, toAbs); else File.Move(fromAbs, toAbs);
            UaJournal.Moved(fromAbs, toAbs);
        }
    }

    public sealed class AssetInfo
    {
        public string Path, Guid, Importer;
        public bool Folder;
        public YMap Meta;
    }

    public static class AssetDb
    {
        static Dictionary<string, AssetInfo> byPath, byGuid;
        public static readonly string[] BuiltinGuids = { "0000000000000000e000000000000000", "0000000000000000f000000000000000", "0000000000000000d000000000000000" };

        public static IEnumerable<string> Roots()
        {
            yield return "Assets";
            if (Directory.Exists(Ua.Abs("Packages"))) yield return "Packages";
            if (Directory.Exists(Ua.Abs("Library/PackageCache"))) yield return "Library/PackageCache";
        }

        public static void Reset() { byPath = null; byGuid = null; }

        // .meta path → (size, mtime, guid): kept across Reset() so that a long-lived process (the MCP server) re-reads only the
        // .meta files that changed instead of all of them
        static readonly Dictionary<string, (long size, long mtime, string guid)> metaGuids = new(StringComparer.Ordinal);

        static void Ensure()
        {
            if (byPath != null) return;
            byPath = new(StringComparer.Ordinal); byGuid = new(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in Roots())
            {
                var absRoot = Ua.Abs(root);
                if (!Directory.Exists(absRoot)) continue;
                var dirs = new HashSet<string>(Directory.EnumerateDirectories(absRoot, "*", SearchOption.AllDirectories), StringComparer.Ordinal);
                foreach (var fi in new DirectoryInfo(absRoot).EnumerateFiles("*.meta", SearchOption.AllDirectories))
                {
                    var meta = fi.FullName;
                    seen.Add(meta);
                    long size = fi.Length, mtime = fi.LastWriteTimeUtc.Ticks;
                    string guid;
                    if (metaGuids.TryGetValue(meta, out var c) && c.size == size && c.mtime == mtime) guid = c.guid;
                    else
                    {
                        guid = null;
                        foreach (var l in File.ReadLines(meta)) if (l.StartsWith("guid: ")) { guid = l.Substring(6).Trim(); break; }
                        metaGuids[meta] = (size, mtime, guid);
                    }
                    if (guid == null) continue;
                    var rel = Ua.Rel(meta[..^5]);
                    var a = new AssetInfo { Path = rel, Guid = guid, Folder = dirs.Contains(meta[..^5]) };
                    byPath[rel] = a;
                    byGuid.TryAdd(guid, a);
                }
            }
            if (metaGuids.Count > seen.Count) foreach (var k in metaGuids.Keys.Where(k => !seen.Contains(k)).ToList()) metaGuids.Remove(k);
        }

        public static IEnumerable<AssetInfo> All { get { Ensure(); return byPath.Values; } }
        public static AssetInfo ByPath(string p) { Ensure(); return byPath.TryGetValue(Ua.Rel(p), out var a) ? a : null; }
        public static AssetInfo ByGuid(string g)
        {
            Ensure();
            if (g == null) return null;
            if (byGuid.TryGetValue(g, out var a)) return a;
            // created in this process after the index was built (editor scripts: AssetDatabase / PrefabUtility): the engine's index has it
            if (MiniUnity.AssetIndex.PathOf(g) is string p && File.Exists(Ua.Abs(p))) { Register(p, g); return byGuid[g]; }
            return null;
        }
        public static bool IsBuiltin(string guid) => BuiltinGuids.Contains(guid);

        /// <summary>"Assets/x.mat" | "guid:abc…" | bare 32-hex guid → asset.</summary>
        public static AssetInfo Resolve(string pathOrGuid)
        {
            if (pathOrGuid == null) return null;
            var s = pathOrGuid.StartsWith("guid:") ? pathOrGuid[5..] : pathOrGuid;
            if (Regex.IsMatch(s, "^[0-9a-fA-F]{32}$")) return ByGuid(s);
            return ByPath(s);
        }
        public static AssetInfo Need(string pathOrGuid) => Resolve(pathOrGuid) ?? throw new UaError("asset not found: " + pathOrGuid);

        // parsed .meta files by path, validated by size + mtime (kept across Reset(), like metaGuids); callers get a copy
        static readonly Dictionary<string, (long size, long mtime, YMap meta)> metaParsed = new(StringComparer.Ordinal);

        public static YMap Meta(AssetInfo a)
        {
            if (a.Meta != null) return a.Meta;
            var mp = Ua.Abs(a.Path) + ".meta";
            var fi = new FileInfo(mp);
            if (!fi.Exists) a.Meta = new YMap();
            else if (metaParsed.TryGetValue(mp, out var c) && c.size == fi.Length && c.mtime == fi.LastWriteTimeUtc.Ticks) a.Meta = (YMap)c.meta.Clone();
            else
            {
                var parsed = YamlReader.ParseText(File.ReadAllText(mp));
                metaParsed[mp] = (fi.Length, fi.LastWriteTimeUtc.Ticks, parsed);
                a.Meta = (YMap)parsed.Clone();
            }
            a.Importer = a.Meta.Entries.Select(e => e.Key).FirstOrDefault(k => k.EndsWith("Importer"));
            return a.Meta;
        }
        public static string ImporterOf(AssetInfo a) { Meta(a); return a.Importer; }

        public static void Register(string rel, string guid)
        {
            Ensure();
            rel = Ua.Rel(rel);
            var a = new AssetInfo { Path = rel, Guid = guid, Folder = Directory.Exists(Ua.Abs(rel)) };
            byPath[rel] = a; byGuid[guid] = a;
        }
        public static void Unregister(string rel)
        {
            Ensure();
            if (byPath.Remove(Ua.Rel(rel), out var a)) byGuid.Remove(a.Guid);
        }

        // ------------------------------------------------------------------ main object reference
        /// <summary>Reference to the main object (or a named sub-asset) of an asset, as Unity writes it in YAML.</summary>
        public static YRef MainRef(AssetInfo a, string sub = null)
        {
            var imp = ImporterOf(a);
            var ext = System.IO.Path.GetExtension(a.Path).ToLowerInvariant();
            if (sub != null)
            {
                long sid = SubAssetId(a, sub);
                if (sid == 0) throw new UaError($"{a.Path}: no sub-asset '{sub}'");
                return new YRef(sid, a.Guid, imp == "NativeFormatImporter" ? 2 : 3);
            }
            switch (imp)
            {
                case "NativeFormatImporter":
                    return new YRef(Serializer.Long(Meta(a)["NativeFormatImporter"] is YMap nm ? nm["mainObjectFileID"] : null) is long id && id != 0 ? id : 11400000, a.Guid, 2);
                case "PrefabImporter":
                    return new YRef(PrefabRootGo(a.Path), a.Guid, 3);
                case "TextureImporter":
                {
                    var ti = Meta(a)["TextureImporter"] as YMap;
                    int spriteMode = Serializer.Int(ti?["spriteMode"], 0), texType = Serializer.Int(ti?["textureType"], 0);
                    return new YRef(texType == 8 && spriteMode == 1 ? 21300000 : 2800000, a.Guid, 3);
                }
                case "MonoImporter": return new YRef(11500000, a.Guid, 3);
                case "AudioImporter": return new YRef(8300000, a.Guid, 3);
                case "TextScriptImporter": return new YRef(4900000, a.Guid, 3);
                case "TrueTypeFontImporter": return new YRef(12800000, a.Guid, 3);
                case "ShaderImporter": return new YRef(4800000, a.Guid, 3);
                case "ModelImporter": return new YRef(919132149155446097, a.Guid, 3);
                case "VideoClipImporter": return new YRef(32900000, a.Guid, 3);
            }
            if (ext == ".unity") return new YRef(102900000, a.Guid, 3);
            if (ext is ".asset" or ".mat" or ".controller" or ".anim" or ".playable" or ".mixer" or ".overrideController" or ".mask" or ".physicMaterial" or ".spriteatlas")
            {
                // native file without NativeFormatImporter block: first document is the main object
                var first = UFile.IsYaml(Ua.Abs(a.Path)) ? YamlReader.ReadFile(Ua.Abs(a.Path)).FirstOrDefault() : null;
                return new YRef(first?.FileId ?? 11400000, a.Guid, 2);
            }
            return new YRef(102900000, a.Guid, 3);
        }

        public static long PrefabRootGo(string prefabRel)
        {
            var f = UFile.Load(prefabRel);
            foreach (var d in f.Docs.Where(d => d.ClassId is 4 or 224 && !d.Stripped))
                if ((d.Body["m_Father"] as YRef)?.FileID == 0) return (d.Body["m_GameObject"] as YRef)?.FileID ?? 0;
            // prefab variant: root is a PrefabInstance; its root GameObject is stripped
            var go = f.Docs.FirstOrDefault(d => d.ClassId == 1);
            return go?.FileId ?? 0;
        }

        /// <summary>Sub-asset id by name (sprites of a multi-sprite texture, meshes / materials of a model) from the .meta tables.</summary>
        public static long SubAssetId(AssetInfo a, string name)
        {
            var meta = Meta(a);
            var imp = meta[ImporterOf(a) ?? ""] as YMap;
            if (imp == null) return 0;
            // 2021+: internalIDToNameTable: - first: {213: 123456} second: name
            if (imp["internalIDToNameTable"] is YSeq tbl)
                foreach (var e in tbl.Items.OfType<YMap>())
                    if (Serializer.Str(e["second"]) == name && e["first"] is YMap fm && fm.Entries.Count > 0) return Serializer.Long(fm.Entries[0].Value);
            if (imp["spriteSheet"] is YMap ss && ss["sprites"] is YSeq sprites)
                foreach (var s in sprites.Items.OfType<YMap>())
                    if (Serializer.Str(s["name"]) == name) return Serializer.Long(s["internalID"]);
            if (imp["fileIDToRecycleName"] is YMap old)
                foreach (var e in old.Entries) if (Serializer.Str(e.Value) == name) return long.Parse(e.Key);
            if (name == "Sprite" || name == a.Path.Split('/')[^1].Split('.')[0]) return 21300000;
            return 0;
        }

        // ------------------------------------------------------------------ references
        static readonly Regex GuidRef = new(@"guid: ([0-9a-fA-F]{32})", RegexOptions.Compiled);
        static readonly string[] TextExts = { ".unity", ".prefab", ".asset", ".mat", ".controller", ".anim", ".playable", ".overrideController", ".mask",
            ".mixer", ".physicMaterial", ".physicsMaterial2D", ".spriteatlas", ".spriteatlasv2", ".lighting", ".giparams", ".preset", ".terrainlayer",
            ".flare", ".renderTexture", ".cubemap", ".shadervariants", ".guiskin", ".fontsettings", ".brush", ".signal", ".meta" };

        public static bool IsTextAsset(string rel) => TextExts.Contains(System.IO.Path.GetExtension(rel).ToLowerInvariant()) || rel.StartsWith("ProjectSettings/");

        /// <summary>Every YAML file of the project that may hold references (Assets + ProjectSettings + embedded packages; not the cache).</summary>
        public static IEnumerable<string> ReferenceFiles(string under = null)
        {
            var roots = under != null ? new[] { Ua.Rel(under) } : new[] { "Assets", "ProjectSettings", "Packages" };
            foreach (var r in roots)
            {
                var abs = Ua.Abs(r);
                if (File.Exists(abs)) { yield return Ua.Rel(abs); continue; }
                if (!Directory.Exists(abs)) continue;
                foreach (var f in Directory.EnumerateFiles(abs, "*", SearchOption.AllDirectories))
                {
                    var rel = Ua.Rel(f);
                    if (rel.EndsWith(".meta") ? !rel.EndsWith(".cs.meta") && IsTextAsset(rel) : IsTextAsset(rel)) yield return rel;
                }
            }
        }

        /// <summary>GUIDs the file references ("guid: …"), from the reference index.</summary>
        public static IEnumerable<string> GuidsIn(string rel)
        {
            var e = RefIndex.Get(Ua.Rel(rel));
            return e == null ? Enumerable.Empty<string>() : e.Refs.Keys;
        }

        /// <summary>Files that mention guid (as a reference or as a bare GUID, e.g. Addressables m_GUID), excluding the asset's own .meta.</summary>
        public static List<(string file, int count)> ReferencesTo(string guid, string under = null)
        {
            var res = new List<(string, int)>();
            var own = ByGuid(guid)?.Path;
            guid = guid.ToLowerInvariant();
            foreach (var f in ReferenceFiles(under))
            {
                if (own != null && (f == own + ".meta")) continue;
                var e = RefIndex.Get(f);
                if (e == null) continue;
                int n = (e.Refs.TryGetValue(guid, out var r) ? r : 0) + (e.Other.TryGetValue(guid, out var o) ? o : 0);
                if (n > 0) res.Add((f, n));
            }
            return res;
        }
    }

    /// <summary>Persistent reference index (Library/MiniUnity/refindex.json): for every file that may hold references, the GUIDs it
    /// mentions, keyed by size + mtime. A command re-reads only the files that changed since the index was written, instead of
    /// the text of the whole project (asset.refs / asset.missing / asset.delete / ref.replace / guid.regenerate).</summary>
    public static class RefIndex
    {
        public sealed class Entry
        {
            public long Size { get; set; }
            public long Mtime { get; set; }
            public Dictionary<string, int> Refs { get; set; } = new();    // "guid: x"
            public Dictionary<string, int> Other { get; set; } = new();   // x elsewhere (m_GUID: x, strings)
        }

        const int Version = 1;
        static Dictionary<string, Entry> files;
        static bool dirty;
        static string IndexPath => Path.Combine(Ua.Root, "Library/MiniUnity/refindex.json");
        static readonly Regex token = new(@"(guid: )?(?<![0-9a-fA-F])([0-9a-fA-F]{32})(?![0-9a-fA-F])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static void Reset() { files = null; dirty = false; }

        static void Load()
        {
            if (files != null) return;
            files = new Dictionary<string, Entry>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(IndexPath))
                {
                    var doc = System.Text.Json.JsonSerializer.Deserialize<Stored>(File.ReadAllText(IndexPath));
                    if (doc?.Version == Version && doc.Files != null) files = new Dictionary<string, Entry>(doc.Files, StringComparer.Ordinal);
                }
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }   // rebuilt below
        }

        sealed class Stored { public int Version { get; set; } public Dictionary<string, Entry> Files { get; set; } }

        /// <summary>Index entry of a project file, refreshed when its size / mtime changed; null when the file does not exist.</summary>
        public static Entry Get(string rel)
        {
            Load();
            var fi = new FileInfo(Ua.Abs(rel));
            if (!fi.Exists) { if (files.Remove(rel)) dirty = true; return null; }
            long mt = fi.LastWriteTimeUtc.Ticks;
            if (files.TryGetValue(rel, out var e) && e.Size == fi.Length && e.Mtime == mt) return e;
            e = new Entry { Size = fi.Length, Mtime = mt };
            string text;
            try { text = File.ReadAllText(fi.FullName); }
            catch (IOException) { return e; }
            foreach (Match m in token.Matches(text))
            {
                var g = m.Groups[2].Value.ToLowerInvariant();
                var d = m.Groups[1].Success ? e.Refs : e.Other;
                d[g] = d.TryGetValue(g, out var c) ? c + 1 : 1;
            }
            files[rel] = e;
            dirty = true;
            return e;
        }

        /// <summary>Write the index if a command refreshed it (best effort: it is a cache).</summary>
        public static void Save()
        {
            if (!dirty || files == null || Ua.Root == null) return;
            try
            {
                var keep = files.Where(kv => File.Exists(Ua.Abs(kv.Key))).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
                Directory.CreateDirectory(Path.GetDirectoryName(IndexPath)!);
                SafeIO.AtomicWriteText(IndexPath, System.Text.Json.JsonSerializer.Serialize(new Stored { Version = Version, Files = keep }));
                dirty = false;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
