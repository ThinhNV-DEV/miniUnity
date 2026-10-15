// UnityEngine.Physics2D (settings, simulation, contacts) and PhysicsScene2D. The query overload families (Raycast / Linecast /
// *Cast / Overlap* / GetRayIntersection / GetContacts …) are generated into Physics2DQueries.cs and call the helpers below.
using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity;
using MiniUnity.Box2D;

namespace UnityEngine
{
    public partial class Physics2D
    {
        public Physics2D() { }
        static World2 W { get { Physics2DWorld.EnsureSettings(); return Physics2DWorld.World; } }
        static PhysicsJobOptions2D s_JobOptions;

        public static PhysicsScene2D defaultPhysicsScene => new() { m_Handle = 1 };
        public static int velocityIterations { get => W.VelocityIterations; set => W.VelocityIterations = Math.Max(1, value); }
        public static int positionIterations { get => W.PositionIterations; set => W.PositionIterations = Math.Max(1, value); }
        public static Vector2 gravity { get => W.Gravity; set { W.Gravity = value; foreach (var b in Physics2DWorld.Bodies) b.SetAwake(true); } }
        public static bool queriesHitTriggers { get { Physics2DWorld.EnsureSettings(); return Physics2DWorld.QueriesHitTriggers; } set { Physics2DWorld.EnsureSettings(); Physics2DWorld.QueriesHitTriggers = value; } }
        public static bool queriesStartInColliders { get { Physics2DWorld.EnsureSettings(); return Physics2DWorld.QueriesStartInColliders; } set { Physics2DWorld.EnsureSettings(); Physics2DWorld.QueriesStartInColliders = value; } }
        public static bool callbacksOnDisable { get { Physics2DWorld.EnsureSettings(); return Physics2DWorld.CallbacksOnDisable; } set { Physics2DWorld.EnsureSettings(); Physics2DWorld.CallbacksOnDisable = value; } }
        public static bool reuseCollisionCallbacks { get { Physics2DWorld.EnsureSettings(); return Physics2DWorld.ReuseCollisionCallbacks; } set { Physics2DWorld.EnsureSettings(); Physics2DWorld.ReuseCollisionCallbacks = value; } }
        public static bool autoSyncTransforms { get { Physics2DWorld.EnsureSettings(); return Physics2DWorld.AutoSyncTransforms; } set { Physics2DWorld.EnsureSettings(); Physics2DWorld.AutoSyncTransforms = value; } }
        public static SimulationMode2D simulationMode { get { Physics2DWorld.EnsureSettings(); return Physics2DWorld.Mode; } set { Physics2DWorld.EnsureSettings(); Physics2DWorld.Mode = value; } }
        public static bool autoSimulation { get => simulationMode != SimulationMode2D.Script; set => simulationMode = value ? SimulationMode2D.FixedUpdate : SimulationMode2D.Script; }
        public static PhysicsJobOptions2D jobOptions { get => s_JobOptions; set => s_JobOptions = value; }
        public static float velocityThreshold { get => W.VelocityThreshold; set => W.VelocityThreshold = Math.Max(0, value); }
        public static float maxLinearCorrection { get => W.MaxLinearCorrection; set => W.MaxLinearCorrection = Math.Max(1e-4f, value); }
        public static float maxAngularCorrection { get => W.MaxAngularCorrection * Mathf.Rad2Deg; set => W.MaxAngularCorrection = Math.Max(1e-4f, value) * Mathf.Deg2Rad; }
        public static float maxTranslationSpeed { get => W.MaxTranslationSpeed; set => W.MaxTranslationSpeed = Math.Max(1e-4f, value); }
        public static float maxRotationSpeed { get => W.MaxRotationSpeed * Mathf.Rad2Deg; set => W.MaxRotationSpeed = Math.Max(1e-4f, value) * Mathf.Deg2Rad; }
        public static float defaultContactOffset { get { Physics2DWorld.EnsureSettings(); return Physics2DWorld.DefaultContactOffset; } set { Physics2DWorld.EnsureSettings(); Physics2DWorld.DefaultContactOffset = Math.Max(1e-4f, value); } }
        public static float baumgarteScale { get => W.Baumgarte; set => W.Baumgarte = Mathf.Clamp(value, 1e-4f, 1); }
        public static float baumgarteTOIScale { get { Physics2DWorld.EnsureSettings(); return Physics2DWorld.BaumgarteTOIScale; } set => Physics2DWorld.BaumgarteTOIScale = value; }
        public static float timeToSleep { get => W.TimeToSleep; set => W.TimeToSleep = Math.Max(0, value); }
        public static float linearSleepTolerance { get => W.LinearSleepTolerance; set => W.LinearSleepTolerance = Math.Max(0, value); }
        public static float angularSleepTolerance { get => W.AngularSleepTolerance * Mathf.Rad2Deg; set => W.AngularSleepTolerance = Math.Max(0, value) * Mathf.Deg2Rad; }
        public static bool changeStopsCallbacks { get => Physics2DWorld.ChangeStopsCallbacks; set => Physics2DWorld.ChangeStopsCallbacks = value; }
        public static float minPenetrationForPenalty { get => 0.01f; set { } }

