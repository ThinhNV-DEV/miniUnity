using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniUnity.Yaml;
using UnityEngine;

namespace MiniUnity
{
    /// <summary>MonoScript GUIDs: class name ⇄ .cs.meta guid (project scripts + package sources).</summary>
    public static class ScriptRegistry
    {
        public static List<string> ExtraScriptDirs = new();
        static Dictionary<string, List<(string guid, string path)>> byName;
        static Dictionary<string, Type> guidToType;
        static Dictionary<Type, string> typeToGuid;

        static void Ensure()
        {
            if (byName != null) return;
            byName = new(); guidToType = new(); typeToGuid = new();
            var dirs = new List<string> { Path.Combine(AssetIndex.Root, "Assets") };
            dirs.AddRange(ExtraScriptDirs);
            foreach (var d in dirs)
            {
                if (!Directory.Exists(d)) continue;
                foreach (var meta in Directory.EnumerateFiles(d, "*.cs.meta", SearchOption.AllDirectories))
                {
                    string guid = null;
                    foreach (var l in File.ReadLines(meta)) if (l.StartsWith("guid: ")) { guid = l.Substring(6).Trim(); break; }
                    if (guid == null) continue;
                    var name = Path.GetFileNameWithoutExtension(meta[..^5]);
                    if (!byName.TryGetValue(name, out var list)) byName[name] = list = new();
                    list.Add((guid, meta));
                }
            }
            foreach (var t in TypeCache.AllTypes)
            {
                if (!(typeof(MonoBehaviour).IsAssignableFrom(t) || typeof(ScriptableObject).IsAssignableFrom(t))) continue;
                if (t.IsGenericTypeDefinition) continue;
                if (!byName.TryGetValue(t.Name, out var cands)) continue;
                var pick = cands.Count == 1 ? cands[0] : cands.OrderByDescending(c => Score(c.path, t)).First();
                typeToGuid[t] = pick.guid;
                if (!guidToType.TryGetValue(pick.guid, out var have) || (TypeCache.IsProjectType(t) && !TypeCache.IsProjectType(have))) guidToType[pick.guid] = t;
            }
        }
        public static void Reset() { byName = null; guidToType = null; typeToGuid = null; }

        static readonly Dictionary<string, string> sources = new();

        /// <summary>Whether the .cs next to <paramref name="metaPath"/> declares <paramref name="t"/> (namespace + type name).</summary>
        static bool Declares(string metaPath, Type t)
        {
            var cs = metaPath[..^5];
            if (!sources.TryGetValue(cs, out var text))
            {
                try { text = File.Exists(cs) ? File.ReadAllText(cs) : ""; } catch { text = ""; }
                sources[cs] = text;
            }
            var kind = t.IsValueType ? "struct" : "class";
            if (!System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{kind}\s+{System.Text.RegularExpressions.Regex.Escape(t.Name)}\b")) return false;
            var ns = t.Namespace;
            if (string.IsNullOrEmpty(ns)) return !System.Text.RegularExpressions.Regex.IsMatch(text, @"^\s*namespace\s", System.Text.RegularExpressions.RegexOptions.Multiline);
            return System.Text.RegularExpressions.Regex.IsMatch(text, $@"^\s*namespace\s+{System.Text.RegularExpressions.Regex.Escape(ns)}\s*[{{;]", System.Text.RegularExpressions.RegexOptions.Multiline);
        }

        static int Score(string path, Type t)
        {
            int s = 0;
            if (Declares(path, t)) s += 100;   // same class name in two namespaces (e.g. two games' GameController)
            if (TypeCache.IsProjectType(t)) s += 50;   // compiled project scripts (ua play / ua test) beat MiniUnity's embedded copies
            var ns = t.Namespace ?? "";
            if (ns.StartsWith("TMPro") && path.Contains("textmeshpro")) s += 5;
            if (ns.StartsWith("UnityEngine.UI") && path.Contains("ugui")) s += 5;
            if (ns.StartsWith("UnityEngine.EventSystems") && path.Contains("ugui")) s += 5;
            if (ns.StartsWith("UnityEngine.Timeline") && path.Contains("timeline")) s += 5;
            if (ns.StartsWith("Cinemachine") && path.Contains("cinemachine")) s += 5;
            var root = ns.Split('.')[0];
            if (root.Length > 0 && path.Contains("/" + root + "/", StringComparison.Ordinal)) s += 5;   // project namespace = its folder (VideoRecreate/…)
            return s;
        }

        public static bool KnownGuid(string guid) { Ensure(); return byName.Values.Any(l => l.Any(x => x.guid == guid)); }
        public static Type TypeOf(string guid) { Ensure(); return guid != null && guidToType.TryGetValue(guid, out var t) ? t : null; }
        public static string GuidOf(Type t) { Ensure(); return typeToGuid.TryGetValue(t, out var g) ? g : null; }

        /// <summary>Engine ScriptableObject classes written in C# inside Unity's modules: their MonoScript lives in the built-in
        /// resources (guid 0000000000000000e000000000000000) under a fixed file id.</summary>
        static readonly Dictionary<long, Type> builtinScripts = new()
        {
            [13312] = typeof(UnityEngine.Tilemaps.Tile),
        };
        public const string BuiltinScriptGuid = "0000000000000000e000000000000000";
        public static Type TypeOf(string guid, long fileID) => guid == BuiltinScriptGuid ? (builtinScripts.TryGetValue(fileID, out var bt) ? bt : null) : TypeOf(guid);

        public static YRef ScriptRef(Type t)
        {
            foreach (var kv in builtinScripts) if (kv.Value == t) return new YRef(kv.Key, BuiltinScriptGuid, 0);
            var g = GuidOf(t);
            if (g == null) { Debug.LogWarning("[MiniUnity] no MonoScript guid for " + t.FullName); return new YRef(0, null, 0); }
            return new YRef(11500000, g, 3);
        }
    }

