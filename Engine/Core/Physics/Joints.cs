// Joints as PhysX D6-style constraints solved inside the same PGS loop as contacts: one 1D row per locked / limited degree of
// freedom (linear rows at the anchors, angular rows on the relative rotation), motors as velocity rows with a force cap,
// springs and drives as explicit forces. The connected body's frame is captured when the joint starts (as Unity does).
// Fixed: all locked · Hinge: free about `axis` (+ limits / motor / spring) · Character: twist + swing limits · Spring: distance spring
// · Configurable: per-axis Locked / Limited / Free + drives. breakForce / breakTorque destroy the joint and send OnJointBreak.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity.Physics3D
{
    public sealed class JointState
    {
        public Joint J; public Body A, B;          // B null: anchored to the world
        public Quaternion FrameA = Quaternion.identity;   // joint frame in A's local space (X = axis, Y = secondary)
        public Quaternion FrameB = Quaternion.identity;   // same frame in B's local space (or world), captured at start
        public Vector3 LocalAnchorA, LocalAnchorB;
        public Vector3 LinImpulse, AngImpulse;
        public bool Started;
    }

    public static class JointSolver
    {
        internal sealed class JRow
        {
            public Body A, B; public Vector3 N, CA, CB, IA, IB; public float VelMul, Bias, Target, Lo = float.MinValue, Hi = float.MaxValue, Applied;
            public JointState Owner; public bool Angular, Motor, InBlock; public Vector3 Axis;
        }

        static readonly Dictionary<Joint, JointState> states = new();
        static readonly Body World = new();
        public static void Reset() => states.Clear();

        public static JointState StateOf(Joint j) => states.TryGetValue(j, out var s) ? s : null;

        /// <summary>A joint the world owns (articulation links): tracked and started here instead of being found on a GameObject.</summary>
        internal static JointState Track(Joint j, Body A, Body B)
        {
            var s = new JointState { J = j, A = A, B = B };
            states[j] = s;
            Start(s);
            return s;
        }

        public static List<JointState> Collect(IEnumerable<GameObject> objects, Func<Rigidbody, Body> bodyOf)
        {
            var live = new List<JointState>();
            foreach (var j in objects.Where(g => !g.m_Destroyed && g.activeInHierarchy).SelectMany(g => g.m_Components.OfType<Joint>()).ToList())
            {
                if (j.m_Destroyed || j.m_Broken) continue;
                var rbA = j.GetComponent<Rigidbody>(); if (rbA == null) continue;
                var A = bodyOf(rbA); if (A == null) continue;
                var cb = j.connectedBody; var B = cb != null ? bodyOf(cb) : null;
                if (!states.TryGetValue(j, out var s)) { s = new JointState { J = j }; states[j] = s; }
                s.A = A; s.B = B;
                if (!s.Started) Start(s);
                live.Add(s);
            }
            foreach (var k in states.Keys.Where(k => k.m_Destroyed || k.m_Broken).ToList()) states.Remove(k);
            return live;
        }

        static PxPose PoseOf(Body b) => b == null ? PxPose.Identity : b.PxPose;

        static void Start(JointState s)
        {
            var j = s.J;
            var axis = j.axis.sqrMagnitude > 1e-8f ? j.axis.normalized : Vector3.right;
            var sec = j is ConfigurableJoint cj ? cj.secondaryAxis : j is CharacterJoint ch ? ch.swingAxis : Geo.AnyPerpendicular(axis);
            sec -= axis * Vector3.Dot(sec, axis);
            if (sec.sqrMagnitude < 1e-8f) sec = Geo.AnyPerpendicular(axis);
            sec.Normalize();
            s.FrameA = FromAxes(axis, sec);
            var pA = PoseOf(s.A); var pB = PoseOf(s.B);
            // anchors: joint.anchor is in A's Transform space (scaled); the connected anchor is in B's space (or world)
            var tA = j.transform;
            var worldAnchor = tA.TransformPoint(j.anchor);
            s.LocalAnchorA = pA.InvApply(worldAnchor);
            if (j.autoConfigureConnectedAnchor || s.B == null && j.connectedAnchor == Vector3.zero && j.autoConfigureConnectedAnchor)
            {
                s.LocalAnchorB = pB.InvApply(worldAnchor);
                j.SetConnectedAnchorAuto(s.B != null ? s.B.Rb.transform.InverseTransformPoint(worldAnchor) : worldAnchor);
            }
            else s.LocalAnchorB = s.B != null ? pB.InvApply(s.B.Rb.transform.TransformPoint(j.connectedAnchor)) : j.connectedAnchor;
            // connected frame = A's joint frame at start, expressed in B
            var worldFrame = pA.q * s.FrameA;
            s.FrameB = Quaternion.Inverse(pB.q) * worldFrame;
            s.Started = true;
        }

        static Quaternion FromAxes(Vector3 x, Vector3 y)
        {
            var z = Vector3.Cross(x, y);
            return Quaternion.LookRotation(z, y);
        }

        static Body Or(Body b) => b ?? World;

        /// <summary>Explicit forces (springs, drives) added before solving.</summary>
        public static void ApplyForces(List<JointState> joints, float dt)
        {
            foreach (var s in joints)
            {
                var j = s.J; var A = s.A; var B = s.B;
                var pA = PoseOf(A); var pB = PoseOf(B);
                var wa = pA.Apply(s.LocalAnchorA); var wb = pB.Apply(s.LocalAnchorB);
                if (j is SpringJoint sj)
                {
                    var d = wa - wb; float len = d.magnitude;
                    float lo = sj.minDistance, hi = Math.Max(sj.maxDistance, lo);
                    float stretch = len > hi + sj.tolerance ? len - hi : len < lo - sj.tolerance ? len - lo : 0;
                    var n = len > 1e-6f ? d / len : Vector3.up;
                    var relV = A.PointVelocity(wa) - (B?.PointVelocity(wb) ?? Vector3.zero);
                    float f = -sj.spring * stretch - sj.damper * Vector3.Dot(relV, n);
                    if (stretch == 0) f = -sj.damper * Vector3.Dot(relV, n) * 0;   // inside the slack range: no force
                    Push(A, n * f, wa, dt); if (B != null && B.Dynamic) Push(B, -n * f, wb, dt);
                }
                else if (j is HingeJoint hj && hj.useSpring)
                {
                    var sp = hj.spring;
                    float angle = HingeAngle(s) * Mathf.Deg2Rad, target = sp.targetPosition * Mathf.Deg2Rad;
                    var axisW = (pA.q * s.FrameA) * Vector3.right;
                    float w = Vector3.Dot(A.W - (B?.W ?? Vector3.zero), axisW);
                    float torque = sp.spring * (target - angle) - sp.damper * w;
                    Twist(A, axisW * torque, dt); if (B != null && B.Dynamic) Twist(B, -axisW * torque, dt);
                }
                else if (j is ConfigurableJoint cj)
                {
                    // linear drives toward targetPosition (in the joint frame), angular slerp / XY drives toward targetRotation
                    var frame = pB.q * s.FrameB;
                    var err = Quaternion.Inverse(frame) * (wa - wb);
                    var relV = Quaternion.Inverse(frame) * (A.PointVelocity(wa) - (B?.PointVelocity(wb) ?? Vector3.zero));
                    var tgt = -cj.targetPosition; var tv = cj.targetVelocity;
                    var drives = new[] { cj.xDrive, cj.yDrive, cj.zDrive };
                    var f = Vector3.zero;
                    for (int k = 0; k < 3; k++)
                    {
                        var dr = drives[k]; if (dr.positionSpring == 0 && dr.positionDamper == 0) continue;
                        float fk = dr.positionSpring * (tgt[k] - err[k]) + dr.positionDamper * (tv[k] - relV[k]);
                        f[k] = Mathf.Clamp(fk, -dr.maximumForce, dr.maximumForce);
                    }
                    if (f != Vector3.zero) { var fw = frame * f; Push(A, fw, wa, dt); if (B != null && B.Dynamic) Push(B, -fw, wb, dt); }
                    var drv = cj.rotationDriveMode == RotationDriveMode.Slerp ? cj.slerpDrive : cj.angularXDrive;
                    if (drv.positionSpring != 0 || drv.positionDamper != 0)
                    {
                        var qa = pA.q * s.FrameA; var target = frame * Quaternion.Inverse(cj.targetRotation);
                        var dq = target * Quaternion.Inverse(qa);
                        dq.ToAngleAxis(out float ang, out var ax); if (ang > 180) ang -= 360;
                        var rotErr = float.IsNaN(ax.x) ? Vector3.zero : ax * (ang * Mathf.Deg2Rad);
                        var wrel = A.W - (B?.W ?? Vector3.zero);
                        var torque = rotErr * drv.positionSpring + (frame * cj.targetAngularVelocity - wrel) * drv.positionDamper;
                        if (torque.magnitude > drv.maximumForce) torque = torque.normalized * drv.maximumForce;
                        Twist(A, torque, dt); if (B != null && B.Dynamic) Twist(B, -torque, dt);
                    }
                }
            }
        }

        static void Push(Body b, Vector3 f, Vector3 at, float dt)
        {
            if (!b.Dynamic || b.Sleeping) return;
            b.V += f * (b.InvMass * dt);
            b.W += PhysicsWorld.InvInertiaWorld(b) * Vector3.Cross(at - b.ComWorld, f) * dt;
        }
        static void Twist(Body b, Vector3 t, float dt)
        {
            if (!b.Dynamic || b.Sleeping) return;
            b.W += PhysicsWorld.InvInertiaWorld(b) * t * dt;
        }

        /// <summary>Swing-twist decomposition (twist about X, swing about Y / Z of the connected frame), radians.</summary>
        public static Vector3 SwingTwist(Quaternion rel)
        {
            if (rel.w < 0) rel = new Quaternion(-rel.x, -rel.y, -rel.z, -rel.w);
            float tl = Mathf.Sqrt(rel.x * rel.x + rel.w * rel.w);
            var twist = tl > 1e-9f ? new Quaternion(rel.x / tl, 0, 0, rel.w / tl) : Quaternion.identity;
            var swing = rel * Quaternion.Inverse(twist);
            if (swing.w < 0) swing = new Quaternion(-swing.x, -swing.y, -swing.z, -swing.w);
            return new Vector3(2 * Mathf.Atan2(twist.x, twist.w), 2 * Mathf.Asin(Mathf.Clamp(swing.y, -1, 1)), 2 * Mathf.Asin(Mathf.Clamp(swing.z, -1, 1)));
        }

        /// <summary>Hinge angle in degrees (rotation of A's frame about the hinge axis relative to the start).</summary>
        public static float HingeAngle(JointState s)
        {
            var qa = PoseOf(s.A).q * s.FrameA; var qb = PoseOf(s.B).q * s.FrameB;
            var rel = Quaternion.Inverse(qb) * qa;
            // twist about X
            float ang = 2 * Mathf.Atan2(rel.x, rel.w) * Mathf.Rad2Deg;
            if (ang > 180) ang -= 360; if (ang < -180) ang += 360;
            return ang;
        }

        internal static List<JRow> Prepare(List<JointState> joints, float dt)
        {
            float invDt = 1 / dt;
            var rows = new List<JRow>();
            foreach (var s in joints)
            {
                var j = s.J; var A = s.A; var B = Or(s.B);
                if (!A.Dynamic && !B.Dynamic) continue;
                var pA = PoseOf(s.A); var pB = PoseOf(s.B);
                var wa = pA.Apply(s.LocalAnchorA); var wb = pB.Apply(s.LocalAnchorB);
                var frameB = pB.q * s.FrameB; var frameA = pA.q * s.FrameA;
                ConfigurableJointMotion[] lin, ang;
                float linLimit = 0; float[] angLo = new float[3], angHi = new float[3];
                switch (j)
                {
                    case FixedJoint: lin = Locked3(); ang = Locked3(); break;
                    case HingeJoint hj:
                        lin = Locked3();
                        ang = new[] { hj.useLimits ? ConfigurableJointMotion.Limited : ConfigurableJointMotion.Free, ConfigurableJointMotion.Locked, ConfigurableJointMotion.Locked };
                        if (hj.useLimits) { angLo[0] = hj.limits.min; angHi[0] = hj.limits.max; }
                        break;
                    case CharacterJoint ch:
                        lin = Locked3(); ang = new[] { ConfigurableJointMotion.Limited, ConfigurableJointMotion.Limited, ConfigurableJointMotion.Limited };
                        angLo[0] = ch.lowTwistLimit.limit; angHi[0] = ch.highTwistLimit.limit;
                        angLo[1] = -ch.swing1Limit.limit; angHi[1] = ch.swing1Limit.limit; angLo[2] = -ch.swing2Limit.limit; angHi[2] = ch.swing2Limit.limit;
                        break;
                    case ConfigurableJoint cj:
                        lin = new[] { cj.xMotion, cj.yMotion, cj.zMotion }; ang = new[] { cj.angularXMotion, cj.angularYMotion, cj.angularZMotion };
                        linLimit = cj.linearLimit.limit;
                        angLo[0] = cj.lowAngularXLimit.limit; angHi[0] = cj.highAngularXLimit.limit;
                        angLo[1] = -cj.angularYLimit.limit; angHi[1] = cj.angularYLimit.limit; angLo[2] = -cj.angularZLimit.limit; angHi[2] = cj.angularZLimit.limit;
                        break;
                    default: continue;   // SpringJoint: force only
                }
                // linear rows along the connected frame's axes
                var err = wa - wb;
                for (int k = 0; k < 3; k++)
                {
                    if (lin[k] == ConfigurableJointMotion.Free) continue;
                    var n = frameB * Axis(k);
                    float e = Vector3.Dot(err, n);
                    var r = LinRow(A, B, n, wa, wb, s);
                    if (lin[k] == ConfigurableJointMotion.Limited)
                    {
                        float cd = Math.Min(0.01f, 0.49f * linLimit);
                        if (e > linLimit - cd) { r.Bias = (e - linLimit) * invDt; r.Hi = 0; }
                        else if (e < -linLimit + cd) { r.Bias = (e + linLimit) * invDt; r.Lo = 0; }
                        else continue;
                    }
                    else r.Bias = e * invDt;
                    rows.Add(r);
                }
                // angular rows: relative rotation of A's frame in B's frame → twist (X) / swing (Y, Z) small angles
                var angErr = SwingTwist(Quaternion.Inverse(frameB) * frameA);
                for (int k = 0; k < 3; k++)
                {
                    if (ang[k] == ConfigurableJointMotion.Free) continue;
                    var a = frameB * Axis(k);
                    var r = AngRow(A, B, a, s);
                    float e = angErr[k];
                    if (ang[k] == ConfigurableJointMotion.Limited)
                    {
                        // speculative limit: active within contactDistance (Unity 0 → PhysX default min(0.1 rad, 0.49·range))
                        float lo = angLo[k] * Mathf.Deg2Rad, hi = angHi[k] * Mathf.Deg2Rad;
                        float cd = Math.Min(0.1f, 0.49f * Math.Max(0, hi - lo));
                        if (e > hi - cd) { r.Bias = (e - hi) * invDt; r.Hi = 0; }
                        else if (e < lo + cd) { r.Bias = (e - lo) * invDt; r.Lo = 0; }
                        else continue;
                    }
                    else r.Bias = e * invDt;
                    rows.Add(r);
                }
                // hinge motor: velocity row about the axis with an impulse cap
                if (j is HingeJoint hm && hm.useMotor)
                {
                    var m = hm.motor;
                    var a = frameB * Vector3.right;
                    var r = AngRow(A, B, a, s);
                    r.Target = m.targetVelocity * Mathf.Deg2Rad;
                    float cap = m.force * dt;
                    r.Lo = m.freeSpin ? 0 : -cap; r.Hi = cap;
                    if (m.freeSpin && r.Target < 0) { r.Lo = -cap; r.Hi = 0; }
                    r.Bias = 0; r.Motor = true;
                    rows.Add(r);
                }
            }
            return rows;
        }

        /// <summary>
        /// Joint projection (PhysX joint projection as exposed by ConfigurableJoint.projectionMode and
        /// CharacterJoint.enableProjection): after the solve, when the locked degrees of freedom drift past
        /// projectionAngle / projectionDistance the joint's own body is snapped back onto the connected frame. Velocities
        /// are left alone, like PhysX.
        /// </summary>
        internal static void Project(List<JointState> joints)
        {
            foreach (var s in joints)
            {
                var j = s.J; var A = s.A;
                if (A == null || !A.Dynamic || A.Sleeping) continue;
                ConfigurableJointMotion[] lin, ang; float dist, angle;
                switch (j)
                {
                    case ConfigurableJoint cj when cj.projectionMode == JointProjectionMode.PositionAndRotation:
                        lin = new[] { cj.xMotion, cj.yMotion, cj.zMotion }; ang = new[] { cj.angularXMotion, cj.angularYMotion, cj.angularZMotion };
                        dist = cj.projectionDistance; angle = cj.projectionAngle; break;
                    case CharacterJoint ch when ch.enableProjection:
                        lin = Locked3(); ang = new[] { ConfigurableJointMotion.Free, ConfigurableJointMotion.Free, ConfigurableJointMotion.Free };
                        dist = ch.projectionDistance; angle = ch.projectionAngle; break;
                    default: continue;
                }
                var pB = PoseOf(s.B);
                var frameB = pB.q * s.FrameB;
                // angular: remove the locked rotation components (twist about X is kept when X is not locked)
                bool lockX = ang[0] == ConfigurableJointMotion.Locked, lockYZ = ang[1] == ConfigurableJointMotion.Locked && ang[2] == ConfigurableJointMotion.Locked;
                if (lockYZ)
                {
                    var frameA = A.PxPose.q * s.FrameA;
                    var rel = Quaternion.Inverse(frameB) * frameA;
                    if (rel.w < 0) rel = new Quaternion(-rel.x, -rel.y, -rel.z, -rel.w);
                    float tl = Mathf.Sqrt(rel.x * rel.x + rel.w * rel.w);
                    var keep = lockX || tl < 1e-9f ? Quaternion.identity : new Quaternion(rel.x / tl, 0, 0, rel.w / tl);
                    if (Quaternion.Angle(rel, keep) > angle)
                    {
                        var com = A.ComWorld;
                        var q = frameB * keep * Quaternion.Inverse(s.FrameA);
                        A.PxPose = new PxPose(com - q * A.ComLocal, q);
                    }
                }
                // linear: pull the anchor back along the locked axes of the connected frame
                var wa = A.PxPose.Apply(s.LocalAnchorA); var wb = pB.Apply(s.LocalAnchorB);
                var err = wa - wb; var corr = Vector3.zero;
                for (int k = 0; k < 3; k++)
                    if (lin[k] == ConfigurableJointMotion.Locked) { var n = frameB * Axis(k); corr += Vector3.Dot(err, n) * n; }
                if (corr.magnitude > dist) A.PxPose = new PxPose(A.PxPose.p - corr, A.PxPose.q);
            }
        }

        static ConfigurableJointMotion[] Locked3() => new[] { ConfigurableJointMotion.Locked, ConfigurableJointMotion.Locked, ConfigurableJointMotion.Locked };
        static Vector3 Axis(int k) => k == 0 ? Vector3.right : k == 1 ? Vector3.up : Vector3.forward;

        static JRow LinRow(Body A, Body B, Vector3 n, Vector3 wa, Vector3 wb, JointState s)
        {
            var r = new JRow { A = A, B = B, N = n, Owner = s };
            r.CA = Vector3.Cross(wa - A.ComWorld, n); r.CB = Vector3.Cross(wb - B.ComWorld, n);
            r.IA = A.InvIw * r.CA; r.IB = B.InvIw * r.CB;
            float k = Vector3.Dot(Vector3.Scale(A.InvMassAxis, n), n) + Vector3.Dot(Vector3.Scale(B.InvMassAxis, n), n) + Vector3.Dot(r.CA, r.IA) + Vector3.Dot(r.CB, r.IB);
            r.VelMul = k > 1e-12f ? 1 / k : 0;
            return r;
        }

        static JRow AngRow(Body A, Body B, Vector3 a, JointState s)
        {
            var r = new JRow { A = A, B = B, N = Vector3.zero, CA = a, CB = a, Owner = s, Angular = true, Axis = a };
            r.IA = A.InvIw * a; r.IB = B.InvIw * a;
            float k = Vector3.Dot(a, r.IA) + Vector3.Dot(a, r.IB);
            r.VelMul = k > 1e-12f ? 1 / k : 0;
            return r;
        }

        /// <summary>Equality rows of one joint, solved as a block (PhysX constraint preprocessing makes the rows of a joint
        /// independent, so a single joint converges in one iteration).</summary>
        internal sealed class Block { public List<JRow> Rows; public double[,] KInv; }

        internal static List<Block> Blocks(List<JRow> rows)
        {
            var res = new List<Block>();
            foreach (var g in rows.Where(r => !r.Motor && r.Lo == float.MinValue && r.Hi == float.MaxValue).GroupBy(r => r.Owner))
            {
                var rs = g.ToList(); int n = rs.Count;
                var K = new double[n, n];
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        var a = rs[i]; var b = rs[j];
                        K[i, j] = Vector3.Dot(a.N, Vector3.Scale(a.A.InvMassAxis, b.N)) + Vector3.Dot(a.CA, b.IA)
                                + Vector3.Dot(a.N, Vector3.Scale(a.B.InvMassAxis, b.N)) + Vector3.Dot(a.CB, b.IB);
                    }
                var inv = Invert(K, n);
                if (inv != null) { res.Add(new Block { Rows = rs, KInv = inv }); foreach (var r in rs) r.InBlock = true; }
            }
            return res;
        }

        static double[,] Invert(double[,] m, int n)
        {
            var a = new double[n, 2 * n];
            for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) a[i, j] = m[i, j]; a[i, n + i] = 1; }
            for (int c = 0; c < n; c++)
            {
                int p = c; for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
                if (Math.Abs(a[p, c]) < 1e-12) return null;
                if (p != c) for (int k = 0; k < 2 * n; k++) (a[p, k], a[c, k]) = (a[c, k], a[p, k]);
                double d = a[c, c]; for (int k = 0; k < 2 * n; k++) a[c, k] /= d;
                for (int r = 0; r < n; r++)
                {
                    if (r == c) continue; double f = a[r, c]; if (f == 0) continue;
                    for (int k = 0; k < 2 * n; k++) a[r, k] -= f * a[c, k];
                }
            }
            var res = new double[n, n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) res[i, j] = a[i, n + j];
            return res;
        }

        static float RelVel(JRow r) => Vector3.Dot(r.N, r.A.SV - r.B.SV) + Vector3.Dot(r.CA, r.A.SW) - Vector3.Dot(r.CB, r.B.SW);

        static void Apply(JRow r, float d)
        {
            r.A.SV += Vector3.Scale(r.A.InvMassAxis, r.N * d); r.A.SW += r.IA * d;
            r.B.SV -= Vector3.Scale(r.B.InvMassAxis, r.N * d); r.B.SW -= r.IB * d;
        }

        internal static void Solve(List<JRow> rows, List<Block> blocks, bool biased)
        {
            // motors first, then the hard equality blocks and limits (the last word goes to the constraints)
            foreach (var r in rows) if (r.Motor) SolveRow(r, biased);
            foreach (var b in blocks)
            {
                int n = b.Rows.Count; var rhs = new double[n];
                for (int i = 0; i < n; i++) { var r = b.Rows[i]; rhs[i] = -(RelVel(r) + (biased ? r.Bias : 0) - r.Target); }
                for (int i = 0; i < n; i++)
                {
                    double d = 0; for (int j = 0; j < n; j++) d += b.KInv[i, j] * rhs[j];
                    var r = b.Rows[i]; r.Applied += (float)d; Apply(r, (float)d);
                }
            }
            foreach (var r in rows) if (!r.Motor && !r.InBlock) SolveRow(r, biased);
        }

        static void SolveRow(JRow r, bool biased)
        {
            float d = -(RelVel(r) + (biased ? r.Bias : 0) - r.Target) * r.VelMul;
            float nf = Mathf.Clamp(r.Applied + d, r.Lo, r.Hi);
            d = nf - r.Applied; r.Applied = nf;
            if (d != 0) Apply(r, d);
        }

        /// <summary>Constraint forces → currentForce / currentTorque, joint breaking (OnJointBreak).</summary>
        internal static void Finish(List<JRow> rows, List<JointState> joints, float dt)
        {
            foreach (var s in joints) { s.LinImpulse = Vector3.zero; s.AngImpulse = Vector3.zero; }
            foreach (var r in rows)
            {
                if (r.Angular) r.Owner.AngImpulse += r.Axis * r.Applied; else r.Owner.LinImpulse += r.N * r.Applied;
            }
            foreach (var s in joints)
            {
                var j = s.J;
                j.m_CurrentForce = s.LinImpulse / dt; j.m_CurrentTorque = s.AngImpulse / dt;
                float f = j.m_CurrentForce.magnitude, t = j.m_CurrentTorque.magnitude;
                if (f > j.breakForce || t > j.breakTorque)
                {
                    j.m_Broken = true;
                    PhysicsWorld.PendingJointBreaks.Add((j, f > j.breakForce ? f : t));
                }
            }
        }
    }
}

