// UnityEngine.Physics and PhysicsScene: settings (DynamicsManager.asset), simulation control and scene queries.
// The overloads mirror the Unity 2022.3 API one to one (defaults: maxDistance = ∞, layerMask = DefaultRaycastLayers for casts and
// checks / AllLayers for Overlap*, QueryTriggerInteraction.UseGlobal); shapes that start overlapping are not reported by casts.
using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity.Physics3D;

namespace UnityEngine
{
    public partial class Physics
    {
        public Physics() { }
        public static Vector3 gravity { get { PhysicsWorld.EnsureSettings(); return PhysicsWorld.Gravity; } set { PhysicsWorld.EnsureSettings(); PhysicsWorld.Gravity = value; } }
        public static float defaultContactOffset { get { PhysicsWorld.EnsureSettings(); return PhysicsWorld.DefaultContactOffset; } set { PhysicsWorld.EnsureSettings(); PhysicsWorld.DefaultContactOffset = value; } }
        public static float sleepThreshold { get { PhysicsWorld.EnsureSettings(); return PhysicsWorld.SleepThreshold; } set { PhysicsWorld.EnsureSettings(); PhysicsWorld.SleepThreshold = value; } }
        public static bool queriesHitTriggers { get { PhysicsWorld.EnsureSettings(); return PhysicsWorld.QueriesHitTriggers; } set { PhysicsWorld.EnsureSettings(); PhysicsWorld.QueriesHitTriggers = value; } }
        public static bool queriesHitBackfaces { get { PhysicsWorld.EnsureSettings(); return PhysicsWorld.QueriesHitBackfaces; } set { PhysicsWorld.EnsureSettings(); PhysicsWorld.QueriesHitBackfaces = value; } }
        public static float bounceThreshold { get { PhysicsWorld.EnsureSettings(); return PhysicsWorld.BounceThreshold; } set { PhysicsWorld.EnsureSettings(); PhysicsWorld.BounceThreshold = value; } }
        public static float bounceTreshold { get => bounceThreshold; set => bounceThreshold = value; }
        public static float defaultMaxDepenetrationVelocity { get { PhysicsWorld.EnsureSettings(); return PhysicsWorld.DefaultMaxDepenetrationVelocity; } set { PhysicsWorld.EnsureSettings(); PhysicsWorld.DefaultMaxDepenetrationVelocity = value; } }
        public static int defaultSolverIterations { get { PhysicsWorld.EnsureSettings(); return PhysicsWorld.SolverIterations; } set { PhysicsWorld.EnsureSettings(); PhysicsWorld.SolverIterations = value; } }
        public static int defaultSolverVelocityIterations { get { PhysicsWorld.EnsureSettings(); return PhysicsWorld.SolverVelocityIterations; } set { PhysicsWorld.EnsureSettings(); PhysicsWorld.SolverVelocityIterations = value; } }
        public static int solverIterationCount { get => defaultSolverIterations; set => defaultSolverIterations = value; }
        public static int solverVelocityIterationCount { get => defaultSolverVelocityIterations; set => defaultSolverVelocityIterations = value; }
        public static SimulationMode simulationMode { get { PhysicsWorld.EnsureSettings(); return PhysicsWorld.Mode; } set { PhysicsWorld.EnsureSettings(); PhysicsWorld.Mode = value; } }
        public static bool autoSimulation { get => simulationMode != SimulationMode.Script; set => simulationMode = value ? SimulationMode.FixedUpdate : SimulationMode.Script; }
        public static float defaultMaxAngularSpeed { get { PhysicsWorld.EnsureSettings(); return PhysicsWorld.DefaultMaxAngularSpeed; } set { PhysicsWorld.EnsureSettings(); PhysicsWorld.DefaultMaxAngularSpeed = value; } }
        public static bool improvedPatchFriction { get => PhysicsWorld.ImprovedPatchFriction; set { PhysicsWorld.EnsureSettings(); PhysicsWorld.ImprovedPatchFriction = value; } }
        public static bool invokeCollisionCallbacks { get => PhysicsWorld.InvokeCollisionCallbacks; set { PhysicsWorld.EnsureSettings(); PhysicsWorld.InvokeCollisionCallbacks = value; } }
        public static bool autoSyncTransforms { get { PhysicsWorld.EnsureSettings(); return PhysicsWorld.AutoSyncTransforms; } set { PhysicsWorld.EnsureSettings(); PhysicsWorld.AutoSyncTransforms = value; } }
        public static bool reuseCollisionCallbacks { get { PhysicsWorld.EnsureSettings(); return PhysicsWorld.ReuseCollisionCallbacks; } set { PhysicsWorld.EnsureSettings(); PhysicsWorld.ReuseCollisionCallbacks = value; } }
        public static PhysicsScene defaultPhysicsScene => new PhysicsScene { m_Handle = 1 };
        public static float interCollisionDistance { get; set; }
        public static float interCollisionStiffness { get; set; }
        public static bool interCollisionSettingsToggle { get; set; }
        public static Vector3 clothGravity { get => gravity; set { } }

