// ArticulationBody on top of the maximal-coordinate solver: every articulation link is a hidden Rigidbody ("proxy") that the
// world simulates like any other body, and every non-root link adds a hidden ConfigurableJoint to its parent link with the
// articulation's locks and limits (Fixed / Prismatic / Revolute / Spherical). Drives are solved implicitly per degree of freedom
// (spring-damper with backward Euler on the joint's effective mass), so robot-arm stiffness values stay stable.
// Reduced-coordinate quantities (jointPosition, jointVelocity, GetJointPositions, drive targets…) are measured from the
// link poses. Not simulated: inverse dynamics (jacobian, gravity / Coriolis / joint forces), jointFriction.
using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity.Yaml;
using UnityEngine;

namespace MiniUnity.Physics3D
{
    public static class ArticulationSystem
    {
        internal sealed class Link
        {
            public ArticulationBody Ab;
            public Rigidbody Proxy;
            public ConfigurableJoint Joint;     // null for the root
            public Link Parent;
            public JointState State;
            public float LinearOffset;          // prismatic: connected anchor shifted to the limit centre (metres along the axis)
        }

        static readonly Dictionary<ArticulationBody, Link> links = new();
        static readonly List<Link> live = new();

        public static void Reset() { links.Clear(); live.Clear(); }

        internal static Link LinkOf(ArticulationBody ab) => ab != null && links.TryGetValue(ab, out var l) ? l : null;

        /// <summary>Nearest enabled ArticulationBody above (or on) a transform.</summary>
        internal static ArticulationBody Owner(Transform t)
        {
            for (; t != null; t = t.m_Parent)
                if (t.gameObject.GetComponent<ArticulationBody>() is ArticulationBody ab && ab.enabled && !ab.m_Destroyed)
                    return ab;
            return null;
        }

        internal static ArticulationBody ParentOf(ArticulationBody ab) => Owner(ab.transform.m_Parent);

        static Rigidbody MakeProxy(ArticulationBody ab)
        {
            var rb = new Rigidbody { m_GameObject = ab.gameObject, m_Native = new YMap() };
            return rb;
        }

        static void CacheProxy(ArticulationBody ab, Rigidbody rb)
        {
            PhysicsWorld.EnsureSettings();
            rb.m_Cached = true;
            rb.m_MassCache = Math.Max(ab.mass, 1e-7f);
            rb.m_DragCache = ab.linearDamping; rb.m_AngularDragCache = ab.angularDamping;
            rb.m_UseGravityCache = ab.useGravity;
            rb.m_IsKinematicCache = ab.isRoot && ab.immovable;
            rb.m_CollisionModeCache = ab.collisionDetectionMode;
            rb.m_AutoCom = ab.automaticCenterOfMass; rb.m_AutoTensor = ab.automaticInertiaTensor;
            rb.m_ComCache = ab.m_ComCache; rb.m_TensorCache = ab.m_TensorCache; rb.m_TensorRotCache = ab.m_TensorRotCache;
            if (rb.m_MaxDepenetration < 0) rb.m_MaxDepenetration = PhysicsWorld.DefaultMaxDepenetrationVelocity;
            if (rb.m_MaxAngularVelocity < 0) rb.m_MaxAngularVelocity = PhysicsWorld.DefaultMaxAngularSpeed;
            if (rb.m_SolverIterations < 0) rb.m_SolverIterations = PhysicsWorld.SolverIterations;
            if (rb.m_SolverVelocityIterations < 0) rb.m_SolverVelocityIterations = PhysicsWorld.SolverVelocityIterations;
            if (rb.m_SleepThreshold < 0) rb.m_SleepThreshold = PhysicsWorld.SleepThreshold;
        }

        /// <summary>Called from PhysicsWorld.Sync: proxies for the live links (added to the world's bodies by the caller).</summary>
        internal static List<Rigidbody> Collect(IEnumerable<GameObject> objects)
        {
            live.Clear();
            var seen = new HashSet<ArticulationBody>();
            foreach (var go in objects)
            {
                if (go.m_Destroyed || !go.activeInHierarchy) continue;
                foreach (var c in go.m_Components)
                    if (c is ArticulationBody ab && !ab.m_Destroyed && ab.enabled)
                    {
                        seen.Add(ab);
                        if (!links.TryGetValue(ab, out var l)) { l = new Link { Ab = ab, Proxy = MakeProxy(ab) }; links[ab] = l; }
                        CacheProxy(ab, l.Proxy);
                        live.Add(l);
                    }
            }
            foreach (var k in links.Keys.Where(k => !seen.Contains(k)).ToList()) links.Remove(k);
            foreach (var l in live)
            {
                var parent = ParentOf(l.Ab);
                var pl = parent != null ? LinkOf(parent) : null;
                if (pl != l.Parent) { l.Parent = pl; l.State = null; l.Joint = null; }
            }
            // parents before children (index order)
            live.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
            return live.Select(l => l.Proxy).ToList();
        }

        static int Depth(Link l) { int d = 0; for (var p = l.Parent; p != null; p = p.Parent) d++; return d; }

        /// <summary>Joint states of the non-root links (after the bodies exist).</summary>
        internal static IEnumerable<JointState> Joints(Func<Rigidbody, Body> bodyOf)
        {
            foreach (var l in live)
            {
                if (l.Parent == null) continue;
                var A = bodyOf(l.Proxy); var B = bodyOf(l.Parent.Proxy);
                if (A == null || B == null) continue;
                if (l.Joint == null) { l.Joint = new ConfigurableJoint { m_GameObject = l.Ab.gameObject, m_Native = new YMap() }; Configure(l); }
                else ConfigureLimits(l);
                if (l.State == null) l.State = JointSolver.Track(l.Joint, A, B);
                else { l.State.A = A; l.State.B = B; }
                yield return l.State;
            }
        }