namespace UnityEngine
{
    public partial class Joint
    {
        internal Vector3 m_CurrentForce, m_CurrentTorque; internal bool m_Broken;
        public Joint() { }
        public Rigidbody connectedBody { get => NRef<Rigidbody>("m_ConnectedBody"); set => NSetRef("m_ConnectedBody", value); }
        public ArticulationBody connectedArticulationBody { get => null; set { } }
        public Vector3 axis { get => NGet("m_Axis") != null ? NVec3("m_Axis") : Vector3.right; set => NSetVec3("m_Axis", value); }
        public Vector3 anchor { get => NVec3("m_Anchor"); set => NSetVec3("m_Anchor", value); }
        public Vector3 connectedAnchor { get => NVec3("m_ConnectedAnchor"); set => NSetVec3("m_ConnectedAnchor", value); }
        internal void SetConnectedAnchorAuto(Vector3 v) => NSetVec3("m_ConnectedAnchor", v);
        public bool autoConfigureConnectedAnchor { get => NBool("m_AutoConfigureConnectedAnchor", true); set => NSetB("m_AutoConfigureConnectedAnchor", value); }
        public float breakForce { get => NGet("m_BreakForce") is MiniUnity.Yaml.YScalar s && s.Value.Length > 0 ? s.F : float.PositiveInfinity; set => NSetF("m_BreakForce", value); }
        public float breakTorque { get => NGet("m_BreakTorque") is MiniUnity.Yaml.YScalar s && s.Value.Length > 0 ? s.F : float.PositiveInfinity; set => NSetF("m_BreakTorque", value); }
        public bool enableCollision { get => NBool("m_EnableCollision"); set => NSetB("m_EnableCollision", value); }
        public bool enablePreprocessing { get => NBool("m_EnablePreprocessing", true); set => NSetB("m_EnablePreprocessing", value); }
        public float massScale { get => NFloat("m_MassScale", 1); set => NSetF("m_MassScale", value); }
        public float connectedMassScale { get => NFloat("m_ConnectedMassScale", 1); set => NSetF("m_ConnectedMassScale", value); }
        public Vector3 currentForce => m_CurrentForce;
        public Vector3 currentTorque => m_CurrentTorque;
        internal Matrix4x4 GetLocalPoseMatrix(int bodyIndex) => Matrix4x4.identity;
    }

