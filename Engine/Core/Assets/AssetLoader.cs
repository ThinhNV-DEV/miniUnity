using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniUnity.Yaml;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity
{
    /// <summary>GUID ⇄ path index built from .meta files of the (scratch) project.</summary>
    public static class AssetIndex
    {
        public static string Root = Directory.GetCurrentDirectory();
        static Dictionary<string, string> guidToPath, pathToGuid;

        static void Ensure()
        {
            if (guidToPath != null) return;
            guidToPath = new(); pathToGuid = new();
            foreach (var meta in Directory.EnumerateFiles(Path.Combine(Root, "Assets"), "*.meta", SearchOption.AllDirectories))
            {
                string guid = null;
                foreach (var l in File.ReadLines(meta)) { if (l.StartsWith("guid: ")) { guid = l.Substring(6).Trim(); break; } }
                if (guid == null) continue;
                var rel = Path.GetRelativePath(Root, meta[..^5]).Replace('\\', '/');
                guidToPath[guid] = rel; pathToGuid[rel] = guid;
            }
        }
        public static string Abs(string assetPath)
        {
            if (assetPath != null && assetPath.StartsWith("Packages/", StringComparison.Ordinal))
            {
                EnsurePackages();
                int slash = assetPath.IndexOf('/', 9);
                var name = slash < 0 ? assetPath.Substring(9) : assetPath.Substring(9, slash - 9);
                if (packageDirs.TryGetValue(name, out var dir)) return slash < 0 ? dir : Path.Combine(dir, assetPath.Substring(slash + 1));
            }
            return Path.Combine(Root, assetPath);
        }
        public static string GuidOf(string path)
        {
            Ensure();
            var n = Norm(path);
            if (pathToGuid.TryGetValue(n, out var g)) return g;
            if (n != null && n.StartsWith("Packages/", StringComparison.Ordinal)) { EnsurePackages(); if (pkgPathToGuid.TryGetValue(n, out g)) return g; }
            return null;
        }
        public static string PathOf(string guid)
        {
            Ensure();
            if (guid == null) return null;
            if (guidToPath.TryGetValue(guid, out var p)) return p;
            EnsurePackages();
            return pkgGuidToPath.TryGetValue(guid, out p) ? p : null;
        }

        // package assets ("Packages/<name>/…", as Unity names them): embedded packages in Packages/ and Library/PackageCache,
        // indexed on the first lookup that misses Assets/
        static Dictionary<string, string> pkgGuidToPath, pkgPathToGuid, packageDirs;
        static string pkgRoot;
        static void EnsurePackages()
        {
            if (pkgGuidToPath != null && pkgRoot == Root) return;
            pkgRoot = Root; pkgGuidToPath = new(); pkgPathToGuid = new(); packageDirs = new();
            var dirs = new List<string>();
            foreach (var r in new[] { "Packages", "Library/PackageCache" })
                if (Directory.Exists(Path.Combine(Root, r))) dirs.AddRange(Directory.GetDirectories(Path.Combine(Root, r)));
            foreach (var dir in dirs)
            {
                string name = Path.GetFileName(dir);
                int at = name.IndexOf('@');
                if (at > 0) name = name.Substring(0, at);
                var pj = Path.Combine(dir, "package.json");
                if (File.Exists(pj))
                    try { using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(pj)); if (doc.RootElement.TryGetProperty("name", out var n)) name = n.GetString(); }
                    catch (Exception) { }
                if (packageDirs.ContainsKey(name)) continue;   // an embedded package (Packages/) wins over the cache
                packageDirs[name] = dir;
                foreach (var meta in Directory.EnumerateFiles(dir, "*.meta", SearchOption.AllDirectories))
                {
                    string guid = null;
                    foreach (var l in File.ReadLines(meta)) { if (l.StartsWith("guid: ")) { guid = l.Substring(6).Trim(); break; } }
                    if (guid == null) continue;
                    var rel = "Packages/" + name + "/" + Path.GetRelativePath(dir, meta[..^5]).Replace('\\', '/');
                    pkgGuidToPath.TryAdd(guid, rel); pkgPathToGuid[rel] = guid;
                }
            }
        }
        public static IEnumerable<string> AllPaths() { Ensure(); return pathToGuid.Keys.ToList(); }
        public static string Norm(string p) => p?.Replace('\\', '/').TrimEnd('/');

        public static string Register(string path, string guid = null)
        {
            Ensure();
            path = Norm(path);
            if (pathToGuid.TryGetValue(path, out var g)) return g;
            // Unity's import gives every new folder under Assets/ its own .meta; do the same for parents.
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && parent != "Assets" && parent.StartsWith("Assets/") && Directory.Exists(Abs(parent)))
                Register(parent);
            guid ??= Guid.NewGuid().ToString("N");
            guidToPath[guid] = path; pathToGuid[path] = guid;
            var metaPath = Abs(path) + ".meta";
            if (!File.Exists(metaPath))
            {
                SafeIO.WriteAllText(metaPath, $"fileFormatVersion: 2\nguid: {guid}\n" + MetaBody(path));
            }
            return guid;
        }
        static string MetaBody(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (Directory.Exists(Abs(path))) return "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
            if (ext is ".wav" or ".mp3" or ".ogg" or ".aif" or ".aiff")   // same importer block as the project's narration / BGM clips
                return "AudioImporter:\n  externalObjects: {}\n  serializedVersion: 7\n  defaultSettings:\n    serializedVersion: 2\n    loadType: 0\n    sampleRateSetting: 0\n    sampleRateOverride: 44100\n    compressionFormat: 1\n    quality: 1\n    conversionMode: 0\n    preloadAudioData: 0\n  platformSettingOverrides: {}\n  forceToMono: 0\n  normalize: 1\n  loadInBackground: 0\n  ambisonic: 0\n  3D: 1\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
            if (ext is ".png" or ".jpg" or ".jpeg" or ".tga" or ".psd")
                return "TextureImporter:\n  internalIDToNameTable: []\n  externalObjects: {}\n  serializedVersion: 12\n  mipmaps:\n    enableMipMap: 1\n  sRGBTexture: 1\n  isReadable: 0\n  maxTextureSize: 2048\n  textureSettings:\n    serializedVersion: 2\n    filterMode: 1\n    wrapU: 0\n    wrapV: 0\n    wrapW: 0\n  spriteMode: 0\n  spritePixelsToUnits: 100\n  spriteBorder: {x: 0, y: 0, z: 0, w: 0}\n  spritePivot: {x: 0.5, y: 0.5}\n  alphaIsTransparency: 0\n  textureType: 0\n  textureCompression: 1\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
            if (ext == ".prefab") return "PrefabImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
            if (ext == ".unity") return "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
            return "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: " + MainFileIdFor(ext) + "\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
        }
        static long MainFileIdFor(string ext) => ext switch { ".mat" => 2100000, ".anim" => 7400000, ".controller" => 9100000, ".overridecontroller" => 22100000, ".mask" => 31900000, ".physicsmaterial2d" => 6200000, ".physicmaterial" => 13400000, ".mixer" => 24100000, ".rendertexture" => 8400000, _ => 11400000 };
        public static void Remove(string path)
        {
            Ensure(); path = Norm(path);
            if (pathToGuid.TryGetValue(path, out var g)) { pathToGuid.Remove(path); guidToPath.Remove(g); }
        }
        public static YMap ReadMeta(string path)
        {
            var mp = Abs(path) + ".meta";
            return File.Exists(mp) ? YamlReader.ParseText(File.ReadAllText(mp)) : new YMap();
        }
    }

    /// <summary>Guards writes: only inside the scratch project, never through symlinks.</summary>
    public static class SafeIO
    {
        public static void Check(string path)
        {
            var full = Path.GetFullPath(path);
            if (!IsUnder(full, AssetIndex.Root)) throw new UnauthorizedAccessException("MiniUnity refuses to write outside the scratch project: " + full);
            var fi = new FileInfo(full);
            if (fi.Exists && fi.LinkTarget != null) throw new UnauthorizedAccessException("MiniUnity refuses to write through symlink: " + full);
        }
        /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or inside it ("/proj2" is not inside "/proj").</summary>
        public static bool IsUnder(string path, string root)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var r = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var cmp = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(full, r, cmp) || Path.EndsInDirectorySeparator(r)) return true;   // r ends in a separator only when it is a filesystem root
            return full.StartsWith(r + Path.DirectorySeparatorChar, cmp) || full.StartsWith(r + Path.AltDirectorySeparatorChar, cmp);
        }
        /// <summary>MINIUNITY_DRYRUN=1: run the builder without touching the project (layout check, experiments).</summary>
        public static bool DryRun = Environment.GetEnvironmentVariable("MINIUNITY_DRYRUN") == "1";
        public static void WriteAllText(string path, string text) { if (DryRun) return; Check(path); Directory.CreateDirectory(Path.GetDirectoryName(path)); AtomicWriteText(path, text); }
        public static void WriteAllBytes(string path, byte[] data) { if (DryRun) return; Check(path); Directory.CreateDirectory(Path.GetDirectoryName(path)); AtomicWriteBytes(path, data); }

        /// <summary>Write to a temp file next to the target, then rename over it: a crash mid-write never leaves a truncated scene / asset.</summary>
        public static void AtomicWriteText(string path, string text) => AtomicWriteBytes(path, new System.Text.UTF8Encoding(false).GetBytes(text));
        public static void AtomicWriteBytes(string path, byte[] data)
        {
            var tmp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
            try
            {
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(data, 0, data.Length);
                    fs.Flush(true);
                }
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
                throw;
            }
        }
    }

    public sealed class LoadedAsset
    {
        public string Path, Guid;
        public int RefType;
        public readonly Dictionary<long, Object> Objects = new();
        public readonly List<long> Order = new();
        public Object Main;
        public bool IsPrefab;
    }

    public sealed class MissingScriptObject : ScriptableObject
    {
        public YMap RawBody;
    }

    public static class AssetLoader
    {
        static readonly Dictionary<string, LoadedAsset> byPath = new();
        static readonly HashSet<string> loading = new();
        public static bool Verbose = Environment.GetEnvironmentVariable("MINIUNITY_VERBOSE") == "1";

        public static void Forget(string path) => byPath.Remove(AssetIndex.Norm(path));
        /// <summary>A moved asset keeps its loaded objects (AssetDatabase.MoveAsset): same objects, new path.</summary>
        internal static void Rename(string oldPath, string newPath)
        {
            oldPath = AssetIndex.Norm(oldPath); newPath = AssetIndex.Norm(newPath);
            if (!byPath.Remove(oldPath, out var la)) return;
            la.Path = newPath;
            foreach (var o in la.Objects.Values) if (o.m_Asset != null) o.m_Asset.Path = newPath;
            byPath[newPath] = la;
        }
        public static void Put(LoadedAsset a) => byPath[AssetIndex.Norm(a.Path)] = a;
        public static LoadedAsset Peek(string path) => byPath.TryGetValue(AssetIndex.Norm(path), out var a) ? a : null;

        public static Object LoadMain(string path) => Load(path)?.Main;

        public static LoadedAsset Load(string path)
        {
            path = AssetIndex.Norm(path);
            if (path == null) return null;
            if (byPath.TryGetValue(path, out var la)) return la;
            if (!File.Exists(AssetIndex.Abs(path))) return null;
            var guid = AssetIndex.GuidOf(path);
            if (guid == null && path.StartsWith("Assets/") && !File.Exists(AssetIndex.Abs(path) + ".meta"))
            {
                guid = AssetIndex.Register(path);   // like Unity's auto-import of a new file
                Console.Error.WriteLine($"[MiniUnity] imported new file {path} (.meta created)");
            }
            if (guid == null) return null;
            if (!loading.Add(path)) return null;
            try
            {
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                la = new LoadedAsset { Path = path, Guid = guid };
                byPath[path] = la; // early for cycles
                if (Verbose) Console.Error.WriteLine("[load] " + path);
                switch (ext)
                {
                    case ".asset": case ".mat": case ".playable": case ".prefab": case ".controller": case ".anim": case ".mask": case ".signal": case ".preset": case ".mixer": case ".physicsmaterial2d": case ".physicmaterial": case ".overridecontroller": case ".rendertexture":
                        la.RefType = ext == ".prefab" ? 3 : 2;
                        la.IsPrefab = ext == ".prefab";
                        if (la.IsPrefab && YamlReader.ReadFile(AssetIndex.Abs(path)) is var pdocs && pdocs.Any(d => d.ClassId == 1001))
                        {
                            // nested prefabs / prefab variant: bake the instances (as scenes are), then connect them to their prefabs
                            var instances = new List<Agent.RuntimeHost.FlatInstance>();
                            LoadYaml(la, Agent.RuntimeHost.Flatten(Agent.UFile.Load(path), instances));
                            Agent.RuntimeHost.Reconnect(la, instances);
                        }
                        else LoadYaml(la);
                        break;
                    default:
                        la.RefType = 3;
                        Importers.Import(la, ext);
                        break;
                }
                return la;
            }
            finally { loading.Remove(path); }
        }

        /// <summary>Deserialize an in-memory document set (agent runtime: flattened scenes) into live objects.</summary>
        public static LoadedAsset LoadDocs(string pseudoPath, string guid, List<YDoc> docs)
        {
            var la = new LoadedAsset { Path = pseudoPath, Guid = guid, RefType = 3 };
            LoadYaml(la, docs);
            return la;
        }

        /// <summary>Objects of an asset from documents read elsewhere (a binary import artifact).</summary>
        internal static void LoadDocsInto(LoadedAsset la, List<YDoc> docs) => LoadYaml(la, docs);

        static void LoadYaml(LoadedAsset la, List<YDoc> preloaded = null)
        {
            var docs = preloaded ?? YamlReader.ReadFile(AssetIndex.Abs(la.Path));
            // pass 1: create
            var bodies = new Dictionary<long, YDoc>();
            foreach (var d in docs)
            {
                if (d.Stripped || d.ClassId == 1001) continue;
                Object o = CreateForDoc(d);
                if (o == null) continue;
                o.m_Asset = new AssetIdentity { Path = la.Path, Guid = la.Guid, FileID = d.FileId, RefType = la.RefType };
                la.Objects[d.FileId] = o;
                la.Order.Add(d.FileId);
                bodies[d.FileId] = d;
            }
            // pass 2: deserialize
            Object Resolve(YRef r, Type expected)
            {
                if (r.FileID == 0) return null;
                if (string.IsNullOrEmpty(r.Guid)) return la.Objects.TryGetValue(r.FileID, out var lo) ? lo : null;
                if (r.Guid == la.Guid) return la.Objects.TryGetValue(r.FileID, out var lo2) ? lo2 : null;
                return ResolveGuid(r.Guid, r.FileID, expected);
            }
            foreach (var id in la.Order)
            {
                var o = la.Objects[id];
                var d = bodies[id];
                if (o is MissingScriptObject ms) { ms.RawBody = d.Body; continue; }
                try { Serializer.Deserialize(o, d.Body, Resolve); }
                catch (Exception e) { Debug.LogWarning($"[load] {la.Path}#{id} ({o.GetType().Name}): {e.Message}"); }
            }
            // Unity writes m_LocalPosition.x/y of a RectTransform as 0 and derives them from anchors / anchoredPosition on load
            foreach (var id in la.Order)
                if (la.Objects[id] is RectTransform rt) rt.m_LocalPositionRaw = rt.ComputeLocalPosition();
            // main object
            if (la.IsPrefab)
            {
                foreach (var o in la.Objects.Values)
                    if (o is GameObject go) { go.m_InPrefabAsset = true; }
                la.Main = la.Objects.Values.OfType<GameObject>().FirstOrDefault(g => g.m_Transform != null && g.m_Transform.m_Parent == null);
            }
            else
            {
                var meta = AssetIndex.ReadMeta(la.Path);
                long mainId = meta["NativeFormatImporter"] is YMap nf && nf["mainObjectFileID"] is YScalar ms ? ms.L : 0;
                if (mainId != 0 && la.Objects.TryGetValue(mainId, out var mo)) la.Main = mo;
                else if (la.Objects.TryGetValue(11400000, out var m1)) la.Main = m1;
                else if (la.Objects.TryGetValue(2100000, out var m2)) la.Main = m2;
                else if (la.Objects.Values.OfType<UnityEngine.Audio.AudioMixer>().FirstOrDefault() is Object mx) la.Main = mx;
                else la.Main = la.Order.Count > 0 ? la.Objects[la.Order[0]] : null;
                // ScriptableObject load callbacks
                foreach (var id in la.Order)
                    if (la.Objects[id] is ScriptableObject so && !(so is MissingScriptObject))
                    {
                        try { Lifecycle.Invoke(so, "Awake"); Lifecycle.Invoke(so, "OnEnable"); }
                        catch (Exception e) { Debug.LogWarning($"[load] OnEnable {so.GetType().Name}: {e.Message}"); }
                    }
            }
        }

        public static Object CreateForDoc(YDoc d)
        {
            if (d.ClassId == 114)
            {
                var sref = d.Body["m_Script"] as YRef;
                var t = sref != null ? ScriptRegistry.TypeOf(sref.Guid, sref.FileID) : null;
                bool isComponent = d.Body["m_GameObject"] is YRef gr && gr.FileID != 0;
                if (t == null)
                {
                    if (Verbose) Console.Error.WriteLine($"[load] unknown script guid {sref?.Guid}");
                    return isComponent ? null : Lifecycle.CreateScriptableObject(typeof(MissingScriptObject), false);
                }
                if (typeof(ScriptableObject).IsAssignableFrom(t)) return Lifecycle.CreateScriptableObject(t, false);
                return (Object)Activator.CreateInstance(t, true);
            }
            if (Serializer.TypeById.TryGetValue(d.ClassId, out var nt))
            {
                if (nt == typeof(GameObject)) return CreateBareGameObject();
                return (Object)Activator.CreateInstance(nt, true);
            }
            if (Verbose) Console.Error.WriteLine($"[load] unsupported class {d.ClassId} {d.TypeName}");
            return null;
        }

        [ThreadStatic] internal static bool s_BareGO;
        public static GameObject CreateBareGameObject()
        {
            s_BareGO = true;
            try { return new GameObject(""); }
            finally { s_BareGO = false; }
        }

        public static Object ResolveExternal(YRef r)
        {
            if (r == null || r.FileID == 0) return null;
            return ResolveGuid(r.Guid, r.FileID, typeof(Object));
        }

        static readonly Dictionary<(string, long), Object> builtinCache = new();
        public static Object ResolveGuid(string guid, long fileID, Type expected)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            if (guid == "0000000000000000f000000000000000" || guid == "0000000000000000e000000000000000" || guid == "0000000000000000d000000000000000")
            {
                if (builtinCache.TryGetValue((guid, fileID), out var b)) return b;
                if (guid == "0000000000000000e000000000000000" && fileID >= 10202 && fileID <= 10210)
                {
                    var pt = fileID switch { 10202 => PrimitiveType.Cube, 10207 => PrimitiveType.Sphere, 10208 => PrimitiveType.Capsule, 10206 => PrimitiveType.Cylinder, 10209 => PrimitiveType.Plane, 10210 => PrimitiveType.Quad, _ => (PrimitiveType)(-1) };
                    if ((int)pt >= 0) return builtinCache[(guid, fileID)] = BuiltinResources.PrimitiveMesh(pt, fileID);
                }
                if (guid == "0000000000000000f000000000000000" && fileID == 10303) return builtinCache[(guid, fileID)] = BuiltinResources.DefaultMaterial;
                if (guid == "0000000000000000f000000000000000" && fileID == 10304) return builtinCache[(guid, fileID)] = BuiltinResources.DefaultSkybox;
                Type t = expected;
                if (t == null || t.IsAbstract || t == typeof(Object)) t = fileID switch { 10770 or 10753 or 46 or 10755 or 10752 or 10750 or 10751 or 211 or 17 or 103 or 104 or 106 or 108 => typeof(Shader), 10754 or 10303 or 10304 or 10302 => typeof(Material), 10905 or 10906 or 10907 or 10908 or 10910 or 10911 or 10913 or 10915 or 10917 => typeof(Sprite), 10102 or 10100 => typeof(Font), _ => typeof(Texture2D) };
                if (!typeof(Object).IsAssignableFrom(t) || t.IsAbstract) return null;
                Object o;
                try { o = (Object)Activator.CreateInstance(t, true); } catch { return null; }
                o.m_Asset = new AssetIdentity { Guid = guid, FileID = fileID, RefType = 0, Path = "builtin" };
                if (o is Shader sh && ShaderIndex.BuiltinName(guid, fileID) is string sn) { sh.m_ShaderName = sn; sh.m_Name = sn; }
                builtinCache[(guid, fileID)] = o;
                return o;
            }
            var path = AssetIndex.PathOf(guid);
            if (path == null) { if (Verbose) Console.Error.WriteLine($"[load] missing guid {guid}"); return null; }
            var la = Load(path);
            if (la == null) return null;
            return la.Objects.TryGetValue(fileID, out var res) ? res : null;
        }

        public static Object LoadAssetAtPath(string path, Type type)
        {
            var la = Load(path);
            if (la == null) return null;
            if (type == null || type == typeof(Object)) return la.Main;
            if (la.Main != null && type.IsInstanceOfType(la.Main)) return la.Main;
            if (la.IsPrefab && la.Main is GameObject go && typeof(Component).IsAssignableFrom(type)) return go.GetComponent(type);
            foreach (var id in la.Order) if (type.IsInstanceOfType(la.Objects[id])) return la.Objects[id];
            return null;
        }
    }
}
