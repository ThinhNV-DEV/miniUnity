// Managed Mesh: vertex streams + per-submesh index lists. uGUI (VertexHelper.FillMesh) and TMP write their geometry
// here before handing the mesh to CanvasRenderer.SetMesh, which snapshots it for the renderer.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public sealed partial class Mesh
    {
        internal Vector3[] m_V = Array.Empty<Vector3>();
        internal Vector3[] m_N;
        internal Vector4[] m_T;
        internal Color[] m_C;
        internal readonly Vector4[][] m_UV = new Vector4[8][];
        internal readonly List<int[]> m_Sub = new() { Array.Empty<int>() };
        internal readonly List<MeshTopology> m_Topo = new() { MeshTopology.Triangles };
        Bounds m_Bounds;
        IndexFormat m_IndexFormat = IndexFormat.UInt16;

        /// <summary>Mesh asset loaded from YAML: decode m_VertexData / m_IndexBuffer / m_SubMeshes once.</summary>
        internal void EnsureData()
        {
            if (m_V.Length > 0 || m_Native == null || m_Decoded) return;
            m_Decoded = true;
            try
            {
                var vd = m_Native["m_VertexData"] as MiniUnity.Yaml.YMap;
                int n = MiniUnity.Serializer.Int(vd?["m_VertexCount"], 0);
                var raw = Hex((vd?["_typelessdata"] as MiniUnity.Yaml.YScalar)?.Value);
                var ch = (vd?["m_Channels"] as MiniUnity.Yaml.YSeq)?.Items.OfType<MiniUnity.Yaml.YMap>().ToList() ?? new();
                // stream layout: channels of stream 0 interleaved; stride = sum of channel sizes in that stream
                int[] stride = new int[4];
                var info = ch.Select(c => (stream: MiniUnity.Serializer.Int(c["stream"], 0), off: MiniUnity.Serializer.Int(c["offset"], 0), fmt: MiniUnity.Serializer.Int(c["format"], 0), dim: MiniUnity.Serializer.Int(c["dimension"], 0) & 0xF)).ToList();
                foreach (var c in info) if (c.dim > 0) stride[c.stream] = Math.Max(stride[c.stream], c.off + c.dim * FmtSize(c.fmt));
                int[] streamStart = new int[4]; int acc = 0;
                for (int si = 0; si < 4; si++) { streamStart[si] = acc; acc += stride[si] * n; acc = (acc + 15) & ~15; }
                float Read(int ci, int v, int comp)
                {
                    var c = info[ci]; int o = streamStart[c.stream] + v * stride[c.stream] + c.off + comp * FmtSize(c.fmt);
                    return c.fmt switch
                    {
                        0 => BitConverter.ToSingle(raw, o),
                        1 => (float)BitConverter.ToHalf(raw, o),
                        2 => raw[o] / 255f,
                        _ => BitConverter.ToSingle(raw, o),
                    };
                }
                var V = new Vector3[n];
                for (int i = 0; i < n; i++) V[i] = new Vector3(Read(0, i, 0), Read(0, i, 1), Read(0, i, 2));
                m_V = V;
                if (info.Count > 1 && info[1].dim > 0) { var N = new Vector3[n]; for (int i = 0; i < n; i++) N[i] = new Vector3(Read(1, i, 0), Read(1, i, 1), Read(1, i, 2)); m_N = N; }
                if (info.Count > 3 && info[3].dim > 0) { var C = new Color[n]; for (int i = 0; i < n; i++) C[i] = new Color(Read(3, i, 0), Read(3, i, 1), Read(3, i, 2), info[3].dim > 3 ? Read(3, i, 3) : 1); m_C = C; }
                if (info.Count > 4 && info[4].dim > 0) { var U = new Vector4[n]; for (int i = 0; i < n; i++) U[i] = new Vector4(Read(4, i, 0), info[4].dim > 1 ? Read(4, i, 1) : 0, 0, 0); m_UV[0] = U; }
                var ib = Hex((m_Native["m_IndexBuffer"] as MiniUnity.Yaml.YScalar)?.Value);
                bool i32 = MiniUnity.Serializer.Int(m_Native["m_IndexFormat"], 0) == 1;
                m_Sub.Clear(); m_Topo.Clear();
                foreach (var sm in (m_Native["m_SubMeshes"] as MiniUnity.Yaml.YSeq)?.Items.OfType<MiniUnity.Yaml.YMap>() ?? Enumerable.Empty<MiniUnity.Yaml.YMap>())
                {
                    int first = MiniUnity.Serializer.Int(sm["firstByte"], 0), count = MiniUnity.Serializer.Int(sm["indexCount"], 0), bv = MiniUnity.Serializer.Int(sm["baseVertex"], 0);
                    var idx = new int[count];
                    for (int k = 0; k < count; k++) idx[k] = (i32 ? BitConverter.ToInt32(ib, first + k * 4) : BitConverter.ToUInt16(ib, first + k * 2)) + bv;
                    m_Sub.Add(idx); m_Topo.Add((MeshTopology)MiniUnity.Serializer.Int(sm["topology"], 0));
                }
                if (m_Sub.Count == 0) { m_Sub.Add(Array.Empty<int>()); m_Topo.Add(MeshTopology.Triangles); }
                RecalculateBounds();
            }
            catch (Exception e) { Debug.LogWarning($"[MiniUnity] mesh '{name}' decode: {e.Message}"); }
        }
        bool m_Decoded;
        static int FmtSize(int f) => f switch { 0 => 4, 1 => 2, 2 or 3 or 6 or 7 => 1, 4 or 5 or 8 or 9 => 2, _ => 4 };
        static byte[] Hex(string s)
        {
            if (string.IsNullOrEmpty(s)) return Array.Empty<byte>();
            var b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return b;
        }

        public int vertexCount { get { EnsureData(); return m_V.Length; } }
        public int subMeshCount
        {
            get => m_Sub.Count;
            set { while (m_Sub.Count < value) { m_Sub.Add(Array.Empty<int>()); m_Topo.Add(MeshTopology.Triangles); } while (m_Sub.Count > Math.Max(1, value)) { m_Sub.RemoveAt(m_Sub.Count - 1); m_Topo.RemoveAt(m_Topo.Count - 1); } }
        }
        public IndexFormat indexFormat { get => m_IndexFormat; set => m_IndexFormat = value; }
        public bool isReadable => true;
        public Bounds bounds { get => m_Bounds; set => m_Bounds = value; }

        static T[] Copy<T>(T[] a) => a == null ? null : (T[])a.Clone();
        static Vector4[] V4(Vector2[] a) => a?.Select(v => new Vector4(v.x, v.y, 0, 0)).ToArray();
        static Vector4[] V4(Vector3[] a) => a?.Select(v => new Vector4(v.x, v.y, v.z, 0)).ToArray();
        static Vector2[] V2(Vector4[] a) => a?.Select(v => new Vector2(v.x, v.y)).ToArray() ?? Array.Empty<Vector2>();
        /// <summary>Copy l[start..start+length) into <paramref name="cur"/> when it has that length (Unity copies mesh data into native
        /// memory, so per-frame SetVertices(...) on a mesh of fixed size allocates no managed memory), else into a new array.</summary>
        static T[] Into<T>(T[] cur, IList<T> l, int start, int length)
        {
            if (start < 0 || length < 0 || start + length > l.Count) throw new ArgumentOutOfRangeException(nameof(start), $"start {start} + length {length} is outside the {l.Count} source elements");
            var r = cur != null && cur.Length == length ? cur : length == 0 ? Array.Empty<T>() : new T[length];
            if (l is T[] a) Array.Copy(a, start, r, 0, length);
            else for (int i = 0; i < length; i++) r[i] = l[start + i];
            return r;
        }
        static T[] Slice<T>(IList<T> l, int start, int length) => Into<T>(null, l, start, length);

        public Vector3[] vertices { get => Copy(m_V); set => m_V = Copy(value) ?? Array.Empty<Vector3>(); }
        public Vector3[] normals { get => Copy(m_N) ?? Array.Empty<Vector3>(); set => m_N = Copy(value); }
        public Vector4[] tangents { get => Copy(m_T) ?? Array.Empty<Vector4>(); set => m_T = Copy(value); }
        public Color[] colors { get => Copy(m_C) ?? Array.Empty<Color>(); set => m_C = Copy(value); }
        public Color32[] colors32 { get => m_C?.Select(c => (Color32)c).ToArray() ?? Array.Empty<Color32>(); set => m_C = value?.Select(c => (Color)c).ToArray(); }
        public Vector2[] uv { get => V2(m_UV[0]); set => m_UV[0] = V4(value); }
        public Vector2[] uv2 { get => V2(m_UV[1]); set => m_UV[1] = V4(value); }
        public Vector2[] uv3 { get => V2(m_UV[2]); set => m_UV[2] = V4(value); }
        public Vector2[] uv4 { get => V2(m_UV[3]); set => m_UV[3] = V4(value); }
        public Vector2[] uv5 { get => V2(m_UV[4]); set => m_UV[4] = V4(value); }
        public Vector2[] uv6 { get => V2(m_UV[5]); set => m_UV[5] = V4(value); }
        public Vector2[] uv7 { get => V2(m_UV[6]); set => m_UV[6] = V4(value); }
        public Vector2[] uv8 { get => V2(m_UV[7]); set => m_UV[7] = V4(value); }
        public int[] triangles
        {
            get => m_Sub.SelectMany(s => s).ToArray();
            set { subMeshCount = 1; m_Sub[0] = Copy(value) ?? Array.Empty<int>(); m_Topo[0] = MeshTopology.Triangles; }
        }

        public void Clear() => Clear(true);
        public void Clear(bool keepVertexLayout)
        {
            m_V = Array.Empty<Vector3>(); m_N = null; m_T = null; m_C = null;
            for (int i = 0; i < 8; i++) m_UV[i] = null;
            m_Sub.Clear(); m_Sub.Add(Array.Empty<int>()); m_Topo.Clear(); m_Topo.Add(MeshTopology.Triangles);
            m_Bounds = default;
        }
        public void MarkDynamic() { }
        public void UploadMeshData(bool markNoLongerReadable) { }
        public void RecalculateBounds() => RecalculateBounds(MeshUpdateFlags.Default);
        public void RecalculateBounds(MeshUpdateFlags flags)
        {
            if (m_V.Length == 0) { m_Bounds = default; return; }
            Vector3 mn = m_V[0], mx = m_V[0];
            foreach (var v in m_V) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
            m_Bounds = new Bounds((mn + mx) * 0.5f, mx - mn);
        }
        public void RecalculateNormals() => RecalculateNormals(MeshUpdateFlags.Default);
        public void RecalculateNormals(MeshUpdateFlags flags)
        {
            var n = new Vector3[m_V.Length];
            foreach (var s in m_Sub)
                for (int i = 0; i + 2 < s.Length; i += 3)
                {
                    var f = Vector3.Cross(m_V[s[i + 1]] - m_V[s[i]], m_V[s[i + 2]] - m_V[s[i]]);
                    n[s[i]] += f; n[s[i + 1]] += f; n[s[i + 2]] += f;
                }
            for (int i = 0; i < n.Length; i++) n[i] = n[i].normalized;
            m_N = n;
        }
        public void RecalculateTangents() { }
        public void RecalculateTangents(MeshUpdateFlags flags) { }

        public void SetVertices(List<Vector3> inVertices) => m_V = Into(m_V, inVertices, 0, inVertices.Count);
        public void SetVertices(List<Vector3> inVertices, int start, int length) => m_V = Into(m_V, inVertices, start, length);
        public void SetVertices(List<Vector3> inVertices, int start, int length, MeshUpdateFlags flags) => SetVertices(inVertices, start, length);
        public void SetVertices(Vector3[] inVertices) => m_V = Into(m_V, inVertices, 0, inVertices.Length);
        public void SetVertices(Vector3[] inVertices, int start, int length) => m_V = Into(m_V, inVertices, start, length);
        public void SetVertices(Vector3[] inVertices, int start, int length, MeshUpdateFlags flags) => SetVertices(inVertices, start, length);
        public void GetVertices(List<Vector3> vertices) { vertices.Clear(); vertices.AddRange(m_V); }

        public void SetNormals(List<Vector3> inNormals) => m_N = Into(m_N, inNormals, 0, inNormals.Count);
        public void SetNormals(List<Vector3> inNormals, int start, int length) => m_N = Into(m_N, inNormals, start, length);
        public void SetNormals(List<Vector3> inNormals, int start, int length, MeshUpdateFlags flags) => SetNormals(inNormals, start, length);
        public void SetNormals(Vector3[] inNormals) => m_N = Into(m_N, inNormals, 0, inNormals.Length);
        public void SetNormals(Vector3[] inNormals, int start, int length) => m_N = Into(m_N, inNormals, start, length);
        public void SetNormals(Vector3[] inNormals, int start, int length, MeshUpdateFlags flags) => SetNormals(inNormals, start, length);
        public void GetNormals(List<Vector3> normals) { normals.Clear(); if (m_N != null) normals.AddRange(m_N); }

        public void SetTangents(List<Vector4> inTangents) => m_T = Into(m_T, inTangents, 0, inTangents.Count);
        public void SetTangents(List<Vector4> inTangents, int start, int length) => m_T = Into(m_T, inTangents, start, length);
        public void SetTangents(List<Vector4> inTangents, int start, int length, MeshUpdateFlags flags) => SetTangents(inTangents, start, length);
        public void SetTangents(Vector4[] inTangents) => m_T = Into(m_T, inTangents, 0, inTangents.Length);
        public void SetTangents(Vector4[] inTangents, int start, int length) => m_T = Into(m_T, inTangents, start, length);
        public void SetTangents(Vector4[] inTangents, int start, int length, MeshUpdateFlags flags) => SetTangents(inTangents, start, length);
        public void GetTangents(List<Vector4> tangents) { tangents.Clear(); if (m_T != null) tangents.AddRange(m_T); }

        public void SetColors(List<Color> inColors) => m_C = Into(m_C, inColors, 0, inColors.Count);
        public void SetColors(List<Color> inColors, int start, int length) => m_C = Into(m_C, inColors, start, length);
        public void SetColors(List<Color> inColors, int start, int length, MeshUpdateFlags flags) => SetColors(inColors, start, length);
        public void SetColors(Color[] inColors) => m_C = Into(m_C, inColors, 0, inColors.Length);
        public void SetColors(Color[] inColors, int start, int length) => m_C = Into(m_C, inColors, start, length);
        public void SetColors(Color[] inColors, int start, int length, MeshUpdateFlags flags) => SetColors(inColors, start, length);
        public void SetColors(List<Color32> inColors) => m_C = inColors.Select(c => (Color)c).ToArray();
        public void SetColors(List<Color32> inColors, int start, int length) => m_C = Slice(inColors, start, length).Select(c => (Color)c).ToArray();
        public void SetColors(List<Color32> inColors, int start, int length, MeshUpdateFlags flags) => SetColors(inColors, start, length);
        public void SetColors(Color32[] inColors) => colors32 = inColors;
        public void SetColors(Color32[] inColors, int start, int length) => m_C = Slice(inColors, start, length).Select(c => (Color)c).ToArray();
        public void SetColors(Color32[] inColors, int start, int length, MeshUpdateFlags flags) => SetColors(inColors, start, length);
        public void GetColors(List<Color> colors) { colors.Clear(); if (m_C != null) colors.AddRange(m_C); }
        public void GetColors(List<Color32> colors) { colors.Clear(); if (m_C != null) colors.AddRange(m_C.Select(c => (Color32)c)); }

        public void SetUVs(int channel, List<Vector2> uvs) => m_UV[channel] = uvs.Select(v => new Vector4(v.x, v.y, 0, 0)).ToArray();
        public void SetUVs(int channel, List<Vector3> uvs) => m_UV[channel] = uvs.Select(v => new Vector4(v.x, v.y, v.z, 0)).ToArray();
        public void SetUVs(int channel, List<Vector4> uvs) => m_UV[channel] = uvs.ToArray();
        public void SetUVs(int channel, List<Vector2> uvs, int start, int length) => m_UV[channel] = Slice(uvs, start, length).Select(v => new Vector4(v.x, v.y, 0, 0)).ToArray();
        public void SetUVs(int channel, List<Vector2> uvs, int start, int length, MeshUpdateFlags flags) => SetUVs(channel, uvs, start, length);
        public void SetUVs(int channel, List<Vector3> uvs, int start, int length) => m_UV[channel] = Slice(uvs, start, length).Select(v => new Vector4(v.x, v.y, v.z, 0)).ToArray();
        public void SetUVs(int channel, List<Vector3> uvs, int start, int length, MeshUpdateFlags flags) => SetUVs(channel, uvs, start, length);
        public void SetUVs(int channel, List<Vector4> uvs, int start, int length) => m_UV[channel] = Into(m_UV[channel], uvs, start, length);
        public void SetUVs(int channel, List<Vector4> uvs, int start, int length, MeshUpdateFlags flags) => SetUVs(channel, uvs, start, length);
        public void SetUVs(int channel, Vector2[] uvs) => m_UV[channel] = V4(uvs);
        public void SetUVs(int channel, Vector3[] uvs) => m_UV[channel] = V4(uvs);
        public void SetUVs(int channel, Vector4[] uvs) => m_UV[channel] = Copy(uvs);
        public void SetUVs(int channel, Vector2[] uvs, int start, int length) => m_UV[channel] = V4(Slice(uvs, start, length));
        public void SetUVs(int channel, Vector2[] uvs, int start, int length, MeshUpdateFlags flags) => SetUVs(channel, uvs, start, length);
        public void SetUVs(int channel, Vector3[] uvs, int start, int length) => m_UV[channel] = V4(Slice(uvs, start, length));
        public void SetUVs(int channel, Vector3[] uvs, int start, int length, MeshUpdateFlags flags) => SetUVs(channel, uvs, start, length);
        public void SetUVs(int channel, Vector4[] uvs, int start, int length) => m_UV[channel] = Into(m_UV[channel], uvs, start, length);
        public void SetUVs(int channel, Vector4[] uvs, int start, int length, MeshUpdateFlags flags) => SetUVs(channel, uvs, start, length);
        public void GetUVs(int channel, List<Vector2> uvs) { uvs.Clear(); if (m_UV[channel] != null) uvs.AddRange(m_UV[channel].Select(v => new Vector2(v.x, v.y))); }
        public void GetUVs(int channel, List<Vector3> uvs) { uvs.Clear(); if (m_UV[channel] != null) uvs.AddRange(m_UV[channel].Select(v => new Vector3(v.x, v.y, v.z))); }
        public void GetUVs(int channel, List<Vector4> uvs) { uvs.Clear(); if (m_UV[channel] != null) uvs.AddRange(m_UV[channel]); }

        void SetSub(int submesh, int[] idx, MeshTopology topo) { if (submesh >= subMeshCount) subMeshCount = submesh + 1; m_Sub[submesh] = idx; m_Topo[submesh] = topo; }
        static int[] Base(int[] a, int b) { if (b != 0) for (int i = 0; i < a.Length; i++) a[i] += b; return a; }
        public void SetTriangles(int[] triangles, int submesh) => SetSub(submesh, Copy(triangles), MeshTopology.Triangles);
        public void SetTriangles(int[] triangles, int submesh, bool calculateBounds) { SetTriangles(triangles, submesh); if (calculateBounds) RecalculateBounds(); }
        public void SetTriangles(int[] triangles, int submesh, bool calculateBounds, int baseVertex) => SetSub(submesh, Base(Copy(triangles), baseVertex), MeshTopology.Triangles);
        public void SetTriangles(int[] triangles, int trianglesStart, int trianglesLength, int submesh, bool calculateBounds = true, int baseVertex = 0) => SetSub(submesh, Base(Slice(triangles, trianglesStart, trianglesLength), baseVertex), MeshTopology.Triangles);
        public void SetTriangles(ushort[] triangles, int submesh, bool calculateBounds = true, int baseVertex = 0) => SetSub(submesh, Base(triangles.Select(x => (int)x).ToArray(), baseVertex), MeshTopology.Triangles);
        public void SetTriangles(ushort[] triangles, int trianglesStart, int trianglesLength, int submesh, bool calculateBounds = true, int baseVertex = 0) => SetSub(submesh, Base(Slice(triangles, trianglesStart, trianglesLength).Select(x => (int)x).ToArray(), baseVertex), MeshTopology.Triangles);
        public void SetTriangles(List<int> triangles, int submesh) => SetSub(submesh, triangles.ToArray(), MeshTopology.Triangles);
        public void SetTriangles(List<int> triangles, int submesh, bool calculateBounds) => SetTriangles(triangles, submesh);
        public void SetTriangles(List<int> triangles, int submesh, bool calculateBounds, int baseVertex) => SetSub(submesh, Base(triangles.ToArray(), baseVertex), MeshTopology.Triangles);
        public void SetTriangles(List<int> triangles, int trianglesStart, int trianglesLength, int submesh, bool calculateBounds = true, int baseVertex = 0) => SetSub(submesh, Base(Slice(triangles, trianglesStart, trianglesLength), baseVertex), MeshTopology.Triangles);
        public void SetTriangles(List<ushort> triangles, int submesh, bool calculateBounds = true, int baseVertex = 0) => SetSub(submesh, Base(triangles.Select(x => (int)x).ToArray(), baseVertex), MeshTopology.Triangles);
        public void SetTriangles(List<ushort> triangles, int trianglesStart, int trianglesLength, int submesh, bool calculateBounds = true, int baseVertex = 0) => SetSub(submesh, Base(Slice(triangles, trianglesStart, trianglesLength).Select(x => (int)x).ToArray(), baseVertex), MeshTopology.Triangles);
        public void SetIndices(int[] indices, MeshTopology topology, int submesh) => SetSub(submesh, Copy(indices), topology);
        public void SetIndices(int[] indices, MeshTopology topology, int submesh, bool calculateBounds) => SetIndices(indices, topology, submesh);
        public void SetIndices(int[] indices, MeshTopology topology, int submesh, bool calculateBounds, int baseVertex) => SetSub(submesh, Base(Copy(indices), baseVertex), topology);
        public void SetIndices(int[] indices, int indicesStart, int indicesLength, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) => SetSub(submesh, Base(Slice(indices, indicesStart, indicesLength), baseVertex), topology);
        public void SetIndices(ushort[] indices, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) => SetSub(submesh, Base(indices.Select(x => (int)x).ToArray(), baseVertex), topology);
        public void SetIndices(ushort[] indices, int indicesStart, int indicesLength, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) => SetSub(submesh, Base(Slice(indices, indicesStart, indicesLength).Select(x => (int)x).ToArray(), baseVertex), topology);
        public void SetIndices(List<int> indices, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) => SetSub(submesh, Base(indices.ToArray(), baseVertex), topology);
        public void SetIndices(List<int> indices, int indicesStart, int indicesLength, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) => SetSub(submesh, Base(Slice(indices, indicesStart, indicesLength), baseVertex), topology);
        public void SetIndices(List<ushort> indices, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) => SetSub(submesh, Base(indices.Select(x => (int)x).ToArray(), baseVertex), topology);
        public void SetIndices(List<ushort> indices, int indicesStart, int indicesLength, MeshTopology topology, int submesh, bool calculateBounds = true, int baseVertex = 0) => SetSub(submesh, Base(Slice(indices, indicesStart, indicesLength).Select(x => (int)x).ToArray(), baseVertex), topology);
        public int[] GetTriangles(int submesh) => Copy(m_Sub[submesh]);
        public int[] GetTriangles(int submesh, bool applyBaseVertex) => GetTriangles(submesh);
        public void GetTriangles(List<int> triangles, int submesh) { triangles.Clear(); triangles.AddRange(m_Sub[submesh]); }
        public void GetTriangles(List<int> triangles, int submesh, bool applyBaseVertex) => GetTriangles(triangles, submesh);
        public void GetTriangles(List<ushort> triangles, int submesh, bool applyBaseVertex = true) { triangles.Clear(); triangles.AddRange(m_Sub[submesh].Select(x => (ushort)x)); }
        public int[] GetIndices(int submesh) => GetTriangles(submesh);
        public int[] GetIndices(int submesh, bool applyBaseVertex) => GetTriangles(submesh);
        public void GetIndices(List<int> indices, int submesh) => GetTriangles(indices, submesh);
        public void GetIndices(List<int> indices, int submesh, bool applyBaseVertex) => GetTriangles(indices, submesh);
        public void GetIndices(List<ushort> indices, int submesh, bool applyBaseVertex = true) => GetTriangles(indices, submesh);
        public uint GetIndexStart(int submesh) { uint s = 0; for (int i = 0; i < submesh; i++) s += (uint)m_Sub[i].Length; return s; }
        public uint GetIndexCount(int submesh) => (uint)m_Sub[submesh].Length;
        public uint GetBaseVertex(int submesh) => 0;
        public MeshTopology GetTopology(int submesh) => m_Topo[submesh];
    }
}
