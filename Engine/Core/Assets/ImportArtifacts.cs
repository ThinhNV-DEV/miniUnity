// Unity's import results (Library/Artifacts/**, binary SerializedFiles) for assets MiniUnity cannot import itself: models (FBX,
// OBJ, Blender…) give their meshes, materials, avatars, clips and the model prefab this way once the project has been opened in
// Unity. An artifact's object path ids are the asset's local file ids (the ones scenes reference and the .meta names in
// internalIDToNameTable / fileIDToRecycleName), so the artifact of an asset is the file holding the most of those ids, its
// object names agreeing with the .meta. Files are indexed from their metadata only; object data is read when an asset loads.
// Without Library/Artifacts (or no matching artifact) the asset stays unimported, as before.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MiniUnity.Serialization;
using MiniUnity.Yaml;

namespace MiniUnity
{
    public static class ImportArtifacts
    {
        static Dictionary<long, List<string>> s_ByPathId;
        static readonly Dictionary<string, SerializedFile> s_Files = new();
        static string s_Root;

        public static string Root => Path.Combine(AssetIndex.Root ?? ".", "Library", "Artifacts");
        public static void Reset() { s_ByPathId = null; s_Files.Clear(); s_Root = null; }

        static void Scan()
        {
            if (s_ByPathId != null && s_Root == Root) return;
            s_Root = Root; s_ByPathId = new(); s_Files.Clear();
            if (!Directory.Exists(s_Root)) return;
            foreach (var f in Directory.EnumerateFiles(s_Root, "*", SearchOption.AllDirectories))
            {
                if (f.EndsWith(".info", StringComparison.OrdinalIgnoreCase) || !SerializedFile.IsSerializedFile(f)) continue;
                SerializedFile sf;
                try { sf = SerializedFile.OpenMeta(f); } catch (Exception) { continue; }
                s_Files[f] = sf;
                foreach (var o in sf.Objects)
                {
                    if (!s_ByPathId.TryGetValue(o.PathId, out var l)) s_ByPathId[o.PathId] = l = new List<string>();
                    l.Add(f);
                }
            }
        }

        /// <summary>(file id → name) of an asset's sub-objects from its .meta (internalIDToNameTable, fileIDToRecycleName).</summary>
        public static Dictionary<long, string> MetaNames(string assetPath)
        {
            var res = new Dictionary<long, string>();
            var meta = AssetIndex.Abs(assetPath) + ".meta";
            if (!File.Exists(meta)) return res;
            var text = File.ReadAllText(meta);
            // internalIDToNameTable:  - first:\n      74: -203655887218126122\n    second: Take 001
            foreach (Match m in Regex.Matches(text, @"- first:\s*\n\s+-?\d+:\s*(-?\d+)\s*\n\s+second:\s*(.*)"))
                res[long.Parse(m.Groups[1].Value)] = m.Groups[2].Value.Trim();
            // fileIDToRecycleName (older importers):  100000: //RootNode
            int i = text.IndexOf("fileIDToRecycleName:", StringComparison.Ordinal);
            if (i >= 0)
                foreach (Match m in Regex.Matches(text[i..], @"^\s+(-?\d+):\s*(.+)$", RegexOptions.Multiline))
                {
                    if (!long.TryParse(m.Groups[1].Value, out var id)) break;
                    res.TryAdd(id, m.Groups[2].Value.Trim());
                }
            return res;
        }

        /// <summary>The import result of an asset as YAML documents, or null when Library/Artifacts has none for it.</summary>
        public static List<YDoc> DocsFor(string assetPath)
        {
            var names = MetaNames(assetPath);
            if (names.Count == 0) return null;
            Scan();
            if (s_ByPathId.Count == 0) return null;
            var score = new Dictionary<string, int>();
            foreach (var id in names.Keys)
                if (s_ByPathId.TryGetValue(id, out var files))
                    foreach (var f in files) score[f] = score.GetValueOrDefault(f) + 1;
            if (score.Count == 0) return null;
            // most ids in common, then the one whose object names agree with the .meta
            string best = null; int bestScore = -1;
            foreach (var (f, s) in score.OrderByDescending(kv => kv.Value).Take(8))
            {
                var sf = s_Files[f];
                int named = 0;
                foreach (var o in sf.Objects) if (names.TryGetValue(o.PathId, out var n) && sf.NameOf(o) == n) named++;
                int total = s * 1000 + named;
                if (total > bestScore) { bestScore = total; best = f; }
            }
            return best == null ? null : s_Files[best].ToDocs();
        }
    }
}
