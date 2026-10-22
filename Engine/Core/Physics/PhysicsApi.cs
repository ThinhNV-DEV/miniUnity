// UnityEngine physics API (PhysicsModule) over MiniUnity.Physics3D.PhysicsWorld. Public surface and defaults follow the Unity
// 2022.3 bindings (UnityCsReference Modules/Physics); behaviour of the native side follows PhysX 4.1 (see World.cs).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MiniUnity;
using MiniUnity.Physics3D;
using MiniUnity.Yaml;

namespace UnityEngine
{
    public partial class PhysicMaterial
    {
        public PhysicMaterial() { m_Name = "DynamicMaterial"; }
        public PhysicMaterial(string name) { m_Name = name; }
        // files saved by Unity 6 (PhysicsMaterial) prefix the fields: m_DynamicFriction, m_StaticFriction, m_Bounciness, m_*Combine
        string K(string name) => NGet(name) == null && NGet("m_" + char.ToUpperInvariant(name[0]) + name.Substring(1)) != null ? "m_" + char.ToUpperInvariant(name[0]) + name.Substring(1) : name;
        public float bounciness { get => NFloat(K("bounciness"), 0); set => NSetF(K("bounciness"), value); }
        public float dynamicFriction { get => NFloat(K("dynamicFriction"), 0.6f); set => NSetF(K("dynamicFriction"), value); }
        public float staticFriction { get => NFloat(K("staticFriction"), 0.6f); set => NSetF(K("staticFriction"), value); }
        public PhysicMaterialCombine frictionCombine { get => (PhysicMaterialCombine)NInt(K("frictionCombine")); set => NSetI(K("frictionCombine"), (int)value); }
        public PhysicMaterialCombine bounceCombine { get => (PhysicMaterialCombine)NInt(K("bounceCombine")); set => NSetI(K("bounceCombine"), (int)value); }
        public float bouncyness { get => bounciness; set => bounciness = value; }
    }

    public partial struct RaycastHit
    {
        internal Collider m_ColliderRef;
        public Collider collider => m_ColliderRef ?? Object.FindObjectFromInstanceID(m_Collider) as Collider;
        public int colliderInstanceID => m_Collider;
        public Vector3 point { get => m_Point; set => m_Point = value; }
        public Vector3 normal { get => m_Normal; set => m_Normal = value; }
        public Vector3 barycentricCoordinate { get => new(1 - (m_UV.y + m_UV.x), m_UV.x, m_UV.y); set => m_UV = value; }
        public float distance { get => m_Distance; set => m_Distance = value; }
        public int triangleIndex => (int)m_FaceID;
        public Vector2 textureCoord => TexCoord(0);
        public Vector2 textureCoord2 => TexCoord(1);
        public Vector2 textureCoord1 => TexCoord(1);
        public Vector2 lightmapCoord => TexCoord(1);
        public Transform transform { get { var rb = rigidbody; return rb != null ? rb.transform : collider != null ? collider.transform : null; } }
        public Rigidbody rigidbody => collider != null ? collider.attachedRigidbody : null;
        public ArticulationBody articulationBody => null;
        Vector2 TexCoord(int channel)
        {
            if (collider is not MeshCollider mc || mc.sharedMesh == null || m_FaceID == uint.MaxValue) return Vector2.zero;
            var mesh = mc.sharedMesh; var uvs = new List<Vector2>(); mesh.GetUVs(channel, uvs);
            var tris = mesh.triangles; int f = (int)m_FaceID;
            if (uvs.Count == 0 || 3 * f + 2 >= tris.Length) return Vector2.zero;
            var b = barycentricCoordinate;
            return uvs[tris[3 * f]] * b.x + uvs[tris[3 * f + 1]] * b.y + uvs[tris[3 * f + 2]] * b.z;
        }

        internal static RaycastHit From(PhysicsWorld.Hit h) => new()
        {
            m_Point = h.Point, m_Normal = h.Normal, m_Distance = h.Distance, m_Collider = h.Shape.Col.GetInstanceID(), m_ColliderRef = h.Shape.Col,
            m_FaceID = h.Face >= 0 ? (uint)h.Face : 0, m_UV = h.UV,
        };
    }

    public partial struct ContactPoint
    {
        internal Collider m_This, m_Other;
        public Vector3 point => m_Point;
        public Vector3 normal => m_Normal;
        public Vector3 impulse => m_Impulse;
        public Collider thisCollider => m_This ?? Physics.GetColliderByInstanceID(m_ThisColliderInstanceID);
        public Collider otherCollider => m_Other ?? Physics.GetColliderByInstanceID(m_OtherColliderInstanceID);
        public float separation => m_Separation;
    }

    /// <summary>Describes a collision (argument of OnCollisionEnter / Stay / Exit), seen from the receiving collider.</summary>
    public class Collision
    {
        internal Collider m_Self, m_Other; internal ContactPoint[] m_Contacts = Array.Empty<ContactPoint>();
        internal Vector3 m_Impulse, m_RelativeVelocity;
        public Collision() { }
        internal Collision(PhysicsWorld.CollisionEvent e)
        {
            m_Self = e.Self; m_Other = e.Other;
            var p = e.Pair;
            if (p == null) return;
            float sign = e.SelfIsA ? 1 : -1;
            m_Contacts = new ContactPoint[p.Contacts.Count];
            for (int i = 0; i < p.Contacts.Count; i++)
            {
                var c = p.Contacts[i];
                float imp = i < p.NormalImpulse.Length ? p.NormalImpulse[i] : 0;
                m_Contacts[i] = new ContactPoint
                {
                    m_Point = c.Point, m_Normal = c.Normal * sign, m_Impulse = c.Normal * imp, m_Separation = c.Separation,
                    m_ThisColliderInstanceID = e.Self.GetInstanceID(), m_OtherColliderInstanceID = e.Other.GetInstanceID(), m_This = e.Self, m_Other = e.Other,
                };
            }
            m_Impulse = p.Impulse;
            // velocity of the other body relative to this one
            m_RelativeVelocity = e.SelfIsA ? p.RelVel : -p.RelVel;
        }
        public Vector3 impulse => m_Impulse;
        public Vector3 relativeVelocity => m_RelativeVelocity;
        public Rigidbody rigidbody => m_Other != null ? m_Other.attachedRigidbody : null;
        public ArticulationBody articulationBody => null;
        public Component body => rigidbody;
        public Collider collider => m_Other;
        public Transform transform => rigidbody != null ? rigidbody.transform : collider.transform;
        public GameObject gameObject => body != null ? body.gameObject : collider.gameObject;
        public int contactCount => m_Contacts.Length;
        public ContactPoint[] contacts => m_Contacts;
        public Vector3 impactForceSum => m_Impulse;
        public Component other => (Component)rigidbody ?? collider;
        internal bool Flipped { get; set; }
        public ContactPoint GetContact(int index)
        {
            if (index < 0 || index >= contactCount) throw new ArgumentOutOfRangeException($"Cannot get contact at index {index}. There are {contactCount} contact(s).");
            return m_Contacts[index];
        }
        public int GetContacts(ContactPoint[] contacts)
        {
            if (contacts == null) throw new NullReferenceException("Cannot get contacts as the provided array is NULL.");
            int n = Math.Min(m_Contacts.Length, contacts.Length); Array.Copy(m_Contacts, contacts, n); return n;
        }
        public int GetContacts(List<ContactPoint> contacts)
        {
            if (contacts == null) throw new NullReferenceException("Cannot get contacts as the provided list is NULL.");
            contacts.Clear(); contacts.AddRange(m_Contacts); return m_Contacts.Length;
        }
        public virtual IEnumerator GetEnumerator() => m_Contacts.GetEnumerator();
    }