        public static void IgnoreCollision(Collider collider1, Collider collider2, bool ignore)
        {
            if (collider1 == null || collider2 == null) throw new NullReferenceException();
            var k = (Math.Min(collider1.GetInstanceID(), collider2.GetInstanceID()), Math.Max(collider1.GetInstanceID(), collider2.GetInstanceID()));
            if (ignore) PhysicsWorld.IgnoredPairs.Add(k); else PhysicsWorld.IgnoredPairs.Remove(k);
        }
        public static void IgnoreCollision(Collider collider1, Collider collider2) => IgnoreCollision(collider1, collider2, true);
        public static void IgnoreLayerCollision(int layer1, int layer2, bool ignore)
        {
            if (layer1 < 0 || layer1 > 31 || layer2 < 0 || layer2 > 31) throw new ArgumentException("layer numbers must be between 0 and 31");
            PhysicsWorld.EnsureSettings(); PhysicsWorld.SetLayerCollision(layer1, layer2, !ignore);
        }
        public static void IgnoreLayerCollision(int layer1, int layer2) => IgnoreLayerCollision(layer1, layer2, true);
        public static bool GetIgnoreLayerCollision(int layer1, int layer2) { PhysicsWorld.EnsureSettings(); return !PhysicsWorld.LayersCollide(layer1, layer2); }
        public static bool GetIgnoreCollision(Collider collider1, Collider collider2) =>
            PhysicsWorld.IgnoredPairs.Contains((Math.Min(collider1.GetInstanceID(), collider2.GetInstanceID()), Math.Max(collider1.GetInstanceID(), collider2.GetInstanceID())));

        // ------------------------------------------------------------------ simulation
        public static void Simulate(float step)
        {
            if (simulationMode != SimulationMode.Script)
            {
                Debug.LogWarning("Physics.Simulate(...) was called but simulation mode is not set to Script. You should set simulation mode to Script first before calling this function therefore the simulation was not run.");
                return;
            }
            Simulate_Internal(defaultPhysicsScene, step);
        }
        internal static void Simulate_Internal(PhysicsScene physicsScene, float step) { PhysicsWorld.Simulate(step); MiniUnity.PhysicsMessages.Dispatch(); }
        internal static void InterpolateBodies_Internal(PhysicsScene physicsScene) { }
        internal static void ResetInterpolationPoses_Internal(PhysicsScene physicsScene) { }
        public static void SyncTransforms() => PhysicsWorld.Sync();
        public static void RebuildBroadphaseRegions(Bounds worldBounds, int subdivisions) { }
        public static void BakeMesh(int meshID, bool convex, MeshColliderCookingOptions cookingOptions) { }
        public static void BakeMesh(int meshID, bool convex) { }
        internal static Collider GetColliderByInstanceID(int instanceID) => Object.FindObjectFromInstanceID(instanceID) as Collider;
        internal static Component GetBodyByInstanceID(int instanceID) => Object.FindObjectFromInstanceID(instanceID) as Component;

        public static bool ComputePenetration(Collider colliderA, Vector3 positionA, Quaternion rotationA, Collider colliderB, Vector3 positionB, Quaternion rotationB, out Vector3 direction, out float distance)
        {
            direction = Vector3.zero; distance = 0;
            var ga = Shape(colliderA, positionA, rotationA, out var pa); var gb = Shape(colliderB, positionB, rotationB, out var pb);
            var cs = new List<Contact>();
            Narrowphase.Collide(ga, pa, gb, pb, 0, cs);
            var deepest = cs.Where(c => c.Separation < 0).OrderBy(c => c.Separation).ToList();
            if (deepest.Count == 0) return false;
            direction = deepest[0].Normal; distance = -deepest[0].Separation;
            return true;
        }
        static Geom Shape(Collider c, Vector3 pos, Quaternion rot, out PxPose pose)
        {
            var lossy = c.transform.lossyScale;
            var g = c.BuildGeom(lossy, out var off, out var srot);
            pose = new PxPose(pos + rot * off, rot * srot);
            return g;
        }
        public static Vector3 ClosestPoint(Vector3 point, Collider collider, Vector3 position, Quaternion rotation)
        {
            var g = Shape(collider, position, rotation, out var pose);
            if (g.Kind == GeomKind.Mesh) return point;
            if (g.Kind is GeomKind.Box or GeomKind.Convex) return pose.Apply(Geo.ClosestOnPoly(g.Poly, pose.InvApply(point), out _));
            var core = Narrowphase.Core(g, pose);
            var c = core.Length == 1 ? core[0] : Geo.ClosestOnSegment(point, core[0], core[1], out _);
            var d = point - c; float l = d.magnitude;
            return l <= g.Radius ? point : c + d / l * g.Radius;
        }