    /// <summary>Open scene: ordered root GameObjects.</summary>
    public sealed class SceneData
    {
        static SceneData s_Active;
        public static SceneData Active { get => s_Active; set { if (s_Active != value) { s_Active = value; SetVersion++; } } }
        /// <summary>Changes when the set of scenes whose objects play (loaded scenes, the active one, DontDestroyOnLoad) changes.</summary>
        internal static int SetVersion;
        static readonly List<SceneData> all = new();
        public readonly List<GameObject> Roots = new();
        public string Path = "";
        public string Name = "";
        public int Handle;
        public bool IsDirty;
        /// <summary>The scene's RenderSettings document (fog, ambient, skybox…), edited through UnityEngine.RenderSettings.</summary>
        public YMap RenderSettings = DefaultRenderSettings();
        public static YMap DefaultRenderSettings()
        {
            var t = SceneWriter.RenderSettingsBody;
            return YamlReader.ParseText(t);
        }
        bool m_Loaded = true;
        public bool Loaded { get => m_Loaded; set { if (m_Loaded != value) { m_Loaded = value; SetVersion++; } } }
        /// <summary>The play-mode "DontDestroyOnLoad" scene: not counted by SceneManager, never unloaded by a single-mode load.</summary>
        public bool IsDontDestroyOnLoad;
        public static List<SceneData> LoadedScenes => all.Where(s => s.Loaded && !s.IsDontDestroyOnLoad).ToList();
        public SceneData() { all.Add(this); Handle = all.Count; SetVersion++; }
        /// <summary>Single-mode load / new scene: every other scene is closed (DontDestroyOnLoad objects stay).</summary>
        public void MakeOnly() { foreach (var s in all) if (s != this && !s.IsDontDestroyOnLoad) s.Loaded = false; Loaded = true; Active = this; }

        static SceneData s_DontDestroy;
        public static SceneData DontDestroyOnLoadScene => s_DontDestroy ??= new SceneData { Name = "DontDestroyOnLoad", IsDontDestroyOnLoad = true };
        /// <summary>Drop the DontDestroyOnLoad scene (a new play session).</summary>
        public static void ResetDontDestroyOnLoad() { if (s_DontDestroy != null) { s_DontDestroy.Loaded = false; s_DontDestroy = null; } }

