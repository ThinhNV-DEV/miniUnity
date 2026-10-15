// 3D collision geometry for MiniUnity's PhysX-style simulation: shapes in world space (sphere, capsule, box, convex hull,
// triangle mesh with an AABB tree), support mappings for GJK, ray casts, closest points. Single precision like PhysX.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniUnity.Physics3D
{
    public enum GeomKind { Sphere, Capsule, Box, Convex, Mesh }

    /// <summary>Rigid pose (position + rotation).</summary>
    public struct PxPose
    {
        public Vector3 p; public Quaternion q;
        public PxPose(Vector3 p, Quaternion q) { this.p = p; this.q = q; }
        public static readonly PxPose Identity = new(Vector3.zero, Quaternion.identity);
        public Vector3 Apply(Vector3 v) => p + q * v;
        public Vector3 Rotate(Vector3 v) => q * v;
        public Vector3 InvApply(Vector3 v) => Quaternion.Inverse(q) * (v - p);
        public Vector3 InvRotate(Vector3 v) => Quaternion.Inverse(q) * v;
        public PxPose Mul(PxPose b) => new(p + q * b.p, q * b.q);
        public PxPose Inverse() { var iq = Quaternion.Inverse(q); return new(iq * -p, iq); }
    }

    /// <summary>Convex polyhedron data (local space): vertices, face planes with their vertex loops, unique edge directions.</summary>
    public sealed class Polyhedron
    {
        public Vector3[] Verts;
        public Vector3[] FaceNormals; public float[] FaceDist; public int[][] Faces;
        public Vector3[] EdgeDirs;
        public Vector3 Centroid; public float Volume; public Vector3 InertiaDiag; public Quaternion InertiaRot = Quaternion.identity; // unit density, about centroid

        public static Polyhedron Box(Vector3 h)
        {
            var v = new Vector3[8];
            for (int i = 0; i < 8; i++) v[i] = new Vector3((i & 1) != 0 ? h.x : -h.x, (i & 2) != 0 ? h.y : -h.y, (i & 4) != 0 ? h.z : -h.z);
            var faces = new[] { new[] { 1, 3, 7, 5 }, new[] { 0, 4, 6, 2 }, new[] { 2, 6, 7, 3 }, new[] { 0, 1, 5, 4 }, new[] { 4, 5, 7, 6 }, new[] { 0, 2, 3, 1 } };
            var n = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            var d = new[] { h.x, h.x, h.y, h.y, h.z, h.z };
            float m = 8 * h.x * h.y * h.z;
            return new Polyhedron
            {
                Verts = v, Faces = faces, FaceNormals = n, FaceDist = d, EdgeDirs = new[] { Vector3.right, Vector3.up, Vector3.forward },
                Centroid = Vector3.zero, Volume = m,
                InertiaDiag = new Vector3(m / 3 * (h.y * h.y + h.z * h.z), m / 3 * (h.x * h.x + h.z * h.z), m / 3 * (h.x * h.x + h.y * h.y)),
            };
        }

        public int Support(Vector3 dir)
        {
            int best = 0; float bd = float.NegativeInfinity;
            for (int i = 0; i < Verts.Length; i++) { float d = Vector3.Dot(Verts[i], dir); if (d > bd) { bd = d; best = i; } }
            return best;
        }
    }

    /// <summary>Triangle mesh (local space) with a binary AABB tree for queries and contact generation.</summary>
    public sealed class TriMesh
    {
        public Vector3[] V; public int[] T;   // T: 3 indices per triangle
        public int TriCount => T.Length / 3;
        struct Node { public Bounds B; public int Left, Right, Start, Count; }
        Node[] nodes; int[] order; int nodeCount;

        public TriMesh(Vector3[] v, int[] t) { V = v; T = t; Build(); }

        public void Tri(int i, out Vector3 a, out Vector3 b, out Vector3 c) { a = V[T[3 * i]]; b = V[T[3 * i + 1]]; c = V[T[3 * i + 2]]; }

        void Build()
        {
            int n = TriCount;
            order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            nodes = new Node[Math.Max(1, 2 * n)];
            var centers = new Vector3[n];
            for (int i = 0; i < n; i++) { Tri(i, out var a, out var b, out var c); centers[i] = (a + b + c) / 3; }
            nodeCount = 0;
            BuildNode(0, n, centers);
        }

        int BuildNode(int start, int count, Vector3[] centers)
        {
            int id = nodeCount++;
            var b = new Bounds();
            bool first = true;
            for (int i = start; i < start + count; i++)
            {
                Tri(order[i], out var p0, out var p1, out var p2);
                if (first) { b = new Bounds(p0, Vector3.zero); first = false; } else b.Encapsulate(p0);
                b.Encapsulate(p1); b.Encapsulate(p2);
            }
            nodes[id].B = b;
            if (count <= 4) { nodes[id].Start = start; nodes[id].Count = count; nodes[id].Left = nodes[id].Right = -1; return id; }
            var e = b.size; int axis = e.x > e.y ? (e.x > e.z ? 0 : 2) : (e.y > e.z ? 1 : 2);
            Array.Sort(order, start, count, Comparer<int>.Create((x, y) => centers[x][axis].CompareTo(centers[y][axis])));
            int half = count / 2;
            nodes[id].Count = 0;
            nodes[id].Left = BuildNode(start, half, centers);
            nodes[id].Right = BuildNode(start + half, count - half, centers);
            return id;
        }

        /// <summary>Triangles whose bounds overlap the local-space box.</summary>
        public void Query(Bounds q, List<int> outTris)
        {
            if (TriCount == 0) return;
            var stack = new Stack<int>(); stack.Push(0);
            while (stack.Count > 0)
            {
                var n = nodes[stack.Pop()];
                if (!n.B.Intersects(q)) continue;
                if (n.Left < 0) { for (int i = n.Start; i < n.Start + n.Count; i++) outTris.Add(order[i]); continue; }
                stack.Push(n.Left); stack.Push(n.Right);
            }
        }

        /// <summary>Closest ray hit (local space). Two-sided unless frontOnly.</summary>
        public bool Raycast(Vector3 o, Vector3 d, float maxT, bool frontOnly, out float t, out int tri, out Vector3 normal, out Vector2 uv)
        {
            t = maxT; tri = -1; normal = default; uv = default;
            if (TriCount == 0) return false;
            var inv = new Vector3(1 / d.x, 1 / d.y, 1 / d.z);
            var stack = new Stack<int>(); stack.Push(0);
            while (stack.Count > 0)
            {
                var n = nodes[stack.Pop()];
                if (!RayBox(o, inv, n.B, t)) continue;
                if (n.Left >= 0) { stack.Push(n.Left); stack.Push(n.Right); continue; }
                for (int i = n.Start; i < n.Start + n.Count; i++)
                {
                    int k = order[i]; Tri(k, out var a, out var b, out var c);
                    if (Geo.RayTriangle(o, d, a, b, c, frontOnly, out float tt, out float u, out float v) && tt < t)
                    { t = tt; tri = k; normal = Vector3.Cross(b - a, c - a).normalized; uv = new Vector2(u, v); }
                }
            }
            return tri >= 0;
        }

        static bool RayBox(Vector3 o, Vector3 inv, Bounds b, float maxT)
        {
            float t0 = 0, t1 = maxT;
            for (int a = 0; a < 3; a++)
            {
                float lo = (b.min[a] - o[a]) * inv[a], hi = (b.max[a] - o[a]) * inv[a];
                if (float.IsNaN(lo) || float.IsNaN(hi)) { if (o[a] < b.min[a] || o[a] > b.max[a]) return false; continue; }
                if (lo > hi) (lo, hi) = (hi, lo);
                t0 = Math.Max(t0, lo); t1 = Math.Min(t1, hi);
                if (t0 > t1) return false;
            }
            return true;
        }
    }

    /// <summary>Shape geometry in its own local frame (scale already applied).</summary>
    public sealed class Geom
    {
        public GeomKind Kind;
        public float Radius;            // sphere, capsule
        public float HalfHeight;        // capsule: half length of the core segment along local Y
        public Vector3 HalfExtents;     // box
        public Polyhedron Poly;         // box, convex
        public TriMesh Mesh;            // mesh

        public static Geom Sphere(float r) => new() { Kind = GeomKind.Sphere, Radius = r };
        public static Geom Capsule(float r, float halfHeight) => new() { Kind = GeomKind.Capsule, Radius = r, HalfHeight = Math.Max(0, halfHeight) };
        public static Geom Box(Vector3 h) => new() { Kind = GeomKind.Box, HalfExtents = h, Poly = Polyhedron.Box(h) };
        public static Geom Convex(Polyhedron p) => new() { Kind = GeomKind.Convex, Poly = p };
        public static Geom TriangleMesh(TriMesh m) => new() { Kind = GeomKind.Mesh, Mesh = m };

        public bool IsConvex => Kind != GeomKind.Mesh;
        /// <summary>Inner core used by GJK: point (sphere), segment (capsule) or the polyhedron; Radius is added outside it.</summary>
        public float CoreRadius => Kind is GeomKind.Sphere or GeomKind.Capsule ? Radius : 0;

        /// <summary>Local support point of the core (radius excluded).</summary>
        public Vector3 CoreSupport(Vector3 dir)
        {
            switch (Kind)
            {
                case GeomKind.Sphere: return Vector3.zero;
                case GeomKind.Capsule: return new Vector3(0, dir.y >= 0 ? HalfHeight : -HalfHeight, 0);
                case GeomKind.Box: return new Vector3(dir.x >= 0 ? HalfExtents.x : -HalfExtents.x, dir.y >= 0 ? HalfExtents.y : -HalfExtents.y, dir.z >= 0 ? HalfExtents.z : -HalfExtents.z);
                case GeomKind.Convex: return Poly.Verts[Poly.Support(dir)];
                default: throw new InvalidOperationException("mesh has no support mapping");
            }
        }

        public Bounds LocalBounds()
        {
            switch (Kind)
            {
                case GeomKind.Sphere: return new Bounds(Vector3.zero, Vector3.one * 2 * Radius);
                case GeomKind.Capsule: return new Bounds(Vector3.zero, new Vector3(2 * Radius, 2 * (Radius + HalfHeight), 2 * Radius));
                case GeomKind.Box: return new Bounds(Vector3.zero, HalfExtents * 2);
                default:
                    var vs = Kind == GeomKind.Convex ? Poly.Verts : Mesh.V;
                    if (vs.Length == 0) return new Bounds();
                    var b = new Bounds(vs[0], Vector3.zero);
                    foreach (var v in vs) b.Encapsulate(v);
                    return b;
            }
        }

        public Bounds WorldBounds(PxPose pose)
        {
            if (Kind == GeomKind.Sphere) return new Bounds(pose.p, Vector3.one * 2 * Radius);
            var lb = LocalBounds();
            var c = pose.Apply(lb.center); var e = lb.extents;
            var m = Matrix4x4.Rotate(pose.q);
            var ex = new Vector3(Math.Abs(m.m00) * e.x + Math.Abs(m.m01) * e.y + Math.Abs(m.m02) * e.z,
                                 Math.Abs(m.m10) * e.x + Math.Abs(m.m11) * e.y + Math.Abs(m.m12) * e.z,
                                 Math.Abs(m.m20) * e.x + Math.Abs(m.m21) * e.y + Math.Abs(m.m22) * e.z);
            return new Bounds(c, ex * 2);
        }

        /// <summary>Volume and inertia (unit density, about the local origin-centered centroid, principal axes).</summary>
        public void MassProperties(out float volume, out Vector3 com, out Vector3 inertiaDiag, out Quaternion inertiaRot)
        {
            com = Vector3.zero; inertiaRot = Quaternion.identity;
            switch (Kind)
            {
                case GeomKind.Sphere:
                    volume = 4f / 3f * Mathf.PI * Radius * Radius * Radius;
                    inertiaDiag = Vector3.one * (0.4f * volume * Radius * Radius);
                    return;
                case GeomKind.Capsule:
                {
                    float r = Radius, h = 2 * HalfHeight;
                    float cyl = Mathf.PI * r * r * h, hemi = 2f / 3f * Mathf.PI * r * r * r;   // two hemispheres = one sphere
                    volume = cyl + 2 * hemi;
                    float iy = cyl * r * r / 2 + 2 * hemi * 0.4f * r * r;
                    float ix = cyl * (r * r / 4 + h * h / 12) + 2 * hemi * (0.4f * r * r + h * h / 4 + 3f / 8f * h * r);
                    inertiaDiag = new Vector3(ix, iy, ix);
                    return;
                }
                case GeomKind.Box:
                    volume = Poly.Volume; inertiaDiag = Poly.InertiaDiag; return;
                case GeomKind.Convex:
                    volume = Poly.Volume; com = Poly.Centroid; inertiaDiag = Poly.InertiaDiag; inertiaRot = Poly.InertiaRot; return;
                default:
                    // non-convex meshes can only be static or kinematic in PhysX; Unity uses their bounds for mass properties
                    var b = LocalBounds();
                    var box = Polyhedron.Box(b.extents);
                    volume = box.Volume; com = b.center; inertiaDiag = box.InertiaDiag; return;
            }
        }
    }

    public static class Geo
    {
        public const float Eps = 1e-6f;

        public static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b, out float t)
        {
            var ab = b - a; float l2 = Vector3.Dot(ab, ab);
            t = l2 > Eps ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2) : 0;
            return a + ab * t;
        }

        /// <summary>Closest points between segments p1q1 and p2q2 (Ericson, Real-Time Collision Detection 5.1.9).</summary>
        public static void SegmentSegment(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2, out float s, out float t)
        {
            var d1 = q1 - p1; var d2 = q2 - p2; var r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            if (a <= Eps && e <= Eps) { s = t = 0; c1 = p1; c2 = p2; return; }
            if (a <= Eps) { s = 0; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= Eps) { t = 0; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                    s = denom > Eps ? Mathf.Clamp01((b * f - c * e) / denom) : 0;
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1) { t = 1; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            c1 = p1 + d1 * s; c2 = p2 + d2 * t;
        }

        /// <summary>Closest point on triangle abc to p (Ericson 5.1.5).</summary>
        public static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a; var ac = c - a; var ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return a;
            var bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
            var cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6));
            float denom = 1 / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        public static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, bool frontOnly, out float t, out float u, out float v)
        {
            t = u = v = 0;
            var e1 = b - a; var e2 = c - a;
            var pv = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, pv);
            if (frontOnly ? det > -1e-9f : Math.Abs(det) < 1e-9f) return false;   // PhysX meshes: front face = counter-clockwise seen from the normal side
            float inv = 1 / det;
            var tv = o - a;
            u = Vector3.Dot(tv, pv) * inv; if (u < 0 || u > 1) return false;
            var qv = Vector3.Cross(tv, e1);
            v = Vector3.Dot(d, qv) * inv; if (v < 0 || u + v > 1) return false;
            t = Vector3.Dot(e2, qv) * inv;
            return t >= 0;
        }

        /// <summary>Ray vs sphere; origin inside → no hit (Unity: rays starting inside a collider do not detect it).</summary>
        public static bool RaySphere(Vector3 o, Vector3 d, Vector3 c, float r, out float t)
        {
            t = 0;
            var m = o - c; float b = Vector3.Dot(m, d), cc = Vector3.Dot(m, m) - r * r;
            if (cc <= 0) return false;
            if (b > 0) return false;
            float disc = b * b - cc; if (disc < 0) return false;
            t = -b - Mathf.Sqrt(disc);
            return t >= 0;
        }

        public static bool RayCapsule(Vector3 o, Vector3 d, Vector3 a, Vector3 b, float r, out float t, out Vector3 n)
        {
            t = float.PositiveInfinity; n = default;
            // inside?
            ClosestOnSegment(o, a, b, out float s0);
            if ((o - (a + (b - a) * s0)).sqrMagnitude <= r * r) return false;
            var ab = b - a; var ao = o - a;
            float abab = Vector3.Dot(ab, ab);
            bool hit = false;
            if (abab > Eps)
            {
                // infinite cylinder
                var dPerp = d - ab * (Vector3.Dot(d, ab) / abab);
                var oPerp = ao - ab * (Vector3.Dot(ao, ab) / abab);
                float A = Vector3.Dot(dPerp, dPerp), B = 2 * Vector3.Dot(dPerp, oPerp), C = Vector3.Dot(oPerp, oPerp) - r * r;
                if (A > Eps)
                {
                    float disc = B * B - 4 * A * C;
                    if (disc >= 0)
                    {
                        float tt = (-B - Mathf.Sqrt(disc)) / (2 * A);
                        if (tt >= 0)
                        {
                            var p = o + d * tt; float s = Vector3.Dot(p - a, ab) / abab;
                            if (s >= 0 && s <= 1) { t = tt; var axisP = a + ab * s; n = (p - axisP).normalized; hit = true; }
                        }
                    }
                }
            }
            if (RaySphere(o, d, a, r, out float t1) && t1 < t) { t = t1; n = (o + d * t1 - a).normalized; hit = true; }
            if (RaySphere(o, d, b, r, out float t2) && t2 < t) { t = t2; n = (o + d * t2 - b).normalized; hit = true; }
            return hit;
        }

        /// <summary>Ray vs convex polyhedron in local space (origin inside → no hit).</summary>
        public static bool RayPoly(Polyhedron poly, Vector3 o, Vector3 d, out float t, out Vector3 n)
        {
            float tEnter = float.NegativeInfinity, tExit = float.PositiveInfinity; n = default; t = 0;
            bool inside = true; int enterFace = -1;
            for (int i = 0; i < poly.FaceNormals.Length; i++)
            {
                var fn = poly.FaceNormals[i];
                float dist = Vector3.Dot(fn, o) - poly.FaceDist[i];
                float denom = Vector3.Dot(fn, d);
                if (dist > 0) inside = false;
                if (Math.Abs(denom) < 1e-12f) { if (dist > 0) return false; continue; }
                float tt = -dist / denom;
                if (denom < 0) { if (tt > tEnter) { tEnter = tt; enterFace = i; } }
                else if (tt < tExit) tExit = tt;
                if (tEnter > tExit) return false;
            }
            if (inside || enterFace < 0 || tEnter < 0) return false;
            t = tEnter; n = poly.FaceNormals[enterFace];
            return true;
        }

        /// <summary>Closest point on a convex polyhedron (local) to p; returns p when inside.</summary>
        public static Vector3 ClosestOnPoly(Polyhedron poly, Vector3 p, out bool inside)
        {
            inside = true;
            for (int i = 0; i < poly.FaceNormals.Length; i++) if (Vector3.Dot(poly.FaceNormals[i], p) > poly.FaceDist[i]) { inside = false; break; }
            if (inside) return p;
            float best = float.PositiveInfinity; Vector3 bp = p;
            foreach (var f in poly.Faces)
                for (int k = 1; k + 1 < f.Length; k++)
                {
                    var c = ClosestOnTriangle(p, poly.Verts[f[0]], poly.Verts[f[k]], poly.Verts[f[k + 1]]);
                    float d2 = (c - p).sqrMagnitude;
                    if (d2 < best) { best = d2; bp = c; }
                }
            return bp;
        }

        public static Vector3 AnyPerpendicular(Vector3 n)
        {
            var t = Math.Abs(n.x) > 0.57735f ? new Vector3(n.y, -n.x, 0) : new Vector3(0, n.z, -n.y);
            return t.normalized;
        }
    }
}