        /// <summary>Steps the 2D world and sends the resulting callbacks (Unity sends them from within Simulate).</summary>
        public static bool Simulate(float step)
        {
            if (step < 0.0001f) return false;
            Physics2DWorld.Simulate(step);
            PhysicsMessages2D.Dispatch();
            return true;
        }
        internal static bool Simulate_Internal(PhysicsScene2D physicsScene, float step) => Simulate(step);
        public static void SyncTransforms() => Physics2DWorld.Sync();
        public static void IgnoreCollision(Collider2D collider1, Collider2D collider2) => IgnoreCollision(collider1, collider2, true);
        public static void IgnoreCollision(Collider2D collider1, Collider2D collider2, bool ignore)
        {
            if (collider1 == null || collider2 == null) throw new ArgumentNullException(collider1 == null ? nameof(collider1) : nameof(collider2));
            var k = Physics2DWorld.PairKey(collider1, collider2);
            if (ignore) Physics2DWorld.IgnoredPairs.Add(k); else Physics2DWorld.IgnoredPairs.Remove(k);
            foreach (var f in Physics2DWorld.FixturesOf(collider1)) f.Body.SetAwake(true);
        }
        public static bool GetIgnoreCollision(Collider2D collider1, Collider2D collider2) => Physics2DWorld.IgnoredPairs.Contains(Physics2DWorld.PairKey(collider1, collider2));
        public static void IgnoreLayerCollision(int layer1, int layer2) => IgnoreLayerCollision(layer1, layer2, true);
        public static void IgnoreLayerCollision(int layer1, int layer2, bool ignore)
        {
            if (layer1 < 0 || layer1 > 31 || layer2 < 0 || layer2 > 31) throw new ArgumentOutOfRangeException("layer", "Layer numbers must be between 0 and 31.");
            Physics2DWorld.EnsureSettings(); Physics2DWorld.SetLayerCollision(layer1, layer2, !ignore);
        }
        public static bool GetIgnoreLayerCollision(int layer1, int layer2) { Physics2DWorld.EnsureSettings(); return !Physics2DWorld.LayersCollide(layer1, layer2); }
        public static void SetLayerCollisionMask(int layer, int layerMask)
        {
            Physics2DWorld.EnsureSettings();
            for (int i = 0; i < 32; i++) Physics2DWorld.SetLayerCollision(layer, i, (layerMask & (1 << i)) != 0);
        }
        public static int GetLayerCollisionMask(int layer) { Physics2DWorld.EnsureSettings(); return (int)Physics2DWorld.LayerMatrix[layer & 31]; }
        public static bool IsTouching(Collider2D collider1, Collider2D collider2) => collider1.IsTouching(collider2);
        public static bool IsTouching(Collider2D collider1, Collider2D collider2, ContactFilter2D contactFilter) => collider1.IsTouching(collider2, contactFilter);
        public static bool IsTouching(Collider2D collider, ContactFilter2D contactFilter) => collider.IsTouching(contactFilter);
        public static bool IsTouchingLayers(Collider2D collider) => collider.IsTouchingLayers();
        public static bool IsTouchingLayers(Collider2D collider, int layerMask) => collider.IsTouchingLayers(layerMask);
        public static ColliderDistance2D Distance(Collider2D colliderA, Collider2D colliderB) => Physics2DWorld.Distance(colliderA, colliderB);
        public static Vector2 ClosestPoint(Vector2 position, Collider2D collider) => collider.ClosestPoint(position);
        public static Vector2 ClosestPoint(Vector2 position, Rigidbody2D rigidbody) => rigidbody.ClosestPoint(position);
        internal static void SetEditorDragMovement(bool dragging, GameObject[] objs) { }