        static ConfigurableJointMotion M(ArticulationDofLock k) => k switch
        {
            ArticulationDofLock.LockedMotion => ConfigurableJointMotion.Locked,
            ArticulationDofLock.LimitedMotion => ConfigurableJointMotion.Limited,
            _ => ConfigurableJointMotion.Free,
        };

        /// <summary>Joint frame from the anchor (X = primary axis), anchors from anchorPosition / parentAnchorPosition.</summary>
        static void Configure(Link l)
        {
            var ab = l.Ab; var j = l.Joint;
            var q = ab.anchorRotation;
            j.axis = q * Vector3.right;
            j.secondaryAxis = q * Vector3.up;
            j.anchor = ab.anchorPosition;
            j.autoConfigureConnectedAnchor = false;
            var parentT = l.Parent.Ab.transform;
            var worldAnchor = ab.transform.TransformPoint(ab.anchorPosition);
            var parentAnchor = ab.matchAnchors || ab.computeParentAnchor ? parentT.InverseTransformPoint(worldAnchor) : ab.parentAnchorPosition;
            l.LinearOffset = 0;
            if (ab.jointType == ArticulationJointType.PrismaticJoint)
            {
                int axis = PrismaticAxis(ab);
                var d = axis == 0 ? ab.xDrive : axis == 1 ? ab.yDrive : ab.zDrive;
                var lk = axis == 0 ? ab.linearLockX : axis == 1 ? ab.linearLockY : ab.linearLockZ;
                if (lk == ArticulationDofLock.LimitedMotion) l.LinearOffset = (d.lowerLimit + d.upperLimit) * 0.5f;
                var axisW = ab.transform.rotation * (q * Unit(axis));
                parentAnchor = parentT.InverseTransformPoint(parentT.TransformPoint(parentAnchor) + axisW * l.LinearOffset);
            }
            j.connectedAnchor = parentAnchor;
            ConfigureLimits(l);
        }

        static Vector3 Unit(int axis) => axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

        internal static int PrismaticAxis(ArticulationBody ab) =>
            ab.linearLockX != ArticulationDofLock.LockedMotion ? 0 : ab.linearLockY != ArticulationDofLock.LockedMotion ? 1 : ab.linearLockZ != ArticulationDofLock.LockedMotion ? 2 : 0;

        static void ConfigureLimits(Link l)
        {
            var ab = l.Ab; var j = l.Joint;
            var locked = ConfigurableJointMotion.Locked;
            j.xMotion = j.yMotion = j.zMotion = locked;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = locked;
            switch (ab.jointType)
            {
                case ArticulationJointType.PrismaticJoint:
                {
                    int axis = PrismaticAxis(ab);
                    var d = axis == 0 ? ab.xDrive : axis == 1 ? ab.yDrive : ab.zDrive;
                    var m = M(axis == 0 ? ab.linearLockX : axis == 1 ? ab.linearLockY : ab.linearLockZ);
                    if (axis == 0) j.xMotion = m; else if (axis == 1) j.yMotion = m; else j.zMotion = m;
                    j.linearLimit = new SoftJointLimit { limit = Math.Max(0, (d.upperLimit - d.lowerLimit) * 0.5f) };
                    break;
                }
                case ArticulationJointType.RevoluteJoint:
                    j.angularXMotion = M(ab.twistLock);
                    j.lowAngularXLimit = new SoftJointLimit { limit = ab.xDrive.lowerLimit };
                    j.highAngularXLimit = new SoftJointLimit { limit = ab.xDrive.upperLimit };
                    break;
                case ArticulationJointType.SphericalJoint:
                    j.angularXMotion = M(ab.twistLock);
                    j.angularYMotion = M(ab.swingYLock);
                    j.angularZMotion = M(ab.swingZLock);
                    j.lowAngularXLimit = new SoftJointLimit { limit = ab.xDrive.lowerLimit };
                    j.highAngularXLimit = new SoftJointLimit { limit = ab.xDrive.upperLimit };
                    j.angularYLimit = new SoftJointLimit { limit = Math.Max(Math.Abs(ab.yDrive.lowerLimit), Math.Abs(ab.yDrive.upperLimit)) };
                    j.angularZLimit = new SoftJointLimit { limit = Math.Max(Math.Abs(ab.zDrive.lowerLimit), Math.Abs(ab.zDrive.upperLimit)) };
                    break;
            }
        }

        // ------------------------------------------------------------------ reduced coordinates

        /// <summary>Degrees of freedom of a link: (kind, axis) with kind 0 = linear, 1 = angular; angular axis 0 = twist, 1 = swing Y, 2 = swing Z.</summary>
        internal static List<(int kind, int axis)> Dofs(ArticulationBody ab)
        {
            var r = new List<(int, int)>();
            if (ab.isRoot) return r;
            switch (ab.jointType)
            {
                case ArticulationJointType.PrismaticJoint: r.Add((0, PrismaticAxis(ab))); break;
                case ArticulationJointType.RevoluteJoint: r.Add((1, 0)); break;
                case ArticulationJointType.SphericalJoint:
                    if (ab.twistLock != ArticulationDofLock.LockedMotion) r.Add((1, 0));
                    if (ab.swingYLock != ArticulationDofLock.LockedMotion) r.Add((1, 1));
                    if (ab.swingZLock != ArticulationDofLock.LockedMotion) r.Add((1, 2));
                    break;
            }
            return r;
        }