    public partial class ControllerColliderHit
    {
        public CharacterController controller => m_Controller;
        public Collider collider => m_Collider;
        public Rigidbody rigidbody => m_Collider.attachedRigidbody;
        public GameObject gameObject => m_Collider.gameObject;
        public Transform transform => m_Collider.transform;
        public Vector3 point => m_Point;
        public Vector3 normal => m_Normal;
        public Vector3 moveDirection => m_MoveDirection;
        public float moveLength => m_MoveLength;
    }

    // ====================================================================================================== Rigidbody
    public partial class Rigidbody
    {
        // cached serialized state (read once, updated by the setters) – the solver reads these every step
        internal bool m_IsKinematicCache, m_UseGravityCache = true, m_DetectCollisions = true, m_AutoCom = true, m_AutoTensor = true, m_Cached;
        internal float m_MassCache = 1, m_DragCache, m_AngularDragCache = 0.05f, m_MaxDepenetration = -1, m_MaxAngularVelocity = -1, m_MaxLinearVelocity = 1e16f, m_SleepThreshold = -1;
        internal int m_SolverIterations = -1, m_SolverVelocityIterations = -1;
        internal RigidbodyConstraints m_ConstraintsCache; internal RigidbodyInterpolation m_InterpolationCache; internal CollisionDetectionMode m_CollisionModeCache;
        internal Vector3 m_ComCache, m_TensorCache = Vector3.one; internal Quaternion m_TensorRotCache = Quaternion.identity;
        internal Vector3 m_ComLocal; internal Vector3 m_InertiaDiag = Vector3.one; internal Quaternion m_InertiaRot = Quaternion.identity;
        LayerMask m_Include, m_Exclude;

        public Rigidbody() { }

        internal void CacheProps()
        {
            if (m_Cached) return;
            m_Cached = true;
            PhysicsWorld.EnsureSettings();
            m_MassCache = NFloat("m_Mass", 1); m_DragCache = NFloat(DragKey); m_AngularDragCache = NFloat(AngularDragKey, 0.05f);
            m_UseGravityCache = NBool("m_UseGravity", true); m_IsKinematicCache = NBool("m_IsKinematic");
            m_InterpolationCache = (RigidbodyInterpolation)NInt("m_Interpolate"); m_ConstraintsCache = (RigidbodyConstraints)NInt("m_Constraints");
            m_CollisionModeCache = (CollisionDetectionMode)NInt("m_CollisionDetection");
            m_Include = NInt("m_IncludeLayers.m_Bits"); m_Exclude = NInt("m_ExcludeLayers.m_Bits");
            m_AutoCom = NBool("m_ImplicitCom", true); m_AutoTensor = NBool("m_ImplicitTensor", true);
            m_ComCache = NVec3("m_CenterOfMass"); var it = NGet("m_InertiaTensor"); m_TensorCache = it != null ? NVec3("m_InertiaTensor") : Vector3.one;
            if (NGet("m_InertiaRotation") is YMap) m_TensorRotCache = Serializer.ReadQuat(NGet("m_InertiaRotation"));
            if (m_MaxDepenetration < 0) m_MaxDepenetration = PhysicsWorld.DefaultMaxDepenetrationVelocity;
            if (m_MaxAngularVelocity < 0) m_MaxAngularVelocity = PhysicsWorld.DefaultMaxAngularSpeed;
            if (m_SolverIterations < 0) m_SolverIterations = PhysicsWorld.SolverIterations;
            if (m_SolverVelocityIterations < 0) m_SolverVelocityIterations = PhysicsWorld.SolverVelocityIterations;
            if (m_SleepThreshold < 0) m_SleepThreshold = PhysicsWorld.SleepThreshold;
        }

        Body B { get { CacheProps(); var b = PhysicsWorld.BodyOf(this); if (b == null) { PhysicsWorld.Sync(); b = PhysicsWorld.BodyOf(this); } return b; } }
        void Changed() { var b = PhysicsWorld.BodyOf(this); if (b != null) PhysicsWorld.Wake(b); }

