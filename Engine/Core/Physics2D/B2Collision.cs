// 2D collision for MiniUnity's Physics2D, ported from Box2D v2.4.1 (the engine Unity's 2D physics is built on):
// shapes, contact manifolds (b2_collide_circle / b2_collide_polygon / b2_collide_edge), world manifolds, ray casts, GJK distance.
//
// Box2D — MIT License
// Copyright (c) 2019 Erin Catto
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy,
// modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software
// is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
// ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniUnity.Box2D
{
    public struct Rot
    {
        public float s, c;
        public Rot(float angle) { s = MathF.Sin(angle); c = MathF.Cos(angle); }
        public static readonly Rot Identity = new() { s = 0, c = 1 };
        public Vector2 Mul(Vector2 v) => new(c * v.x - s * v.y, s * v.x + c * v.y);
        public Vector2 MulT(Vector2 v) => new(c * v.x + s * v.y, -s * v.x + c * v.y);
        public static Rot MulT(Rot q, Rot r) => new() { s = q.c * r.s - q.s * r.c, c = q.c * r.c + q.s * r.s };
        public static Rot Mul(Rot q, Rot r) => new() { s = q.s * r.c + q.c * r.s, c = q.c * r.c - q.s * r.s };
        public float Angle => MathF.Atan2(s, c);
    }

    public struct Xf
    {
        public Vector2 p; public Rot q;
        public Xf(Vector2 p, float angle) { this.p = p; q = new Rot(angle); }
        public Xf(Vector2 p, Rot q) { this.p = p; this.q = q; }
        public static readonly Xf Identity = new() { p = Vector2.zero, q = Rot.Identity };
        public Vector2 Mul(Vector2 v) => q.Mul(v) + p;
        public Vector2 MulT(Vector2 v) => q.MulT(v - p);
        /// <summary>A⁻¹·B</summary>
        public static Xf MulT(Xf A, Xf B) => new(A.q.MulT(B.p - A.p), Rot.MulT(A.q, B.q));
        public static Xf Mul(Xf A, Xf B) => new(A.q.Mul(B.p) + A.p, Rot.Mul(A.q, B.q));
    }

    public static class B2
    {
        public const float LinearSlop = 0.005f, AngularSlop = 2f / 180f * MathF.PI, PolygonRadius = 2 * LinearSlop, AabbExtension = 0.1f;
        public const float Epsilon = 1.1920929e-7f;
        public const int MaxPolygonVertices = 8;
        public static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        public static Vector2 Cross(Vector2 a, float s) => new(s * a.y, -s * a.x);
        public static Vector2 Cross(float s, Vector2 a) => new(-s * a.y, s * a.x);
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static Vector2 Normalize(Vector2 v) { float l = MathF.Sqrt(v.x * v.x + v.y * v.y); return l < Epsilon ? Vector2.zero : v / l; }
    }

    public struct AABB2
    {
        public Vector2 lo, hi;
        public bool Overlaps(AABB2 o) => !(o.lo.x > hi.x || o.lo.y > hi.y || lo.x > o.hi.x || lo.y > o.hi.y);
        public AABB2 Fat(float m) => new() { lo = lo - new Vector2(m, m), hi = hi + new Vector2(m, m) };
        public static AABB2 Union(AABB2 a, AABB2 b) => new() { lo = Vector2.Min(a.lo, b.lo), hi = Vector2.Max(a.hi, b.hi) };
        public bool Contains(Vector2 p) => p.x >= lo.x && p.x <= hi.x && p.y >= lo.y && p.y <= hi.y;
    }

    public enum ShapeType { Circle = 0, Polygon = 1, Edge = 2 }   // contact registry order: A ranks at least as high as B

    /// <summary>A Box2D shape in body-local coordinates. A polygon with two vertices is a capsule (rounded segment).</summary>
    public sealed class Shape2
    {
        public ShapeType Type;
        public float Radius;
        public Vector2 P;                       // circle centre
        public Vector2[] V = Array.Empty<Vector2>(), N = Array.Empty<Vector2>();
        public int Count;
        public Vector2 Centroid;
        public Vector2 V0, V3; public bool OneSided;  // edge ghost vertices

        public bool IsCapsule => Type == ShapeType.Polygon && Count == 2;

        public static Shape2 Circle(Vector2 c, float r) => new() { Type = ShapeType.Circle, P = c, Radius = r, Centroid = c };

        public static Shape2 Edge(Vector2 a, Vector2 b, float radius) => new()
        { Type = ShapeType.Edge, V = new[] { a, b }, Count = 2, Radius = radius, Centroid = (a + b) * 0.5f, V0 = a, V3 = b };

        public static Shape2 Capsule(Vector2 a, Vector2 b, float r)
        {
            var n = B2.Normalize(B2.Cross(b - a, 1f));
            return new Shape2 { Type = ShapeType.Polygon, V = new[] { a, b }, N = new[] { n, -n }, Count = 2, Radius = r, Centroid = (a + b) * 0.5f };
        }

        public static Shape2 Box(float hx, float hy, Vector2 center, float angle, float radius)
        {
            var xf = new Xf(center, angle);
            var s = new Shape2 { Type = ShapeType.Polygon, Count = 4, Radius = radius, V = new Vector2[4], N = new Vector2[4], Centroid = center };
            Vector2[] v = { new(-hx, -hy), new(hx, -hy), new(hx, hy), new(-hx, hy) };
            Vector2[] n = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };
            for (int i = 0; i < 4; i++) { s.V[i] = xf.Mul(v[i]); s.N[i] = xf.q.Mul(n[i]); }
            return s;
        }

        /// <summary>b2PolygonShape::Set — convex hull (gift wrapping) of up to 8 points; null if degenerate.</summary>
        public static Shape2 Polygon(IList<Vector2> verts, float radius)
        {
            int n = Math.Min(verts.Count, B2.MaxPolygonVertices);
            var ps = new List<Vector2>();
            for (int i = 0; i < n; i++)
            {
                bool unique = true;
                foreach (var q in ps) if ((verts[i] - q).sqrMagnitude < 0.25f * B2.LinearSlop * B2.LinearSlop) { unique = false; break; }
                if (unique) ps.Add(verts[i]);
            }
            n = ps.Count;
            if (n < 3) return null;
            int i0 = 0; float x0 = ps[0].x;
            for (int i = 1; i < n; i++) { float x = ps[i].x; if (x > x0 || (x == x0 && ps[i].y < ps[i0].y)) { i0 = i; x0 = x; } }
            var hull = new List<int>(); int ih = i0;
            for (int guard = 0; guard < 64; guard++)
            {
                hull.Add(ih);
                int ie = 0;
                for (int j = 1; j < n; j++)
                {
                    if (ie == ih) { ie = j; continue; }
                    var r = ps[ie] - ps[hull[^1]]; var v = ps[j] - ps[hull[^1]];
                    float c = B2.Cross(r, v);
                    if (c < 0) ie = j;
                    if (c == 0 && v.sqrMagnitude > r.sqrMagnitude) ie = j;
                }
                ih = ie;
                if (ie == i0) break;
            }
            if (hull.Count < 3) return null;
            var s = new Shape2 { Type = ShapeType.Polygon, Count = hull.Count, Radius = radius, V = new Vector2[hull.Count], N = new Vector2[hull.Count] };
            for (int i = 0; i < hull.Count; i++) s.V[i] = ps[hull[i]];
            for (int i = 0; i < hull.Count; i++) s.N[i] = B2.Normalize(B2.Cross(s.V[(i + 1) % hull.Count] - s.V[i], 1f));
            s.Centroid = ComputeCentroid(s.V);
            return s;
        }

        static Vector2 ComputeCentroid(Vector2[] vs)
        {
            var c = Vector2.zero; float area = 0; var s = vs[0];
            for (int i = 0; i < vs.Length; i++)
            {
                var e1 = vs[i] - s; var e2 = (i + 1 < vs.Length ? vs[i + 1] : vs[0]) - s;
                float a = 0.5f * B2.Cross(e1, e2);
                c += a * (1f / 3f) * (e1 + e2); area += a;
            }
            return area > B2.Epsilon ? c / area + s : s;
        }

        public void ComputeMass(float density, out float mass, out Vector2 center, out float I)
        {
            switch (Type)
            {
                case ShapeType.Circle:
                    mass = density * MathF.PI * Radius * Radius; center = P;
                    I = mass * (0.5f * Radius * Radius + B2.Dot(P, P));
                    return;
                case ShapeType.Edge:
                    mass = 0; center = Centroid; I = 0; return;
            }
            if (Count == 2)
            {
                // capsule: rectangle + two half discs
                float r = Radius; var a = V[0]; var b = V[1]; float len = (b - a).magnitude;
                float boxMass = density * 2 * r * len, circleMass = density * MathF.PI * r * r;
                mass = boxMass + circleMass; center = (a + b) * 0.5f;
                float lc = 4 * r / (3 * MathF.PI), h = 0.5f * len;
                float circleI = circleMass * (0.5f * r * r + h * h + 2 * h * lc);
                float boxI = boxMass * (4 * r * r + len * len) / 12f;
                I = circleI + boxI + mass * B2.Dot(center, center);
                return;
            }
            // b2PolygonShape::ComputeMass (core polygon, radius ignored like Box2D)
            var cen = Vector2.zero; float area = 0, inertia = 0; var s0 = V[0];
            for (int i = 0; i < Count; i++)
            {
                var e1 = V[i] - s0; var e2 = (i + 1 < Count ? V[i + 1] : V[0]) - s0;
                float D = B2.Cross(e1, e2), tri = 0.5f * D;
                area += tri; cen += tri * (1f / 3f) * (e1 + e2);
                float intx2 = e1.x * e1.x + e2.x * e1.x + e2.x * e2.x, inty2 = e1.y * e1.y + e2.y * e1.y + e2.y * e2.y;
                inertia += 0.25f * (1f / 3f) * D * (intx2 + inty2);
            }
            mass = density * area;
            cen *= 1f / Math.Max(area, B2.Epsilon);
            center = cen + s0;
            I = density * inertia + mass * (B2.Dot(center, center) - B2.Dot(cen, cen));
        }

        public AABB2 ComputeAABB(Xf xf)
        {
            Vector2 lo, hi;
            if (Type == ShapeType.Circle) { lo = hi = xf.Mul(P); }
            else
            {
                lo = hi = xf.Mul(V[0]);
                for (int i = 1; i < Count; i++) { var v = xf.Mul(V[i]); lo = Vector2.Min(lo, v); hi = Vector2.Max(hi, v); }
            }
            var r = new Vector2(Radius, Radius);
            return new AABB2 { lo = lo - r, hi = hi + r };
        }

        public bool TestPoint(Xf xf, Vector2 p)
        {
            var l = xf.MulT(p);
            switch (Type)
            {
                case ShapeType.Circle: return (l - P).sqrMagnitude <= Radius * Radius;
                case ShapeType.Edge: return false;
            }
            if (Count == 2) { var c = Geo2.ClosestOnSegment(l, V[0], V[1]); return (l - c).sqrMagnitude <= Radius * Radius; }
            for (int i = 0; i < Count; i++) if (B2.Dot(N[i], l - V[i]) > 0) return false;
            return true;
        }

        /// <summary>Ray p1→p2, hit fraction in [0, maxFraction]. Polygons are cast against their core shape (like Box2D).</summary>
        public bool RayCast(Vector2 p1w, Vector2 p2w, float maxFraction, Xf xf, out float fraction, out Vector2 normal)
        {
            fraction = 0; normal = default;
            var p1 = xf.MulT(p1w); var p2 = xf.MulT(p2w); var d = p2 - p1;
            switch (Type)
            {
                case ShapeType.Circle:
                {
                    if (!Geo2.RayCircle(p1, d, P, Radius, maxFraction, out fraction, out var n)) return false;
                    normal = xf.q.Mul(n); return true;
                }
                case ShapeType.Edge:
                {
                    var v1 = V[0]; var v2 = V[1]; var e = v2 - v1;
                    var nn = B2.Normalize(new Vector2(e.y, -e.x));
                    float numerator = B2.Dot(nn, v1 - p1);
                    if (OneSided && numerator > 0) return false;
                    float denominator = B2.Dot(nn, d);
                    if (denominator == 0) return false;
                    float t = numerator / denominator;
                    if (t < 0 || maxFraction < t) return false;
                    var q = p1 + t * d; float rr = B2.Dot(e, e);
                    if (rr == 0) return false;
                    float s = B2.Dot(q - v1, e) / rr;
                    if (s < 0 || 1 < s) return false;
                    fraction = t; normal = xf.q.Mul(numerator > 0 ? -nn : nn);
                    return true;
                }
            }
            if (Count == 2)
            {
                if (!Geo2.RayCapsule(p1, d, V[0], V[1], Radius, maxFraction, out fraction, out var n)) return false;
                normal = xf.q.Mul(n); return true;
            }
            float lower = 0, upper = maxFraction; int index = -1;
            for (int i = 0; i < Count; i++)
            {
                float num = B2.Dot(N[i], V[i] - p1), den = B2.Dot(N[i], d);
                if (den == 0) { if (num < 0) return false; }
                else if (den < 0 && num < lower * den) { lower = num / den; index = i; }
                else if (den > 0 && num < upper * den) upper = num / den;
                if (upper < lower) return false;
            }
            if (index < 0) return false;
            fraction = lower; normal = xf.q.Mul(N[index]);
            return true;
        }

        /// <summary>Core vertices for GJK (circle: its centre).</summary>
        public Vector2[] CorePoints => Type == ShapeType.Circle ? new[] { P } : V;

        public Shape2 Transformed(Xf xf)
        {
            var s = (Shape2)MemberwiseClone();
            s.P = xf.Mul(P); s.Centroid = xf.Mul(Centroid); s.V0 = xf.Mul(V0); s.V3 = xf.Mul(V3);
            s.V = new Vector2[V.Length]; s.N = new Vector2[N.Length];
            for (int i = 0; i < V.Length; i++) s.V[i] = xf.Mul(V[i]);
            for (int i = 0; i < N.Length; i++) s.N[i] = xf.q.Mul(N[i]);
            return s;
        }
    }

    public static class Geo2
    {
        public static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var e = b - a; float l = B2.Dot(e, e);
            if (l < 1e-12f) return a;
            float t = Math.Clamp(B2.Dot(p - a, e) / l, 0, 1);
            return a + e * t;
        }

        public static bool RayCircle(Vector2 p1, Vector2 d, Vector2 c, float r, float maxFraction, out float fraction, out Vector2 normal)
        {
            fraction = 0; normal = default;
            var s = p1 - c; float b = B2.Dot(s, s) - r * r;
            float cc = B2.Dot(s, d), rr = B2.Dot(d, d), sigma = cc * cc - rr * b;
            if (sigma < 0 || rr < B2.Epsilon) return false;
            float a = -(cc + MathF.Sqrt(sigma));
            if (0 <= a && a <= maxFraction * rr)
            {
                a /= rr; fraction = a; normal = B2.Normalize(s + a * d);
                return true;
            }
            return false;
        }

        public static bool RayCapsule(Vector2 p1, Vector2 d, Vector2 a, Vector2 b, float r, float maxFraction, out float fraction, out Vector2 normal)
        {
            fraction = float.MaxValue; normal = default; bool hit = false;
            if (RayCircle(p1, d, a, r, maxFraction, out float f, out var n) && f < fraction) { fraction = f; normal = n; hit = true; }
            if (RayCircle(p1, d, b, r, maxFraction, out f, out n) && f < fraction) { fraction = f; normal = n; hit = true; }
            var e = b - a; float len = e.magnitude;
            if (len > 1e-6f)
            {
                var t = e / len; var sn = new Vector2(t.y, -t.x);
                for (int side = 0; side < 2; side++)
                {
                    var nn = side == 0 ? sn : -sn; var off = nn * r;
                    float den = B2.Dot(nn, d);
                    if (den >= 0) continue;
                    float tt = B2.Dot(nn, a + off - p1) / den;
                    if (tt < 0 || tt > maxFraction || tt >= fraction) continue;
                    float u = B2.Dot(p1 + d * tt - a, t);
                    if (u < 0 || u > len) continue;
                    fraction = tt; normal = nn; hit = true;
                }
            }
            return hit;
        }
    }

    // ------------------------------------------------------------------------------------------------ manifolds
    public enum ManifoldType { Circles, FaceA, FaceB }

    public struct ManifoldPoint { public Vector2 LocalPoint; public float NormalImpulse, TangentImpulse; public uint Id; }

    public sealed class Manifold
    {
        public ManifoldPoint[] Points = new ManifoldPoint[2];
        public Vector2 LocalNormal, LocalPoint;
        public ManifoldType Type;
        public int PointCount;
        public void CopyFrom(Manifold o) { Points[0] = o.Points[0]; Points[1] = o.Points[1]; LocalNormal = o.LocalNormal; LocalPoint = o.LocalPoint; Type = o.Type; PointCount = o.PointCount; }
    }

    public struct WorldManifold
    {
        public Vector2 Normal, P0, P1; public float S0, S1;
        public Vector2 Point(int i) => i == 0 ? P0 : P1;
        public float Separation(int i) => i == 0 ? S0 : S1;

        public static WorldManifold Of(Manifold m, Xf xfA, float rA, Xf xfB, float rB)
        {
            var w = new WorldManifold();
            if (m.PointCount == 0) return w;
            switch (m.Type)
            {
                case ManifoldType.Circles:
                {
                    w.Normal = new Vector2(1, 0);
                    var pA = xfA.Mul(m.LocalPoint); var pB = xfB.Mul(m.Points[0].LocalPoint);
                    if ((pB - pA).sqrMagnitude > B2.Epsilon * B2.Epsilon) w.Normal = B2.Normalize(pB - pA);
                    var cA = pA + rA * w.Normal; var cB = pB - rB * w.Normal;
                    w.P0 = 0.5f * (cA + cB); w.S0 = B2.Dot(cB - cA, w.Normal);
                    break;
                }
                case ManifoldType.FaceA:
                {
                    w.Normal = xfA.q.Mul(m.LocalNormal);
                    var plane = xfA.Mul(m.LocalPoint);
                    for (int i = 0; i < m.PointCount; i++)
                    {
                        var clip = xfB.Mul(m.Points[i].LocalPoint);
                        var cA = clip + (rA - B2.Dot(clip - plane, w.Normal)) * w.Normal; var cB = clip - rB * w.Normal;
                        var p = 0.5f * (cA + cB); float s = B2.Dot(cB - cA, w.Normal);
                        if (i == 0) { w.P0 = p; w.S0 = s; } else { w.P1 = p; w.S1 = s; }
                    }
                    break;
                }
                default:
                {
                    w.Normal = xfB.q.Mul(m.LocalNormal);
                    var plane = xfB.Mul(m.LocalPoint);
                    for (int i = 0; i < m.PointCount; i++)
                    {
                        var clip = xfA.Mul(m.Points[i].LocalPoint);
                        var cB = clip + (rB - B2.Dot(clip - plane, w.Normal)) * w.Normal; var cA = clip - rA * w.Normal;
                        var p = 0.5f * (cA + cB); float s = B2.Dot(cA - cB, w.Normal);
                        if (i == 0) { w.P0 = p; w.S0 = s; } else { w.P1 = p; w.S1 = s; }
                    }
                    w.Normal = -w.Normal;
                    break;
                }
            }
            return w;
        }
    }

    public static class Collide2
    {
        // contact feature id: indexA | indexB << 8 | typeA << 16 | typeB << 24 (type: 0 vertex, 1 face)
        static uint Id(int indexA, int indexB, int typeA, int typeB) => (uint)(indexA & 0xFF) | (uint)(indexB & 0xFF) << 8 | (uint)typeA << 16 | (uint)typeB << 24;
        static uint Flip(uint id) => Id((int)(id >> 8) & 0xFF, (int)id & 0xFF, (int)(id >> 24) & 0xFF, (int)(id >> 16) & 0xFF);
        static int IndexB(uint id) => (int)(id >> 8) & 0xFF;

        struct ClipVertex { public Vector2 v; public uint id; }

        /// <summary>Contact registry order: shape A must rank at least as high as B (edge &gt; polygon &gt; circle), like b2Contact::Create.</summary>
        public static bool NeedsSwap(Shape2 a, Shape2 b) => (int)a.Type < (int)b.Type && !(a.Type == ShapeType.Circle && b.Type == ShapeType.Circle);

        public static bool CanCollide(Shape2 a, Shape2 b) => !(a.Type == ShapeType.Edge && b.Type == ShapeType.Edge);

        public static void Collide(Manifold m, Shape2 A, Xf xfA, Shape2 B, Xf xfB)
        {
            m.PointCount = 0;
            switch (A.Type, B.Type)
            {
                case (ShapeType.Circle, ShapeType.Circle): Circles(m, A, xfA, B, xfB); break;
                case (ShapeType.Polygon, ShapeType.Circle): PolygonCircle(m, A, xfA, B, xfB); break;
                case (ShapeType.Polygon, ShapeType.Polygon):
                    if ((A.IsCapsule || B.IsCapsule) && RoundedVertexCase(m, A, xfA, B, xfB)) break;
                    Polygons(m, A, xfA, B, xfB); break;
                case (ShapeType.Edge, ShapeType.Circle): EdgeCircle(m, A, xfA, B, xfB); break;
                case (ShapeType.Edge, ShapeType.Polygon):
                    if (B.IsCapsule && RoundedVertexCase(m, A, xfA, B, xfB)) break;
                    EdgePolygon(m, A, xfA, B, xfB); break;
            }
        }

        static void Circles(Manifold m, Shape2 A, Xf xfA, Shape2 B, Xf xfB)
        {
            var pA = xfA.Mul(A.P); var pB = xfB.Mul(B.P); var d = pB - pA;
            float r = A.Radius + B.Radius;
            if (B2.Dot(d, d) > r * r) return;
            m.Type = ManifoldType.Circles; m.LocalPoint = A.P; m.LocalNormal = Vector2.zero; m.PointCount = 1;
            m.Points[0].LocalPoint = B.P; m.Points[0].Id = 0;
        }

        static void PolygonCircle(Manifold m, Shape2 A, Xf xfA, Shape2 B, Xf xfB)
        {
            var c = xfB.Mul(B.P); var cl = xfA.MulT(c);
            int normalIndex = 0; float separation = float.MinValue; float radius = A.Radius + B.Radius;
            for (int i = 0; i < A.Count; i++)
            {
                float s = B2.Dot(A.N[i], cl - A.V[i]);
                if (s > radius) return;
                if (s > separation) { separation = s; normalIndex = i; }
            }
            int i1 = normalIndex, i2 = i1 + 1 < A.Count ? i1 + 1 : 0;
            var v1 = A.V[i1]; var v2 = A.V[i2];
            m.Points[0].LocalPoint = B.P; m.Points[0].Id = 0; m.Type = ManifoldType.FaceA;
            if (separation < B2.Epsilon) { m.PointCount = 1; m.LocalNormal = A.N[normalIndex]; m.LocalPoint = 0.5f * (v1 + v2); return; }
            float u1 = B2.Dot(cl - v1, v2 - v1), u2 = B2.Dot(cl - v2, v1 - v2);
            if (u1 <= 0)
            {
                if ((cl - v1).sqrMagnitude > radius * radius) return;
                m.PointCount = 1; m.LocalNormal = B2.Normalize(cl - v1); m.LocalPoint = v1;
            }
            else if (u2 <= 0)
            {
                if ((cl - v2).sqrMagnitude > radius * radius) return;
                m.PointCount = 1; m.LocalNormal = B2.Normalize(cl - v2); m.LocalPoint = v2;
            }
            else
            {
                var fc = 0.5f * (v1 + v2);
                if (B2.Dot(cl - fc, A.N[i1]) > radius) return;
                m.PointCount = 1; m.LocalNormal = A.N[i1]; m.LocalPoint = fc;
            }
        }

        static float FindMaxSeparation(out int edgeIndex, Shape2 p1, Xf xf1, Shape2 p2, Xf xf2)
        {
            var xf = Xf.MulT(xf2, xf1);
            int best = 0; float maxSep = float.MinValue;
            for (int i = 0; i < p1.Count; i++)
            {
                var n = xf.q.Mul(p1.N[i]); var v1 = xf.Mul(p1.V[i]);
                float si = float.MaxValue;
                for (int j = 0; j < p2.Count; j++) { float sij = B2.Dot(n, p2.V[j] - v1); if (sij < si) si = sij; }
                if (si > maxSep) { maxSep = si; best = i; }
            }
            edgeIndex = best; return maxSep;
        }

        static void FindIncidentEdge(ClipVertex[] c, Shape2 p1, Xf xf1, int edge1, Shape2 p2, Xf xf2)
        {
            var normal1 = xf2.q.MulT(xf1.q.Mul(p1.N[edge1]));
            int index = 0; float minDot = float.MaxValue;
            for (int i = 0; i < p2.Count; i++) { float dot = B2.Dot(normal1, p2.N[i]); if (dot < minDot) { minDot = dot; index = i; } }
            int i1 = index, i2 = i1 + 1 < p2.Count ? i1 + 1 : 0;
            c[0] = new ClipVertex { v = xf2.Mul(p2.V[i1]), id = Id(edge1, i1, 1, 0) };
            c[1] = new ClipVertex { v = xf2.Mul(p2.V[i2]), id = Id(edge1, i2, 1, 0) };
        }

        static int ClipSegmentToLine(ClipVertex[] vOut, ClipVertex[] vIn, Vector2 normal, float offset, int vertexIndexA)
        {
            int count = 0;
            float d0 = B2.Dot(normal, vIn[0].v) - offset, d1 = B2.Dot(normal, vIn[1].v) - offset;
            if (d0 <= 0) vOut[count++] = vIn[0];
            if (d1 <= 0) vOut[count++] = vIn[1];
            if (d0 * d1 < 0)
            {
                float interp = d0 / (d0 - d1);
                vOut[count++] = new ClipVertex { v = vIn[0].v + interp * (vIn[1].v - vIn[0].v), id = Id(vertexIndexA, IndexB(vIn[0].id), 0, 1) };
            }
            return count;
        }

        static void Polygons(Manifold m, Shape2 A, Xf xfA, Shape2 B, Xf xfB)
        {
            float totalRadius = A.Radius + B.Radius;
            float sepA = FindMaxSeparation(out int edgeA, A, xfA, B, xfB);
            if (sepA > totalRadius) return;
            float sepB = FindMaxSeparation(out int edgeB, B, xfB, A, xfA);
            if (sepB > totalRadius) return;
            Shape2 poly1, poly2; Xf xf1, xf2; int edge1; bool flip;
            const float kTol = 0.1f * B2.LinearSlop;
            if (sepB > sepA + kTol) { poly1 = B; poly2 = A; xf1 = xfB; xf2 = xfA; edge1 = edgeB; m.Type = ManifoldType.FaceB; flip = true; }
            else { poly1 = A; poly2 = B; xf1 = xfA; xf2 = xfB; edge1 = edgeA; m.Type = ManifoldType.FaceA; flip = false; }
            var incident = new ClipVertex[2];
            FindIncidentEdge(incident, poly1, xf1, edge1, poly2, xf2);
            int iv1 = edge1, iv2 = edge1 + 1 < poly1.Count ? edge1 + 1 : 0;
            var v11 = poly1.V[iv1]; var v12 = poly1.V[iv2];
            var localTangent = B2.Normalize(v12 - v11);
            var localNormal = B2.Cross(localTangent, 1f);
            var planePoint = 0.5f * (v11 + v12);
            var tangent = xf1.q.Mul(localTangent); var normal = B2.Cross(tangent, 1f);
            v11 = xf1.Mul(v11); v12 = xf1.Mul(v12);
            float frontOffset = B2.Dot(normal, v11);
            float side1 = -B2.Dot(tangent, v11) + totalRadius, side2 = B2.Dot(tangent, v12) + totalRadius;
            var clip1 = new ClipVertex[2]; var clip2 = new ClipVertex[2];
            if (ClipSegmentToLine(clip1, incident, -tangent, side1, iv1) < 2) return;
            if (ClipSegmentToLine(clip2, clip1, tangent, side2, iv2) < 2) return;
            m.LocalNormal = localNormal; m.LocalPoint = planePoint;
            int pc = 0;
            for (int i = 0; i < 2; i++)
            {
                float sep = B2.Dot(normal, clip2[i].v) - frontOffset;
                if (sep <= totalRadius)
                {
                    m.Points[pc].LocalPoint = xf2.MulT(clip2[i].v);
                    m.Points[pc].Id = flip ? Flip(clip2[i].id) : clip2[i].id;
                    pc++;
                }
            }
            m.PointCount = pc;
        }

        static void EdgeCircle(Manifold m, Shape2 E, Xf xfA, Shape2 C, Xf xfB)
        {
            var Q = xfA.MulT(xfB.Mul(C.P));
            var A = E.V[0]; var B = E.V[1]; var e = B - A;
            var n = new Vector2(e.y, -e.x);
            float offset = B2.Dot(n, Q - A);
            if (E.OneSided && offset < 0) return;
            float u = B2.Dot(e, B - Q), v = B2.Dot(e, Q - A);
            float radius = E.Radius + C.Radius;
            m.Points[0].LocalPoint = C.P;
            if (v <= 0)
            {
                if ((Q - A).sqrMagnitude > radius * radius) return;
                if (E.OneSided && B2.Dot(A - E.V0, A - Q) > 0) return;
                m.PointCount = 1; m.Type = ManifoldType.Circles; m.LocalNormal = Vector2.zero; m.LocalPoint = A; m.Points[0].Id = Id(0, 0, 0, 0);
                return;
            }
            if (u <= 0)
            {
                if ((Q - B).sqrMagnitude > radius * radius) return;
                if (E.OneSided && B2.Dot(E.V3 - B, Q - B) > 0) return;
                m.PointCount = 1; m.Type = ManifoldType.Circles; m.LocalNormal = Vector2.zero; m.LocalPoint = B; m.Points[0].Id = Id(1, 0, 0, 0);
                return;
            }
            float den = B2.Dot(e, e);
            var P = (1f / den) * (u * A + v * B);
            if ((Q - P).sqrMagnitude > radius * radius) return;
            if (offset < 0) n = -n;
            m.PointCount = 1; m.Type = ManifoldType.FaceA; m.LocalNormal = B2.Normalize(n); m.LocalPoint = A; m.Points[0].Id = Id(0, 0, 1, 0);
        }

        struct EPAxis { public int Type; public int Index; public float Separation; public Vector2 Normal; }   // Type: 1 edgeA, 2 edgeB

        static void EdgePolygon(Manifold m, Shape2 E, Xf xfA, Shape2 P, Xf xfB)
        {
            var xf = Xf.MulT(xfA, xfB);
            var centroidB = xf.Mul(P.Centroid);
            var v1 = E.V[0]; var v2 = E.V[1];
            var edge1 = B2.Normalize(v2 - v1);
            var normal1 = new Vector2(edge1.y, -edge1.x);
            float offset1 = B2.Dot(normal1, centroidB - v1);
            if (E.OneSided && offset1 < 0) return;
            int count = P.Count;
            var pv = new Vector2[count]; var pn = new Vector2[count];
            for (int i = 0; i < count; i++) { pv[i] = xf.Mul(P.V[i]); pn[i] = xf.q.Mul(P.N[i]); }
            float radius = P.Radius + E.Radius;

            var edgeAxis = new EPAxis { Type = 1, Index = -1, Separation = float.MinValue };
            foreach (var (ax, j) in new[] { (normal1, 0), (-normal1, 1) })
            {
                float sj = float.MaxValue;
                for (int i = 0; i < count; i++) { float si = B2.Dot(ax, pv[i] - v1); if (si < sj) sj = si; }
                if (sj > edgeAxis.Separation) { edgeAxis.Index = j; edgeAxis.Separation = sj; edgeAxis.Normal = ax; }
            }
            if (edgeAxis.Separation > radius) return;
            var polyAxis = new EPAxis { Type = 0, Index = -1, Separation = float.MinValue };
            for (int i = 0; i < count; i++)
            {
                var n = -pn[i];
                float s = Math.Min(B2.Dot(n, pv[i] - v1), B2.Dot(n, pv[i] - v2));
                if (s > polyAxis.Separation) { polyAxis.Type = 2; polyAxis.Index = i; polyAxis.Separation = s; polyAxis.Normal = n; }
            }
            if (polyAxis.Separation > radius) return;
            const float kRelTol = 0.98f, kAbsTol = 0.001f;
            var primary = polyAxis.Separation - radius > kRelTol * (edgeAxis.Separation - radius) + kAbsTol ? polyAxis : edgeAxis;
            if (E.OneSided)
            {
                var edge0 = B2.Normalize(v1 - E.V0); var normal0 = new Vector2(edge0.y, -edge0.x);
                bool convex1 = B2.Cross(edge0, edge1) >= 0;
                var edge2 = B2.Normalize(E.V3 - v2); var normal2 = new Vector2(edge2.y, -edge2.x);
                bool convex2 = B2.Cross(edge1, edge2) >= 0;
                const float sinTol = 0.1f;
                bool side1 = B2.Dot(primary.Normal, edge1) <= 0;
                if (side1) { if (convex1) { if (B2.Cross(primary.Normal, normal0) > sinTol) return; } else primary = edgeAxis; }
                else { if (convex2) { if (B2.Cross(normal2, primary.Normal) > sinTol) return; } else primary = edgeAxis; }
            }
            var clip = new ClipVertex[2];
            int ri1, ri2; Vector2 rv1, rv2, rn, sn1, sn2;
            if (primary.Type == 1)
            {
                m.Type = ManifoldType.FaceA;
                int best = 0; float bestV = B2.Dot(primary.Normal, pn[0]);
                for (int i = 1; i < count; i++) { float val = B2.Dot(primary.Normal, pn[i]); if (val < bestV) { bestV = val; best = i; } }
                int i1 = best, i2 = i1 + 1 < count ? i1 + 1 : 0;
                clip[0] = new ClipVertex { v = pv[i1], id = Id(0, i1, 1, 0) };
                clip[1] = new ClipVertex { v = pv[i2], id = Id(0, i2, 1, 0) };
                ri1 = 0; ri2 = 1; rv1 = v1; rv2 = v2; rn = primary.Normal; sn1 = -edge1; sn2 = edge1;
            }
            else
            {
                m.Type = ManifoldType.FaceB;
                clip[0] = new ClipVertex { v = v2, id = Id(1, primary.Index, 0, 1) };
                clip[1] = new ClipVertex { v = v1, id = Id(0, primary.Index, 0, 1) };
                ri1 = primary.Index; ri2 = ri1 + 1 < count ? ri1 + 1 : 0;
                rv1 = pv[ri1]; rv2 = pv[ri2]; rn = pn[ri1];
                sn1 = new Vector2(rn.y, -rn.x); sn2 = -sn1;
            }
            float so1 = B2.Dot(sn1, rv1), so2 = B2.Dot(sn2, rv2);
            var c1 = new ClipVertex[2]; var c2 = new ClipVertex[2];
            if (ClipSegmentToLine(c1, clip, sn1, so1, ri1) < 2) return;
            if (ClipSegmentToLine(c2, c1, sn2, so2, ri2) < 2) return;
            if (primary.Type == 1) { m.LocalNormal = rn; m.LocalPoint = rv1; }
            else { m.LocalNormal = P.N[ri1]; m.LocalPoint = P.V[ri1]; }
            int pc = 0;
            for (int i = 0; i < 2; i++)
            {
                float sep = B2.Dot(rn, c2[i].v - rv1);
                if (sep > radius) continue;
                if (primary.Type == 1) { m.Points[pc].LocalPoint = xf.MulT(c2[i].v); m.Points[pc].Id = c2[i].id; }
                else { m.Points[pc].LocalPoint = c2[i].v; m.Points[pc].Id = Flip(c2[i].id); }
                pc++;
            }
            m.PointCount = pc;
        }

        /// <summary>
        /// Capsules (2-vertex polygons) separated in a vertex-vertex region: Box2D 2.4's SAT only tests face normals, which
        /// misjudges rounded ends meeting end-on, so use the closest points of the core segments (like Box2D v3's capsule manifolds).
        /// </summary>
        static bool RoundedVertexCase(Manifold m, Shape2 A, Xf xfA, Shape2 B, Xf xfB)
        {
            var d = Gjk2.Distance(A.CorePoints, A.Type == ShapeType.Circle ? 1 : A.Count, xfA, B.CorePoints, B.Type == ShapeType.Circle ? 1 : B.Count, xfB, out var pA, out var pB);
            float r = A.Radius + B.Radius;
            if (d < 10 * B2.Epsilon) return false;
            if (d > r) { m.PointCount = 0; return true; }
            var n = (pB - pA) / d;
            // face region (normal parallel to a face normal of either core) → regular clipping keeps two points
            float best = 0;
            for (int i = 0; i < A.Count; i++) best = Math.Max(best, B2.Dot(xfA.q.Mul(A.N[i]), n));
            if (B.Type != ShapeType.Circle) for (int i = 0; i < B.Count; i++) best = Math.Max(best, B2.Dot(xfB.q.Mul(B.N[i]), -n));
            if (A.Type == ShapeType.Edge)
            {
                var en = B2.Normalize(B2.Cross(xfA.q.Mul(A.V[1] - A.V[0]), 1f));
                best = Math.Max(best, Math.Abs(B2.Dot(en, n)));
            }
            if (best > 0.999f) return false;
            m.Type = ManifoldType.Circles; m.LocalPoint = xfA.MulT(pA); m.LocalNormal = Vector2.zero; m.PointCount = 1;
            m.Points[0].LocalPoint = xfB.MulT(pB); m.Points[0].Id = Id(255, 255, 0, 0);
            return true;
        }
    }

    // ------------------------------------------------------------------------------------------------ GJK (b2Distance)
    public static class Gjk2
    {
        struct SV { public Vector2 wA, wB, w; public float a; public int iA, iB; }

        static int Support(Vector2[] v, int n, Vector2 d)
        {
            int best = 0; float bv = B2.Dot(v[0], d);
            for (int i = 1; i < n; i++) { float x = B2.Dot(v[i], d); if (x > bv) { best = i; bv = x; } }
            return best;
        }

        /// <summary>Distance between the convex hulls of two point sets (core shapes); 0 when they overlap.</summary>
        public static float Distance(Vector2[] va, int na, Xf xfA, Vector2[] vb, int nb, Xf xfB, out Vector2 pA, out Vector2 pB)
        {
            var s = new SV[3]; int count = 1;
            s[0].iA = 0; s[0].iB = 0; s[0].wA = xfA.Mul(va[0]); s[0].wB = xfB.Mul(vb[0]); s[0].w = s[0].wB - s[0].wA; s[0].a = 1;
            var saveA = new int[3]; var saveB = new int[3];
            for (int iter = 0; iter < 20; iter++)
            {
                int saveCount = count;
                for (int i = 0; i < count; i++) { saveA[i] = s[i].iA; saveB[i] = s[i].iB; }
                if (count == 2) Solve2(s, ref count);
                else if (count == 3) Solve3(s, ref count);
                if (count == 3) break;
                Vector2 d;
                if (count == 1) d = -s[0].w;
                else
                {
                    var e12 = s[1].w - s[0].w; float sgn = B2.Cross(e12, -s[0].w);
                    d = sgn > 0 ? B2.Cross(1f, e12) : B2.Cross(e12, 1f);
                }
                if (B2.Dot(d, d) < B2.Epsilon * B2.Epsilon) break;
                ref var v = ref s[count];
                v.iA = Support(va, na, xfA.q.MulT(-d)); v.wA = xfA.Mul(va[v.iA]);
                v.iB = Support(vb, nb, xfB.q.MulT(d)); v.wB = xfB.Mul(vb[v.iB]);
                v.w = v.wB - v.wA;
                bool dup = false;
                for (int i = 0; i < saveCount; i++) if (v.iA == saveA[i] && v.iB == saveB[i]) { dup = true; break; }
                if (dup) break;
                count++;
            }
            switch (count)
            {
                case 1: pA = s[0].wA; pB = s[0].wB; break;
                case 2: pA = s[0].a * s[0].wA + s[1].a * s[1].wA; pB = s[0].a * s[0].wB + s[1].a * s[1].wB; break;
                default: pA = s[0].a * s[0].wA + s[1].a * s[1].wA + s[2].a * s[2].wA; pB = pA; break;
            }
            return (pB - pA).magnitude;
        }

        static void Solve2(SV[] s, ref int count)
        {
            var w1 = s[0].w; var w2 = s[1].w; var e12 = w2 - w1;
            float d12_2 = -B2.Dot(w1, e12);
            if (d12_2 <= 0) { s[0].a = 1; count = 1; return; }
            float d12_1 = B2.Dot(w2, e12);
            if (d12_1 <= 0) { s[1].a = 1; count = 1; s[0] = s[1]; return; }
            float inv = 1 / (d12_1 + d12_2);
            s[0].a = d12_1 * inv; s[1].a = d12_2 * inv; count = 2;
        }

        static void Solve3(SV[] s, ref int count)
        {
            var w1 = s[0].w; var w2 = s[1].w; var w3 = s[2].w;
            var e12 = w2 - w1; float d12_1 = B2.Dot(w2, e12), d12_2 = -B2.Dot(w1, e12);
            var e13 = w3 - w1; float d13_1 = B2.Dot(w3, e13), d13_2 = -B2.Dot(w1, e13);
            var e23 = w3 - w2; float d23_1 = B2.Dot(w3, e23), d23_2 = -B2.Dot(w2, e23);
            float n123 = B2.Cross(e12, e13);
            float d123_1 = n123 * B2.Cross(w2, w3), d123_2 = n123 * B2.Cross(w3, w1), d123_3 = n123 * B2.Cross(w1, w2);
            if (d12_2 <= 0 && d13_2 <= 0) { s[0].a = 1; count = 1; return; }
            if (d12_1 > 0 && d12_2 > 0 && d123_3 <= 0) { float inv = 1 / (d12_1 + d12_2); s[0].a = d12_1 * inv; s[1].a = d12_2 * inv; count = 2; return; }
            if (d13_1 > 0 && d13_2 > 0 && d123_2 <= 0) { float inv = 1 / (d13_1 + d13_2); s[0].a = d13_1 * inv; s[2].a = d13_2 * inv; count = 2; s[1] = s[2]; return; }
            if (d12_1 <= 0 && d23_2 <= 0) { s[1].a = 1; count = 1; s[0] = s[1]; return; }
            if (d13_1 <= 0 && d23_1 <= 0) { s[2].a = 1; count = 1; s[0] = s[2]; return; }
            if (d23_1 > 0 && d23_2 > 0 && d123_1 <= 0) { float inv = 1 / (d23_1 + d23_2); s[1].a = d23_1 * inv; s[2].a = d23_2 * inv; count = 2; s[0] = s[2]; return; }
            float inv3 = 1 / (d123_1 + d123_2 + d123_3);
            s[0].a = d123_1 * inv3; s[1].a = d123_2 * inv3; s[2].a = d123_3 * inv3; count = 3;
        }

        public static float ShapeDistance(Shape2 a, Xf xa, Shape2 b, Xf xb, out Vector2 pA, out Vector2 pB)
        {
            var A = a.CorePoints; var B = b.CorePoints;
            return Distance(A, A.Length, xa, B, B.Length, xb, out pA, out pB);
        }

        /// <summary>b2TestOverlap: distance including radii below 10ε.</summary>
        public static bool Overlap(Shape2 a, Xf xa, Shape2 b, Xf xb)
        {
            float d = ShapeDistance(a, xa, b, xb, out _, out _);
            return d - a.Radius - b.Radius < 10 * B2.Epsilon;
        }

        /// <summary>
        /// Linear cast of shape A by <paramref name="translation"/> against B (conservative advancement on the GJK distance).
        /// Returns the fraction of the translation, the contact point on B and B's surface normal. fraction 0 = initially overlapping.
        /// </summary>
        public static bool Cast(Shape2 a, Xf xa, Vector2 translation, Shape2 b, Xf xb, out float fraction, out Vector2 point, out Vector2 normal)
        {
            fraction = 0; point = default; normal = default;
            float R = a.Radius + b.Radius, t = 0;
            const float tol = 0.25f * B2.LinearSlop;
            for (int iter = 0; iter < 40; iter++)
            {
                var xt = new Xf(xa.p + translation * t, xa.q);
                float d = ShapeDistance(a, xt, b, xb, out var pA, out var pB);
                if (d < 10 * B2.Epsilon)
                {
                    if (t == 0) { point = pB; normal = B2.Normalize(-translation); return true; }
                    break;
                }
                var n = (pB - pA) / d;   // A → B
                float sep = d - R;
                if (sep < 0 && t == 0) { fraction = 0; point = pB - n * b.Radius; normal = -n; return true; }
                if (sep < tol) { fraction = t; point = pB - n * b.Radius; normal = -n; return true; }
                float vn = B2.Dot(translation, n);
                if (vn <= 0) return false;
                t += sep / vn;
                if (t > 1) return false;
            }
            // advancement stalled inside tolerance band
            {
                var xt = new Xf(xa.p + translation * t, xa.q);
                float d = ShapeDistance(a, xt, b, xb, out var pA, out var pB);
                var n = d > 1e-9f ? (pB - pA) / d : B2.Normalize(translation);
                fraction = t; point = pB - n * b.Radius; normal = -n;
                return t <= 1;
            }
        }
    }
}