    public partial class HingeJoint
    {
        public HingeJoint() { }
        public JointMotor motor { get => new() { targetVelocity = NFloat("m_Motor.targetVelocity"), force = NFloat("m_Motor.force"), freeSpin = NBool("m_Motor.freeSpin") }; set { NSetF("m_Motor.targetVelocity", value.targetVelocity); NSetF("m_Motor.force", value.force); NSetB("m_Motor.freeSpin", value.freeSpin); } }
        public JointLimits limits
        {
            get => new() { min = NFloat("m_Limits.min"), max = NFloat("m_Limits.max"), bounciness = NFloat("m_Limits.bounciness"), bounceMinVelocity = NFloat("m_Limits.bounceMinVelocity", 0.2f), contactDistance = NFloat("m_Limits.contactDistance") };
            set { NSetF("m_Limits.min", value.min); NSetF("m_Limits.max", value.max); NSetF("m_Limits.bounciness", value.bounciness); NSetF("m_Limits.bounceMinVelocity", value.bounceMinVelocity); NSetF("m_Limits.contactDistance", value.contactDistance); }
        }
        public JointSpring spring { get => new() { spring = NFloat("m_Spring.spring"), damper = NFloat("m_Spring.damper"), targetPosition = NFloat("m_Spring.targetPosition") }; set { NSetF("m_Spring.spring", value.spring); NSetF("m_Spring.damper", value.damper); NSetF("m_Spring.targetPosition", value.targetPosition); } }
        public bool useMotor { get => NBool("m_UseMotor"); set => NSetB("m_UseMotor", value); }
        public bool useLimits { get => NBool("m_UseLimits"); set => NSetB("m_UseLimits", value); }
        public bool extendedLimits { get => NBool("m_ExtendedLimits"); set => NSetB("m_ExtendedLimits", value); }
        public bool useSpring { get => NBool("m_UseSpring"); set => NSetB("m_UseSpring", value); }
        public bool useAcceleration { get => NBool("m_UseAcceleration"); set => NSetB("m_UseAcceleration", value); }
        public float angle { get { var s = State(); return s != null ? MiniUnity.Physics3D.JointSolver.HingeAngle(s) : 0; } }
        public float velocity
        {
            get
            {
                var s = State(); if (s == null) return 0;
                var axisW = (s.A.PxPose.q * s.FrameA) * Vector3.right;
                return Vector3.Dot(s.A.W - (s.B?.W ?? Vector3.zero), axisW) * Mathf.Rad2Deg;
            }
        }
        MiniUnity.Physics3D.JointState State() => MiniUnity.Physics3D.PhysicsWorld.JointStateOf(this);
    }