        static (PxPose pA, PxPose pB, Quaternion frameA, Quaternion frameB) Frames(JointState s)
        {
            var pA = s.A.PxPose; var pB = s.B != null ? s.B.PxPose : PxPose.Identity;
            return (pA, pB, pA.q * s.FrameA, pB.q * s.FrameB);
        }

        /// <summary>Position of one degree of freedom: metres (linear) or radians (angular).</summary>
        internal static float Position(Link l, (int kind, int axis) dof)
        {
            var s = l.State; if (s == null) return 0;
            var (pA, pB, fa, fb) = Frames(s);
            if (dof.kind == 0)
            {
                var d = Quaternion.Inverse(fb) * (pA.Apply(s.LocalAnchorA) - pB.Apply(s.LocalAnchorB));
                return d[dof.axis] + (dof.axis == PrismaticAxis(l.Ab) ? l.LinearOffset : 0);
            }
            return JointSolver.SwingTwist(Quaternion.Inverse(fb) * fa)[dof.axis];
        }

        static Vector3 AxisWorld(Link l, (int kind, int axis) dof) { var (_, _, _, fb) = Frames(l.State); return fb * Unit(dof.axis); }

        internal static float Velocity(Link l, (int kind, int axis) dof)
        {
            var s = l.State; if (s == null) return 0;
            var n = AxisWorld(l, dof);
            if (dof.kind == 0)
            {
                var (pA, pB, _, _) = Frames(s);
                var wa = pA.Apply(s.LocalAnchorA); var wb = pB.Apply(s.LocalAnchorB);
                return Vector3.Dot(s.A.PointVelocity(wa) - (s.B?.PointVelocity(wb) ?? Vector3.zero), n);
            }
            return Vector3.Dot(s.A.W - (s.B?.W ?? Vector3.zero), n);
        }

        // ------------------------------------------------------------------ drives (implicit spring-damper per DOF)

        internal static void ApplyDrives(float dt)
        {
            foreach (var l in live)
            {
                if (l.State == null || l.Parent == null) continue;
                var A = l.State.A; var B = l.State.B;
                if (!A.Dynamic && (B == null || !B.Dynamic)) continue;
                foreach (var dof in Dofs(l.Ab))
                {
                    var d = dof.kind == 0 ? (dof.axis == 0 ? l.Ab.xDrive : dof.axis == 1 ? l.Ab.yDrive : l.Ab.zDrive)
                                          : (dof.axis == 0 ? l.Ab.xDrive : dof.axis == 1 ? l.Ab.yDrive : l.Ab.zDrive);
                    if (d.stiffness == 0 && d.damping == 0 && d.driveType != ArticulationDriveType.Velocity && d.driveType != ArticulationDriveType.Target) continue;
                    var n = AxisWorld(l, dof);
                    var (pA, pB, _, _) = Frames(l.State);
                    var wa = pA.Apply(l.State.LocalAnchorA); var wb = pB.Apply(l.State.LocalAnchorB);
                    // joint-space inverse inertia: linear DOFs move the body without rotating it; angular DOFs rotate it
                    // about the joint anchor (parallel-axis term), which is how the locked rows let it move
                    float invA = dof.kind == 0 ? (A.Dynamic ? A.InvMass : 0) : AnchorInvInertia(A, wa, n);
                    float invB = B == null ? 0 : dof.kind == 0 ? (B.Dynamic ? B.InvMass : 0) : AnchorInvInertia(B, wb, n);
                    float invM = invA + invB;
                    if (invM <= 0) continue;
                    float m = 1 / invM;
                    float x = Position(l, dof), v = Velocity(l, dof);
                    float target = dof.kind == 0 ? d.target : d.target * Mathf.Deg2Rad;
                    float vt = dof.kind == 0 ? d.targetVelocity : d.targetVelocity * Mathf.Deg2Rad;
                    float impulse;
                    switch (d.driveType)
                    {
                        case ArticulationDriveType.Target: impulse = m * ((target - x) / dt - v); break;
                        case ArticulationDriveType.Velocity: impulse = m * (vt - v); break;
                        default:
                        {
                            float k = d.stiffness, c = d.damping;
                            float mm = d.driveType == ArticulationDriveType.Acceleration ? 1 : m;   // acceleration drives ignore the inertia
                            float dv = (dt * k * (target - x) + dt * c * (vt - v) - dt * dt * k * v) / (mm + dt * c + dt * dt * k);
                            impulse = dv * m;
                            break;
                        }
                    }
                    float cap = d.forceLimit > 0 ? d.forceLimit * dt : float.MaxValue;
                    impulse = Mathf.Clamp(impulse, -cap, cap);
                    if (dof.kind == 0) { Shift(A, n * (impulse * invA)); if (B != null) Shift(B, -n * (impulse * invB)); }
                    else { Spin(A, wa, n * (impulse * invA)); if (B != null) Spin(B, wb, -n * (impulse * invB)); }
                }
            }
        }

        /// <summary>1 / (inertia about the axis through the anchor) = 1 / (I_com,n + m·r⊥²).</summary>
        static float AnchorInvInertia(Body b, Vector3 anchor, Vector3 n)
        {
            if (!b.Dynamic) return 0;
            float k = Vector3.Dot(n, PhysicsWorld.InvInertiaWorld(b) * n);
            float icom = k > 1e-12f ? 1 / k : 0;
            var r = b.ComWorld - anchor; r -= n * Vector3.Dot(r, n);
            float i = icom + b.Mass * r.sqrMagnitude;
            return i > 1e-12f ? 1 / i : 0;
        }