        public Vector3 velocity { get => B?.V ?? Vector3.zero; set { var b = B; if (b != null && !m_IsKinematicCache) { b.V = value; PhysicsWorld.Wake(b); } } }
        public Vector3 angularVelocity { get => B?.W ?? Vector3.zero; set { var b = B; if (b != null && !m_IsKinematicCache) { b.W = value; PhysicsWorld.Wake(b); } } }
        public float drag { get { CacheProps(); return m_DragCache; } set { CacheProps(); m_DragCache = value; NSetF(DragKey, value); } }
        public float angularDrag { get { CacheProps(); return m_AngularDragCache; } set { CacheProps(); m_AngularDragCache = value; NSetF(AngularDragKey, value); } }
        // files saved by Unity 6 name drag / angular drag m_LinearDamping / m_AngularDamping
        string DragKey => NGet("m_LinearDamping") != null ? "m_LinearDamping" : "m_Drag";
        string AngularDragKey => NGet("m_AngularDamping") != null ? "m_AngularDamping" : "m_AngularDrag";
        public float mass { get { CacheProps(); return m_MassCache; } set { CacheProps(); m_MassCache = Math.Max(value, 1e-7f); NSetF("m_Mass", m_MassCache); var b = PhysicsWorld.BodyOf(this); if (b != null) { b.Mass = m_MassCache; b.InvMass = 1 / m_MassCache; } } }
        public void SetDensity(float density)
        {
            var b = B; if (b == null) return;
            float vol = 0; foreach (var s in b.Shapes) if (!s.Trigger) { s.Geom.MassProperties(out float v, out _, out _, out _); vol += v; }
            if (vol > 0) mass = density * vol;
        }
        public bool useGravity { get { CacheProps(); return m_UseGravityCache; } set { CacheProps(); m_UseGravityCache = value; NSetB("m_UseGravity", value); Changed(); } }
        public float maxDepenetrationVelocity { get { CacheProps(); return m_MaxDepenetration; } set { CacheProps(); m_MaxDepenetration = value; } }
        public bool isKinematic
        {
            get { CacheProps(); return m_IsKinematicCache; }
            set { CacheProps(); if (m_IsKinematicCache == value) return; m_IsKinematicCache = value; NSetB("m_IsKinematic", value); var b = PhysicsWorld.BodyOf(this); if (b != null) { if (value) { b.V = b.W = Vector3.zero; b.Sleeping = false; } else PhysicsWorld.Wake(b); PhysicsWorld.RefilterTriggers(b); } }
        }
        public bool freezeRotation
        {
            get => (constraints & RigidbodyConstraints.FreezeRotation) == RigidbodyConstraints.FreezeRotation;
            set => constraints = value ? constraints | RigidbodyConstraints.FreezeRotation : constraints & ~RigidbodyConstraints.FreezeRotation;
        }
        public RigidbodyConstraints constraints { get { CacheProps(); return m_ConstraintsCache; } set { CacheProps(); m_ConstraintsCache = value; NSetI("m_Constraints", (int)value); Changed(); } }
        public CollisionDetectionMode collisionDetectionMode { get { CacheProps(); return m_CollisionModeCache; } set { CacheProps(); m_CollisionModeCache = value; NSetI("m_CollisionDetection", (int)value); } }
        public bool automaticCenterOfMass { get { CacheProps(); return m_AutoCom; } set { CacheProps(); m_AutoCom = value; } }
        public Vector3 centerOfMass { get { var b = B; return b != null ? b.ComLocal : m_ComCache; } set { CacheProps(); m_AutoCom = false; m_ComCache = value; var b = PhysicsWorld.BodyOf(this); if (b != null) b.ComLocal = value; } }
        public Vector3 worldCenterOfMass { get { var b = B; return b != null ? b.ComWorld : transform.position; } }
        public bool automaticInertiaTensor { get { CacheProps(); return m_AutoTensor; } set { CacheProps(); m_AutoTensor = value; } }
        public Quaternion inertiaTensorRotation { get { var b = B; return b != null ? b.InertiaRot : m_TensorRotCache; } set { CacheProps(); m_AutoTensor = false; m_TensorRotCache = value; var b = PhysicsWorld.BodyOf(this); if (b != null) b.InertiaRot = value; } }
        public Vector3 inertiaTensor { get { var b = B; return b != null ? b.InertiaDiag : m_TensorCache; } set { CacheProps(); m_AutoTensor = false; m_TensorCache = value; var b = PhysicsWorld.BodyOf(this); if (b != null) b.InertiaDiag = value; } }
        public bool detectCollisions { get => m_DetectCollisions; set => m_DetectCollisions = value; }
        public Vector3 position { get => B?.PxPose.p ?? transform.position; set { var b = B; if (b == null) { transform.position = value; return; } b.PxPose = new PxPose(value, b.PxPose.q); transform.position = value; b.LastWrittenP = transform.position; PhysicsWorld.Wake(b); } }
        public Quaternion rotation { get => B?.PxPose.q ?? transform.rotation; set { var b = B; if (b == null) { transform.rotation = value; return; } b.PxPose = new PxPose(b.PxPose.p, value); transform.rotation = value; b.LastWrittenQ = transform.rotation; PhysicsWorld.Wake(b); } }
        public RigidbodyInterpolation interpolation { get { CacheProps(); return m_InterpolationCache; } set { CacheProps(); m_InterpolationCache = value; NSetI("m_Interpolate", (int)value); } }
        public int solverIterations { get { CacheProps(); return m_SolverIterations; } set { CacheProps(); m_SolverIterations = Math.Max(1, value); } }
        public int solverVelocityIterations { get { CacheProps(); return m_SolverVelocityIterations; } set { CacheProps(); m_SolverVelocityIterations = Math.Max(1, value); } }
        public int solverIterationCount { get => solverIterations; set => solverIterations = value; }
        public int solverVelocityIterationCount { get => solverVelocityIterations; set => solverVelocityIterations = value; }
        public float sleepThreshold { get { CacheProps(); return m_SleepThreshold; } set { CacheProps(); m_SleepThreshold = value; } }
        public float maxAngularVelocity { get { CacheProps(); return m_MaxAngularVelocity; } set { CacheProps(); m_MaxAngularVelocity = value; } }
        public void SetMaxAngularVelocity(float a) => maxAngularVelocity = a;
        public float maxLinearVelocity { get => m_MaxLinearVelocity; set => m_MaxLinearVelocity = value; }
        public LayerMask excludeLayers { get { CacheProps(); return m_Exclude; } set { CacheProps(); m_Exclude = value; NSetI("m_ExcludeLayers.m_Bits", value.value); } }
        public LayerMask includeLayers { get { CacheProps(); return m_Include; } set { CacheProps(); m_Include = value; NSetI("m_IncludeLayers.m_Bits", value.value); } }
        internal int m_IncludeBits => m_Include.value;
        internal int m_ExcludeBits => m_Exclude.value;

