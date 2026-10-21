// Mesh.MeshData / Mesh.MeshDataArray: the writable (AllocateWritableMeshData → SetVertexBufferParams / GetVertexData →
// ApplyAndDisposeWritableMeshData) and read-only (AcquireReadOnlyMeshData) mesh data API. Vertex streams and the index buffer
// live in unmanaged memory handed out as NativeArray views; applying decodes them into the managed Mesh (attributes laid out
// per stream in VertexAttribute order, as Unity does).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public sealed partial class Mesh
    {
        /// <summary>The buffers behind one MeshData (a GCHandle to it is the struct's m_Ptr).</summary>
        internal sealed class MeshDataBuffers : IDisposable
        {
            public int vertexCount, indexCount;
            public VertexAttributeDescriptor[] attributes = Array.Empty<VertexAttributeDescriptor>();
            public readonly IntPtr[] streams = new IntPtr[4];
            public readonly int[] strides = new int[4];
            public IntPtr indices; public IndexFormat indexFormat = IndexFormat.UInt16;
            public readonly List<SubMeshDescriptor> subMeshes = new() { new SubMeshDescriptor(0, 0) };

            public static int FormatSize(VertexAttributeFormat f) => f switch
            {
                VertexAttributeFormat.Float32 or VertexAttributeFormat.UInt32 or VertexAttributeFormat.SInt32 => 4,
                VertexAttributeFormat.Float16 or VertexAttributeFormat.UNorm16 or VertexAttributeFormat.SNorm16 or VertexAttributeFormat.UInt16 or VertexAttributeFormat.SInt16 => 2,
                _ => 1,
            };
            public int Offset(VertexAttribute a)
            {
                var d = attributes.FirstOrDefault(x => x.attribute == a); int off = 0;
                foreach (var x in attributes.Where(x => x.stream == d.stream).OrderBy(x => (int)x.attribute))
                {
                    if (x.attribute == a) return off;
                    off += x.dimension * FormatSize(x.format);
                }
                return -1;
            }
            public void SetVertexLayout(int count, VertexAttributeDescriptor[] attrs)
            {
                FreeStreams();
                vertexCount = count; attributes = attrs.ToArray();
                Array.Clear(strides);
                foreach (var a in attributes) strides[a.stream] += a.dimension * FormatSize(a.format);
                for (int s = 0; s < 4; s++)
                    if (strides[s] > 0) { streams[s] = Marshal.AllocHGlobal(Math.Max(1, strides[s] * count)); unsafe { UnsafeUtility.MemClear((void*)streams[s], strides[s] * (long)count); } }
            }
            public void SetIndexLayout(int count, IndexFormat format)
            {
                if (indices != IntPtr.Zero) Marshal.FreeHGlobal(indices);
                indexCount = count; indexFormat = format;
                long size = (long)count * (format == IndexFormat.UInt32 ? 4 : 2);
                indices = Marshal.AllocHGlobal((IntPtr)Math.Max(1, size));
                unsafe { UnsafeUtility.MemClear((void*)indices, size); }
            }
            void FreeStreams() { for (int s = 0; s < 4; s++) if (streams[s] != IntPtr.Zero) { Marshal.FreeHGlobal(streams[s]); streams[s] = IntPtr.Zero; } }
            public void Dispose()
            {
                FreeStreams();
                if (indices != IntPtr.Zero) { Marshal.FreeHGlobal(indices); indices = IntPtr.Zero; }
            }

            unsafe float Read(VertexAttributeDescriptor a, int v, int comp)
            {
                var p = (byte*)streams[a.stream] + (long)v * strides[a.stream] + Offset(a.attribute) + comp * FormatSize(a.format);
                return a.format switch
                {
                    VertexAttributeFormat.Float32 => *(float*)p,
                    VertexAttributeFormat.Float16 => (float)*(Half*)p,
                    VertexAttributeFormat.UNorm8 => *p / 255f,
                    VertexAttributeFormat.SNorm8 => Math.Max(-1, *(sbyte*)p / 127f),
                    VertexAttributeFormat.UNorm16 => *(ushort*)p / 65535f,
                    VertexAttributeFormat.SNorm16 => Math.Max(-1, *(short*)p / 32767f),
                    VertexAttributeFormat.UInt8 => *p,
                    VertexAttributeFormat.SInt8 => *(sbyte*)p,
                    VertexAttributeFormat.UInt16 => *(ushort*)p,
                    VertexAttributeFormat.SInt16 => *(short*)p,
                    VertexAttributeFormat.UInt32 => *(uint*)p,
                    _ => *(int*)p,
                };
            }
            unsafe void Write(VertexAttributeDescriptor a, int v, int comp, float value)
            {
                var p = (byte*)streams[a.stream] + (long)v * strides[a.stream] + Offset(a.attribute) + comp * FormatSize(a.format);
                switch (a.format)
                {
                    case VertexAttributeFormat.Float32: *(float*)p = value; break;
                    case VertexAttributeFormat.Float16: *(Half*)p = (Half)value; break;
                    case VertexAttributeFormat.UNorm8: *p = (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255), 0, 255); break;
                    case VertexAttributeFormat.UNorm16: *(ushort*)p = (ushort)Mathf.Clamp(Mathf.RoundToInt(value * 65535), 0, 65535); break;
                    case VertexAttributeFormat.SNorm16: *(short*)p = (short)Mathf.Clamp(Mathf.RoundToInt(value * 32767), -32767, 32767); break;
                    case VertexAttributeFormat.SNorm8: *(sbyte*)p = (sbyte)Mathf.Clamp(Mathf.RoundToInt(value * 127), -127, 127); break;
                    case VertexAttributeFormat.UInt32: *(uint*)p = (uint)value; break;
                    case VertexAttributeFormat.SInt32: *(int*)p = (int)value; break;
                    case VertexAttributeFormat.UInt16: *(ushort*)p = (ushort)value; break;
                    case VertexAttributeFormat.SInt16: *(short*)p = (short)value; break;
                    case VertexAttributeFormat.UInt8: *p = (byte)value; break;
                    default: *(sbyte*)p = (sbyte)value; break;
                }
            }
            unsafe int Index(int i) => indexFormat == IndexFormat.UInt32 ? ((int*)indices)[i] : ((ushort*)indices)[i];

            /// <summary>Decode into a Mesh (replacing its geometry).</summary>
            public void ApplyTo(Mesh mesh, MeshUpdateFlags flags)
            {
                int n = vertexCount;
                Vector4 V4(VertexAttributeDescriptor a, int v) => new(Read(a, v, 0), a.dimension > 1 ? Read(a, v, 1) : 0, a.dimension > 2 ? Read(a, v, 2) : 0, a.dimension > 3 ? Read(a, v, 3) : (a.attribute == VertexAttribute.Color ? 1 : 0));
                mesh.m_V = Array.Empty<Vector3>(); mesh.m_N = null; mesh.m_T = null; mesh.m_C = null;
                for (int k = 0; k < 8; k++) mesh.m_UV[k] = null;
                foreach (var a in attributes)
                {
                    switch (a.attribute)
                    {
                        case VertexAttribute.Position: mesh.m_V = Enumerable.Range(0, n).Select(v => (Vector3)V4(a, v)).ToArray(); break;
                        case VertexAttribute.Normal: mesh.m_N = Enumerable.Range(0, n).Select(v => (Vector3)V4(a, v)).ToArray(); break;
                        case VertexAttribute.Tangent: mesh.m_T = Enumerable.Range(0, n).Select(v => V4(a, v)).ToArray(); break;
                        case VertexAttribute.Color: mesh.m_C = Enumerable.Range(0, n).Select(v => { var c = V4(a, v); return new Color(c.x, c.y, c.z, c.w); }).ToArray(); break;
                        case >= VertexAttribute.TexCoord0 and <= VertexAttribute.TexCoord7: mesh.m_UV[a.attribute - VertexAttribute.TexCoord0] = Enumerable.Range(0, n).Select(v => V4(a, v)).ToArray(); break;
                    }
                }
                if (mesh.m_V.Length != n) mesh.m_V = new Vector3[n];
                mesh.m_IndexFormat = indexFormat;
                mesh.m_Sub.Clear(); mesh.m_Topo.Clear();
                foreach (var sm in subMeshes)
                {
                    var idx = new int[Math.Max(0, Math.Min(sm.indexCount, indexCount - sm.indexStart))];
                    for (int i = 0; i < idx.Length; i++) idx[i] = Index(sm.indexStart + i) + sm.baseVertex;
                    mesh.m_Sub.Add(idx); mesh.m_Topo.Add(sm.topology);
                }
                if (mesh.m_Sub.Count == 0) { mesh.m_Sub.Add(Array.Empty<int>()); mesh.m_Topo.Add(MeshTopology.Triangles); }
                if ((flags & MeshUpdateFlags.DontRecalculateBounds) == 0) mesh.RecalculateBounds();
            }

            /// <summary>Encode a Mesh (read-only mesh data): float32 streams in VertexAttribute order, 32-bit indices when needed.</summary>
            public void ReadFrom(Mesh mesh)
            {
                mesh.EnsureData();
                int n = mesh.m_V.Length;
                var attrs = new List<VertexAttributeDescriptor> { new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3) };
                if (mesh.m_N != null) attrs.Add(new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3));
                if (mesh.m_T != null) attrs.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4));
                if (mesh.m_C != null) attrs.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4));
                for (int k = 0; k < 8; k++) if (mesh.m_UV[k] != null) attrs.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0 + k, VertexAttributeFormat.Float32, 2));
                SetVertexLayout(n, attrs.ToArray());
                foreach (var a in attributes)
                    for (int v = 0; v < n; v++)
                    {
                        Vector4 x = a.attribute switch
                        {
                            VertexAttribute.Position => mesh.m_V[v], VertexAttribute.Normal => mesh.m_N[v], VertexAttribute.Tangent => mesh.m_T[v],
                            VertexAttribute.Color => (Vector4)mesh.m_C[v], _ => mesh.m_UV[a.attribute - VertexAttribute.TexCoord0][v],
                        };
                        for (int c = 0; c < a.dimension; c++) Write(a, v, c, x[c]);
                    }
                int total = mesh.m_Sub.Sum(s => s.Length);
                SetIndexLayout(total, n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16);
                subMeshes.Clear(); int at = 0;
                for (int s = 0; s < mesh.m_Sub.Count; s++)
                {
                    var sub = mesh.m_Sub[s];
                    unsafe { for (int i = 0; i < sub.Length; i++) { if (indexFormat == IndexFormat.UInt32) ((int*)indices)[at + i] = sub[i]; else ((ushort*)indices)[at + i] = (ushort)sub[i]; } }
                    subMeshes.Add(new SubMeshDescriptor(at, sub.Length, s < mesh.m_Topo.Count ? mesh.m_Topo[s] : MeshTopology.Triangles));
                    at += sub.Length;
                }
            }
        }

        internal static unsafe NativeArray<T> View<T>(IntPtr ptr, long bytes) where T : struct
        {
            var a = default(NativeArray<T>);
            a.m_Buffer = (void*)ptr; a.m_Length = ptr == IntPtr.Zero ? 0 : (int)(bytes / UnsafeUtility.SizeOf<T>());
            a.m_MinIndex = 0; a.m_MaxIndex = a.m_Length - 1; a.m_AllocatorLabel = Allocator.None;
            return a;
        }

        public partial struct MeshData
        {
            internal IntPtr m_Ptr;
            internal MeshDataBuffers B => m_Ptr == IntPtr.Zero ? throw new InvalidOperationException("MeshData is not valid (disposed or default)") : (MeshDataBuffers)GCHandle.FromIntPtr(m_Ptr).Target;

            public int vertexCount => B.vertexCount;
            public int vertexBufferCount => B.strides.Count(s => s > 0);
            public int GetVertexBufferStride(int stream) => stream >= 0 && stream < 4 ? B.strides[stream] : 0;
            public bool HasVertexAttribute(VertexAttribute attr) => B.attributes.Any(a => a.attribute == attr);
            public int GetVertexAttributeDimension(VertexAttribute attr) => B.attributes.FirstOrDefault(a => a.attribute == attr).dimension;
            public VertexAttributeFormat GetVertexAttributeFormat(VertexAttribute attr) => B.attributes.FirstOrDefault(a => a.attribute == attr).format;
            public int GetVertexAttributeStream(VertexAttribute attr) => HasVertexAttribute(attr) ? B.attributes.First(a => a.attribute == attr).stream : -1;
            public int GetVertexAttributeOffset(VertexAttribute attr) => HasVertexAttribute(attr) ? B.Offset(attr) : -1;
            public void SetVertexBufferParams(int vertexCount, params VertexAttributeDescriptor[] attributes) => B.SetVertexLayout(vertexCount, attributes);
            public void SetVertexBufferParams(int vertexCount, NativeArray<VertexAttributeDescriptor> attributes) => B.SetVertexLayout(vertexCount, attributes.ToArray());
            public NativeArray<T> GetVertexData<T>(int stream = 0) where T : struct
            {
                var b = B;
                if (stream < 0 || stream > 3 || b.streams[stream] == IntPtr.Zero) throw new ArgumentOutOfRangeException(nameof(stream), $"Mesh data stream {stream} does not exist");
                return View<T>(b.streams[stream], (long)b.strides[stream] * b.vertexCount);
            }
            public void SetIndexBufferParams(int indexCount, IndexFormat format) => B.SetIndexLayout(indexCount, format);
            public IndexFormat indexFormat => B.indexFormat;
            public NativeArray<T> GetIndexData<T>() where T : struct => View<T>(B.indices, (long)B.indexCount * (B.indexFormat == IndexFormat.UInt32 ? 4 : 2));
            public int subMeshCount
            {
                get => B.subMeshes.Count;
                set { var l = B.subMeshes; while (l.Count > value) l.RemoveAt(l.Count - 1); while (l.Count < value) l.Add(new SubMeshDescriptor(0, 0)); }
            }
            public SubMeshDescriptor GetSubMesh(int index) => B.subMeshes[index];
            public void SetSubMesh(int index, SubMeshDescriptor desc, MeshUpdateFlags flags = MeshUpdateFlags.Default) => B.subMeshes[index] = desc;

            void Copy<T>(VertexAttribute attr, NativeArray<T> dst, Func<Vector4, T> conv) where T : struct
            {
                var b = B; var a = b.attributes.FirstOrDefault(x => x.attribute == attr);
                var tmp = new Mesh(); b.ApplyTo(tmp, MeshUpdateFlags.DontRecalculateBounds);
                Vector4[] src = attr switch
                {
                    VertexAttribute.Position => tmp.m_V.Select(v => (Vector4)v).ToArray(),
                    VertexAttribute.Normal => tmp.m_N?.Select(v => (Vector4)v).ToArray(),
                    VertexAttribute.Tangent => tmp.m_T,
                    VertexAttribute.Color => tmp.m_C?.Select(c => (Vector4)c).ToArray(),
                    _ => tmp.m_UV[attr - VertexAttribute.TexCoord0],
                } ?? Array.Empty<Vector4>();
                for (int i = 0; i < Math.Min(dst.Length, src.Length); i++) dst[i] = conv(src[i]);
            }
            public void GetVertices(NativeArray<Vector3> outVertices) => Copy(VertexAttribute.Position, outVertices, v => v);
            public void GetNormals(NativeArray<Vector3> outNormals) => Copy(VertexAttribute.Normal, outNormals, v => v);
            public void GetTangents(NativeArray<Vector4> outTangents) => Copy(VertexAttribute.Tangent, outTangents, v => v);
            public void GetColors(NativeArray<Color> outColors) => Copy(VertexAttribute.Color, outColors, v => new Color(v.x, v.y, v.z, v.w));
            public void GetColors(NativeArray<Color32> outColors) => Copy(VertexAttribute.Color, outColors, v => (Color32)new Color(v.x, v.y, v.z, v.w));
            public void GetUVs(int channel, NativeArray<Vector2> outUVs) => Copy(VertexAttribute.TexCoord0 + channel, outUVs, v => v);
            public void GetUVs(int channel, NativeArray<Vector3> outUVs) => Copy(VertexAttribute.TexCoord0 + channel, outUVs, v => v);
            public void GetUVs(int channel, NativeArray<Vector4> outUVs) => Copy(VertexAttribute.TexCoord0 + channel, outUVs, v => v);
            public void GetIndices(NativeArray<ushort> outIndices, int submesh, bool applyBaseVertex = true)
            {
                var b = B; var sm = b.subMeshes[submesh]; var src = GetIndexData<ushort>();
                for (int i = 0; i < Math.Min(sm.indexCount, outIndices.Length); i++) outIndices[i] = (ushort)((b.indexFormat == IndexFormat.UInt32 ? View<int>(b.indices, (long)b.indexCount * 4)[sm.indexStart + i] : src[sm.indexStart + i]) + (applyBaseVertex ? sm.baseVertex : 0));
            }
            public void GetIndices(NativeArray<int> outIndices, int submesh, bool applyBaseVertex = true)
            {
                var b = B; var sm = b.subMeshes[submesh];
                for (int i = 0; i < Math.Min(sm.indexCount, outIndices.Length); i++)
                    outIndices[i] = (b.indexFormat == IndexFormat.UInt32 ? View<int>(b.indices, (long)b.indexCount * 4)[sm.indexStart + i] : View<ushort>(b.indices, (long)b.indexCount * 2)[sm.indexStart + i]) + (applyBaseVertex ? sm.baseVertex : 0);
            }
        }

        public partial struct MeshDataArray : IDisposable
        {
            internal IntPtr[] m_Handles;
            internal MeshDataArray(int meshesCount)
            {
                m_Handles = new IntPtr[meshesCount];
                for (int i = 0; i < meshesCount; i++) m_Handles[i] = GCHandle.ToIntPtr(GCHandle.Alloc(new MeshDataBuffers()));
            }
            internal MeshDataArray(Mesh mesh, bool checkReadWrite = true) : this(new[] { mesh }, 1, checkReadWrite) { }
            internal MeshDataArray(Mesh[] meshes, int meshesCount, bool checkReadWrite = true) : this(meshesCount)
            {
                for (int i = 0; i < meshesCount; i++) ((MeshDataBuffers)GCHandle.FromIntPtr(m_Handles[i]).Target).ReadFrom(meshes[i]);
            }
            public int Length => m_Handles?.Length ?? 0;
            public MeshData this[int index] => new MeshData { m_Ptr = m_Handles[index] };
            public void Dispose()
            {
                if (m_Handles == null) return;
                foreach (var h in m_Handles) { if (h == IntPtr.Zero) continue; var gh = GCHandle.FromIntPtr(h); ((MeshDataBuffers)gh.Target).Dispose(); gh.Free(); }
                Array.Clear(m_Handles);
            }
            internal void ApplyToMeshAndDispose(Mesh mesh, MeshUpdateFlags flags) => ApplyToMeshesAndDispose(new[] { mesh }, flags);
            internal void ApplyToMeshesAndDispose(Mesh[] meshes, MeshUpdateFlags flags)
            {
                for (int i = 0; i < Math.Min(meshes.Length, Length); i++)
                    if (meshes[i] != null) ((MeshDataBuffers)GCHandle.FromIntPtr(m_Handles[i]).Target).ApplyTo(meshes[i], flags);
                Dispose();
            }
        }

        public static MeshDataArray AllocateWritableMeshData(int meshCount) => new MeshDataArray(meshCount);
        public static MeshDataArray AllocateWritableMeshData(Mesh mesh) => new MeshDataArray(mesh);
        public static MeshDataArray AllocateWritableMeshData(Mesh[] meshes) => new MeshDataArray(meshes, meshes.Length);
        public static MeshDataArray AllocateWritableMeshData(List<Mesh> meshes) => new MeshDataArray(meshes.ToArray(), meshes.Count);
        public static MeshDataArray AcquireReadOnlyMeshData(Mesh mesh) => new MeshDataArray(mesh);
        public static MeshDataArray AcquireReadOnlyMeshData(Mesh[] meshes) => new MeshDataArray(meshes, meshes.Length);
        public static MeshDataArray AcquireReadOnlyMeshData(List<Mesh> meshes) => new MeshDataArray(meshes.ToArray(), meshes.Count);
        public static void ApplyAndDisposeWritableMeshData(MeshDataArray data, Mesh mesh, MeshUpdateFlags flags = MeshUpdateFlags.Default) => data.ApplyToMeshAndDispose(mesh, flags);
        public static void ApplyAndDisposeWritableMeshData(MeshDataArray data, Mesh[] meshes, MeshUpdateFlags flags = MeshUpdateFlags.Default) => data.ApplyToMeshesAndDispose(meshes, flags);
        public static void ApplyAndDisposeWritableMeshData(MeshDataArray data, List<Mesh> meshes, MeshUpdateFlags flags = MeshUpdateFlags.Default) => data.ApplyToMeshesAndDispose(meshes.ToArray(), flags);
    }
}
