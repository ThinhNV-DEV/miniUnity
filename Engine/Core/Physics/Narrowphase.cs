// Contact generation (PhysX-style manifolds): contacts are produced while the shapes are closer than the sum of their contact
// offsets (speculative contacts, negative separation = penetration). Normal points from shape B to shape A.
//   sphere/capsule pairs: segment closest points (two points for parallel capsules)
//   sphere/capsule vs box/convex/triangle: GJK when separated, SAT when the core penetrates; flat capsule contacts are clipped to the face
//   box/convex/triangle pairs: SAT (faces + edge pairs), reference/incident face clipping, reduced to four points
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniUnity.Physics3D
{
    public struct Contact
    {
        public Vector3 Point, Normal; public float Separation; public int Face;
    }

    public static class Narrowphase
    {
        /// <summary>Contacts between shape A (geom ga at pa) and shape B, up to separation maxSep.</summary>
        public static void Collide(Geom ga, PxPose pa, Geom gb, PxPose pb, float maxSep, List<Contact> outC)
        {
            if (ga.Kind == GeomKind.Mesh && gb.Kind == GeomKind.Mesh) return;   // PhysX: no mesh-mesh contacts
            if (ga.Kind == GeomKind.Mesh) { int s = outC.Count; Collide(gb, pb, ga, pa, maxSep, outC); Flip(outC, s); return; }
            if (gb.Kind == GeomKind.Mesh) { MeshContacts(ga, pa, gb, pb, maxSep, outC); return; }
            bool roundA = ga.Kind is GeomKind.Sphere or GeomKind.Capsule, roundB = gb.Kind is GeomKind.Sphere or GeomKind.Capsule;
            if (roundA && roundB) { RoundRound(ga, pa, gb, pb, maxSep, outC); return; }
            if (roundA) { RoundPoly(Core(ga, pa), ga.Radius, gb.Poly, pb, maxSep, outC, false); return; }
            if (roundB) { int s = outC.Count; RoundPoly(Core(gb, pb), gb.Radius, ga.Poly, pa, maxSep, outC, false); Flip(outC, s); return; }
            PolyPoly(ga.Poly, pa, gb.Poly, pb, maxSep, outC, false);
        }

        static void Flip(List<Contact> c, int start)
        {
            for (int i = start; i < c.Count; i++) { var k = c[i]; k.Normal = -k.Normal; c[i] = k; }
        }

        /// <summary>Core of a round shape in world space: one point (sphere) or two (capsule segment).</summary>
        public static Vector3[] Core(Geom g, PxPose p) =>
            g.Kind == GeomKind.Sphere ? new[] { p.p } : new[] { p.Apply(new Vector3(0, -g.HalfHeight, 0)), p.Apply(new Vector3(0, g.HalfHeight, 0)) };

        static void Add(List<Contact> c, Vector3 point, Vector3 n, float sep, int face = -1) => c.Add(new Contact { Point = point, Normal = n, Separation = sep, Face = face });

        // ---------------------------------------------------------------- round vs round
        static void RoundRound(Geom ga, PxPose pa, Geom gb, PxPose pb, float maxSep, List<Contact> outC)
        {
            var A = Core(ga, pa); var B = Core(gb, pb);
            float r = ga.Radius + gb.Radius;
            Vector3 ca, cb;
            if (A.Length == 1 && B.Length == 1) { ca = A[0]; cb = B[0]; }
            else if (A.Length == 1) { cb = Geo.ClosestOnSegment(A[0], B[0], B[1], out _); ca = A[0]; }
            else if (B.Length == 1) { ca = Geo.ClosestOnSegment(B[0], A[0], A[1], out _); cb = B[0]; }
            else
            {
                Geo.SegmentSegment(A[0], A[1], B[0], B[1], out ca, out cb, out _, out _);
                // parallel capsules: two contacts from the overlapping part of the segments
                var da = (A[1] - A[0]); var db = (B[1] - B[0]);
                if (da.sqrMagnitude > 1e-8f && db.sqrMagnitude > 1e-8f && Math.Abs(Vector3.Dot(da.normalized, db.normalized)) > 0.995f)
                {
                    var dir = da.normalized;
                    float a0 = 0, a1 = da.magnitude;
                    float b0 = Vector3.Dot(B[0] - A[0], dir), b1 = Vector3.Dot(B[1] - A[0], dir);
                    float lo = Math.Max(a0, Math.Min(b0, b1)), hi = Math.Min(a1, Math.Max(b0, b1));
                    if (hi - lo > 1e-4f)
                    {
                        int before = outC.Count;
                        foreach (var s in new[] { lo, hi })
                        {
                            var p = A[0] + dir * s;
                            var q = Geo.ClosestOnSegment(p, B[0], B[1], out _);
                            var d = p - q; float len = d.magnitude;
                            if (len < 1e-6f) continue;
                            float sep = len - r;
                            if (sep < maxSep) Add(outC, q + d / len * (gb.Radius + sep * 0.5f), d / len, sep);
                        }
                        if (outC.Count > before) return;
                    }
                }
            }
            var dv = ca - cb; float dl = dv.magnitude;
            var n = dl > 1e-6f ? dv / dl : Vector3.up;
            float se = dl - r;
            if (se < maxSep) Add(outC, cb + n * (gb.Radius + se * 0.5f), n, se);
        }

        // ---------------------------------------------------------------- round vs polyhedron (box, convex, triangle)
        /// <summary>Round core (point or segment, world) with radius r vs polyhedron (local data, world pose). Normal: poly → round.</summary>
        public static void RoundPoly(Vector3[] core, float r, Polyhedron poly, PxPose pp, float maxSep, List<Contact> outC, bool oneSided)
        {
            // work in the polyhedron's local frame
            var c = new Vector3[core.Length];
            for (int i = 0; i < core.Length; i++) c[i] = pp.InvApply(core[i]);
            if (oneSided)
            {
                // mesh triangles: ignore shapes whose core is behind the face
                float dmin = float.PositiveInfinity; foreach (var p in c) dmin = Math.Min(dmin, Vector3.Dot(poly.FaceNormals[0], p) - poly.FaceDist[0]);
                float dmax = float.NegativeInfinity; foreach (var p in c) dmax = Math.Max(dmax, Vector3.Dot(poly.FaceNormals[0], p) - poly.FaceDist[0]);
                if (dmax < -r * 0.5f && dmin < 0) return;
            }
            var coreConvex = new PointsConvex(0, c);
            var polyConvex = new GeomConvex(new Geom { Kind = GeomKind.Convex, Poly = poly }, PxPose.Identity);
            int start = outC.Count;
            if (Gjk.Distance(coreConvex, polyConvex, out float dist, out var cp, out var pp2) && dist > 1e-5f)
            {
                float sep = dist - r;
                if (sep >= maxSep) return;
                var n = (cp - pp2) / dist;
                // flat capsule on a face → clip the segment to the face
                if (c.Length == 2)
                {
                    int f = BestFace(poly, n);
                    var fn = poly.FaceNormals[f];
                    var seg = (c[1] - c[0]);
                    if (Vector3.Dot(fn, n) > 0.99f && seg.sqrMagnitude > 1e-8f && Math.Abs(Vector3.Dot(seg.normalized, fn)) < 0.05f)
                    {
                        var pts = ClipSegmentToFace(c[0], c[1], poly, f);
                        foreach (var p in pts)
                        {
                            float s = Vector3.Dot(fn, p) - poly.FaceDist[f] - r;
                            if (s < maxSep) Add(outC, pp.Apply(p - fn * (r + s * 0.5f)), pp.Rotate(fn), s, f);
                        }
                        if (outC.Count > start) return;
                    }
                }
                Add(outC, pp.Apply(pp2 + n * (sep * 0.5f)), pp.Rotate(n), sep);
                return;
            }
            // core penetrates: SAT over face normals (+ segment × edges for capsules)
            float best = float.NegativeInfinity; Vector3 bn = Vector3.up; int bface = -1;
            int faceCount = oneSided ? 1 : poly.FaceNormals.Length;
            for (int i = 0; i < faceCount; i++)
            {
                var fn = poly.FaceNormals[i];
                float minC = float.PositiveInfinity; foreach (var p in c) minC = Math.Min(minC, Vector3.Dot(fn, p));
                float s = minC - poly.FaceDist[i] - r;
                if (s > best) { best = s; bn = fn; bface = i; }
            }
            if (c.Length == 2 && !oneSided)
            {
                var sd = (c[1] - c[0]);
                foreach (var e in poly.EdgeDirs)
                {
                    var ax = Vector3.Cross(sd, e); float l = ax.magnitude; if (l < 1e-5f) continue; ax /= l;
                    float pMax = float.NegativeInfinity, pMin = float.PositiveInfinity;
                    foreach (var v in poly.Verts) { float d = Vector3.Dot(v, ax); pMax = Math.Max(pMax, d); pMin = Math.Min(pMin, d); }
                    float c0 = Vector3.Dot(c[0], ax), c1 = Vector3.Dot(c[1], ax);
                    // choose the direction that pushes the capsule out the shortest way
                    float sPos = Math.Min(c0, c1) - pMax - r, sNeg = pMin - Math.Max(c0, c1) - r;
                    if (sPos > best + 1e-3f) { best = sPos; bn = ax; bface = -1; }
                    if (sNeg > best + 1e-3f) { best = sNeg; bn = -ax; bface = -1; }
                }
            }
            if (best >= maxSep) return;
            if (bface >= 0)
            {
                foreach (var p in c)
                {
                    float s = Vector3.Dot(bn, p) - poly.FaceDist[bface] - r;
                    if (s < maxSep) Add(outC, pp.Apply(p - bn * (r + s * 0.5f)), pp.Rotate(bn), s, bface);
                }
            }
            else
            {
                var mid = (c[0] + c[1]) * 0.5f;
                Add(outC, pp.Apply(mid - bn * r), pp.Rotate(bn), best);
            }
        }

        static int BestFace(Polyhedron poly, Vector3 n)
        {
            int best = 0; float bd = float.NegativeInfinity;
            for (int i = 0; i < poly.FaceNormals.Length; i++) { float d = Vector3.Dot(poly.FaceNormals[i], n); if (d > bd) { bd = d; best = i; } }
            return best;
        }

        static List<Vector3> ClipSegmentToFace(Vector3 a, Vector3 b, Polyhedron poly, int f)
        {
            var face = poly.Faces[f]; var fn = poly.FaceNormals[f];
            float t0 = 0, t1 = 1; var d = b - a;
            for (int k = 0; k < face.Length; k++)
            {
                var v0 = poly.Verts[face[k]]; var v1 = poly.Verts[face[(k + 1) % face.Length]];
                var sideN = Vector3.Cross(v1 - v0, fn);   // outward for counter-clockwise faces
                float da = Vector3.Dot(a - v0, sideN), dd = Vector3.Dot(d, sideN);
                if (Math.Abs(dd) < 1e-12f) { if (da > 0) return new List<Vector3>(); continue; }
                float t = -da / dd;
                if (dd > 0) t1 = Math.Min(t1, t); else t0 = Math.Max(t0, t);
                if (t0 > t1) return new List<Vector3>();
            }
            return new List<Vector3> { a + d * t0, a + d * t1 };
        }

        // ---------------------------------------------------------------- polyhedron vs polyhedron
        static Vector3[] World(Polyhedron p, PxPose pose)
        {
            var w = new Vector3[p.Verts.Length];
            for (int i = 0; i < w.Length; i++) w[i] = pose.Apply(p.Verts[i]);
            return w;
        }

        static void Project(Vector3[] vs, Vector3 n, out float min, out float max)
        {
            min = float.PositiveInfinity; max = float.NegativeInfinity;
            foreach (var v in vs) { float d = Vector3.Dot(v, n); if (d < min) min = d; if (d > max) max = d; }
        }

        /// <summary>SAT + clipping. oneSidedB: B is a mesh triangle (only its front face is a reference/separating face).</summary>
        public static void PolyPoly(Polyhedron A, PxPose pa, Polyhedron B, PxPose pb, float maxSep, List<Contact> outC, bool oneSidedB)
        {
            var wa = World(A, pa); var wb = World(B, pb);
            // faces of A: B's vertices outside A's face planes
            float sepA = float.NegativeInfinity; int faceA = -1;
            for (int i = 0; i < A.FaceNormals.Length; i++)
            {
                var n = pa.Rotate(A.FaceNormals[i]); float d = A.FaceDist[i] + Vector3.Dot(n, pa.p);
                Project(wb, n, out float mn, out _);
                float s = mn - d;
                if (s > sepA) { sepA = s; faceA = i; }
                if (s > maxSep) return;
            }
            float sepB = float.NegativeInfinity; int faceB = -1;
            int nbFaces = oneSidedB ? 1 : B.FaceNormals.Length;
            for (int i = 0; i < nbFaces; i++)
            {
                var n = pb.Rotate(B.FaceNormals[i]); float d = B.FaceDist[i] + Vector3.Dot(n, pb.p);
                Project(wa, n, out float mn, out _);
                float s = mn - d;
                if (s > sepB) { sepB = s; faceB = i; }
                if (s > maxSep) return;
            }
            // edge pairs
            float sepE = float.NegativeInfinity; Vector3 nE = default; int eA = -1, eB = -1;
            var cA = pa.Apply(A.Centroid); var cB = pb.Apply(B.Centroid);
            for (int i = 0; i < A.EdgeDirs.Length; i++)
            {
                var da = pa.Rotate(A.EdgeDirs[i]);
                for (int j = 0; j < B.EdgeDirs.Length; j++)
                {
                    var db = pb.Rotate(B.EdgeDirs[j]);
                    var n = Vector3.Cross(da, db); float l = n.magnitude; if (l < 1e-4f) continue; n /= l;
                    if (oneSidedB) { if (Vector3.Dot(n, pb.Rotate(B.FaceNormals[0])) > 0) n = -n; }   // A sits on the front side
                    else if (Vector3.Dot(n, cB - cA) < 0) n = -n;
                    Project(wa, n, out _, out float maxA); Project(wb, n, out float minB, out _);
                    float s = minB - maxA;
                    if (s > maxSep) return;
                    if (s > sepE) { sepE = s; nE = n; eA = i; eB = j; }
                }
            }
            const float faceBias = 0.98f, absTol = 0.001f;
            float bestFace = Math.Max(sepA, sepB);
            if (eA >= 0 && sepE > bestFace * faceBias + absTol && sepE > bestFace + absTol)
            {
                // edge-edge: closest points of the two supporting edges
                var segA = SupportEdge(A, pa, wa, eA, nE); var segB = SupportEdge(B, pb, wb, eB, -nE);
                Geo.SegmentSegment(segA.Item1, segA.Item2, segB.Item1, segB.Item2, out var p1, out var p2, out _, out _);
                // normal B → A is -nE (nE points from A to B)
                Add(outC, (p1 + p2) * 0.5f, -nE, sepE);
                return;
            }
            // reference face: the face axis with the larger separation (mesh triangles win ties: their normal is the stable one)
            bool refIsA = oneSidedB ? sepA > sepB + absTol : sepA >= sepB - absTol;
            if (refIsA) ClipFaces(A, pa, faceA, B, pb, wb, maxSep, outC, false);
            else ClipFaces(B, pb, faceB, A, pa, wa, maxSep, outC, true);
        }

        static (Vector3, Vector3) SupportEdge(Polyhedron p, PxPose pose, Vector3[] w, int dirIndex, Vector3 n)
        {
            var dir = p.EdgeDirs[dirIndex];
            float best = float.NegativeInfinity; (Vector3, Vector3) res = default;
            foreach (var f in p.Faces)
                for (int k = 0; k < f.Length; k++)
                {
                    int i0 = f[k], i1 = f[(k + 1) % f.Length];
                    var e = (p.Verts[i1] - p.Verts[i0]).normalized;
                    if (Math.Abs(Vector3.Dot(e, dir)) < 0.999f) continue;
                    float d = Vector3.Dot((w[i0] + w[i1]) * 0.5f, n);
                    if (d > best) { best = d; res = (w[i0], w[i1]); }
                }
            return res;
        }

        /// <summary>Reference face of R clipped against the incident face of I. flipNormal: R is shape B (normal must point to A).</summary>
        static void ClipFaces(Polyhedron R, PxPose pr, int refFace, Polyhedron I, PxPose pi, Vector3[] wi, float maxSep, List<Contact> outC, bool flipNormal)
        {
            var rn = pr.Rotate(R.FaceNormals[refFace]);
            float rd = R.FaceDist[refFace] + Vector3.Dot(rn, pr.p);
            // incident face: most anti-parallel
            int inc = 0; float bd = float.PositiveInfinity;
            for (int i = 0; i < I.FaceNormals.Length; i++) { float d = Vector3.Dot(pi.Rotate(I.FaceNormals[i]), rn); if (d < bd) { bd = d; inc = i; } }
            var poly = new List<Vector3>();
            foreach (var vi in I.Faces[inc]) poly.Add(wi[vi]);
            var rf = R.Faces[refFace];
            for (int k = 0; k < rf.Length && poly.Count > 0; k++)
            {
                var v0 = pr.Apply(R.Verts[rf[k]]); var v1 = pr.Apply(R.Verts[rf[(k + 1) % rf.Length]]);
                var sideN = Vector3.Cross(v1 - v0, rn).normalized;
                float sd = Vector3.Dot(sideN, v0);
                poly = ClipPolygon(poly, sideN, sd);
            }
            var pts = new List<Contact>();
            foreach (var p in poly)
            {
                float s = Vector3.Dot(rn, p) - rd;
                if (s < maxSep) pts.Add(new Contact { Point = p - rn * (s * 0.5f), Normal = flipNormal ? rn : -rn, Separation = s, Face = refFace });
            }
            Reduce(pts, outC);
        }

        static List<Vector3> ClipPolygon(List<Vector3> poly, Vector3 n, float d)
        {
            var res = new List<Vector3>();
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                float da = Vector3.Dot(n, a) - d, db = Vector3.Dot(n, b) - d;
                if (da <= 0) res.Add(a);
                if ((da < 0 && db > 0) || (da > 0 && db < 0)) res.Add(a + (b - a) * (da / (da - db)));
            }
            return res;
        }

        /// <summary>PhysX keeps at most four points per patch: deepest, farthest from it, then the two that maximise the area.</summary>
        public static void Reduce(List<Contact> pts, List<Contact> outC)
        {
            if (pts.Count <= 4) { outC.AddRange(pts); return; }
            int i0 = 0; for (int i = 1; i < pts.Count; i++) if (pts[i].Separation < pts[i0].Separation) i0 = i;
            int i1 = -1; float bd = -1;
            for (int i = 0; i < pts.Count; i++) { float d = (pts[i].Point - pts[i0].Point).sqrMagnitude; if (d > bd) { bd = d; i1 = i; } }
            var n = pts[i0].Normal; var a = pts[i0].Point; var b = pts[i1].Point;
            int i2 = -1, i3 = -1; float maxP = 0, minN = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                float area = Vector3.Dot(Vector3.Cross(b - a, pts[i].Point - a), n);
                if (area > maxP) { maxP = area; i2 = i; }
                if (area < minN) { minN = area; i3 = i; }
            }
            outC.Add(pts[i0]); outC.Add(pts[i1]);
            if (i2 >= 0) outC.Add(pts[i2]);
            if (i3 >= 0) outC.Add(pts[i3]);
        }

        // ---------------------------------------------------------------- vs triangle mesh
        static readonly List<int> triBuf = new();

        static void MeshContacts(Geom g, PxPose pg, Geom mesh, PxPose pm, float maxSep, List<Contact> outC)
        {
            var wb = g.WorldBounds(pg);
            // bounds in mesh local space
            var lb = new Geom { Kind = GeomKind.Box, HalfExtents = wb.extents + Vector3.one * Math.Max(0, maxSep) }.WorldBounds(new PxPose(pm.InvApply(wb.center), Quaternion.Inverse(pm.q)));
            triBuf.Clear();
            mesh.Mesh.Query(lb, triBuf);
            if (triBuf.Count == 0) return;
            var all = new List<Contact>();
            var tris = new List<int>(triBuf);
            foreach (int t in tris)
            {
                mesh.Mesh.Tri(t, out var a, out var b, out var c);
                var tp = TrianglePoly(a, b, c);
                if (tp == null) continue;
                int s = all.Count;
                if (g.Kind is GeomKind.Sphere or GeomKind.Capsule) RoundPoly(Core(g, pg), g.Radius, tp, pm, maxSep, all, true);
                else PolyPoly(g.Poly, pg, tp, pm, maxSep, all, true);
                for (int i = s; i < all.Count; i++) { var k = all[i]; k.Face = t; all[i] = k; }
            }
            if (all.Count == 0) return;
            // merge: drop near-duplicate points, then reduce per normal cluster
            all.Sort((x, y) => x.Separation.CompareTo(y.Separation));
            var kept = new List<Contact>();
            foreach (var c in all)
            {
                bool dup = false;
                foreach (var k in kept) if ((k.Point - c.Point).sqrMagnitude < 1e-6f && Vector3.Dot(k.Normal, c.Normal) > 0.9f) { dup = true; break; }
                if (!dup) kept.Add(c);
            }
            while (kept.Count > 0)
            {
                var n = kept[0].Normal;
                var cluster = kept.FindAll(k => Vector3.Dot(k.Normal, n) > 0.95f);
                kept.RemoveAll(k => Vector3.Dot(k.Normal, n) > 0.95f);
                Reduce(cluster, outC);
            }
        }

        public static Polyhedron TrianglePoly(Vector3 a, Vector3 b, Vector3 c)
        {
            var n = Vector3.Cross(b - a, c - a); float l = n.magnitude;
            if (l < 1e-12f) return null;
            n /= l;
            return new Polyhedron
            {
                Verts = new[] { a, b, c },
                Faces = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 } },
                FaceNormals = new[] { n, -n }, FaceDist = new[] { Vector3.Dot(n, a), -Vector3.Dot(n, a) },
                EdgeDirs = new[] { (b - a).normalized, (c - b).normalized, (a - c).normalized },
                Centroid = (a + b + c) / 3,
            };
        }
    }
}