    public partial class SpringJoint
    {
        public SpringJoint() { }
        public float spring { get => NFloat("m_Spring", 10); set => NSetF("m_Spring", value); }
        public float damper { get => NFloat("m_Damper", 0.2f); set => NSetF("m_Damper", value); }
        public float minDistance { get => NFloat("m_MinDistance"); set => NSetF("m_MinDistance", value); }
        public float maxDistance { get => NFloat("m_MaxDistance"); set => NSetF("m_MaxDistance", value); }
        public float tolerance { get => NFloat("m_Tolerance", 0.025f); set => NSetF("m_Tolerance", value); }
    }

    public partial class FixedJoint { public FixedJoint() { } }

    public partial class CharacterJoint
    {
        public CharacterJoint() { }
        public Vector3 swingAxis { get => NGet("m_SwingAxis") != null ? NVec3("m_SwingAxis") : Vector3.up; set => NSetVec3("m_SwingAxis", value); }
        static SoftJointLimit Lim(Object o, string k, float def) => new() { limit = ((Joint)o).NFloat(k + ".limit", def), bounciness = ((Joint)o).NFloat(k + ".bounciness"), contactDistance = ((Joint)o).NFloat(k + ".contactDistance") };
        void SetLim(string k, SoftJointLimit v) { NSetF(k + ".limit", v.limit); NSetF(k + ".bounciness", v.bounciness); NSetF(k + ".contactDistance", v.contactDistance); }
        public SoftJointLimitSpring twistLimitSpring { get => new() { spring = NFloat("m_TwistLimitSpring.spring"), damper = NFloat("m_TwistLimitSpring.damper") }; set { NSetF("m_TwistLimitSpring.spring", value.spring); NSetF("m_TwistLimitSpring.damper", value.damper); } }
        public SoftJointLimitSpring swingLimitSpring { get => new() { spring = NFloat("m_SwingLimitSpring.spring"), damper = NFloat("m_SwingLimitSpring.damper") }; set { NSetF("m_SwingLimitSpring.spring", value.spring); NSetF("m_SwingLimitSpring.damper", value.damper); } }
        public SoftJointLimit lowTwistLimit { get => Lim(this, "m_LowTwistLimit", -20); set => SetLim("m_LowTwistLimit", value); }
        public SoftJointLimit highTwistLimit { get => Lim(this, "m_HighTwistLimit", 70); set => SetLim("m_HighTwistLimit", value); }
        public SoftJointLimit swing1Limit { get => Lim(this, "m_Swing1Limit", 40); set => SetLim("m_Swing1Limit", value); }
        public SoftJointLimit swing2Limit { get => Lim(this, "m_Swing2Limit", 40); set => SetLim("m_Swing2Limit", value); }
        public bool enableProjection { get => NBool("m_EnableProjection"); set => NSetB("m_EnableProjection", value); }
        public float projectionDistance { get => NFloat("m_ProjectionDistance", 0.1f); set => NSetF("m_ProjectionDistance", value); }
        public float projectionAngle { get => NFloat("m_ProjectionAngle", 180); set => NSetF("m_ProjectionAngle", value); }
    }