        // ------------------------------------------------------------------ helpers used by the generated overloads
        internal static ContactFilter2D LF(int layerMask = DefaultRaycastLayers, float minDepth = float.NegativeInfinity, float maxDepth = float.PositiveInfinity)
            => ContactFilter2D.CreateLegacyFilter(layerMask, minDepth, maxDepth);

        internal static T One<T>(List<T> l) => l.Count > 0 ? l[0] : default;
        internal static int ToBuf<T>(List<T> l, T[] results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            int n = Math.Min(l.Count, results.Length); for (int i = 0; i < n; i++) results[i] = l[i]; return n;
        }
        internal static int ToList<T>(List<T> l, List<T> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear(); results.AddRange(l); return l.Count;
        }

        internal static List<RaycastHit2D> RayQ(Vector2 origin, Vector2 direction, float distance, ContactFilter2D f)
            => Physics2DWorld.Raycast(origin, direction, distance, f).Where(h => !f.useNormalAngle || !f.IsFilteringNormalAngle(h.Normal)).Select(RaycastHit2D.From).ToList();

        internal static List<RaycastHit2D> LineQ(Vector2 start, Vector2 end, ContactFilter2D f)
        {
            var d = end - start;
            return RayQ(start, d, d.magnitude, f);
        }

        internal static Shape2 CapsuleShape(Vector2 center, Vector2 size, CapsuleDirection2D dir, float angleDeg)
        {
            float w = Math.Abs(size.x), h = Math.Abs(size.y);
            var rot = new Rot(angleDeg * Mathf.Deg2Rad);
            float r, half; Vector2 axis;
            if (dir == CapsuleDirection2D.Vertical) { r = w * 0.5f; half = h * 0.5f - r; axis = Vector2.up; }
            else { r = h * 0.5f; half = w * 0.5f - r; axis = Vector2.right; }
            if (half <= 1e-5f) return Shape2.Circle(center, Math.Max(r, 1e-4f));
            var a = rot.Mul(-axis * half) + center; var b = rot.Mul(axis * half) + center;
            return Shape2.Capsule(a, b, Math.Max(r, 1e-4f));
        }
        static Shape2 BoxShape(Vector2 size) => Shape2.Box(Math.Max(Math.Abs(size.x) * 0.5f, 1e-5f), Math.Max(Math.Abs(size.y) * 0.5f, 1e-5f), Vector2.zero, 0, 0);

        static List<RaycastHit2D> CastShape(Shape2 s, Xf xf, Vector2 direction, float distance, ContactFilter2D f)
            => Physics2DWorld.Cast(new[] { (s, xf) }, direction, distance, f).Where(h => !f.useNormalAngle || !f.IsFilteringNormalAngle(h.Normal)).Select(RaycastHit2D.From).ToList();
        internal static List<RaycastHit2D> CircleCastQ(Vector2 origin, float radius, Vector2 direction, float distance, ContactFilter2D f)
            => CastShape(Shape2.Circle(Vector2.zero, Math.Max(radius, 1e-5f)), new Xf(origin, 0), direction, distance, f);
        internal static List<RaycastHit2D> BoxCastQ(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance, ContactFilter2D f)
            => CastShape(BoxShape(size), new Xf(origin, angle * Mathf.Deg2Rad), direction, distance, f);
        internal static List<RaycastHit2D> CapsuleCastQ(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, float distance, ContactFilter2D f)
            => CastShape(CapsuleShape(Vector2.zero, size, capsuleDirection, angle), new Xf(origin, 0), direction, distance, f);