        public void MovePosition(Vector3 position)
        {
            var b = B; if (b == null) return;
            if (m_IsKinematicCache) { if (!b.HasKinTarget) b.KinTargetQ = b.PxPose.q; b.HasKinTarget = true; b.KinTargetP = position; }
            else this.position = position;
        }
        public void MoveRotation(Quaternion rot)
        {
            var b = B; if (b == null) return;
            if (m_IsKinematicCache) { if (!b.HasKinTarget) b.KinTargetP = b.PxPose.p; b.HasKinTarget = true; b.KinTargetQ = rot; }
            else rotation = rot;
        }
        public void Move(Vector3 position, Quaternion rotation) { MovePosition(position); MoveRotation(rotation); }
        public void Sleep() { var b = B; if (b == null || !b.Dynamic) return; b.Sleeping = true; b.WakeCounter = 0; b.V = b.W = Vector3.zero; }
        public bool IsSleeping() => B?.Sleeping ?? false;
        public void WakeUp() => PhysicsWorld.Wake(B);
        public void ResetCenterOfMass() { m_AutoCom = true; PhysicsWorld.Sync(); }
        public void ResetInertiaTensor() { m_AutoTensor = true; PhysicsWorld.Sync(); }
        public Vector3 GetRelativePointVelocity(Vector3 relativePoint) => GetPointVelocity(transform.TransformPoint(relativePoint));
        public Vector3 GetPointVelocity(Vector3 worldPoint) => B?.PointVelocity(worldPoint) ?? Vector3.zero;
        public Vector3 GetAccumulatedForce(float step) { var b = B; return b == null ? Vector3.zero : b.ForceAcc * m_MassCache + (step > 0 ? b.DV * m_MassCache / step : Vector3.zero); }
        public Vector3 GetAccumulatedForce() => GetAccumulatedForce(Time.fixedDeltaTime);
        public Vector3 GetAccumulatedTorque(float step) => B?.TorqueAcc ?? Vector3.zero;
        public Vector3 GetAccumulatedTorque() => GetAccumulatedTorque(Time.fixedDeltaTime);

        public void AddForce(Vector3 force, ForceMode mode)
        {
            var b = B; if (b == null || m_IsKinematicCache) return;
            switch (mode)
            {
                case ForceMode.Force: b.ForceAcc += force * b.InvMass; break;
                case ForceMode.Acceleration: b.ForceAcc += force; break;
                case ForceMode.Impulse: b.DV += force * b.InvMass; break;
                default: b.DV += force; break;
            }
            PhysicsWorld.Wake(b);
        }
        public void AddForce(Vector3 force) => AddForce(force, ForceMode.Force);
        public void AddForce(float x, float y, float z, ForceMode mode) => AddForce(new Vector3(x, y, z), mode);
        public void AddForce(float x, float y, float z) => AddForce(new Vector3(x, y, z), ForceMode.Force);
        public void AddRelativeForce(Vector3 force, ForceMode mode) => AddForce(transform.TransformDirection(force), mode);
        public void AddRelativeForce(Vector3 force) => AddRelativeForce(force, ForceMode.Force);
        public void AddRelativeForce(float x, float y, float z, ForceMode mode) => AddRelativeForce(new Vector3(x, y, z), mode);
        public void AddRelativeForce(float x, float y, float z) => AddRelativeForce(new Vector3(x, y, z), ForceMode.Force);
        public void AddTorque(Vector3 torque, ForceMode mode)
        {
            var b = B; if (b == null || m_IsKinematicCache) return;
            var invI = PhysicsWorld.InvInertiaWorld(b);
            switch (mode)
            {
                case ForceMode.Force: b.TorqueAcc += torque; break;
                case ForceMode.Acceleration: b.TorqueAcc += Inv(invI, torque); break;   // stored as torque: I·α
                case ForceMode.Impulse: b.DW += invI * torque; break;
                default: b.DW += torque; break;
            }
            PhysicsWorld.Wake(b);
        }
        static Vector3 Inv(M3 invI, Vector3 a)
        {
            // torque that produces angular acceleration a: solve invI·T = a (diagonal enough in practice; fall back per axis)
            return new Vector3(invI.m00 != 0 ? a.x / invI.m00 : 0, invI.m11 != 0 ? a.y / invI.m11 : 0, invI.m22 != 0 ? a.z / invI.m22 : 0);
        }
        public void AddTorque(Vector3 torque) => AddTorque(torque, ForceMode.Force);
        public void AddTorque(float x, float y, float z, ForceMode mode) => AddTorque(new Vector3(x, y, z), mode);
        public void AddTorque(float x, float y, float z) => AddTorque(new Vector3(x, y, z), ForceMode.Force);
        public void AddRelativeTorque(Vector3 torque, ForceMode mode) => AddTorque(transform.TransformDirection(torque), mode);
        public void AddRelativeTorque(Vector3 torque) => AddRelativeTorque(torque, ForceMode.Force);
        public void AddRelativeTorque(float x, float y, float z, ForceMode mode) => AddRelativeTorque(new Vector3(x, y, z), mode);
        public void AddRelativeTorque(float x, float y, float z) => AddRelativeTorque(x, y, z, ForceMode.Force);
        public void AddForceAtPosition(Vector3 force, Vector3 position, ForceMode mode)
        {
            var b = B; if (b == null || m_IsKinematicCache) return;
            AddForce(force, mode);
            var torque = Vector3.Cross(position - b.ComWorld, force);
            AddTorque(torque, mode == ForceMode.Acceleration ? ForceMode.Force : mode == ForceMode.VelocityChange ? ForceMode.Impulse : mode);
        }
        public void AddForceAtPosition(Vector3 force, Vector3 position) => AddForceAtPosition(force, position, ForceMode.Force);
        public void AddExplosionForce(float explosionForce, Vector3 explosionPosition, float explosionRadius, float upwardsModifier, ForceMode mode)
        {
            var b = B; if (b == null) return;
            var bounds = BodyBounds(b);
            var expl = explosionPosition - Vector3.up * upwardsModifier;
            var closest = bounds.ClosestPoint(expl);
            var dir = closest - expl; float dist = dir.magnitude;
            if (explosionRadius > 0 && dist > explosionRadius) return;
            float scale = explosionRadius > 0 ? 1 - dist / explosionRadius : 1;
            if (dist < 1e-6f) { dir = b.ComWorld - expl; if (dir.sqrMagnitude < 1e-12f) dir = Vector3.up; }
            AddForceAtPosition(dir.normalized * (explosionForce * scale), closest, mode);
        }
        public void AddExplosionForce(float explosionForce, Vector3 explosionPosition, float explosionRadius, float upwardsModifier) => AddExplosionForce(explosionForce, explosionPosition, explosionRadius, upwardsModifier, ForceMode.Force);
        public void AddExplosionForce(float explosionForce, Vector3 explosionPosition, float explosionRadius) => AddExplosionForce(explosionForce, explosionPosition, explosionRadius, 0, ForceMode.Force);

