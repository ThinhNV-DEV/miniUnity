// Continuous collision for MiniUnity's Physics2D, after Box2D v2.4.1 (b2_world.cpp SolveTOI, b2_island.cpp SolveTOI,
// b2_time_of_impact.cpp). The time of impact is found by conservative advancement on the GJK distance of the swept
// shapes (same targets and tolerances as b2TimeOfImpact); the sub-stepping, mini islands and the TOI position solver
// follow b2World::SolveTOI.
//
// Unity mapping: a Rigidbody2D with CollisionDetectionMode2D.Continuous is a Box2D bullet; Discrete bodies never take
// part in continuous collision, so a pair is swept only when at least one body is continuous.
//
// Box2D — MIT License, Copyright (c) 2019 Erin Catto. See B2Collision.cs for the full license text.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniUnity.Box2D
{
    public sealed partial class World2
    {
        public bool ContinuousPhysics = true, SubStepping;
        public float ToiBaumgarte = 0.75f;
        const int MaxSubSteps = 8, MaxToiContacts = 32, ToiPositionIterations = 20;

        static Xf SweepXf(Body2 b, float beta)
        {
            var c = (1 - beta) * b.C0 + beta * b.C; float a = (1 - beta) * b.A0 + beta * b.A;
            var xf = new Xf { q = new Rot(a) }; xf.p = c - xf.q.Mul(b.LocalCenter);
            return xf;
        }

        /// <summary>b2Sweep::Advance: move the sweep start to <paramref name="alpha"/>.</summary>
        static void AdvanceSweep(Body2 b, float alpha)
        {
            if (b.Alpha0 >= 1) return;
            float beta = (alpha - b.Alpha0) / (1 - b.Alpha0);
            b.C0 += beta * (b.C - b.C0); b.A0 += beta * (b.A - b.A0); b.Alpha0 = alpha;
        }

        /// <summary>b2Body::Advance: put the body at the sweep start.</summary>
        static void AdvanceBody(Body2 b, float alpha)
        {
            AdvanceSweep(b, alpha);
            b.C = b.C0; b.A = b.A0;
            b.SynchronizeTransform();
        }

        static float MaxExtent(Shape2 s, Vector2 localCenter)
        {
            float r = 0;
            foreach (var p in s.CorePoints) r = Math.Max(r, (p - localCenter).magnitude);
            return r;
        }

        /// <summary>
        /// Time of impact in [0, 1] of the two swept shapes over the sweeps' remaining interval; 1 when they never come
        /// within the target separation. Touching / overlapping at the start returns 0.
        /// </summary>
        public static float TimeOfImpact(Fixture2 fa, Fixture2 fb)
        {
            Body2 A = fa.Body, B = fb.Body;
            float R = fa.Shape.Radius + fb.Shape.Radius;
            float target = Math.Max(B2.LinearSlop, R - 3 * B2.LinearSlop), tol = 0.25f * B2.LinearSlop;
            float rA = MaxExtent(fa.Shape, A.LocalCenter), rB = MaxExtent(fb.Shape, B.LocalCenter);
            var dA = A.C - A.C0; var dB = B.C - B.C0;
            float angBound = Math.Abs(A.A - A.A0) * rA + Math.Abs(B.A - B.A0) * rB;
            float t = 0;
            for (int iter = 0; iter < 30; iter++)
            {
                var xa = SweepXf(A, t); var xb = SweepXf(B, t);
                float d = Gjk2.ShapeDistance(fa.Shape, xa, fb.Shape, xb, out var pA, out var pB);
                if (d < target + tol) return t;
                var n = (pB - pA) / d;                          // A → B
                float approach = -B2.Dot(dB - dA, n) + angBound; // closing distance over the whole interval
                if (approach <= 0) return 1;
                float step = (d - target) / approach;
                if (iter > 0 && step < 1e-6f) return t;
                t += step;
                if (t >= 1) return 1;
            }
            return t;
        }

        static bool Continuous(Body2 b) => b.Bullet && b.Type == BodyType2.Dynamic;

        void SolveTOI(in TimeStep2 step, List<Fixture2> fixtures)
        {
            bool any = false;
            foreach (var b in Bodies) if (Continuous(b) && b.Awake && b.Enabled) { any = true; break; }
            if (!any) return;
            // b2Body::SynchronizeFixtures: the broad phase sees the swept AABBs so fast bodies find their pairs
            foreach (var f in fixtures) f.UpdateSweptAabb(SweepXf(f.Body, 0));
            UpdatePairsKeepAabb(fixtures);
            foreach (var c in Contacts.Values) { c.ToiFlag = false; c.ToiCount = 0; c.IslandFlag = false; }
            foreach (var b in Bodies) { b.IslandFlag = false; b.Alpha0 = 0; }
            var islandBodies = new List<Body2>(); var islandContacts = new List<Contact2>();
            for (int guard = 0; guard < 256; guard++)
            {
                Contact2 minContact = null; float minAlpha = 1;
                foreach (var c in Contacts.Values)
                {
                    if (!c.Enabled || c.Sensor || c.ToiCount > MaxSubSteps) continue;
                    float alpha;
                    if (c.ToiFlag) alpha = c.Toi;
                    else
                    {
                        Body2 bA = c.A.Body, bB = c.B.Body;
                        bool activeA = bA.Awake && bA.Type != BodyType2.Static, activeB = bB.Awake && bB.Type != BodyType2.Static;
                        if (!activeA && !activeB) continue;
                        if (!bA.IsDynamic && !bB.IsDynamic) continue;
                        if (!Continuous(bA) && !Continuous(bB)) continue;
                        float alpha0 = bA.Alpha0;
                        if (bA.Alpha0 < bB.Alpha0) { alpha0 = bB.Alpha0; AdvanceSweep(bA, alpha0); }
                        else if (bB.Alpha0 < bA.Alpha0) AdvanceSweep(bB, alpha0);
                        float t = TimeOfImpact(c.A, c.B);
                        alpha = t < 1 ? Math.Min(alpha0 + (1 - alpha0) * t, 1) : 1;
                        c.Toi = alpha; c.ToiFlag = true;
                    }
                    if (alpha < minAlpha) { minContact = c; minAlpha = alpha; }
                }
                if (minContact == null || 1 - 10 * B2.Epsilon < minAlpha) break;

                Body2 b1 = minContact.A.Body, b2 = minContact.B.Body;
                var back1 = (b1.C0, b1.A0, b1.C, b1.A, b1.Alpha0); var back2 = (b2.C0, b2.A0, b2.C, b2.A, b2.Alpha0);
                AdvanceBody(b1, minAlpha); AdvanceBody(b2, minAlpha);
                UpdateContact(minContact);
                minContact.ToiFlag = false; minContact.ToiCount++;
                if (!minContact.Enabled || !minContact.Touching)
                {
                    minContact.Enabled = false;
                    Restore(b1, back1); Restore(b2, back2);
                    continue;
                }
                b1.SetAwake(true); b2.SetAwake(true);

                islandBodies.Clear(); islandContacts.Clear();
                islandBodies.Add(b1); islandBodies.Add(b2); islandContacts.Add(minContact);
                b1.IslandFlag = b2.IslandFlag = true; minContact.IslandFlag = true;
                foreach (var body in new[] { b1, b2 })
                {
                    if (!body.IsDynamic) continue;
                    foreach (var ce in body.Contacts)
                    {
                        if (islandBodies.Count >= MaxToiContacts || islandContacts.Count >= MaxToiContacts) break;
                        if (ce.IslandFlag || ce.Sensor) continue;
                        var other = ce.A.Body == body ? ce.B.Body : ce.A.Body;
                        if (other.IsDynamic && !Continuous(body) && !Continuous(other)) continue;
                        var backup = (other.C0, other.A0, other.C, other.A, other.Alpha0);
                        if (!other.IslandFlag) AdvanceBody(other, minAlpha);
                        UpdateContact(ce);
                        if (!ce.Enabled || !ce.Touching) { Restore(other, backup); continue; }
                        ce.IslandFlag = true; islandContacts.Add(ce);
                        if (other.IslandFlag) continue;
                        other.IslandFlag = true;
                        if (other.Type != BodyType2.Static) other.SetAwake(true);
                        islandBodies.Add(other);
                    }
                }

                float dt = (1 - minAlpha) * step.Dt;
                var sub = new TimeStep2
                {
                    Dt = dt, InvDt = dt > 0 ? 1 / dt : 0, DtRatio = 1, PositionIterations = ToiPositionIterations,
                    VelocityIterations = step.VelocityIterations, WarmStarting = false,
                };
                SolveToiIsland(sub, islandBodies, islandContacts, b1, b2);

                foreach (var b in islandBodies)
                {
                    b.IslandFlag = false;
                    if (!b.IsDynamic) continue;
                    foreach (var f in b.Fixtures) f.UpdateSweptAabb(SweepXf(b, 0));
                    foreach (var ce in b.Contacts) { ce.ToiFlag = false; ce.IslandFlag = false; }
                }
                // b2ContactManager::FindNewContacts after the sub-step moved bodies
                UpdatePairsKeepAabb(fixtures);
                if (SubStepping) break;
            }
            foreach (var f in fixtures) f.UpdateAabb();
        }

        static void Restore(Body2 b, (Vector2 c0, float a0, Vector2 c, float a, float alpha0) s)
        {
            b.C0 = s.c0; b.A0 = s.a0; b.C = s.c; b.A = s.a; b.Alpha0 = s.alpha0;
            b.SynchronizeTransform();
        }

        /// <summary>b2Island::SolveTOI: push the two TOI bodies apart, then integrate the rest of the step.</summary>
        void SolveToiIsland(in TimeStep2 sub, List<Body2> bodies, List<Contact2> contacts, Body2 toiA, Body2 toiB)
        {
            foreach (var b in bodies) { b.SC = b.C; b.SA = b.A; b.SV = b.V; b.SW = b.W; }
            var vcs = InitContacts(sub, contacts);
            for (int i = 0; i < sub.PositionIterations; i++)
                if (SolvePositionConstraints(vcs, toiA, toiB, ToiBaumgarte, -1.5f * B2.LinearSlop)) break;
            // leap of faith: the solved positions become the sweep start
            toiA.C0 = toiA.SC; toiA.A0 = toiA.SA; toiB.C0 = toiB.SC; toiB.A0 = toiB.SA;
            InitVelocityConstraints(vcs);
            for (int i = 0; i < sub.VelocityIterations; i++) SolveVelocityConstraints(vcs);
            StoreImpulses(vcs);
            float h = sub.Dt, maxT = MaxTranslationSpeed * h, maxR = MaxRotationSpeed * h;
            foreach (var b in bodies)
            {
                if (b.Type == BodyType2.Static) continue;
                var v = b.SV; float w = b.SW;
                var tr = h * v;
                if (B2.Dot(tr, tr) > maxT * maxT) v *= maxT / tr.magnitude;
                float rot = h * w;
                if (rot * rot > maxR * maxR) w *= maxR / Math.Abs(rot);
                b.SC += h * v; b.SA += h * w;
                b.C = b.SC; b.A = b.SA; b.V = v; b.W = w;
                b.SynchronizeTransform();
            }
        }
    }
}