    public partial class ConfigurableJoint
    {
        public ConfigurableJoint() { }
        public Vector3 secondaryAxis { get => NGet("m_SecondaryAxis") != null ? NVec3("m_SecondaryAxis") : Vector3.up; set => NSetVec3("m_SecondaryAxis", value); }
        public ConfigurableJointMotion xMotion { get => (ConfigurableJointMotion)NInt("m_XMotion", 2); set => NSetI("m_XMotion", (int)value); }
        public ConfigurableJointMotion yMotion { get => (ConfigurableJointMotion)NInt("m_YMotion", 2); set => NSetI("m_YMotion", (int)value); }
        public ConfigurableJointMotion zMotion { get => (ConfigurableJointMotion)NInt("m_ZMotion", 2); set => NSetI("m_ZMotion", (int)value); }
        public ConfigurableJointMotion angularXMotion { get => (ConfigurableJointMotion)NInt("m_AngularXMotion", 2); set => NSetI("m_AngularXMotion", (int)value); }
        public ConfigurableJointMotion angularYMotion { get => (ConfigurableJointMotion)NInt("m_AngularYMotion", 2); set => NSetI("m_AngularYMotion", (int)value); }
        public ConfigurableJointMotion angularZMotion { get => (ConfigurableJointMotion)NInt("m_AngularZMotion", 2); set => NSetI("m_AngularZMotion", (int)value); }
        SoftJointLimit Lim(string k) => new() { limit = NFloat(k + ".limit"), bounciness = NFloat(k + ".bounciness"), contactDistance = NFloat(k + ".contactDistance") };
        void SetLim(string k, SoftJointLimit v) { NSetF(k + ".limit", v.limit); NSetF(k + ".bounciness", v.bounciness); NSetF(k + ".contactDistance", v.contactDistance); }
        SoftJointLimitSpring LS(string k) => new() { spring = NFloat(k + ".spring"), damper = NFloat(k + ".damper") };
        void SetLS(string k, SoftJointLimitSpring v) { NSetF(k + ".spring", v.spring); NSetF(k + ".damper", v.damper); }
        JointDrive Drive(string k) => new() { positionSpring = NFloat(k + ".positionSpring"), positionDamper = NFloat(k + ".positionDamper"), maximumForce = NGet(k + ".maximumForce") is MiniUnity.Yaml.YScalar s && s.Value.Length > 0 ? s.F : float.MaxValue };
        void SetDrive(string k, JointDrive v) { NSetF(k + ".positionSpring", v.positionSpring); NSetF(k + ".positionDamper", v.positionDamper); NSetF(k + ".maximumForce", v.maximumForce); }
        public SoftJointLimitSpring linearLimitSpring { get => LS("m_LinearLimitSpring"); set => SetLS("m_LinearLimitSpring", value); }
        public SoftJointLimitSpring angularXLimitSpring { get => LS("m_AngularXLimitSpring"); set => SetLS("m_AngularXLimitSpring", value); }
        public SoftJointLimitSpring angularYZLimitSpring { get => LS("m_AngularYZLimitSpring"); set => SetLS("m_AngularYZLimitSpring", value); }
        public SoftJointLimit linearLimit { get => Lim("m_LinearLimit"); set => SetLim("m_LinearLimit", value); }
        public SoftJointLimit lowAngularXLimit { get => Lim("m_LowAngularXLimit"); set => SetLim("m_LowAngularXLimit", value); }
        public SoftJointLimit highAngularXLimit { get => Lim("m_HighAngularXLimit"); set => SetLim("m_HighAngularXLimit", value); }
        public SoftJointLimit angularYLimit { get => Lim("m_AngularYLimit"); set => SetLim("m_AngularYLimit", value); }
        public SoftJointLimit angularZLimit { get => Lim("m_AngularZLimit"); set => SetLim("m_AngularZLimit", value); }
        public Vector3 targetPosition { get => NVec3("m_TargetPosition"); set => NSetVec3("m_TargetPosition", value); }
        public Vector3 targetVelocity { get => NVec3("m_TargetVelocity"); set => NSetVec3("m_TargetVelocity", value); }
        public JointDrive xDrive { get => Drive("m_XDrive"); set => SetDrive("m_XDrive", value); }
        public JointDrive yDrive { get => Drive("m_YDrive"); set => SetDrive("m_YDrive", value); }
        public JointDrive zDrive { get => Drive("m_ZDrive"); set => SetDrive("m_ZDrive", value); }
        public Quaternion targetRotation { get => NGet("m_TargetRotation") != null ? MiniUnity.Serializer.ReadQuat(NGet("m_TargetRotation")) : Quaternion.identity; set => NSet("m_TargetRotation", MiniUnity.Serializer.Vec(value)); }
        public Vector3 targetAngularVelocity { get => NVec3("m_TargetAngularVelocity"); set => NSetVec3("m_TargetAngularVelocity", value); }
        public RotationDriveMode rotationDriveMode { get => (RotationDriveMode)NInt("m_RotationDriveMode"); set => NSetI("m_RotationDriveMode", (int)value); }
        public JointDrive angularXDrive { get => Drive("m_AngularXDrive"); set => SetDrive("m_AngularXDrive", value); }
        public JointDrive angularYZDrive { get => Drive("m_AngularYZDrive"); set => SetDrive("m_AngularYZDrive", value); }
        public JointDrive slerpDrive { get => Drive("m_SlerpDrive"); set => SetDrive("m_SlerpDrive", value); }
        public JointProjectionMode projectionMode { get => (JointProjectionMode)NInt("m_ProjectionMode"); set => NSetI("m_ProjectionMode", (int)value); }
        public float projectionDistance { get => NFloat("m_ProjectionDistance", 0.1f); set => NSetF("m_ProjectionDistance", value); }
        public float projectionAngle { get => NFloat("m_ProjectionAngle", 180); set => NSetF("m_ProjectionAngle", value); }
        public bool configuredInWorldSpace { get => NBool("m_ConfiguredInWorldSpace"); set => NSetB("m_ConfiguredInWorldSpace", value); }
        public bool swapBodies { get => NBool("m_SwapBodies"); set => NSetB("m_SwapBodies", value); }
    }
}

