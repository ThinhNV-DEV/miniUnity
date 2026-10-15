// Outline of the union of polygons, for CompositeCollider2D: every edge is split where it crosses another, a split piece is
// kept when one side of it lies inside the union and the other outside, and the kept pieces are chained into closed paths
// (outer boundaries counter-clockwise, holes clockwise). Shared edges (tiles side by side) have the union on both sides and
// disappear, so neighbouring pieces weld into one outline. Collinear points and points closer than vertexDistance are merged.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MiniUnity.Box2D
{
    public static class PolygonUnion
    {
        /// <param name="polygons">simple polygons (either winding)</param>
        public static List<List<Vector2>> Outline(IList<IList<Vector2>> polygons, float vertexDistance = 0.0005f)
        {
            var polys = polygons.Where(p => p.Count >= 3).Select(p => p.Select(v => new Vector2d(v.x, v.y)).ToArray()).ToList();
            if (polys.Count == 0) return new List<List<Vector2>>();
            // bounding boxes for the inside tests
            var boxes = polys.Select(p => (minX: p.Min(v => v.x), minY: p.Min(v => v.y), maxX: p.Max(v => v.x), maxY: p.Max(v => v.y))).ToList();
            var segs = new List<(Vector2d a, Vector2d b)>();
            foreach (var p in polys) for (int i = 0; i < p.Length; i++) segs.Add((p[i], p[(i + 1) % p.Length]));
            double scale = Math.Max(1e-9, polys.SelectMany(p => p).Max(v => Math.Max(Math.Abs(v.x), Math.Abs(v.y))));
            double eps = 1e-9 * Math.Max(1, scale);

            // split every segment at the points where others cross or touch it
            var pieces = new List<(Vector2d a, Vector2d b)>();
            for (int i = 0; i < segs.Count; i++)
            {
                var (a, b) = segs[i];
                var d = b - a; double len2 = d.Dot(d);
                if (len2 < eps * eps) continue;
                var ts = new List<double> { 0, 1 };
                double sMinX = Math.Min(a.x, b.x) - eps, sMaxX = Math.Max(a.x, b.x) + eps, sMinY = Math.Min(a.y, b.y) - eps, sMaxY = Math.Max(a.y, b.y) + eps;
                for (int j = 0; j < segs.Count; j++)
                {
                    if (i == j) continue;
                    var (c, e) = segs[j];
                    if (Math.Max(c.x, e.x) < sMinX || Math.Min(c.x, e.x) > sMaxX || Math.Max(c.y, e.y) < sMinY || Math.Min(c.y, e.y) > sMaxY) continue;
                    // endpoints of the other segment lying on this one
                    foreach (var q in new[] { c, e })
                    {
                        double t = (q - a).Dot(d) / len2;
                        if (t > 0 && t < 1 && ((a + d * t) - q).Length() < eps * 10) ts.Add(t);
                    }
                    // proper crossing
                    var f = e - c; double den = d.Cross(f);
                    if (Math.Abs(den) < 1e-18) continue;
                    double tt = (c - a).Cross(f) / den, u = (c - a).Cross(d) / den;
                    if (tt > 0 && tt < 1 && u >= 0 && u <= 1) ts.Add(tt);
                }
                ts.Sort();
                for (int k = 0; k + 1 < ts.Count; k++)
                {
                    if (ts[k + 1] - ts[k] < 1e-12) continue;
                    pieces.Add((a + d * ts[k], a + d * ts[k + 1]));
                }
            }

            bool Inside(Vector2d p)
            {
                for (int i = 0; i < polys.Count; i++)
                {
                    var bx = boxes[i];
                    if (p.x < bx.minX || p.x > bx.maxX || p.y < bx.minY || p.y > bx.maxY) continue;
                    if (PointInPolygon(polys[i], p)) return true;
                }
                return false;
            }

            // keep boundary pieces, oriented with the union on their left; drop duplicates (a shared edge listed twice)
            var kept = new List<(Vector2d a, Vector2d b)>();
            var seen = new HashSet<(long, long, long, long)>();
            foreach (var (a, b) in pieces)
            {
                var d = b - a; double len = d.Length();
                var n = new Vector2d(-d.y / len, d.x / len);   // left normal
                var m = (a + b) * 0.5; double off = Math.Max(eps * 100, len * 1e-4);
                bool left = Inside(m + n * off), right = Inside(m - n * off);
                if (left == right) continue;
                var (p0, p1) = left ? (a, b) : (b, a);
                var key = (Q(p0.x, eps), Q(p0.y, eps), Q(p1.x, eps), Q(p1.y, eps));
                if (!seen.Add(key)) continue;
                kept.Add((p0, p1));
            }

            // chain into loops
            var byStart = new Dictionary<(long, long), List<int>>();
            for (int i = 0; i < kept.Count; i++)
            {
                var k = (Q(kept[i].a.x, eps), Q(kept[i].a.y, eps));
                (byStart.TryGetValue(k, out var l) ? l : byStart[k] = new List<int>()).Add(i);
            }
            var used = new bool[kept.Count];
            var loops = new List<List<Vector2d>>();
            for (int s = 0; s < kept.Count; s++)
            {
                if (used[s]) continue;
                var loop = new List<Vector2d>();
                int cur = s;
                while (cur >= 0 && !used[cur])
                {
                    used[cur] = true;
                    loop.Add(kept[cur].a);
                    var inDir = kept[cur].b - kept[cur].a;
                    var endKey = (Q(kept[cur].b.x, eps), Q(kept[cur].b.y, eps));
                    int nextSeg = -1; double bestAng = double.MaxValue;
                    // several pieces continue from a vertex where parts touch: the rightmost turn keeps each loop simple
                    if (byStart.TryGetValue(endKey, out var next))
                        foreach (var cand in next)
                        {
                            if (used[cand]) continue;
                            var outDir = kept[cand].b - kept[cand].a;
                            double ang = Math.Atan2(inDir.Cross(outDir), inDir.Dot(outDir));
                            if (ang < bestAng) { bestAng = ang; nextSeg = cand; }
                        }
                    cur = nextSeg;
                }
                if (loop.Count >= 3) loops.Add(loop);
            }

            var result = new List<List<Vector2>>();
            foreach (var loop in loops)
            {
                var simplified = Simplify(loop, Math.Max(vertexDistance, eps));
                if (simplified.Count >= 3) result.Add(simplified.Select(v => new Vector2((float)v.x, (float)v.y)).ToList());
            }
            // outer boundaries first (largest area), as Unity lists paths
            return result.OrderByDescending(p => Math.Abs(SignedArea(p))).ToList();
        }

        static long Q(double v, double eps) => (long)Math.Round(v / (eps * 100));

        static List<Vector2d> Simplify(List<Vector2d> loop, double minDist)
        {
            var pts = new List<Vector2d>(loop);
            bool changed = true;
            while (changed && pts.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < pts.Count && pts.Count >= 3; i++)
                {
                    var prev = pts[(i - 1 + pts.Count) % pts.Count]; var p = pts[i]; var next = pts[(i + 1) % pts.Count];
                    var d1 = p - prev; var d2 = next - p;
                    bool collinear = Math.Abs(d1.Cross(d2)) <= 1e-9 * Math.Max(1e-12, d1.Length() * d2.Length()) && d1.Dot(d2) > 0;
                    if (collinear || (p - prev).Length() < minDist) { pts.RemoveAt(i); i--; changed = true; }
                }
            }
            return pts;
        }

        public static double SignedArea(IList<Vector2> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++) { var u = p[i]; var v = p[(i + 1) % p.Count]; a += (double)u.x * v.y - (double)v.x * u.y; }
            return a * 0.5;
        }

        static bool PointInPolygon(Vector2d[] poly, Vector2d p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        /// <summary>One simple polygon from an outer boundary (CCW) and the holes inside it (CW), joined by bridges (for ear clipping).</summary>
        public static List<Vector2> Bridge(List<Vector2> outer, List<List<Vector2>> holes)
        {
            var poly = new List<Vector2>(outer);
            foreach (var hole in holes.OrderByDescending(h => h.Max(v => v.x)))
            {
                int hi = 0; for (int i = 1; i < hole.Count; i++) if (hole[i].x > hole[hi].x) hi = i;
                var hp = hole[hi];
                // the nearest outer vertex to the right whose connection crosses no edge
                int best = -1; float bestD = float.MaxValue;
                for (int i = 0; i < poly.Count; i++)
                {
                    var q = poly[i];
                    if (q.x < hp.x) continue;
                    float dd = (q - hp).sqrMagnitude;
                    if (dd >= bestD || Crosses(poly, hole, hp, q)) continue;
                    best = i; bestD = dd;
                }
                if (best < 0) for (int i = 0; i < poly.Count; i++) { float dd = (poly[i] - hp).sqrMagnitude; if (dd < bestD) { bestD = dd; best = i; } }
                var merged = new List<Vector2>();
                merged.AddRange(poly.Take(best + 1));
                for (int k = 0; k <= hole.Count; k++) merged.Add(hole[(hi + k) % hole.Count]);
                merged.Add(poly[best]);
                merged.AddRange(poly.Skip(best + 1));
                poly = merged;
            }
            return poly;
        }

        /// <summary>Convex pieces (at most <paramref name="maxVerts"/> vertices) of a counter-clockwise polygon that may touch itself at
        /// bridge vertices: ear clipping that ignores points coinciding with the ear's corners, then Hertel–Mehlhorn merging.</summary>
        public static List<List<Vector2>> ConvexPieces(List<Vector2> poly, int maxVerts = 8)
        {
            var tris = new List<List<Vector2>>();
            var v = new List<Vector2>(poly);
            if (SignedArea(v) < 0) v.Reverse();
            int guard = v.Count * v.Count + 8;
            while (v.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < v.Count; i++)
                {
                    Vector2 a = v[(i - 1 + v.Count) % v.Count], b = v[i], c = v[(i + 1) % v.Count];
                    float cr = Cross(b - a, c - b);
                    if (cr <= 1e-9f) { if (Mathf.Abs(cr) <= 1e-9f && (b - a).sqrMagnitude * (c - b).sqrMagnitude < 1e-18f) { v.RemoveAt(i); clipped = true; break; } continue; }
                    bool empty = true;
                    for (int k = 0; k < v.Count && empty; k++)
                    {
                        var p = v[k];
                        if ((p - a).sqrMagnitude < 1e-12f || (p - b).sqrMagnitude < 1e-12f || (p - c).sqrMagnitude < 1e-12f) continue;
                        if (Cross(b - a, p - a) > 1e-9f && Cross(c - b, p - b) > 1e-9f && Cross(a - c, p - c) > 1e-9f) empty = false;
                    }
                    if (!empty) continue;
                    tris.Add(new List<Vector2> { a, b, c });
                    v.RemoveAt(i); clipped = true; break;
                }
                if (!clipped)
                {
                    // degenerate leftovers (collinear runs): drop the flattest vertex
                    int flat = 0; float best = float.MaxValue;
                    for (int i = 0; i < v.Count; i++) { float cr = Mathf.Abs(Cross(v[i] - v[(i - 1 + v.Count) % v.Count], v[(i + 1) % v.Count] - v[i])); if (cr < best) { best = cr; flat = i; } }
                    v.RemoveAt(flat);
                }
            }
            if (v.Count == 3 && Cross(v[1] - v[0], v[2] - v[1]) > 1e-9f) tris.Add(v);
            // merge neighbours across a shared edge while the result stays convex and small enough
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < tris.Count && !merged; i++)
                    for (int j = i + 1; j < tris.Count && !merged; j++)
                        if (MergeConvex(tris[i], tris[j], maxVerts) is List<Vector2> m) { tris[i] = m; tris.RemoveAt(j); merged = true; }
            }
            return tris;
        }

        static List<Vector2> MergeConvex(List<Vector2> a, List<Vector2> b, int maxVerts)
        {
            if (a.Count + b.Count - 2 > maxVerts) return null;
            for (int i = 0; i < a.Count; i++)
            {
                Vector2 p = a[i], q = a[(i + 1) % a.Count];
                for (int j = 0; j < b.Count; j++)
                {
                    if ((b[j] - q).sqrMagnitude > 1e-12f || (b[(j + 1) % b.Count] - p).sqrMagnitude > 1e-12f) continue;
                    // a: … p q …, b: … q p …  →  a up to p, then b after p around to q
                    var m = new List<Vector2>();
                    for (int k = 0; k < a.Count; k++) m.Add(a[(i + 1 + k) % a.Count]);           // q … p
                    for (int k = 2; k < b.Count; k++) m.Add(b[(j + k) % b.Count]);                // after p … before q
                    for (int k = 0; k < m.Count; k++)
                        if (Cross(m[(k + 1) % m.Count] - m[k], m[(k + 2) % m.Count] - m[(k + 1) % m.Count]) < -1e-9f) return null;
                    // drop collinear points
                    for (int k = m.Count - 1; k >= 0 && m.Count > 3; k--)
                        if (Mathf.Abs(Cross(m[k] - m[(k - 1 + m.Count) % m.Count], m[(k + 1) % m.Count] - m[k])) <= 1e-9f) m.RemoveAt(k);
                    return m.Count <= maxVerts ? m : null;
                }
            }
            return null;
        }

        static bool Crosses(List<Vector2> poly, List<Vector2> hole, Vector2 a, Vector2 b)
        {
            bool Seg(Vector2 p, Vector2 q)
            {
                if (p == a || p == b || q == a || q == b) return false;
                float d1 = Cross(b - a, p - a), d2 = Cross(b - a, q - a), d3 = Cross(q - p, a - p), d4 = Cross(q - p, b - p);
                return d1 * d2 < 0 && d3 * d4 < 0;
            }
            for (int i = 0; i < poly.Count; i++) if (Seg(poly[i], poly[(i + 1) % poly.Count])) return true;
            for (int i = 0; i < hole.Count; i++) if (Seg(hole[i], hole[(i + 1) % hole.Count])) return true;
            return false;
        }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        readonly struct Vector2d
        {
            public readonly double x, y;
            public Vector2d(double x, double y) { this.x = x; this.y = y; }
            public static Vector2d operator +(Vector2d a, Vector2d b) => new(a.x + b.x, a.y + b.y);
            public static Vector2d operator -(Vector2d a, Vector2d b) => new(a.x - b.x, a.y - b.y);
            public static Vector2d operator *(Vector2d a, double s) => new(a.x * s, a.y * s);
            public double Dot(Vector2d b) => x * b.x + y * b.y;
            public double Cross(Vector2d b) => x * b.y - y * b.x;
            public double Length() => Math.Sqrt(x * x + y * y);
        }
    }
}
