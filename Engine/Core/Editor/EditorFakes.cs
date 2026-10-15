using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniUnity;
using MiniUnity.Yaml;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    public sealed partial class AssetDatabase
    {
        internal static readonly HashSet<string> dirtyAssets = new();
        /// <summary>MiniUnity deviation (default on): DeleteAsset+CreateAsset on the same path keeps the old GUID so rebuilt
        /// timelines don't churn .meta files / scene references. Set MINIUNITY_FRESH_GUIDS=1 for Unity's behaviour.</summary>
        static readonly Dictionary<string, string> deletedGuids = new();
        static readonly bool freshGuids = Environment.GetEnvironmentVariable("MINIUNITY_FRESH_GUIDS") == "1";

        public static T LoadAssetAtPath<T>(string assetPath) where T : Object => (T)AssetLoader.LoadAssetAtPath(assetPath, typeof(T));
        public static Object LoadAssetAtPath(string assetPath, Type type) => AssetLoader.LoadAssetAtPath(assetPath, type);
        public static Object LoadMainAssetAtPath(string assetPath) => AssetLoader.LoadMain(assetPath);
        public static Object[] LoadAllAssetsAtPath(string assetPath) { var la = AssetLoader.Load(assetPath); return la == null ? Array.Empty<Object>() : la.Order.Select(i => la.Objects[i]).ToArray(); }
        public static Object[] LoadAllAssetRepresentationsAtPath(string assetPath) => LoadAllAssetsAtPath(assetPath).Where(o => o != AssetLoader.LoadMain(assetPath)).ToArray();

        public static void CreateAsset(Object asset, string path)
        {
            path = AssetIndex.Norm(path);
            if (asset.m_Asset != null) throw new InvalidOperationException("Asset already persistent: " + asset);
            if (File.Exists(AssetIndex.Abs(path))) DeleteAsset(path);
            deletedGuids.TryGetValue(path, out var oldGuid);
            var guid = AssetIndex.Register(path, freshGuids ? null : oldGuid);
            long main = asset switch { Material => 2100000, AnimationClip => 7400000, Texture2D => 2800000, RuntimeAnimatorController and not AnimatorOverrideController => 9100000, AnimatorOverrideController => 22100000, AvatarMask => 31900000, PhysicsMaterial2D => 6200000, PhysicMaterial => 13400000, RenderTexture => 8400000, _ => 11400000 };
            if (string.IsNullOrEmpty(asset.m_Name)) asset.m_Name = Path.GetFileNameWithoutExtension(path);
            asset.m_Asset = new AssetIdentity { Path = path, Guid = guid, FileID = main, RefType = 2 };
            var la = new LoadedAsset { Path = path, Guid = guid, RefType = 2, Main = asset };
            la.Objects[main] = asset; la.Order.Add(main);
            AssetLoader.Put(la);
            WriteAsset(path);
        }

        public static void AddObjectToAsset(Object objectToAdd, Object assetObject)
        {
            if (assetObject?.m_Asset == null) throw new ArgumentException("AddObjectToAsset: target is not persistent");
            AddObjectToAsset(objectToAdd, assetObject.m_Asset.Path);
        }
        public static void AddObjectToAsset(Object objectToAdd, string path)
        {
            path = AssetIndex.Norm(path);
            var la = AssetLoader.Load(path) ?? throw new ArgumentException("AddObjectToAsset: no asset at " + path);
            if (objectToAdd.m_Asset != null) return;
            long id; do { id = FileIds.Random64(); } while (la.Objects.ContainsKey(id));
            objectToAdd.m_Asset = new AssetIdentity { Path = path, Guid = la.Guid, FileID = id, RefType = la.RefType };
            la.Objects[id] = objectToAdd; la.Order.Add(id);
            dirtyAssets.Add(path);
        }
        public static void RemoveObjectFromAsset(Object objectToRemove)
        {
            var a = objectToRemove?.m_Asset; if (a == null) return;
            var la = AssetLoader.Peek(a.Path);
            if (la != null) { la.Objects.Remove(a.FileID); la.Order.Remove(a.FileID); dirtyAssets.Add(a.Path); }
            objectToRemove.m_Asset = null;
        }

        internal static void WriteAsset(string path)
        {
            var la = AssetLoader.Peek(path);
            if (la == null) return;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".prefab" || ext == ".unity") return;
            var objs = la.Order.Select(i => la.Objects[i]).Where(o => o != null && !o.m_Destroyed && o.m_Asset != null && o.m_Asset.Path == path).ToList();
            foreach (var clip in objs.OfType<AnimationClip>()) clip.BakeScriptCurves();
            SafeIO.WriteAllText(AssetIndex.Abs(path), AssetWriter.Write(path, objs));
        }

        public static void SaveAssets()
        {
            foreach (var p in dirtyAssets.ToList()) WriteAsset(p);
            dirtyAssets.Clear();
        }
        public static void SaveAssetIfDirty(Object obj) { if (obj?.m_Asset != null && dirtyAssets.Remove(obj.m_Asset.Path)) WriteAsset(obj.m_Asset.Path); }
        public static void SaveAssetIfDirty(GUID guid) { }

        public static bool DeleteAsset(string path)
        {
            path = AssetIndex.Norm(path);
            var abs = AssetIndex.Abs(path);
            if (!File.Exists(abs) && !Directory.Exists(abs)) return false;
            var la = AssetLoader.Peek(path);
            if (la != null) foreach (var o in la.Objects.Values) { o.m_Asset = null; }
            AssetLoader.Forget(path);
            var g = AssetIndex.GuidOf(path); if (g != null) deletedGuids[path] = g;
            AssetIndex.Remove(path);
            SafeIO.Check(abs);
            if (SafeIO.DryRun) { dirtyAssets.Remove(path); return true; }
            if (File.Exists(abs)) File.Delete(abs);
            if (File.Exists(abs + ".meta")) File.Delete(abs + ".meta");
            dirtyAssets.Remove(path);
            return true;
        }
        public static void Refresh() { }
        public static void Refresh(ImportAssetOptions options) { }
        public static void ImportAsset(string path) => ImportAsset(path, ImportAssetOptions.Default);
        public static void ImportAsset(string path, ImportAssetOptions options)
        {
            path = AssetIndex.Norm(path);
            if (AssetIndex.GuidOf(path) == null && File.Exists(AssetIndex.Abs(path))) AssetIndex.Register(path);
            // re-import: keep the identity of already loaded objects, refresh what the importer derives from the file
            var la = AssetLoader.Peek(path);
            if (la != null)
                foreach (var o in la.Objects.Values)
                {
                    if (o is AudioClip ac && path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) Importers.ReadWav(AssetIndex.Abs(path), ac);
                    if (o is Texture2D tx) { var (w, h) = Importers.ImageSize(AssetIndex.Abs(path)); tx.m_W = w; tx.m_H = h; }
                }
        }
        public static string GetAssetPath(Object assetObject) => assetObject?.m_Asset?.Path ?? "";
        public static string GetAssetPath(int instanceID) => GetAssetPath(Object.FindObjectFromInstanceID(instanceID));
        public static bool Contains(Object obj) => obj != null && obj.m_Asset != null;
        public static bool Contains(int instanceID) => Contains(Object.FindObjectFromInstanceID(instanceID));
        public static bool IsMainAsset(Object obj) => obj?.m_Asset != null && AssetLoader.Peek(obj.m_Asset.Path)?.Main == obj;
        public static bool IsSubAsset(Object obj) => obj?.m_Asset != null && !IsMainAsset(obj);
        public static string GUIDToAssetPath(string guid) => AssetIndex.PathOf(guid) ?? "";
        public static string AssetPathToGUID(string path) => AssetIndex.GuidOf(path) ?? "";
        public static bool TryGetGUIDAndLocalFileIdentifier(Object obj, out string guid, out long localId)
        {
            guid = obj?.m_Asset?.Guid; localId = obj?.m_Asset?.FileID ?? 0; return guid != null;
        }
        public static bool TryGetGUIDAndLocalFileIdentifier(int instanceID, out string guid, out long localId) => TryGetGUIDAndLocalFileIdentifier(Object.FindObjectFromInstanceID(instanceID), out guid, out localId);
        public static void StartAssetEditing() { }
        public static void StopAssetEditing() { }
        public static void AllowAutoRefresh() { }
        public static void DisallowAutoRefresh() { }
        public static bool IsValidFolder(string path) => Directory.Exists(AssetIndex.Abs(path));
        public static string CreateFolder(string parentFolder, string newFolderName)
        {
            var p = parentFolder + "/" + newFolderName; Directory.CreateDirectory(AssetIndex.Abs(p)); return AssetIndex.Register(p);
        }
        public static string[] FindAssets(string filter) => FindAssets(filter, null);
        public static string[] FindAssets(string filter, string[] searchInFolders)
        {
            string typeName = null; var words = new List<string>();
            foreach (var tok in (filter ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (tok.StartsWith("t:")) typeName = tok[2..]; else words.Add(tok);
            var res = new List<string>();
            foreach (var p in AssetIndex.AllPaths())
            {
                if (searchInFolders != null && !searchInFolders.Any(f => p.StartsWith(f.TrimEnd('/') + "/"))) continue;
                if (Directory.Exists(AssetIndex.Abs(p))) continue;
                var file = Path.GetFileNameWithoutExtension(p);
                if (words.Any(w => file.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                if (typeName != null)
                {
                    var ext = Path.GetExtension(p).ToLowerInvariant();
                    bool ok = typeName switch
                    {
                        "Prefab" or "GameObject" => ext == ".prefab",
                        "Scene" or "SceneAsset" => ext == ".unity",
                        "Texture2D" or "Texture" or "Sprite" => ext is ".png" or ".jpg",
                        "AudioClip" => ext is ".wav" or ".mp3" or ".ogg",
                        "Material" => ext == ".mat",
                        "TimelineAsset" => ext == ".playable",
                        _ => ext == ".asset" && (AssetLoader.LoadMain(p)?.GetType().Name == typeName),
                    };
                    if (!ok) continue;
                }
                res.Add(AssetIndex.GuidOf(p));
            }
            return res.ToArray();
        }
        public static string GetTextMetaFilePathFromAssetPath(string path) => path + ".meta";
        public static Type GetMainAssetTypeAtPath(string assetPath) => AssetLoader.LoadMain(assetPath)?.GetType();
        public static string[] GetDependencies(string pathName) => new[] { pathName };
        public static string[] GetDependencies(string pathName, bool recursive) => new[] { pathName };
        public static bool IsOpenForEdit(string assetOrMetaFilePath) => true;
        /// <summary>Move / rename with the .meta (GUID kept, so references stay valid); "" on success, otherwise the error, as Unity.</summary>
        public static string MoveAsset(string oldPath, string newPath)
        {
            oldPath = AssetIndex.Norm(oldPath); newPath = AssetIndex.Norm(newPath);
            var src = AssetIndex.Abs(oldPath); var dst = AssetIndex.Abs(newPath);
            if (!File.Exists(src) && !Directory.Exists(src)) return $"Source asset could not be found [{oldPath}]";
            if (File.Exists(dst) || Directory.Exists(dst)) return $"Destination path name does already exist [{newPath}]";
            if (!Directory.Exists(Path.GetDirectoryName(dst))) return $"Parent directory is not in asset database [{newPath}]";
            var guid = AssetIndex.GuidOf(oldPath);
            SafeIO.Check(dst);
            if (!SafeIO.DryRun)
            {
                if (Directory.Exists(src)) Directory.Move(src, dst); else File.Move(src, dst);
                if (File.Exists(src + ".meta")) File.Move(src + ".meta", dst + ".meta");
            }
            AssetLoader.Rename(oldPath, newPath);
            AssetIndex.Remove(oldPath);
            AssetIndex.Register(newPath, guid);
            if (dirtyAssets.Remove(oldPath)) dirtyAssets.Add(newPath);
            return "";
        }
        /// <summary>Duplicate a file asset under a new GUID.</summary>
        public static bool CopyAsset(string path, string newPath)
        {
            path = AssetIndex.Norm(path); newPath = AssetIndex.Norm(newPath);
            var src = AssetIndex.Abs(path); var dst = AssetIndex.Abs(newPath);
            if (!File.Exists(src)) { Debug.LogError($"Couldn't copy asset {path}: source does not exist"); return false; }
            if (File.Exists(dst) || !Directory.Exists(Path.GetDirectoryName(dst))) { Debug.LogError($"Couldn't copy asset to {newPath}"); return false; }
            var guid = Guid.NewGuid().ToString("N");
            SafeIO.Check(dst);
            if (!SafeIO.DryRun)
            {
                File.Copy(src, dst);
                if (File.Exists(src + ".meta"))
                    File.WriteAllText(dst + ".meta", System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(src + ".meta"), @"^guid: [0-9a-f]{32}", "guid: " + guid, System.Text.RegularExpressions.RegexOptions.Multiline));
            }
            AssetIndex.Register(newPath, guid);
            return true;
        }
        public static void ForceReserializeAssets() { }
        public static void ReleaseCachedFileHandles() { }
    }

    public sealed partial class EditorUtility
    {
        public static void SetDirty(Object target)
        {
            if (target?.m_Asset != null && target.m_Asset.RefType == 2) AssetDatabase.dirtyAssets.Add(target.m_Asset.Path);
        }
        public static bool IsPersistent(Object target) => target != null && target.m_Asset != null;
        public static bool DisplayDialog(string title, string message, string ok) { Debug.Log($"[Dialog] {title}: {message}"); return true; }
        public static bool DisplayDialog(string title, string message, string ok, string cancel) { Debug.Log($"[Dialog] {title}: {message} → {ok}"); return true; }
        public static int DisplayDialogComplex(string title, string message, string ok, string cancel, string alt) => 0;
        public static void DisplayProgressBar(string title, string info, float progress) { }
        public static bool DisplayCancelableProgressBar(string title, string info, float progress) => false;
        public static void ClearProgressBar() { }
        public static void RevealInFinder(string path) { }
        public static string SaveFilePanelInProject(string title, string defaultName, string extension, string message) => "";
        public static void CopySerialized(Object source, Object dest)
        {
            var body = Serializer.Serialize(source);
            Serializer.Deserialize(dest, body, (r, t) => r.Raw ? AssetLoader.ResolveExternal(r) : r.Target);
        }
        public static Object InstanceIDToObject(int instanceID) => Object.FindObjectFromInstanceID(instanceID);
        public static bool IsDirty(Object target) => false;
        public static void ClearDirty(Object target) { }
        public static string FormatBytes(long bytes) => bytes + " B";
        public static void UnloadUnusedAssetsImmediate() { }
        public static void RequestScriptReload() { }
        public static bool scriptCompilationFailed => false;
    }

    public partial class Undo
    {
        public static void RecordObject(Object objectToUndo, string name) { }
        public static void RecordObjects(Object[] objectsToUndo, string name) { }
        public static void RegisterCompleteObjectUndo(Object objectToUndo, string name) { }
        public static void RegisterCompleteObjectUndo(Object[] objectsToUndo, string name) { }
        public static void RegisterCreatedObjectUndo(Object objectToUndo, string name) { }
        public static void DestroyObjectImmediate(Object objectToUndo) => Object.DestroyImmediate(objectToUndo);
        public static void SetTransformParent(Transform transform, Transform newParent, string name) => transform.SetParent(newParent, true);
        public static void SetTransformParent(Transform transform, Transform newParent, bool worldPositionStays, string name) => transform.SetParent(newParent, worldPositionStays);
        public static T AddComponent<T>(GameObject go) where T : Component => go.AddComponent<T>();
        public static Component AddComponent(GameObject go, Type t) => go.AddComponent(t);
        public static void IncrementCurrentGroup() { }
        public static int GetCurrentGroup() => 0;
        public static void CollapseUndoOperations(int groupIndex) { }
        public static void SetCurrentGroupName(string name) { }
        public static void ClearAll() { }
        public static void PerformUndo() { }
        public static void PerformRedo() { }
        public static void FlushUndoRecordObjects() { }
        public static void RegisterFullObjectHierarchyUndo(Object objectToUndo, string name) { }
        public static void ClearUndo(Object identifier) { }
    }

    public sealed partial class Selection
    {
        static Object s_Active;
        public static Object activeObject { get => s_Active; set => s_Active = value; }
        public static GameObject activeGameObject { get => s_Active as GameObject; set => s_Active = value; }
        public static Transform activeTransform { get => (s_Active as GameObject)?.transform; set => s_Active = value?.gameObject; }
        public static Object[] objects { get => s_Active != null ? new[] { s_Active } : Array.Empty<Object>(); set => s_Active = value?.FirstOrDefault(); }
        public static GameObject[] gameObjects => s_Active is GameObject g ? new[] { g } : Array.Empty<GameObject>();
        public static int activeInstanceID { get => s_Active?.GetInstanceID() ?? 0; set { } }
    }

    public sealed partial class EditorPrefs
    {
        static readonly Dictionary<string, object> store = new();
        public static void SetInt(string key, int value) => store[key] = value;
        public static int GetInt(string key, int defaultValue) => store.TryGetValue(key, out var v) && v is int i ? i : defaultValue;
        public static int GetInt(string key) => GetInt(key, 0);
        public static void SetFloat(string key, float value) => store[key] = value;
        public static float GetFloat(string key, float defaultValue) => store.TryGetValue(key, out var v) && v is float f ? f : defaultValue;
        public static float GetFloat(string key) => GetFloat(key, 0);
        public static void SetString(string key, string value) => store[key] = value;
        public static string GetString(string key, string defaultValue) => store.TryGetValue(key, out var v) && v is string s ? s : defaultValue;
        public static string GetString(string key) => GetString(key, "");
        public static void SetBool(string key, bool value) => store[key] = value;
        public static bool GetBool(string key, bool defaultValue) => store.TryGetValue(key, out var v) && v is bool b ? b : defaultValue;
        public static bool GetBool(string key) => GetBool(key, false);
        public static bool HasKey(string key) => store.ContainsKey(key);
        public static void DeleteKey(string key) => store.Remove(key);
        public static void DeleteAll() => store.Clear();
    }

    public partial class SessionState
    {
        static readonly Dictionary<string, object> store = new();
        public static void SetString(string key, string value) => store[key] = value;
        public static string GetString(string key, string defaultValue) => store.TryGetValue(key, out var v) && v is string s ? s : defaultValue;
        public static void EraseString(string key) => store.Remove(key);
        public static void SetFloat(string key, float value) => store[key] = value;
        public static float GetFloat(string key, float defaultValue) => store.TryGetValue(key, out var v) && v is float f ? f : defaultValue;
        public static void SetBool(string key, bool value) => store[key] = value;
        public static bool GetBool(string key, bool defaultValue) => store.TryGetValue(key, out var v) && v is bool b ? b : defaultValue;
        public static void SetInt(string key, int value) => store[key] = value;
        public static int GetInt(string key, int defaultValue) => store.TryGetValue(key, out var v) && v is int i ? i : defaultValue;
        public static void EraseInt(string key) => store.Remove(key);
        public static void EraseBool(string key) => store.Remove(key);
        public static void EraseFloat(string key) => store.Remove(key);
    }

    public sealed partial class EditorApplication
    {
        public static bool isPlaying { get => MiniUnity.Play.IsPlaying; set { } }   // true while ua play / PlayMode tests run
        public static bool isPlayingOrWillChangePlaymode => false;
        public static bool isPaused { get => false; set { } }
        public static bool isCompiling => false;
        public static bool isUpdating => false;
        public static bool isRemoteConnected => false;
        public static double timeSinceStartup => (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalSeconds;
        public static CallbackFunction delayCall;
        public static CallbackFunction update;
        public static CallbackFunction hierarchyChanged_ { get; set; }
        public static event Action<PlayModeStateChange> playModeStateChanged { add { } remove { } }
        public static event Action hierarchyChanged { add { } remove { } }
        public static event Action projectChanged { add { } remove { } }
        public static void QueuePlayerLoopUpdate() { }
        public static void RepaintHierarchyWindow() { }
        public static void RepaintProjectWindow() { }
        public static void EnterPlaymode() => throw new NotSupportedException("Play mode is not available in MiniUnity");
        public static void ExitPlaymode() { }
        public static void Exit(int returnValue) => Environment.Exit(returnValue);
        public static void Beep() { }
        public static string applicationPath => "/opt/unity/Editor/Unity";
        public static string applicationContentsPath => "/opt/unity/Editor/Data";
    }

    public partial class AssetImporter
    {
        internal string m_AssetPath;
        internal YMap m_Meta;
        public string assetPath => m_AssetPath;
        public string userData { get; set; } = "";
        public string assetBundleName { get; set; } = "";
        public string assetBundleVariant { get; set; } = "";
        public static AssetImporter GetAtPath(string path)
        {
            path = AssetIndex.Norm(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            AssetImporter imp = ext is ".png" or ".jpg" or ".jpeg" or ".tga" or ".psd" ? new TextureImporter() : new AssetImporter();
            imp.m_AssetPath = path;
            imp.m_Meta = AssetIndex.ReadMeta(path);
            if (imp is TextureImporter ti) ti.LoadFromMeta();
            return imp;
        }
        public void SaveAndReimport()
        {
            if (this is TextureImporter ti) ti.SaveToMeta();
        }
        public void SetAssetBundleNameAndVariant(string a, string b) { }
    }

    public partial class TextureImporter
    {
        YMap TI => m_Meta?["TextureImporter"] as YMap;
        public TextureImporterType textureType { get; set; }
        public SpriteImportMode spriteImportMode { get; set; }
        public bool alphaIsTransparency { get; set; }
        public bool mipmapEnabled { get; set; }
        public TextureWrapMode wrapMode { get; set; }
        public TextureImporterCompression textureCompression { get; set; }
        public Vector4 spriteBorder { get; set; }
        public float spritePixelsPerUnit { get; set; } = 100;
        public Vector2 spritePivot { get; set; } = new Vector2(.5f, .5f);
        public bool isReadable { get; set; }
        public FilterMode filterMode { get; set; } = FilterMode.Bilinear;
        public int maxTextureSize { get; set; } = 2048;
        public bool sRGBTexture { get; set; } = true;
        internal void LoadFromMeta()
        {
            var t = TI; if (t == null) return;
            textureType = (TextureImporterType)Serializer.Int(t["textureType"], 0);
            spriteImportMode = (SpriteImportMode)Serializer.Int(t["spriteMode"], 0);
            alphaIsTransparency = Serializer.Int(t["alphaIsTransparency"], 0) != 0;
            mipmapEnabled = Serializer.Int((t["mipmaps"] as YMap)?["enableMipMap"], 1) != 0;
            var b = t["spriteBorder"] as YMap;
            spriteBorder = new Vector4(Serializer.F(b?["x"]), Serializer.F(b?["y"]), Serializer.F(b?["z"]), Serializer.F(b?["w"]));
            if (t["spritePixelsToUnits"] is YScalar ppu) spritePixelsPerUnit = ppu.F;
            if (t["spritePivot"] != null) spritePivot = Serializer.ReadV2(t["spritePivot"]);
            isReadable = Serializer.Int(t["isReadable"], 0) != 0;
            sRGBTexture = Serializer.Int((t["mipmaps"] as YMap)?["sRGBTexture"], 1) != 0;   // Unity keeps it in the mipmaps block
            maxTextureSize = Serializer.Int(t["maxTextureSize"], 2048);
            if (t["textureSettings"] is YMap ts)
            {
                filterMode = (FilterMode)Serializer.Int(ts["filterMode"], 1);
                wrapMode = (TextureWrapMode)Serializer.Int(ts["wrapU"], 0);
            }
            textureCompression = (TextureImporterCompression)Serializer.Int(DefaultPlatform(t)?["textureCompression"], 1);
        }

        /// <summary>Write the settings into the .meta (like Unity) when they changed, and re-import the texture.</summary>
        /// <summary>The DefaultTexturePlatform entry of platformSettings (where Unity stores textureCompression).</summary>
        static YMap DefaultPlatform(YMap t) => (t["platformSettings"] as YSeq)?.Items.OfType<YMap>().FirstOrDefault(m => (m["buildTarget"] as YScalar)?.Value == "DefaultTexturePlatform");

        internal void SaveToMeta()
        {
            m_Meta ??= new YMap();
            var t = TI;
            if (t == null) { t = new YMap(); t.Add("internalIDToNameTable", new YSeq { Flow = true }).Add("externalObjects", new YMap { Flow = true }).Add("serializedVersion", "12"); m_Meta["TextureImporter"] = t; }
            string before = MiniUnity.Yaml.YamlWriter.Dump(m_Meta);
            void Set(YMap m, string k, YNode v) => m[k] = v;
            var mips = t["mipmaps"] as YMap ?? new YMap(); Set(mips, "enableMipMap", YScalar.Of(mipmapEnabled)); Set(t, "mipmaps", mips);
            Set(mips, "sRGBTexture", YScalar.Of(sRGBTexture));
            Set(t, "isReadable", YScalar.Of(isReadable));
            Set(t, "maxTextureSize", YScalar.Of(maxTextureSize));
            var ts = t["textureSettings"] as YMap ?? new YMap().Add("serializedVersion", "2");
            Set(ts, "filterMode", YScalar.Of((int)filterMode)); Set(ts, "wrapU", YScalar.Of((int)wrapMode)); Set(ts, "wrapV", YScalar.Of((int)wrapMode)); Set(ts, "wrapW", YScalar.Of((int)wrapMode));
            Set(t, "textureSettings", ts);
            Set(t, "spriteMode", YScalar.Of((int)spriteImportMode));
            Set(t, "spritePixelsToUnits", YScalar.Of(spritePixelsPerUnit));
            Set(t, "spriteBorder", new YMap { Flow = true }.Add("x", YScalar.Of(spriteBorder.x)).Add("y", YScalar.Of(spriteBorder.y)).Add("z", YScalar.Of(spriteBorder.z)).Add("w", YScalar.Of(spriteBorder.w)));
            Set(t, "spritePivot", new YMap { Flow = true }.Add("x", YScalar.Of(spritePivot.x)).Add("y", YScalar.Of(spritePivot.y)));
            Set(t, "alphaIsTransparency", YScalar.Of(alphaIsTransparency));
            Set(t, "textureType", YScalar.Of((int)textureType));
            var platform = DefaultPlatform(t);
            if (platform == null)
            {
                platform = new YMap().Add("serializedVersion", "3").Add("buildTarget", "DefaultTexturePlatform").Add("maxTextureSize", YScalar.Of(maxTextureSize));
                if (t["platformSettings"] is not YSeq ps) Set(t, "platformSettings", ps = new YSeq());
                ps.Items.Add(platform);
            }
            Set(platform, "textureCompression", YScalar.Of((int)textureCompression));
            if (MiniUnity.Yaml.YamlWriter.Dump(m_Meta) == before) return;
            SafeIO.WriteAllText(AssetIndex.Abs(m_AssetPath) + ".meta", MiniUnity.Yaml.YamlWriter.Dump(m_Meta));
            AssetLoader.Forget(m_AssetPath);
        }
    }

    public partial class SerializedObject
    {
        internal Object[] m_Targets;
        internal YMap m_Body;
        public SerializedObject(Object obj) { m_Targets = new[] { obj }; m_Body = Serializer.Serialize(obj); }
        public SerializedObject(Object[] objs) { m_Targets = objs; m_Body = Serializer.Serialize(objs[0]); }
        public SerializedObject(Object obj, Object context) : this(obj) { }
        public Object targetObject => m_Targets[0];
        public Object[] targetObjects => m_Targets;
        public SerializedProperty FindProperty(string propertyPath)
        {
            YMap parent = m_Body; string key = null;
            var parts = propertyPath.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                key = parts[i];
                if (i == parts.Length - 1) break;
                if (parent[key] is not YMap next) return null;
                parent = next;
            }
            if (parent[key] == null) return null;
            return new SerializedProperty { m_Owner = this, m_Parent = parent, m_Key = key, m_Path = propertyPath };
        }
        public bool ApplyModifiedProperties() => ApplyModifiedPropertiesWithoutUndo();
        public bool ApplyModifiedPropertiesWithoutUndo()
        {
            foreach (var t in m_Targets) Serializer.Deserialize(t, (YMap)m_Body.Clone(), (r, ty) => r.Raw ? AssetLoader.ResolveExternal(r) : r.Target);
            return true;
        }
        public void Update() { m_Body = Serializer.Serialize(m_Targets[0]); }
        public void UpdateIfRequiredOrScript() => Update();
        public bool hasModifiedProperties => true;
        public void Dispose() { }
    }

    public sealed partial class SerializedProperty
    {
        internal SerializedObject m_Owner; internal YMap m_Parent; internal string m_Key, m_Path;
        internal YSeq m_Seq; internal int m_Index = -1;   // set for array elements: writes go back into the array
        void Put(YNode v) { m_Parent[m_Key] = v; if (m_Seq != null && m_Index >= 0 && m_Index < m_Seq.Items.Count) m_Seq.Items[m_Index] = v; }
        YScalar S => m_Parent[m_Key] as YScalar;
        public string propertyPath => m_Path;
        public string name => m_Key;
        public SerializedObject serializedObject => m_Owner;
        public void Dispose() { }
        public float floatValue { get => S?.F ?? 0; set => Put(YScalar.Of(value)); }
        public double doubleValue { get => S?.D ?? 0; set => Put(YScalar.Of(value)); }
        public int intValue { get => S?.I ?? 0; set => Put(YScalar.Of(value)); }
        public long longValue { get => S?.L ?? 0; set => Put(YScalar.Of(value)); }
        public bool boolValue { get => S?.B ?? false; set => Put(YScalar.Of(value)); }
        public string stringValue { get => S?.Value ?? ""; set => Put(new YScalar(value)); }
        public int enumValueIndex { get => intValue; set => intValue = value; }
        public Object objectReferenceValue { get => m_Parent[m_Key] is YRef r ? (r.Raw ? AssetLoader.ResolveExternal(r) : r.Target) : null; set => Put(new YRef(value)); }
        public Color colorValue { get => Serializer.ReadColor(m_Parent[m_Key]); set => Put(Serializer.Col(value)); }
        public Vector2 vector2Value { get => Serializer.ReadV2(m_Parent[m_Key]); set => Put(Serializer.Vec(value)); }
        public Vector3 vector3Value { get => Serializer.ReadV3(m_Parent[m_Key]); set => Put(Serializer.Vec(value)); }
        public Rect rectValue
        {
            get { var m = m_Parent[m_Key] as YMap; float F(string k) => (m?[k] as YScalar)?.F ?? 0; return new Rect(F("x"), F("y"), F("width"), F("height")); }
            set => Put(new YMap().Add("serializedVersion", new YScalar("2")).Add("x", YScalar.Of(value.x)).Add("y", YScalar.Of(value.y))
                .Add("width", YScalar.Of(value.width)).Add("height", YScalar.Of(value.height)));
        }
        public bool isArray => m_Parent[m_Key] is YSeq;
        public int arraySize
        {
            get => (m_Parent[m_Key] as YSeq)?.Items.Count ?? 0;
            set
            {
                // like Unity: growing duplicates the last element, shrinking drops from the end
                if (m_Parent[m_Key] is not YSeq seq) { seq = new YSeq(); Put(seq); }
                seq.Flow = false;
                while (seq.Items.Count > Math.Max(0, value)) seq.Items.RemoveAt(seq.Items.Count - 1);
                while (seq.Items.Count < value) seq.Items.Add(seq.Items.Count > 0 ? seq.Items[^1].Clone() : new YRef(null));
            }
        }
        public SerializedProperty FindPropertyRelative(string relativePropertyPath) => m_Owner.FindProperty(m_Path + "." + relativePropertyPath);
        /// <summary>Element of an array; values set on it are written back into the array.</summary>
        public SerializedProperty GetArrayElementAtIndex(int index)
        {
            var seq = m_Parent[m_Key] as YSeq;
            var holder = new YMap().Add("data", seq != null && index >= 0 && index < seq.Items.Count ? seq.Items[index] : new YScalar(""));
            return new SerializedProperty { m_Owner = m_Owner, m_Parent = holder, m_Key = "data", m_Path = $"{m_Path}.Array.data[{index}]", m_Seq = seq, m_Index = index };
        }
    }
}

namespace UnityEditor.SceneManagement
{
    public sealed partial class EditorSceneManager
    {
        public static Scene NewScene(NewSceneSetup setup) => NewScene(setup, NewSceneMode.Single);
        public static Scene NewScene(NewSceneSetup setup, NewSceneMode mode)
        {
            var s = new SceneData { Name = "Untitled" };
            if (mode == NewSceneMode.Single) s.MakeOnly();
            SceneData.Active = s;
            if (setup == NewSceneSetup.DefaultGameObjects)
            {
                var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
                cam.transform.position = new Vector3(0, 1, -10);
                new GameObject("Directional Light", typeof(Light));
            }
            return SceneData.ToScene(s);
        }
        public static bool SaveScene(Scene scene) => SaveScene(scene, scene.path, false);
        public static bool SaveScene(Scene scene, string dstScenePath) => SaveScene(scene, dstScenePath, false);
        public static bool SaveScene(Scene scene, string dstScenePath, bool saveAsCopy)
        {
            var sd = SceneData.ByHandle(scene.m_Handle) ?? throw new ArgumentException("invalid scene");
            if (string.IsNullOrEmpty(dstScenePath)) dstScenePath = sd.Path;
            dstScenePath = AssetIndex.Norm(dstScenePath);
            var text = new SceneWriter(sd).Write();
            SafeIO.WriteAllText(AssetIndex.Abs(dstScenePath), text);
            AssetIndex.Register(dstScenePath);
            if (!saveAsCopy) { sd.Path = dstScenePath; sd.Name = Path.GetFileNameWithoutExtension(dstScenePath); sd.IsDirty = false; }
            Debug.Log($"[MiniUnity] Saved scene → {dstScenePath} ({text.Length / 1024} KB)");
            return true;
        }
        public static bool SaveOpenScenes() => SceneData.Active == null || SaveScene(SceneData.ToScene(SceneData.Active));
        public static bool SaveCurrentModifiedScenesIfUserWantsTo() => true;
        public static bool MarkSceneDirty(Scene scene) { var s = SceneData.ByHandle(scene.m_Handle); if (s != null) s.IsDirty = true; return true; }
        public static void MarkAllScenesDirty() { }
        public static Scene GetActiveScene() => SceneManager.GetActiveScene();
        public static Scene OpenScene(string scenePath) => OpenScene(scenePath, OpenSceneMode.Single);
        /// <summary>Load a .unity file (prefab instances flattened to live objects). Additive keeps the active scene.</summary>
        public static Scene OpenScene(string scenePath, OpenSceneMode mode)
        {
            var prev = SceneData.Active;
            var s = MiniUnity.Agent.RuntimeHost.LoadScene(AssetIndex.Norm(scenePath));
            if (mode == OpenSceneMode.Single) s.MakeOnly();
            else if (prev != null && prev.Loaded) SceneData.Active = prev;
            return SceneData.ToScene(s);
        }
        public static bool CloseScene(Scene scene, bool removeScene)
        {
            var s = SceneData.ByHandle(scene.m_Handle);
            if (s == null || !s.Loaded) return false;
            foreach (var r in s.Roots.ToList()) if (r != null && !r.m_Destroyed) Object.DestroyImmediate(r);
            s.Roots.Clear(); MiniUnity.Hier.Version++;
            s.Loaded = false;
            if (SceneData.Active == s) SceneData.Active = SceneData.LoadedScenes.FirstOrDefault();
            return true;
        }
        public static void MoveGameObjectToScene(GameObject go, Scene scene) => UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
    }
}