        /// <summary>Velocity change of a translation along the joint axis.</summary>
        static void Shift(Body b, Vector3 dv)
        {
            if (!b.Dynamic) return;
            b.V += dv;
            if (b.Sleeping) PhysicsWorld.Wake(b);
        }

        /// <summary>Velocity change of a rotation about the joint anchor: ω += dω, v_com += dω × (com − anchor).</summary>
        static void Spin(Body b, Vector3 anchor, Vector3 dw)
        {
            if (!b.Dynamic) return;
            b.W += dw;
            b.V += Vector3.Cross(dw, b.ComWorld - anchor);
            if (b.Sleeping) PhysicsWorld.Wake(b);
        }

        // ------------------------------------------------------------------ reduced-coordinate projection

        /// <summary>Relative rotation of the child's joint frame from twist (X) and swing (Y, Z) angles: inverse of JointSolver.SwingTwist.</summary>
        static Quaternion FromSwingTwist(float tw, float sy, float sz)
        {
            var twist = new Quaternion(Mathf.Sin(tw * 0.5f), 0, 0, Mathf.Cos(tw * 0.5f));
            float y = Mathf.Sin(sy * 0.5f), z = Mathf.Sin(sz * 0.5f);
            float w = Mathf.Sqrt(Mathf.Max(0, 1 - y * y - z * z));
            return new Quaternion(0, y, z, w) * twist;
        }

        static (float lo, float hi) Range(ArticulationBody ab, (int kind, int axis) dof)
        {
            var d = dof.axis == 0 ? ab.xDrive : dof.axis == 1 ? ab.yDrive : ab.zDrive;
            var lk = dof.kind == 0 ? (dof.axis == 0 ? ab.linearLockX : dof.axis == 1 ? ab.linearLockY : ab.linearLockZ)
                                   : (dof.axis == 0 ? ab.twistLock : dof.axis == 1 ? ab.swingYLock : ab.swingZLock);
            if (ab.jointType == ArticulationJointType.RevoluteJoint && dof.kind == 1) lk = ab.twistLock;
            if (lk != ArticulationDofLock.LimitedMotion) return (float.MinValue, float.MaxValue);
            float k = dof.kind == 1 ? Mathf.Deg2Rad : 1;
            return (d.lowerLimit * k, d.upperLimit * k);
        }

        /// <summary>Child pose that puts the link's DOFs at <paramref name="x"/> (other DOFs locked) relative to its parent's current pose.</summary>
        static PxPose PoseFor(Link l, List<(int kind, int axis)> dofs, IReadOnlyList<float> x)
        {
            var s = l.State;
            var pB = s.B != null ? s.B.PxPose : PxPose.Identity;
            var fb = pB.q * s.FrameB;
            float tw = 0, sy = 0, sz = 0; var offset = Vector3.zero;
            for (int i = 0; i < dofs.Count && i < x.Count; i++)
            {
                var dof = dofs[i];
                if (dof.kind == 0) offset[dof.axis] = x[i] - (dof.axis == PrismaticAxis(l.Ab) ? l.LinearOffset : 0);
                else if (dof.axis == 0) tw = x[i]; else if (dof.axis == 1) sy = x[i]; else sz = x[i];
            }
            var q = fb * FromSwingTwist(tw, sy, sz) * Quaternion.Inverse(s.FrameA);
            var anchorW = pB.Apply(s.LocalAnchorB) + fb * offset;
            return new PxPose(anchorW - q * s.LocalAnchorA, q);
        }

        /// <summary>
        /// After the solve, every link is put back exactly on its parent through its joint coordinates (clamped to the limits)
        /// and gets the parent's velocity plus the joint rates – what a reduced-coordinate articulation guarantees by
        /// construction. Parents first, so whole chains stay rigid however light or long the links are.
        /// </summary>
        internal static void Project()
        {
            foreach (var l in live)
            {
                if (l.State == null || l.Parent == null) continue;
                var A = l.State.A; var B = l.State.B;
                if (A == null || !A.Dynamic) continue;
                var dofs = Dofs(l.Ab);
                var x = new List<float>(); var rates = new List<float>();
                foreach (var dof in dofs)
                {
                    float xi = Position(l, dof), vi = Velocity(l, dof);
                    var (lo, hi) = Range(l.Ab, dof);
                    if (xi <= lo) { xi = lo; vi = Math.Max(vi, 0); }
                    else if (xi >= hi) { xi = hi; vi = Math.Min(vi, 0); }
                    x.Add(xi); rates.Add(vi);
                }
                var pose = PoseFor(l, dofs, x);
                A.PxPose = pose; A.CurPose = pose;
                // velocities: parent's motion at the anchor + joint rates along the joint axes
                var fb = (B != null ? B.PxPose.q : Quaternion.identity) * l.State.FrameB;
                var anchorW = (B != null ? B.PxPose : PxPose.Identity).Apply(l.State.LocalAnchorB);
                var w = B?.W ?? Vector3.zero;
                var vAnchor = B?.PointVelocity(anchorW) ?? Vector3.zero;
                for (int i = 0; i < dofs.Count; i++)
                {
                    var axis = fb * Unit(dofs[i].axis);
                    if (dofs[i].kind == 0) vAnchor += axis * rates[i]; else w += axis * rates[i];
                }
                var childAnchor = pose.Apply(l.State.LocalAnchorA);
                A.W = w;
                A.V = vAnchor + Vector3.Cross(w, A.ComWorld - childAnchor);
            }
        }