        internal static List<RaycastHit2D> RayIntersectionQ(Ray ray, float distance, int layerMask)
        {
            Physics2DWorld.PrepareQuery();
            var res = new List<(float t, RaycastHit2D h)>();
            var dir = ray.direction.normalized;
            var f = LF(layerMask);
            var seen = new HashSet<Collider2D>();
            foreach (var fx in Physics2DWorld.AllFixtures)
            {
                var col = (Collider2D)fx.Owner;
                if (seen.Contains(col) || !Physics2DWorld.Passes(col, f)) continue;
                float z = col.transform.position.z;
                float t;
                if (Math.Abs(dir.z) < 1e-8f) { if (Math.Abs(ray.origin.z - z) > 1e-5f) continue; t = 0; }
                else t = (z - ray.origin.z) / dir.z;
                if (t < 0 || t > distance) continue;
                var p3 = ray.origin + dir * t; var p = new Vector2(p3.x, p3.y);
                if (!fx.Shape.TestPoint(fx.Body.Xf, p)) continue;
                seen.Add(col);
                res.Add((t, new RaycastHit2D { m_Point = p, m_Centroid = p, m_Normal = new Vector2(-dir.x, -dir.y), m_Distance = t, m_Fraction = float.IsInfinity(distance) ? 0 : t / distance, m_Collider = col.GetInstanceID(), m_ColliderRef = col }));
            }
            return res.OrderBy(r => r.t).Select(r => r.h).ToList();
        }

        internal static List<Collider2D> OverlapPointQ(Vector2 point, ContactFilter2D f) => Physics2DWorld.OverlapPoint(point, f);
        internal static List<Collider2D> OverlapCircleQ(Vector2 point, float radius, ContactFilter2D f)
            => Physics2DWorld.Overlap(new[] { (Shape2.Circle(Vector2.zero, Math.Max(radius, 1e-5f)), new Xf(point, 0)) }, f);
        internal static List<Collider2D> OverlapBoxQ(Vector2 point, Vector2 size, float angle, ContactFilter2D f)
            => Physics2DWorld.Overlap(new[] { (BoxShape(size), new Xf(point, angle * Mathf.Deg2Rad)) }, f);
        internal static List<Collider2D> OverlapAreaQ(Vector2 pointA, Vector2 pointB, ContactFilter2D f)
            => OverlapBoxQ((pointA + pointB) * 0.5f, new Vector2(Math.Abs(pointB.x - pointA.x), Math.Abs(pointB.y - pointA.y)), 0, f);
        internal static List<Collider2D> OverlapCapsuleQ(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle, ContactFilter2D f)
            => Physics2DWorld.Overlap(new[] { (CapsuleShape(Vector2.zero, size, direction, angle), new Xf(point, 0)) }, f);
        internal static List<Collider2D> OverlapColliderQ(Collider2D collider, ContactFilter2D f)
        {
            Physics2DWorld.PrepareQuery();
            var fs = Physics2DWorld.FixturesOf(collider).ToList();
            if (fs.Count == 0) return new List<Collider2D>();
            return Physics2DWorld.Overlap(fs.Select(x => (x.Shape, x.Body.Xf)).ToList(), f, null, collider);
        }

        // ------------------------------------------------------------------ contacts
        internal static bool PassContact(Collider2D other, List<Contact2> contacts, Collider2D self, ContactFilter2D f)
        {
            if (f.IsFilteringTrigger(other) || f.IsFilteringTrigger(self) && !f.useTriggers) return false;
            if (f.IsFilteringLayerMask(other.gameObject) || f.IsFilteringDepth(other.gameObject)) return false;
            if (!f.useNormalAngle) return true;
            foreach (var cp in Physics2DWorld.ContactPoints(self, other, contacts)) if (!f.IsFilteringNormalAngle(cp.normal)) return true;
            return false;
        }

