using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniUnity;
using MiniUnity.Agent;
using MiniUnity.Yaml;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>Build scene list and config objects, read from / written to ProjectSettings/EditorBuildSettings.asset.</summary>
    public partial class EditorBuildSettings
    {
        const string File = "ProjectSettings/EditorBuildSettings.asset";
        static readonly Dictionary<string, Object> s_Memory = new(StringComparer.Ordinal);   // objects that are not assets

        static UFile Load() => System.IO.File.Exists(Path.Combine(AssetIndex.Root, File)) ? UFile.Load(File) : null;

        static YMap Config(UFile f)
        {
            var body = f.Docs[0].Body;
            if (body["m_configObjects"] is not YMap m) { m = new YMap(); body["m_configObjects"] = m; }
            return m;
        }

        public static EditorBuildSettingsScene[] scenes
        {
            get
            {
                var f = Load(); if (f == null) return Array.Empty<EditorBuildSettingsScene>();
                return ((f.Docs[0].Body["m_Scenes"] as YSeq)?.Items.OfType<YMap>() ?? Enumerable.Empty<YMap>())
                    .Select(s => new EditorBuildSettingsScene(Serializer.Str(s["path"]), Serializer.Str(s["enabled"]) == "1")).ToArray();
            }
            set
            {
                var f = Load(); if (f == null) return;
                var seq = new YSeq();
                foreach (var s in value ?? Array.Empty<EditorBuildSettingsScene>())
                    seq.Items.Add(new YMap().Add("enabled", s.enabled ? "1" : "0").Add("path", s.path).Add("guid", AssetIndex.GuidOf(s.path) ?? ""));
                f.Docs[0].Body["m_Scenes"] = seq; f.Dirty = true; f.Save();
            }
        }

        public static bool TryGetConfigObject<T>(string name, out T result) where T : Object
        {
            result = null;
            if (s_Memory.TryGetValue(name, out var mem)) { result = mem as T; return result != null; }
            var f = Load(); if (f == null) return false;
            if (Config(f)[name] is not YMap r) return false;
            var path = AssetIndex.PathOf(Serializer.Str(r["guid"]));
            if (string.IsNullOrEmpty(path)) return false;
            result = AssetDatabase.LoadAssetAtPath<T>(path);
            return result != null;
        }

        public static void AddConfigObject(string name, Object obj, bool overwrite)
        {
            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) { if (overwrite || !s_Memory.ContainsKey(name)) s_Memory[name] = obj; return; }
            var f = Load(); if (f == null) return;
            var cfg = Config(f);
            if (cfg[name] != null && !overwrite) return;
            cfg[name] = new YMap { Flow = true }.Add("fileID", "11400000").Add("guid", AssetIndex.GuidOf(path) ?? "").Add("type", "2");
            f.Dirty = true; f.Save();
        }

        public static bool RemoveConfigObject(string name)
        {
            bool any = s_Memory.Remove(name);
            var f = Load(); if (f == null) return any;
            var cfg = Config(f);
            if (cfg[name] == null) return any;
            cfg.Entries.RemoveAll(e => e.Key == name); f.Dirty = true; f.Save();
            return true;
        }

        public static string[] GetConfigObjectNames()
        {
            var f = Load();
            var names = f != null ? Config(f).Entries.Select(e => e.Key).ToList() : new List<string>();
            return names.Concat(s_Memory.Keys).Distinct().ToArray();
        }
    }

    public partial class EditorBuildSettingsScene
    {
        string m_Path = ""; bool m_Enabled;
        public EditorBuildSettingsScene() { }
        public EditorBuildSettingsScene(string path, bool enabled) { m_Path = path; m_Enabled = enabled; }
        public bool enabled { get => m_Enabled; set => m_Enabled = value; }
        public string path { get => m_Path; set => m_Path = value; }
        public int CompareTo(object obj) => obj is EditorBuildSettingsScene s ? string.CompareOrdinal(m_Path, s.m_Path) : 1;
        public static string[] GetActiveSceneList(EditorBuildSettingsScene[] scenes) => scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
    }
}

namespace UnityEditorInternal
{
    public partial class InternalEditorUtility
    {
        /// <summary>The headless editor is always the active application (Input System syncs devices when it regains focus).</summary>
        public static bool isApplicationActive => true;
    }
}