        static Bounds BodyBounds(Body b)
        {
            if (b.Shapes.Count == 0) return new Bounds(b.ComWorld, Vector3.zero);
            var bb = b.Shapes[0].Aabb; foreach (var s in b.Shapes) bb.Encapsulate(s.Aabb); return bb;
        }
        public Vector3 ClosestPointOnBounds(Vector3 position) { var b = B; return b == null ? transform.position : BodyBounds(b).ClosestPoint(position); }

        public bool SweepTest(Vector3 direction, out RaycastHit hitInfo, float maxDistance, QueryTriggerInteraction queryTriggerInteraction)
        {
            hitInfo = default;
            var all = SweepTestAll(direction, maxDistance, queryTriggerInteraction);
            if (all.Length == 0) return false;
            hitInfo = all.OrderBy(h => h.distance).First(); return true;
        }
        public bool SweepTest(Vector3 direction, out RaycastHit hitInfo, float maxDistance) => SweepTest(direction, out hitInfo, maxDistance, QueryTriggerInteraction.UseGlobal);
        public bool SweepTest(Vector3 direction, out RaycastHit hitInfo) => SweepTest(direction, out hitInfo, Mathf.Infinity, QueryTriggerInteraction.UseGlobal);
        public RaycastHit[] SweepTestAll(Vector3 direction, float maxDistance, QueryTriggerInteraction queryTriggerInteraction)
        {
            float l = direction.magnitude; if (l <= float.Epsilon) return new RaycastHit[0];
            var b = B; if (b == null) return new RaycastHit[0];
            var res = new Dictionary<Collider, RaycastHit>();
            foreach (var s in b.Shapes)
            {
                if (s.Trigger || s.Geom.Kind == GeomKind.Mesh) continue;
                foreach (var h in PhysicsWorld.Sweep(PhysicsWorld.ConvexOf(s), s.Aabb, direction / l, maxDistance, Physics.AllLayers, queryTriggerInteraction, true))
                {
                    if (h.Shape.Body == b) continue;
                    if (!res.TryGetValue(h.Shape.Col, out var old) || old.distance > h.Distance) res[h.Shape.Col] = RaycastHit.From(h);
                }
            }
            return res.Values.ToArray();
        }
        public RaycastHit[] SweepTestAll(Vector3 direction, float maxDistance) => SweepTestAll(direction, maxDistance, QueryTriggerInteraction.UseGlobal);
        public RaycastHit[] SweepTestAll(Vector3 direction) => SweepTestAll(direction, Mathf.Infinity, QueryTriggerInteraction.UseGlobal);
    }

    // ====================================================================================================== colliders
    public partial class Collider
    {
        internal Vector3 m_ShapeOffset; internal Quaternion m_ShapeRot = Quaternion.identity; internal float m_ContactOffset = -1;
        internal virtual Vector3 m_CenterCache => Vector3.zero;
        bool m_Modifiable, m_Provides;
        public Collider() { }
        public bool enabled { get => NBool("m_Enabled", true); set => NSetB("m_Enabled", value); }
        public Rigidbody attachedRigidbody { get { for (var t = transform; t != null; t = t.m_Parent) { var rb = t.gameObject.GetComponent<Rigidbody>(); if (rb != null) return rb; } return null; } }
        public bool isTrigger { get => NBool("m_IsTrigger"); set => NSetB("m_IsTrigger", value); }
        public float contactOffset { get => m_ContactOffset > 0 ? m_ContactOffset : Physics.defaultContactOffset; set => m_ContactOffset = value; }
        public bool hasModifiableContacts { get => m_Modifiable; set => m_Modifiable = value; }
        public bool providesContacts { get => m_Provides; set => m_Provides = value; }
        // Unity 2022.2+ layer overrides (serialized; the physics shape caches them with the other collider properties)
        public int layerOverridePriority { get => NInt("m_LayerOverridePriority"); set => NSetI("m_LayerOverridePriority", value); }
        public LayerMask excludeLayers { get => NInt("m_ExcludeLayers.m_Bits"); set => NSetI("m_ExcludeLayers.m_Bits", value.value); }
        public LayerMask includeLayers { get => NInt("m_IncludeLayers.m_Bits"); set => NSetI("m_IncludeLayers.m_Bits", value.value); }
        public PhysicMaterial sharedMaterial { get => NRef<PhysicMaterial>("m_Material"); set => NSetRef("m_Material", value); }
        public PhysicMaterial material
        {
            get { var m = sharedMaterial; if (m == null) { m = new PhysicMaterial(); sharedMaterial = m; } return m; }
            set => sharedMaterial = value;
        }

        internal Shape PhysShape() { var s = PhysicsWorld.ShapeOf(this); if (s == null) { PhysicsWorld.Sync(); s = PhysicsWorld.ShapeOf(this); } return s; }
        public Bounds bounds { get { var s = PhysShape(); return s?.Aabb ?? new Bounds(transform.position, Vector3.zero); } }

        public Vector3 ClosestPoint(Vector3 position)
        {
            var s = PhysShape(); if (s == null) return position;
            if (s.Geom.Kind == GeomKind.Mesh) { Debug.LogWarning("ClosestPoint is not supported for non-convex MeshColliders"); return position; }
            if (s.Geom.Kind == GeomKind.Box || s.Geom.Kind == GeomKind.Convex)
            {
                var lp = s.World.InvApply(position);
                return s.World.Apply(Geo.ClosestOnPoly(s.Geom.Poly, lp, out _));
            }
            var core = Narrowphase.Core(s.Geom, s.World);
            var c = core.Length == 1 ? core[0] : Geo.ClosestOnSegment(position, core[0], core[1], out _);
            var d = position - c; float l = d.magnitude;
            return l <= s.Geom.Radius ? position : c + d / l * s.Geom.Radius;
        }
        public Vector3 ClosestPointOnBounds(Vector3 position) => bounds.ClosestPoint(position);
        public bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance)
        {
            hitInfo = default;
            var s = PhysShape(); if (s == null) return false;
            var d = ray.direction.normalized;
            if (!PhysicsWorld.RayShape(s, ray.origin, d, maxDistance, out var h)) return false;
            hitInfo = RaycastHit.From(h); return true;
        }