        // ------------------------------------------------------------------ query cores
        internal static bool RaycastOne(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance, int layerMask, QueryTriggerInteraction q)
        {
            hit = default;
            float l = direction.magnitude; if (l <= float.Epsilon) return false;
            var hs = PhysicsWorld.Raycast(origin, direction / l, maxDistance, layerMask, q, false);
            if (hs.Count == 0) return false;
            hit = RaycastHit.From(hs[0]); return true;
        }
        internal static List<PhysicsWorld.Hit> RaycastList(Vector3 origin, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction q)
        {
            float l = direction.magnitude; if (l <= float.Epsilon) return new List<PhysicsWorld.Hit>();
            return PhysicsWorld.Raycast(origin, direction / l, maxDistance, layerMask, q, true);
        }
        internal static RaycastHit[] Hits(List<PhysicsWorld.Hit> hs) => hs.Select(RaycastHit.From).ToArray();
        internal static int ToBuffer(List<PhysicsWorld.Hit> hs, RaycastHit[] results)
        {
            int n = Math.Min(hs.Count, results.Length);
            for (int i = 0; i < n; i++) results[i] = RaycastHit.From(hs[i]);
            return n;
        }
        internal static int ToBuffer(List<Collider> cs, Collider[] results)
        {
            int n = Math.Min(cs.Count, results.Length);
            for (int i = 0; i < n; i++) results[i] = cs[i];
            return n;
        }
        internal static (Convex c, Bounds b) SphereQ(Vector3 p, float r) => (new PointsConvex(r, p), new Bounds(p, Vector3.one * 2 * r));
        internal static (Convex c, Bounds b) CapsuleQ(Vector3 a, Vector3 b, float r)
        {
            var bb = new Bounds(a, Vector3.zero); bb.Encapsulate(b); bb.Expand(2 * r);
            return (new PointsConvex(r, a, b), bb);
        }
        internal static (Convex c, Bounds b) BoxQ(Vector3 center, Vector3 half, Quaternion rot)
        {
            var g = Geom.Box(new Vector3(Math.Abs(half.x), Math.Abs(half.y), Math.Abs(half.z)));
            var pose = new PxPose(center, rot);
            return (new GeomConvex(g, pose), g.WorldBounds(pose));
        }
        internal static bool SweepOne((Convex c, Bounds b) q, Vector3 direction, out RaycastHit hit, float maxDistance, int layerMask, QueryTriggerInteraction qt)
        {
            hit = default;
            float l = direction.magnitude; if (l <= float.Epsilon) return false;
            var hs = PhysicsWorld.Sweep(q.c, q.b, direction / l, maxDistance, layerMask, qt, false);
            if (hs.Count == 0) return false;
            hit = RaycastHit.From(hs[0]); return true;
        }
        internal static List<PhysicsWorld.Hit> SweepList((Convex c, Bounds b) q, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction qt)
        {
            float l = direction.magnitude; if (l <= float.Epsilon) return new List<PhysicsWorld.Hit>();
            return PhysicsWorld.Sweep(q.c, q.b, direction / l, maxDistance, layerMask, qt, true);
        }
        internal static List<Collider> OverlapList((Convex c, Bounds b) q, int layerMask, QueryTriggerInteraction qt) =>
            PhysicsWorld.Overlap(q.c, q.b, layerMask, qt).Select(s => s.Col).ToList();

