using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniUnity.Yaml;

namespace MiniUnity
{
    /// <summary>Semantic comparison of a generated scene/timeline against Unity-written oracle files (fileIDs normalized away).</summary>
    public static class Compare
    {
        // ------------------------------------------------------------------ timeline model
        sealed class TL
        {
            public string Guid;
            public Dictionary<long, YDoc> Docs = new();
            public Dictionary<long, string> Name = new();   // fileID → normalized name (track path / clip-asset id)
            public List<(string path, string type, YMap body)> Tracks = new();
        }

        static TL LoadTL(string path)
        {
            var tl = new TL();
            var meta = File.Exists(path + ".meta") ? File.ReadAllLines(path + ".meta").FirstOrDefault(l => l.StartsWith("guid: "))?.Substring(6) : null;
            tl.Guid = meta;
            foreach (var d in YamlReader.ReadFile(path)) tl.Docs[d.FileId] = d;
            var main = tl.Docs.Values.FirstOrDefault(d => d.FileId == 11400000) ?? tl.Docs.Values.First();
            tl.Name[main.FileId] = "TIMELINE";
            void Walk(YSeq seq, string prefix)
            {
                if (seq == null) return;
                var seen = new Dictionary<string, int>();
                foreach (var it in seq.Items.OfType<YRef>())
                {
                    if (!tl.Docs.TryGetValue(it.FileID, out var d)) continue;
                    var nm = Serializer.Str(d.Body["m_Name"]);
                    seen.TryGetValue(nm, out var k); seen[nm] = k + 1;
                    var p = prefix + "/" + nm + (k > 0 ? "#" + k : "");
                    tl.Name[d.FileId] = "Track:" + p;
                    tl.Tracks.Add((p, (d.Body["m_Script"] as YRef)?.Guid, d.Body));
                    if (d.Body["m_Clips"] is YSeq clips)
                        for (int i = 0; i < clips.Items.Count; i++)
                            if ((clips.Items[i] as YMap)?["m_Asset"] is YRef ar && ar.FileID != 0) tl.Name[ar.FileID] = $"Clip:{p}[{i}]";
                    Walk(d.Body["m_Children"] as YSeq, p);
                }
            }
            Walk(main.Body["m_Tracks"] as YSeq, "");
            int other = 0;
            foreach (var d in tl.Docs.Values) if (!tl.Name.ContainsKey(d.FileId)) tl.Name[d.FileId] = "Other#" + (other++);
            return tl;
        }

        static string NormRefTL(YRef r, TL tl)
        {
            if (r.FileID == 0) return "null";
            if (string.IsNullOrEmpty(r.Guid) || r.Guid == tl.Guid) return tl.Name.TryGetValue(r.FileID, out var n) ? n : "local?" + r.FileID;
            return $"{r.Guid}:{r.FileID}";
        }

        static readonly HashSet<string> skipKeys = new() { "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject", "m_Children", "m_Father", "m_Component" };

        static List<(string path, string val)> Flat(YNode n, Func<YRef, string> norm, string prefix = "")
        {
            var list = new List<(string, string)>();
            void Rec(string p, YNode x)
            {
                switch (x)
                {
                    case YScalar s: list.Add((p, s.Value)); break;
                    case YRef r: list.Add((p, norm(r))); break;
                    case YMap m: foreach (var e in m.Entries) { if (p.Length == 0 && skipKeys.Contains(e.Key)) continue; Rec(p.Length == 0 ? e.Key : p + "." + e.Key, e.Value); } break;
                    case YSeq q: list.Add((p + ".size", q.Items.Count.ToString())); for (int i = 0; i < q.Items.Count; i++) Rec($"{p}[{i}]", q.Items[i]); break;
                }
            }
            Rec(prefix, n);
            return list;
        }