        /// <summary>Geometry inputs that live outside this collider's own properties (the shape is rebuilt when they, the collider's
        /// properties or its scale change).</summary>
        internal virtual (int, int) GeometryExtra() => default;
        internal virtual Geom BuildGeom(Vector3 lossy, out Vector3 offset, out Quaternion rot) { offset = Vector3.zero; rot = Quaternion.identity; return Geom.Sphere(0); }
        protected static Vector3 Abs(Vector3 v) => new(Math.Abs(v.x), Math.Abs(v.y), Math.Abs(v.z));
    }

    public partial class BoxCollider
    {
        public BoxCollider() { }
        public Vector3 center { get => NVec3("m_Center"); set => NSetVec3("m_Center", value); }
        public Vector3 size { get => NGet("m_Size") != null ? NVec3("m_Size") : Vector3.one; set => NSetVec3("m_Size", value); }
        public Vector3 extents { get => size * 0.5f; set => size = value * 2; }
        internal override Vector3 m_CenterCache => center;
        internal override Geom BuildGeom(Vector3 lossy, out Vector3 offset, out Quaternion rot)
        { offset = Vector3.Scale(center, lossy); rot = Quaternion.identity; return Geom.Box(Vector3.Max(Abs(Vector3.Scale(size, lossy)) * 0.5f, Vector3.one * 1e-4f)); }
    }

    public partial class SphereCollider
    {
        public SphereCollider() { }
        public Vector3 center { get => NVec3("m_Center"); set => NSetVec3("m_Center", value); }
        public float radius { get => NFloat("m_Radius", 0.5f); set => NSetF("m_Radius", value); }
        internal override Vector3 m_CenterCache => center;
        internal override Geom BuildGeom(Vector3 lossy, out Vector3 offset, out Quaternion rot)
        {
            offset = Vector3.Scale(center, lossy); rot = Quaternion.identity;
            var a = Abs(lossy); return Geom.Sphere(Math.Max(1e-4f, radius * Math.Max(a.x, Math.Max(a.y, a.z))));
        }
    }

    public partial class CapsuleCollider
    {
        public CapsuleCollider() { }
        public Vector3 center { get => NVec3("m_Center"); set => NSetVec3("m_Center", value); }
        public float radius { get => NFloat("m_Radius", 0.5f); set => NSetF("m_Radius", value); }
        public float height { get => NFloat("m_Height", 2); set => NSetF("m_Height", value); }
        public int direction { get => NInt("m_Direction", 1); set => NSetI("m_Direction", value); }
        internal override Vector3 m_CenterCache => center;
        internal override Geom BuildGeom(Vector3 lossy, out Vector3 offset, out Quaternion rot)
        {
            offset = Vector3.Scale(center, lossy);
            var a = Abs(lossy); int d = direction;
            float axisScale = d == 0 ? a.x : d == 2 ? a.z : a.y;
            float rScale = d == 0 ? Math.Max(a.y, a.z) : d == 2 ? Math.Max(a.x, a.y) : Math.Max(a.x, a.z);
            float r = Math.Max(1e-4f, radius * rScale), h = Math.Max(height * axisScale, 2 * r);
            rot = d == 0 ? Quaternion.Euler(0, 0, -90) : d == 2 ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
            return Geom.Capsule(r, h * 0.5f - r);
        }
        internal Vector2 GetGlobalExtents() { var a = Abs(transform.lossyScale); return new Vector2(radius * Math.Max(a.x, a.z), height * a.y * 0.5f); }
        internal Matrix4x4 CalculateTransform() => transform.localToWorldMatrix;
    }

    public partial class MeshCollider
    {
        public MeshCollider() { }
        // Unity assigns the MeshFilter's mesh when the collider is added (Reset)
        public Mesh sharedMesh { get => NRef<Mesh>("m_Mesh") ?? (m_GameObject != null && gameObject.GetComponent<MeshFilter>() is MeshFilter mf ? mf.sharedMesh : null); set => NSetRef("m_Mesh", value); }
        public bool convex { get => NBool("m_Convex"); set => NSetB("m_Convex", value); }
        public MeshColliderCookingOptions cookingOptions { get => (MeshColliderCookingOptions)NInt("m_CookingOptions", 30); set => NSetI("m_CookingOptions", (int)value); }
        public float skinWidth { get; set; }
        public bool inflateMesh { get; set; }
        internal override (int, int) GeometryExtra() { var m = sharedMesh; return m != null ? (m.GetInstanceID(), m.vertexCount) : default; }
        internal override Geom BuildGeom(Vector3 lossy, out Vector3 offset, out Quaternion rot)
        {
            offset = Vector3.zero; rot = Quaternion.identity;
            var m = sharedMesh;
            if (m == null) return Geom.TriangleMesh(new TriMesh(Array.Empty<Vector3>(), Array.Empty<int>()));
            var v = m.vertices.Select(p => Vector3.Scale(p, lossy)).ToArray();
            if (convex) return Geom.Convex(Hull.Build(v));
            var t = m.triangles;
            if (lossy.x * lossy.y * lossy.z < 0) { t = (int[])t.Clone(); for (int i = 0; i + 2 < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]); }
            return Geom.TriangleMesh(new TriMesh(v, t));
        }
    }

    public partial class ConstantForce
    {
        public ConstantForce() { }
        public Vector3 force { get => NVec3("m_Force"); set => NSetVec3("m_Force", value); }
        public Vector3 relativeForce { get => NVec3("m_RelativeForce"); set => NSetVec3("m_RelativeForce", value); }
        public Vector3 torque { get => NVec3("m_Torque"); set => NSetVec3("m_Torque", value); }
        public Vector3 relativeTorque { get => NVec3("m_RelativeTorque"); set => NSetVec3("m_RelativeTorque", value); }
    }

    // ====================================================================================================== CharacterController
    /// <summary>PhysX character controller (CCT) style: kinematic capsule moved by sweeps; up (step) / side (slide) / down passes,
    /// slope limit, step offset, skin width (contact offset), OnControllerColliderHit for every touched collider.</summary>
    public partial class CharacterController
    {
        CollisionFlags m_Flags; Vector3 m_Velocity; float m_FallSpeed; bool m_Detect = true, m_Overlap = true;
        public CharacterController() { }
        public float radius { get => NFloat("m_Radius", 0.5f); set => NSetF("m_Radius", value); }
        public float height { get => NFloat("m_Height", 2); set => NSetF("m_Height", value); }
        public Vector3 center { get => NVec3("m_Center"); set => NSetVec3("m_Center", value); }
        public float slopeLimit { get => NFloat("m_SlopeLimit", 45); set => NSetF("m_SlopeLimit", Mathf.Clamp(value, 0, 180)); }
        public float stepOffset { get => NFloat("m_StepOffset", 0.3f); set => NSetF("m_StepOffset", Math.Max(0, value)); }
        public float skinWidth { get => NFloat("m_SkinWidth", 0.08f); set => NSetF("m_SkinWidth", value); }
        public float minMoveDistance { get => NFloat("m_MinMoveDistance", 0.001f); set => NSetF("m_MinMoveDistance", value); }
        public bool detectCollisions { get => m_Detect; set => m_Detect = value; }
        public bool enableOverlapRecovery { get => m_Overlap; set => m_Overlap = value; }
        public Vector3 velocity => m_Velocity;
        public bool isGrounded => (m_Flags & CollisionFlags.Below) != 0;
        public CollisionFlags collisionFlags => m_Flags;
        internal override Vector3 m_CenterCache => center;
        internal override Geom BuildGeom(Vector3 lossy, out Vector3 offset, out Quaternion rot)
        {
            offset = Vector3.Scale(center, lossy); rot = Quaternion.identity;
            var a = Abs(lossy);
            float r = Math.Max(1e-4f, radius * Math.Max(a.x, a.z)), h = Math.Max(height * a.y, 2 * r);
            return Geom.Capsule(r, h * 0.5f - r);
        }

        public bool SimpleMove(Vector3 speed)
        {
            // Unity: y of speed is ignored, gravity is applied by the controller
            float dt = Time.deltaTime;
            if (isGrounded) m_FallSpeed = 0;
            m_FallSpeed += Physics.gravity.y * dt;
            Move(new Vector3(speed.x, m_FallSpeed, speed.z) * dt);
            if (isGrounded) m_FallSpeed = 0;
            return isGrounded;
        }

        public CollisionFlags Move(Vector3 motion)
        {
            var start = transform.position;
            if (motion.magnitude < minMoveDistance) { m_Velocity = Vector3.zero; return m_Flags; }
            PhysicsWorld.PrepareQuery();
            m_Flags = CollisionFlags.None;
            var hits = new List<ControllerColliderHit>();
            var pos = start;
            var up = Vector3.up;
            float vertical = Vector3.Dot(motion, up);
            var side = motion - up * vertical;
            float skin = skinWidth;
            float cosSlope = Mathf.Cos(slopeLimit * Mathf.Deg2Rad);
            bool stepping = side.sqrMagnitude > 1e-10f;
            float step = stepping ? Math.Min(stepOffset, height) : 0;

            if (m_Overlap) pos = Recover(pos);
            // up pass: vertical up motion + step offset
            float upDist = Math.Max(0, vertical) + step;
            if (upDist > 0)
            {
                var moved = Slide(ref pos, up * upDist, skin, cosSlope, hits, pass: 0);
                if (moved < upDist - 1e-5f && vertical > 0) m_Flags |= CollisionFlags.Above;
                step = Math.Min(step, moved);
            }
            // side pass
            if (stepping) Slide(ref pos, side, skin, cosSlope, hits, pass: 1);
            // down pass: undo the step and apply downward motion
            float downDist = Math.Max(0, -vertical) + step;
            if (downDist > 0)
            {
                var before = pos;
                Slide(ref pos, -up * downDist, skin, cosSlope, hits, pass: 2);
            }
            transform.position = pos;
            var b = PhysicsWorld.Bodies.FirstOrDefault(x => x.Cct == this);
            if (b != null) { b.PxPose = new PxPose(pos, transform.rotation); b.LastWrittenP = pos; }
            float dt = Time.deltaTime > 0 ? Time.deltaTime : Time.fixedDeltaTime;
            m_Velocity = (pos - start) / dt;
            foreach (var h in hits) MiniUnity.PhysicsMessages.Send(gameObject, "OnControllerColliderHit", h, false);
            return m_Flags;
        }

        Convex CoreAt(Vector3 pos, float extraRadius)
        {
            var g = BuildGeom(transform.lossyScale, out var off, out _);
            var p = pos + transform.rotation * off;
            var a = p + transform.rotation * new Vector3(0, -g.HalfHeight, 0); var b = p + transform.rotation * new Vector3(0, g.HalfHeight, 0);
            return new PointsConvex(g.Radius + extraRadius, a, b);
        }

        /// <summary>Collide-and-slide along `move` (≤ 4 iterations). Returns the distance moved along the original direction.</summary>
        float Slide(ref Vector3 pos, Vector3 move, float skin, float cosSlope, List<ControllerColliderHit> hits, int pass)
        {
            var origDir = move.normalized; float moved = 0;
            var startPos = pos;
            for (int iter = 0; iter < 4 && move.sqrMagnitude > 1e-12f; iter++)
            {
                // surfaces already within the skin: a sweep that starts touching them reports nothing, so take the motion into them
                // out first (PhysX's controller keeps that contact offset the same way)
                foreach (var (tn, tcol) in Touching(pos, skin))
                {
                    var bn = tn;
                    if (pass == 1 && bn.y < cosSlope) { bn = new Vector3(bn.x, 0, bn.z); if (bn.sqrMagnitude < 1e-8f) continue; bn.Normalize(); }
                    float into = Vector3.Dot(move, bn);
                    if (into >= -1e-5f * move.magnitude) continue;
                    if (pass == 2) { if (tn.y > 1e-3f) m_Flags |= CollisionFlags.Below; }
                    else if (pass == 0) { if (tn.y < -0.1f) m_Flags |= CollisionFlags.Above; }
                    else if (tn.y > 1e-3f && tn.y >= cosSlope) m_Flags |= CollisionFlags.Below; else m_Flags |= CollisionFlags.Sides;
                    move -= bn * into;
                }
                if (move.sqrMagnitude <= 1e-12f) break;
                float len = move.magnitude; var dir = move / len;
                var q = CoreAt(pos, skin);
                var qb = new Bounds(pos, Vector3.one * (height + 2 * (radius + skin)) * 2);
                var hs = PhysicsWorld.Sweep(q, qb, dir, len, Physics.AllLayers, QueryTriggerInteraction.Ignore, true, this)
                    .Where(h => !(h.Shape.Body.Rb != null && h.Shape.Body.Rb.m_DetectCollisions == false)).ToList();
                if (hs.Count == 0) { pos += move; break; }
                var h = hs[0];
                float travel = Math.Max(0, h.Distance - 1e-4f);
                pos += dir * travel;
                var n = h.Normal;
                hits.Add(new ControllerColliderHit { m_Controller = this, m_Collider = h.Shape.Col, m_Point = h.Point, m_Normal = n, m_MoveDirection = dir, m_MoveLength = travel });
                float ny = n.y;
                if (pass == 2) { if (ny > 0) m_Flags |= CollisionFlags.Below; }
                else if (pass == 0) { if (ny < -0.1f) m_Flags |= CollisionFlags.Above; }
                else
                {
                    if (ny > 0 && ny >= cosSlope) m_Flags |= CollisionFlags.Below;
                    else m_Flags |= CollisionFlags.Sides;
                }
                var rest = move - dir * travel;
                if (pass == 2) break;                       // landing: stop
                if (pass == 0) { if (ny < 0) break; }      // head hit
                // walls (or slopes steeper than the limit) are treated as vertical: no climbing
                if (pass == 1 && ny < cosSlope) { n = new Vector3(n.x, 0, n.z); if (n.sqrMagnitude < 1e-8f) break; n.Normalize(); }
                move = rest - n * Vector3.Dot(rest, n);
            }
            return Vector3.Dot(pos - startPos, origDir);
        }

        /// <summary>Normals (pointing at the controller) of the solid shapes within skin + 1 mm of its capsule.</summary>
        List<(Vector3 n, Collider col)> Touching(Vector3 pos, float skin)
        {
            var res = new List<(Vector3, Collider)>();
            var me = PhysicsWorld.ShapeOf(this);
            var core = CoreAt(pos, 0);
            var qb = new Bounds(pos, Vector3.one * (height + 2 * (radius + skin)) + Vector3.one * 0.01f);
            foreach (var s in PhysicsWorld.Shapes)
            {
                if (s == me || s.Trigger || s.Col == this || s.Geom.Kind == GeomKind.Mesh || !s.Aabb.Intersects(qb)) continue;
                if (s.Body.Rb != null && s.Body.Rb.m_DetectCollisions == false) continue;
                var other = PhysicsWorld.ConvexOf(s);
                if (!Gjk.Distance(core, other, out float d, out var pa, out var pb) || d < 1e-6f) continue;
                float gap = d - core.Radius - other.Radius;
                if (gap > skin + 1e-3f) continue;
                res.Add(((pa - pb) / d, s.Col));
            }
            return res;
        }

        Vector3 Recover(Vector3 pos)
        {
            var me = PhysicsWorld.ShapeOf(this);
            for (int iter = 0; iter < 4; iter++)
            {
                var g = BuildGeom(transform.lossyScale, out var off, out _);
                var pose = new PxPose(pos + transform.rotation * off, transform.rotation);
                var push = Vector3.zero; bool any = false;
                foreach (var s in PhysicsWorld.Shapes)
                {
                    if (s == me || s.Trigger || s.Col == this) continue;
                    var cs = new List<Contact>();
                    Narrowphase.Collide(g, pose, s.Geom, s.World, 0, cs);
                    foreach (var c in cs) if (c.Separation < -1e-4f) { push += c.Normal * -c.Separation; any = true; }
                }
                if (!any) break;
                pos += push;
            }
            return pos;
        }
    }
}

