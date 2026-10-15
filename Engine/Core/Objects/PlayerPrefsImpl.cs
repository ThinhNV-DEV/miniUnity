// PlayerPrefs: key → int / float / string, kept in memory and written to <project>/Library/MiniUnity/PlayerPrefs.json on
// Save() (Unity's editor keeps its prefs per project too). Getters with the wrong type return the default, like Unity.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace UnityEngine
{
    public partial class PlayerPrefs
    {
        static Dictionary<string, (char type, string value)> s_Prefs;
        static string FilePath => Path.Combine(MiniUnity.AssetIndex.Root ?? ".", "Library", "MiniUnity", "PlayerPrefs.json");

        static Dictionary<string, (char type, string value)> Prefs
        {
            get
            {
                if (s_Prefs != null) return s_Prefs;
                s_Prefs = new Dictionary<string, (char, string)>();
                try
                {
                    if (File.Exists(FilePath))
                        foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(FilePath)) ?? new())
                            if (kv.Value?.Length == 2 && kv.Value[0].Length == 1) s_Prefs[kv.Key] = (kv.Value[0][0], kv.Value[1]);
                }
                catch { }
                return s_Prefs;
            }
        }

        static string Inv(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        public static void SetInt(string key, int value) => Prefs[key] = ('i', value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public static int GetInt(string key, int defaultValue) => Prefs.TryGetValue(key, out var v) && v.type == 'i' && int.TryParse(v.value, out var r) ? r : defaultValue;
        public static int GetInt(string key) => GetInt(key, 0);
        public static void SetFloat(string key, float value) => Prefs[key] = ('f', Inv(value));
        public static float GetFloat(string key, float defaultValue) => Prefs.TryGetValue(key, out var v) && v.type == 'f' && float.TryParse(v.value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : defaultValue;
        public static float GetFloat(string key) => GetFloat(key, 0f);
        public static void SetString(string key, string value) => Prefs[key] = ('s', value ?? "");
        public static string GetString(string key, string defaultValue) => Prefs.TryGetValue(key, out var v) && v.type == 's' ? v.value : defaultValue;
        public static string GetString(string key) => GetString(key, "");
        public static bool HasKey(string key) => Prefs.ContainsKey(key);
        public static void DeleteKey(string key) => Prefs.Remove(key);
        public static void DeleteAll() => Prefs.Clear();
        public static void Save()
        {
            if (MiniUnity.SafeIO.DryRun) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var o = new Dictionary<string, string[]>();
                foreach (var kv in Prefs) o[kv.Key] = new[] { kv.Value.type.ToString(), kv.Value.value };
                MiniUnity.SafeIO.AtomicWriteText(FilePath, JsonSerializer.Serialize(o));
            }
            catch (Exception e) { Debug.LogWarning("[PlayerPrefs] save failed: " + e.Message); }
        }
    }
}
