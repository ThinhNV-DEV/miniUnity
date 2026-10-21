// Hierarchy view of a scene / prefab file for the agent tools.
// Real objects are documents of the file; objects inside a prefab instance are "virtual": they come from the source prefab
// (recursively, nested prefabs included) with the instance's modifications applied, and are addressed in this file by
// fileID = (prefabInstanceId ^ idInSourcePrefab) & 0x7FFFFFFFFFFFFFFF – the same ids Unity gives their stripped stand-ins.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MiniUnity.Yaml;

namespace MiniUnity.Agent
{
    public sealed class GComp
    {
        public long Id;              // id in the viewed file
        public int ClassId;
        public string Type;          // "Transform", "MonoBehaviour:PlayerController"
        public bool Virtual;
        public long SrcId;           // id in the source prefab (virtual only)
        public YDoc Doc;             // real only
        public Func<YMap> Effective; // body with overrides applied
        public GNode Owner;
    }

    public sealed class GNode
    {
        public string Name;
        public long Go, Tr;          // ids in the viewed file
        public bool Virtual;
        public long Instance;        // PrefabInstance doc id that produced this node (virtual) / that this node is the root of
        public string SrcGuid;       // source prefab guid (virtual)
        public long SrcGo, SrcTr;    // ids in the source prefab (virtual)
        public bool IsInstanceRoot;
        public GNode Parent;
        public readonly List<GNode> Children = new();
        public readonly List<GComp> Comps = new();
        public Func<YMap> GoBody;    // effective GameObject body
        public string Path => Parent == null ? Name : Parent.Path + "/" + Seg;
        public string Seg
        {
            get
            {
                var sibs = Parent?.Children ?? Graph?.Roots;
                if (sibs == null) return Name;
                int same = 0, mine = 0;
                foreach (var s in sibs) { if (s.Name == Name) { if (s == this) mine = same; same++; } }
                return same > 1 && mine > 0 ? $"{Name}[{mine}]" : Name;
            }
        }
        public SceneGraph Graph;
    }

    public sealed class SceneGraph
    {
        public const long Mask = 0x7FFFFFFFFFFFFFFF;
        public readonly UFile File;
        public readonly List<GNode> Roots = new();
        public readonly Dictionary<long, GNode> ByGo = new(), ByTr = new();
        public readonly Dictionary<long, GComp> CompById = new();
        /// <summary>Stripped stand-ins whose id is not (instance ^ source) – older or sequentially numbered files – → the virtual id.</summary>
        public readonly Dictionary<long, long> StrippedAlias = new();
        static readonly Dictionary<string, SceneGraph> sourceCache = new();
        readonly int depth;

        SceneGraph(UFile f, int depth) { File = f; this.depth = depth; }

        public static SceneGraph Build(UFile f, int depth = 0)
        {
            var g = new SceneGraph(f, depth);
            g.Construct();
            return g;
        }

        static SceneGraph Source(string guid, int depth)
        {
            if (sourceCache.TryGetValue(guid, out var g)) return g;
            var a = AssetDb.ByGuid(guid);
            if (a == null || depth > 12) return null;
            g = Build(UFile.Load(a.Path), depth + 1);
            sourceCache[guid] = g;
            return g;
        }
        public static void ClearCache() => sourceCache.Clear();

        public static long Combine(long instance, long src) => (instance ^ src) & Mask;

        static string Str(YNode n) => Serializer.Str(n);
        static long Fid(YNode n) => (n as YRef)?.FileID ?? 0;
        public long Alias(long id) => StrippedAlias.TryGetValue(id, out var v) ? v : id;