namespace UnityEngine
{
    // value types of the joint API (plain data, as in Unity's managed PhysicsModule)
    public partial struct WheelFrictionCurve
    {
        float m_ExtremumSlip, m_ExtremumValue, m_AsymptoteSlip, m_AsymptoteValue, m_Stiffness;
        public float extremumSlip { get => m_ExtremumSlip; set => m_ExtremumSlip = value; }
        public float extremumValue { get => m_ExtremumValue; set => m_ExtremumValue = value; }
        public float asymptoteSlip { get => m_AsymptoteSlip; set => m_AsymptoteSlip = value; }
        public float asymptoteValue { get => m_AsymptoteValue; set => m_AsymptoteValue = value; }
        public float stiffness { get => m_Stiffness; set => m_Stiffness = value; }
    }
    public partial struct SoftJointLimit
    {
        float m_Limit, m_Bounciness, m_ContactDistance;
        public float limit { get => m_Limit; set => m_Limit = value; }
        public float bounciness { get => m_Bounciness; set => m_Bounciness = value; }
        public float contactDistance { get => m_ContactDistance; set => m_ContactDistance = value; }
    }
    public partial struct SoftJointLimitSpring
    {
        float m_Spring, m_Damper;
        public float spring { get => m_Spring; set => m_Spring = value; }
        public float damper { get => m_Damper; set => m_Damper = value; }
    }
    public partial struct JointDrive
    {
        float m_PositionSpring, m_PositionDamper, m_MaximumForce; int m_UseAcceleration;
        public float positionSpring { get => m_PositionSpring; set => m_PositionSpring = value; }
        public float positionDamper { get => m_PositionDamper; set => m_PositionDamper = value; }
        public float maximumForce { get => m_MaximumForce; set => m_MaximumForce = value; }
        public bool useAcceleration { get => m_UseAcceleration == 1; set => m_UseAcceleration = value ? 1 : 0; }
        public JointDriveMode mode { get => JointDriveMode.None; set { } }
    }
    public partial struct JointMotor
    {
        float m_TargetVelocity, m_Force; int m_FreeSpin;
        public float targetVelocity { get => m_TargetVelocity; set => m_TargetVelocity = value; }
        public float force { get => m_Force; set => m_Force = value; }
        public bool freeSpin { get => m_FreeSpin == 1; set => m_FreeSpin = value ? 1 : 0; }
    }
    public partial struct JointLimits
    {
        float m_Min, m_Max, m_Bounciness, m_BounceMinVelocity, m_ContactDistance;
        public float min { get => m_Min; set => m_Min = value; }
        public float max { get => m_Max; set => m_Max = value; }
        public float bounciness { get => m_Bounciness; set => m_Bounciness = value; }
        public float bounceMinVelocity { get => m_BounceMinVelocity; set => m_BounceMinVelocity = value; }
        public float contactDistance { get => m_ContactDistance; set => m_ContactDistance = value; }
    }
}