        // ------------------------------------------------------------------ overloads (Unity 2022.3 surface)
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => RaycastOne(origin, direction, out _, maxDistance, layerMask, queryTriggerInteraction);
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask) => RaycastOne(origin, direction, out _, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance) => RaycastOne(origin, direction, out _, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool Raycast(Vector3 origin, Vector3 direction) => RaycastOne(origin, direction, out _, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => RaycastOne(origin, direction, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask) => RaycastOne(origin, direction, out hitInfo, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance) => RaycastOne(origin, direction, out hitInfo, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo) => RaycastOne(origin, direction, out hitInfo, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool Raycast(Ray ray, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => RaycastOne(ray.origin, ray.direction, out _, maxDistance, layerMask, queryTriggerInteraction);
        public static bool Raycast(Ray ray, float maxDistance, int layerMask) => RaycastOne(ray.origin, ray.direction, out _, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);
        public static bool Raycast(Ray ray, float maxDistance) => RaycastOne(ray.origin, ray.direction, out _, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool Raycast(Ray ray) => RaycastOne(ray.origin, ray.direction, out _, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => RaycastOne(ray.origin, ray.direction, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);
        public static bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance, int layerMask) => RaycastOne(ray.origin, ray.direction, out hitInfo, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);
        public static bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance) => RaycastOne(ray.origin, ray.direction, out hitInfo, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool Raycast(Ray ray, out RaycastHit hitInfo) => RaycastOne(ray.origin, ray.direction, out hitInfo, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool Linecast(Vector3 start, Vector3 end, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => RaycastOne(start, end - start, out _, (end - start).magnitude, layerMask, queryTriggerInteraction);
        public static bool Linecast(Vector3 start, Vector3 end, int layerMask) => RaycastOne(start, end - start, out _, (end - start).magnitude, layerMask, QueryTriggerInteraction.UseGlobal);
        public static bool Linecast(Vector3 start, Vector3 end) => RaycastOne(start, end - start, out _, (end - start).magnitude, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool Linecast(Vector3 start, Vector3 end, out RaycastHit hitInfo, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => RaycastOne(start, end - start, out hitInfo, (end - start).magnitude, layerMask, queryTriggerInteraction);
        public static bool Linecast(Vector3 start, Vector3 end, out RaycastHit hitInfo, int layerMask) => RaycastOne(start, end - start, out hitInfo, (end - start).magnitude, layerMask, QueryTriggerInteraction.UseGlobal);
        public static bool Linecast(Vector3 start, Vector3 end, out RaycastHit hitInfo) => RaycastOne(start, end - start, out hitInfo, (end - start).magnitude, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => SweepOne(CapsuleQ(point1, point2, radius), direction, out _, maxDistance, layerMask, queryTriggerInteraction);
        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, float maxDistance, int layerMask) => SweepOne(CapsuleQ(point1, point2, radius), direction, out _, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);
        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, float maxDistance) => SweepOne(CapsuleQ(point1, point2, radius), direction, out _, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction) => SweepOne(CapsuleQ(point1, point2, radius), direction, out _, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => SweepOne(CapsuleQ(point1, point2, radius), direction, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);
        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask) => SweepOne(CapsuleQ(point1, point2, radius), direction, out hitInfo, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);
        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance) => SweepOne(CapsuleQ(point1, point2, radius), direction, out hitInfo, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, out RaycastHit hitInfo) => SweepOne(CapsuleQ(point1, point2, radius), direction, out hitInfo, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => SweepOne(SphereQ(origin, radius), direction, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);
        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask) => SweepOne(SphereQ(origin, radius), direction, out hitInfo, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);
        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance) => SweepOne(SphereQ(origin, radius), direction, out hitInfo, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hitInfo) => SweepOne(SphereQ(origin, radius), direction, out hitInfo, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool SphereCast(Ray ray, float radius, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => SweepOne(SphereQ(ray.origin, radius), ray.direction, out _, maxDistance, layerMask, queryTriggerInteraction);
        public static bool SphereCast(Ray ray, float radius, float maxDistance, int layerMask) => SweepOne(SphereQ(ray.origin, radius), ray.direction, out _, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);
        public static bool SphereCast(Ray ray, float radius, float maxDistance) => SweepOne(SphereQ(ray.origin, radius), ray.direction, out _, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool SphereCast(Ray ray, float radius) => SweepOne(SphereQ(ray.origin, radius), ray.direction, out _, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool SphereCast(Ray ray, float radius, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => SweepOne(SphereQ(ray.origin, radius), ray.direction, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);
        public static bool SphereCast(Ray ray, float radius, out RaycastHit hitInfo, float maxDistance, int layerMask) => SweepOne(SphereQ(ray.origin, radius), ray.direction, out hitInfo, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);
        public static bool SphereCast(Ray ray, float radius, out RaycastHit hitInfo, float maxDistance) => SweepOne(SphereQ(ray.origin, radius), ray.direction, out hitInfo, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool SphereCast(Ray ray, float radius, out RaycastHit hitInfo) => SweepOne(SphereQ(ray.origin, radius), ray.direction, out hitInfo, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => SweepOne(BoxQ(center, halfExtents, orientation), direction, out _, maxDistance, layerMask, queryTriggerInteraction);
        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation, float maxDistance, int layerMask) => SweepOne(BoxQ(center, halfExtents, orientation), direction, out _, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);
        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation, float maxDistance) => SweepOne(BoxQ(center, halfExtents, orientation), direction, out _, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation) => SweepOne(BoxQ(center, halfExtents, orientation), direction, out _, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction) => SweepOne(BoxQ(center, halfExtents, Quaternion.identity), direction, out _, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hitInfo, Quaternion orientation, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => SweepOne(BoxQ(center, halfExtents, orientation), direction, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);
        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hitInfo, Quaternion orientation, float maxDistance, int layerMask) => SweepOne(BoxQ(center, halfExtents, orientation), direction, out hitInfo, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);
        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hitInfo, Quaternion orientation, float maxDistance) => SweepOne(BoxQ(center, halfExtents, orientation), direction, out hitInfo, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hitInfo, Quaternion orientation) => SweepOne(BoxQ(center, halfExtents, orientation), direction, out hitInfo, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hitInfo) => SweepOne(BoxQ(center, halfExtents, Quaternion.identity), direction, out hitInfo, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
        public static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => Hits(RaycastList(origin, direction, maxDistance, layerMask, queryTriggerInteraction));
        public static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction, float maxDistance, int layerMask) => Hits(RaycastList(origin, direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction, float maxDistance) => Hits(RaycastList(origin, direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction) => Hits(RaycastList(origin, direction, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] RaycastAll(Ray ray, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => Hits(RaycastList(ray.origin, ray.direction, maxDistance, layerMask, queryTriggerInteraction));
        public static RaycastHit[] RaycastAll(Ray ray, float maxDistance, int layerMask) => Hits(RaycastList(ray.origin, ray.direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] RaycastAll(Ray ray, float maxDistance) => Hits(RaycastList(ray.origin, ray.direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] RaycastAll(Ray ray) => Hits(RaycastList(ray.origin, ray.direction, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
        public static int RaycastNonAlloc(Ray ray, RaycastHit[] results, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => ToBuffer(RaycastList(ray.origin, ray.direction, maxDistance, layerMask, queryTriggerInteraction), results);
        public static int RaycastNonAlloc(Ray ray, RaycastHit[] results, float maxDistance, int layerMask) => ToBuffer(RaycastList(ray.origin, ray.direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal), results);
        public static int RaycastNonAlloc(Ray ray, RaycastHit[] results, float maxDistance) => ToBuffer(RaycastList(ray.origin, ray.direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal), results);
        public static int RaycastNonAlloc(Ray ray, RaycastHit[] results) => ToBuffer(RaycastList(ray.origin, ray.direction, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal), results);
        public static int RaycastNonAlloc(Vector3 origin, Vector3 direction, RaycastHit[] results, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => ToBuffer(RaycastList(origin, direction, maxDistance, layerMask, queryTriggerInteraction), results);
        public static int RaycastNonAlloc(Vector3 origin, Vector3 direction, RaycastHit[] results, float maxDistance, int layerMask) => ToBuffer(RaycastList(origin, direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal), results);
        public static int RaycastNonAlloc(Vector3 origin, Vector3 direction, RaycastHit[] results, float maxDistance) => ToBuffer(RaycastList(origin, direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal), results);
        public static int RaycastNonAlloc(Vector3 origin, Vector3 direction, RaycastHit[] results) => ToBuffer(RaycastList(origin, direction, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal), results);
        public static RaycastHit[] CapsuleCastAll(Vector3 point1, Vector3 point2, float radius, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => Hits(SweepList(CapsuleQ(point1, point2, radius), direction, maxDistance, layerMask, queryTriggerInteraction));
        public static RaycastHit[] CapsuleCastAll(Vector3 point1, Vector3 point2, float radius, Vector3 direction, float maxDistance, int layerMask) => Hits(SweepList(CapsuleQ(point1, point2, radius), direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] CapsuleCastAll(Vector3 point1, Vector3 point2, float radius, Vector3 direction, float maxDistance) => Hits(SweepList(CapsuleQ(point1, point2, radius), direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] CapsuleCastAll(Vector3 point1, Vector3 point2, float radius, Vector3 direction) => Hits(SweepList(CapsuleQ(point1, point2, radius), direction, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] SphereCastAll(Vector3 origin, float radius, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => Hits(SweepList(SphereQ(origin, radius), direction, maxDistance, layerMask, queryTriggerInteraction));
        public static RaycastHit[] SphereCastAll(Vector3 origin, float radius, Vector3 direction, float maxDistance, int layerMask) => Hits(SweepList(SphereQ(origin, radius), direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] SphereCastAll(Vector3 origin, float radius, Vector3 direction, float maxDistance) => Hits(SweepList(SphereQ(origin, radius), direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] SphereCastAll(Vector3 origin, float radius, Vector3 direction) => Hits(SweepList(SphereQ(origin, radius), direction, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] SphereCastAll(Ray ray, float radius, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => Hits(SweepList(SphereQ(ray.origin, radius), ray.direction, maxDistance, layerMask, queryTriggerInteraction));
        public static RaycastHit[] SphereCastAll(Ray ray, float radius, float maxDistance, int layerMask) => Hits(SweepList(SphereQ(ray.origin, radius), ray.direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] SphereCastAll(Ray ray, float radius, float maxDistance) => Hits(SweepList(SphereQ(ray.origin, radius), ray.direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] SphereCastAll(Ray ray, float radius) => Hits(SweepList(SphereQ(ray.origin, radius), ray.direction, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
        public static Collider[] OverlapCapsule(Vector3 point0, Vector3 point1, float radius, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => OverlapList(CapsuleQ(point0, point1, radius), layerMask, queryTriggerInteraction).ToArray();
        public static Collider[] OverlapCapsule(Vector3 point0, Vector3 point1, float radius, int layerMask) => OverlapList(CapsuleQ(point0, point1, radius), layerMask, QueryTriggerInteraction.UseGlobal).ToArray();
        public static Collider[] OverlapCapsule(Vector3 point0, Vector3 point1, float radius) => OverlapList(CapsuleQ(point0, point1, radius), AllLayers, QueryTriggerInteraction.UseGlobal).ToArray();
        public static Collider[] OverlapSphere(Vector3 position, float radius, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => OverlapList(SphereQ(position, radius), layerMask, queryTriggerInteraction).ToArray();
        public static Collider[] OverlapSphere(Vector3 position, float radius, int layerMask) => OverlapList(SphereQ(position, radius), layerMask, QueryTriggerInteraction.UseGlobal).ToArray();
        public static Collider[] OverlapSphere(Vector3 position, float radius) => OverlapList(SphereQ(position, radius), AllLayers, QueryTriggerInteraction.UseGlobal).ToArray();
        public static int OverlapSphereNonAlloc(Vector3 position, float radius, Collider[] results, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => ToBuffer(OverlapList(SphereQ(position, radius), layerMask, queryTriggerInteraction), results);
        public static int OverlapSphereNonAlloc(Vector3 position, float radius, Collider[] results, int layerMask) => ToBuffer(OverlapList(SphereQ(position, radius), layerMask, QueryTriggerInteraction.UseGlobal), results);
        public static int OverlapSphereNonAlloc(Vector3 position, float radius, Collider[] results) => ToBuffer(OverlapList(SphereQ(position, radius), AllLayers, QueryTriggerInteraction.UseGlobal), results);
        public static bool CheckSphere(Vector3 position, float radius, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => OverlapList(SphereQ(position, radius), layerMask, queryTriggerInteraction).Count > 0;
        public static bool CheckSphere(Vector3 position, float radius, int layerMask) => OverlapList(SphereQ(position, radius), layerMask, QueryTriggerInteraction.UseGlobal).Count > 0;
        public static bool CheckSphere(Vector3 position, float radius) => OverlapList(SphereQ(position, radius), DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal).Count > 0;
        public static int CapsuleCastNonAlloc(Vector3 point1, Vector3 point2, float radius, Vector3 direction, RaycastHit[] results, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => ToBuffer(SweepList(CapsuleQ(point1, point2, radius), direction, maxDistance, layerMask, queryTriggerInteraction), results);
        public static int CapsuleCastNonAlloc(Vector3 point1, Vector3 point2, float radius, Vector3 direction, RaycastHit[] results, float maxDistance, int layerMask) => ToBuffer(SweepList(CapsuleQ(point1, point2, radius), direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal), results);
        public static int CapsuleCastNonAlloc(Vector3 point1, Vector3 point2, float radius, Vector3 direction, RaycastHit[] results, float maxDistance) => ToBuffer(SweepList(CapsuleQ(point1, point2, radius), direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal), results);
        public static int CapsuleCastNonAlloc(Vector3 point1, Vector3 point2, float radius, Vector3 direction, RaycastHit[] results) => ToBuffer(SweepList(CapsuleQ(point1, point2, radius), direction, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal), results);
        public static int SphereCastNonAlloc(Vector3 origin, float radius, Vector3 direction, RaycastHit[] results, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => ToBuffer(SweepList(SphereQ(origin, radius), direction, maxDistance, layerMask, queryTriggerInteraction), results);
        public static int SphereCastNonAlloc(Vector3 origin, float radius, Vector3 direction, RaycastHit[] results, float maxDistance, int layerMask) => ToBuffer(SweepList(SphereQ(origin, radius), direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal), results);
        public static int SphereCastNonAlloc(Vector3 origin, float radius, Vector3 direction, RaycastHit[] results, float maxDistance) => ToBuffer(SweepList(SphereQ(origin, radius), direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal), results);
        public static int SphereCastNonAlloc(Vector3 origin, float radius, Vector3 direction, RaycastHit[] results) => ToBuffer(SweepList(SphereQ(origin, radius), direction, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal), results);
        public static int SphereCastNonAlloc(Ray ray, float radius, RaycastHit[] results, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => ToBuffer(SweepList(SphereQ(ray.origin, radius), ray.direction, maxDistance, layerMask, queryTriggerInteraction), results);
        public static int SphereCastNonAlloc(Ray ray, float radius, RaycastHit[] results, float maxDistance, int layerMask) => ToBuffer(SweepList(SphereQ(ray.origin, radius), ray.direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal), results);
        public static int SphereCastNonAlloc(Ray ray, float radius, RaycastHit[] results, float maxDistance) => ToBuffer(SweepList(SphereQ(ray.origin, radius), ray.direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal), results);
        public static int SphereCastNonAlloc(Ray ray, float radius, RaycastHit[] results) => ToBuffer(SweepList(SphereQ(ray.origin, radius), ray.direction, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal), results);
        public static bool CheckCapsule(Vector3 start, Vector3 end, float radius, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => OverlapList(CapsuleQ(start, end, radius), layerMask, queryTriggerInteraction).Count > 0;
        public static bool CheckCapsule(Vector3 start, Vector3 end, float radius, int layerMask) => OverlapList(CapsuleQ(start, end, radius), layerMask, QueryTriggerInteraction.UseGlobal).Count > 0;
        public static bool CheckCapsule(Vector3 start, Vector3 end, float radius) => OverlapList(CapsuleQ(start, end, radius), DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal).Count > 0;
        public static bool CheckBox(Vector3 center, Vector3 halfExtents, Quaternion orientation, int layermask, QueryTriggerInteraction queryTriggerInteraction) => OverlapList(BoxQ(center, halfExtents, orientation), layermask, queryTriggerInteraction).Count > 0;
        public static bool CheckBox(Vector3 center, Vector3 halfExtents, Quaternion orientation, int layerMask) => OverlapList(BoxQ(center, halfExtents, orientation), layerMask, QueryTriggerInteraction.UseGlobal).Count > 0;
        public static bool CheckBox(Vector3 center, Vector3 halfExtents, Quaternion orientation) => OverlapList(BoxQ(center, halfExtents, orientation), DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal).Count > 0;
        public static bool CheckBox(Vector3 center, Vector3 halfExtents) => OverlapList(BoxQ(center, halfExtents, Quaternion.identity), DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal).Count > 0;
        public static Collider[] OverlapBox(Vector3 center, Vector3 halfExtents, Quaternion orientation, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => OverlapList(BoxQ(center, halfExtents, orientation), layerMask, queryTriggerInteraction).ToArray();
        public static Collider[] OverlapBox(Vector3 center, Vector3 halfExtents, Quaternion orientation, int layerMask) => OverlapList(BoxQ(center, halfExtents, orientation), layerMask, QueryTriggerInteraction.UseGlobal).ToArray();
        public static Collider[] OverlapBox(Vector3 center, Vector3 halfExtents, Quaternion orientation) => OverlapList(BoxQ(center, halfExtents, orientation), AllLayers, QueryTriggerInteraction.UseGlobal).ToArray();
        public static Collider[] OverlapBox(Vector3 center, Vector3 halfExtents) => OverlapList(BoxQ(center, halfExtents, Quaternion.identity), AllLayers, QueryTriggerInteraction.UseGlobal).ToArray();
        public static int OverlapBoxNonAlloc(Vector3 center, Vector3 halfExtents, Collider[] results, Quaternion orientation, int mask, QueryTriggerInteraction queryTriggerInteraction) => ToBuffer(OverlapList(BoxQ(center, halfExtents, orientation), mask, queryTriggerInteraction), results);
        public static int OverlapBoxNonAlloc(Vector3 center, Vector3 halfExtents, Collider[] results, Quaternion orientation, int mask) => ToBuffer(OverlapList(BoxQ(center, halfExtents, orientation), mask, QueryTriggerInteraction.UseGlobal), results);
        public static int OverlapBoxNonAlloc(Vector3 center, Vector3 halfExtents, Collider[] results, Quaternion orientation) => ToBuffer(OverlapList(BoxQ(center, halfExtents, orientation), AllLayers, QueryTriggerInteraction.UseGlobal), results);
        public static int OverlapBoxNonAlloc(Vector3 center, Vector3 halfExtents, Collider[] results) => ToBuffer(OverlapList(BoxQ(center, halfExtents, Quaternion.identity), AllLayers, QueryTriggerInteraction.UseGlobal), results);
        public static int BoxCastNonAlloc(Vector3 center, Vector3 halfExtents, Vector3 direction, RaycastHit[] results, Quaternion orientation, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => ToBuffer(SweepList(BoxQ(center, halfExtents, orientation), direction, maxDistance, layerMask, queryTriggerInteraction), results);
        public static int BoxCastNonAlloc(Vector3 center, Vector3 halfExtents, Vector3 direction, RaycastHit[] results, Quaternion orientation) => ToBuffer(SweepList(BoxQ(center, halfExtents, orientation), direction, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal), results);
        public static int BoxCastNonAlloc(Vector3 center, Vector3 halfExtents, Vector3 direction, RaycastHit[] results, Quaternion orientation, float maxDistance) => ToBuffer(SweepList(BoxQ(center, halfExtents, orientation), direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal), results);
        public static int BoxCastNonAlloc(Vector3 center, Vector3 halfExtents, Vector3 direction, RaycastHit[] results, Quaternion orientation, float maxDistance, int layerMask) => ToBuffer(SweepList(BoxQ(center, halfExtents, orientation), direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal), results);
        public static int BoxCastNonAlloc(Vector3 center, Vector3 halfExtents, Vector3 direction, RaycastHit[] results) => ToBuffer(SweepList(BoxQ(center, halfExtents, Quaternion.identity), direction, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal), results);
        public static RaycastHit[] BoxCastAll(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => Hits(SweepList(BoxQ(center, halfExtents, orientation), direction, maxDistance, layerMask, queryTriggerInteraction));
        public static RaycastHit[] BoxCastAll(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation, float maxDistance, int layerMask) => Hits(SweepList(BoxQ(center, halfExtents, orientation), direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] BoxCastAll(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation, float maxDistance) => Hits(SweepList(BoxQ(center, halfExtents, orientation), direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] BoxCastAll(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation) => Hits(SweepList(BoxQ(center, halfExtents, orientation), direction, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
        public static RaycastHit[] BoxCastAll(Vector3 center, Vector3 halfExtents, Vector3 direction) => Hits(SweepList(BoxQ(center, halfExtents, Quaternion.identity), direction, float.PositiveInfinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal));
        public static int OverlapCapsuleNonAlloc(Vector3 point0, Vector3 point1, float radius, Collider[] results, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => ToBuffer(OverlapList(CapsuleQ(point0, point1, radius), layerMask, queryTriggerInteraction), results);
        public static int OverlapCapsuleNonAlloc(Vector3 point0, Vector3 point1, float radius, Collider[] results, int layerMask) => ToBuffer(OverlapList(CapsuleQ(point0, point1, radius), layerMask, QueryTriggerInteraction.UseGlobal), results);
        public static int OverlapCapsuleNonAlloc(Vector3 point0, Vector3 point1, float radius, Collider[] results) => ToBuffer(OverlapList(CapsuleQ(point0, point1, radius), AllLayers, QueryTriggerInteraction.UseGlobal), results);
    }

    public partial struct PhysicsScene
    {
        internal int m_Handle;
        public override string ToString() => $"({m_Handle})";
        public static bool operator ==(PhysicsScene lhs, PhysicsScene rhs) => lhs.m_Handle == rhs.m_Handle;
        public static bool operator !=(PhysicsScene lhs, PhysicsScene rhs) => lhs.m_Handle != rhs.m_Handle;
        public override int GetHashCode() => m_Handle;
        public override bool Equals(object other) => other is PhysicsScene s && s.m_Handle == m_Handle;
        public bool Equals(PhysicsScene other) => m_Handle == other.m_Handle;
        public bool IsValid() => m_Handle == 1;
        public bool IsEmpty() => !PhysicsWorld.Shapes.Any();
        public void Simulate(float step)
        {
            if (this == Physics.defaultPhysicsScene && Physics.simulationMode != SimulationMode.Script)
            {
                Debug.LogWarning("PhysicsScene.Simulate(...) was called but simulation mode is not set to Script. You should set simulation mode to Script first before calling this function therefore the simulation was not run.");
                return;
            }
            Physics.Simulate_Internal(this, step);
        }
        public void InterpolateBodies() { }
        public void ResetInterpolationPoses() { }
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) => Physics.RaycastOne(origin, direction, out _, maxDistance, layerMask, queryTriggerInteraction);
        public bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) => Physics.RaycastOne(origin, direction, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);
        public int Raycast(Vector3 origin, Vector3 direction, RaycastHit[] raycastHits, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) => Physics.ToBuffer(Physics.RaycastList(origin, direction, maxDistance, layerMask, queryTriggerInteraction), raycastHits);
        public bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) => Physics.SweepOne(Physics.CapsuleQ(point1, point2, radius), direction, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);
        public int CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, RaycastHit[] results, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) => Physics.ToBuffer(Physics.SweepList(Physics.CapsuleQ(point1, point2, radius), direction, maxDistance, layerMask, queryTriggerInteraction), results);
        public int OverlapCapsule(Vector3 point0, Vector3 point1, float radius, Collider[] results, int layerMask = -1, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) => Physics.ToBuffer(Physics.OverlapList(Physics.CapsuleQ(point0, point1, radius), layerMask, queryTriggerInteraction), results);
        public bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) => Physics.SweepOne(Physics.SphereQ(origin, radius), direction, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);
        public int SphereCast(Vector3 origin, float radius, Vector3 direction, RaycastHit[] results, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) => Physics.ToBuffer(Physics.SweepList(Physics.SphereQ(origin, radius), direction, maxDistance, layerMask, queryTriggerInteraction), results);
        public int OverlapSphere(Vector3 position, float radius, Collider[] results, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => Physics.ToBuffer(Physics.OverlapList(Physics.SphereQ(position, radius), layerMask, queryTriggerInteraction), results);
        public bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hitInfo, Quaternion orientation, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) => Physics.SweepOne(Physics.BoxQ(center, halfExtents, orientation), direction, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);
        public bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hitInfo) => BoxCast(center, halfExtents, direction, out hitInfo, Quaternion.identity);
        public int OverlapBox(Vector3 center, Vector3 halfExtents, Collider[] results, Quaternion orientation, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) => Physics.ToBuffer(Physics.OverlapList(Physics.BoxQ(center, halfExtents, orientation), layerMask, queryTriggerInteraction), results);
        public int OverlapBox(Vector3 center, Vector3 halfExtents, Collider[] results) => OverlapBox(center, halfExtents, results, Quaternion.identity);
        public int BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, RaycastHit[] results, Quaternion orientation, float maxDistance = float.PositiveInfinity, int layerMask = -5, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) => Physics.ToBuffer(Physics.SweepList(Physics.BoxQ(center, halfExtents, orientation), direction, maxDistance, layerMask, queryTriggerInteraction), results);
        public int BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, RaycastHit[] results) => BoxCast(center, halfExtents, direction, results, Quaternion.identity);
    }

    public static partial class PhysicsSceneExtensions
    {
        public static PhysicsScene GetPhysicsScene(this SceneManagement.Scene scene) => Physics.defaultPhysicsScene;
    }
}