namespace MiniUnity
{
    using UnityEngine;

    /// <summary>Physics messages like Unity: sent to every MonoBehaviour of the GameObject (disabled ones too), with or without the
    /// argument; IEnumerator handlers run as coroutines.</summary>
    public static class PhysicsMessages
    {
        static readonly Dictionary<(Type, string), System.Reflection.MethodInfo> cache = new();

        public static void Send(GameObject go, string name, object arg, bool includeDisabled = true)
        {
            if (go == null || go.m_Destroyed || !go.activeInHierarchy) return;
            foreach (var c in go.m_Components.ToList())
            {
                if (c is not MonoBehaviour mb || mb.m_Destroyed) continue;
                if (!includeDisabled && !mb.enabled) continue;
                var m = Find(mb.GetType(), name);
                if (m == null) continue;
                try
                {
                    var r = m.Invoke(mb, m.GetParameters().Length == 0 ? null : new[] { arg });
                    if (r is IEnumerator e && Play.IsPlaying) Play.StartRoutine(mb, e, name);
                }
                catch (System.Reflection.TargetInvocationException tie) { Play.Report(tie.InnerException ?? tie, $"{mb.GetType().Name}.{name}"); }
                catch (Exception ex) { Play.Report(ex, $"{mb.GetType().Name}.{name}"); }
            }
        }