        static readonly List<SceneData> s_Playing = new();
        static int s_PlayingVersion = -1;
        /// <summary>Scenes whose objects exist for the player loop / Find*: the active scene, the other loaded scenes in load
        /// order, then DontDestroyOnLoad.</summary>
        public static List<SceneData> Playing
        {
            get
            {
                if (s_PlayingVersion == SetVersion) return s_Playing;
                s_Playing.Clear();
                if (Active != null) s_Playing.Add(Active);
                foreach (var s in all) if (s.Loaded && s != Active && !s.IsDontDestroyOnLoad) s_Playing.Add(s);
                if (s_DontDestroy != null && s_DontDestroy.Loaded) s_Playing.Add(s_DontDestroy);
                s_PlayingVersion = SetVersion;
                return s_Playing;
            }
        }
        public void AddRoot(GameObject go) { if (!Roots.Contains(go)) Roots.Add(go); IsDirty = true; UnityEngine.Object.SceneEdited(); Hier.Version++; }
        public void RemoveRoot(GameObject go) { Roots.Remove(go); UnityEngine.Object.SceneEdited(); Hier.Version++; }
        public static SceneData ByHandle(int h) => h > 0 && h <= all.Count ? all[h - 1] : null;
        public static UnityEngine.SceneManagement.Scene ToScene(SceneData s) => s == null ? default : new UnityEngine.SceneManagement.Scene { m_Handle = s.Handle };
    }
}

namespace UnityEngine.SceneManagement
{
    public partial struct Scene
    {
        internal int m_Handle;
        MiniUnity.SceneData D => MiniUnity.SceneData.ByHandle(m_Handle);
        public int handle => m_Handle;
        public bool IsValid() => D != null;
        public string path => D?.Path ?? "";
        public string name { get => D?.Name ?? ""; set { if (D != null) D.Name = value; } }
        public bool isLoaded => D != null && D.Loaded;
        public bool isDirty => D?.IsDirty ?? false;
        public int rootCount => D?.Roots.Count ?? 0;
        public int buildIndex => D == null || D.IsDontDestroyOnLoad ? -1 : MiniUnity.SceneLoader.IndexOf(D.Path);
        public bool isSubScene { get => false; set { } }
        public GameObject[] GetRootGameObjects() => D?.Roots.ToArray() ?? Array.Empty<GameObject>();
        public void GetRootGameObjects(List<GameObject> rootGameObjects) { rootGameObjects.Clear(); if (D != null) rootGameObjects.AddRange(D.Roots); }
        public override int GetHashCode() => m_Handle;
        public override bool Equals(object other) => other is Scene s && s.m_Handle == m_Handle;
        public static bool operator ==(Scene lhs, Scene rhs) => lhs.m_Handle == rhs.m_Handle;
        public static bool operator !=(Scene lhs, Scene rhs) => lhs.m_Handle != rhs.m_Handle;
    }

    public partial class SceneManager
    {
        public static Scene GetActiveScene() => MiniUnity.SceneData.ToScene(MiniUnity.SceneData.Active);
        public static bool SetActiveScene(Scene scene) { MiniUnity.SceneData.Active = MiniUnity.SceneData.ByHandle(scene.m_Handle); return true; }
        public static int sceneCount => MiniUnity.SceneData.LoadedScenes.Count;
        public static int loadedSceneCount => sceneCount;
        public static Scene GetSceneAt(int index) { var l = MiniUnity.SceneData.LoadedScenes; return index >= 0 && index < l.Count ? MiniUnity.SceneData.ToScene(l[index]) : default; }
        public static Scene GetSceneByPath(string scenePath)
        {
            var p = MiniUnity.AssetIndex.Norm(scenePath);
            return MiniUnity.SceneData.ToScene(MiniUnity.SceneData.LoadedScenes.FirstOrDefault(s => s.Path == p));
        }
        public static Scene GetSceneByName(string name) => MiniUnity.SceneData.ToScene(MiniUnity.SceneData.LoadedScenes.FirstOrDefault(s => s.Name == name || s.Path == name));
        public static void MoveGameObjectToScene(GameObject go, Scene scene)
        {
            if (go == null) throw new ArgumentException("The GameObject is null");
            if (go.transform.parent != null) throw new ArgumentException("Gameobject is not a root in a scene");
            var to = MiniUnity.SceneData.ByHandle(scene.m_Handle) ?? throw new ArgumentException("Scene is not valid");
            go.m_Scene?.RemoveRoot(go);
            to.AddRoot(go);
            foreach (var g in MiniUnity.Hier.Walk(go)) g.m_Scene = to;
        }
    }
}
