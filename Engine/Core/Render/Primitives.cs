// Built-in meshes (Library/unity default resources: Cube 10202, Sphere 10207, Capsule 10208, Cylinder 10206,
// Plane 10209, Quad 10210) with Unity's dimensions, and the Default-Material (unity_builtin_extra 10303).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniUnity
{
    public static partial class BuiltinResources
    {
        static readonly Dictionary<PrimitiveType, Mesh> s_Prims = new();
        static Material s_DefaultMat;

        public static Material DefaultMaterial
        {
            get
            {
                if (s_DefaultMat != null) return s_DefaultMat;
                s_DefaultMat = new Material(ShaderIndex.Find("Standard")) { name = "Default-Material" };
                s_DefaultMat.SetColor("_Color", Color.white);
                s_DefaultMat.SetFloat("_Glossiness", 0.5f);
                s_DefaultMat.m_Asset = new AssetIdentity { Guid = "0000000000000000f000000000000000", FileID = 10303, RefType = 0, Path = "Resources/unity_builtin_extra" };
                return s_DefaultMat;
            }
        }

        public static Mesh PrimitiveMesh(PrimitiveType type, long fileId)
        {
            if (s_Prims.TryGetValue(type, out var m)) return m;
            m = type switch
            {
                PrimitiveType.Cube => Cube(),
                PrimitiveType.Sphere => Sphere(0.5f, 24, 16),
                PrimitiveType.Capsule => Capsule(),
                PrimitiveType.Cylinder => Cylinder(),
                PrimitiveType.Plane => Plane(10, 10),
                _ => Plane(1, 1, quad: true),
            };
            m.name = type.ToString();
            m.m_Asset = new AssetIdentity { Guid = "0000000000000000e000000000000000", FileID = fileId, RefType = 0, Path = "Library/unity default resources" };
            m.RecalculateBounds();
            return s_Prims[type] = m;
        }

        static Mesh Build(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)
        {
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            return m;
        }

        static Mesh Cube()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            void Face(Vector3 nrm, Vector3 u, Vector3 w)
            {
                int s = v.Count;
                foreach (var (a, b) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                { v.Add(nrm * .5f + u * (a * .5f) + w * (b * .5f)); n.Add(nrm); uv.Add(new Vector2((a + 1) / 2f, (b + 1) / 2f)); }
                t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }
            Face(Vector3.forward, Vector3.left, Vector3.up); Face(Vector3.back, Vector3.right, Vector3.up);
            Face(Vector3.up, Vector3.right, Vector3.forward); Face(Vector3.down, Vector3.right, Vector3.back);
            Face(Vector3.right, Vector3.forward, Vector3.up); Face(Vector3.left, Vector3.back, Vector3.up);
            return Fix(Build(v, n, uv, t));
        }

        /// <summary>Unity front faces: clockwise seen from outside, i.e. cross(v1-v0, v2-v0) along the normal.</summary>
        static Mesh Fix(Mesh m)
        {
            var idx = m.m_Sub[0]; var V = m.m_V; var N = m.m_N;
            for (int i = 0; i + 2 < idx.Length; i += 3)
            {
                var f = Vector3.Cross(V[idx[i + 1]] - V[idx[i]], V[idx[i + 2]] - V[idx[i]]);
                var nn = N[idx[i]] + N[idx[i + 1]] + N[idx[i + 2]];
                if (Vector3.Dot(f, nn) < 0) (idx[i + 1], idx[i + 2]) = (idx[i + 2], idx[i + 1]);
            }
            return m;
        }

        static Mesh Sphere(float r, int seg, int rings, float yOffTop = 0, float yOffBot = 0)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= rings; j++)
            {
                float th = Mathf.PI * j / rings, y = Mathf.Cos(th), s = Mathf.Sin(th);
                for (int i = 0; i <= seg; i++)
                {
                    float ph = 2 * Mathf.PI * i / seg;
                    var d = new Vector3(s * Mathf.Cos(ph), y, s * Mathf.Sin(ph));
                    var p = d * r; p.y += y >= 0 ? yOffTop : yOffBot;
                    v.Add(p); n.Add(d); uv.Add(new Vector2(i / (float)seg, 1 - j / (float)rings));
                }
            }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = j * (seg + 1) + i, b = a + seg + 1;
                    t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            return Fix(Build(v, n, uv, t));
        }

        static Mesh Capsule() => Sphere(0.5f, 24, 16, 0.5f, -0.5f);

        static Mesh Cylinder()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            const int seg = 24;
            for (int i = 0; i <= seg; i++)
            {
                float ph = 2 * Mathf.PI * i / seg; var d = new Vector3(Mathf.Cos(ph), 0, Mathf.Sin(ph));
                v.Add(d * .5f + Vector3.up); n.Add(d); uv.Add(new Vector2(i / (float)seg, 1));
                v.Add(d * .5f + Vector3.down); n.Add(d); uv.Add(new Vector2(i / (float)seg, 0));
            }
            for (int i = 0; i < seg; i++) { int a = i * 2; t.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 }); }
            foreach (var y in new[] { 1f, -1f })
            {
                int c = v.Count; v.Add(new Vector3(0, y, 0)); n.Add(new Vector3(0, y, 0)); uv.Add(new Vector2(.5f, .5f));
                for (int i = 0; i <= seg; i++)
                {
                    float ph = 2 * Mathf.PI * i / seg; var d = new Vector3(Mathf.Cos(ph), 0, Mathf.Sin(ph));
                    v.Add(d * .5f + new Vector3(0, y, 0)); n.Add(new Vector3(0, y, 0)); uv.Add(new Vector2(d.x * .5f + .5f, d.z * .5f + .5f));
                }
                for (int i = 0; i < seg; i++) t.AddRange(new[] { c, c + 1 + i, c + 2 + i });
            }
            return Fix(Build(v, n, uv, t));
        }

        static Mesh Plane(float size, int cells, bool quad = false)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= cells; j++)
                for (int i = 0; i <= cells; i++)
                {
                    float x = (i / (float)cells - .5f) * size, z = (j / (float)cells - .5f) * size;
                    if (quad) { v.Add(new Vector3(x, z, 0)); n.Add(Vector3.back); }
                    else { v.Add(new Vector3(x, 0, z)); n.Add(Vector3.up); }
                    uv.Add(new Vector2(i / (float)cells, j / (float)cells));
                }
            for (int j = 0; j < cells; j++)
                for (int i = 0; i < cells; i++)
                {
                    int a = j * (cells + 1) + i, b = a + cells + 1;
                    t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            return Fix(Build(v, n, uv, t));
        }
    }
}