        static bool ValEq(string a, string b)
        {
            if (a == b) return true;
            if (double.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) &&
                double.TryParse(b, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
                return Math.Abs(x - y) <= 1e-4 * Math.Max(1, Math.Abs(x));
            return false;
        }

        sealed class Stat
        {
            public int Total, Equal;
            public Dictionary<string, (int n, string ex)> Diffs = new();
            public void Add(string key, string a, string b, string ctx)
            {
                Total++;
                if (ValEq(a, b)) { Equal++; return; }
                Diffs.TryGetValue(key, out var d);
                Diffs[key] = (d.n + 1, d.ex ?? $"{ctx}: oracle={Trim(a)} ours={Trim(b)}");
            }
            static string Trim(string s) => s == null ? "<missing>" : s.Length > 70 ? s.Substring(0, 70) + "…" : s;
            public string Pct => Total == 0 ? "n/a" : $"{100.0 * Equal / Total:0.0}% ({Equal}/{Total})";
            public void Print(TextWriter w, int top)
            {
                foreach (var kv in Diffs.OrderByDescending(k => k.Value.n).Take(top)) w.WriteLine($"      {kv.Value.n,6} × {kv.Key}   e.g. {kv.Value.ex}");
            }
        }

        static string Bucket(string path) => System.Text.RegularExpressions.Regex.Replace(path, @"\[\d+\]", "[]");

        public static void Timeline(string oraclePath, string oursPath, TextWriter w, out TLInfo oi, out TLInfo ni)
        {
            var o = LoadTL(oraclePath); var n = LoadTL(oursPath);
            oi = new TLInfo { Guid = o.Guid, Names = o.Name }; ni = new TLInfo { Guid = n.Guid, Names = n.Name };
            w.WriteLine("== Timeline " + Path.GetFileName(oraclePath));
            w.WriteLine($"  objects: oracle {o.Docs.Count}, ours {n.Docs.Count}");
            var ot = o.Tracks.ToDictionary(t => t.path); var nt = n.Tracks.ToDictionary(t => t.path);
            int both = ot.Keys.Count(nt.ContainsKey);
            w.WriteLine($"  tracks: oracle {ot.Count}, ours {nt.Count}, same path {both}, same path+type {ot.Count(kv => nt.TryGetValue(kv.Key, out var x) && x.type == kv.Value.type)}");
            foreach (var p in ot.Keys.Where(k => !nt.ContainsKey(k)).Take(5)) w.WriteLine("    only oracle: " + p);
            foreach (var p in nt.Keys.Where(k => !ot.ContainsKey(k)).Take(5)) w.WriteLine("    only ours:   " + p);
            // track order (flattened) equal?
            w.WriteLine($"  track order identical: {o.Tracks.Select(t => t.path).SequenceEqual(n.Tracks.Select(t => t.path))}");
            int oc = 0, nc = 0, clipsEq = 0, clipsTimingEq = 0;
            var trackStat = new Stat(); var clipAssetStat = new Stat();
            foreach (var (path, type, ob) in o.Tracks)
            {
                var oclips = (ob["m_Clips"] as YSeq)?.Items ?? new();
                oc += oclips.Count;
                if (!nt.TryGetValue(path, out var nn)) continue;
                var nclips = (nn.body["m_Clips"] as YSeq)?.Items ?? new();
                // track-level fields (without clips)
                var of = Flat(ob, r => NormRefTL(r, o)).Where(x => !x.path.StartsWith("m_Clips")).ToList();
                var nf = Flat(nn.body, r => NormRefTL(r, n)).Where(x => !x.path.StartsWith("m_Clips")).ToDictionary(x => x.path, x => x.val);
                foreach (var (p, v) in of) trackStat.Add(Bucket(p), v, nf.TryGetValue(p, out var nv) ? nv : null, path + "." + p);
                for (int i = 0; i < oclips.Count; i++)
                {
                    if (i >= nclips.Count) break;
                    var a = (YMap)oclips[i]; var b = (YMap)nclips[i];
                    var fa = Flat(a, r => NormRefTL(r, o)); var fb = Flat(b, r => NormRefTL(r, n)).ToDictionary(x => x.path, x => x.val);
                    bool eq = fa.All(x => fb.TryGetValue(x.path, out var v) && ValEq(x.val, v));
                    bool timing = new[] { "m_Start", "m_Duration", "m_DisplayName" }.All(k => ValEq(Serializer.Str(a[k]), Serializer.Str(b[k])));
                    if (eq) clipsEq++; if (timing) clipsTimingEq++;
                    foreach (var (p, v) in fa) clipAssetStat.Add("clip." + Bucket(p), v, fb.TryGetValue(p, out var vv) ? vv : null, $"{path}[{i}].{p}");
                    // clip asset body
                    if (a["m_Asset"] is YRef ar && b["m_Asset"] is YRef br && o.Docs.TryGetValue(ar.FileID, out var ad) && n.Docs.TryGetValue(br.FileID, out var bd))
                    {
                        var ca = Flat(ad.Body, r => NormRefTL(r, o)); var cb = Flat(bd.Body, r => NormRefTL(r, n)).ToDictionary(x => x.path, x => x.val);
                        foreach (var (p, v) in ca) clipAssetStat.Add("asset." + Bucket(p), v, cb.TryGetValue(p, out var vv) ? vv : null, $"{path}[{i}].asset.{p}");
                    }
                }
            }
            foreach (var t in n.Tracks) nc += ((t.body["m_Clips"] as YSeq)?.Items.Count ?? 0);
            w.WriteLine($"  clips: oracle {oc}, ours {nc}; clips identical (all clip fields) {clipsEq}; start+duration+name equal {clipsTimingEq}");
            w.WriteLine($"  track fields equal: {trackStat.Pct}"); trackStat.Print(w, 6);
            w.WriteLine($"  clip + clip-asset fields equal: {clipAssetStat.Pct}"); clipAssetStat.Print(w, 10);
            var outTL = Environment.GetEnvironmentVariable("MINIUNITY_REMAP_TL_OUT");
            if (outTL != null) Remap(oraclePath, oursPath, outTL, o.Name, n.Name, null, null, YamlReader.ReadFile(oraclePath), YamlReader.ReadFile(oursPath));
            // timeline asset itself
            var om = o.Docs[11400000]; var nm2 = n.Docs.TryGetValue(11400000, out var x2) ? x2 : null;
            if (nm2 != null)
            {
                var st = new Stat();
                var fa = Flat(om.Body, r => NormRefTL(r, o)); var fb = Flat(nm2.Body, r => NormRefTL(r, n)).ToDictionary(x => x.path, x => x.val);
                foreach (var (p, v) in fa) st.Add(Bucket(p), v, fb.TryGetValue(p, out var vv) ? vv : null, "TimelineAsset." + p);
                w.WriteLine($"  TimelineAsset fields equal: {st.Pct}"); st.Print(w, 5);
            }
        }

        public sealed class TLInfo { public string Guid; public Dictionary<long, string> Names; }

        /// <summary>Rewrite our file with oracle fileIDs (matched by identity) and oracle doc order → enables a plain text diff.</summary>
        public static void Remap(string oraclePath, string oursPath, string outPath, Dictionary<long, string> oNames, Dictionary<long, string> nNames, TLInfo otl, TLInfo ntl, List<YDoc> oDocs, List<YDoc> nDocs)
        {
            var oByName = new Dictionary<string, long>();
            foreach (var kv in oNames) oByName.TryAdd(kv.Value, kv.Key);
            var map = new Dictionary<long, long>();
            long fresh = 1;
            foreach (var kv in nNames) map[kv.Key] = oByName.TryGetValue(kv.Value, out var oid) ? oid : -(fresh++);
            Dictionary<long, long> tlMap = null;
            if (otl != null && ntl != null)
            {
                var ob = new Dictionary<string, long>(); foreach (var kv in otl.Names) ob.TryAdd(kv.Value, kv.Key);
                tlMap = ntl.Names.ToDictionary(kv => kv.Key, kv => ob.TryGetValue(kv.Value, out var x) ? x : kv.Key);
            }
            YNode Fix(YNode n)
            {
                switch (n)
                {
                    case YRef r when r.Raw:
                        if (r.FileID == 0) return r;
                        if (string.IsNullOrEmpty(r.Guid)) return new YRef(map.TryGetValue(r.FileID, out var m) ? m : r.FileID, null, 0);
                        if (ntl != null && r.Guid == ntl.Guid) return new YRef(tlMap != null && tlMap.TryGetValue(r.FileID, out var t) ? t : r.FileID, otl.Guid, r.Type);
                        return r;
                    case YMap mm: { var c = new YMap { Flow = mm.Flow }; foreach (var e in mm.Entries) c.Add(e.Key, Fix(e.Value)); return c; }
                    case YSeq q: { var c = new YSeq { Flow = q.Flow }; foreach (var i in q.Items) c.Items.Add(Fix(i)); return c; }
                    default: return n;
                }
            }
            var orderIdx = new Dictionary<long, int>(); for (int i = 0; i < oDocs.Count; i++) orderIdx[oDocs[i].FileId] = i;
            var w = new YamlWriter(); w.WriteHeader();
            foreach (var d in nDocs.Select(d => (d, id: map.TryGetValue(d.FileId, out var x) ? x : d.FileId)).OrderBy(x => orderIdx.TryGetValue(x.id, out var i) ? i : int.MaxValue))
                w.WriteDoc(d.d.ClassId, d.id, d.d.TypeName, (YMap)Fix(d.d.Body), d.d.Stripped);
            File.WriteAllText(outPath, w.ToString());
        }

        // ------------------------------------------------------------------ scene model
        sealed class Scene
        {
            public Dictionary<long, YDoc> Docs = new();
            public Dictionary<long, string> GoPath = new();          // GameObject (plain or stripped) → path
            public Dictionary<long, string> ObjName = new();         // any object → normalized identity
            public Dictionary<string, Dictionary<long, YDoc>> Prefabs = new();
            public TLInfo Timeline;
        }

        static Dictionary<long, YDoc> LoadPrefab(string projectRoot, string guid, Dictionary<string, Dictionary<long, YDoc>> cache)
        {
            if (cache.TryGetValue(guid, out var c)) return c;
            AssetIndex.Root = projectRoot;
            var p = AssetIndex.PathOf(guid);
            c = new();
            if (p != null) foreach (var d in YamlReader.ReadFile(AssetIndex.Abs(p))) c[d.FileId] = d;
            cache[guid] = c;
            return c;
        }

        static string PrefabRelPath(Dictionary<long, YDoc> pf, long transformId)
        {
            var parts = new List<string>();
            long t = transformId;
            while (t != 0 && pf.TryGetValue(t, out var td))
            {
                var go = (td.Body["m_GameObject"] as YRef)?.FileID ?? 0;
                parts.Add(pf.TryGetValue(go, out var gd) ? Serializer.Str(gd.Body["m_Name"]) : "?");
                t = (td.Body["m_Father"] as YRef)?.FileID ?? 0;
            }
            parts.Reverse();
            return string.Join("/", parts.Skip(1)); // relative to prefab root
        }

        static Scene LoadScene(string path, string projectRoot, TLInfo tl)
        {
            var s = new Scene { Timeline = tl };
            foreach (var d in YamlReader.ReadFile(path)) s.Docs[d.FileId] = d;
            var transformOfGo = new Dictionary<long, long>();
            foreach (var d in s.Docs.Values) if ((d.ClassId == 4 || d.ClassId == 224) && !d.Stripped) transformOfGo[(d.Body["m_GameObject"] as YRef)?.FileID ?? 0] = d.FileId;
            var memo = new Dictionary<long, string>(); // transform id → GO path
            var siblingCounter = new Dictionary<string, int>();
            var assigned = new Dictionary<long, string>();

            // children order: plain transforms list children; added objects under stripped parents in file order
            string TPath(long tid)
            {
                if (tid == 0) return "";
                if (memo.TryGetValue(tid, out var mp)) return mp;
                if (!s.Docs.TryGetValue(tid, out var td)) return "?" + tid;
                string res;
                if (td.Stripped)
                {
                    var inst = (td.Body["m_PrefabInstance"] as YRef).FileID;
                    var src = (YRef)td.Body["m_CorrespondingSourceObject"];
                    var pf = LoadPrefab(projectRoot, src.Guid, s.Prefabs);
                    var rel = PrefabRelPath(pf, src.FileID);
                    var ip = InstPath(inst);
                    res = rel.Length == 0 ? ip : ip + "/" + rel;
                }
                else
                {
                    var go = (td.Body["m_GameObject"] as YRef).FileID;
                    var name = s.Docs.TryGetValue(go, out var gd) ? Serializer.Str(gd.Body["m_Name"]) : "?";
                    var father = (td.Body["m_Father"] as YRef)?.FileID ?? 0;
                    var parent = TPath(father);
                    var key = parent + "/" + name;
                    // disambiguate by order of appearance under the same parent (m_Children order when available)
                    int k = SiblingIndex(father, tid, name);
                    res = (parent.Length == 0 ? name : parent + "/" + name) + (k > 0 ? "#" + k : "");
                }
                memo[tid] = res;
                return res;
            }
            int SiblingIndex(long father, long tid, string name)
            {
                IEnumerable<long> sibs;
                if (father != 0 && s.Docs.TryGetValue(father, out var fd) && !fd.Stripped && fd.Body["m_Children"] is YSeq ch) sibs = ch.Items.OfType<YRef>().Select(r => r.FileID);
                else sibs = s.Docs.Values.Where(d => (d.ClassId == 4 || d.ClassId == 224) && !d.Stripped && ((d.Body["m_Father"] as YRef)?.FileID ?? 0) == father).Select(d => d.FileId);
                int k = 0;
                foreach (var sb in sibs)
                {
                    if (sb == tid) return k;
                    if (s.Docs.TryGetValue(sb, out var sd) && !sd.Stripped && s.Docs.TryGetValue((sd.Body["m_GameObject"] as YRef).FileID, out var sg) && Serializer.Str(sg.Body["m_Name"]) == name) k++;
                }
                return k;
            }
            string InstPath(long inst)
            {
                var d = s.Docs[inst];
                var mod = (YMap)d.Body["m_Modification"];
                var parent = TPath((mod["m_TransformParent"] as YRef)?.FileID ?? 0);
                var srcGuid = ((YRef)d.Body["m_SourcePrefab"]).Guid;
                var pf = LoadPrefab(projectRoot, srcGuid, s.Prefabs);
                var rootGo = pf.Values.FirstOrDefault(x => x.ClassId == 1 && pf.Values.Any(t => (t.ClassId == 4 || t.ClassId == 224) && (t.Body["m_GameObject"] as YRef)?.FileID == x.FileId && ((t.Body["m_Father"] as YRef)?.FileID ?? 0) == 0));
                var name = rootGo != null ? Serializer.Str(rootGo.Body["m_Name"]) : "?";
                if (mod["m_Modifications"] is YSeq mods)
                    foreach (var m in mods.Items.OfType<YMap>())
                        if (Serializer.Str(m["propertyPath"]) == "m_Name" && (m["target"] as YRef)?.FileID == rootGo?.FileId) name = Serializer.Str(m["value"]);
                return (parent.Length == 0 ? name : parent + "/" + name) + "{prefab}";
            }

            // name every object
            foreach (var d in s.Docs.Values)
            {
                if (d.ClassId == 1 && !d.Stripped && transformOfGo.TryGetValue(d.FileId, out var t)) s.GoPath[d.FileId] = TPath(t);
            }
            var compCounter = new Dictionary<string, int>();
            foreach (var d in s.Docs.Values.OrderBy(x => x.FileId))
            {
                string id;
                if (d.ClassId == 1001) { id = "Instance:" + InstPath(d.FileId); }
                else if (d.Stripped)
                {
                    var inst = (d.Body["m_PrefabInstance"] as YRef).FileID;
                    var src = (YRef)d.Body["m_CorrespondingSourceObject"];
                    id = $"Inst:{InstPath(inst)}:{SrcDesc(projectRoot, s, src)}";
                }
                else if (d.ClassId == 1) id = "GO:" + s.GoPath.GetValueOrDefault(d.FileId, "?");
                else if (d.Body["m_GameObject"] is YRef gr && gr.FileID != 0)
                {
                    string gp;
                    if (s.GoPath.TryGetValue(gr.FileID, out var gpp)) gp = gpp;
                    else if (s.Docs.TryGetValue(gr.FileID, out var sgd) && sgd.Stripped)
                    {
                        var src = (YRef)sgd.Body["m_CorrespondingSourceObject"];
                        var rel = SrcDesc(projectRoot, s, src).Split('|')[0];
                        var ip = InstPath((sgd.Body["m_PrefabInstance"] as YRef).FileID);
                        gp = rel.Length == 0 ? ip : ip + "/" + rel;
                    }
                    else gp = "?";
                    var type = d.ClassId == 114 ? "MB:" + (d.Body["m_Script"] as YRef)?.Guid : "C" + d.ClassId;
                    var key = gp + "|" + type;
                    compCounter.TryGetValue(key, out var k); compCounter[key] = k + 1;
                    id = "Comp:" + key + (k > 0 ? "#" + k : "");
                }
                else id = $"Obj:{d.TypeName}";
                s.ObjName[d.FileId] = id;
            }
            return s;
        }

        static string GoPathOf(Scene s, long goId, string root)
        {
            if (s.GoPath.TryGetValue(goId, out var p)) return p;
            if (s.Docs.TryGetValue(goId, out var d) && d.Stripped)
            {
                var inst = (d.Body["m_PrefabInstance"] as YRef).FileID;
                var src = (YRef)d.Body["m_CorrespondingSourceObject"];
                return "INST" + inst + ":" + src.FileID; // resolved lazily below in names
            }
            return "?";
        }

        static string SrcDesc(string root, Scene s, YRef src)
        {
            var pf = LoadPrefab(root, src.Guid, s.Prefabs);
            if (!pf.TryGetValue(src.FileID, out var sd)) return "src?" + src.FileID;
            long goId = sd.ClassId == 1 ? sd.FileId : (sd.Body["m_GameObject"] as YRef)?.FileID ?? 0;
            var tr = pf.Values.FirstOrDefault(t => (t.ClassId == 4 || t.ClassId == 224) && (t.Body["m_GameObject"] as YRef)?.FileID == goId);
            var rel = tr != null ? PrefabRelPath(pf, tr.FileId) : "?";
            var type = sd.ClassId == 114 ? "MB:" + (sd.Body["m_Script"] as YRef)?.Guid : "C" + sd.ClassId;
            return rel + "|" + type;
        }

        static string NormRefScene(YRef r, Scene s)
        {
            if (r.FileID == 0) return "null";
            if (string.IsNullOrEmpty(r.Guid)) return s.ObjName.TryGetValue(r.FileID, out var n) ? n : "local?";
            if (s.Timeline != null && r.Guid == s.Timeline.Guid) return s.Timeline.Names.TryGetValue(r.FileID, out var tn) ? tn : "TL?";
            return $"{r.Guid}:{r.FileID}";
        }

        public static void SceneCmp(string oraclePath, string oursPath, string oracleRoot, string oursRoot, TLInfo otl, TLInfo ntl, TextWriter w)
        {
            var o = LoadScene(oraclePath, oracleRoot, otl);
            var n = LoadScene(oursPath, oursRoot, ntl);
            w.WriteLine("== Scene " + Path.GetFileName(oraclePath));
            var classes = o.Docs.Values.Concat(n.Docs.Values).Select(d => (d.ClassId, d.Stripped)).Distinct().OrderBy(x => x);
            w.WriteLine("  class counts (oracle / ours):");
            foreach (var c in classes)
                w.WriteLine($"    {c.ClassId,10}{(c.Stripped ? " stripped" : "         ")}  {o.Docs.Values.Count(d => d.ClassId == c.ClassId && d.Stripped == c.Stripped),6} / {n.Docs.Values.Count(d => d.ClassId == c.ClassId && d.Stripped == c.Stripped),6}");

            // GameObjects by path
            var oGo = o.Docs.Values.Where(d => d.ClassId == 1 && !d.Stripped).GroupBy(d => o.ObjName[d.FileId]).ToDictionary(g => g.Key, g => g.First());
            var nGo = n.Docs.Values.Where(d => d.ClassId == 1 && !d.Stripped).GroupBy(d => n.ObjName[d.FileId]).ToDictionary(g => g.Key, g => g.First());
            int pathBoth = oGo.Keys.Count(nGo.ContainsKey);
            string CompSig(Scene s, YDoc go) => string.Join(",", ((YSeq)go.Body["m_Component"]).Items.OfType<YMap>().Select(m => (m["component"] as YRef).FileID).Select(id => s.Docs.TryGetValue(id, out var cd) ? (cd.ClassId == 114 ? "MB:" + (cd.Body["m_Script"] as YRef)?.Guid?.Substring(0, 6) : cd.ClassId.ToString()) : "?"));
            int sigEq = oGo.Count(kv => nGo.TryGetValue(kv.Key, out var nd) && CompSig(o, kv.Value) == CompSig(n, nd));
            w.WriteLine($"  GameObjects (plain): oracle {oGo.Count}, ours {nGo.Count}; same path {pathBoth} ({100.0 * pathBoth / Math.Max(1, oGo.Count):0.0}%); same path + same component list {sigEq} ({100.0 * sigEq / Math.Max(1, oGo.Count):0.0}%)");
            foreach (var p in oGo.Keys.Where(k => !nGo.ContainsKey(k)).Take(6)) w.WriteLine("    only oracle: " + p);
            foreach (var p in nGo.Keys.Where(k => !oGo.ContainsKey(k)).Take(6)) w.WriteLine("    only ours:   " + p);
            foreach (var kv in oGo.Where(kv => nGo.TryGetValue(kv.Key, out var nd) && CompSig(o, kv.Value) != CompSig(n, nd)).Take(4))
                w.WriteLine($"    comp list differs: {kv.Key}: oracle [{CompSig(o, kv.Value)}] ours [{CompSig(n, nGo[kv.Key])}]");

            // field values of all plain objects matched by identity
            var oObj = o.Docs.Values.Where(d => !d.Stripped && d.ClassId != 1001).GroupBy(d => o.ObjName[d.FileId]).ToDictionary(g => g.Key, g => g.First());
            var nObj = n.Docs.Values.Where(d => !d.Stripped && d.ClassId != 1001).GroupBy(d => n.ObjName[d.FileId]).ToDictionary(g => g.Key, g => g.First());
            var perClass = new Dictionary<string, Stat>();
            var all = new Stat();
            int matchedObjs = 0, identicalObjs = 0;
            foreach (var (key, od) in oObj)
            {
                if (!nObj.TryGetValue(key, out var nd)) continue;
                matchedObjs++;
                var cls = od.ClassId == 114 ? "MB " + ScriptName(oracleRoot, (od.Body["m_Script"] as YRef)?.Guid) : od.TypeName;
                if (!perClass.TryGetValue(cls, out var st)) perClass[cls] = st = new Stat();
                var fa = Flat(od.Body, r => NormRefScene(r, o));
                var fb = Flat(nd.Body, r => NormRefScene(r, n)).GroupBy(x => x.path).ToDictionary(g => g.Key, g => g.First().val);
                bool same = true;
                foreach (var (p, v) in fa)
                {
                    var nv = fb.TryGetValue(p, out var x) ? x : null;
                    st.Add(cls + "." + Bucket(p), v, nv, key + " ." + p);
                    all.Add(p, v, nv, key);
                    if (!ValEq(v, nv)) same = false;
                }
                if (same) identicalObjs++;
            }
            w.WriteLine($"  plain objects matched by identity: {matchedObjs}/{oObj.Count} (oracle) — fully identical (all fields): {identicalObjs}");
            w.WriteLine($"  field values equal (all matched plain objects): {all.Pct}");
            foreach (var (cls, st) in perClass.OrderByDescending(k => k.Value.Total))
            {
                w.WriteLine($"    {cls,-40} {st.Pct}");
                st.Print(w, 4);
            }

            // prefab instance modifications
            var om = Mods(o); var nm = Mods(n);
            int keyBoth = om.Keys.Count(nm.ContainsKey);
            int valEq = om.Count(kv => nm.TryGetValue(kv.Key, out var v) && ValEq(kv.Value, v));
            w.WriteLine($"  prefab-instance modifications: oracle {om.Count}, ours {nm.Count}; same target+path {keyBoth}; same value {valEq} ({100.0 * valEq / Math.Max(1, om.Count):0.0}%)");
            var missing = om.Keys.Where(k => !nm.ContainsKey(k)).GroupBy(k => Bucket(k.Split('|')[2])).OrderByDescending(g => g.Count()).Take(6);
            foreach (var g in missing) w.WriteLine($"    only oracle ×{g.Count()}: {g.Key}  e.g. {g.First()}");
            var extra = nm.Keys.Where(k => !om.ContainsKey(k)).GroupBy(k => Bucket(k.Split('|')[2])).OrderByDescending(g => g.Count()).Take(6);
            foreach (var g in extra) w.WriteLine($"    only ours   ×{g.Count()}: {g.Key}  e.g. {g.First()}");
            var diffVals = om.Where(kv => nm.TryGetValue(kv.Key, out var v) && !ValEq(kv.Value, v)).GroupBy(kv => Bucket(kv.Key.Split('|')[2])).OrderByDescending(g => g.Count()).Take(6);
            foreach (var g in diffVals) w.WriteLine($"    value differs ×{g.Count()}: {g.Key}  e.g. {g.First().Key} oracle={g.First().Value} ours={nm[g.First().Key]}");

            // added objects / components lists
            Dictionary<string, Dictionary<long, YDoc>> pc = o.Prefabs;
            int Added(Scene s, string list) => s.Docs.Values.Where(d => d.ClassId == 1001).Sum(d => (((YMap)d.Body["m_Modification"])[list] as YSeq)?.Items.Count ?? 0);
            w.WriteLine($"  m_AddedGameObjects entries: oracle {Added(o, "m_AddedGameObjects")}, ours {Added(n, "m_AddedGameObjects")}; m_AddedComponents: oracle {Added(o, "m_AddedComponents")}, ours {Added(n, "m_AddedComponents")}; m_RemovedComponents: oracle {Added(o, "m_RemovedComponents")}, ours {Added(n, "m_RemovedComponents")}");
            // roots
            var oroots = ((YSeq)o.Docs.Values.First(d => d.ClassId == 1660057539).Body["m_Roots"]).Items.OfType<YRef>().Select(r => NormRefScene(r, o)).ToList();
            var nroots = ((YSeq)n.Docs.Values.First(d => d.ClassId == 1660057539).Body["m_Roots"]).Items.OfType<YRef>().Select(r => NormRefScene(r, n)).ToList();
            w.WriteLine($"  SceneRoots: oracle [{string.Join(", ", oroots)}]\n              ours   [{string.Join(", ", nroots)}]");
            var outP = Environment.GetEnvironmentVariable("MINIUNITY_REMAP_OUT");
            if (outP != null) Remap(oraclePath, oursPath, outP, o.ObjName, n.ObjName, otl, ntl, YamlReader.ReadFile(oraclePath), YamlReader.ReadFile(oursPath));
        }

        static readonly Dictionary<string, string> scriptNames = new();
        static string ScriptName(string root, string guid)
        {
            if (guid == null) return "?";
            if (scriptNames.TryGetValue(guid, out var n)) return n;
            var t = ScriptRegistry.TypeOf(guid);
            n = t?.Name ?? guid.Substring(0, 8);
            scriptNames[guid] = n;
            return n;
        }

        static Dictionary<string, string> Mods(Scene s)
        {
            var res = new Dictionary<string, string>();
            foreach (var d in s.Docs.Values.Where(d => d.ClassId == 1001))
            {
                var inst = s.ObjName[d.FileId];
                var mods = ((YMap)d.Body["m_Modification"])["m_Modifications"] as YSeq;
                if (mods == null) continue;
                foreach (var m in mods.Items.OfType<YMap>())
                {
                    var t = (YRef)m["target"];
                    var key = $"{inst}|{t.FileID}|{Serializer.Str(m["propertyPath"])}";
                    var r = m["objectReference"] as YRef;
                    var val = r != null && r.FileID != 0 ? NormRefScene(r, s) : Serializer.Str(m["value"]);
                    res[key] = val;
                }
            }
            return res;
        }
    }
}