        void Construct()
        {
            var f = File;
            // ---- real GameObjects
            foreach (var d in f.Docs.Where(d => d.ClassId == 1 && !d.Stripped))
            {
                var n = new GNode { Graph = this, Go = d.FileId, Name = Str(d.Body["m_Name"]), GoBody = () => d.Body };
                ByGo[d.FileId] = n;
            }
            foreach (var d in f.Docs.Where(d => !d.Stripped && d.ClassId != 1 && d.ClassId != 1001 && d.Body?["m_GameObject"] is YRef))
            {
                if (!ByGo.TryGetValue(Fid(d.Body["m_GameObject"]), out var n)) continue;
                var c = new GComp { Id = d.FileId, ClassId = d.ClassId, Doc = d, Owner = n, Effective = () => d.Body, Type = TypeName(d.ClassId, d.Body) };
                CompById[d.FileId] = c;
                if (d.ClassId is 4 or 224) { n.Tr = d.FileId; ByTr[d.FileId] = n; }
            }
            // components in GameObject order
            foreach (var n in ByGo.Values)
            {
                var goDoc = f.Get(n.Go);
                if (goDoc.Body["m_Component"] is YSeq comps)
                    foreach (var it in comps.Items.OfType<YMap>())
                        if (CompById.TryGetValue(Fid(it["component"]), out var c) && c.Owner == n) n.Comps.Add(c);
            }

            // ---- prefab instances → virtual subtrees
            var strippedToNode = new Dictionary<long, GNode>();
            foreach (var inst in f.Docs.Where(d => d.ClassId == 1001))
            {
                var mod = inst.Body["m_Modification"] as YMap;
                var src = inst.Body["m_SourcePrefab"] as YRef;
                var sg = src?.Guid != null ? Source(src.Guid, depth) : null;
                if (sg == null)
                {
                    var missing = new GNode { Graph = this, Name = "(missing prefab " + src?.Guid + ")", Instance = inst.FileId, IsInstanceRoot = true, Virtual = true, GoBody = () => new YMap() };
                    instanceRoots[inst.FileId] = missing;
                    continue;
                }
                var mods = (mod?["m_Modifications"] as YSeq)?.Items.OfType<YMap>().ToList() ?? new List<YMap>();
                var removedComps = new HashSet<long>((mod?["m_RemovedComponents"] as YSeq)?.Items.Select(Fid) ?? Enumerable.Empty<long>());
                var removedGos = new HashSet<long>((mod?["m_RemovedGameObjects"] as YSeq)?.Items.Select(Fid) ?? Enumerable.Empty<long>());
                foreach (var sroot in sg.Roots)
                {
                    var v = CloneVirtual(sroot, null, inst.FileId, src.Guid, mods, removedComps, removedGos);
                    if (v == null) continue;
                    v.IsInstanceRoot = true;
                    instanceRoots[inst.FileId] = v;
                }
            }
            foreach (var d in f.Docs.Where(d => d.Stripped))
            {
                long v = Combine(Fid(d.Body["m_PrefabInstance"]), Fid(d.Body["m_CorrespondingSourceObject"]));
                if (v != d.FileId && (ByGo.ContainsKey(v) || ByTr.ContainsKey(v) || CompById.ContainsKey(v))) StrippedAlias[d.FileId] = v;
            }
            // real components added to objects inside an instance (m_GameObject = stripped stand-in of a virtual GameObject)
            foreach (var d in f.Docs.Where(d => !d.Stripped && d.ClassId != 1 && d.ClassId != 1001 && d.Body?["m_GameObject"] is YRef && !CompById.ContainsKey(d.FileId)))
            {
                if (!ByGo.TryGetValue(Alias(Fid(d.Body["m_GameObject"])), out var vn) || !vn.Virtual) continue;
                var c = new GComp { Id = d.FileId, ClassId = d.ClassId, Doc = d, Owner = vn, Effective = () => d.Body, Type = TypeName(d.ClassId, d.Body) };
                CompById[d.FileId] = c;
                vn.Comps.Add(c);
            }
            // stripped stand-ins of virtual objects (targets of m_Father / added components)
            foreach (var d in f.Docs.Where(d => d.Stripped))
            {
                long id = Alias(d.FileId);
                if (ByTr.TryGetValue(id, out var vn) || ByGo.TryGetValue(id, out vn)) strippedToNode[id] = vn;
            }

            // ---- parent / child links
            foreach (var n in ByGo.Values.Where(n => !n.Virtual && n.Tr != 0))
            {
                var tr = f.Get(n.Tr).Body;
                long father = Alias(Fid(tr["m_Father"]));
                if (father != 0 && ByTr.TryGetValue(father, out var p)) n.Parent = p;
            }
            foreach (var kv in instanceRoots)
            {
                var inst = f.Get(kv.Key);
                long tp = Alias(Fid((inst.Body["m_Modification"] as YMap)?["m_TransformParent"]));
                if (tp != 0 && ByTr.TryGetValue(tp, out var p)) kv.Value.Parent = p;
            }
            // children order: real parents follow m_Children; virtual parents keep source order then added objects
            foreach (var n in ByGo.Values)
            {
                if (n.Virtual) continue;
                var tr = n.Tr != 0 ? f.Get(n.Tr).Body : null;
                if (tr?["m_Children"] is YSeq kids)
                    foreach (var k in kids.Items)
                    {
                        long kid = Alias(Fid(k));
                        var child = ByTr.TryGetValue(kid, out var c) ? c : instanceRoots.Values.FirstOrDefault(r => r.Tr == kid);
                        if (child != null && child.Parent == n && !n.Children.Contains(child)) n.Children.Add(child);
                    }
            }
            foreach (var n in ByGo.Values.Concat(instanceRoots.Values).Distinct())
                if (n.Parent != null && !n.Parent.Children.Contains(n)) n.Parent.Children.Add(n);

            // ---- roots
            var sceneRoots = f.Docs.FirstOrDefault(d => d.ClassId == 1660057539)?.Body["m_Roots"] as YSeq;
            var allRoots = ByGo.Values.Concat(instanceRoots.Values).Distinct().Where(n => n.Parent == null).ToList();
            if (sceneRoots != null)
            {
                foreach (var r in sceneRoots.Items)
                {
                    long id = Alias(Fid(r));
                    var n = allRoots.FirstOrDefault(x => x.Tr == id);
                    if (n != null && !Roots.Contains(n)) Roots.Add(n);
                }
            }
            else
            {
                allRoots = allRoots.OrderBy(n => n.Virtual ? 0 : Serializer.Int(f.Get(n.Tr)?.Body["m_RootOrder"], 0)).ToList();
            }
            foreach (var n in allRoots) if (!Roots.Contains(n)) Roots.Add(n);
        }