        static System.Reflection.MethodInfo Find(Type t, string name)
        {
            if (cache.TryGetValue((t, name), out var m)) return m;
            for (var c = t; c != null && c != typeof(MonoBehaviour); c = c.BaseType)
            {
                foreach (var mi in c.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                    if (mi.Name == name && mi.GetParameters().Length <= 1) { m = mi; break; }
                if (m != null) break;
            }
            return cache[(t, name)] = m;
        }

        /// <summary>Deliver the events of the last simulation step.</summary>
        public static void Dispatch()
        {
            foreach (var (j, f) in Physics3D.PhysicsWorld.PendingJointBreaks.ToList())
            {
                Send(j.gameObject, "OnJointBreak", f);
                Object.Destroy(j);
            }
            Physics3D.PhysicsWorld.PendingJointBreaks.Clear();
            var evs = Physics3D.PhysicsWorld.PendingEvents.ToList();
            Physics3D.PhysicsWorld.PendingEvents.Clear();
            if (!Physics3D.PhysicsWorld.InvokeCollisionCallbacks) return;
            foreach (var e in evs)
            {
                if (e.Self == null || e.Self.m_Destroyed) continue;
                object arg = e.Message.StartsWith("OnTrigger") ? e.Other : new Collision(e);
                var targets = new List<GameObject> { e.Self.gameObject };
                var rb = e.Self.attachedRigidbody;
                if (rb != null && rb.gameObject != e.Self.gameObject) targets.Add(rb.gameObject);
                foreach (var go in targets) Send(go, e.Message, arg);
            }
        }
    }
}