        // ------------------------------------------------------------------ setting joint positions

        /// <summary>Places a link so that its DOFs read <paramref name="values"/> (parent pose kept), then carries its children along.</summary>
        internal static void SetPositions(Link l, IReadOnlyList<float> values)
        {
            if (l.State == null || l.Parent == null) return;
            Place(l.Ab.transform, PoseFor(l, Dofs(l.Ab), values));
        }

        /// <summary>Moves a link's transform (descendant links follow through the hierarchy) and keeps their bodies in step.</summary>
        internal static void Place(Transform t, PxPose to)
        {
            var rbody = links.Values.FirstOrDefault(x => x.Ab.transform == t);
            t.SetPositionAndRotation(to.p, to.q);
            if (rbody != null && PhysicsWorld.BodyOf(rbody.Proxy) is Body b) { b.PxPose = to; b.CurPose = b.PrevPose = to; }
            foreach (var l in links.Values)
            {
                if (l.Ab.transform == t || !l.Ab.transform.IsChildOf(t)) continue;
                if (Owner(l.Ab.transform.m_Parent) == null) continue;
                if (PhysicsWorld.BodyOf(l.Proxy) is Body cb) { var np = new PxPose(l.Ab.transform.position, l.Ab.transform.rotation); cb.PxPose = np; cb.CurPose = cb.PrevPose = np; }
            }
        }

        internal static IEnumerable<Link> Tree(ArticulationBody any)
        {
            var root = any; for (var p = ParentOf(root); p != null; p = ParentOf(p)) root = p;
            return live.Where(l => l.Ab == root || l.Ab.transform.IsChildOf(root.transform));
        }
    }
}

namespace UnityEngine
{
    public partial struct ArticulationReducedSpace
    {
        float m_P0, m_P1, m_P2;

        public ArticulationReducedSpace(float a) { m_P0 = a; m_P1 = m_P2 = 0; dofCount = 1; }
        public ArticulationReducedSpace(float a, float b) { m_P0 = a; m_P1 = b; m_P2 = 0; dofCount = 2; }
        public ArticulationReducedSpace(float a, float b, float c) { m_P0 = a; m_P1 = b; m_P2 = c; dofCount = 3; }

        public float this[int i]
        {
            get
            {
                if (i < 0 || i >= dofCount) throw new IndexOutOfRangeException();
                return i == 0 ? m_P0 : i == 1 ? m_P1 : m_P2;
            }
            set
            {
                if (i < 0 || i >= dofCount) throw new IndexOutOfRangeException();
                if (i == 0) m_P0 = value; else if (i == 1) m_P1 = value; else m_P2 = value;
            }
        }
    }

    public partial class ArticulationBody
    {
        internal Vector3 m_ComCache, m_TensorCache = Vector3.one; internal Quaternion m_TensorRotCache = Quaternion.identity;

        public ArticulationBody() { }

        MiniUnity.Physics3D.ArticulationSystem.Link L
        {
            get
            {
                var l = MiniUnity.Physics3D.ArticulationSystem.LinkOf(this);
                if (l == null) { MiniUnity.Physics3D.PhysicsWorld.Sync(); l = MiniUnity.Physics3D.ArticulationSystem.LinkOf(this); }
                return l;
            }
        }
        Rigidbody P => L?.Proxy;
        /// <summary>Joint settings changed: the link's joint is rebuilt on the next physics sync.</summary>
        void Changed() { if (MiniUnity.Physics3D.ArticulationSystem.LinkOf(this) is { } l) { l.Joint = null; l.State = null; } }

        ArticulationDrive Drive(string k) => new()
        {
            lowerLimit = NFloat(k + ".lowerLimit"), upperLimit = NFloat(k + ".upperLimit"), stiffness = NFloat(k + ".stiffness"),
            damping = NFloat(k + ".damping"), forceLimit = NFloat(k + ".forceLimit", float.MaxValue), target = NFloat(k + ".target"),
            targetVelocity = NFloat(k + ".targetVelocity"), driveType = (ArticulationDriveType)NInt(k + ".driveType"),
        };
        void SetDrive(string k, ArticulationDrive d)
        {
            NSetF(k + ".lowerLimit", d.lowerLimit); NSetF(k + ".upperLimit", d.upperLimit); NSetF(k + ".stiffness", d.stiffness);
            NSetF(k + ".damping", d.damping); NSetF(k + ".forceLimit", d.forceLimit); NSetF(k + ".target", d.target);
            NSetF(k + ".targetVelocity", d.targetVelocity); NSetI(k + ".driveType", (int)d.driveType);
        }
        Quaternion Q(string k) => NGet(k) != null ? MiniUnity.Serializer.ReadQuat(NGet(k)) : Quaternion.identity;
        void SetQ(string k, Quaternion q) => NSet(k, MiniUnity.Serializer.Vec(q));

