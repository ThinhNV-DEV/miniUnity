// A model asset from an .fbx file when Unity's import result (Library/Artifacts) is not there: the node hierarchy as
// GameObjects (one root named after the file; a file with a single top node uses it as the root), Lcl / pre / post rotation
// and pivots, meshes (polygons fanned into triangles, vertices split per normal / uv / colour like Unity, one submesh per
// material, normals computed at 60° when the file has none), Standard materials from the FBX materials (diffuse colour,
// opacity, emission, diffuse / normal map textures looked up next to the model) or the .meta's remapped materials.
// Conversion follows ModelImporter's defaults: file axes → Y-up (root rotation), right- → left-handed (x mirrored),
// file units → metres (UnitScaleFactor / 100, × globalScale). Sub-object file IDs come from the .meta's
// internalIDToNameTable. Not imported: skinning / blend shapes (meshes are static), animation, cameras and lights.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniUnity.Yaml;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MiniUnity
{
    public static class FbxImporter
    {
        sealed class Obj { public long Id; public string Class, Name, Sub; public FbxNode Node; }

        public static bool Import(LoadedAsset la, YMap meta, Action<long, Object, bool> add)
        {
            FbxNode doc;
            try { doc = FbxReader.Read(AssetIndex.Abs(la.Path)); }
            catch (Exception e) { Debug.LogWarning($"[import] {la.Path}: not a readable FBX file ({e.Message})"); return false; }
            var objectsNode = doc.Child("Objects");
            if (objectsNode == null) return false;
            var mi = meta?["ModelImporter"] as YMap;

            // ---------------------------------------------------------------- objects and connections
            var objs = new Dictionary<long, Obj>();
            foreach (var n in objectsNode.Children)
            {
                var full = n.S(1) ?? "";
                int sep = full.IndexOf("::", StringComparison.Ordinal);
                objs[n.L(0)] = new Obj { Id = n.L(0), Class = n.Name, Name = sep >= 0 ? full.Substring(sep + 2) : full, Sub = n.S(2) ?? "", Node = n };
            }
            var children = new Dictionary<long, List<(long id, string prop)>>();
            var parentOf = new Dictionary<long, long>();
            foreach (var c in doc.Child("Connections")?.All("C") ?? Enumerable.Empty<FbxNode>())
            {
                long child = c.L(1), parent = c.L(2);
                if (!children.TryGetValue(parent, out var l)) children[parent] = l = new();
                l.Add((child, c.S(3)));
                if (c.S(0) == "OO" && objs.TryGetValue(child, out var co) && co.Class == "Model") parentOf[child] = parent;
            }
            IEnumerable<Obj> Kids(long id, string cls) => children.TryGetValue(id, out var l) ? l.Where(x => x.prop == null && objs.ContainsKey(x.id) && objs[x.id].Class == cls).Select(x => objs[x.id]) : Enumerable.Empty<Obj>();

            // ---------------------------------------------------------------- global settings
            var gs = Props70(doc.Child("GlobalSettings"));
            double unit = GetD(gs, "UnitScaleFactor", 1);
            bool useFileScale = mi == null || Serializer.Int((mi["meshes"] as YMap)?["useFileScale"], 1) != 0;
            double globalScale = mi != null && (mi["meshes"] as YMap)?["globalScale"] is YScalar g ? Serializer.F(g) : 1;
            double scale = (useFileScale ? unit / 100.0 : 1) * globalScale;
            var axes = AxisConversion(gs);

            // ---------------------------------------------------------------- meta names → file IDs
            var ids = new Dictionary<(int cls, string name), long>();
            if (mi?["internalIDToNameTable"] is YSeq table)
                foreach (var e in table.Items.OfType<YMap>())
                    if (e["first"] is YMap f && f.Entries.Count > 0 && int.TryParse(f.Entries[0].Key, out int cls))
                        ids.TryAdd((cls, Serializer.Str(e["second"])), Serializer.Long(f.Entries[0].Value));
            if (mi?["fileIDToRecycleName"] is YMap recycle)
                foreach (var kv in recycle.Entries)
                    if (long.TryParse(kv.Key, out long fid)) ids.TryAdd(((int)(fid / 100000), Serializer.Str(kv.Value)), fid);
            var used = new HashSet<long>();
            var occurrences = new Dictionary<(int, string), int>();
            long Id(int cls, string name)
            {
                if (ids.TryGetValue((cls, name), out var v) && used.Add(v)) return v;
                // not in the .meta (Unity 2019.3+ writes no table): Unity's own id, xxHash64 of "Type:<Class>-><name><n>" where n counts
                // earlier objects of the same class and name
                int n = occurrences.TryGetValue((cls, name), out var k) ? k : 0;
                occurrences[(cls, name)] = n + 1;
                long id = unchecked((long)XxHash64(System.Text.Encoding.UTF8.GetBytes($"Type:{Agent.ClassIds.Name(cls)}->{name}{n}")));
                while (!used.Add(id)) id--;
                return id;
            }

            // ---------------------------------------------------------------- materials
            var remap = new Dictionary<string, Material>();
            if (mi?["externalObjects"] is YSeq ext)
                foreach (var e in ext.Items.OfType<YMap>())
                    if (e["first"] is YMap f && Serializer.Str(f["type"]).EndsWith("Material") && e["second"] is YRef r && r.Guid != null)
                        if (AssetLoader.ResolveGuid(r.Guid, r.FileID, typeof(Material)) is Material m) remap[Serializer.Str(f["name"])] = m;
            int importMode = mi?["materials"] is YMap mm ? Serializer.Int(mm["materialImportMode"], 1) : 1;
            var materials = new Dictionary<long, Material>();
            Material MaterialFor(Obj mo)
            {
                if (materials.TryGetValue(mo.Id, out var m)) return m;
                if (remap.TryGetValue(mo.Name, out m)) return materials[mo.Id] = m;
                if (importMode == 0) return materials[mo.Id] = BuiltinResources.DefaultMaterial;
                m = MakeMaterial(mo, objs, children, la.Path);
                add(Id(21, mo.Name), m, false);
                return materials[mo.Id] = m;
            }

            // ---------------------------------------------------------------- hierarchy
            string fileName = Path.GetFileNameWithoutExtension(la.Path);
            var tops = objs.Values.Where(o => o.Class == "Model" && (!parentOf.TryGetValue(o.Id, out var p) || p == 0 || !objs.ContainsKey(p))).ToList();
            var pending = new List<(Obj model, GameObject go)>();
            GameObject Make(string name, GameObject parent)
            {
                var go = AssetLoader.CreateBareGameObject();
                go.m_Name = name;
                var t = (Transform)Lifecycle.CreateComponentRaw(go, typeof(Transform));
                go.m_Transform = t;
                go.m_InPrefabAsset = true;
                if (parent != null) { t.m_Parent = parent.m_Transform; parent.m_Transform.m_Children.Add(t); }
                return go;
            }
            void Build(Obj model, GameObject go)
            {
                pending.Add((model, go));
                foreach (var ch in Kids(model.Id, "Model")) Build(ch, Make(ch.Name, go));
            }
            GameObject root;
            Matrix4x4 rootAxes = axes;
            if (tops.Count == 1)
            {
                root = Make(fileName, null);
                Build(tops[0], root);
            }
            else
            {
                root = Make(fileName, null);
                foreach (var t in tops) Build(t, Make(t.Name, root));
            }
            // transforms (the root of a collapsed single-node file keeps its node's transform under the axis conversion)
            foreach (var (model, go) in pending)
            {
                var local = ModelMatrix(model.Node, scale);
                if (go == root) local = rootAxes * local;
                Apply(go.m_Transform, local);
            }
            if (tops.Count != 1) Apply(root.m_Transform, rootAxes);

            // ---------------------------------------------------------------- meshes
            int meshCount = 0;
            foreach (var (model, go) in pending)
            {
                var geo = Kids(model.Id, "Geometry").FirstOrDefault(gg => gg.Sub == "Mesh" || gg.Node.Child("Vertices") != null);
                if (geo == null) continue;
                var mats = Kids(model.Id, "Material").ToList();
                var mesh = BuildMesh(geo.Node, GeometricMatrix(model.Node), scale, Math.Max(1, mats.Count), out int usedSubmeshes);
                if (mesh == null) continue;
                mesh.name = model.Name;   // the node's name, also when the root GameObject takes the file's name
                add(Id(43, mesh.name), mesh, false);
                meshCount++;
                var mf = (MeshFilter)Lifecycle.CreateComponentRaw(go, typeof(MeshFilter));
                mf.sharedMesh = mesh;
                var mr = (MeshRenderer)Lifecycle.CreateComponentRaw(go, typeof(MeshRenderer));
                var list = new Material[usedSubmeshes];
                for (int i = 0; i < usedSubmeshes; i++) list[i] = i < mats.Count ? MaterialFor(mats[i]) : BuiltinResources.DefaultMaterial;
                mr.sharedMaterials = list;
            }

            // register the GameObjects and components (the root first: the asset's main object)
            // without an id table in the .meta (Unity 2019.3+) the root's objects have the same file ids in every model
            long RootId(int cls) => cls switch { 1 => 919132149155446097, 4 => -8679921383154817045, 23 => -7511558181221131132, _ => 0 };
            void Register(GameObject go)
            {
                long Of(int cls) => go == root && ids.Count == 0 && RootId(cls) is long r && r != 0 && used.Add(r) ? r : Id(cls, go.m_Name);
                add(Of(1), go, go == root);
                foreach (var c in go.m_Components)
                {
                    int cls = c switch { Transform => 4, MeshFilter => 33, MeshRenderer => 23, _ => 0 };
                    if (cls != 0) add(Of(cls), c, false);
                }
                foreach (var ch in go.m_Transform.m_Children) Register(ch.gameObject);
            }
            Register(root);
            la.IsPrefab = true;
            la.Main = root;
            return true;
        }

        /// <summary>XXH64 with seed 0 (the hash behind Unity's model sub-asset file IDs).</summary>
        internal static ulong XxHash64(byte[] data)
        {
            const ulong P1 = 11400714785074694791UL, P2 = 14029467366897019727UL, P3 = 1609587929392839161UL, P4 = 9650029242287828579UL, P5 = 2870177450012600261UL;
            static ulong Rotl(ulong x, int r) => (x << r) | (x >> (64 - r));
            static ulong Round(ulong acc, ulong input) => Rotl(acc + input * P2, 31) * P1;
            static ulong Merge(ulong acc, ulong val) => (acc ^ Round(0, val)) * P1 + P4;
            int len = data.Length, i = 0;
            ulong h;
            if (len >= 32)
            {
                ulong v1 = unchecked(P1 + P2), v2 = P2, v3 = 0, v4 = unchecked(0 - P1);
                for (; i <= len - 32; i += 32)
                {
                    v1 = Round(v1, BitConverter.ToUInt64(data, i)); v2 = Round(v2, BitConverter.ToUInt64(data, i + 8));
                    v3 = Round(v3, BitConverter.ToUInt64(data, i + 16)); v4 = Round(v4, BitConverter.ToUInt64(data, i + 24));
                }
                h = Rotl(v1, 1) + Rotl(v2, 7) + Rotl(v3, 12) + Rotl(v4, 18);
                h = Merge(h, v1); h = Merge(h, v2); h = Merge(h, v3); h = Merge(h, v4);
            }
            else h = P5;
            h += (ulong)len;
            for (; i + 8 <= len; i += 8) h = Rotl(h ^ Round(0, BitConverter.ToUInt64(data, i)), 27) * P1 + P4;
            if (i + 4 <= len) { h = Rotl(h ^ (BitConverter.ToUInt32(data, i) * P1), 23) * P2 + P3; i += 4; }
            for (; i < len; i++) h = Rotl(h ^ (data[i] * P5), 11) * P1;
            h ^= h >> 33; h *= P2; h ^= h >> 29; h *= P3; h ^= h >> 32;
            return h;
        }

        // ---------------------------------------------------------------- properties
        static Dictionary<string, FbxNode> Props70(FbxNode owner)
        {
            var d = new Dictionary<string, FbxNode>();
            foreach (var p in owner?.Child("Properties70")?.All("P") ?? Enumerable.Empty<FbxNode>()) if (p.S(0) is string n) d[n] = p;
            return d;
        }
        static double GetD(Dictionary<string, FbxNode> p, string n, double def) => p.TryGetValue(n, out var v) ? v.D(4) : def;
        static Vector3D GetV(Dictionary<string, FbxNode> p, string n, Vector3D def) => p.TryGetValue(n, out var v) && v.Props.Count >= 7 ? new Vector3D(v.D(4), v.D(5), v.D(6)) : def;

        // ---------------------------------------------------------------- transforms
        readonly struct Vector3D { public readonly double x, y, z; public Vector3D(double x, double y, double z) { this.x = x; this.y = y; this.z = z; } }

        /// <summary>FBX node local transform T·Roff·Rp·Rpre·R·Rpost⁻¹·Rp⁻¹·Soff·Sp·S·Sp⁻¹, converted to Unity (x mirrored, metres).</summary>
        static Matrix4x4 ModelMatrix(FbxNode model, double scale)
        {
            var p = Props70(model);
            var zero = new Vector3D(0, 0, 0); var one = new Vector3D(1, 1, 1);
            int order = p.TryGetValue("RotationOrder", out var ro) ? (int)ro.L(4) : 0;
            var m = T(GetV(p, "Lcl Translation", zero)) * T(GetV(p, "RotationOffset", zero)) * T(GetV(p, "RotationPivot", zero))
                  * R(GetV(p, "PreRotation", zero), 0) * R(GetV(p, "Lcl Rotation", zero), order) * R(GetV(p, "PostRotation", zero), 0).inverse
                  * T(Neg(GetV(p, "RotationPivot", zero))) * T(GetV(p, "ScalingOffset", zero)) * T(GetV(p, "ScalingPivot", zero))
                  * S(GetV(p, "Lcl Scaling", one)) * T(Neg(GetV(p, "ScalingPivot", zero)));
            return ToUnity(m, scale);
        }

        /// <summary>The geometric transform (applied to the mesh only, not to children), in Unity space with the translation in metres.</summary>
        static Matrix4x4 GeometricMatrix(FbxNode model)
        {
            var p = Props70(model);
            var zero = new Vector3D(0, 0, 0); var one = new Vector3D(1, 1, 1);
            return ToUnity(T(GetV(p, "GeometricTranslation", zero)) * R(GetV(p, "GeometricRotation", zero), 0) * S(GetV(p, "GeometricScaling", one)), 1);
        }

        static Vector3D Neg(Vector3D v) => new(-v.x, -v.y, -v.z);
        static Matrix4x4 T(Vector3D v) => Matrix4x4.Translate(new Vector3((float)v.x, (float)v.y, (float)v.z));
        static Matrix4x4 S(Vector3D v) => Matrix4x4.Scale(new Vector3((float)v.x, (float)v.y, (float)v.z));
        /// <summary>Euler degrees in an FBX rotation order (0 = XYZ: X applied first).</summary>
        static Matrix4x4 R(Vector3D e, int order)
        {
            Matrix4x4 X = Rot(new Vector3(1, 0, 0), e.x), Y = Rot(new Vector3(0, 1, 0), e.y), Z = Rot(new Vector3(0, 0, 1), e.z);
            return order switch
            {
                1 => Y * Z * X,   // XZY
                2 => X * Z * Y,   // YZX
                3 => Z * X * Y,   // YXZ
                4 => X * Y * Z,   // ZXY
                5 => Y * X * Z,   // ZYX
                _ => Z * Y * X,   // XYZ
            };
        }
        static Matrix4x4 Rot(Vector3 axis, double deg)
        {
            float c = (float)Math.Cos(deg * Math.PI / 180), s = (float)Math.Sin(deg * Math.PI / 180);
            var m = Matrix4x4.identity;
            if (axis.x != 0) { m.m11 = c; m.m12 = -s; m.m21 = s; m.m22 = c; }
            else if (axis.y != 0) { m.m00 = c; m.m02 = s; m.m20 = -s; m.m22 = c; }
            else { m.m00 = c; m.m01 = -s; m.m10 = s; m.m11 = c; }
            return m;
        }
        /// <summary>Right-handed FBX → left-handed Unity: mirror x on both sides (C·M·C), translation in metres.</summary>
        static Matrix4x4 ToUnity(Matrix4x4 m, double scale)
        {
            var c = Matrix4x4.Scale(new Vector3(-1, 1, 1));
            var r = c * m * c;
            r.m03 *= (float)scale; r.m13 *= (float)scale; r.m23 *= (float)scale;
            return r;
        }

        /// <summary>The rotation that brings the file's axes (GlobalSettings Up / Front / Coord axis and sign) to Y-up, in Unity space.</summary>
        static Matrix4x4 AxisConversion(Dictionary<string, FbxNode> gs)
        {
            int up = (int)GetD(gs, "UpAxis", 1), upSign = (int)GetD(gs, "UpAxisSign", 1);
            int front = (int)GetD(gs, "FrontAxis", 2), frontSign = (int)GetD(gs, "FrontAxisSign", 1);
            int coord = (int)GetD(gs, "CoordAxis", 0), coordSign = (int)GetD(gs, "CoordAxisSign", 1);
            if (up == 1 && upSign == 1 && front == 2 && frontSign == 1 && coord == 0 && coordSign == 1) return Matrix4x4.identity;
            if (up == front || up == coord || front == coord || up is < 0 or > 2 || front is < 0 or > 2 || coord is < 0 or > 2) return Matrix4x4.identity;
            var a = Matrix4x4.zero; a.m33 = 1;
            a[0, coord] = coordSign; a[1, up] = upSign; a[2, front] = frontSign;
            if (a.determinant < 0) a[2, front] = -frontSign;   // keep it a rotation (Unity applies a rotation, not a mirror, on the root)
            return ToUnity(a, 1);
        }

        static void Apply(Transform t, Matrix4x4 m)
        {
            var pos = new Vector3(m.m03, m.m13, m.m23);
            var sx = new Vector3(m.m00, m.m10, m.m20).magnitude; var sy = new Vector3(m.m01, m.m11, m.m21).magnitude; var sz = new Vector3(m.m02, m.m12, m.m22).magnitude;
            if (m.determinant < 0) sx = -sx;
            var rm = Matrix4x4.identity;
            if (sx != 0) { rm.m00 = m.m00 / sx; rm.m10 = m.m10 / sx; rm.m20 = m.m20 / sx; }
            if (sy != 0) { rm.m01 = m.m01 / sy; rm.m11 = m.m11 / sy; rm.m21 = m.m21 / sy; }
            if (sz != 0) { rm.m02 = m.m02 / sz; rm.m12 = m.m12 / sz; rm.m22 = m.m22 / sz; }
            t.localPosition = pos;
            t.localRotation = rm.rotation;
            t.localScale = new Vector3(sx, sy, sz);
        }

        // ---------------------------------------------------------------- mesh
        sealed class Layer
        {
            public string Mapping = "ByPolygonVertex", Reference = "Direct";
            public double[] Data; public int[] Index; public int Width;
            public static Layer Of(FbxNode n, string data, string index, int width)
            {
                if (n == null || n.Child(data) == null) return null;
                return new Layer
                {
                    Mapping = n.Child("MappingInformationType")?.S(0) ?? "ByPolygonVertex", Reference = n.Child("ReferenceInformationType")?.S(0) ?? "Direct",
                    Data = n.Child(data).Doubles(), Index = index != null ? n.Child(index)?.Ints() : null, Width = width,
                };
            }
            public int Slot(int pvi, int cp, int poly)
            {
                int i = Mapping switch { "ByPolygonVertex" => pvi, "ByVertice" or "ByVertex" or "ByControlPoint" => cp, "ByPolygon" => poly, "AllSame" => 0, _ => pvi };
                if ((Reference == "IndexToDirect" || Reference == "Index") && Index != null) i = i < Index.Length ? Index[i] : 0;
                return i;
            }
            public double Get(int slot, int c) { int k = slot * Width + c; return Data != null && k >= 0 && k < Data.Length ? Data[k] : 0; }
        }

        static Mesh BuildMesh(FbxNode g, Matrix4x4 geom, double scale, int materialCount, out int submeshes)
        {
            submeshes = 1;
            var v = g.Child("Vertices")?.Doubles(); var pv = g.Child("PolygonVertexIndex")?.Ints();
            if (v == null || pv == null || v.Length < 3 || pv.Length < 3) return null;
            var nrm = Layer.Of(g.Child("LayerElementNormal"), "Normals", "NormalsIndex", 3);
            var uvs = g.All("LayerElementUV").OrderBy(l => l.L(0)).Select(l => Layer.Of(l, "UV", "UVIndex", 2)).Where(l => l != null).Take(2).ToList();
            var col = Layer.Of(g.Child("LayerElementColor"), "Colors", "ColorIndex", 4);
            var matL = g.Child("LayerElementMaterial");
            var matIdx = matL?.Child("Materials")?.Ints();
            string matMap = matL?.Child("MappingInformationType")?.S(0) ?? "AllSame";
            var geomN = geom.inverse.transpose;

            // control points in Unity space
            int ncp = v.Length / 3;
            var cps = new Vector3[ncp];
            for (int i = 0; i < ncp; i++)
            {
                var p = new Vector3(-(float)v[i * 3], (float)v[i * 3 + 1], (float)v[i * 3 + 2]);
                p = geom.MultiplyPoint3x4(p);
                cps[i] = p * (float)scale;
            }
            // polygons
            var polys = new List<(int start, int count)>();
            for (int i = 0, st = 0; i < pv.Length; i++) if (pv[i] < 0) { polys.Add((st, i - st + 1)); st = i + 1; }
            int CP(int i) => pv[i] < 0 ? ~pv[i] : pv[i];

            // computed normals when the file has none (Unity: smoothing angle 60°)
            Vector3[] faceN = new Vector3[polys.Count];
            for (int f = 0; f < polys.Count; f++)
            {
                var (st, n) = polys[f];
                var acc = Vector3.zero;
                for (int k = 0; k < n; k++) { var a = cps[CP(st + k)]; var b = cps[CP(st + (k + 1) % n)]; acc += new Vector3((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)); }
                faceN[f] = -acc.normalized;   // Newell over the mirrored points in file order: negated to point outwards
            }
            Dictionary<int, List<int>> facesOf = null;
            if (nrm == null)
            {
                facesOf = new();
                for (int f = 0; f < polys.Count; f++) for (int k = 0; k < polys[f].count; k++) { int c = CP(polys[f].start + k); if (!facesOf.TryGetValue(c, out var l)) facesOf[c] = l = new(); if (!l.Contains(f)) l.Add(f); }
            }
            float cos60 = Mathf.Cos(60 * Mathf.Deg2Rad);

            var key = new Dictionary<(int, Vector3, Vector2, Vector2, Color), int>();
            var outV = new List<Vector3>(); var outN = new List<Vector3>(); var outUV = new List<Vector2>(); var outUV2 = new List<Vector2>(); var outC = new List<Color>();
            var tris = new List<int>[Math.Max(1, materialCount)];
            for (int i = 0; i < tris.Length; i++) tris[i] = new List<int>();
            for (int f = 0; f < polys.Count; f++)
            {
                var (st, n) = polys[f];
                if (n < 3) continue;
                int sub = matIdx == null || matIdx.Length == 0 ? 0 : matMap == "AllSame" ? matIdx[0] : (f < matIdx.Length ? matIdx[f] : 0);
                if (sub < 0 || sub >= tris.Length) sub = 0;
                var idx = new int[n];
                for (int k = 0; k < n; k++)
                {
                    int pvi = st + k, cp = CP(pvi);
                    Vector3 nn;
                    if (nrm != null) { int s = nrm.Slot(pvi, cp, f); nn = geomN.MultiplyVector(new Vector3(-(float)nrm.Get(s, 0), (float)nrm.Get(s, 1), (float)nrm.Get(s, 2))).normalized; }
                    else { nn = Vector3.zero; foreach (var of in facesOf[cp]) if (Vector3.Dot(faceN[of], faceN[f]) >= cos60) nn += faceN[of]; nn = nn.normalized; }
                    Vector2 t0 = default, t1 = default;
                    if (uvs.Count > 0) { int s = uvs[0].Slot(pvi, cp, f); t0 = new Vector2((float)uvs[0].Get(s, 0), (float)uvs[0].Get(s, 1)); }
                    if (uvs.Count > 1) { int s = uvs[1].Slot(pvi, cp, f); t1 = new Vector2((float)uvs[1].Get(s, 0), (float)uvs[1].Get(s, 1)); }
                    Color cc = Color.white;
                    if (col != null) { int s = col.Slot(pvi, cp, f); cc = new Color((float)col.Get(s, 0), (float)col.Get(s, 1), (float)col.Get(s, 2), (float)col.Get(s, 3)); }
                    var kk = (cp, nn, t0, t1, cc);
                    if (!key.TryGetValue(kk, out int vi))
                    {
                        vi = outV.Count; key[kk] = vi;
                        outV.Add(cps[cp]); outN.Add(nn); outUV.Add(t0); outUV2.Add(t1); outC.Add(cc);
                    }
                    idx[k] = vi;
                }
                // fan, winding reversed by the x mirror
                for (int k = 1; k + 1 < n; k++) { tris[sub].Add(idx[0]); tris[sub].Add(idx[k + 1]); tris[sub].Add(idx[k]); }
            }
            var mesh = new Mesh();
            if (outV.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = outV.ToArray();
            mesh.normals = outN.ToArray();
            if (uvs.Count > 0) mesh.uv = outUV.ToArray();
            if (uvs.Count > 1) mesh.uv2 = outUV2.ToArray();
            if (col != null) mesh.colors = outC.ToArray();
            // trailing materials without triangles still get their (empty) submesh, as Unity keeps the material slots
            submeshes = tris.Length;
            mesh.subMeshCount = submeshes;
            for (int i = 0; i < submeshes; i++) mesh.SetTriangles(tris[i].ToArray(), i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // ---------------------------------------------------------------- materials
        static Material MakeMaterial(Obj mo, Dictionary<long, Obj> objs, Dictionary<long, List<(long id, string prop)>> children, string modelPath)
        {
            var p = Props70(mo.Node);
            var m = new Material(ShaderIndex.Find("Standard")) { name = mo.Name };
            Color Col(string n, Color d) => p.TryGetValue(n, out var v) && v.Props.Count >= 7 ? new Color((float)v.D(4), (float)v.D(5), (float)v.D(6), 1) : d;
            Texture2D Tex(params string[] props)
            {
                if (!children.TryGetValue(mo.Id, out var l)) return null;
                foreach (var (id, prop) in l)
                    if (prop != null && props.Contains(prop) && objs.TryGetValue(id, out var t) && t.Class == "Texture") return FindTexture(t, modelPath);
                return null;
            }
            var diffuse = Col("DiffuseColor", Col("Diffuse", Color.white));
            double factor = p.TryGetValue("DiffuseFactor", out var df) ? df.D(4) : 1;
            double opacity = p.TryGetValue("Opacity", out var op) ? op.D(4) : p.TryGetValue("TransparencyFactor", out var tf) ? 1 - tf.D(4) : 1;
            var mainTex = Tex("DiffuseColor", "Maya|baseColor", "3dsMax|Parameters|base_color_map", "base_color_map");
            if (mainTex != null) m.SetTexture("_MainTex", mainTex);
            else
            {
                var c = new Color(diffuse.r * (float)factor, diffuse.g * (float)factor, diffuse.b * (float)factor, (float)Math.Clamp(opacity, 0, 1));
                // the description holds linear values; a linear project stores the gamma colour in the material, like Unity
                m.SetColor("_Color", QualitySettings.activeColorSpace == ColorSpace.Linear ? c.gamma : c);
            }
            var bump = Tex("NormalMap", "Bump", "Maya|normalCamera");
            if (bump != null) { m.SetTexture("_BumpMap", bump); m.EnableKeyword("_NORMALMAP"); }
            var emission = Col("EmissiveColor", Color.black);
            double ef = p.TryGetValue("EmissiveFactor", out var ee) ? ee.D(4) : 1;
            if (emission.maxColorComponent * ef > 0) { m.SetColor("_EmissionColor", emission * (float)ef); m.EnableKeyword("_EMISSION"); }
            if (opacity < 1)
            {
                // Standard "Fade"
                m.SetFloat("_Mode", 2); m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_ALPHABLEND_ON"); m.renderQueue = 3000;
            }
            return m;
        }

        /// <summary>A texture referenced by the FBX: its relative path from the model, then its file name next to the model, in a
        /// Textures folder there or above (Unity's search), then anywhere in Assets.</summary>
        static Texture2D FindTexture(Obj tex, string modelPath)
        {
            string file = tex.Node.Child("RelativeFilename")?.S(0) ?? tex.Node.Child("FileName")?.S(0);
            if (string.IsNullOrEmpty(file)) return null;
            file = file.Replace('\\', '/');
            string name = Path.GetFileName(file), dir = Path.GetDirectoryName(modelPath)?.Replace('\\', '/') ?? "";
            var candidates = new List<string> { Norm(dir + "/" + file) };
            for (var d = dir; !string.IsNullOrEmpty(d); d = Path.GetDirectoryName(d)?.Replace('\\', '/'))
            {
                candidates.Add(d + "/" + name);
                candidates.Add(d + "/Textures/" + name);
                candidates.Add(d + "/textures/" + name);
            }
            foreach (var c in candidates)
                if (AssetIndex.GuidOf(c) != null && AssetLoader.LoadAssetAtPath(c, typeof(Texture2D)) is Texture2D t) return t;
            var any = AssetIndex.AllPaths().FirstOrDefault(pp => string.Equals(Path.GetFileName(pp), name, StringComparison.OrdinalIgnoreCase));
            return any != null ? AssetLoader.LoadAssetAtPath(any, typeof(Texture2D)) as Texture2D : null;
        }
        static string Norm(string p)
        {
            var parts = new List<string>();
            foreach (var s in p.Split('/')) { if (s == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); } else if (s != "." && s != "") parts.Add(s); }
            return string.Join("/", parts);
        }
    }
}
