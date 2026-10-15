// Physics2D effectors: Area / Point / Buoyancy effectors apply forces to bodies overlapping their trigger colliders before the
// step; Platform (one-way) and Surface effectors modify contacts in PreSolve. Colliders take part when usedByEffector is set.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MiniUnity.Box2D
{
    static class EffectorUtil
    {
        public static T EffectorOf<T>(Collider2D c) where T : Effector2D
        {
            if (c == null || !c.usedByEffector) return null;
            foreach (var comp in c.gameObject.m_Components) if (comp is T e && !e.m_Destroyed && e.enabled) return e;
            return null;
        }

        public static bool Affects(Effector2D e, Collider2D other)
        {
            if (e.useColliderMask) return (e.colliderMask & (1 << other.gameObject.layer)) != 0;
            return Physics2DWorld.LayersCollide(e.gameObject.layer, other.gameObject.layer);
        }

        static readonly System.Random rng = new(12345);
        public static float Vary(float value, float variation) => variation == 0 ? value : value + (float)(rng.NextDouble() * 2 - 1) * variation;
    }

    public static class Effectors
    {
        /// <summary>Forces from Area / Point / Buoyancy effectors on the bodies their trigger colliders overlap.</summary>
        public static void Apply(float dt)
        {
            var w = Physics2DWorld.World;
            var done = new HashSet<(Effector2D, Collider2D)>();
            foreach (var c in w.Contacts.Values)
            {
                if (!c.Touching) continue;
                var ca = (Collider2D)c.A.Owner; var cb = (Collider2D)c.B.Owner;
                Try(ca, cb, c.B.Body, done); Try(cb, ca, c.A.Body, done);
            }
        }

        static void Try(Collider2D effCol, Collider2D other, Body2 body, HashSet<(Effector2D, Collider2D)> done)
        {
            if (!effCol.usedByEffector || body.Type != BodyType2.Dynamic || other.usedByEffector) return;
            var rb = body.Owner as Rigidbody2D; if (rb == null) return;
            foreach (var comp in effCol.gameObject.m_Components)
            {
                if (comp is not Effector2D e || e.m_Destroyed || !e.enabled || !EffectorUtil.Affects(e, other)) continue;
                if (!done.Add((e, other))) continue;
                switch (e)
                {
                    case AreaEffector2D a: Area(a, other, rb, body); break;
                    case PointEffector2D p: Point(p, effCol, other, rb, body); break;
                    case BuoyancyEffector2D bu: Buoyancy(bu, other, rb, body); break;
                }
            }
        }

        static void ApplyDrag(Body2 b, float drag, float angularDrag)
        {
            // effector drag acts like extra linear / angular damping for this step
            if (drag > 0) b.Force += -b.V * drag * b.Mass;
            if (angularDrag > 0) b.Torque += -b.W * angularDrag * b.I;
        }

        static void Area(AreaEffector2D a, Collider2D other, Rigidbody2D rb, Body2 b)
        {
            float ang = a.forceAngle * Mathf.Deg2Rad;
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            if (!a.useGlobalAngle) dir = a.transform.TransformDirection(dir);
            var f = dir.normalized * EffectorUtil.Vary(a.forceMagnitude, a.forceVariation);
            if (a.forceTarget == EffectorSelection2D.Collider) rb.AddForceAtPosition(f, other.bounds.center);
            else rb.AddForce(f);
            ApplyDrag(b, a.drag, a.angularDrag);
        }

        static void Point(PointEffector2D p, Collider2D effCol, Collider2D other, Rigidbody2D rb, Body2 b)
        {
            Vector2 src = p.forceSource == EffectorSelection2D.Collider ? (Vector2)effCol.bounds.center : effCol.attachedRigidbody != null ? effCol.attachedRigidbody.worldCenterOfMass : (Vector2)p.transform.position;
            Vector2 tgt = p.forceTarget == EffectorSelection2D.Collider ? (Vector2)other.bounds.center : b.C;
            var d = tgt - src; float dist = d.magnitude * p.distanceScale;
            if (dist < 1e-6f) return;
            float mag = EffectorUtil.Vary(p.forceMagnitude, p.forceVariation);
            if (p.forceMode == EffectorForceMode2D.InverseLinear) mag /= dist;
            else if (p.forceMode == EffectorForceMode2D.InverseSquared) mag /= dist * dist;
            var f = d.normalized * mag;
            if (p.forceTarget == EffectorSelection2D.Collider) rb.AddForceAtPosition(f, tgt); else rb.AddForce(f);
            ApplyDrag(b, p.drag, p.angularDrag);
        }

        static void Buoyancy(BuoyancyEffector2D bu, Collider2D other, Rigidbody2D rb, Body2 b)
        {
            float surface = bu.transform.position.y + bu.surfaceLevel * bu.transform.lossyScale.y;
            var bb = other.bounds;
            float h = bb.size.y; if (h <= 0) return;
            float submerged = Math.Clamp((surface - bb.min.y) / h, 0, 1);
            if (submerged <= 0) return;
            float area = 0;
            foreach (var f in Physics2DWorld.FixturesOf(other)) { f.Shape.ComputeMass(1, out float m, out _, out _); area += m; }
            var g = Physics2DWorld.World.Gravity;
            var buoy = -g * (bu.density * area * submerged);
            rb.AddForceAtPosition(buoy, new Vector2(bb.center.x, bb.min.y + h * submerged * 0.5f));
            float ang = bu.flowAngle * Mathf.Deg2Rad;
            var flow = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * EffectorUtil.Vary(bu.flowMagnitude, bu.flowVariation);
            if (flow != Vector2.zero) rb.AddForce(flow * submerged);
            ApplyDrag(b, bu.linearDrag * submerged, bu.angularDrag * submerged);
        }
    }

    public static class PlatformEffector
    {
        /// <summary>Contact modification for PlatformEffector2D (one-way) and SurfaceEffector2D.</summary>
        public static void PreSolve(Contact2 c)
        {
            var ca = (Collider2D)c.A.Owner; var cb = (Collider2D)c.B.Owner;
            Handle(c, ca, cb, true); Handle(c, cb, ca, false);
        }

        static void Handle(Contact2 c, Collider2D effCol, Collider2D other, bool effIsA)
        {
            if (!effCol.usedByEffector) return;
            var plat = EffectorUtil.EffectorOf<PlatformEffector2D>(effCol);
            var wm = WorldManifold.Of(c.M, c.A.Body.Xf, c.A.Shape.Radius, c.B.Body.Xf, c.B.Shape.Radius);
            var n = effIsA ? wm.Normal : -wm.Normal;   // from the effector towards the other collider
            if (plat != null && EffectorUtil.Affects(plat, other) && plat.useOneWay)
            {
                if (c.OneWayState == 0)
                {
                    var up = (Vector2)(Quaternion.Euler(0, 0, plat.rotationalOffset) * plat.transform.up);
                    float angle = Vector2.Angle(up, n);
                    c.OneWayState = angle <= plat.surfaceArc * 0.5f ? 2 : 1;
                }
                if (c.OneWayState == 1) c.Enabled = false;
            }
            var surf = EffectorUtil.EffectorOf<SurfaceEffector2D>(effCol);
            if (surf != null && EffectorUtil.Affects(surf, other))
            {
                var t = B2.Cross(wm.Normal, 1f);
                float speed = EffectorUtil.Vary(surf.speed, surf.speedVariation) * B2.Dot((Vector2)surf.transform.right, t);
                c.TangentSpeed = effIsA ? speed : -speed;
                if (!surf.useFriction) c.Friction = Math.Max(c.Friction, 1);
                if (!surf.useBounce) c.Restitution = 0;
            }
        }
    }
}
