// MaterialPropertyBlock: per-renderer (or per-draw) material property overrides, and Renderer.Set/GetPropertyBlock.
// Values are kept by property id. Floats and ints share one slot, colours and vectors share one slot (as in Unity: SetColor then
// GetVector reads it); arrays keep the length of their first assignment (longer arrays are capped, shorter ones overwrite the
// start). WorldRenderer reads a renderer's block over its material (_Color, _Glossiness, _Metallic, _EmissionColor, …).
using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public sealed partial class MaterialPropertyBlock
    {
        internal enum Kind { Float, Integer, Vector, Matrix, Texture, FloatArray, VectorArray, MatrixArray, Buffer, ConstantBuffer }
        internal readonly Dictionary<int, (Kind kind, object value)> m_Values = new();

        public bool isEmpty => m_Values.Count == 0;
        public void Clear() => m_Values.Clear();
        internal void CopyFrom(MaterialPropertyBlock src) { m_Values.Clear(); if (src != null) foreach (var kv in src.m_Values) m_Values[kv.Key] = (kv.Value.kind, CloneValue(kv.Value.value)); }
        static object CloneValue(object v) => v is Array a ? a.Clone() : v;

        static int Id(string name) => Shader.PropertyToID(name);
        void Put(int id, Kind k, object v) => m_Values[id] = (k, v);
        bool TryGet(int id, Kind k, out object v)
        {
            if (m_Values.TryGetValue(id, out var e) && e.kind == k) { v = e.value; return true; }
            v = null; return false;
        }

        // ---------------------------------------------------------------- scalars
        public void SetFloat(string name, float value) => SetFloat(Id(name), value);
        public void SetFloat(int nameID, float value) => Put(nameID, Kind.Float, value);
        public void SetInt(string name, int value) => SetFloat(Id(name), value);
        public void SetInt(int nameID, int value) => SetFloat(nameID, value);
        public void SetInteger(string name, int value) => SetInteger(Id(name), value);
        public void SetInteger(int nameID, int value) => Put(nameID, Kind.Integer, value);
        public void SetVector(string name, Vector4 value) => SetVector(Id(name), value);
        public void SetVector(int nameID, Vector4 value) => Put(nameID, Kind.Vector, value);
        public void SetColor(string name, Color value) => SetColor(Id(name), value);
        // colours are sRGB values: in a linear-space project the block holds the linear value the shader reads (as Unity does)
        public void SetColor(int nameID, Color value) { var c = Linear ? value.linear : value; Put(nameID, Kind.Vector, new Vector4(c.r, c.g, c.b, c.a)); }
        static bool Linear => QualitySettings.activeColorSpace == ColorSpace.Linear;
        public void SetMatrix(string name, Matrix4x4 value) => SetMatrix(Id(name), value);
        public void SetMatrix(int nameID, Matrix4x4 value) => Put(nameID, Kind.Matrix, value);
        public void SetTexture(string name, Texture value) => SetTexture(Id(name), value);
        public void SetTexture(int nameID, Texture value) => Put(nameID, Kind.Texture, value);
        public void SetTexture(string name, RenderTexture value, RenderTextureSubElement element) => SetTexture(Id(name), value);
        public void SetTexture(int nameID, RenderTexture value, RenderTextureSubElement element) => SetTexture(nameID, value);
        public void SetBuffer(string name, ComputeBuffer value) => Put(Id(name), Kind.Buffer, value);
        public void SetBuffer(int nameID, ComputeBuffer value) => Put(nameID, Kind.Buffer, value);
        public void SetBuffer(string name, GraphicsBuffer value) => Put(Id(name), Kind.Buffer, value);
        public void SetBuffer(int nameID, GraphicsBuffer value) => Put(nameID, Kind.Buffer, value);
        public void SetConstantBuffer(string name, ComputeBuffer value, int offset, int size) => Put(Id(name), Kind.ConstantBuffer, value);
        public void SetConstantBuffer(int nameID, ComputeBuffer value, int offset, int size) => Put(nameID, Kind.ConstantBuffer, value);
        public void SetConstantBuffer(string name, GraphicsBuffer value, int offset, int size) => Put(Id(name), Kind.ConstantBuffer, value);
        public void SetConstantBuffer(int nameID, GraphicsBuffer value, int offset, int size) => Put(nameID, Kind.ConstantBuffer, value);

        // obsolete Add* (same as Set*)
        public void AddFloat(string name, float value) => SetFloat(name, value);
        public void AddFloat(int nameID, float value) => SetFloat(nameID, value);
        public void AddVector(string name, Vector4 value) => SetVector(name, value);
        public void AddVector(int nameID, Vector4 value) => SetVector(nameID, value);
        public void AddColor(string name, Color value) => SetColor(name, value);
        public void AddColor(int nameID, Color value) => SetColor(nameID, value);
        public void AddMatrix(string name, Matrix4x4 value) => SetMatrix(name, value);
        public void AddMatrix(int nameID, Matrix4x4 value) => SetMatrix(nameID, value);
        public void AddTexture(string name, Texture value) => SetTexture(name, value);
        public void AddTexture(int nameID, Texture value) => SetTexture(nameID, value);

        public float GetFloat(string name) => GetFloat(Id(name));
        public float GetFloat(int nameID) => TryGet(nameID, Kind.Float, out var v) ? (float)v : TryGet(nameID, Kind.Integer, out var i) ? (int)i : 0f;
        public int GetInt(string name) => (int)GetFloat(name);
        public int GetInt(int nameID) => (int)GetFloat(nameID);
        public int GetInteger(string name) => GetInteger(Id(name));
        public int GetInteger(int nameID) => TryGet(nameID, Kind.Integer, out var v) ? (int)v : TryGet(nameID, Kind.Float, out var f) ? (int)(float)f : 0;
        public Vector4 GetVector(string name) => GetVector(Id(name));
        public Vector4 GetVector(int nameID) => TryGet(nameID, Kind.Vector, out var v) ? (Vector4)v : Vector4.zero;
        public Color GetColor(string name) => GetColor(Id(name));
        public Color GetColor(int nameID) { var v = GetVector(nameID); var c = new Color(v.x, v.y, v.z, v.w); return Linear ? c.gamma : c; }
        public Matrix4x4 GetMatrix(string name) => GetMatrix(Id(name));
        public Matrix4x4 GetMatrix(int nameID) => TryGet(nameID, Kind.Matrix, out var v) ? (Matrix4x4)v : Matrix4x4.zero;
        public Texture GetTexture(string name) => GetTexture(Id(name));
        public Texture GetTexture(int nameID) => TryGet(nameID, Kind.Texture, out var v) ? (Texture)v : null;

        public bool HasProperty(string name) => HasProperty(Id(name));
        public bool HasProperty(int nameID) => m_Values.ContainsKey(nameID);
        public bool HasFloat(string name) => HasFloat(Id(name));
        public bool HasFloat(int nameID) => m_Values.TryGetValue(nameID, out var e) && e.kind == Kind.Float;
        public bool HasInt(string name) => HasFloat(name);
        public bool HasInt(int nameID) => HasFloat(nameID);
        public bool HasInteger(string name) => HasInteger(Id(name));
        public bool HasInteger(int nameID) => m_Values.TryGetValue(nameID, out var e) && e.kind == Kind.Integer;
        public bool HasVector(string name) => HasVector(Id(name));
        public bool HasVector(int nameID) => m_Values.TryGetValue(nameID, out var e) && e.kind == Kind.Vector;
        public bool HasColor(string name) => HasVector(name);
        public bool HasColor(int nameID) => HasVector(nameID);
        public bool HasMatrix(string name) => HasMatrix(Id(name));
        public bool HasMatrix(int nameID) => m_Values.TryGetValue(nameID, out var e) && e.kind == Kind.Matrix;
        public bool HasTexture(string name) => HasTexture(Id(name));
        public bool HasTexture(int nameID) => m_Values.TryGetValue(nameID, out var e) && e.kind == Kind.Texture;
        public bool HasBuffer(string name) => HasBuffer(Id(name));
        public bool HasBuffer(int nameID) => m_Values.TryGetValue(nameID, out var e) && e.kind == Kind.Buffer;
        public bool HasConstantBuffer(string name) => HasConstantBuffer(Id(name));
        public bool HasConstantBuffer(int nameID) => m_Values.TryGetValue(nameID, out var e) && e.kind == Kind.ConstantBuffer;

        // ---------------------------------------------------------------- arrays
        void PutArray<T>(int id, Kind k, IList<T> values)
        {
            if (values == null) throw new ArgumentNullException("values");
            if (values.Count == 0) throw new ArgumentException("Zero-sized array is not allowed.");
            if (m_Values.TryGetValue(id, out var e) && e.kind == k && e.value is T[] existing)
            {
                int n = Math.Min(existing.Length, values.Count);
                if (values.Count > existing.Length)
                    Debug.LogError($"Property ({Shader.IdToName(id)}) exceeds previous array size ({values.Count} vs {existing.Length}). Cap to previous size. Restart Unity to recreate the arrays.");
                for (int i = 0; i < n; i++) existing[i] = values[i];
                return;
            }
            var a = new T[values.Count];
            for (int i = 0; i < a.Length; i++) a[i] = values[i];
            Put(id, k, a);
        }
        T[] GetArray<T>(int id, Kind k) => TryGet(id, k, out var v) ? (T[])((T[])v).Clone() : null;
        void GetArrayInto<T>(int id, Kind k, List<T> values)
        {
            if (values == null) throw new ArgumentNullException("values");
            values.Clear();
            if (TryGet(id, k, out var v)) values.AddRange((T[])v);
        }

        public void SetFloatArray(string name, List<float> values) => PutArray(Id(name), Kind.FloatArray, values);
        public void SetFloatArray(int nameID, List<float> values) => PutArray(nameID, Kind.FloatArray, values);
        public void SetFloatArray(string name, float[] values) => PutArray(Id(name), Kind.FloatArray, values);
        public void SetFloatArray(int nameID, float[] values) => PutArray(nameID, Kind.FloatArray, values);
        public void SetVectorArray(string name, List<Vector4> values) => PutArray(Id(name), Kind.VectorArray, values);
        public void SetVectorArray(int nameID, List<Vector4> values) => PutArray(nameID, Kind.VectorArray, values);
        public void SetVectorArray(string name, Vector4[] values) => PutArray(Id(name), Kind.VectorArray, values);
        public void SetVectorArray(int nameID, Vector4[] values) => PutArray(nameID, Kind.VectorArray, values);
        public void SetMatrixArray(string name, List<Matrix4x4> values) => PutArray(Id(name), Kind.MatrixArray, values);
        public void SetMatrixArray(int nameID, List<Matrix4x4> values) => PutArray(nameID, Kind.MatrixArray, values);
        public void SetMatrixArray(string name, Matrix4x4[] values) => PutArray(Id(name), Kind.MatrixArray, values);
        public void SetMatrixArray(int nameID, Matrix4x4[] values) => PutArray(nameID, Kind.MatrixArray, values);

        public float[] GetFloatArray(string name) => GetArray<float>(Id(name), Kind.FloatArray);
        public float[] GetFloatArray(int nameID) => GetArray<float>(nameID, Kind.FloatArray);
        public void GetFloatArray(string name, List<float> values) => GetArrayInto(Id(name), Kind.FloatArray, values);
        public void GetFloatArray(int nameID, List<float> values) => GetArrayInto(nameID, Kind.FloatArray, values);
        public Vector4[] GetVectorArray(string name) => GetArray<Vector4>(Id(name), Kind.VectorArray);
        public Vector4[] GetVectorArray(int nameID) => GetArray<Vector4>(nameID, Kind.VectorArray);
        public void GetVectorArray(string name, List<Vector4> values) => GetArrayInto(Id(name), Kind.VectorArray, values);
        public void GetVectorArray(int nameID, List<Vector4> values) => GetArrayInto(nameID, Kind.VectorArray, values);
        public Matrix4x4[] GetMatrixArray(string name) => GetArray<Matrix4x4>(Id(name), Kind.MatrixArray);
        public Matrix4x4[] GetMatrixArray(int nameID) => GetArray<Matrix4x4>(nameID, Kind.MatrixArray);
        public void GetMatrixArray(string name, List<Matrix4x4> values) => GetArrayInto(Id(name), Kind.MatrixArray, values);
        public void GetMatrixArray(int nameID, List<Matrix4x4> values) => GetArrayInto(nameID, Kind.MatrixArray, values);

        // ---------------------------------------------------------------- what the renderer reads
        /// <summary>The value as the shader sees it (linear in a linear-space project).</summary>
        internal bool TryGetShaderColor(string name, out Color c)
        {
            if (TryGet(Id(name), Kind.Vector, out var v)) { var x = (Vector4)v; c = new Color(x.x, x.y, x.z, x.w); return true; }
            c = default; return false;
        }
        /// <summary>Element <paramref name="instance"/> of a vector array (instanced draws: UNITY_ACCESS_INSTANCED_PROP).</summary>
        internal bool TryGetInstanceVector(string name, int instance, out Vector4 v)
        {
            if (TryGet(Id(name), Kind.VectorArray, out var a) && a is Vector4[] arr && instance >= 0 && instance < arr.Length) { v = arr[instance]; return true; }
            v = default; return false;
        }
        internal bool TryGetInstanceFloat(string name, int instance, out float f)
        {
            if (TryGet(Id(name), Kind.FloatArray, out var a) && a is float[] arr && instance >= 0 && instance < arr.Length) { f = arr[instance]; return true; }
            f = 0; return false;
        }
        internal bool HasArrays { get { foreach (var e in m_Values.Values) if (e.kind is Kind.FloatArray or Kind.VectorArray or Kind.MatrixArray) return true; return false; } }

        internal bool TryGetFloat(string name, out float f)
        {
            int id = Id(name);
            if (TryGet(id, Kind.Float, out var v)) { f = (float)v; return true; }
            if (TryGet(id, Kind.Integer, out var i)) { f = (int)i; return true; }
            f = 0; return false;
        }
    }

    public partial class Renderer
    {
        // Unity copies the block into the renderer: later changes to the caller's block do not apply until set again
        MaterialPropertyBlock m_Block;
        Dictionary<int, MaterialPropertyBlock> m_BlockPerMaterial;

        public bool HasPropertyBlock() => m_Block != null && !m_Block.isEmpty || m_BlockPerMaterial != null && m_BlockPerMaterial.Count > 0;
        public void SetPropertyBlock(MaterialPropertyBlock properties)
        {
            if (properties == null) { m_Block = null; return; }
            (m_Block ??= new MaterialPropertyBlock()).CopyFrom(properties);
        }
        public void SetPropertyBlock(MaterialPropertyBlock properties, int materialIndex)
        {
            if (properties == null) { m_BlockPerMaterial?.Remove(materialIndex); return; }
            m_BlockPerMaterial ??= new();
            if (!m_BlockPerMaterial.TryGetValue(materialIndex, out var b)) m_BlockPerMaterial[materialIndex] = b = new MaterialPropertyBlock();
            b.CopyFrom(properties);
        }
        public void GetPropertyBlock(MaterialPropertyBlock properties)
        {
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            properties.CopyFrom(m_Block);
        }
        public void GetPropertyBlock(MaterialPropertyBlock properties, int materialIndex)
        {
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            properties.CopyFrom(m_BlockPerMaterial != null && m_BlockPerMaterial.TryGetValue(materialIndex, out var b) ? b : null);
        }

        /// <summary>The block that applies to a material slot: the per-material one if set, otherwise the renderer's (Unity).</summary>
        internal MaterialPropertyBlock PropertyBlockFor(int materialIndex)
        {
            if (m_BlockPerMaterial != null && m_BlockPerMaterial.TryGetValue(materialIndex, out var b)) return b;
            return m_Block != null && !m_Block.isEmpty ? m_Block : null;
        }
    }
}
