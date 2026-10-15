// 2D rigid-body world for MiniUnity's Physics2D, ported from Box2D v2.4.1 (b2_world / b2_island / b2_contact /
// b2_contact_solver): contact update with feature-id warm starting, islands, sequential-impulse velocity solver with the
// 2-point block solver, non-linear Gauss-Seidel position solver, island sleeping.
//
// Box2D — MIT License, Copyright (c) 2019 Erin Catto. See B2Collision.cs for the full license text.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MiniUnity.Box2D
{
    public enum BodyType2 { Static = 0, Kinematic = 1, Dynamic = 2 }

    public sealed class Body2
    {
        public object Owner;                    // Rigidbody2D, or null for the static ground body
        public BodyType2 Type;
        public Xf Xf = Xf.Identity;             // body origin
        public Vector2 LocalCenter;
        public Vector2 C, C0; public float A, A0;   // sweep: centre of mass and angle
        public Vector2 V; public float W;
        public Vector2 Force; public float Torque;
        public float Mass = 1, InvMass = 1, I, InvI;
        public Vector2 InvMassAxis = Vector2.one;   // FreezePosition X / Y zero an axis
        public float LinearDamping, AngularDamping, GravityScale = 1;
        public bool Awake = true, AutoSleep = true, FixedRotation, Enabled = true, FullKinematicContacts, Bullet;
        public float SleepTime;
        internal float Alpha0;                  // sweep start fraction within the step (continuous collision)
        public readonly List<Fixture2> Fixtures = new();
        public readonly List<Contact2> Contacts = new();
        public readonly List<Joint2> Joints = new();
        internal bool IslandFlag;
        // solver state
        internal Vector2 SC, SV; internal float SA, SW;

        public bool IsStatic => Type == BodyType2.Static;
        public bool IsDynamic => Type == BodyType2.Dynamic;

        public void SetAwake(bool flag)
        {
            if (Type == BodyType2.Static) return;
            if (flag) { Awake = true; SleepTime = 0; }
            else { Awake = false; SleepTime = 0; V = Vector2.zero; W = 0; Force = Vector2.zero; Torque = 0; }
        }

        public void SetTransform(Vector2 p, float angle)
        {
            Xf = new Xf(p, angle);
            C = Xf.Mul(LocalCenter); A = angle; C0 = C; A0 = A;
        }

        public void SynchronizeTransform() { Xf.q = new Rot(A); Xf.p = C - Xf.q.Mul(LocalCenter); }

        public Vector2 PointVelocity(Vector2 world) => V + B2.Cross(W, world - C);
    }

    public sealed class Fixture2
    {
        public object Owner;                    // Collider2D
        public Body2 Body;
        public Shape2 Shape;                    // body-local
        public float Density = 1, Friction = 0.4f, Restitution;
        public bool Sensor;
        public int Id, Layer;
        public AABB2 Aabb;
        public void UpdateAabb() => Aabb = Shape.ComputeAABB(Body.Xf);
        /// <summary>b2Fixture::Synchronize: AABB covering the sweep start and end transforms.</summary>
        public void UpdateSweptAabb(Xf xf0) => Aabb = AABB2.Union(Shape.ComputeAABB(xf0), Shape.ComputeAABB(Body.Xf));
    }

    public sealed class Contact2
    {
        public Fixture2 A, B;
        public readonly Manifold M = new();
        public bool Touching, Enabled = true, IslandFlag, Sensor;
        public float Friction, Restitution, Threshold, TangentSpeed;
        public Vector2 PreSolveRelVel;          // relative velocity at the first point before solving (for callbacks)
        public int OneWayState;                 // PlatformEffector2D: 0 undecided, 1 passes through, 2 collides
        internal ContactConstraint VC;
        internal bool ToiFlag; internal float Toi; internal int ToiCount;
    }

    public struct TimeStep2 { public float Dt, InvDt, DtRatio; public int VelocityIterations, PositionIterations; public bool WarmStarting; }

    public abstract class Joint2
    {
        public object Owner;
        public Body2 A, B;
        public bool CollideConnected, IslandFlag;
        public abstract void InitVelocity(in TimeStep2 step);
        public abstract void SolveVelocity(in TimeStep2 step);
        public abstract bool SolvePosition(in TimeStep2 step);
        public abstract Vector2 ReactionForce(float invDt);
        public abstract float ReactionTorque(float invDt);
        // Box2D settings used by joints (Unity's Physics2D settings)
        public static float MaxLinearCorrection = 0.2f, MaxAngularCorrection = 8f / 180f * MathF.PI;
    }

    internal sealed class ContactConstraint
    {
        public Contact2 Contact; public Body2 A, B;
        public Vector2 Normal; public float Friction, Restitution, Threshold, TangentSpeed;
        public Vector2 mA, mB; public float iA, iB;
        public int PointCount;
        public Vector2[] rA = new Vector2[2], rB = new Vector2[2];
        public float[] NormalImpulse = new float[2], TangentImpulse = new float[2], NormalMass = new float[2], TangentMass = new float[2], VelocityBias = new float[2];
        public float K11, K12, K22, NM11, NM12, NM21, NM22;   // block solver K and its inverse
        // position constraint
        public Vector2[] LocalPoints = new Vector2[2]; public Vector2 LocalNormal, LocalPoint, LocalCenterA, LocalCenterB;
        public ManifoldType Type; public float RadiusA, RadiusB; public int PosPointCount;
    }

    public sealed partial class World2
    {
        public Vector2 Gravity = new(0, -9.81f);
        public int VelocityIterations = 8, PositionIterations = 3;
        public float VelocityThreshold = 1, MaxLinearCorrection = 0.2f, MaxAngularCorrection = 8f / 180f * MathF.PI;
        public float MaxTranslationSpeed = 100, MaxRotationSpeed = 360 * Mathf.Deg2Rad;
        public float Baumgarte = 0.2f, TimeToSleep = 0.5f, LinearSleepTolerance = 0.01f, AngularSleepTolerance = 2f * Mathf.Deg2Rad;
        public bool AllowSleep = true, WarmStarting = true;
        public readonly List<Body2> Bodies = new();
        public readonly Dictionary<(int, int), Contact2> Contacts = new();
        public readonly List<Joint2> Joints = new();
        public Func<Fixture2, Fixture2, bool> ShouldCollide = (a, b) => true;
        public Action<Contact2> BeginContact, EndContact;
        public Action<Contact2, Manifold> PreSolve;
        float prevDt;

        public void RemoveContact(Contact2 c)
        {
            Contacts.Remove((c.A.Id, c.B.Id));
            c.A.Body.Contacts.Remove(c); c.B.Body.Contacts.Remove(c);
            if (c.Touching) EndContact?.Invoke(c);
        }

        static bool BodiesCollide(Body2 a, Body2 b)
        {
            if (a == b) return false;
            if (a.Type != BodyType2.Dynamic && b.Type != BodyType2.Dynamic)
            {
                // Unity: useFullKinematicContacts lets a kinematic body contact static / kinematic bodies (callbacks only)
                bool fa = a.Type == BodyType2.Kinematic && a.FullKinematicContacts, fb = b.Type == BodyType2.Kinematic && b.FullKinematicContacts;
                if (!fa && !fb) return false;
            }
            foreach (var j in b.Joints) if ((j.A == a || j.B == a) && !j.CollideConnected) return false;
            return true;
        }

        /// <summary>Broad phase: create contacts for overlapping (fat) AABBs, destroy the ones that stopped overlapping.</summary>
        public void UpdatePairs(List<Fixture2> fixtures)
        {
            foreach (var f in fixtures) f.UpdateAabb();
            UpdatePairsKeepAabb(fixtures);
        }

        void UpdatePairsKeepAabb(List<Fixture2> fixtures)
        {
            var live = new HashSet<(int, int)>();
            var list = fixtures.Where(f => f.Body.Enabled).OrderBy(f => f.Aabb.lo.x).ToList();
            const float m = B2.AabbExtension;
            for (int i = 0; i < list.Count; i++)
            {
                var fa = list[i]; float maxX = fa.Aabb.hi.x + 2 * m;
                for (int j = i + 1; j < list.Count; j++)
                {
                    var fb = list[j];
                    if (fb.Aabb.lo.x > maxX) break;
                    if (!fa.Aabb.Fat(m).Overlaps(fb.Aabb.Fat(m))) continue;
                    if (!Collide2.CanCollide(fa.Shape, fb.Shape)) continue;
                    if (!BodiesCollide(fa.Body, fb.Body) || !ShouldCollide(fa, fb)) continue;
                    var (A, B) = Collide2.NeedsSwap(fa.Shape, fb.Shape) ? (fb, fa) : (fa, fb);
                    if (A.Shape.Type == B.Shape.Type && A.Id > B.Id) (A, B) = (B, A);
                    var key = (A.Id, B.Id);
                    live.Add(key);
                    if (!Contacts.ContainsKey(key))
                    {
                        var c = new Contact2 { A = A, B = B, Sensor = A.Sensor || B.Sensor };
                        Contacts[key] = c; A.Body.Contacts.Add(c); B.Body.Contacts.Add(c);
                    }
                }
            }
            foreach (var c in Contacts.Values.ToList())
                if (!live.Contains((c.A.Id, c.B.Id))) RemoveContact(c);
        }

        /// <summary>b2ContactManager::Collide + b2Contact::Update.</summary>
        public void Collide()
        {
            foreach (var c in Contacts.Values.ToList())
            {
                var ba = c.A.Body; var bb = c.B.Body;
                bool activeA = ba.Awake && ba.Type != BodyType2.Static, activeB = bb.Awake && bb.Type != BodyType2.Static;
                if (!activeA && !activeB) continue;
                UpdateContact(c);
            }
        }

        readonly Manifold oldM = new();

        void UpdateContact(Contact2 c)
        {
            oldM.CopyFrom(c.M);
            c.Enabled = true;
            bool was = c.Touching, touching;
            var xa = c.A.Body.Xf; var xb = c.B.Body.Xf;
            if (c.Sensor)
            {
                touching = Gjk2.Overlap(c.A.Shape, xa, c.B.Shape, xb);
                c.M.PointCount = 0;
            }
            else
            {
                Collide2.Collide(c.M, c.A.Shape, xa, c.B.Shape, xb);
                touching = c.M.PointCount > 0;
                for (int i = 0; i < c.M.PointCount; i++)
                {
                    ref var mp = ref c.M.Points[i];
                    mp.NormalImpulse = 0; mp.TangentImpulse = 0;
                    for (int j = 0; j < oldM.PointCount; j++)
                        if (oldM.Points[j].Id == mp.Id) { mp.NormalImpulse = oldM.Points[j].NormalImpulse; mp.TangentImpulse = oldM.Points[j].TangentImpulse; break; }
                }
                if (touching != was) { c.A.Body.SetAwake(true); c.B.Body.SetAwake(true); }
            }
            c.Touching = touching;
            if (!touching) c.OneWayState = 0;
            // mixing (b2MixFriction / b2MixRestitution)
            c.Friction = MathF.Sqrt(c.A.Friction * c.B.Friction);
            c.Restitution = Math.Max(c.A.Restitution, c.B.Restitution);
            c.Threshold = VelocityThreshold; c.TangentSpeed = 0;
            if (!was && touching) BeginContact?.Invoke(c);
            if (was && !touching) EndContact?.Invoke(c);
            if (!c.Sensor && touching) PreSolve?.Invoke(c, oldM);
        }

        // ------------------------------------------------------------------ step (b2World::Step: collide, solve)
        public void Step(float dt, List<Fixture2> fixtures)
        {
            var step = new TimeStep2
            {
                Dt = dt, InvDt = dt > 0 ? 1 / dt : 0, DtRatio = prevDt > 0 ? dt / prevDt : 1,
                VelocityIterations = VelocityIterations, PositionIterations = PositionIterations, WarmStarting = WarmStarting,
            };
            Joint2.MaxLinearCorrection = MaxLinearCorrection; Joint2.MaxAngularCorrection = MaxAngularCorrection;
            UpdatePairs(fixtures);
            Collide();
            if (dt > 0) Solve(step);
            if (dt > 0 && ContinuousPhysics) SolveTOI(step, fixtures);
            prevDt = dt;
            foreach (var b in Bodies) { b.Force = Vector2.zero; b.Torque = 0; }
        }

        void Solve(in TimeStep2 step)
        {
            foreach (var b in Bodies) { b.C0 = b.C; b.A0 = b.A; b.Alpha0 = 0; }
            foreach (var b in Bodies) b.IslandFlag = false;
            foreach (var c in Contacts.Values) c.IslandFlag = false;
            foreach (var j in Joints) j.IslandFlag = false;
            var stack = new Stack<Body2>();
            var ib = new List<Body2>(); var ic = new List<Contact2>(); var ij = new List<Joint2>();
            foreach (var seed in Bodies)
            {
                if (seed.IslandFlag || !seed.Awake || !seed.Enabled || seed.Type == BodyType2.Static) continue;
                ib.Clear(); ic.Clear(); ij.Clear();
                stack.Push(seed); seed.IslandFlag = true;
                while (stack.Count > 0)
                {
                    var b = stack.Pop();
                    ib.Add(b);
                    if (b.Type == BodyType2.Static) continue;
                    b.Awake = true;
                    foreach (var c in b.Contacts)
                    {
                        if (c.IslandFlag || !c.Enabled || !c.Touching || c.Sensor) continue;
                        // contacts between non-dynamic bodies (full kinematic contacts) are reported but not solved
                        if (!c.A.Body.IsDynamic && !c.B.Body.IsDynamic) continue;
                        ic.Add(c); c.IslandFlag = true;
                        var other = c.A.Body == b ? c.B.Body : c.A.Body;
                        if (other.IslandFlag) continue;
                        stack.Push(other); other.IslandFlag = true;
                    }
                    foreach (var j in b.Joints)
                    {
                        if (j.IslandFlag) continue;
                        var other = j.A == b ? j.B : j.A;
                        if (!other.Enabled) continue;
                        ij.Add(j); j.IslandFlag = true;
                        if (other.IslandFlag) continue;
                        stack.Push(other); other.IslandFlag = true;
                    }
                }
                SolveIsland(step, ib, ic, ij);
                foreach (var b in ib) if (b.Type == BodyType2.Static) b.IslandFlag = false;
            }
        }

        // ------------------------------------------------------------------ b2Island::Solve
        void SolveIsland(in TimeStep2 step, List<Body2> bodies, List<Contact2> contacts, List<Joint2> joints)
        {
            float h = step.Dt;
            foreach (var b in bodies)
            {
                var v = b.V; float w = b.W;
                b.C0 = b.C; b.A0 = b.A;
                if (b.Type == BodyType2.Dynamic)
                {
                    v += h * b.InvMass * (b.GravityScale * b.Mass * Gravity + b.Force);
                    w += h * b.InvI * b.Torque;
                    v *= 1f / (1f + h * b.LinearDamping);
                    w *= 1f / (1f + h * b.AngularDamping);
                    v = Vector2.Scale(v, new Vector2(b.InvMassAxis.x > 0 ? 1 : 0, b.InvMassAxis.y > 0 ? 1 : 0));
                    if (b.FixedRotation) w = 0;
                }
                b.SC = b.C; b.SA = b.A; b.SV = v; b.SW = w;
            }
            var vcs = InitContacts(step, contacts);
            InitVelocityConstraints(vcs);
            if (step.WarmStarting) WarmStart(vcs);
            foreach (var j in joints) j.InitVelocity(step);
            for (int i = 0; i < step.VelocityIterations; i++)
            {
                foreach (var j in joints) { j.SolveVelocity(step); ClampFrozen(j.A); ClampFrozen(j.B); }
                SolveVelocityConstraints(vcs);
            }
            StoreImpulses(vcs);
            float maxT = MaxTranslationSpeed * h, maxR = MaxRotationSpeed * h;
            foreach (var b in bodies)
            {
                var v = b.SV; float w = b.SW;
                var tr = h * v;
                if (B2.Dot(tr, tr) > maxT * maxT) v *= maxT / tr.magnitude;
                float rot = h * w;
                if (rot * rot > maxR * maxR) w *= maxR / Math.Abs(rot);
                b.SC += h * v; b.SA += h * w; b.SV = v; b.SW = w;
            }
            bool positionSolved = false;
            for (int i = 0; i < step.PositionIterations; i++)
            {
                bool contactsOkay = SolvePositionConstraints(vcs);
                bool jointsOkay = true;
                foreach (var j in joints) jointsOkay &= j.SolvePosition(step);
                if (contactsOkay && jointsOkay) { positionSolved = true; break; }
            }
            foreach (var b in bodies)
            {
                b.C = b.SC; b.A = b.SA; b.V = b.SV; b.W = b.SW;
                b.SynchronizeTransform();
            }
            if (!AllowSleep) return;
            float minSleep = float.MaxValue;
            float linTol = LinearSleepTolerance * LinearSleepTolerance, angTol = AngularSleepTolerance * AngularSleepTolerance;
            foreach (var b in bodies)
            {
                if (b.Type == BodyType2.Static) continue;
                if (!b.AutoSleep || b.W * b.W > angTol || B2.Dot(b.V, b.V) > linTol) { b.SleepTime = 0; minSleep = 0; }
                else { b.SleepTime += h; minSleep = Math.Min(minSleep, b.SleepTime); }
            }
            if (minSleep >= TimeToSleep && positionSolved) foreach (var b in bodies) b.SetAwake(false);
        }

        static void ClampFrozen(Body2 b)
        {
            if (b.Type != BodyType2.Dynamic) return;
            if (b.InvMassAxis.x == 0) b.SV.x = 0;
            if (b.InvMassAxis.y == 0) b.SV.y = 0;
        }

        static Vector2 MassAxis(Body2 b) => b.InvMass * b.InvMassAxis;

        List<ContactConstraint> InitContacts(in TimeStep2 step, List<Contact2> contacts)
        {
            var list = new List<ContactConstraint>(contacts.Count);
            foreach (var c in contacts)
            {
                var vc = c.VC ??= new ContactConstraint();
                var ba = c.A.Body; var bb = c.B.Body; var m = c.M;
                vc.Contact = c; vc.A = ba; vc.B = bb;
                vc.Friction = c.Friction; vc.Restitution = c.Restitution; vc.Threshold = c.Threshold; vc.TangentSpeed = c.TangentSpeed;
                vc.mA = MassAxis(ba); vc.mB = MassAxis(bb); vc.iA = ba.InvI; vc.iB = bb.InvI;
                vc.PointCount = vc.PosPointCount = m.PointCount;
                vc.LocalCenterA = ba.LocalCenter; vc.LocalCenterB = bb.LocalCenter;
                vc.LocalNormal = m.LocalNormal; vc.LocalPoint = m.LocalPoint; vc.Type = m.Type;
                vc.RadiusA = c.A.Shape.Radius; vc.RadiusB = c.B.Shape.Radius;
                for (int j = 0; j < m.PointCount; j++)
                {
                    vc.NormalImpulse[j] = step.WarmStarting ? step.DtRatio * m.Points[j].NormalImpulse : 0;
                    vc.TangentImpulse[j] = step.WarmStarting ? step.DtRatio * m.Points[j].TangentImpulse : 0;
                    vc.LocalPoints[j] = m.Points[j].LocalPoint;
                }
                list.Add(vc);
            }
            return list;
        }

        static float KOf(Vector2 m, Vector2 n) => m.x * n.x * n.x + m.y * n.y * n.y;
        static float KOf(Vector2 m, Vector2 n1, Vector2 n2) => m.x * n1.x * n2.x + m.y * n1.y * n2.y;

        static void InitVelocityConstraints(List<ContactConstraint> vcs)
        {
            foreach (var vc in vcs)
            {
                var A = vc.A; var B = vc.B;
                var xfA = new Xf { q = new Rot(A.SA) }; xfA.p = A.SC - xfA.q.Mul(vc.LocalCenterA);
                var xfB = new Xf { q = new Rot(B.SA) }; xfB.p = B.SC - xfB.q.Mul(vc.LocalCenterB);
                var wm = WorldManifold.Of(vc.Contact.M, xfA, vc.RadiusA, xfB, vc.RadiusB);
                vc.Normal = wm.Normal;
                var n = vc.Normal; var t = B2.Cross(n, 1f);
                for (int j = 0; j < vc.PointCount; j++)
                {
                    vc.rA[j] = wm.Point(j) - A.SC; vc.rB[j] = wm.Point(j) - B.SC;
                    float rnA = B2.Cross(vc.rA[j], n), rnB = B2.Cross(vc.rB[j], n);
                    float kN = KOf(vc.mA, n) + KOf(vc.mB, n) + vc.iA * rnA * rnA + vc.iB * rnB * rnB;
                    vc.NormalMass[j] = kN > 0 ? 1 / kN : 0;
                    float rtA = B2.Cross(vc.rA[j], t), rtB = B2.Cross(vc.rB[j], t);
                    float kT = KOf(vc.mA, t) + KOf(vc.mB, t) + vc.iA * rtA * rtA + vc.iB * rtB * rtB;
                    vc.TangentMass[j] = kT > 0 ? 1 / kT : 0;
                    vc.VelocityBias[j] = 0;
                    float vRel = B2.Dot(n, B.SV + B2.Cross(B.SW, vc.rB[j]) - A.SV - B2.Cross(A.SW, vc.rA[j]));
                    if (vRel < -vc.Threshold) vc.VelocityBias[j] = -vc.Restitution * vRel;
                }
                if (vc.PointCount == 2)
                {
                    float rn1A = B2.Cross(vc.rA[0], n), rn1B = B2.Cross(vc.rB[0], n), rn2A = B2.Cross(vc.rA[1], n), rn2B = B2.Cross(vc.rB[1], n);
                    float mm = KOf(vc.mA, n) + KOf(vc.mB, n);
                    float k11 = mm + vc.iA * rn1A * rn1A + vc.iB * rn1B * rn1B;
                    float k22 = mm + vc.iA * rn2A * rn2A + vc.iB * rn2B * rn2B;
                    float k12 = mm + vc.iA * rn1A * rn2A + vc.iB * rn1B * rn2B;
                    const float kMaxCond = 1000;
                    if (k11 * k11 < kMaxCond * (k11 * k22 - k12 * k12))
                    {
                        vc.K11 = k11; vc.K12 = k12; vc.K22 = k22;
                        float det = k11 * k22 - k12 * k12; if (det != 0) det = 1 / det;
                        vc.NM11 = det * k22; vc.NM12 = -det * k12; vc.NM21 = -det * k12; vc.NM22 = det * k11;
                    }
                    else vc.PointCount = 1;
                }
            }
        }

        static void WarmStart(List<ContactConstraint> vcs)
        {
            foreach (var vc in vcs)
            {
                var A = vc.A; var B = vc.B; var n = vc.Normal; var t = B2.Cross(n, 1f);
                for (int j = 0; j < vc.PointCount; j++)
                {
                    var P = vc.NormalImpulse[j] * n + vc.TangentImpulse[j] * t;
                    A.SW -= vc.iA * B2.Cross(vc.rA[j], P); A.SV -= Vector2.Scale(vc.mA, P);
                    B.SW += vc.iB * B2.Cross(vc.rB[j], P); B.SV += Vector2.Scale(vc.mB, P);
                }
            }
        }

        static void Apply(ContactConstraint vc, int j, Vector2 P)
        {
            vc.A.SV -= Vector2.Scale(vc.mA, P); vc.A.SW -= vc.iA * B2.Cross(vc.rA[j], P);
            vc.B.SV += Vector2.Scale(vc.mB, P); vc.B.SW += vc.iB * B2.Cross(vc.rB[j], P);
        }

        static void SolveVelocityConstraints(List<ContactConstraint> vcs)
        {
            foreach (var vc in vcs)
            {
                var A = vc.A; var B = vc.B; var n = vc.Normal; var t = B2.Cross(n, 1f);
                for (int j = 0; j < vc.PointCount; j++)
                {
                    var dv = B.SV + B2.Cross(B.SW, vc.rB[j]) - A.SV - B2.Cross(A.SW, vc.rA[j]);
                    float vt = B2.Dot(dv, t) - vc.TangentSpeed;
                    float lambda = vc.TangentMass[j] * -vt;
                    float maxF = vc.Friction * vc.NormalImpulse[j];
                    float ni = Math.Clamp(vc.TangentImpulse[j] + lambda, -maxF, maxF);
                    lambda = ni - vc.TangentImpulse[j]; vc.TangentImpulse[j] = ni;
                    Apply(vc, j, lambda * t);
                }
                if (vc.PointCount == 1)
                {
                    var dv = B.SV + B2.Cross(B.SW, vc.rB[0]) - A.SV - B2.Cross(A.SW, vc.rA[0]);
                    float vn = B2.Dot(dv, n);
                    float lambda = -vc.NormalMass[0] * (vn - vc.VelocityBias[0]);
                    float ni = Math.Max(vc.NormalImpulse[0] + lambda, 0);
                    lambda = ni - vc.NormalImpulse[0]; vc.NormalImpulse[0] = ni;
                    Apply(vc, 0, lambda * n);
                    continue;
                }
                if (vc.PointCount == 0) continue;
                // block solver (b2ContactSolver::SolveVelocityConstraints, 2 points)
                float ax = vc.NormalImpulse[0], ay = vc.NormalImpulse[1];
                var dv1 = B.SV + B2.Cross(B.SW, vc.rB[0]) - A.SV - B2.Cross(A.SW, vc.rA[0]);
                var dv2 = B.SV + B2.Cross(B.SW, vc.rB[1]) - A.SV - B2.Cross(A.SW, vc.rA[1]);
                float vn1 = B2.Dot(dv1, n), vn2 = B2.Dot(dv2, n);
                float bx = vn1 - vc.VelocityBias[0] - (vc.K11 * ax + vc.K12 * ay);
                float by = vn2 - vc.VelocityBias[1] - (vc.K12 * ax + vc.K22 * ay);
                float xx, xy;
                // case 1: both points active
                xx = -(vc.NM11 * bx + vc.NM12 * by); xy = -(vc.NM21 * bx + vc.NM22 * by);
                if (xx >= 0 && xy >= 0) { ApplyBlock(vc, xx - ax, xy - ay, n); vc.NormalImpulse[0] = xx; vc.NormalImpulse[1] = xy; continue; }
                // case 2: point 1 only
                xx = -vc.NormalMass[0] * bx; xy = 0;
                vn2 = vc.K12 * xx + by;
                if (xx >= 0 && vn2 >= 0) { ApplyBlock(vc, xx - ax, xy - ay, n); vc.NormalImpulse[0] = xx; vc.NormalImpulse[1] = xy; continue; }
                // case 3: point 2 only
                xx = 0; xy = -vc.NormalMass[1] * by;
                vn1 = vc.K12 * xy + bx;
                if (xy >= 0 && vn1 >= 0) { ApplyBlock(vc, xx - ax, xy - ay, n); vc.NormalImpulse[0] = xx; vc.NormalImpulse[1] = xy; continue; }
                // case 4: none
                xx = 0; xy = 0;
                if (bx >= 0 && by >= 0) { ApplyBlock(vc, xx - ax, xy - ay, n); vc.NormalImpulse[0] = xx; vc.NormalImpulse[1] = xy; }
            }
        }

        static void ApplyBlock(ContactConstraint vc, float dx, float dy, Vector2 n)
        {
            var P1 = dx * n; var P2 = dy * n;
            vc.A.SV -= Vector2.Scale(vc.mA, P1 + P2); vc.A.SW -= vc.iA * (B2.Cross(vc.rA[0], P1) + B2.Cross(vc.rA[1], P2));
            vc.B.SV += Vector2.Scale(vc.mB, P1 + P2); vc.B.SW += vc.iB * (B2.Cross(vc.rB[0], P1) + B2.Cross(vc.rB[1], P2));
        }

        static void StoreImpulses(List<ContactConstraint> vcs)
        {
            foreach (var vc in vcs)
            {
                var m = vc.Contact.M;
                for (int j = 0; j < vc.PointCount; j++) { m.Points[j].NormalImpulse = vc.NormalImpulse[j]; m.Points[j].TangentImpulse = vc.TangentImpulse[j]; }
            }
        }

        bool SolvePositionConstraints(List<ContactConstraint> vcs) => SolvePositionConstraints(vcs, null, null, Baumgarte, -3 * B2.LinearSlop);

        /// <summary>
        /// Non-linear Gauss-Seidel position pass. With TOI bodies set (b2ContactSolver::SolveTOIPositionConstraints) only
        /// those two bodies move.
        /// </summary>
        bool SolvePositionConstraints(List<ContactConstraint> vcs, Body2 toiA, Body2 toiB, float baumgarte, float okSep)
        {
            float minSep = 0;
            foreach (var pc in vcs)
            {
                var A = pc.A; var B = pc.B;
                var mA = pc.mA; var mB = pc.mB; float iA = pc.iA, iB = pc.iB;
                if (toiA != null)
                {
                    if (A != toiA && A != toiB) { mA = Vector2.zero; iA = 0; }
                    if (B != toiA && B != toiB) { mB = Vector2.zero; iB = 0; }
                }
                for (int j = 0; j < pc.PosPointCount; j++)
                {
                    var xfA = new Xf { q = new Rot(A.SA) }; xfA.p = A.SC - xfA.q.Mul(pc.LocalCenterA);
                    var xfB = new Xf { q = new Rot(B.SA) }; xfB.p = B.SC - xfB.q.Mul(pc.LocalCenterB);
                    Vector2 normal, point; float sep;
                    switch (pc.Type)
                    {
                        case ManifoldType.Circles:
                        {
                            var pA = xfA.Mul(pc.LocalPoint); var pB = xfB.Mul(pc.LocalPoints[0]);
                            normal = B2.Normalize(pB - pA); point = 0.5f * (pA + pB);
                            sep = B2.Dot(pB - pA, normal) - pc.RadiusA - pc.RadiusB;
                            break;
                        }
                        case ManifoldType.FaceA:
                        {
                            normal = xfA.q.Mul(pc.LocalNormal);
                            var plane = xfA.Mul(pc.LocalPoint); var clip = xfB.Mul(pc.LocalPoints[j]);
                            sep = B2.Dot(clip - plane, normal) - pc.RadiusA - pc.RadiusB; point = clip;
                            break;
                        }
                        default:
                        {
                            normal = xfB.q.Mul(pc.LocalNormal);
                            var plane = xfB.Mul(pc.LocalPoint); var clip = xfA.Mul(pc.LocalPoints[j]);
                            sep = B2.Dot(clip - plane, normal) - pc.RadiusA - pc.RadiusB; point = clip;
                            normal = -normal;
                            break;
                        }
                    }
                    var rA = point - A.SC; var rB = point - B.SC;
                    minSep = Math.Min(minSep, sep);
                    float C = Math.Clamp(baumgarte * (sep + B2.LinearSlop), -MaxLinearCorrection, 0);
                    float rnA = B2.Cross(rA, normal), rnB = B2.Cross(rB, normal);
                    float K = KOf(mA, normal) + KOf(mB, normal) + iA * rnA * rnA + iB * rnB * rnB;
                    float impulse = K > 0 ? -C / K : 0;
                    var P = impulse * normal;
                    A.SC -= Vector2.Scale(mA, P); A.SA -= iA * B2.Cross(rA, P);
                    B.SC += Vector2.Scale(mB, P); B.SA += iB * B2.Cross(rB, P);
                }
            }
            return minSep >= okSep;
        }
    }
}