        internal static List<ContactPoint2D> ContactPointsQ(Collider2D self, Collider2D only, ContactFilter2D f)
        {
            Physics2DWorld.PrepareQuery();
            var res = new List<ContactPoint2D>();
            foreach (var (other, l) in Physics2DWorld.ContactsOf(self, false))
            {
                if (only != null && other != only) continue;
                if (f.IsFilteringTrigger(other) || f.IsFilteringLayerMask(other.gameObject) || f.IsFilteringDepth(other.gameObject)) continue;
                foreach (var cp in Physics2DWorld.ContactPoints(self, other, l)) if (!f.IsFilteringNormalAngle(cp.normal)) res.Add(cp);
            }
            return res;
        }

        internal static List<Collider2D> ContactCollidersQ(Collider2D self, ContactFilter2D f)
        {
            Physics2DWorld.PrepareQuery();
            var res = new List<Collider2D>();
            foreach (var (other, l) in Physics2DWorld.ContactsOf(self)) if (PassContact(other, l, self, f)) res.Add(other);
            return res;
        }

        internal static List<ContactPoint2D> ContactPointsQ(Rigidbody2D rb, ContactFilter2D f)
        {
            var res = new List<ContactPoint2D>();
            foreach (var c in rb.AttachedColliders()) res.AddRange(ContactPointsQ(c, null, f));
            return res;
        }
        internal static List<Collider2D> ContactCollidersQ(Rigidbody2D rb, ContactFilter2D f)
        {
            var res = new List<Collider2D>();
            foreach (var c in rb.AttachedColliders()) foreach (var o in ContactCollidersQ(c, f)) if (!res.Contains(o)) res.Add(o);
            return res;
        }
        static ContactFilter2D NoF => new ContactFilter2D().NoFilter();
        public static int GetContacts(Collider2D collider1, Collider2D collider2, ContactFilter2D contactFilter, ContactPoint2D[] contacts) => ToBuf(ContactPointsQ(collider1, collider2, contactFilter), contacts);
        public static int GetContacts(Collider2D collider1, Collider2D collider2, ContactFilter2D contactFilter, List<ContactPoint2D> contacts) => ToList(ContactPointsQ(collider1, collider2, contactFilter), contacts);
        public static int GetContacts(Collider2D collider, ContactPoint2D[] contacts) => ToBuf(ContactPointsQ(collider, null, NoF), contacts);
        public static int GetContacts(Collider2D collider, List<ContactPoint2D> contacts) => ToList(ContactPointsQ(collider, null, NoF), contacts);
        public static int GetContacts(Collider2D collider, ContactFilter2D contactFilter, ContactPoint2D[] contacts) => ToBuf(ContactPointsQ(collider, null, contactFilter), contacts);
        public static int GetContacts(Collider2D collider, ContactFilter2D contactFilter, List<ContactPoint2D> contacts) => ToList(ContactPointsQ(collider, null, contactFilter), contacts);
        public static int GetContacts(Collider2D collider, Collider2D[] colliders) => ToBuf(ContactCollidersQ(collider, NoF), colliders);
        public static int GetContacts(Collider2D collider, List<Collider2D> colliders) => ToList(ContactCollidersQ(collider, NoF), colliders);
        public static int GetContacts(Collider2D collider, ContactFilter2D contactFilter, Collider2D[] colliders) => ToBuf(ContactCollidersQ(collider, contactFilter), colliders);
        public static int GetContacts(Collider2D collider, ContactFilter2D contactFilter, List<Collider2D> colliders) => ToList(ContactCollidersQ(collider, contactFilter), colliders);
        public static int GetContacts(Rigidbody2D rigidbody, ContactPoint2D[] contacts) => ToBuf(ContactPointsQ(rigidbody, NoF), contacts);
        public static int GetContacts(Rigidbody2D rigidbody, List<ContactPoint2D> contacts) => ToList(ContactPointsQ(rigidbody, NoF), contacts);
        public static int GetContacts(Rigidbody2D rigidbody, ContactFilter2D contactFilter, ContactPoint2D[] contacts) => ToBuf(ContactPointsQ(rigidbody, contactFilter), contacts);
        public static int GetContacts(Rigidbody2D rigidbody, ContactFilter2D contactFilter, List<ContactPoint2D> contacts) => ToList(ContactPointsQ(rigidbody, contactFilter), contacts);
        public static int GetContacts(Rigidbody2D rigidbody, Collider2D[] colliders) => ToBuf(ContactCollidersQ(rigidbody, NoF), colliders);
        public static int GetContacts(Rigidbody2D rigidbody, List<Collider2D> colliders) => ToList(ContactCollidersQ(rigidbody, NoF), colliders);
        public static int GetContacts(Rigidbody2D rigidbody, ContactFilter2D contactFilter, Collider2D[] colliders) => ToBuf(ContactCollidersQ(rigidbody, contactFilter), colliders);
        public static int GetContacts(Rigidbody2D rigidbody, ContactFilter2D contactFilter, List<Collider2D> colliders) => ToList(ContactCollidersQ(rigidbody, contactFilter), colliders);
    }