        readonly Dictionary<long, GNode> instanceRoots = new();

        GNode CloneVirtual(GNode s, GNode parent, long inst, string guid, List<YMap> mods, HashSet<long> removedComps, HashSet<long> removedGos)
        {
            if (removedGos.Contains(s.Go)) return null;
            var v = new GNode
            {
                Graph = this, Virtual = true, Instance = inst, SrcGuid = guid, SrcGo = s.Go, SrcTr = s.Tr,
                Go = Combine(inst, s.Go), Tr = s.Tr != 0 ? Combine(inst, s.Tr) : 0, Parent = parent,
            };
            var srcGoBody = s.GoBody;
            v.GoBody = () => Apply(ToInstanceIds(srcGoBody(), inst), s.Go, guid, mods);
            v.Name = Str(v.GoBody()["m_Name"]);
            ByGo[v.Go] = v;
            if (v.Tr != 0) ByTr[v.Tr] = v;
            foreach (var c in s.Comps)
            {
                if (removedComps.Contains(c.Id)) continue;
                var sc = c;
                var vc = new GComp { Id = Combine(inst, c.Id), ClassId = c.ClassId, Type = c.Type, Virtual = true, SrcId = c.Id, Owner = v };
                vc.Effective = () => Apply(ToInstanceIds(sc.Effective(), inst), sc.Id, guid, mods);
                CompById[vc.Id] = vc;
                v.Comps.Add(vc);
            }
            foreach (var k in s.Children)
            {
                var vk = CloneVirtual(k, v, inst, guid, mods, removedComps, removedGos);
                if (vk != null) v.Children.Add(vk);
            }
            return v;
        }

        /// <summary>
        /// Clone a source-prefab body with its local references (ids in the prefab file) rewritten to the ids the same objects have
        /// in this file (Combine(instance, id)); modifications are applied afterwards, so their own local references (to objects of
        /// this file) are kept as they are.
        /// </summary>
        public static YMap ToInstanceIds(YMap body, long inst)
        {
            var b = (YMap)body.Clone();
            void Walk(YNode n)
            {
                switch (n)
                {
                    case YMap m:
                        for (int i = 0; i < m.Entries.Count; i++)
                        {
                            if (m.Entries[i].Key is "m_CorrespondingSourceObject" or "m_PrefabInstance" or "m_PrefabAsset") continue;
                            if (m.Entries[i].Value is YRef r && r.Raw && r.Guid == null && r.FileID != 0) m.Entries[i] = new(m.Entries[i].Key, new YRef(Combine(inst, r.FileID), null, 0));
                            else Walk(m.Entries[i].Value);
                        }
                        break;
                    case YSeq q:
                        for (int i = 0; i < q.Items.Count; i++)
                            if (q.Items[i] is YRef r && r.Raw && r.Guid == null && r.FileID != 0) q.Items[i] = new YRef(Combine(inst, r.FileID), null, 0);
                            else Walk(q.Items[i]);
                        break;
                }
            }
            Walk(b);
            return b;
        }

        /// <summary>Clone body and apply the instance modifications that target (srcId, guid).</summary>
        public static YMap Apply(YMap body, long srcId, string guid, List<YMap> mods)
        {
            var b = (YMap)body.Clone();
            foreach (var m in mods)
            {
                var t = m["target"] as YRef;
                if (t == null || t.FileID != srcId || !string.Equals(t.Guid, guid, StringComparison.OrdinalIgnoreCase)) continue;
                var path = Str(m["propertyPath"]);
                var oref = m["objectReference"] as YRef;
                var existing = Values.Get(b, path);
                YNode val = existing is YRef || (oref != null && !oref.IsNull) ? oref?.Clone() ?? new YRef(0, null, 0) : new YScalar(Str(m["value"]));
                try { SetRaw(b, path, val); } catch (UaError) { }
            }
            return b;
        }