        public ArticulationJointType jointType { get => (ArticulationJointType)NInt("m_ArticulationJointType"); set { NSetI("m_ArticulationJointType", (int)value); Changed(); } }
        public Vector3 anchorPosition { get => NVec3("m_AnchorPosition"); set { NSetVec3("m_AnchorPosition", value); Changed(); } }
        public Vector3 parentAnchorPosition { get => NVec3("m_ParentAnchorPosition"); set { NSetVec3("m_ParentAnchorPosition", value); Changed(); } }
        public Quaternion anchorRotation { get => Q("m_AnchorRotation"); set { SetQ("m_AnchorRotation", value); Changed(); } }
        public Quaternion parentAnchorRotation { get => Q("m_ParentAnchorRotation"); set { SetQ("m_ParentAnchorRotation", value); Changed(); } }
        public bool isRoot => MiniUnity.Physics3D.ArticulationSystem.ParentOf(this) == null;
        public bool matchAnchors { get => NBool("m_MatchAnchors", true); set => NSetB("m_MatchAnchors", value); }
        public bool computeParentAnchor { get => NBool("m_ComputeParentAnchor", true); set => NSetB("m_ComputeParentAnchor", value); }
        public ArticulationDofLock linearLockX { get => (ArticulationDofLock)NInt("m_LinearX"); set { NSetI("m_LinearX", (int)value); Changed(); } }
        public ArticulationDofLock linearLockY { get => (ArticulationDofLock)NInt("m_LinearY"); set { NSetI("m_LinearY", (int)value); Changed(); } }
        public ArticulationDofLock linearLockZ { get => (ArticulationDofLock)NInt("m_LinearZ"); set { NSetI("m_LinearZ", (int)value); Changed(); } }
        public ArticulationDofLock swingYLock { get => (ArticulationDofLock)NInt("m_SwingY"); set { NSetI("m_SwingY", (int)value); Changed(); } }
        public ArticulationDofLock swingZLock { get => (ArticulationDofLock)NInt("m_SwingZ"); set { NSetI("m_SwingZ", (int)value); Changed(); } }
        public ArticulationDofLock twistLock { get => (ArticulationDofLock)NInt("m_Twist"); set { NSetI("m_Twist", (int)value); Changed(); } }
        public ArticulationDrive xDrive { get => Drive("m_XDrive"); set => SetDrive("m_XDrive", value); }
        public ArticulationDrive yDrive { get => Drive("m_YDrive"); set => SetDrive("m_YDrive", value); }
        public ArticulationDrive zDrive { get => Drive("m_ZDrive"); set => SetDrive("m_ZDrive", value); }
        public bool immovable { get => NBool("m_Immovable"); set { NSetB("m_Immovable", value); if (P != null) P.m_IsKinematicCache = value && isRoot; } }
        public bool useGravity { get => NBool("m_UseGravity", true); set { NSetB("m_UseGravity", value); if (P != null) P.m_UseGravityCache = value; } }
        public float linearDamping { get => NFloat("m_LinearDamping", 0.05f); set { NSetF("m_LinearDamping", value); if (P != null) P.m_DragCache = value; } }
        public float angularDamping { get => NFloat("m_AngularDamping", 0.05f); set { NSetF("m_AngularDamping", value); if (P != null) P.m_AngularDragCache = value; } }
        public float jointFriction { get => NFloat("m_JointFriction", 0.05f); set => NSetF("m_JointFriction", value); }
        public float mass { get => NFloat("m_Mass", 1); set { NSetF("m_Mass", value); if (P != null) P.mass = value; } }
        public CollisionDetectionMode collisionDetectionMode { get => (CollisionDetectionMode)NInt("m_CollisionDetectionMode"); set { NSetI("m_CollisionDetectionMode", (int)value); if (P != null) P.m_CollisionModeCache = value; } }

        public Vector3 velocity { get => P?.velocity ?? Vector3.zero; set { if (P != null) P.velocity = value; } }
        public Vector3 angularVelocity { get => P?.angularVelocity ?? Vector3.zero; set { if (P != null) P.angularVelocity = value; } }
        public bool automaticCenterOfMass { get => NBool("m_ImplicitCom", true); set => NSetB("m_ImplicitCom", value); }
        public bool automaticInertiaTensor { get => NBool("m_ImplicitTensor", true); set => NSetB("m_ImplicitTensor", value); }
        public Vector3 centerOfMass { get => P?.centerOfMass ?? m_ComCache; set { m_ComCache = value; automaticCenterOfMass = false; if (P != null) P.centerOfMass = value; } }
        public Vector3 worldCenterOfMass => P?.worldCenterOfMass ?? transform.position;
        public Vector3 inertiaTensor { get => P?.inertiaTensor ?? m_TensorCache; set { m_TensorCache = value; automaticInertiaTensor = false; if (P != null) P.inertiaTensor = value; } }
        public Quaternion inertiaTensorRotation { get => P?.inertiaTensorRotation ?? m_TensorRotCache; set { m_TensorRotCache = value; if (P != null) P.inertiaTensorRotation = value; } }
        public void ResetCenterOfMass() { automaticCenterOfMass = true; P?.ResetCenterOfMass(); }
        public void ResetInertiaTensor() { automaticInertiaTensor = true; P?.ResetInertiaTensor(); }
        public float sleepThreshold { get => P?.sleepThreshold ?? 0.005f; set { if (P != null) P.sleepThreshold = value; } }
        public int solverIterations { get => P?.solverIterations ?? 4; set { if (P != null) P.solverIterations = value; } }
        public int solverVelocityIterations { get => P?.solverVelocityIterations ?? 1; set { if (P != null) P.solverVelocityIterations = value; } }
        public float maxAngularVelocity { get => P?.maxAngularVelocity ?? 7; set { if (P != null) P.maxAngularVelocity = value; } }
        public float maxLinearVelocity { get => P?.maxLinearVelocity ?? 1e16f; set { if (P != null) P.maxLinearVelocity = value; } }
        public float maxJointVelocity { get => NFloat("m_MaxJointVelocity", 1e16f); set => NSetF("m_MaxJointVelocity", value); }
        public float maxDepenetrationVelocity { get => P?.maxDepenetrationVelocity ?? 1e32f; set { if (P != null) P.maxDepenetrationVelocity = value; } }
        public LayerMask excludeLayers { get => NInt("m_ExcludeLayers.m_Bits"); set => NSetI("m_ExcludeLayers.m_Bits", value.value); }
        public LayerMask includeLayers { get => NInt("m_IncludeLayers.m_Bits"); set => NSetI("m_IncludeLayers.m_Bits", value.value); }