    public partial struct PhysicsScene2D
    {
        internal int m_Handle;
        public override string ToString() => $"({m_Handle})";
        public static bool operator ==(PhysicsScene2D lhs, PhysicsScene2D rhs) => lhs.m_Handle == rhs.m_Handle;
        public static bool operator !=(PhysicsScene2D lhs, PhysicsScene2D rhs) => lhs.m_Handle != rhs.m_Handle;
        public override int GetHashCode() => m_Handle;
        public override bool Equals(object other) => other is PhysicsScene2D s && s.m_Handle == m_Handle;
        public bool Equals(PhysicsScene2D other) => other.m_Handle == m_Handle;
        public bool IsValid() => m_Handle != 0;
        public bool IsEmpty() { Physics2DWorld.PrepareQuery(); return !Physics2DWorld.AllFixtures.Any(); }
        public bool Simulate(float step) => Physics2D.Simulate(step);
    }

    public static partial class PhysicsSceneExtensions2D
    {
        public static PhysicsScene2D GetPhysicsScene2D(this SceneManagement.Scene scene) => Physics2D.defaultPhysicsScene;
    }
}

namespace MiniUnity
{
    /// <summary>Delivers the 2D physics callbacks of the last step (OnTrigger*2D before OnCollision*2D, joint breaks).</summary>
    public static class PhysicsMessages2D
    {
        public static void Dispatch()
        {
            foreach (var (j, f, t) in Box2D.Physics2DWorld.PendingJointBreaks.ToList())
            {
                if (j == null || j.m_Destroyed) continue;
                var action = j.breakAction;
                if (action == UnityEngine.JointBreakAction2D.Ignore) continue;
                PhysicsMessages.Send(j.gameObject, "OnJointBreak2D", j);
                if (action == UnityEngine.JointBreakAction2D.Destroy) UnityEngine.Object.Destroy(j);
                else if (action == UnityEngine.JointBreakAction2D.Disable) j.enabled = false;
            }
            Box2D.Physics2DWorld.PendingJointBreaks.Clear();
            var evs = Box2D.Physics2DWorld.PendingEvents.ToList();
            Box2D.Physics2DWorld.PendingEvents.Clear();
            foreach (var (msg, self, other, col) in evs)
            {
                if (self == null || self.m_Destroyed) continue;
                object arg = msg.StartsWith("OnTrigger") ? other : col;
                var targets = new List<UnityEngine.GameObject> { self.gameObject };
                var rb = (self as UnityEngine.Collider2D)?.attachedRigidbody;
                if (rb != null && rb.gameObject != self.gameObject) targets.Add(rb.gameObject);
                foreach (var go in targets) PhysicsMessages.Send(go, msg, arg);
            }
        }
    }
}
