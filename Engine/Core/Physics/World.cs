// MiniUnity's 3D physics scene, modelled on PhysX 4.1 as used by Unity 2022.3 (formulas from the PhysX sources):
//   unconstrained velocity: v += F/m·dt + impulses; v += g·dt; v *= max(0, 1 − drag·dt); clamp max linear / angular speed
//   contacts within contactOffsetA + contactOffsetB (speculative), PGS solver: `solverIterations` position iterations with
//   penetration bias max(−maxDepenetrationVelocity, sep·0.8/dt), restitution when the approach speed exceeds bounceThreshold,
//   patch friction (2 anchors × 2 tangents, static → dynamic coefficient when broken), positions integrated, then
//   `solverVelocityIterations` velocity iterations without bias; sleeping by mass-normalised kinetic energy (wake counter 0.4 s).
//   Events: OnTrigger* / OnCollision* Enter/Stay/Exit after each simulation step; kinematic MovePosition; interpolation.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity.Physics3D
{
    public struct M3
    {
        public float m00, m01, m02, m10, m11, m12, m20, m21, m22;
        public static M3 Diag(Vector3 d) => new() { m00 = d.x, m11 = d.y, m22 = d.z };
        public static M3 Rot(Quaternion q)
        {
            var m = Matrix4x4.Rotate(q);
            return new M3 { m00 = m.m00, m01 = m.m01, m02 = m.m02, m10 = m.m10, m11 = m.m11, m12 = m.m12, m20 = m.m20, m21 = m.m21, m22 = m.m22 };
        }
        public static M3 operator *(M3 a, M3 b) => new()
        {
            m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20, m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21, m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22,
            m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20, m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21, m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22,
            m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20, m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21, m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22,
        };
        public M3 T => new() { m00 = m00, m01 = m10, m02 = m20, m10 = m01, m11 = m11, m12 = m21, m20 = m02, m21 = m12, m22 = m22 };
        public static Vector3 operator *(M3 a, Vector3 v) => new(a.m00 * v.x + a.m01 * v.y + a.m02 * v.z, a.m10 * v.x + a.m11 * v.y + a.m12 * v.z, a.m20 * v.x + a.m21 * v.y + a.m22 * v.z);
    }

    public sealed class Body
    {
        public Rigidbody Rb;                 // null: static actor of one collider
        public CharacterController Cct;      // kinematic actor of a CharacterController
        public bool IsStatic => Rb == null && Cct == null;
        public bool Kinematic => Cct != null || Rb != null && Rb.m_IsKinematicCache;
        public bool Dynamic => Rb != null && !Rb.m_IsKinematicCache;
        public PxPose PxPose;                    // actor frame (the Rigidbody's Transform)
        public Vector3 ComLocal; public Quaternion InertiaRot = Quaternion.identity; public Vector3 InertiaDiag = Vector3.one;
        public float Mass = 1, InvMass = 1;
        public Vector3 V, W;                 // linear velocity of the centre of mass, angular velocity (world)
        public Vector3 ForceAcc, TorqueAcc, DV, DW;   // accumulated: acceleration terms (×dt later) and velocity changes
        public float WakeCounter = 0.4f; public bool Sleeping;
        public Vector3 SleepLinAcc, SleepAngAcc;
        public bool HasKinTarget; public Vector3 KinTargetP; public Quaternion KinTargetQ;
        public PxPose PrevPose, CurPose; public bool InterpValid;
        public Vector3 LastWrittenP; public Quaternion LastWrittenQ; public bool Written;
        public readonly List<Shape> Shapes = new();
        public int Touching;                 // contacts this step (cluster factor)
        public bool TouchesStatic;
        // solver state
        public Vector3 SV, SW; public M3 InvIw; public Vector3 InvMassAxis; public int Index;

        public Vector3 ComWorld => PxPose.Apply(ComLocal);
        public Vector3 PointVelocity(Vector3 p) => V + Vector3.Cross(W, p - ComWorld);
    }

    public sealed class Shape
    {
        public Collider Col; public Body Body; public Geom Geom; public PxPose Local; public PxPose World;
        public bool Trigger; public float ContactOffset; public int Layer; public Bounds Aabb;
        public float StaticFriction = 0.6f, DynamicFriction = 0.6f, Bounce; public PhysicMaterialCombine FrictionCombine, BounceCombine;
        public int Id;
        // cached from the collider while its properties (m_NVersion), scale and GeometryExtra are unchanged
        public int GeomVer = -1; public Vector3 GeomLossy, Center; public (int, int) GeomExtra; public PhysicMaterial Mat; public bool IsTrigger;
        public int Include, Exclude, Priority;   // layer overrides of the collider (Unity 2022.2+)
    }

    public sealed class Pair
    {
        public Shape A, B;
        public readonly List<Contact> Contacts = new();
        public Vector3 RelVel;               // velocity of B relative to A at the first contact before solving
        public Vector3 Impulse;
        public float[] NormalImpulse = Array.Empty<float>();
        public List<FrictionPatch> FrictionPatches = new();   // friction anchors kept between steps (PhysX friction correlation)
    }

    public sealed class FrictionPatch
    {
        public Vector3 NormalA; public readonly List<Vector3> LocalA = new(), LocalB = new(); public bool Broken;
    }

    public static class PhysicsWorld
    {
        // ------------------------------------------------------------------ settings (ProjectSettings/DynamicsManager.asset)
        public static Vector3 Gravity = new(0, -9.81f, 0);
        public static float DefaultContactOffset = 0.01f, SleepThreshold = 0.005f, BounceThreshold = 2, DefaultMaxDepenetrationVelocity = 10, DefaultMaxAngularSpeed = 7;
        public static int SolverIterations = 6, SolverVelocityIterations = 1;
        public static bool QueriesHitTriggers = true, QueriesHitBackfaces, AutoSyncTransforms, ReuseCollisionCallbacks = true, InvokeCollisionCallbacks = true, ImprovedPatchFriction;
        public static SimulationMode Mode = SimulationMode.FixedUpdate;
        public static readonly uint[] LayerMatrix = Enumerable.Repeat(0xFFFFFFFFu, 32).ToArray();
        public static readonly HashSet<(int, int)> IgnoredPairs = new();
        static bool settingsLoaded;

        public static void LoadSettings()
        {
            settingsLoaded = true;
            try
            {
                var path = System.IO.Path.Combine(AssetIndex.Root ?? ".", "ProjectSettings/DynamicsManager.asset");
                if (!System.IO.File.Exists(path)) return;
                var doc = Yaml.YamlReader.ReadFile(path).FirstOrDefault();
                if (doc?.Body is not Yaml.YMap m) return;
                float F(string k, float d) => m[k] is Yaml.YScalar s && s.Value.Length > 0 ? s.F : d;
                if (m["m_Gravity"] != null) Gravity = Serializer.ReadV3(m["m_Gravity"]);
                BounceThreshold = F("m_BounceThreshold", 2); SleepThreshold = F("m_SleepThreshold", 0.005f);
                DefaultContactOffset = F("m_DefaultContactOffset", 0.01f);
                SolverIterations = (int)F("m_DefaultSolverIterations", 6); SolverVelocityIterations = (int)F("m_DefaultSolverVelocityIterations", 1);
                QueriesHitBackfaces = F("m_QueriesHitBackfaces", 0) != 0; QueriesHitTriggers = F("m_QueriesHitTriggers", 1) != 0;
                AutoSyncTransforms = F("m_AutoSyncTransforms", 0) != 0; ReuseCollisionCallbacks = F("m_ReuseCollisionCallbacks", 1) != 0;
                Mode = F("m_AutoSimulation", 1) != 0 ? SimulationMode.FixedUpdate : SimulationMode.Script;
                if (m["m_SimulationMode"] is Yaml.YScalar sm) Mode = (SimulationMode)(int)sm.L;
                DefaultMaxAngularSpeed = F("m_DefaultMaxAngluarSpeed", F("m_DefaultMaxAngularSpeed", 7));
                DefaultMaxDepenetrationVelocity = F("m_DefaultMaxDepenetrationVelocity", 10);
                ImprovedPatchFriction = F("m_ImprovedPatchFriction", 0) != 0;
                if (m["m_LayerCollisionMatrix"] is Yaml.YScalar lm && lm.Value.Length >= 256)
                    for (int i = 0; i < 32; i++)
                    {
                        // 32 little-endian uint32 rows as hex
                        uint v = 0;
                        for (int b = 0; b < 4; b++) v |= (uint)Convert.ToByte(lm.Value.Substring(i * 8 + b * 2, 2), 16) << (8 * b);
                        LayerMatrix[i] = v;
                    }
            }
            catch { }
        }

        public static bool LayersCollide(int a, int b) => (LayerMatrix[a & 31] & (1u << (b & 31))) != 0;
        public static void SetLayerCollision(int a, int b, bool collide)
        {
            if (collide) { LayerMatrix[a] |= 1u << b; LayerMatrix[b] |= 1u << a; }
            else { LayerMatrix[a] &= ~(1u << b); LayerMatrix[b] &= ~(1u << a); }
        }

        // ------------------------------------------------------------------ scene state
        static readonly Dictionary<Collider, Shape> shapes = new();
        static readonly Dictionary<Component, Body> bodies = new();     // Rigidbody / CharacterController / static collider → actor
        static Dictionary<(Shape, Shape), Pair> pairs = new();
        static HashSet<(Shape, Shape)> triggerPairs = new();
        static int nextShapeId;
        public static float LastStep = 0.02f;
        public static readonly List<string> Warnings = new();
        public static readonly List<(Joint joint, float force)> PendingJointBreaks = new();
        static List<JointState> joints = new();
        public static JointState JointStateOf(Joint j) => JointSolver.StateOf(j);

        public static void Reset()
        {
            shapes.Clear(); bodies.Clear(); pairs.Clear(); triggerPairs.Clear(); IgnoredPairs.Clear(); nextShapeId = 0;
            JointSolver.Reset(); ArticulationSystem.Reset(); joints.Clear(); PendingJointBreaks.Clear();
            settingsLoaded = false; synced = false;
            for (int i = 0; i < 32; i++) LayerMatrix[i] = 0xFFFFFFFF;
            Gravity = new(0, -9.81f, 0); Mode = SimulationMode.FixedUpdate;
        }

        public static void EnsureSettings() { if (!settingsLoaded) LoadSettings(); }

        public static IEnumerable<Shape> Shapes => shapes.Values;
        public static Body BodyOf(Rigidbody rb) => rb != null && bodies.TryGetValue(rb, out var b) ? b : null;
        public static Shape ShapeOf(Collider c) => c != null && shapes.TryGetValue(c, out var s) ? s : null;

        static IEnumerable<GameObject> AllObjects()
        {
            foreach (var g in Hier.PlayingObjects()) yield return g;
        }

        /// <summary>Bring the physics scene in line with the GameObjects: added / removed / disabled components, moved transforms.</summary>
        static readonly List<WheelCollider> wheels = new();

        // what the last Sync saw: queries re-sync only when something changed since (scene edits, scene, screen, mesh sizes)
        static bool synced;
        static int syncedEdits;
        static float syncedW, syncedH;
        static SceneData syncedScene;
        static readonly List<(MeshCollider col, Mesh mesh, int vertices)> syncedMeshes = new();

        static bool InSync()
        {
            if (!synced || syncedEdits != Object.s_SceneEdits || syncedScene != SceneData.Active || syncedW != MiniUnity.Screen.Width || syncedH != MiniUnity.Screen.Height) return false;
            foreach (var (col, mesh, n) in syncedMeshes) if (col.sharedMesh != mesh || (mesh != null && mesh.vertexCount != n)) return false;
            return true;
        }

        public static void Sync()
        {
            EnsureSettings();
            synced = true; syncedEdits = Object.s_SceneEdits; syncedScene = SceneData.Active; syncedW = MiniUnity.Screen.Width; syncedH = MiniUnity.Screen.Height;
            syncedMeshes.Clear();
            var liveCols = new HashSet<Collider>(); var liveBodies = new HashSet<Component>();
            wheels.Clear();
            foreach (var go in AllObjects())
            {
                if (go.m_Destroyed || !go.activeInHierarchy) continue;
                foreach (var c in go.m_Components)
                {
                    if (c is Rigidbody rb && !rb.m_Destroyed) { liveBodies.Add(rb); GetBody(rb); }
                    else if (c is WheelCollider wc) { if (!wc.m_Destroyed && wc.enabled) wheels.Add(wc); }
                    else if (c is Collider col && !col.m_Destroyed && col.enabled)
                    {
                        liveCols.Add(col);
                        if (col is MeshCollider mc) { var mesh = mc.sharedMesh; syncedMeshes.Add((mc, mesh, mesh != null ? mesh.vertexCount : 0)); }
                        if (col is CharacterController cc) liveBodies.Add(cc);
                    }
                }
            }
            foreach (var proxy in ArticulationSystem.Collect(AllObjects())) { liveBodies.Add(proxy); GetBody(proxy); }
            foreach (var k in bodies.Keys.ToList())
                if (k is Rigidbody or CharacterController ? !liveBodies.Contains(k) : !liveCols.Contains((Collider)k)) bodies.Remove(k);
            foreach (var k in shapes.Keys.ToList()) if (!liveCols.Contains(k)) RemoveShape(k);
            foreach (var b in bodies.Values) b.Shapes.Clear();
            foreach (var col in liveCols) UpdateShape(col);
            foreach (var b in bodies.Values) if (b.Rb != null) { SyncBodyPose(b); UpdateMass(b); }
            foreach (var s in shapes.Values) UpdateShapeWorld(s);
            joints = JointSolver.Collect(AllObjects(), rb => BodyOf(rb));
            joints.AddRange(ArticulationSystem.Joints(rb => BodyOf(rb)));
        }

        static void RemoveShape(Collider c)
        {
            if (!shapes.TryGetValue(c, out var s)) return;
            shapes.Remove(c);
            foreach (var k in pairs.Keys.Where(k => k.Item1 == s || k.Item2 == s).ToList()) { pendingExit.Add(pairs[k]); pairs.Remove(k); }
            foreach (var k in triggerPairs.Where(k => k.Item1 == s || k.Item2 == s).ToList()) { pendingTriggerExit.Add(k); triggerPairs.Remove(k); }
        }

        /// <summary>PhysX re-filters an actor's pairs when its kinematic flag changes: its trigger pairs are lost and found again
        /// (OnTriggerExit now, OnTriggerEnter on the next step while the shapes still overlap).</summary>
        internal static void RefilterTriggers(Body b)
        {
            foreach (var k in triggerPairs.Where(k => k.Item1.Body == b || k.Item2.Body == b).ToList()) { pendingTriggerExit.Add(k); triggerPairs.Remove(k); }
        }

        static readonly List<Pair> pendingExit = new();
        static readonly List<(Shape, Shape)> pendingTriggerExit = new();

        static Body GetBody(Rigidbody rb)
        {
            if (bodies.TryGetValue(rb, out var b)) return b;
            b = new Body { Rb = rb };
            rb.CacheProps();
            b.PxPose = new PxPose(rb.transform.position, rb.transform.rotation);
            b.CurPose = b.PrevPose = b.PxPose;
            b.LastWrittenP = b.PxPose.p; b.LastWrittenQ = b.PxPose.q; b.Written = true;
            bodies[rb] = b;
            return b;
        }

        static void SyncBodyPose(Body b)
        {
            var t = b.Rb != null ? b.Rb.transform : b.Cct.transform;
            var p = t.position; var q = t.rotation;
            if (!b.Written || (p - b.LastWrittenP).sqrMagnitude > 1e-12f || Quaternion.Dot(q, b.LastWrittenQ) < 0.9999999f)
            {
                // moved by script / animation: teleport (Unity syncs the Transform into the physics body)
                b.PxPose = new PxPose(p, q); b.CurPose = b.PrevPose = b.PxPose; b.InterpValid = false;
                b.LastWrittenP = p; b.LastWrittenQ = q; b.Written = true;
                if (b.Dynamic) Wake(b);
            }
        }

        /// <summary>The Rigidbody a collider moves with: the nearest Rigidbody or articulation link (its proxy) up the hierarchy.</summary>
        static Rigidbody OwnerBody(Collider col)
        {
            for (var t = col.transform; t != null; t = t.m_Parent)
            {
                var go = t.gameObject;
                if (go.GetComponent<Rigidbody>() is Rigidbody rb) return rb;
                if (go.GetComponent<ArticulationBody>() is ArticulationBody ab && ab.enabled && ArticulationSystem.LinkOf(ab) is { } l) return l.Proxy;
            }
            return null;
        }

        static void UpdateShape(Collider col)
        {
            var rb = OwnerBody(col);
            Body body;
            if (col is CharacterController cc)
            {
                if (!bodies.TryGetValue(cc, out body)) { body = new Body { Cct = cc }; bodies[cc] = body; }
                body.PxPose = new PxPose(cc.transform.position, cc.transform.rotation);
            }
            else if (rb != null) body = GetBody(rb);
            else
            {
                if (!bodies.TryGetValue(col, out body)) { body = new Body(); bodies[col] = body; }
                body.PxPose = PxPose.Identity;
            }
            if (!shapes.TryGetValue(col, out var s)) { s = new Shape { Col = col, Id = nextShapeId++ }; shapes[col] = s; }
            s.Body = body; body.Shapes.Add(s);
            var t = col.transform;
            var lossy = t.lossyScale;
            // geometry with scale baked in (like Unity's PhysX shapes)
            var extra = col.GeometryExtra();
            if (s.GeomVer != col.m_NVersion || s.GeomExtra != extra || s.GeomLossy.x != lossy.x || s.GeomLossy.y != lossy.y || s.GeomLossy.z != lossy.z)
            {
                s.Geom = col.BuildGeom(lossy, out var off, out var rot); s.Local = new PxPose(off, rot); col.m_ShapeOffset = off; col.m_ShapeRot = rot;
                s.GeomVer = col.m_NVersion; s.GeomExtra = extra; s.GeomLossy = lossy;
                s.Center = col.m_CenterCache; s.IsTrigger = col.isTrigger; s.Mat = col.sharedMaterial;
                s.Include = col.includeLayers.value; s.Exclude = col.excludeLayers.value; s.Priority = col.layerOverridePriority;
            }
            var colWorld = new PxPose(t.position, t.rotation).Mul(new PxPose(Vector3.Scale(s.Center, lossy), col.m_ShapeRot));
            s.Local = body.IsStatic ? colWorld : body.PxPose.Inverse().Mul(colWorld);
            s.Trigger = s.IsTrigger && col is not CharacterController;
            s.ContactOffset = col.m_ContactOffset > 0 ? col.m_ContactOffset : DefaultContactOffset;
            s.Layer = col.gameObject.layer;
            var mat = s.Mat;
            if (mat != null) { s.StaticFriction = mat.staticFriction; s.DynamicFriction = mat.dynamicFriction; s.Bounce = mat.bounciness; s.FrictionCombine = mat.frictionCombine; s.BounceCombine = mat.bounceCombine; }
            else { s.StaticFriction = s.DynamicFriction = 0.6f; s.Bounce = 0; s.FrictionCombine = s.BounceCombine = PhysicMaterialCombine.Average; }
        }

        static void UpdateShapeWorld(Shape s)
        {
            s.World = s.Body.IsStatic ? s.Local : s.Body.PxPose.Mul(s.Local);
            s.Aabb = s.Geom.WorldBounds(s.World);
        }

        /// <summary>Automatic centre of mass and inertia tensor from the attached non-trigger colliders (unit density scaled to the mass).</summary>
        static void UpdateMass(Body b)
        {
            var rb = b.Rb;
            b.Mass = rb.m_MassCache; b.InvMass = b.Mass > 0 ? 1 / b.Mass : 0;
            var solid = b.Shapes.Where(s => !s.Trigger).ToList();
            if (rb.m_AutoCom || rb.m_AutoTensor)
            {
                float vol = 0; var com = Vector3.zero;
                var parts = new List<(float v, Vector3 c, M3 I)>();
                foreach (var s in solid)
                {
                    s.Geom.MassProperties(out float v, out var c, out var diag, out var irot);
                    var cl = s.Local.Apply(c);
                    var R = M3.Rot(s.Local.q * irot);
                    parts.Add((v, cl, R * M3.Diag(diag) * R.T));
                    vol += v; com += cl * v;
                }
                if (vol > 0) com /= vol;
                if (rb.m_AutoCom) b.ComLocal = vol > 0 ? com : Vector3.zero; else b.ComLocal = rb.m_ComCache;
                if (rb.m_AutoTensor)
                {
                    if (vol > 0)
                    {
                        float density = b.Mass / vol;
                        var I = new double[3, 3];
                        foreach (var (v, c, Ii) in parts)
                        {
                            var d = c - b.ComLocal; float m = v * density;
                            float[,] Im = { { Ii.m00, Ii.m01, Ii.m02 }, { Ii.m10, Ii.m11, Ii.m12 }, { Ii.m20, Ii.m21, Ii.m22 } };
                            float[] dd = { d.x, d.y, d.z };
                            for (int i = 0; i < 3; i++)
                                for (int j = 0; j < 3; j++)
                                    I[i, j] += Im[i, j] * density + m * ((i == j ? d.sqrMagnitude : 0) - dd[i] * dd[j]);
                        }
                        Hull.Diagonalize(I, out var diag, out var rot);
                        b.InertiaDiag = Vector3.Max(diag, Vector3.one * 1e-6f); b.InertiaRot = rot;
                    }
                    else { b.InertiaDiag = Vector3.one; b.InertiaRot = Quaternion.identity; }
                }
                else { b.InertiaDiag = rb.m_TensorCache; b.InertiaRot = rb.m_TensorRotCache; }
            }
            else { b.ComLocal = rb.m_ComCache; b.InertiaDiag = rb.m_TensorCache; b.InertiaRot = rb.m_TensorRotCache; }
            rb.m_ComLocal = b.ComLocal; rb.m_InertiaDiag = b.InertiaDiag; rb.m_InertiaRot = b.InertiaRot;
        }

        public static void Wake(Body b)
        {
            if (b == null || !b.Dynamic) return;
            if (b.Sleeping) { b.Sleeping = false; }
            b.WakeCounter = Math.Max(b.WakeCounter, 0.4f);
        }

        // ------------------------------------------------------------------ simulation step
        public static readonly List<(string msg, Collider self, object arg)> Events = new();

        public static void Simulate(float dt)
        {
            if (dt <= 0) return;
            LastStep = dt;
            Sync();
            WheelSystem.Step(wheels, dt);
            var dyn = bodies.Values.Where(b => b.Rb != null || b.Cct != null).Distinct().ToList();
            var awake = new List<Body>();
            foreach (var b in dyn)
            {
                b.PrevPose = b.PxPose; b.Touching = 0; b.TouchesStatic = false;
                if (b.Dynamic)
                {
                    var rb = b.Rb;
                    if (b.Sleeping && (b.DV != Vector3.zero || b.DW != Vector3.zero || b.ForceAcc != Vector3.zero || b.TorqueAcc != Vector3.zero)) Wake(b);
                    if (b.Sleeping) { b.ForceAcc = b.TorqueAcc = b.DV = b.DW = Vector3.zero; continue; }
                    // external forces + impulses
                    var invI = InvInertiaWorld(b);
                    b.V += b.ForceAcc * dt + b.DV;
                    b.W += invI * (b.TorqueAcc * dt) + b.DW;
                    b.ForceAcc = b.TorqueAcc = b.DV = b.DW = Vector3.zero;
                    // gravity, damping, clamping (bodyCoreComputeUnconstrainedVelocity)
                    if (rb.m_UseGravityCache) b.V += Gravity * dt;
                    b.V *= Math.Max(0, 1 - rb.m_DragCache * dt);
                    b.W *= Math.Max(0, 1 - rb.m_AngularDragCache * dt);
                    float maxV = rb.m_MaxLinearVelocity, maxW = rb.m_MaxAngularVelocity;
                    if (b.V.sqrMagnitude > maxV * maxV) b.V *= maxV / b.V.magnitude;
                    if (b.W.sqrMagnitude > maxW * maxW) b.W *= maxW / b.W.magnitude;
                    ApplyLocks(b);
                    awake.Add(b);
                }
                else
                {
                    // kinematic: velocity that reaches the MovePosition target in this step
                    if (b.HasKinTarget)
                    {
                        b.V = (b.KinTargetP + b.KinTargetQ * b.ComLocal - b.ComWorld) / dt;
                        var dq = b.KinTargetQ * Quaternion.Inverse(b.PxPose.q);
                        dq.ToAngleAxis(out float ang, out var axis);
                        if (ang > 180) ang -= 360;
                        b.W = float.IsNaN(axis.x) || Math.Abs(ang) < 1e-6f ? Vector3.zero : axis * (ang * Mathf.Deg2Rad / dt);
                    }
                    else { b.V = Vector3.zero; b.W = Vector3.zero; }
                }
            }
            JointSolver.ApplyForces(joints, dt);
            ArticulationSystem.ApplyDrives(dt);
            foreach (var s in shapes.Values) UpdateShapeWorld(s);

            // broad phase (sweep and prune on x) + narrow phase
            var list = shapes.Values.OrderBy(s => s.Aabb.min.x).ThenBy(s => s.Id).ToList();
            float maxSpec = 0;
            foreach (var b in awake) maxSpec = Math.Max(maxSpec, SpeculativeMargin(b, dt));
            var newPairs = new Dictionary<(Shape, Shape), Pair>();
            var newTriggers = new HashSet<(Shape, Shape)>();
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i]; float aMax = a.Aabb.max.x + a.ContactOffset + 2 * maxSpec;
                for (int j = i + 1; j < list.Count; j++)
                {
                    var b = list[j];
                    if (b.Aabb.min.x - b.ContactOffset > aMax) break;
                    var (sa, sb) = a.Id < b.Id ? (a, b) : (b, a);
                    if (!Filter(sa, sb, out bool trig)) continue;
                    float off = sa.ContactOffset + sb.ContactOffset + SpeculativeMargin(sa.Body, dt) + SpeculativeMargin(sb.Body, dt);
                    if (!Inflated(sa.Aabb, off).Intersects(sb.Aabb)) continue;
                    if (trig)
                    {
                        if (Overlap(sa, sb)) newTriggers.Add((sa, sb));
                        continue;
                    }
                    if (sa.Body.Kinematic && sb.Body.Kinematic || sa.Body.IsStatic && sb.Body.Kinematic || sb.Body.IsStatic && sa.Body.Kinematic) continue;
                    bool aAct = sa.Body.Dynamic && !sa.Body.Sleeping, bAct = sb.Body.Dynamic && !sb.Body.Sleeping;
                    if (!aAct && !bAct && !(sa.Body.Kinematic && sb.Body.Dynamic) && !(sb.Body.Kinematic && sa.Body.Dynamic))
                    {
                        // both asleep / static: keep last step's touching state (sleeping pairs stay in contact)
                        if (pairs.TryGetValue((sa, sb), out var old) && old.Contacts.Count > 0) newPairs[(sa, sb)] = old;
                        continue;
                    }
                    var pr = pairs.TryGetValue((sa, sb), out var prev) ? prev : new Pair { A = sa, B = sb };
                    pr.Contacts.Clear();
                    Narrowphase.Collide(sa.Geom, sa.World, sb.Geom, sb.World, off, pr.Contacts);
                    if (pr.Contacts.Count == 0) continue;
                    newPairs[(sa, sb)] = pr;
                    // a sleeping dynamic body touched by an awake or kinematic body wakes up
                    if (sa.Body.Dynamic && sa.Body.Sleeping && (bAct || sb.Body.HasKinTarget)) { Wake(sa.Body); ReAwake(sa.Body, awake, dt); }
                    if (sb.Body.Dynamic && sb.Body.Sleeping && (aAct || sa.Body.HasKinTarget)) { Wake(sb.Body); ReAwake(sb.Body, awake, dt); }
                }
            }

            var solvePairs = newPairs.Values.Where(p => p.A.Body.Dynamic && !p.A.Body.Sleeping || p.B.Body.Dynamic && !p.B.Body.Sleeping).ToList();
            foreach (var p in solvePairs)
            {
                var c0 = p.Contacts[0].Point;
                p.RelVel = p.B.Body.PointVelocity(c0) - p.A.Body.PointVelocity(c0);
                if (p.A.Body.Dynamic) { p.A.Body.Touching++; if (!p.B.Body.Dynamic) p.A.Body.TouchesStatic = true; }
                if (p.B.Body.Dynamic) { p.B.Body.Touching++; if (!p.A.Body.Dynamic) p.B.Body.TouchesStatic = true; }
            }
            Solve(solvePairs, awake, dyn, dt);
            SweepCcd(awake, dt);
            JointSolver.Project(joints);
            ArticulationSystem.Project();

            // write back poses
            foreach (var b in dyn)
            {
                if (b.Dynamic && b.Sleeping) continue;
                if (b.Kinematic && b.HasKinTarget) { b.PxPose = new PxPose(b.KinTargetP, b.KinTargetQ); b.HasKinTarget = false; }
                b.CurPose = b.PxPose; b.InterpValid = true;
                if (b.Rb != null) WriteTransform(b, b.PxPose);
            }
            foreach (var s in shapes.Values) UpdateShapeWorld(s);
            foreach (var b in awake) SleepCheck(b, dt, newPairs.Values);
            WakeOnLostTouch(newPairs);

            BuildEvents(newPairs, newTriggers);
            pairs = newPairs; triggerPairs = newTriggers;
        }

        // ------------------------------------------------------------------ continuous collision detection
        // ContinuousSpeculative: contacts are generated ahead of time within the distance the body can travel this step
        // (PhysX speculative CCD). Continuous / ContinuousDynamic: after the solve a fast body is swept from its previous
        // pose to the new one (PhysX sweep CCD); at the first impact it stops at the time of impact and its approaching
        // velocity is reflected with the pair's bounciness. Continuous sweeps against static and kinematic colliders,
        // ContinuousDynamic also against dynamic bodies that use a sweep mode.
        static float SpeculativeMargin(Body b, float dt)
        {
            if (b.Rb == null || !b.Dynamic || b.Sleeping || b.Rb.m_CollisionModeCache != CollisionDetectionMode.ContinuousSpeculative) return 0;
            float r = 0;
            foreach (var s in b.Shapes) r = Math.Max(r, s.Aabb.extents.magnitude);
            return (b.V.magnitude + b.W.magnitude * r) * dt;
        }

        static bool SweepMode(Body b) => b.Rb != null && b.Dynamic &&
            (b.Rb.m_CollisionModeCache == CollisionDetectionMode.Continuous || b.Rb.m_CollisionModeCache == CollisionDetectionMode.ContinuousDynamic);

        static void SweepCcd(List<Body> awake, float dt)
        {
            foreach (var b in awake)
            {
                if (!SweepMode(b) || b.Sleeping) continue;
                var disp = b.PxPose.p - b.PrevPose.p; float len = disp.magnitude;
                float minExtent = float.MaxValue;
                foreach (var s in b.Shapes) if (!s.Trigger) minExtent = Math.Min(minExtent, Math.Min(s.Aabb.extents.x, Math.Min(s.Aabb.extents.y, s.Aabb.extents.z)));
                if (minExtent == float.MaxValue || len <= minExtent) continue;   // PhysX ccdThreshold: slow bodies are left to the discrete solver
                var dir = disp / len;
                bool dynTargets = b.Rb.m_CollisionModeCache == CollisionDetectionMode.ContinuousDynamic;
                float best = float.MaxValue; Vector3 bestN = default; Shape bestS = null, bestOwn = null;
                foreach (var own in b.Shapes)
                {
                    if (own.Trigger || own.Geom.Kind == GeomKind.Mesh) continue;
                    var from = new PxPose(b.PrevPose.p, b.PxPose.q).Mul(own.Local);
                    var query = new GeomConvex(own.Geom, from);
                    var qb = own.Geom.WorldBounds(from);
                    var swept = qb; swept.Encapsulate(new Bounds(qb.center + disp, qb.size));
                    foreach (var t in shapes.Values)
                    {
                        if (t.Trigger || t.Body == b || !t.Aabb.Intersects(swept)) continue;
                        if (!Filter(own, t, out bool trig) || trig) continue;
                        if (t.Body.Dynamic && !(dynTargets && SweepMode(t.Body))) continue;
                        if (!SweepShape(query, t, dir, len, swept, out var h)) continue;
                        if (h.Distance < best) { best = h.Distance; bestN = h.Normal; bestS = t; bestOwn = own; }
                    }
                }
                if (bestS == null) continue;
                float back = Math.Min(best, 0.5f * (bestOwn.ContactOffset + bestS.ContactOffset));
                b.PxPose = new PxPose(b.PrevPose.p + dir * (best - back), b.PxPose.q);
                var n = Vector3.Dot(bestN, dir) > 0 ? -bestN : bestN;   // surface normal facing the moving body
                var other = bestS.Body.Dynamic ? bestS.Body : null;
                float vn = Vector3.Dot(b.V - (other?.V ?? Vector3.zero), n);
                if (vn < 0)
                {
                    // impulse along the normal with the pair's bounciness (a static / kinematic target has infinite mass)
                    float e = Combine(bestOwn.Bounce, bestS.Bounce, bestOwn.BounceCombine, bestS.BounceCombine);
                    float invB = other?.InvMass ?? 0;
                    float j = -(1 + e) * vn / (b.InvMass + invB);
                    b.V += j * b.InvMass * n;
                    if (other != null) { other.V -= j * invB * n; if (other.Sleeping) Wake(other); }
                }
                foreach (var s in b.Shapes) UpdateShapeWorld(s);
            }
        }

        static void ReAwake(Body b, List<Body> awake, float dt)
        {
            if (!awake.Contains(b)) { if (b.Rb.m_UseGravityCache) b.V += Gravity * dt; awake.Add(b); }
        }

        static Bounds Inflated(Bounds b, float d) { b.Expand(d * 2); return b; }

        static bool Filter(Shape a, Shape b, out bool trigger)
        {
            trigger = a.Trigger || b.Trigger;
            if (a.Body == b.Body) return false;
            if (a.Body.IsStatic && b.Body.IsStatic) return false;
            if (!LayersAllowContact(a, b)) return false;
            if (IgnoredPairs.Contains((Math.Min(a.Col.GetInstanceID(), b.Col.GetInstanceID()), Math.Max(a.Col.GetInstanceID(), b.Col.GetInstanceID())))) return false;
            if (a.Body.Rb != null && b.Body.Rb != null && (!a.Body.Rb.m_DetectCollisions || !b.Body.Rb.m_DetectCollisions)) return false;
            foreach (var j in joints) if (!j.J.enableCollision && (j.A == a.Body && j.B == b.Body || j.A == b.Body && j.B == a.Body)) return false;
            if (a.Body.Rb != null && !a.Body.Rb.m_DetectCollisions || b.Body.Rb != null && !b.Body.Rb.m_DetectCollisions) return false;
            // triggers need a Rigidbody (or CharacterController) on one side
            if (trigger && a.Body.IsStatic && b.Body.IsStatic) return false;
            return true;
        }

        /// <summary>
        /// Layer collision matrix with the per-collider and per-Rigidbody include / exclude layers (Unity 2022.2+). Each side decides
        /// for the other's layer (include beats exclude, both beat the matrix); a side with a higher layerOverridePriority decides alone,
        /// equal priorities exclude if either side excludes, unless either side includes.
        /// </summary>
        static bool LayersAllowContact(Shape a, Shape b)
        {
            bool matrix = LayersCollide(a.Layer, b.Layer);
            int inA = a.Include | (a.Body.Rb?.m_IncludeBits ?? 0), exA = a.Exclude | (a.Body.Rb?.m_ExcludeBits ?? 0);
            int inB = b.Include | (b.Body.Rb?.m_IncludeBits ?? 0), exB = b.Exclude | (b.Body.Rb?.m_ExcludeBits ?? 0);
            if ((inA | exA | inB | exB) == 0) return matrix;
            bool incA = (inA & (1 << b.Layer)) != 0, excA = (exA & (1 << b.Layer)) != 0;
            bool incB = (inB & (1 << a.Layer)) != 0, excB = (exB & (1 << a.Layer)) != 0;
            if (a.Priority != b.Priority)
            {
                bool inc = a.Priority > b.Priority ? incA : incB, exc = a.Priority > b.Priority ? excA : excB;
                return inc || (!exc && matrix);
            }
            if (incA || incB) return true;
            if (excA || excB) return false;
            return matrix;
        }

        static bool Overlap(Shape a, Shape b)
        {
            var tmp = new List<Contact>();
            Narrowphase.Collide(a.Geom, a.World, b.Geom, b.World, 0, tmp);
            foreach (var c in tmp) if (c.Separation <= 0) return true;
            return false;
        }

        static void ApplyLocks(Body b)
        {
            var c = b.Rb != null ? b.Rb.m_ConstraintsCache : RigidbodyConstraints.None;
            if (c == 0) return;
            if ((c & RigidbodyConstraints.FreezePositionX) != 0) b.V.x = 0;
            if ((c & RigidbodyConstraints.FreezePositionY) != 0) b.V.y = 0;
            if ((c & RigidbodyConstraints.FreezePositionZ) != 0) b.V.z = 0;
            if ((c & RigidbodyConstraints.FreezeRotationX) != 0) b.W.x = 0;
            if ((c & RigidbodyConstraints.FreezeRotationY) != 0) b.W.y = 0;
            if ((c & RigidbodyConstraints.FreezeRotationZ) != 0) b.W.z = 0;
        }

        public static M3 InvInertiaWorld(Body b)
        {
            if (!b.Dynamic) return default;
            var R = M3.Rot(b.PxPose.q * b.InertiaRot);
            var d = new Vector3(b.InertiaDiag.x > 0 ? 1 / b.InertiaDiag.x : 0, b.InertiaDiag.y > 0 ? 1 / b.InertiaDiag.y : 0, b.InertiaDiag.z > 0 ? 1 / b.InertiaDiag.z : 0);
            var I = R * M3.Diag(d) * R.T;
            var c = b.Rb.m_ConstraintsCache;
            if ((c & RigidbodyConstraints.FreezeRotationX) != 0) { I.m00 = I.m01 = I.m02 = I.m10 = I.m20 = 0; }
            if ((c & RigidbodyConstraints.FreezeRotationY) != 0) { I.m10 = I.m11 = I.m12 = I.m01 = I.m21 = 0; }
            if ((c & RigidbodyConstraints.FreezeRotationZ) != 0) { I.m20 = I.m21 = I.m22 = I.m02 = I.m12 = 0; }
            return I;
        }

        static Vector3 InvMassAxis(Body b)
        {
            if (!b.Dynamic) return Vector3.zero;
            var c = b.Rb.m_ConstraintsCache; float m = b.InvMass;
            return new Vector3((c & RigidbodyConstraints.FreezePositionX) != 0 ? 0 : m, (c & RigidbodyConstraints.FreezePositionY) != 0 ? 0 : m, (c & RigidbodyConstraints.FreezePositionZ) != 0 ? 0 : m);
        }

        // ------------------------------------------------------------------ solver
        sealed class Row
        {
            public Body A, B; public Vector3 N, RA, RB, RAxN_I, RBxN_I; public float VelMul, BiasedErr, UnbiasedErr, Applied, Max = float.MaxValue;
            public int Patch; public bool Friction; public float FrictionBias;
        }
        sealed class Patch { public Body A, B; public List<Row> Normals = new(); public List<Row> Frictions = new(); public float Static, Dynamic; public Pair Pair; public FrictionPatch Friction; }

        static Vector3 AnchorLocal(Body b, Vector3 world) => b.IsStatic ? world : b.PxPose.InvApply(world);
        static Vector3 AnchorWorld(Body b, Vector3 local) => b.IsStatic ? local : b.PxPose.Apply(local);

        static float Combine(float a, float b, PhysicMaterialCombine ma, PhysicMaterialCombine mb)
        {
            static int Rank(PhysicMaterialCombine m) => m switch { PhysicMaterialCombine.Average => 0, PhysicMaterialCombine.Minimum => 1, PhysicMaterialCombine.Multiply => 2, _ => 3 };
            var m = Rank(ma) >= Rank(mb) ? ma : mb;
            return m switch { PhysicMaterialCombine.Minimum => Math.Min(a, b), PhysicMaterialCombine.Multiply => a * b, PhysicMaterialCombine.Maximum => Math.Max(a, b), _ => (a + b) * 0.5f };
        }

        static Row MakeRow(Body A, Body B, Vector3 n, Vector3 p)
        {
            var r = new Row { A = A, B = B, N = n, RA = p - A.ComWorld, RB = p - B.ComWorld };
            var raxn = Vector3.Cross(r.RA, n); var rbxn = Vector3.Cross(r.RB, n);
            r.RAxN_I = A.InvIw * raxn; r.RBxN_I = B.InvIw * rbxn;
            float k = Vector3.Dot(Vector3.Scale(A.InvMassAxis, n), n) + Vector3.Dot(Vector3.Scale(B.InvMassAxis, n), n) + Vector3.Dot(raxn, r.RAxN_I) + Vector3.Dot(rbxn, r.RBxN_I);
            r.VelMul = k > 1e-12f ? 1 / k : 0;
            return r;
        }

        static float RelVel(Row r) => Vector3.Dot(r.N, r.A.SV + Vector3.Cross(r.A.SW, r.RA) - r.B.SV - Vector3.Cross(r.B.SW, r.RB));

        static void ApplyImpulse(Row r, float d)
        {
            var J = r.N * d;
            r.A.SV += Vector3.Scale(r.A.InvMassAxis, J); r.A.SW += r.RAxN_I * d;
            r.B.SV -= Vector3.Scale(r.B.InvMassAxis, J); r.B.SW -= r.RBxN_I * d;
        }

        static readonly Body StaticBody = new();

        static void Solve(List<Pair> list, List<Body> awake, List<Body> all, float dt)
        {
            float invDt = 1 / dt;
            foreach (var b in all)
            {
                b.SV = b.V; b.SW = b.W;
                b.InvIw = InvInertiaWorld(b); b.InvMassAxis = InvMassAxis(b);
                if (b.Dynamic && b.Sleeping) { b.InvIw = default; b.InvMassAxis = Vector3.zero; b.SV = b.SW = Vector3.zero; }
            }
            StaticBody.SV = StaticBody.SW = Vector3.zero; StaticBody.InvIw = default; StaticBody.InvMassAxis = Vector3.zero; StaticBody.PxPose = PxPose.Identity;
            var patches = new List<Patch>();
            foreach (var p in list)
            {
                var A = p.A.Body.IsStatic ? StaticBody : p.A.Body; var B = p.B.Body.IsStatic ? StaticBody : p.B.Body;
                float sf = Combine(p.A.StaticFriction, p.B.StaticFriction, p.A.FrictionCombine, p.B.FrictionCombine);
                float df = Combine(p.A.DynamicFriction, p.B.DynamicFriction, p.A.FrictionCombine, p.B.FrictionCombine);
                float e = Combine(p.A.Bounce, p.B.Bounce, p.A.BounceCombine, p.B.BounceCombine);
                float depA = A.Rb != null ? A.Rb.m_MaxDepenetration : float.MaxValue, depB = B.Rb != null ? B.Rb.m_MaxDepenetration : float.MaxValue;
                float maxPenBias = -Math.Min(depA, depB);
                if (float.IsInfinity(maxPenBias) || maxPenBias < -1e30f) maxPenBias = -DefaultMaxDepenetrationVelocity;
                p.NormalImpulse = new float[p.Contacts.Count]; p.Impulse = Vector3.zero;
                var newPatches = new List<FrictionPatch>();
                // group contacts into patches by normal
                var groups = new List<List<int>>();
                for (int i = 0; i < p.Contacts.Count; i++)
                {
                    var g = groups.FirstOrDefault(gg => Vector3.Dot(p.Contacts[gg[0]].Normal, p.Contacts[i].Normal) > 0.995f);
                    if (g == null) groups.Add(new List<int> { i }); else g.Add(i);
                }
                foreach (var g in groups)
                {
                    var patch = new Patch { A = A, B = B, Static = sf, Dynamic = df, Pair = p };
                    var n = p.Contacts[g[0]].Normal;
                    foreach (int ci in g)
                    {
                        var c = p.Contacts[ci];
                        var r = MakeRow(A, B, c.Normal, c.Point); r.Patch = ci;
                        float vrel = RelVel(r);
                        float pen = c.Separation;
                        float scaledBias = r.VelMul * Math.Max(maxPenBias, pen * invDt * 0.8f);
                        bool bounce = e > 0 && vrel < -BounceThreshold && -vrel > pen * invDt;
                        if (bounce) scaledBias = 0;
                        float target = (bounce ? -vrel * e : 0) - vrel;
                        // full-velocity form of PhysX's delta formulation: impulse = velMul·(desired − vrel) − bias
                        r.BiasedErr = (bounce ? -vrel * e : 0) * r.VelMul - scaledBias;
                        r.UnbiasedErr = (bounce ? -vrel * e : 0) * r.VelMul - (bounce ? 0 : Math.Max(scaledBias, 0));
                        _ = target;
                        patch.Normals.Add(r);
                    }
                    // friction (PhysX patch friction): up to two anchors kept in body space between steps while the patch holds,
                    // rows along t0 (relative sliding velocity) and t1, velMultiplier 0.8/k, bias = drift of the anchors / dt
                    if (sf > 0 || df > 0)
                    {
                        var fp = p.FrictionPatches.FirstOrDefault(f => !f.Broken && Vector3.Dot(A.IsStatic ? f.NormalA : A.PxPose.Rotate(f.NormalA), n) > 0.999f
                                                                     && f.LocalA.Zip(f.LocalB).All(ab => Math.Abs(Vector3.Dot(AnchorWorld(A, ab.First) - AnchorWorld(B, ab.Second), n)) < 0.025f));
                        if (fp == null)
                        {
                            fp = new FrictionPatch { NormalA = A.IsStatic ? n : A.PxPose.InvRotate(n) };
                            var cand = g.Where(i => p.Contacts[i].Separation < 0.04f).OrderBy(i => p.Contacts[i].Separation).ToList();
                            if (cand.Count == 0) cand = g.ToList();
                            int a0 = cand[0];
                            int a1 = cand.OrderByDescending(i => (p.Contacts[i].Point - p.Contacts[a0].Point).sqrMagnitude).First();
                            var anchors = a1 != a0 && (p.Contacts[a1].Point - p.Contacts[a0].Point).magnitude > 0.025f ? new[] { a0, a1 } : new[] { a0 };
                            foreach (int ai in anchors) { var cp = p.Contacts[ai].Point; fp.LocalA.Add(AnchorLocal(A, cp)); fp.LocalB.Add(AnchorLocal(B, cp)); }
                        }
                        newPatches.Add(fp); fp.Broken = false;
                        var vr = A.SV - B.SV; var t0 = vr - n * Vector3.Dot(n, vr);
                        if (t0.sqrMagnitude <= 0.0001f) t0 = Math.Abs(n.x) < 0.70710678f ? new Vector3(0, -n.z, n.y) : new Vector3(-n.y, n.x, 0);
                        t0 = t0.normalized; var t1 = Vector3.Cross(n, t0);
                        float coef = ImprovedPatchFriction && fp.LocalA.Count == 2 ? 0.5f : 1f;
                        patch.Static = sf * coef; patch.Dynamic = df * coef; patch.Friction = fp;
                        for (int k = 0; k < fp.LocalA.Count; k++)
                        {
                            var wa = AnchorWorld(A, fp.LocalA[k]); var wb = AnchorWorld(B, fp.LocalB[k]);
                            var error = wa - wb;
                            foreach (var t in new[] { t0, t1 })
                            {
                                var r = new Row { A = A, B = B, N = t, RA = wa - A.ComWorld, RB = wb - B.ComWorld, Friction = true };
                                var raxn = Vector3.Cross(r.RA, t); var rbxn = Vector3.Cross(r.RB, t);
                                r.RAxN_I = A.InvIw * raxn; r.RBxN_I = B.InvIw * rbxn;
                                float kk = Vector3.Dot(Vector3.Scale(A.InvMassAxis, t), t) + Vector3.Dot(Vector3.Scale(B.InvMassAxis, t), t) + Vector3.Dot(raxn, r.RAxN_I) + Vector3.Dot(rbxn, r.RBxN_I);
                                r.VelMul = kk > 1e-12f ? 0.8f / kk : 0;
                                r.FrictionBias = Vector3.Dot(t, error) * invDt;
                                patch.Frictions.Add(r);
                            }
                        }
                    }
                    patches.Add(patch);
                }
                p.FrictionPatches = newPatches;
            }
            // solve order: contacts closest to static / kinematic support first (graph distance), so stacks converge bottom-up
            var depth = new Dictionary<Body, int>();
            var queue = new Queue<Body>();
            foreach (var pt in patches)
                foreach (var b in new[] { pt.A, pt.B })
                    if (!b.Dynamic && !depth.ContainsKey(b)) { depth[b] = 0; queue.Enqueue(b); }
            var adj = new Dictionary<Body, List<Body>>();
            foreach (var pt in patches)
            {
                (adj.TryGetValue(pt.A, out var la) ? la : adj[pt.A] = new()).Add(pt.B);
                (adj.TryGetValue(pt.B, out var lb) ? lb : adj[pt.B] = new()).Add(pt.A);
            }
            while (queue.Count > 0)
            {
                var b = queue.Dequeue();
                if (!adj.TryGetValue(b, out var ns)) continue;
                foreach (var n2 in ns) if (n2.Dynamic && !depth.ContainsKey(n2)) { depth[n2] = depth[b] + 1; queue.Enqueue(n2); }
            }
            int D(Body b) => depth.TryGetValue(b, out var d) ? d : 1000;
            patches = patches.OrderBy(pt => Math.Min(D(pt.A), D(pt.B))).ToList();
            int posIters = Math.Max(1, awake.Count == 0 ? 1 : awake.Max(b => b.Rb.m_SolverIterations));
            int velIters = Math.Max(1, awake.Count == 0 ? 1 : awake.Max(b => b.Rb.m_SolverVelocityIterations));
            var jrows = JointSolver.Prepare(joints, dt); var jblocks = JointSolver.Blocks(jrows);
            for (int it = 0; it < posIters; it++) { JointSolver.Solve(jrows, jblocks, true); foreach (var pt in patches) SolvePatch(pt, true); }
            // integrate positions with the position-iteration velocities
            foreach (var b in awake)
            {
                ApplyLocksSolver(b);
                var com = b.ComWorld + b.SV * dt;
                var q = Integrate(b.PxPose.q, b.SW, dt);
                b.PxPose = new PxPose(com - q * b.ComLocal, q);
            }
            foreach (var b in all) if (b.Kinematic) { var com = b.ComWorld + b.V * dt; var q = Integrate(b.PxPose.q, b.W, dt); b.PxPose = new PxPose(com - q * b.ComLocal, q); }
            for (int it = 0; it < velIters; it++) { JointSolver.Solve(jrows, jblocks, false); foreach (var pt in patches) SolvePatch(pt, false); }
            JointSolver.Finish(jrows, joints, dt);
            foreach (var b in awake) { ApplyLocksSolver(b); b.V = b.SV; b.W = b.SW; }
            // collision impulses for the callbacks
            foreach (var pt in patches)
            {
                var sum = Vector3.zero;
                foreach (var r in pt.Normals) { pt.Pair.NormalImpulse[r.Patch] = r.Applied; sum += r.N * r.Applied; }
                foreach (var r in pt.Frictions) sum += r.N * r.Applied;
                pt.Pair.Impulse += sum;
            }
        }

        static void ApplyLocksSolver(Body b)
        {
            var v = b.V; var w = b.W; b.V = b.SV; b.W = b.SW; ApplyLocks(b); b.SV = b.V; b.SW = b.W; b.V = v; b.W = w;
        }

        static void SolvePatch(Patch pt, bool biased)
        {
            float sumN = 0;
            foreach (var r in pt.Normals)
            {
                float vrel = RelVel(r);
                float err = biased ? r.BiasedErr : r.UnbiasedErr;
                float d = Math.Max(err - vrel * r.VelMul, -r.Applied);
                float nf = Math.Min(r.Applied + d, r.Max);
                d = nf - r.Applied; r.Applied = nf;
                if (d != 0) ApplyImpulse(r, d);
                sumN += nf;
            }
            if (pt.Frictions.Count == 0) return;
            float maxS = pt.Static * sumN, maxD = pt.Dynamic * sumN;
            bool broken = false;
            foreach (var r in pt.Frictions)
            {
                float vrel = RelVel(r);
                float total = r.Applied - (vrel + r.FrictionBias) * r.VelMul;
                bool clamp = Math.Abs(total) > maxS;
                float nf = clamp ? Math.Clamp(total, -maxD, maxD) : total;
                broken |= clamp;
                float d = nf - r.Applied; r.Applied = nf;
                if (d != 0) ApplyImpulse(r, d);
            }
            if (pt.Friction != null) pt.Friction.Broken = broken;
        }

        /// <summary>Closed-form quaternion integration (PhysX integrateCore).</summary>
        public static Quaternion Integrate(Quaternion q, Vector3 w, float dt)
        {
            float wl = w.magnitude;
            if (wl == 0) return q;
            float half = dt * wl * 0.5f;
            float s = Mathf.Sin(half) / wl, c = Mathf.Cos(half);
            var pqr = w * s;
            var qv = new Quaternion(pqr.x, pqr.y, pqr.z, 0);
            var r = qv * q;
            r = new Quaternion(r.x + q.x * c, r.y + q.y * c, r.z + q.z * c, r.w + q.w * c);
            float l = Mathf.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w);
            return new Quaternion(r.x / l, r.y / l, r.z / l, r.w / l);
        }

        static void WriteTransform(Body b, PxPose pose)
        {
            var t = b.Rb.transform;
            t.position = pose.p; t.rotation = pose.q;
            b.LastWrittenP = t.position; b.LastWrittenQ = t.rotation; b.Written = true;
        }

        /// <summary>PhysX updateWakeCounter (no stabilisation): energy accumulated over the last half wake period vs sleepThreshold.</summary>
        static void SleepCheck(Body b, float dt, IEnumerable<Pair> active)
        {
            const float reset = 20f * 0.02f;
            float wc = b.WakeCounter;
            float thr = b.Rb.m_SleepThreshold;
            if (wc < reset * 0.5f || wc < dt)
            {
                var inertia = b.InertiaDiag;
                b.SleepLinAcc += b.V;
                b.SleepAngAcc += Quaternion.Inverse(b.PxPose.q * b.InertiaRot) * b.W;
                float invMass = b.InvMass == 0 ? 1 : b.InvMass;
                float ang = Vector3.Dot(Vector3.Scale(b.SleepAngAcc, b.SleepAngAcc), inertia) * invMass;
                float lin = b.SleepLinAcc.sqrMagnitude;
                float energy = 0.5f * (ang + lin);
                float cluster = 1 + b.Touching;
                float threshold = cluster * thr;
                if (energy >= threshold)
                {
                    b.SleepLinAcc = b.SleepAngAcc = Vector3.zero;
                    float factor = threshold == 0 ? 2 : Math.Min(energy / threshold, 2);
                    b.WakeCounter = factor * 0.5f * reset + dt * (cluster - 1);
                    return;
                }
            }
            wc = Math.Max(wc - dt, 0);
            b.WakeCounter = wc;
            if (wc == 0)
            {
                b.Sleeping = true; b.V = b.W = Vector3.zero; b.SleepLinAcc = b.SleepAngAcc = Vector3.zero;
            }
        }

        // ------------------------------------------------------------------ events
        public sealed class CollisionEvent { public string Message; public Collider Self, Other; public Pair Pair; public bool SelfIsA; }

        public static readonly List<CollisionEvent> PendingEvents = new();

        static bool Reports(Pair p) => p.A.Body.Dynamic || p.B.Body.Dynamic;

        /// <summary>PhysX lost touch: a sleeping body whose contact pair stopped touching (the other body moved away, became
        /// kinematic and was teleported, or its shape was removed) wakes up, so a marble resting on another one falls once it leaves.</summary>
        static void WakeOnLostTouch(Dictionary<(Shape, Shape), Pair> now)
        {
            static void W(Body b) { if (b.Dynamic && b.Sleeping) Wake(b); }
            foreach (var (k, p) in pairs) if (!now.ContainsKey(k)) { W(p.A.Body); W(p.B.Body); }
            foreach (var p in pendingExit) { W(p.A.Body); W(p.B.Body); }
        }

        static void BuildEvents(Dictionary<(Shape, Shape), Pair> now, HashSet<(Shape, Shape)> trig)
        {
            // triggers first (Unity: OnTrigger* before OnCollision*)
            foreach (var k in trig) Queue(triggerPairs.Contains(k) ? "OnTriggerStay" : "OnTriggerEnter", k.Item1, k.Item2, null);
            foreach (var k in triggerPairs) if (!trig.Contains(k)) Queue("OnTriggerExit", k.Item1, k.Item2, null);
            foreach (var k in pendingTriggerExit) Queue("OnTriggerExit", k.Item1, k.Item2, null);
            pendingTriggerExit.Clear();
            foreach (var (k, p) in now) if (Reports(p)) Queue(pairs.ContainsKey(k) ? "OnCollisionStay" : "OnCollisionEnter", p.A, p.B, p);
            foreach (var (k, p) in pairs) if (!now.ContainsKey(k) && Reports(p)) { p.Contacts.Clear(); Queue("OnCollisionExit", p.A, p.B, p); }
            foreach (var p in pendingExit) if (Reports(p)) { p.Contacts.Clear(); Queue("OnCollisionExit", p.A, p.B, p); }
            pendingExit.Clear();
        }

        static void Queue(string msg, Shape a, Shape b, Pair p)
        {
            PendingEvents.Add(new CollisionEvent { Message = msg, Self = a.Col, Other = b.Col, Pair = p, SelfIsA = true });
            PendingEvents.Add(new CollisionEvent { Message = msg, Self = b.Col, Other = a.Col, Pair = p, SelfIsA = false });
        }

        // ------------------------------------------------------------------ interpolation (before Update, like Unity)
        public static void Interpolate(float alpha)
        {
            foreach (var b in bodies.Values)
            {
                if (b.Rb == null || !b.InterpValid || b.Rb.m_InterpolationCache == RigidbodyInterpolation.None || b.Sleeping) continue;
                PxPose p;
                if (b.Rb.m_InterpolationCache == RigidbodyInterpolation.Interpolate)
                    p = new PxPose(Vector3.Lerp(b.PrevPose.p, b.CurPose.p, alpha), Quaternion.Slerp(b.PrevPose.q, b.CurPose.q, alpha));
                else
                    p = new PxPose(b.CurPose.p + b.V * (alpha * LastStep), Integrate(b.CurPose.q, b.W, alpha * LastStep));
                WriteTransform(b, p);
            }
        }

        // ------------------------------------------------------------------ queries
        public struct Hit { public Shape Shape; public float Distance; public Vector3 Point, Normal; public int Face; public Vector2 UV; }

        static bool Passes(Shape s, int mask, QueryTriggerInteraction q)
        {
            if ((mask & (1 << s.Layer)) == 0) return false;
            bool hitTriggers = q == QueryTriggerInteraction.UseGlobal ? QueriesHitTriggers : q == QueryTriggerInteraction.Collide;
            if (s.Trigger && !hitTriggers) return false;
            return true;
        }

        public static void PrepareQuery()
        {
            EnsureSettings();
            // Unity syncs Transform changes before queries when autoSyncTransforms is on; MiniUnity also adds shapes created since
            // the last step so that scripts can query colliders they just spawned (Unity registers new colliders immediately).
            // Nothing changed since the last Sync (no scene edits): the physics scene is already current.
            if (!InSync()) Sync();
        }

        public static List<Hit> Raycast(Vector3 origin, Vector3 dir, float maxDist, int mask, QueryTriggerInteraction q, bool all)
        {
            PrepareQuery();
            if (float.IsInfinity(maxDist) || maxDist > 1e30f) maxDist = float.MaxValue;
            if (!all)
            {
                // nearest hit only: shrink the ray to the best hit so far; ties keep the first shape in iteration order (as the sort did)
                Hit best = default; bool any = false; float limit = maxDist;
                foreach (var s in shapes.Values)
                {
                    if (!Passes(s, mask, q) || !RayAabb(origin, dir, limit, s.Aabb)) continue;
                    if (RayShape(s, origin, dir, maxDist, out var h) && (!any || h.Distance < best.Distance)) { best = h; any = true; limit = h.Distance; }
                }
                var one = new List<Hit>(1);
                if (any) one.Add(best);
                return one;
            }
            var hits = new List<Hit>();
            foreach (var s in shapes.Values)
            {
                if (!Passes(s, mask, q) || !RayAabb(origin, dir, maxDist, s.Aabb)) continue;
                if (RayShape(s, origin, dir, maxDist, out var h)) hits.Add(h);
            }
            hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return hits;
        }

        /// <summary>Conservative slab test: can the ray segment [0, maxDist] touch the box (slightly inflated against float error)?</summary>
        static bool RayAabb(Vector3 o, Vector3 d, float maxDist, Bounds b)
        {
            var mn = b.min; var mx = b.max;
            float pad = 1e-3f + 1e-5f * Math.Max(b.extents.magnitude, 1);
            float t0 = 0, t1 = maxDist;
            for (int i = 0; i < 3; i++)
            {
                float oi = o[i], di = d[i], lo = mn[i] - pad, hi = mx[i] + pad;
                if (Math.Abs(di) < 1e-12f) { if (oi < lo || oi > hi) return false; continue; }
                float inv = 1 / di, ta = (lo - oi) * inv, tb = (hi - oi) * inv;
                if (ta > tb) (ta, tb) = (tb, ta);
                if (ta > t0) t0 = ta;
                if (tb < t1) t1 = tb;
                if (t0 > t1) return false;
            }
            return true;
        }

        public static bool RayShape(Shape s, Vector3 o, Vector3 d, float maxDist, out Hit h)
        {
            h = default; h.Shape = s; h.Face = -1;
            var g = s.Geom; var P = s.World;
            switch (g.Kind)
            {
                case GeomKind.Sphere:
                    if (!Geo.RaySphere(o, d, P.p, g.Radius, out float t) || t > maxDist) return false;
                    h.Distance = t; h.Point = o + d * t; h.Normal = (h.Point - P.p).normalized; return true;
                case GeomKind.Capsule:
                {
                    var core = Narrowphase.Core(g, P);
                    if (!Geo.RayCapsule(o, d, core[0], core[1], g.Radius, out float tc, out var n) || tc > maxDist) return false;
                    h.Distance = tc; h.Point = o + d * tc; h.Normal = n; return true;
                }
                case GeomKind.Box:
                case GeomKind.Convex:
                {
                    var lo = P.InvApply(o); var ld = P.InvRotate(d);
                    if (!Geo.RayPoly(g.Poly, lo, ld, out float tb, out var n) || tb > maxDist) return false;
                    h.Distance = tb; h.Point = o + d * tb; h.Normal = P.Rotate(n); return true;
                }
                default:
                {
                    var lo = P.InvApply(o); var ld = P.InvRotate(d);
                    if (!g.Mesh.Raycast(lo, ld, Math.Min(maxDist, 1e30f), !QueriesHitBackfaces, out float tm, out int tri, out var n, out var uv)) return false;
                    h.Distance = tm; h.Point = o + d * tm; h.Normal = P.Rotate(n); h.Face = tri; h.UV = uv;
                    if (Vector3.Dot(h.Normal, d) > 0) h.Normal = -h.Normal;
                    return true;
                }
            }
        }

        public static Convex ConvexOf(Shape s) => new GeomConvex(s.Geom, s.World);

        /// <summary>Shape cast of a convex query shape against every collider (initial overlaps are not reported, like Unity).</summary>
        public static List<Hit> Sweep(Convex query, Bounds queryBounds, Vector3 dir, float maxDist, int mask, QueryTriggerInteraction q, bool all, Collider ignore = null)
        {
            PrepareQuery();
            if (float.IsInfinity(maxDist) || maxDist > 1e30f) maxDist = 1e6f;
            var swept = queryBounds; swept.Encapsulate(new Bounds(queryBounds.center + dir * maxDist, queryBounds.size));
            var hits = new List<Hit>();
            foreach (var s in shapes.Values)
            {
                if (s.Col == ignore || !Passes(s, mask, q) || !s.Aabb.Intersects(swept)) continue;
                if (SweepShape(query, s, dir, maxDist, swept, out var h)) hits.Add(h);
            }
            hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            if (!all && hits.Count > 1) hits.RemoveRange(1, hits.Count - 1);
            return hits;
        }

        public static bool SweepShape(Convex query, Shape s, Vector3 dir, float maxDist, Bounds swept, out Hit h)
        {
            h = default; h.Shape = s; h.Face = -1;
            if (s.Geom.Kind != GeomKind.Mesh)
            {
                if (!Gjk.Sweep(query, ConvexOf(s), dir, maxDist, out float t, out var p, out var n)) return false;
                h.Distance = t; h.Point = p; h.Normal = n; return true;
            }
            var lb = new Geom { Kind = GeomKind.Box, HalfExtents = swept.extents }.WorldBounds(new PxPose(s.World.InvApply(swept.center), Quaternion.Inverse(s.World.q)));
            var tris = new List<int>(); s.Geom.Mesh.Query(lb, tris);
            bool any = false; h.Distance = float.MaxValue;
            foreach (int ti in tris)
            {
                s.Geom.Mesh.Tri(ti, out var a, out var b, out var c);
                var tri = new PointsConvex(0, s.World.Apply(a), s.World.Apply(b), s.World.Apply(c));
                if (Gjk.Sweep(query, tri, dir, maxDist, out float t, out var p, out var n) && t < h.Distance)
                {
                    var fn = s.World.Rotate(Vector3.Cross(b - a, c - a).normalized);
                    if (!QueriesHitBackfaces && Vector3.Dot(fn, dir) > 0) continue;
                    h.Distance = t; h.Point = p; h.Normal = n; h.Face = ti; any = true;
                }
            }
            return any;
        }

        public static List<Shape> Overlap(Convex query, Bounds qb, int mask, QueryTriggerInteraction q)
        {
            PrepareQuery();
            var res = new List<Shape>();
            foreach (var s in shapes.Values.OrderBy(s => s.Id))
            {
                if (!Passes(s, mask, q) || !s.Aabb.Intersects(qb)) continue;
                if (OverlapShape(query, qb, s)) res.Add(s);
            }
            return res;
        }

        public static bool OverlapShape(Convex query, Bounds qb, Shape s)
        {
            float r = query.Radius + s.Geom.CoreRadius;
            if (s.Geom.Kind != GeomKind.Mesh)
            {
                bool sep = Gjk.Distance(query, ConvexOf(s), out float d, out _, out _);
                return !sep || d <= r;
            }
            var lb = new Geom { Kind = GeomKind.Box, HalfExtents = qb.extents }.WorldBounds(new PxPose(s.World.InvApply(qb.center), Quaternion.Inverse(s.World.q)));
            var tris = new List<int>(); s.Geom.Mesh.Query(lb, tris);
            foreach (int ti in tris)
            {
                s.Geom.Mesh.Tri(ti, out var a, out var b, out var c);
                var tri = new PointsConvex(0, s.World.Apply(a), s.World.Apply(b), s.World.Apply(c));
                bool sep = Gjk.Distance(query, tri, out float d, out _, out _);
                if (!sep || d <= query.Radius) return true;
            }
            return false;
        }

        public static IEnumerable<Body> Bodies => bodies.Values.Distinct();
    }
}
