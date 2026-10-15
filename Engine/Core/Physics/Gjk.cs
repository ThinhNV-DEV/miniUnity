// GJK distance between convex cores (support mappings in world space), conservative-advancement sweeps, and convex hull
// construction for convex MeshColliders (incremental hull, coplanar triangles merged into polygons, mass properties).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MiniUnity.Physics3D
{
    /// <summary>World-space convex core; the shape is the core inflated by Radius.</summary>
    public abstract class Convex
    {
        public float Radius;
        public abstract Vector3 Support(Vector3 dir);
        public virtual Vector3 Center => Support(Vector3.right) * 0.5f + Support(Vector3.left) * 0.5f;
    }

    public sealed class GeomConvex : Convex
    {
        public Geom G; public PxPose P;
        public GeomConvex(Geom g, PxPose p) { G = g; P = p; Radius = g.CoreRadius; }
        public override Vector3 Support(Vector3 dir) => P.Apply(G.CoreSupport(P.InvRotate(dir)));
        public override Vector3 Center => G.Kind == GeomKind.Convex ? P.Apply(G.Poly.Centroid) : P.p;
    }

    public sealed class PointsConvex : Convex
    {
        public Vector3[] Pts;
        public PointsConvex(float radius, params Vector3[] pts) { Pts = pts; Radius = radius; }
        public override Vector3 Support(Vector3 dir)
        {
            int best = 0; float bd = float.NegativeInfinity;
            for (int i = 0; i < Pts.Length; i++) { float d = Vector3.Dot(Pts[i], dir); if (d > bd) { bd = d; best = i; } }
            return Pts[best];
        }
        public override Vector3 Center { get { var c = Vector3.zero; foreach (var p in Pts) c += p; return c / Pts.Length; } }
    }

    public sealed class TranslatedConvex : Convex
    {
        readonly Convex inner; readonly Vector3 off;
        public TranslatedConvex(Convex c, Vector3 offset) { inner = c; off = offset; Radius = c.Radius; }
        public override Vector3 Support(Vector3 dir) => inner.Support(dir) + off;
        public override Vector3 Center => inner.Center + off;
    }

    public static class Gjk
    {
        struct SV { public Vector3 w, a, b; }

        /// <summary>Distance between the cores of A and B (radii not subtracted). Returns false when the cores intersect.</summary>
        public static bool Distance(Convex A, Convex B, out float dist, out Vector3 pa, out Vector3 pb)
        {
            var simplex = new SV[4]; int n = 0;
            var d = A.Center - B.Center;
            if (d.sqrMagnitude < 1e-12f) d = Vector3.right;
            var v = d;
            float prevDist = float.PositiveInfinity;
            pa = pb = default; dist = 0;
            var lam = new float[4];
            for (int iter = 0; iter < 64; iter++)
            {
                var sa = A.Support(-v); var sb = B.Support(v);
                var w = sa - sb;
                float vv = Vector3.Dot(v, v);
                if (n > 0 && vv - Vector3.Dot(v, w) <= 1e-6f * vv + 1e-12f) break;   // no progress: v is the closest point
                bool dup = false;
                for (int i = 0; i < n; i++) if ((simplex[i].w - w).sqrMagnitude < 1e-12f) dup = true;
                if (dup) break;
                simplex[n++] = new SV { w = w, a = sa, b = sb };
                v = ClosestOnSimplex(simplex, ref n, lam);
                float dd = v.magnitude;
                if (n == 4 || dd < 1e-6f) { dist = 0; Weighted(simplex, n, lam, out pa, out pb); return false; }
                if (dd >= prevDist - 1e-7f) break;
                prevDist = dd;
            }
            Weighted(simplex, n, lam, out pa, out pb);
            dist = (pa - pb).magnitude;
            return dist > 1e-6f;
        }

        static void Weighted(SV[] s, int n, float[] lam, out Vector3 pa, out Vector3 pb)
        {
            pa = pb = Vector3.zero;
            for (int i = 0; i < n; i++) { pa += s[i].a * lam[i]; pb += s[i].b * lam[i]; }
        }

        /// <summary>Closest point of the simplex to the origin; reduces the simplex to the supporting feature and fills barycentrics.</summary>
        static Vector3 ClosestOnSimplex(SV[] s, ref int n, float[] lam)
        {
            switch (n)
            {
                case 1: lam[0] = 1; return s[0].w;
                case 2:
                {
                    var a = s[0].w; var b = s[1].w; var ab = b - a;
                    float t = Vector3.Dot(-a, ab), l2 = Vector3.Dot(ab, ab);
                    if (t <= 0 || l2 < 1e-12f) { n = 1; lam[0] = 1; return a; }
                    if (t >= l2) { s[0] = s[1]; n = 1; lam[0] = 1; return b; }
                    t /= l2; lam[0] = 1 - t; lam[1] = t; return a + ab * t;
                }
                case 3: return Triangle(s, ref n, lam, 0, 1, 2);
                default: return Tetra(s, ref n, lam);
            }
        }

        static Vector3 Triangle(SV[] s, ref int n, float[] lam, int ia, int ib, int ic)
        {
            var a = s[ia].w; var b = s[ib].w; var c = s[ic].w;
            var ab = b - a; var ac = c - a; var ap = -a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            SV A = s[ia], B = s[ib], C = s[ic];
            if (d1 <= 0 && d2 <= 0) { s[0] = A; n = 1; lam[0] = 1; return a; }
            var bp = -b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) { s[0] = B; n = 1; lam[0] = 1; return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) { float t = d1 / (d1 - d3); s[0] = A; s[1] = B; n = 2; lam[0] = 1 - t; lam[1] = t; return a + ab * t; }
            var cp = -c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) { s[0] = C; n = 1; lam[0] = 1; return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) { float t = d2 / (d2 - d6); s[0] = A; s[1] = C; n = 2; lam[0] = 1 - t; lam[1] = t; return a + ac * t; }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            { float t = (d4 - d3) / (d4 - d3 + d5 - d6); s[0] = B; s[1] = C; n = 2; lam[0] = 1 - t; lam[1] = t; return b + (c - b) * t; }
            float den = 1 / (va + vb + vc); float v = vb * den, w = vc * den;
            s[0] = A; s[1] = B; s[2] = C; n = 3; lam[0] = 1 - v - w; lam[1] = v; lam[2] = w;
            return a + ab * v + ac * w;
        }

        static Vector3 Tetra(SV[] s, ref int n, float[] lam)
        {
            var orig = (SV[])s.Clone();
            // origin outside a face plane (opposite the 4th vertex) → closest point is on that face
            int[][] faces = { new[] { 0, 1, 2, 3 }, new[] { 0, 2, 3, 1 }, new[] { 0, 3, 1, 2 }, new[] { 1, 3, 2, 0 } };
            float best = float.PositiveInfinity; Vector3 bestP = Vector3.zero; SV[] bestS = null; int bestN = 0; float[] bestL = null;
            bool inside = true;
            foreach (var f in faces)
            {
                var a = orig[f[0]].w; var b = orig[f[1]].w; var c = orig[f[2]].w; var d = orig[f[3]].w;
                var nrm = Vector3.Cross(b - a, c - a);
                float sO = Vector3.Dot(-a, nrm), sD = Vector3.Dot(d - a, nrm);
                if (sO * sD >= 0 || Math.Abs(sD) < 1e-12f) continue;   // origin on the same side as d
                inside = false;
                var tmp = new SV[4]; tmp[0] = orig[f[0]]; tmp[1] = orig[f[1]]; tmp[2] = orig[f[2]];
                int tn = 3; var tl = new float[4];
                var p = Triangle(tmp, ref tn, tl, 0, 1, 2);
                float dd = p.sqrMagnitude;
                if (dd < best) { best = dd; bestP = p; bestS = tmp; bestN = tn; bestL = tl; }
            }
            if (inside)
            {
                // barycentric coordinates of the origin (for witness points)
                var a = orig[0].w; var b = orig[1].w; var c = orig[2].w; var d = orig[3].w;
                float vol = Vector3.Dot(b - a, Vector3.Cross(c - a, d - a));
                if (Math.Abs(vol) < 1e-15f) { lam[0] = 1; lam[1] = lam[2] = lam[3] = 0; n = 4; return Vector3.zero; }
                lam[1] = Vector3.Dot(-a, Vector3.Cross(c - a, d - a)) / vol;
                lam[2] = Vector3.Dot(b - a, Vector3.Cross(-a, d - a)) / vol;
                lam[3] = Vector3.Dot(b - a, Vector3.Cross(c - a, -a)) / vol;
                lam[0] = 1 - lam[1] - lam[2] - lam[3];
                n = 4; return Vector3.zero;
            }
            for (int i = 0; i < bestN; i++) { s[i] = bestS[i]; lam[i] = bestL[i]; }
            n = bestN; return bestP;
        }

        /// <summary>Shape-cast of A along dir (unit) against B by conservative advancement. Initial overlap → false (like Unity sweeps).</summary>
        public static bool Sweep(Convex A, Convex B, Vector3 dir, float maxDist, out float t, out Vector3 point, out Vector3 normal, float tolerance = 1e-4f)
        {
            t = 0; point = normal = default;
            float r = A.Radius + B.Radius;
            for (int iter = 0; iter < 64; iter++)
            {
                var At = t == 0 ? A : new TranslatedConvex(A, dir * t);
                bool sep = Distance(At, B, out float d, out var pa, out var pb);
                float gap = sep ? d - r : -1;
                if (gap <= tolerance)
                {
                    if (iter == 0) return false;   // started overlapping
                    var nrm = sep ? (pa - pb) / d : -dir;
                    normal = nrm.normalized;
                    point = pb + normal * B.Radius;
                    return true;
                }
                var n = (pa - pb) / d;
                float approach = -Vector3.Dot(dir, n);
                if (approach <= 1e-9f) return false;
                t += gap / approach;
                if (t > maxDist) return false;
            }
            return false;
        }
    }

    public static class Hull
    {
        /// <summary>Convex hull of the points (incremental algorithm), faces merged into planar polygons, with unit-density mass properties.</summary>
        public static Polyhedron Build(IList<Vector3> input)
        {
            var pts = Dedup(input);
            if (pts.Count < 4) return Flat(pts);
            // initial tetrahedron from extreme points
            int i0 = 0, i1 = 0;
            for (int i = 1; i < pts.Count; i++) { if (pts[i].x < pts[i0].x) i0 = i; if (pts[i].x > pts[i1].x) i1 = i; }
            if (i0 == i1) i1 = i0 == 0 ? 1 : 0;
            int i2 = -1; float bestA = 0;
            for (int i = 0; i < pts.Count; i++) { float a = Vector3.Cross(pts[i1] - pts[i0], pts[i] - pts[i0]).sqrMagnitude; if (a > bestA) { bestA = a; i2 = i; } }
            if (i2 < 0 || bestA < 1e-12f) return Flat(pts);
            var nrm = Vector3.Cross(pts[i1] - pts[i0], pts[i2] - pts[i0]);
            int i3 = -1; float bestV = 0;
            for (int i = 0; i < pts.Count; i++) { float v = Math.Abs(Vector3.Dot(pts[i] - pts[i0], nrm)); if (v > bestV) { bestV = v; i3 = i; } }
            if (i3 < 0 || bestV < 1e-9f) return Flat(pts);
            var tris = new List<int[]>();
            void AddTri(int a, int b, int c, Vector3 inside)
            {
                var n = Vector3.Cross(pts[b] - pts[a], pts[c] - pts[a]);
                if (Vector3.Dot(n, inside - pts[a]) > 0) tris.Add(new[] { a, c, b }); else tris.Add(new[] { a, b, c });
            }
            var center = (pts[i0] + pts[i1] + pts[i2] + pts[i3]) / 4;
            AddTri(i0, i1, i2, center); AddTri(i0, i1, i3, center); AddTri(i0, i2, i3, center); AddTri(i1, i2, i3, center);
            float scale = 0; foreach (var p in pts) scale = Math.Max(scale, p.magnitude);
            float eps = Math.Max(1e-6f, scale * 1e-5f);
            for (int i = 0; i < pts.Count; i++)
            {
                if (i == i0 || i == i1 || i == i2 || i == i3) continue;
                var p = pts[i];
                var visible = new List<int[]>();
                foreach (var t in tris)
                {
                    var n = Vector3.Cross(pts[t[1]] - pts[t[0]], pts[t[2]] - pts[t[0]]).normalized;
                    if (Vector3.Dot(n, p - pts[t[0]]) > eps) visible.Add(t);
                }
                if (visible.Count == 0) continue;
                // horizon edges: edges of visible triangles not shared with another visible triangle
                var edges = new Dictionary<(int, int), int>();
                foreach (var t in visible)
                    for (int k = 0; k < 3; k++) { var e = (t[k], t[(k + 1) % 3]); edges[e] = edges.TryGetValue(e, out var c) ? c + 1 : 1; }
                foreach (var t in visible) tris.Remove(t);
                foreach (var (e, _) in edges)
                    if (!edges.ContainsKey((e.Item2, e.Item1))) tris.Add(new[] { e.Item1, e.Item2, i });
            }
            return FromTriangles(pts, tris);
        }

        static List<Vector3> Dedup(IList<Vector3> input)
        {
            var res = new List<Vector3>();
            foreach (var p in input) if (!res.Any(q => (q - p).sqrMagnitude < 1e-12f)) res.Add(p);
            return res;
        }

        static Polyhedron Flat(List<Vector3> pts)
        {
            // degenerate (flat or tiny) input: thin box around the points
            var b = new Bounds(pts.Count > 0 ? pts[0] : Vector3.zero, Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            var h = Vector3.Max(b.extents, Vector3.one * 1e-3f);
            var box = Polyhedron.Box(h);
            for (int i = 0; i < box.Verts.Length; i++) box.Verts[i] += b.center;
            for (int i = 0; i < box.FaceDist.Length; i++) box.FaceDist[i] += Vector3.Dot(box.FaceNormals[i], b.center);
            box.Centroid = b.center;
            return box;
        }

        static Polyhedron FromTriangles(List<Vector3> pts, List<int[]> tris)
        {
            // merge coplanar neighbours into polygons
            var used = new bool[tris.Count];
            var faces = new List<int[]>(); var normals = new List<Vector3>(); var dists = new List<float>();
            var triN = tris.Select(t => Vector3.Cross(pts[t[1]] - pts[t[0]], pts[t[2]] - pts[t[0]]).normalized).ToList();
            for (int i = 0; i < tris.Count; i++)
            {
                if (used[i]) continue;
                var group = new List<int>();
                for (int j = i; j < tris.Count; j++)
                    if (!used[j] && Vector3.Dot(triN[i], triN[j]) > 0.9999f && Math.Abs(Vector3.Dot(triN[i], pts[tris[j][0]] - pts[tris[i][0]])) < 1e-5f)
                    { used[j] = true; group.Add(j); }
                // boundary loop of the group
                var edges = new Dictionary<int, int>(); var all = new HashSet<(int, int)>();
                foreach (var g in group) for (int k = 0; k < 3; k++) all.Add((tris[g][k], tris[g][(k + 1) % 3]));
                foreach (var (a, b) in all) if (!all.Contains((b, a))) edges[a] = b;
                if (edges.Count < 3) continue;
                var loop = new List<int>(); int start = edges.Keys.First(), cur = start;
                for (int guard = 0; guard <= edges.Count; guard++) { loop.Add(cur); if (!edges.TryGetValue(cur, out cur) || cur == start) break; }
                faces.Add(loop.ToArray()); normals.Add(triN[i]); dists.Add(Vector3.Dot(triN[i], pts[tris[i][0]]));
            }
            // compact vertices
            var map = new Dictionary<int, int>(); var verts = new List<Vector3>();
            foreach (var f in faces) for (int k = 0; k < f.Length; k++) { if (!map.TryGetValue(f[k], out var m)) { m = verts.Count; map[f[k]] = m; verts.Add(pts[f[k]]); } f[k] = m; }
            // unique edge directions
            var dirs = new List<Vector3>();
            foreach (var f in faces)
                for (int k = 0; k < f.Length; k++)
                {
                    var e = (verts[f[(k + 1) % f.Length]] - verts[f[k]]).normalized;
                    if (!dirs.Any(d => Math.Abs(Vector3.Dot(d, e)) > 0.9999f)) dirs.Add(e);
                }
            var poly = new Polyhedron { Verts = verts.ToArray(), Faces = faces.ToArray(), FaceNormals = normals.ToArray(), FaceDist = dists.ToArray(), EdgeDirs = dirs.ToArray() };
            ComputeMass(poly, tris.Select(t => new[] { map[t[0]], map[t[1]], map[t[2]] }).ToList());
            return poly;
        }

        /// <summary>Volume, centroid and principal inertia (unit density) by tetrahedra from the origin (Mirtich / Tonon).</summary>
        static void ComputeMass(Polyhedron poly, List<int[]> tris)
        {
            double vol = 0; var c = new double[3];
            double xx = 0, yy = 0, zz = 0, xy = 0, yz = 0, zx = 0;
            foreach (var t in tris)
            {
                var a = poly.Verts[t[0]]; var b = poly.Verts[t[1]]; var d = poly.Verts[t[2]];
                double v6 = Vector3.Dot(a, Vector3.Cross(b, d)); double v = v6 / 6;
                vol += v;
                for (int k = 0; k < 3; k++) c[k] += v * (a[k] + b[k] + d[k]) / 4;
                double S(int i, int j) => (2 * (a[i] * a[j] + b[i] * b[j] + d[i] * d[j]) + a[i] * b[j] + a[j] * b[i] + a[i] * d[j] + a[j] * d[i] + b[i] * d[j] + b[j] * d[i]) * v / 20;
                xx += S(0, 0); yy += S(1, 1); zz += S(2, 2); xy += S(0, 1); yz += S(1, 2); zx += S(2, 0);
            }
            if (Math.Abs(vol) < 1e-12) { poly.Volume = 0; return; }
            var com = new Vector3((float)(c[0] / vol), (float)(c[1] / vol), (float)(c[2] / vol));
            // shift second moments to the centroid
            xx -= vol * com.x * com.x; yy -= vol * com.y * com.y; zz -= vol * com.z * com.z;
            xy -= vol * com.x * com.y; yz -= vol * com.y * com.z; zx -= vol * com.z * com.x;
            var I = new double[3, 3] { { yy + zz, -xy, -zx }, { -xy, xx + zz, -yz }, { -zx, -yz, xx + yy } };
            Diagonalize(I, out var diag, out var rot);
            poly.Volume = (float)vol; poly.Centroid = com; poly.InertiaDiag = diag; poly.InertiaRot = rot;
        }

        /// <summary>Jacobi eigen decomposition of a symmetric 3x3 matrix → principal moments + rotation.</summary>
        public static void Diagonalize(double[,] m, out Vector3 diag, out Quaternion rot)
        {
            var a = (double[,])m.Clone();
            var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int sweep = 0; sweep < 50; sweep++)
            {
                double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                if (off < 1e-15) break;
                for (int p = 0; p < 2; p++)
                    for (int q = p + 1; q < 3; q++)
                    {
                        if (Math.Abs(a[p, q]) < 1e-18) continue;
                        double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                        double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1)); if (theta == 0) t = 1;
                        double cs = 1 / Math.Sqrt(t * t + 1), sn = t * cs;
                        for (int k = 0; k < 3; k++) { double akp = a[k, p], akq = a[k, q]; a[k, p] = cs * akp - sn * akq; a[k, q] = sn * akp + cs * akq; }
                        for (int k = 0; k < 3; k++) { double apk = a[p, k], aqk = a[q, k]; a[p, k] = cs * apk - sn * aqk; a[q, k] = sn * apk + cs * aqk; }
                        for (int k = 0; k < 3; k++) { double vkp = v[k, p], vkq = v[k, q]; v[k, p] = cs * vkp - sn * vkq; v[k, q] = sn * vkp + cs * vkq; }
                    }
            }
            diag = new Vector3((float)a[0, 0], (float)a[1, 1], (float)a[2, 2]);
            // columns of v are the principal axes; make it a proper rotation
            var c0 = new Vector3((float)v[0, 0], (float)v[1, 0], (float)v[2, 0]);
            var c1 = new Vector3((float)v[0, 1], (float)v[1, 1], (float)v[2, 1]);
            var c2 = Vector3.Cross(c0, c1);
            rot = Quaternion.LookRotation(c2, c1);
        }
    }
}
