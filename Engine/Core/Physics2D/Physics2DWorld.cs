// Unity side of MiniUnity's 2D physics: keeps a Box2D world (B2World.cs) in step with the scene's Rigidbody2D / Collider2D /
// Joint2D components, runs the simulation step, produces OnCollision*2D / OnTrigger*2D messages and answers the Physics2D
// queries. Behaviour follows Unity 2022.3's Physics2D module on top of Box2D (settings from ProjectSettings/Physics2DSettings.asset).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MiniUnity.Box2D
{
    public static class Physics2DWorld
    {
        // ------------------------------------------------------------------ settings
        public static World2 World = new();
        public static bool QueriesHitTriggers = true, QueriesStartInColliders = true, CallbacksOnDisable = true, ReuseCollisionCallbacks = true,
            AutoSyncTransforms, ChangeStopsCallbacks;
        public static float DefaultContactOffset = 0.01f, BaumgarteTOIScale = 0.75f;
        public static SimulationMode2D Mode = SimulationMode2D.FixedUpdate;
        public static readonly uint[] LayerMatrix = Enumerable.Repeat(0xFFFFFFFFu, 32).ToArray();
        public static readonly HashSet<(int, int)> IgnoredPairs = new();
        public static PhysicsMaterial2D DefaultMaterial;
        static bool settingsLoaded;

        public static void EnsureSettings() { if (!settingsLoaded) LoadSettings(); }

        public static void LoadSettings()
        {
            settingsLoaded = true;
            try
            {
                var path = System.IO.Path.Combine(AssetIndex.Root ?? ".", "ProjectSettings/Physics2DSettings.asset");
                if (!System.IO.File.Exists(path)) return;
                var doc = Yaml.YamlReader.ReadFile(path).FirstOrDefault();
                if (doc?.Body is not Yaml.YMap m) return;
                float F(string k, float d) => m[k] is Yaml.YScalar s && s.Value.Length > 0 ? s.F : d;
                var w = World;
                if (m["m_Gravity"] != null) w.Gravity = Serializer.ReadV2(m["m_Gravity"]);
                w.VelocityIterations = (int)F("m_VelocityIterations", 8); w.PositionIterations = (int)F("m_PositionIterations", 3);
                w.VelocityThreshold = F("m_VelocityThreshold", 1);
                w.MaxLinearCorrection = F("m_MaxLinearCorrection", 0.2f); w.MaxAngularCorrection = F("m_MaxAngularCorrection", 8) * Mathf.Deg2Rad;
                w.MaxTranslationSpeed = F("m_MaxTranslationSpeed", 100); w.MaxRotationSpeed = F("m_MaxRotationSpeed", 360) * Mathf.Deg2Rad;
                w.Baumgarte = F("m_BaumgarteScale", 0.2f); BaumgarteTOIScale = F("m_BaumgarteTimeOfImpactScale", 0.75f);
                w.TimeToSleep = F("m_TimeToSleep", 0.5f); w.LinearSleepTolerance = F("m_LinearSleepTolerance", 0.01f);
                w.AngularSleepTolerance = F("m_AngularSleepTolerance", 2) * Mathf.Deg2Rad;
                DefaultContactOffset = F("m_DefaultContactOffset", 0.01f);
                QueriesHitTriggers = F("m_QueriesHitTriggers", 1) != 0; QueriesStartInColliders = F("m_QueriesStartInColliders", 1) != 0;
                CallbacksOnDisable = F("m_CallbacksOnDisable", 1) != 0; ReuseCollisionCallbacks = F("m_ReuseCollisionCallbacks", 1) != 0;
                AutoSyncTransforms = F("m_AutoSyncTransforms", 0) != 0;
                Mode = F("m_AutoSimulation", 1) != 0 ? SimulationMode2D.FixedUpdate : SimulationMode2D.Script;
                if (m["m_SimulationMode"] is Yaml.YScalar sm && sm.Value.Length > 0) Mode = (SimulationMode2D)(int)sm.L;
                if (m["m_DefaultMaterial"] is Yaml.YRef r && (r.FileID != 0 || r.Target != null))
                    DefaultMaterial = (r.Raw ? AssetLoader.ResolveExternal(r) : r.Target) as PhysicsMaterial2D;
                if (m["m_LayerCollisionMatrix"] is Yaml.YScalar lm && lm.Value.Length >= 256)
                    for (int i = 0; i < 32; i++)
                    {
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
        sealed class ColState { public Body2 Body; public string Key; public List<Fixture2> Fixtures = new(); }
        static readonly Dictionary<Rigidbody2D, Body2> bodies = new();
        static readonly Dictionary<Collider2D, ColState> cols = new();
        static readonly Dictionary<Joint2D, Joint2> joints = new();
        static readonly List<Fixture2> fixtures = new();
        static Body2 ground;
        static int nextFixtureId;
        public static float LastStep = 0.02f;
        static Dictionary<(Collider2D, Collider2D), bool> touching = new();   // collider pair → is trigger pair
        public static readonly List<(string msg, Component self, Collider2D other, Collision2D col)> PendingEvents = new();
        public static readonly List<(Joint2D joint, float force, float torque)> PendingJointBreaks = new();

        public static void Reset()
        {
            World = new World2(); bodies.Clear(); cols.Clear(); joints.Clear(); fixtures.Clear(); touching.Clear();
            PendingEvents.Clear(); PendingJointBreaks.Clear(); IgnoredPairs.Clear(); ground = null; nextFixtureId = 0;
            settingsLoaded = false; DefaultMaterial = null;
            for (int i = 0; i < 32; i++) LayerMatrix[i] = 0xFFFFFFFF;
            QueriesHitTriggers = QueriesStartInColliders = CallbacksOnDisable = ReuseCollisionCallbacks = true; AutoSyncTransforms = false;
            Mode = SimulationMode2D.FixedUpdate;
        }

        public static Body2 Ground
        {
            get
            {
                if (ground != null) return ground;
                ground = new Body2 { Type = BodyType2.Static, Mass = 0, InvMass = 0, InvMassAxis = Vector2.zero, Awake = false };
                World.Bodies.Add(ground); World.ShouldCollide = ShouldCollide;
                World.PreSolve = (c, old) => { c.PreSolveRelVel = c.B.Body.V - c.A.Body.V; PlatformEffector.PreSolve(c); };
                return ground;
            }
        }

        public static Body2 BodyOf(Rigidbody2D rb) => rb != null && bodies.TryGetValue(rb, out var b) ? b : null;
        public static IEnumerable<Fixture2> FixturesOf(Collider2D c) => c != null && cols.TryGetValue(c, out var s) ? s.Fixtures : Enumerable.Empty<Fixture2>();
        public static IEnumerable<Fixture2> AllFixtures => fixtures;
        public static Joint2 JointOf(Joint2D j) => j != null && joints.TryGetValue(j, out var jj) ? jj : null;

        static IEnumerable<GameObject> AllObjects()
        {
            var s = SceneData.Active;
            if (s == null) yield break;
            foreach (var g in Hier.SceneObjects(s)) yield return g;
        }

        static float Angle2D(Transform t) => t.rotation.eulerAngles.z * Mathf.Deg2Rad;

        /// <summary>Bring the Box2D world in line with the scene (components added / removed / changed, transforms moved by scripts).</summary>
        public static void Sync()
        {
            EnsureSettings();
            _ = Ground;
            var liveBodies = new HashSet<Rigidbody2D>(); var liveCols = new List<Collider2D>(); var liveJoints = new List<Joint2D>();
            foreach (var go in AllObjects())
            {
                if (go.m_Destroyed || !go.activeInHierarchy) continue;
                foreach (var c in go.m_Components)
                {
                    if (c == null || c.m_Destroyed) continue;
                    if (c is Rigidbody2D rb) { if (rb.simulated) liveBodies.Add(rb); }
                    else if (c is Collider2D col) { if (col.enabled && !(col.usedByComposite && col.composite != null)) liveCols.Add(col); }
                    else if (c is Joint2D j) { if (j.enabled) liveJoints.Add(j); }
                }
            }
            // bodies
            foreach (var k in bodies.Keys.ToList()) if (!liveBodies.Contains(k)) RemoveBody(k);
            foreach (var rb in liveBodies) SyncBody(rb);
            // colliders
            var liveSet = new HashSet<Collider2D>(liveCols);
            foreach (var k in cols.Keys.ToList())
            {
                if (!liveSet.Contains(k) || cols[k].Body != Owner(k)) RemoveCollider(k);   // destroyed colliders: no Owner lookup
            }
            foreach (var col in liveCols) SyncCollider(col);
            foreach (var (rb, b) in bodies) UpdateMass(rb, b);
            // joints
            var jset = new HashSet<Joint2D>(liveJoints);
            foreach (var k in joints.Keys.ToList()) if (!jset.Contains(k) || !JointBodiesValid(k, joints[k])) RemoveJoint(k);
            foreach (var j in liveJoints) SyncJoint(j);
            foreach (var f in fixtures) f.UpdateAabb();
        }

        static void SyncBody(Rigidbody2D rb)
        {
            rb.CacheProps();
            var t = rb.transform; var p = (Vector2)t.position; float a = Angle2D(t);
            if (!bodies.TryGetValue(rb, out var b))
            {
                b = new Body2 { Owner = rb };
                b.SetTransform(p, a);
                b.Awake = rb.m_SleepModeCache != RigidbodySleepMode2D.StartAsleep;
                rb.m_LastWrittenP = p; rb.m_LastWrittenA = a; rb.m_PrevP = rb.m_CurP = p; rb.m_PrevA = rb.m_CurA = a;
                bodies[rb] = b; World.Bodies.Add(b);
            }
            else if ((p - rb.m_LastWrittenP).sqrMagnitude > 1e-12f || Math.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, rb.m_LastWrittenA * Mathf.Rad2Deg)) > 1e-4f)
            {
                // Transform moved by a script / animation: the body follows it (Unity syncs Transforms before the step)
                b.SetTransform(p, a); rb.m_LastWrittenP = p; rb.m_LastWrittenA = a; rb.m_InterpValid = false;
                b.SetAwake(true);
                foreach (var c in b.Contacts) c.B.Body.SetAwake(true);
            }
            var type = (BodyType2)(2 - (int)rb.m_BodyTypeCache);   // Unity: Dynamic 0, Kinematic 1, Static 2
            if (b.Type != type)
            {
                b.Type = type;
                if (type == BodyType2.Static) { b.V = Vector2.zero; b.W = 0; }
                b.SetAwake(true);
            }
            b.LinearDamping = rb.m_DragCache; b.AngularDamping = rb.m_AngularDragCache; b.GravityScale = rb.m_GravityScaleCache;
            b.AutoSleep = rb.m_SleepModeCache != RigidbodySleepMode2D.NeverSleep;
            b.FullKinematicContacts = rb.m_FullKinematicCache;
            b.Bullet = rb.m_CollisionModeCache == CollisionDetectionMode2D.Continuous;
            var cons = rb.m_ConstraintsCache;
            b.FixedRotation = (cons & RigidbodyConstraints2D.FreezeRotation) != 0;
            b.InvMassAxis = new Vector2((cons & RigidbodyConstraints2D.FreezePositionX) != 0 ? 0 : 1, (cons & RigidbodyConstraints2D.FreezePositionY) != 0 ? 0 : 1);
            if (!b.Awake && !b.AutoSleep) b.SetAwake(true);
        }

        static void RemoveBody(Rigidbody2D rb)
        {
            var b = bodies[rb]; bodies.Remove(rb);
            foreach (var c in b.Contacts.ToList()) World.RemoveContact(c);
            foreach (var k in cols.Where(kv => kv.Value.Body == b).Select(kv => kv.Key).ToList()) RemoveCollider(k);
            foreach (var k in joints.Where(kv => kv.Value.A == b || kv.Value.B == b).Select(kv => kv.Key).ToList()) RemoveJoint(k);
            World.Bodies.Remove(b);
        }

        /// <summary>The body a collider attaches to: the nearest simulated Rigidbody2D up the hierarchy, else the static ground.</summary>
        static Body2 Owner(Collider2D col)
        {
            var rb = col.attachedRigidbody;
            if (rb != null && rb.simulated && bodies.TryGetValue(rb, out var b)) return b;
            return rb != null && !rb.simulated ? null : Ground;
        }

        static void SyncCollider(Collider2D col)
        {
            var body = Owner(col);
            if (body == null) return;      // on a Rigidbody2D that isn't simulated
            var t = col.transform;
            // collider pose relative to the body's Transform (static colliders: world), both read from Transforms so the
            // key stays stable while the body moves
            Xf rel;
            if (body == ground) rel = new Xf((Vector2)t.position, Angle2D(t));
            else
            {
                var bt = ((Rigidbody2D)body.Owner).transform;
                rel = bt == t ? Xf.Identity : Xf.MulT(new Xf((Vector2)bt.position, Angle2D(bt)), new Xf((Vector2)t.position, Angle2D(t)));
            }
            var scale = (Vector2)t.lossyScale;
            string key = col.ShapeKey() + "|" + rel.p.x.ToString("F5") + "," + rel.p.y.ToString("F5") + "," + rel.q.s.ToString("F6") + "," + rel.q.c.ToString("F6") + "|" + scale.x.ToString("F5") + "," + scale.y.ToString("F5");
            if (!cols.TryGetValue(col, out var st)) { st = new ColState { Body = body }; cols[col] = st; }
            var mat = col.sharedMaterial ?? col.attachedRigidbody?.sharedMaterial ?? DefaultMaterial;
            float friction = mat != null ? mat.friction : 0.4f, bounce = mat != null ? mat.bounciness : 0;
            if (st.Key != key)
            {
                foreach (var f in st.Fixtures) RemoveFixture(f);
                st.Fixtures.Clear(); st.Key = key;
                foreach (var shape in col.BuildShapes(scale, DefaultContactOffset))
                {
                    var f = new Fixture2 { Owner = col, Body = body, Shape = shape.Transformed(rel), Id = nextFixtureId++ };
                    st.Fixtures.Add(f); fixtures.Add(f); body.Fixtures.Add(f);
                }
                body.SetAwake(true);
            }
            foreach (var f in st.Fixtures)
            {
                f.Density = col.density; f.Friction = friction; f.Restitution = bounce; f.Layer = col.gameObject.layer;
                if (f.Sensor != col.isTrigger) { f.Sensor = col.isTrigger; foreach (var c in f.Body.Contacts.Where(c => c.A == f || c.B == f).ToList()) World.RemoveContact(c); }
            }
        }

        static void RemoveFixture(Fixture2 f)
        {
            foreach (var c in f.Body.Contacts.Where(c => c.A == f || c.B == f).ToList()) World.RemoveContact(c);
            fixtures.Remove(f); f.Body.Fixtures.Remove(f);
            f.Body.SetAwake(true);
        }

        static void RemoveCollider(Collider2D col)
        {
            if (!cols.TryGetValue(col, out var st)) return;
            foreach (var f in st.Fixtures) RemoveFixture(f);
            cols.Remove(col);
        }

        /// <summary>b2Body::ResetMassData with Unity's useAutoMass / explicit mass, centre of mass and inertia.</summary>
        static void UpdateMass(Rigidbody2D rb, Body2 b)
        {
            float mass = 0, I = 0; var center = Vector2.zero;
            foreach (var f in b.Fixtures)
            {
                if (f.Density == 0 || f.Sensor && false) continue;
                f.Shape.ComputeMass(f.Density, out float m, out var c, out float i);
                mass += m; center += m * c; I += i;
            }
            if (b.Type != BodyType2.Dynamic)
            {
                b.Mass = rb.m_MassCache; b.InvMass = 0; b.I = 0; b.InvI = 0;
                b.LocalCenter = mass > 0 ? center / mass : Vector2.zero;
                if (rb.m_HasCom) b.LocalCenter = rb.m_ComCache;
                b.C = b.Xf.Mul(b.LocalCenter);
                rb.m_AutoMass = mass;
                return;
            }
            if (mass > 0) center /= mass;
            float autoMass = mass;
            rb.m_AutoMass = autoMass;
            float targetMass = rb.m_UseAutoMassCache ? (autoMass > 0 ? autoMass : 1) : Math.Max(rb.m_MassCache, 1e-4f);
            // rotational inertia about the centre of mass, scaled to the body mass when it is set explicitly
            float Ic = I - mass * B2.Dot(center, center);
            if (autoMass > 0 && !rb.m_UseAutoMassCache) Ic *= targetMass / autoMass;
            var lc = rb.m_HasCom ? rb.m_ComCache : center;
            b.Mass = targetMass; b.InvMass = 1 / targetMass;
            if (rb.m_HasInertia) Ic = rb.m_InertiaCache;
            else if (rb.m_HasCom) Ic += targetMass * (lc - center).sqrMagnitude;
            if (Ic > 0 && !b.FixedRotation) { b.I = Ic; b.InvI = 1 / Ic; }
            else { b.I = Math.Max(Ic, 0); b.InvI = 0; }
            if (b.LocalCenter != lc)
            {
                var oldC = b.C;
                b.LocalCenter = lc; b.C = b.Xf.Mul(lc); b.C0 = b.C;
                b.V += B2.Cross(b.W, b.C - oldC);
            }
            else b.C = b.Xf.Mul(lc);
        }

        // ------------------------------------------------------------------ joints
        static bool JointBodiesValid(Joint2D j, Joint2 jj)
        {
            var self = BodyOf(j.attachedRigidbody); var other = j.connectedBody != null ? BodyOf(j.connectedBody) : Ground;
            if (j is TargetJoint2D) other = Ground;
            return j.SelfIsBodyA ? jj.A == self && jj.B == other : jj.B == self && jj.A == other;
        }

        static void SyncJoint(Joint2D j)
        {
            var self = BodyOf(j.attachedRigidbody);
            if (self == null) return;
            var other = j is TargetJoint2D ? Ground : j.connectedBody != null ? BodyOf(j.connectedBody) : Ground;
            if (other == null || other == self) return;
            if (!joints.TryGetValue(j, out var jj))
            {
                // Box2D body A / B: the connected body (or ground) is A, except for joints that drive the connected body
                // relative to this one (WheelJoint2D: chassis A, wheel B)
                var (A, B) = j.SelfIsBodyA ? (self, other) : (other, self);
                jj = j.CreateBox2DJoint(A, B);
                if (jj == null) return;
                jj.Owner = j; jj.A = A; jj.B = B;
                joints[j] = jj; World.Joints.Add(jj); other.Joints.Add(jj); self.Joints.Add(jj);
                // contacts between the connected bodies stop unless enableCollision
                foreach (var c in self.Contacts.Where(c => c.A.Body == other || c.B.Body == other).ToList()) if (!j.enableCollision) World.RemoveContact(c);
                self.SetAwake(true); other.SetAwake(true);
            }
            jj.CollideConnected = j.enableCollision;
            j.UpdateBox2DJoint(jj);
        }

        static void RemoveJoint(Joint2D j)
        {
            if (!joints.TryGetValue(j, out var jj)) return;
            joints.Remove(j); World.Joints.Remove(jj); jj.A.Joints.Remove(jj); jj.B.Joints.Remove(jj);
            jj.A.SetAwake(true); jj.B.SetAwake(true);
        }

        // ------------------------------------------------------------------ filtering
        static bool ShouldCollide(Fixture2 a, Fixture2 b)
        {
            var ca = (Collider2D)a.Owner; var cb = (Collider2D)b.Owner;
            if (IgnoredPairs.Contains(PairKey(ca, cb))) return false;
            int la = ca.gameObject.layer, lb = cb.gameObject.layer;
            bool layers = LayersCollide(la, lb);
            // per-collider / per-body include and exclude layers (Unity 2022.2+)
            if ((ca.m_ExcludeLayers & (1 << lb)) != 0 || (cb.m_ExcludeLayers & (1 << la)) != 0) layers = false;
            var ra = ca.attachedRigidbody; var rbb = cb.attachedRigidbody;
            if (ra != null && (ra.m_ExcludeLayers & (1 << lb)) != 0 || rbb != null && (rbb.m_ExcludeLayers & (1 << la)) != 0) layers = false;
            if ((ca.m_IncludeLayers & (1 << lb)) != 0 || (cb.m_IncludeLayers & (1 << la)) != 0) layers = true;
            if (ra != null && (ra.m_IncludeLayers & (1 << lb)) != 0 || rbb != null && (rbb.m_IncludeLayers & (1 << la)) != 0) layers = true;
            return layers;
        }

        public static (int, int) PairKey(Collider2D a, Collider2D b)
        {
            int x = a.GetInstanceID(), y = b.GetInstanceID();
            return x < y ? (x, y) : (y, x);
        }

        // ------------------------------------------------------------------ step
        public static void Simulate(float dt)
        {
            if (dt <= 0) return;
            LastStep = dt;
            Sync();
            // ConstantForce2D (Unity applies it in the internal fixed update before the step)
            foreach (var (rb, b) in bodies)
            {
                if (b.Type != BodyType2.Dynamic) continue;
                foreach (var cf in rb.gameObject.m_Components.OfType<ConstantForce2D>())
                    if (cf.enabled) { rb.AddForce(cf.force); rb.AddRelativeForce(cf.relativeForce); rb.AddTorque(cf.torque); }
            }
            Effectors.Apply(dt);
            // MovePosition / MoveRotation: the body is driven to the target over this step
            var restore = new List<(Body2 b, Vector2 v, float w, float g)>();
            foreach (var (rb, b) in bodies)
            {
                if (!rb.m_HasMoveP && !rb.m_HasMoveR) continue;
                if (b.Type == BodyType2.Static) { rb.m_HasMoveP = rb.m_HasMoveR = false; continue; }
                var targetP = rb.m_HasMoveP ? rb.m_MoveP : b.Xf.p; float targetA = rb.m_HasMoveR ? rb.m_MoveA : b.A;
                var targetC = new Xf(targetP, targetA).Mul(b.LocalCenter);
                // Unity restores the body's own velocity after the move step (kinematic and dynamic bodies alike)
                restore.Add((b, b.V, b.W, b.GravityScale));
                if (rb.m_HasMoveP || rb.m_HasMoveR) b.V = (targetC - b.C) / dt;
                if (rb.m_HasMoveR) b.W = Mathf.DeltaAngle(b.A * Mathf.Rad2Deg, targetA * Mathf.Rad2Deg) * Mathf.Deg2Rad / dt;
                else b.W = restore[^1].w;
                if (b.Type == BodyType2.Dynamic) b.GravityScale = 0;
                b.SetAwake(true);
                rb.m_HasMoveP = rb.m_HasMoveR = false;
            }
            foreach (var (rb, b) in bodies) { rb.m_PrevP = rb.m_CurP; rb.m_PrevA = rb.m_CurA; }
            World.Step(dt, fixtures);
            foreach (var (b, v, w, g) in restore)
            {
                b.V = v; b.W = w; b.GravityScale = g;
            }
            // write back
            foreach (var (rb, b) in bodies)
            {
                if (b.Type == BodyType2.Static) continue;
                var p = b.Xf.p; float a = b.A;
                rb.m_CurP = p; rb.m_CurA = a; rb.m_InterpValid = true;
                WriteTransform(rb, p, a);
            }
            CheckJointBreaks(dt);
            BuildEvents();
        }

        public static void WriteTransform(Rigidbody2D rb, Vector2 p, float a)
        {
            var t = rb.transform;
            var old = t.position;
            var e = t.rotation.eulerAngles;
            t.position = new Vector3(p.x, p.y, old.z);
            t.rotation = Quaternion.Euler(e.x, e.y, a * Mathf.Rad2Deg);
            rb.m_LastWrittenP = (Vector2)t.position; rb.m_LastWrittenA = Angle2D(t);
        }

        /// <summary>Rigidbody2D interpolation / extrapolation (before Update, like Unity).</summary>
        public static void Interpolate(float alpha)
        {
            foreach (var (rb, b) in bodies)
            {
                if (b.Type == BodyType2.Static || !rb.m_InterpValid || rb.m_InterpolationCache == RigidbodyInterpolation2D.None || !b.Awake) continue;
                Vector2 p; float a;
                if (rb.m_InterpolationCache == RigidbodyInterpolation2D.Interpolate)
                {
                    p = Vector2.Lerp(rb.m_PrevP, rb.m_CurP, alpha);
                    a = Mathf.LerpAngle(rb.m_PrevA * Mathf.Rad2Deg, rb.m_CurA * Mathf.Rad2Deg, alpha) * Mathf.Deg2Rad;
                }
                else { p = rb.m_CurP + b.V * (alpha * LastStep); a = rb.m_CurA + b.W * alpha * LastStep; }
                WriteTransform(rb, p, a);
            }
        }

        static void CheckJointBreaks(float dt)
        {
            foreach (var (j, jj) in joints.ToList())
            {
                float f = jj.ReactionForce(1 / dt).magnitude, tq = Math.Abs(jj.ReactionTorque(1 / dt));
                if (f > j.breakForce || tq > j.breakTorque) PendingJointBreaks.Add((j, f, tq));
            }
        }

        // ------------------------------------------------------------------ events
        static void BuildEvents()
        {
            // touching collider pairs from the fixture contacts (Box2D updates touching in Collide at the start of the step)
            var now = new Dictionary<(Collider2D, Collider2D), bool>();
            var contactsOf = new Dictionary<(Collider2D, Collider2D), List<Contact2>>();
            foreach (var c in World.Contacts.Values)
            {
                if (!c.Touching) continue;
                var ca = (Collider2D)c.A.Owner; var cb = (Collider2D)c.B.Owner;
                if (ca == cb) continue;
                var key = ca.GetInstanceID() < cb.GetInstanceID() ? (ca, cb) : (cb, ca);
                now[key] = c.Sensor;
                if (!contactsOf.TryGetValue(key, out var l)) contactsOf[key] = l = new List<Contact2>();
                l.Add(c);
            }
            // triggers first, then collisions (Unity: trigger callbacks before collision callbacks)
            for (int pass = 0; pass < 2; pass++)
            {
                bool trig = pass == 0;
                foreach (var (k, isTrig) in now)
                {
                    if (isTrig != trig) continue;
                    bool was = touching.TryGetValue(k, out var wasTrig) && wasTrig == isTrig;
                    if (was && !AnyAwake(contactsOf[k])) continue;    // no Stay callbacks for sleeping bodies
                    Queue(trig ? (was ? "OnTriggerStay2D" : "OnTriggerEnter2D") : (was ? "OnCollisionStay2D" : "OnCollisionEnter2D"), k.Item1, k.Item2, trig ? null : contactsOf[k]);
                }
                foreach (var (k, wasTrig) in touching)
                {
                    if (wasTrig != trig || now.TryGetValue(k, out var t2) && t2 == wasTrig) continue;
                    Queue(trig ? "OnTriggerExit2D" : "OnCollisionExit2D", k.Item1, k.Item2, trig ? null : new List<Contact2>());
                }
            }
            touching = now;
        }

        static bool AnyAwake(List<Contact2> cs)
        {
            foreach (var c in cs) if (c.A.Body.Awake && !c.A.Body.IsStatic || c.B.Body.Awake && !c.B.Body.IsStatic) return true;
            return false;
        }

        static void Queue(string msg, Collider2D a, Collider2D b, List<Contact2> contacts)
        {
            bool aliveA = a != null && !a.m_Destroyed, aliveB = b != null && !b.m_Destroyed;
            if (msg.EndsWith("Exit2D") && !CallbacksOnDisable && (!aliveA || !aliveB || !a.enabled || !b.enabled)) return;
            if (aliveA) PendingEvents.Add((msg, a, b, contacts == null ? null : MakeCollision(a, b, contacts)));
            if (aliveB) PendingEvents.Add((msg, b, a, contacts == null ? null : MakeCollision(b, a, contacts)));
        }

        /// <summary>Collision2D as seen by <paramref name="self"/>: collider = the other collider, otherCollider = self; normals point from the other collider towards self.</summary>
        public static Collision2D MakeCollision(Collider2D self, Collider2D other, List<Contact2> contacts)
        {
            var col = new Collision2D();
            var pts = ContactPoints(self, other, contacts);
            col.m_Collider = other.GetInstanceID(); col.m_OtherCollider = self.GetInstanceID();
            col.m_ColliderRef = other; col.m_OtherColliderRef = self;
            col.m_RigidbodyRef = other.attachedRigidbody; col.m_OtherRigidbodyRef = self.attachedRigidbody;
            col.m_Rigidbody = col.m_RigidbodyRef != null ? col.m_RigidbodyRef.GetInstanceID() : 0;
            col.m_OtherRigidbody = col.m_OtherRigidbodyRef != null ? col.m_OtherRigidbodyRef.GetInstanceID() : 0;
            col.m_ReusedContacts = pts; col.m_ContactCount = pts.Length;
            col.m_Enabled = contacts.Count == 0 || contacts.Any(c => c.Enabled) ? 1 : 0;
            if (contacts.Count > 0)
            {
                var c = contacts[0]; bool selfIsA = (Collider2D)c.A.Owner == self;
                col.m_RelativeVelocity = selfIsA ? c.PreSolveRelVel : -c.PreSolveRelVel;   // other − self
            }
            return col;
        }

        public static ContactPoint2D[] ContactPoints(Collider2D self, Collider2D other, List<Contact2> contacts)
        {
            var res = new List<ContactPoint2D>();
            foreach (var c in contacts)
            {
                if (c.Sensor || c.M.PointCount == 0) continue;
                bool selfIsA = (Collider2D)c.A.Owner == self;
                var wm = WorldManifold.Of(c.M, c.A.Body.Xf, c.A.Shape.Radius, c.B.Body.Xf, c.B.Shape.Radius);
                // Box2D normal points from A to B; Unity reports it pointing from the other collider towards self
                var n = selfIsA ? -wm.Normal : wm.Normal;
                var selfBody = selfIsA ? c.A.Body : c.B.Body; var otherBody = selfIsA ? c.B.Body : c.A.Body;
                for (int i = 0; i < c.M.PointCount; i++)
                {
                    var p = wm.Point(i);
                    res.Add(new ContactPoint2D
                    {
                        m_Point = p, m_Normal = n, m_Separation = wm.Separation(i),
                        m_NormalImpulse = c.M.Points[i].NormalImpulse, m_TangentImpulse = c.M.Points[i].TangentImpulse * (selfIsA ? 1 : -1),
                        m_RelativeVelocity = otherBody.PointVelocity(p) - selfBody.PointVelocity(p),
                        m_Collider = other.GetInstanceID(), m_OtherCollider = self.GetInstanceID(),
                        m_ColliderRef = other, m_OtherColliderRef = self,
                        m_Rigidbody = other.attachedRigidbody != null ? other.attachedRigidbody.GetInstanceID() : 0,
                        m_OtherRigidbody = self.attachedRigidbody != null ? self.attachedRigidbody.GetInstanceID() : 0,
                        m_Enabled = c.Enabled ? 1 : 0,
                    });
                }
            }
            return res.ToArray();
        }

        /// <summary>Touching contacts of a collider (non-trigger contacts carry points), grouped by the other collider.</summary>
        public static Dictionary<Collider2D, List<Contact2>> ContactsOf(Collider2D self, bool includeTriggers = true)
        {
            var res = new Dictionary<Collider2D, List<Contact2>>();
            foreach (var f in FixturesOf(self))
                foreach (var c in f.Body.Contacts)
                {
                    if (!c.Touching || (c.A != f && c.B != f)) continue;
                    if (c.Sensor && !includeTriggers) continue;
                    var other = (Collider2D)(c.A == f ? c.B.Owner : c.A.Owner);
                    if (other == self) continue;
                    if (!res.TryGetValue(other, out var l)) res[other] = l = new List<Contact2>();
                    l.Add(c);
                }
            return res;
        }

        // ------------------------------------------------------------------ queries
        public static void PrepareQuery()
        {
            // Unity registers new colliders immediately; Transform changes reach the physics scene when autoSyncTransforms is on
            // or at the next step. MiniUnity syncs before every query (newly created colliders are queryable at once).
            Sync();
        }

        public static bool Passes(Collider2D c, in ContactFilter2D f)
        {
            if (f.IsFilteringTrigger(c)) return false;
            if (f.IsFilteringLayerMask(c.gameObject)) return false;
            if (f.IsFilteringDepth(c.gameObject)) return false;
            return true;
        }

        public struct QueryHit { public Collider2D Col; public float Fraction, Distance; public Vector2 Point, Normal, Centroid; }

        static void KeepNearest(Dictionary<Collider2D, QueryHit> best, QueryHit h)
        {
            if (!best.TryGetValue(h.Col, out var old) || h.Fraction < old.Fraction) best[h.Col] = h;
        }

        static List<QueryHit> Sorted(Dictionary<Collider2D, QueryHit> d)
        {
            var l = d.Values.ToList();
            l.Sort((a, b) => a.Fraction != b.Fraction ? a.Fraction.CompareTo(b.Fraction) : FixturesOf(a.Col).First().Id.CompareTo(FixturesOf(b.Col).First().Id));
            return l;
        }

        public static List<QueryHit> Raycast(Vector2 origin, Vector2 direction, float distance, ContactFilter2D filter, Collider2D ignoreBodyOf = null)
        {
            PrepareQuery();
            var dir = direction.normalized;
            if (dir == Vector2.zero) return new List<QueryHit>();
            if (float.IsInfinity(distance) || distance > 1e7f) distance = 1e7f;
            var p2 = origin + dir * distance;
            var best = new Dictionary<Collider2D, QueryHit>();
            foreach (var f in fixtures)
            {
                var col = (Collider2D)f.Owner;
                if (!Passes(col, filter)) continue;
                if (ignoreBodyOf != null && f.Body == FixturesOf(ignoreBodyOf).FirstOrDefault()?.Body && f.Body != ground) continue;
                if (f.Shape.Type != ShapeType.Edge && f.Shape.TestPoint(f.Body.Xf, origin))
                {
                    if (QueriesStartInColliders) KeepNearest(best, new QueryHit { Col = col, Fraction = 0, Distance = 0, Point = origin, Centroid = origin, Normal = -dir });
                    continue;
                }
                if (f.Shape.RayCast(origin, p2, 1, f.Body.Xf, out float fr, out var n))
                {
                    var pt = origin + (p2 - origin) * fr;
                    KeepNearest(best, new QueryHit { Col = col, Fraction = fr, Distance = fr * distance, Point = pt, Centroid = pt, Normal = n });
                }
            }
            // a collider the ray starts in is skipped entirely when queriesStartInColliders is off
            if (!QueriesStartInColliders)
                foreach (var f in fixtures) if (f.Shape.Type != ShapeType.Edge && f.Shape.TestPoint(f.Body.Xf, origin)) best.Remove((Collider2D)f.Owner);
            return Sorted(best);
        }

        public static List<QueryHit> Cast(IList<(Shape2 shape, Xf xf)> query, Vector2 direction, float distance, ContactFilter2D filter, Body2 ignoreBody = null)
        {
            PrepareQuery();
            var dir = direction.normalized;
            if (dir == Vector2.zero) return new List<QueryHit>();
            if (float.IsInfinity(distance) || distance > 1e6f) distance = 1e6f;
            var tr = dir * distance;
            var best = new Dictionary<Collider2D, QueryHit>();
            var startInside = new HashSet<Collider2D>();
            foreach (var f in fixtures)
            {
                var col = (Collider2D)f.Owner;
                if (ignoreBody != null && f.Body == ignoreBody) continue;
                if (!Passes(col, filter)) continue;
                foreach (var (shape, xf) in query)
                {
                    if (!Gjk2.Cast(shape, xf, tr, f.Shape, f.Body.Xf, out float fr, out var pt, out var n)) continue;
                    if (fr <= 0) { startInside.Add(col); if (!QueriesStartInColliders) continue; }
                    KeepNearest(best, new QueryHit { Col = col, Fraction = fr, Distance = fr * distance, Point = pt, Normal = n, Centroid = query[0].xf.p + tr * fr });
                }
            }
            if (!QueriesStartInColliders) foreach (var c in startInside) best.Remove(c);
            return Sorted(best);
        }

        public static List<Collider2D> Overlap(IList<(Shape2 shape, Xf xf)> query, ContactFilter2D filter, Body2 ignoreBody = null, Collider2D ignore = null)
        {
            PrepareQuery();
            var res = new List<Collider2D>(); var seen = new HashSet<Collider2D>();
            AABB2? box = null;
            foreach (var (s, x) in query) { var bb = s.ComputeAABB(x); box = box == null ? bb : AABB2.Union(box.Value, bb); }
            foreach (var f in fixtures)
            {
                var col = (Collider2D)f.Owner;
                if (col == ignore || seen.Contains(col) || ignoreBody != null && f.Body == ignoreBody) continue;
                if (box != null && !box.Value.Overlaps(f.Aabb)) continue;
                if (!Passes(col, filter)) continue;
                foreach (var (s, x) in query)
                    if (Gjk2.Overlap(s, x, f.Shape, f.Body.Xf)) { res.Add(col); seen.Add(col); break; }
            }
            return res;
        }

        public static List<Collider2D> OverlapPoint(Vector2 p, ContactFilter2D filter)
        {
            PrepareQuery();
            var res = new List<Collider2D>();
            foreach (var f in fixtures)
            {
                var col = (Collider2D)f.Owner;
                if (res.Contains(col) || !Passes(col, filter)) continue;
                if (f.Shape.TestPoint(f.Body.Xf, p)) res.Add(col);
            }
            return res;
        }

        /// <summary>Closest points between two colliders (negative distance when overlapping).</summary>
        public static ColliderDistance2D Distance(Collider2D a, Collider2D b)
        {
            PrepareQuery();
            var res = new ColliderDistance2D { isValid = false };
            float best = float.MaxValue;
            foreach (var fa in FixturesOf(a))
                foreach (var fb in FixturesOf(b))
                {
                    float d = Gjk2.ShapeDistance(fa.Shape, fa.Body.Xf, fb.Shape, fb.Body.Xf, out var pA, out var pB);
                    float rA = fa.Shape.Radius, rB = fb.Shape.Radius;
                    Vector2 n, ptA, ptB; float dist;
                    if (d > 10 * B2.Epsilon)
                    {
                        n = (pB - pA) / d; dist = d - rA - rB; ptA = pA + n * rA; ptB = pB - n * rB;
                    }
                    else
                    {
                        // overlapping cores: deepest point of the contact manifold
                        bool swap = Collide2.NeedsSwap(fa.Shape, fb.Shape);
                        var (A, B) = swap ? (fb, fa) : (fa, fb);
                        var m = new Manifold();
                        Collide2.Collide(m, A.Shape, A.Body.Xf, B.Shape, B.Body.Xf);
                        if (m.PointCount == 0) { n = Vector2.up; dist = -(rA + rB); ptA = ptB = pA; }
                        else
                        {
                            var wm = WorldManifold.Of(m, A.Body.Xf, A.Shape.Radius, B.Body.Xf, B.Shape.Radius);
                            int i = m.PointCount > 1 && wm.S1 < wm.S0 ? 1 : 0;
                            dist = wm.Separation(i); n = swap ? -wm.Normal : wm.Normal;
                            var mid = wm.Point(i);
                            ptA = mid - n * dist * 0.5f; ptB = mid + n * dist * 0.5f;
                        }
                    }
                    if (dist < best) { best = dist; res = new ColliderDistance2D { pointA = ptA, pointB = ptB, m_Normal = n, distance = dist, isValid = true }; }
                }
            return res;
        }

        public static IEnumerable<Body2> Bodies => bodies.Values;
        public static IEnumerable<(Joint2D, Joint2)> Joints => joints.Select(kv => (kv.Key, kv.Value));
    }
}