        public void AddForce(Vector3 force, ForceMode mode) => P?.AddForce(force, mode);
        public void AddForce(Vector3 force) => P?.AddForce(force);
        public void AddRelativeForce(Vector3 force, ForceMode mode) => P?.AddRelativeForce(force, mode);
        public void AddRelativeForce(Vector3 force) => P?.AddRelativeForce(force);
        public void AddTorque(Vector3 torque, ForceMode mode) => P?.AddTorque(torque, mode);
        public void AddTorque(Vector3 torque) => P?.AddTorque(torque);
        public void AddRelativeTorque(Vector3 torque, ForceMode mode) => P?.AddRelativeTorque(torque, mode);
        public void AddRelativeTorque(Vector3 torque) => P?.AddRelativeTorque(torque);
        public void AddForceAtPosition(Vector3 force, Vector3 position, ForceMode mode) => P?.AddForceAtPosition(force, position, mode);
        public void AddForceAtPosition(Vector3 force, Vector3 position) => P?.AddForceAtPosition(force, position);
        public void Sleep() => P?.Sleep();
        public bool IsSleeping() => P?.IsSleeping() ?? false;
        public void WakeUp() => P?.WakeUp();
        public Vector3 GetPointVelocity(Vector3 worldPoint) => P?.GetPointVelocity(worldPoint) ?? Vector3.zero;
        public Vector3 GetRelativePointVelocity(Vector3 relativePoint) => GetPointVelocity(transform.TransformPoint(relativePoint));
        public Vector3 GetClosestPoint(Vector3 point)
        {
            var best = point; float bestD = float.MaxValue;
            foreach (var c in GetComponentsInChildren<Collider>())
            {
                if (MiniUnity.Physics3D.ArticulationSystem.Owner(c.transform) != this) continue;
                var p = c.ClosestPoint(point); float d = (p - point).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        public void TeleportRoot(Vector3 position, Quaternion rotation)
        {
            if (!isRoot) return;
            MiniUnity.Physics3D.ArticulationSystem.Place(transform, new MiniUnity.Physics3D.PxPose(position, rotation));
        }

        public int dofCount => MiniUnity.Physics3D.ArticulationSystem.Dofs(this).Count;
        public int index
        {
            get
            {
                var tree = MiniUnity.Physics3D.ArticulationSystem.Tree(this).ToList();
                for (int i = 0; i < tree.Count; i++) if (tree[i].Ab == this) return i;
                return 0;
            }
        }

        ArticulationReducedSpace Space(Func<MiniUnity.Physics3D.ArticulationSystem.Link, (int, int), float> read)
        {
            var l = L; var dofs = MiniUnity.Physics3D.ArticulationSystem.Dofs(this);
            var r = new ArticulationReducedSpace { dofCount = dofs.Count };
            for (int i = 0; i < dofs.Count; i++) r[i] = l != null ? read(l, dofs[i]) : 0;
            return r;
        }

        public ArticulationReducedSpace jointPosition
        {
            get => Space(MiniUnity.Physics3D.ArticulationSystem.Position);
            set
            {
                if (L is not { } l) return;
                var v = new List<float>(); for (int i = 0; i < value.dofCount; i++) v.Add(value[i]);
                MiniUnity.Physics3D.ArticulationSystem.SetPositions(l, v);
            }
        }

        public ArticulationReducedSpace jointVelocity
        {
            get => Space(MiniUnity.Physics3D.ArticulationSystem.Velocity);
            set
            {
                // child velocity = parent velocity + joint rates along the joint axes
                if (L is not { } l || l.Parent == null || P == null) return;
                var dofs = MiniUnity.Physics3D.ArticulationSystem.Dofs(this);
                var pp = l.Parent.Proxy;
                var lin = pp.GetPointVelocity(worldCenterOfMass); var ang = pp.angularVelocity;
                var pr = l.Parent.Ab.transform.rotation;
                var frame = pr * anchorRotation;
                for (int i = 0; i < dofs.Count && i < value.dofCount; i++)
                {
                    var axis = frame * (dofs[i].axis == 0 ? Vector3.right : dofs[i].axis == 1 ? Vector3.up : Vector3.forward);
                    if (dofs[i].kind == 0) lin += axis * value[i]; else ang += axis * value[i];
                }
                var anchorW = transform.TransformPoint(anchorPosition);
                P.angularVelocity = ang;
                P.velocity = lin + Vector3.Cross(ang - pp.angularVelocity, worldCenterOfMass - anchorW);
            }
        }

        int Gather(List<float> into, Func<ArticulationBody, ArticulationReducedSpace> read)
        {
            into.Clear();
            foreach (var l in MiniUnity.Physics3D.ArticulationSystem.Tree(this))
            {
                var s = read(l.Ab);
                for (int i = 0; i < s.dofCount; i++) into.Add(s[i]);
            }
            return into.Count;
        }

        void Scatter(List<float> from, Action<ArticulationBody, ArticulationReducedSpace> write)
        {
            int at = 0;
            foreach (var l in MiniUnity.Physics3D.ArticulationSystem.Tree(this))
            {
                int n = l.Ab.dofCount; if (n == 0) continue;
                var s = new ArticulationReducedSpace { dofCount = n };
                for (int i = 0; i < n && at < from.Count; i++) s[i] = from[at++];
                write(l.Ab, s);
            }
        }

        public int GetJointPositions(List<float> positions) => Gather(positions, a => a.jointPosition);
        public void SetJointPositions(List<float> positions) => Scatter(positions, (a, s) => a.jointPosition = s);
        public int GetJointVelocities(List<float> velocities) => Gather(velocities, a => a.jointVelocity);
        public void SetJointVelocities(List<float> velocities) => Scatter(velocities, (a, s) => a.jointVelocity = s);

        public int GetDofStartIndices(List<int> dofStartIndices)
        {
            dofStartIndices.Clear(); int at = 0;
            foreach (var l in MiniUnity.Physics3D.ArticulationSystem.Tree(this)) { dofStartIndices.Add(at); at += l.Ab.dofCount; }
            return dofStartIndices.Count;
        }

        ArticulationDrive DriveOf((int kind, int axis) d) => d.axis == 0 ? xDrive : d.axis == 1 ? yDrive : zDrive;
        void SetDriveOf((int kind, int axis) d, ArticulationDrive v) { if (d.axis == 0) xDrive = v; else if (d.axis == 1) yDrive = v; else zDrive = v; }

        int GatherDrive(List<float> into, Func<ArticulationDrive, (int, int), float> read)
        {
            into.Clear();
            foreach (var l in MiniUnity.Physics3D.ArticulationSystem.Tree(this))
                foreach (var d in MiniUnity.Physics3D.ArticulationSystem.Dofs(l.Ab)) into.Add(read(l.Ab.DriveOf(d), d));
            return into.Count;
        }
        void ScatterDrive(List<float> from, Func<ArticulationDrive, (int kind, int axis), float, ArticulationDrive> write)
        {
            int at = 0;
            foreach (var l in MiniUnity.Physics3D.ArticulationSystem.Tree(this))
                foreach (var d in MiniUnity.Physics3D.ArticulationSystem.Dofs(l.Ab))
                    if (at < from.Count) l.Ab.SetDriveOf(d, write(l.Ab.DriveOf(d), d, from[at++]));
        }

        // drive targets in the reduced space are radians / metres (the ArticulationDrive fields are degrees / metres)
        public int GetDriveTargets(List<float> targets) => GatherDrive(targets, (d, k) => k.Item1 == 1 ? d.target * Mathf.Deg2Rad : d.target);
        public void SetDriveTargets(List<float> targets) => ScatterDrive(targets, (d, k, v) => { d.target = k.kind == 1 ? v * Mathf.Rad2Deg : v; return d; });
        public int GetDriveTargetVelocities(List<float> targetVelocities) => GatherDrive(targetVelocities, (d, k) => k.Item1 == 1 ? d.targetVelocity * Mathf.Deg2Rad : d.targetVelocity);
        public void SetDriveTargetVelocities(List<float> targetVelocities) => ScatterDrive(targetVelocities, (d, k, v) => { d.targetVelocity = k.kind == 1 ? v * Mathf.Rad2Deg : v; return d; });

        ArticulationDrive DriveAt(ArticulationDriveAxis axis) => axis == ArticulationDriveAxis.X ? xDrive : axis == ArticulationDriveAxis.Y ? yDrive : zDrive;
        void SetDriveAt(ArticulationDriveAxis axis, ArticulationDrive d) { if (axis == ArticulationDriveAxis.X) xDrive = d; else if (axis == ArticulationDriveAxis.Y) yDrive = d; else zDrive = d; }
        public void SetDriveTarget(ArticulationDriveAxis axis, float value) { var d = DriveAt(axis); d.target = value; SetDriveAt(axis, d); }
        public void SetDriveTargetVelocity(ArticulationDriveAxis axis, float value) { var d = DriveAt(axis); d.targetVelocity = value; SetDriveAt(axis, d); }
        public void SetDriveLimits(ArticulationDriveAxis axis, float lower, float upper) { var d = DriveAt(axis); d.lowerLimit = lower; d.upperLimit = upper; SetDriveAt(axis, d); Changed(); }
        public void SetDriveStiffness(ArticulationDriveAxis axis, float value) { var d = DriveAt(axis); d.stiffness = value; SetDriveAt(axis, d); }
        public void SetDriveDamping(ArticulationDriveAxis axis, float value) { var d = DriveAt(axis); d.damping = value; SetDriveAt(axis, d); }
        public void SetDriveForceLimit(ArticulationDriveAxis axis, float value) { var d = DriveAt(axis); d.forceLimit = value; SetDriveAt(axis, d); }
    }
}

namespace UnityEngine
{
    public partial class Collider
    {
        public ArticulationBody attachedArticulationBody
        {
            get
            {
                for (var t = transform; t != null; t = t.m_Parent)
                {
                    var go = t.gameObject;
                    if (go.GetComponent<Rigidbody>() != null) return null;
                    if (go.GetComponent<ArticulationBody>() is ArticulationBody ab && ab.enabled) return ab;
                }
                return null;
            }
        }
    }
}