        /// <summary>Set a node at a property path (no JSON conversion).</summary>
        public static void SetRaw(YMap body, string path, YNode val)
        {
            var parts = Values.Split(path);
            YNode cur = body;
            for (int i = 0; i < parts.Count; i++)
            {
                bool last = i == parts.Count - 1;
                if (parts[i] is "#size")
                {
                    if (cur is YSeq sq && int.TryParse(Str(val), out int n))
                    {
                        while (sq.Items.Count > n) sq.Items.RemoveAt(sq.Items.Count - 1);
                        while (sq.Items.Count < n) sq.Items.Add(sq.Items.Count > 0 ? sq.Items[^1].Clone() : new YScalar(""));
                    }
                    return;
                }
                if (parts[i] is string k)
                {
                    if (cur is not YMap m) return;
                    if (last) { m[k] = val; return; }
                    if (m[k] == null) m[k] = parts[i + 1] is int || parts[i + 1] is "#size" ? new YSeq() : new YMap();
                    cur = m[k];
                }
                else
                {
                    int idx = (int)parts[i];
                    if (cur is not YSeq s) return;
                    while (s.Items.Count <= idx) s.Items.Add(s.Items.Count > 0 ? s.Items[^1].Clone() : new YScalar(""));
                    if (last) { s.Items[idx] = val; return; }
                    // a placeholder from growing an empty array (Array.size) becomes the struct / array the next part addresses
                    if (s.Items[idx] is YScalar ph && string.IsNullOrEmpty(ph.Value))
                        s.Items[idx] = parts[i + 1] is int || parts[i + 1] is "#size" ? new YSeq() : new YMap();
                    cur = s.Items[idx];
                }
            }
        }

        public static string TypeName(int classId, YMap body)
        {
            if (classId != 114) return ClassIds.Name(classId);
            var s = body?["m_Script"] as YRef;
            var a = s?.Guid != null ? AssetDb.ByGuid(s.Guid) : null;
            if (a != null) return System.IO.Path.GetFileNameWithoutExtension(a.Path);
            var t = s?.Guid != null ? ScriptRegistry.TypeOf(s.Guid) : null;
            if (t != null) return t.Name;
            return s == null || s.FileID == 0 ? "MonoBehaviour(missing script)" : "MonoBehaviour(" + s.Guid + ")";
        }

        // ------------------------------------------------------------------ lookup
        public IEnumerable<GNode> All()
        {
            var st = new Stack<GNode>(Roots.AsEnumerable().Reverse());
            while (st.Count > 0)
            {
                var n = st.Pop();
                yield return n;
                for (int i = n.Children.Count - 1; i >= 0; i--) st.Push(n.Children[i]);
            }
        }

        /// <summary>"Canvas/Panel/Button", "Enemy[2]" (third sibling named Enemy), "#123456" (GameObject or Transform id).</summary>
        public GNode Find(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new UaError("empty GameObject path");
            if (path.StartsWith("#"))
            {
                if (!long.TryParse(path[1..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id)) throw new UaError($"bad object id '{path}' (expected #<fileID>)");
                if (ByGo.TryGetValue(id, out var n)) return n;
                if (ByTr.TryGetValue(id, out n)) return n;
                if (CompById.TryGetValue(id, out var c)) return c.Owner;
                return null;
            }
            var segs = path.Trim('/').Split('/');
            IList<GNode> level = Roots;
            GNode cur = null;
            foreach (var seg in segs)
            {
                string name = seg; int idx = 0;
                int b = seg.LastIndexOf('[');
                if (b > 0 && seg.EndsWith("]") && int.TryParse(seg[(b + 1)..^1], out var ix)) { name = seg[..b]; idx = ix; }
                cur = level.Where(n => n.Name == name).Skip(idx).FirstOrDefault();
                if (cur == null) return null;
                level = cur.Children;
            }
            return cur;
        }
        public GNode Need(string path) => Find(path) ?? throw new UaError($"{File.Rel}: GameObject not found: {path}");

        /// <summary>Component on node: "Image", "MonoBehaviour:Foo", "Foo" (script name), "Image[1]", "#id".</summary>
        public GComp Comp(GNode n, string spec)
        {
            if (spec.StartsWith("#"))
            {
                if (!long.TryParse(spec[1..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id)) throw new UaError($"bad component id '{spec}' (expected #<fileID>)");
                return n.Comps.FirstOrDefault(c => c.Id == id);
            }
            int idx = 0;
            int b = spec.LastIndexOf('[');
            if (b > 0 && spec.EndsWith("]") && int.TryParse(spec[(b + 1)..^1], out var ix)) { idx = ix; spec = spec[..b]; }
            if (spec.StartsWith("MonoBehaviour:")) spec = spec[14..];
            return n.Comps.Where(c => string.Equals(c.Type, spec, StringComparison.OrdinalIgnoreCase)
                                      || (spec == "Transform" && c.ClassId == 224)).Skip(idx).FirstOrDefault();
        }
    }
}
