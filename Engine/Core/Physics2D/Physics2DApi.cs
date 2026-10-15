// UnityEngine 2D physics API (Physics2DModule) over MiniUnity.Box2D.Physics2DWorld. Public surface and defaults follow the
// Unity 2022.3 bindings (UnityCsReference Modules/Physics2D); the simulation follows Box2D v2.4.1.
using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity;
using MiniUnity.Box2D;
using MiniUnity.Yaml;
using Vector2 = UnityEngine.Vector2;

namespace UnityEngine
{
    public partial class PhysicsMaterial2D
    {
        public PhysicsMaterial2D() { m_Name = "DynamicMaterial"; }
        public PhysicsMaterial2D(string name) { m_Name = name; }
        public float friction { get => NFloat("friction", 0.4f); set => NSetF("friction", value); }
        public float bounciness { get => NFloat("bounciness", 0); set => NSetF("bounciness", value); }
    }

    // ====================================================================================================== result structs
    public partial struct RaycastHit2D
    {
        internal Vector2 m_Centroid, m_Point, m_Normal; internal float m_Distance, m_Fraction; internal int m_Collider;
        internal Collider2D m_ColliderRef;
        public static implicit operator bool(RaycastHit2D hit) => hit.collider != null;
        public int CompareTo(RaycastHit2D other)
        {
            if (collider == null) return 1;
            if (other.collider == null) return -1;
            return fraction.CompareTo(other.fraction);
        }
        public Vector2 centroid { get => m_Centroid; set => m_Centroid = value; }
        public Vector2 point { get => m_Point; set => m_Point = value; }
        public Vector2 normal { get => m_Normal; set => m_Normal = value; }
        public float distance { get => m_Distance; set => m_Distance = value; }
        public float fraction { get => m_Fraction; set => m_Fraction = value; }
        public Collider2D collider => m_ColliderRef != null && !m_ColliderRef.m_Destroyed ? m_ColliderRef : m_Collider != 0 ? Object.FindObjectFromInstanceID(m_Collider) as Collider2D : null;
        public Rigidbody2D rigidbody => collider != null ? collider.attachedRigidbody : null;
        public Transform transform { get { var rb = rigidbody; return rb != null ? rb.transform : collider != null ? collider.transform : null; } }

        internal static RaycastHit2D From(Physics2DWorld.QueryHit h) => new()
        {
            m_Centroid = h.Centroid, m_Point = h.Point, m_Normal = h.Normal, m_Distance = h.Distance, m_Fraction = h.Fraction,
            m_Collider = h.Col.GetInstanceID(), m_ColliderRef = h.Col,
        };
    }

    public partial struct ContactPoint2D
    {
        internal Vector2 m_Point, m_Normal, m_RelativeVelocity; internal float m_Separation, m_NormalImpulse, m_TangentImpulse;
        internal int m_Collider, m_OtherCollider, m_Rigidbody, m_OtherRigidbody, m_Enabled;
        internal Collider2D m_ColliderRef, m_OtherColliderRef;
        public Vector2 point => m_Point;
        public Vector2 normal => m_Normal;
        public float separation => m_Separation;
        public float normalImpulse => m_NormalImpulse;
        public float tangentImpulse => m_TangentImpulse;
        public Vector2 relativeVelocity => m_RelativeVelocity;
        public Collider2D collider => m_ColliderRef ?? Object.FindObjectFromInstanceID(m_Collider) as Collider2D;
        public Collider2D otherCollider => m_OtherColliderRef ?? Object.FindObjectFromInstanceID(m_OtherCollider) as Collider2D;
        public Rigidbody2D rigidbody => collider != null ? collider.attachedRigidbody : null;
        public Rigidbody2D otherRigidbody => otherCollider != null ? otherCollider.attachedRigidbody : null;
        public bool enabled => m_Enabled == 1;
    }

    public partial struct ColliderDistance2D
    {
        internal Vector2 m_PointA, m_PointB, m_Normal; internal float m_Distance; internal int m_IsValid;
        public Vector2 pointA { get => m_PointA; set => m_PointA = value; }
        public Vector2 pointB { get => m_PointB; set => m_PointB = value; }
        public Vector2 normal => m_Normal;
        public float distance { get => m_Distance; set => m_Distance = value; }
        public bool isOverlapped => m_Distance < 0;
        public bool isValid { get => m_IsValid != 0; set => m_IsValid = value ? 1 : 0; }
    }

    public partial struct ContactFilter2D
    {
        public ContactFilter2D NoFilter()
        {
            useTriggers = true; useLayerMask = false; layerMask = Physics2D.AllLayers; useDepth = false; useOutsideDepth = false;
            minDepth = float.NegativeInfinity; maxDepth = float.PositiveInfinity; useNormalAngle = false; useOutsideNormalAngle = false;
            minNormalAngle = 0; maxNormalAngle = NormalAngleUpperLimit;
            return this;
        }
        public void ClearLayerMask() => useLayerMask = false;
        public void SetLayerMask(LayerMask layerMask) { this.layerMask = layerMask; useLayerMask = true; }
        public void ClearDepth() => useDepth = false;
        public void SetDepth(float minDepth, float maxDepth) { this.minDepth = minDepth; this.maxDepth = maxDepth; useDepth = true; }
        public void ClearNormalAngle() => useNormalAngle = false;
        public void SetNormalAngle(float minNormalAngle, float maxNormalAngle) { this.minNormalAngle = minNormalAngle; this.maxNormalAngle = maxNormalAngle; useNormalAngle = true; }
        public bool isFiltering => !useTriggers || useLayerMask || useDepth || useNormalAngle;
        public bool IsFilteringTrigger(Collider2D collider) => !useTriggers && collider.isTrigger;
        public bool IsFilteringLayerMask(GameObject obj) => useLayerMask && (layerMask & (1 << obj.layer)) == 0;
        public bool IsFilteringDepth(GameObject obj)
        {
            if (!useDepth) return false;
            float lo = Math.Min(minDepth, maxDepth), hi = Math.Max(minDepth, maxDepth);
            float z = obj.transform.position.z;
            bool result = z < lo || z > hi;
            return useOutsideDepth ? !result : result;
        }
        public bool IsFilteringNormalAngle(Vector2 normal) => IsFilteringNormalAngle(Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg);
        public bool IsFilteringNormalAngle(float angle)
        {
            if (!useNormalAngle) return false;
            float lo = Mathf.Clamp(minNormalAngle, -NormalAngleUpperLimit, NormalAngleUpperLimit), hi = Mathf.Clamp(maxNormalAngle, -NormalAngleUpperLimit, NormalAngleUpperLimit);
            if (lo > hi) (lo, hi) = (hi, lo);
            float a = Mathf.Repeat(angle - lo, 360f);
            bool inside = a <= hi - lo;
            return useOutsideNormalAngle ? inside : !inside;
        }
        internal static ContactFilter2D CreateLegacyFilter(int layerMask, float minDepth, float maxDepth)
        {
            var f = new ContactFilter2D { useTriggers = Physics2D.queriesHitTriggers };
            f.SetLayerMask(layerMask);
            f.SetDepth(minDepth, maxDepth);
            return f;
        }
    }

    public partial struct JointAngleLimits2D
    {
        internal float m_LowerAngle, m_UpperAngle;
        public float min { get => m_LowerAngle; set => m_LowerAngle = value; }
        public float max { get => m_UpperAngle; set => m_UpperAngle = value; }
    }
    public partial struct JointTranslationLimits2D
    {
        internal float m_LowerTranslation, m_UpperTranslation;
        public float min { get => m_LowerTranslation; set => m_LowerTranslation = value; }
        public float max { get => m_UpperTranslation; set => m_UpperTranslation = value; }
    }
    public partial struct JointMotor2D
    {
        internal float m_MotorSpeed, m_MaximumMotorTorque;
        public float motorSpeed { get => m_MotorSpeed; set => m_MotorSpeed = value; }
        public float maxMotorTorque { get => m_MaximumMotorTorque; set => m_MaximumMotorTorque = value; }
    }
    public partial struct JointSuspension2D
    {
        internal float m_DampingRatio, m_Frequency, m_Angle;
        public float dampingRatio { get => m_DampingRatio; set => m_DampingRatio = value; }
        public float frequency { get => m_Frequency; set => m_Frequency = value; }
        public float angle { get => m_Angle; set => m_Angle = value; }
    }

    public partial struct PhysicsJobOptions2D
    {
        public bool useMultithreading { get; set; }
        public bool useConsistencySorting { get; set; }
        public int interpolationPosesPerJob { get; set; }
        public int newContactsPerJob { get; set; }
        public int collideContactsPerJob { get; set; }
        public int clearFlagsPerJob { get; set; }
        public int clearBodyForcesPerJob { get; set; }
        public int syncDiscreteFixturesPerJob { get; set; }
        public int syncContinuousFixturesPerJob { get; set; }
        public int findNearestContactsPerJob { get; set; }
        public int updateTriggerContactsPerJob { get; set; }
        public int islandSolverCostThreshold { get; set; }
        public int islandSolverBodyCostScale { get; set; }
        public int islandSolverContactCostScale { get; set; }
        public int islandSolverJointCostScale { get; set; }
        public int islandSolverBodiesPerJob { get; set; }
        public int islandSolverContactsPerJob { get; set; }
    }

    // ====================================================================================================== Collision2D
    public partial class Collision2D
    {
        internal Collider2D m_ColliderRef, m_OtherColliderRef; internal Rigidbody2D m_RigidbodyRef, m_OtherRigidbodyRef;
        ContactPoint2D[] Contacts_Internal() => m_LegacyContacts ?? m_ReusedContacts ?? Array.Empty<ContactPoint2D>();
        public Collider2D collider => m_ColliderRef ?? Object.FindObjectFromInstanceID(m_Collider) as Collider2D;
        public Collider2D otherCollider => m_OtherColliderRef ?? Object.FindObjectFromInstanceID(m_OtherCollider) as Collider2D;
        public Rigidbody2D rigidbody => m_RigidbodyRef ?? Object.FindObjectFromInstanceID(m_Rigidbody) as Rigidbody2D;
        public Rigidbody2D otherRigidbody => m_OtherRigidbodyRef ?? Object.FindObjectFromInstanceID(m_OtherRigidbody) as Rigidbody2D;
        public Transform transform => rigidbody != null ? rigidbody.transform : collider.transform;
        public GameObject gameObject => rigidbody != null ? rigidbody.gameObject : collider.gameObject;
        public Vector2 relativeVelocity => m_RelativeVelocity;
        public bool enabled => m_Enabled == 1;
        public ContactPoint2D[] contacts
        {
            get
            {
                if (m_LegacyContacts == null) { m_LegacyContacts = new ContactPoint2D[m_ContactCount]; if (m_ReusedContacts != null) Array.Copy(m_ReusedContacts, m_LegacyContacts, m_ContactCount); }
                return m_LegacyContacts;
            }
        }
        public int contactCount => m_ContactCount;
        public ContactPoint2D GetContact(int index)
        {
            if (index < 0 || index >= m_ContactCount) throw new ArgumentOutOfRangeException($"Cannot get contact at index {index}. There are {m_ContactCount} contact(s).");
            return Contacts_Internal()[index];
        }
        public int GetContacts(ContactPoint2D[] contacts)
        {
            if (contacts == null) throw new NullReferenceException("Cannot get contacts as the provided array is NULL.");
            int n = Math.Min(m_ContactCount, contacts.Length); Array.Copy(Contacts_Internal(), contacts, n); return n;
        }
        public int GetContacts(List<ContactPoint2D> contacts)
        {
            if (contacts == null) throw new NullReferenceException("Cannot get contacts as the provided list is NULL.");
            contacts.Clear(); var a = Contacts_Internal();
            for (int i = 0; i < m_ContactCount; i++) contacts.Add(a[i]);
            return m_ContactCount;
        }
    }

    // ====================================================================================================== Rigidbody2D
    public partial class Rigidbody2D
    {
        internal bool m_Cached, m_SimulatedCache = true, m_FullKinematicCache, m_UseAutoMassCache, m_HasCom, m_HasInertia, m_InterpValid;
        internal RigidbodyType2D m_BodyTypeCache; internal RigidbodyInterpolation2D m_InterpolationCache; internal RigidbodySleepMode2D m_SleepModeCache = RigidbodySleepMode2D.StartAwake;
        internal CollisionDetectionMode2D m_CollisionModeCache; internal RigidbodyConstraints2D m_ConstraintsCache;
        internal float m_MassCache = 1, m_DragCache, m_AngularDragCache = 0.05f, m_GravityScaleCache = 1, m_InertiaCache, m_AutoMass;
        internal Vector2 m_ComCache;
        internal int m_IncludeLayers, m_ExcludeLayers;
        internal Vector2 m_LastWrittenP, m_PrevP, m_CurP, m_MoveP; internal float m_LastWrittenA, m_PrevA, m_CurA, m_MoveA;
        internal bool m_HasMoveP, m_HasMoveR;

        internal void CacheProps()
        {
            if (m_Cached) return;
            m_Cached = true;
            m_BodyTypeCache = (RigidbodyType2D)NInt("m_BodyType"); m_SimulatedCache = NBool("m_Simulated", true);
            m_FullKinematicCache = NBool("m_UseFullKinematicContacts"); m_UseAutoMassCache = NBool("m_UseAutoMass");
            m_MassCache = NFloat("m_Mass", 1); m_DragCache = NFloat("m_LinearDrag"); m_AngularDragCache = NFloat("m_AngularDrag", 0.05f);
            m_GravityScaleCache = NFloat("m_GravityScale", 1);
            m_InterpolationCache = (RigidbodyInterpolation2D)NInt("m_Interpolate"); m_SleepModeCache = (RigidbodySleepMode2D)NInt("m_SleepingMode", 1);
            m_CollisionModeCache = (CollisionDetectionMode2D)NInt("m_CollisionDetection"); m_ConstraintsCache = (RigidbodyConstraints2D)NInt("m_Constraints");
            m_IncludeLayers = NInt("m_IncludeLayers.m_Bits"); m_ExcludeLayers = NInt("m_ExcludeLayers.m_Bits");
        }

        internal Body2 B { get { CacheProps(); var b = Physics2DWorld.BodyOf(this); if (b == null) { Physics2DWorld.Sync(); b = Physics2DWorld.BodyOf(this); } return b; } }
        void Wake() { var b = Physics2DWorld.BodyOf(this); b?.SetAwake(true); }

        public Vector2 position
        {
            get { var b = B; return b != null ? b.Xf.p : (Vector2)transform.position; }
            set { var b = B; if (b == null) { transform.position = new Vector3(value.x, value.y, transform.position.z); return; } b.SetTransform(value, b.A); m_CurP = m_PrevP = value; b.SetAwake(true); }
        }
        public float rotation
        {
            get { var b = B; return b != null ? b.A * Mathf.Rad2Deg : transform.rotation.eulerAngles.z; }
            set { var b = B; if (b == null) { var e = transform.eulerAngles; transform.eulerAngles = new Vector3(e.x, e.y, value); return; } b.SetTransform(b.Xf.p, value * Mathf.Deg2Rad); m_CurA = m_PrevA = value * Mathf.Deg2Rad; b.SetAwake(true); }
        }
        public void SetRotation(float angle) => rotation = angle;
        public void SetRotation(Quaternion rotation) => this.rotation = rotation.eulerAngles.z;
        public void MovePosition(Vector2 position) { if (B == null) return; m_HasMoveP = true; m_MoveP = position; }
        public void MoveRotation(float angle) { if (B == null) return; m_HasMoveR = true; m_MoveA = angle * Mathf.Deg2Rad; }
        public void MoveRotation(Quaternion rotation) => MoveRotation(rotation.eulerAngles.z);
        public Vector2 velocity
        {
            get => B?.V ?? Vector2.zero;
            set { var b = B; if (b == null || b.IsStatic) return; b.V = value; if (B2.Dot(value, value) > 0) b.SetAwake(true); }
        }
        public float angularVelocity
        {
            get => (B?.W ?? 0) * Mathf.Rad2Deg;
            set { var b = B; if (b == null || b.IsStatic) return; b.W = value * Mathf.Deg2Rad; if (value != 0) b.SetAwake(true); }
        }
        public bool useAutoMass { get { CacheProps(); return m_UseAutoMassCache; } set { CacheProps(); m_UseAutoMassCache = value; NSetB("m_UseAutoMass", value); Remass(); } }
        public float mass
        {
            get { CacheProps(); var b = B; return m_UseAutoMassCache ? (b != null && b.IsDynamic ? b.Mass : m_AutoMass) : m_MassCache; }
            set { CacheProps(); if (m_UseAutoMassCache) { Debug.LogWarning("Cannot set the mass of a Rigidbody2D when useAutoMass is enabled."); return; } m_MassCache = Math.Max(value, 1e-4f); NSetF("m_Mass", m_MassCache); Remass(); }
        }
        void Remass() { if (Physics2DWorld.BodyOf(this) != null) Physics2DWorld.Sync(); }
        public PhysicsMaterial2D sharedMaterial { get => NRef<PhysicsMaterial2D>("m_Material"); set => NSetRef("m_Material", value); }
        public Vector2 centerOfMass { get { var b = B; return b != null ? b.LocalCenter : m_ComCache; } set { CacheProps(); m_HasCom = true; m_ComCache = value; Remass(); } }
        public Vector2 worldCenterOfMass => B?.C ?? (Vector2)transform.position;
        public float inertia
        {
            get { var b = B; return b != null ? (b.IsDynamic ? b.I : 0) : m_InertiaCache; }
            set { CacheProps(); m_HasInertia = true; m_InertiaCache = Math.Max(0, value); Remass(); }
        }
        public float drag { get { CacheProps(); return m_DragCache; } set { CacheProps(); m_DragCache = Math.Max(0, value); NSetF("m_LinearDrag", m_DragCache); var b = Physics2DWorld.BodyOf(this); if (b != null) b.LinearDamping = m_DragCache; } }
        public float angularDrag { get { CacheProps(); return m_AngularDragCache; } set { CacheProps(); m_AngularDragCache = Math.Max(0, value); NSetF("m_AngularDrag", m_AngularDragCache); var b = Physics2DWorld.BodyOf(this); if (b != null) b.AngularDamping = m_AngularDragCache; } }
        public float gravityScale { get { CacheProps(); return m_GravityScaleCache; } set { CacheProps(); m_GravityScaleCache = value; NSetF("m_GravityScale", value); var b = Physics2DWorld.BodyOf(this); if (b != null) { b.GravityScale = value; b.SetAwake(true); } } }
        public RigidbodyType2D bodyType
        {
            get { CacheProps(); return m_BodyTypeCache; }
            set { CacheProps(); if (m_BodyTypeCache == value) return; m_BodyTypeCache = value; NSetI("m_BodyType", (int)value); if (Physics2DWorld.BodyOf(this) != null) Physics2DWorld.Sync(); }
        }
        public bool useFullKinematicContacts { get { CacheProps(); return m_FullKinematicCache; } set { CacheProps(); m_FullKinematicCache = value; NSetB("m_UseFullKinematicContacts", value); } }
        public bool isKinematic { get => bodyType == RigidbodyType2D.Kinematic; set => bodyType = value ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic; }
        public bool fixedAngle { get => freezeRotation; set => freezeRotation = value; }
        public bool freezeRotation
        {
            get => (constraints & RigidbodyConstraints2D.FreezeRotation) != 0;
            set => constraints = value ? constraints | RigidbodyConstraints2D.FreezeRotation : constraints & ~RigidbodyConstraints2D.FreezeRotation;
        }
        public RigidbodyConstraints2D constraints
        {
            get { CacheProps(); return m_ConstraintsCache; }
            set { CacheProps(); m_ConstraintsCache = value; NSetI("m_Constraints", (int)value); if (Physics2DWorld.BodyOf(this) != null) Physics2DWorld.Sync(); }
        }
        public bool simulated { get { CacheProps(); return m_SimulatedCache; } set { CacheProps(); m_SimulatedCache = value; NSetB("m_Simulated", value); } }
        public RigidbodyInterpolation2D interpolation { get { CacheProps(); return m_InterpolationCache; } set { CacheProps(); m_InterpolationCache = value; NSetI("m_Interpolate", (int)value); } }
        public RigidbodySleepMode2D sleepMode { get { CacheProps(); return m_SleepModeCache; } set { CacheProps(); m_SleepModeCache = value; NSetI("m_SleepingMode", (int)value); } }
        public CollisionDetectionMode2D collisionDetectionMode { get { CacheProps(); return m_CollisionModeCache; } set { CacheProps(); m_CollisionModeCache = value; NSetI("m_CollisionDetection", (int)value); } }
        public int attachedColliderCount => B?.Fixtures.Select(f => f.Owner).Distinct().Count() ?? 0;
        public Vector2 totalForce { get => B?.Force ?? Vector2.zero; set { var b = B; if (b != null) { b.Force = value; b.SetAwake(true); } } }
        public float totalTorque { get => B?.Torque ?? 0; set { var b = B; if (b != null) { b.Torque = value; b.SetAwake(true); } } }
        public LayerMask excludeLayers { get { CacheProps(); return m_ExcludeLayers; } set { CacheProps(); m_ExcludeLayers = value; } }
        public LayerMask includeLayers { get { CacheProps(); return m_IncludeLayers; } set { CacheProps(); m_IncludeLayers = value; } }
        internal void SetDragBehaviour(bool dragged) { }

        public bool IsSleeping() => !(B?.Awake ?? true);
        public bool IsAwake() => B?.Awake ?? false;
        public void Sleep() => B?.SetAwake(false);
        public void WakeUp() => B?.SetAwake(true);

        // ---------------------------------------------------------------- forces (ForceMode2D: Force or Impulse)
        public void AddForce(Vector2 force, ForceMode2D mode)
        {
            var b = B; if (b == null || !b.IsDynamic) return;
            if (mode == ForceMode2D.Impulse) b.V += b.InvMass * Vector2.Scale(force, b.InvMassAxis); else b.Force += force;
            b.SetAwake(true);
        }
        public void AddForce(Vector2 force) => AddForce(force, ForceMode2D.Force);
        public void AddRelativeForce(Vector2 relativeForce, ForceMode2D mode) => AddForce(GetRelativeVector(relativeForce), mode);
        public void AddRelativeForce(Vector2 relativeForce) => AddRelativeForce(relativeForce, ForceMode2D.Force);
        public void AddForceAtPosition(Vector2 force, Vector2 position, ForceMode2D mode)
        {
            var b = B; if (b == null || !b.IsDynamic) return;
            if (mode == ForceMode2D.Impulse) { b.V += b.InvMass * Vector2.Scale(force, b.InvMassAxis); b.W += b.InvI * B2.Cross(position - b.C, force); }
            else { b.Force += force; b.Torque += B2.Cross(position - b.C, force); }
            b.SetAwake(true);
        }
        public void AddForceAtPosition(Vector2 force, Vector2 position) => AddForceAtPosition(force, position, ForceMode2D.Force);
        public void AddTorque(float torque, ForceMode2D mode)
        {
            var b = B; if (b == null || !b.IsDynamic) return;
            if (mode == ForceMode2D.Impulse) b.W += b.InvI * torque; else b.Torque += torque;
            b.SetAwake(true);
        }
        public void AddTorque(float torque) => AddTorque(torque, ForceMode2D.Force);

        Xf BodyXf { get { var b = B; return b != null ? b.Xf : new Xf(transform.position, transform.eulerAngles.z * Mathf.Deg2Rad); } }
        public Vector2 GetPoint(Vector2 point) => BodyXf.MulT(point);
        public Vector2 GetRelativePoint(Vector2 relativePoint) => BodyXf.Mul(relativePoint);
        public Vector2 GetVector(Vector2 vector) => BodyXf.q.MulT(vector);
        public Vector2 GetRelativeVector(Vector2 relativeVector) => BodyXf.q.Mul(relativeVector);
        public Vector2 GetPointVelocity(Vector2 point) => B?.PointVelocity(point) ?? Vector2.zero;
        public Vector2 GetRelativePointVelocity(Vector2 relativePoint) => GetPointVelocity(GetRelativePoint(relativePoint));

        // ---------------------------------------------------------------- queries
        internal IEnumerable<Collider2D> AttachedColliders()
        {
            var b = B; if (b == null) return Enumerable.Empty<Collider2D>();
            return b.Fixtures.Select(f => (Collider2D)f.Owner).Distinct();
        }
        public int GetAttachedColliders(Collider2D[] results) => Physics2D.ToBuf(AttachedColliders().ToList(), results);
        public int GetAttachedColliders(List<Collider2D> results) => Physics2D.ToList(AttachedColliders().ToList(), results);
        public bool IsTouching(Collider2D collider) => AttachedColliders().Any(c => c.IsTouching(collider));
        public bool IsTouching(Collider2D collider, ContactFilter2D contactFilter) => AttachedColliders().Any(c => c.IsTouching(collider, contactFilter));
        public bool IsTouching(ContactFilter2D contactFilter) => AttachedColliders().Any(c => c.IsTouching(contactFilter));
        public bool IsTouchingLayers() => IsTouchingLayers(Physics2D.AllLayers);
        public bool IsTouchingLayers(int layerMask) => AttachedColliders().Any(c => c.IsTouchingLayers(layerMask));
        public bool OverlapPoint(Vector2 point) => AttachedColliders().Any(c => c.OverlapPoint(point));
        public ColliderDistance2D Distance(Collider2D collider)
        {
            var best = new ColliderDistance2D(); bool any = false;
            foreach (var c in AttachedColliders()) { var d = Physics2DWorld.Distance(c, collider); if (d.isValid && (!any || d.distance < best.distance)) { best = d; any = true; } }
            return best;
        }
        public Vector2 ClosestPoint(Vector2 position)
        {
            Vector2 best = position; float bd = float.MaxValue;
            foreach (var c in AttachedColliders()) { var p = c.ClosestPoint(position); float d = (p - position).sqrMagnitude; if (d < bd) { bd = d; best = p; } }
            return best;
        }
        public int OverlapCollider(ContactFilter2D contactFilter, Collider2D[] results) => Physics2D.ToBuf(OverlapAll(contactFilter), results);
        public int OverlapCollider(ContactFilter2D contactFilter, List<Collider2D> results) => Physics2D.ToList(OverlapAll(contactFilter), results);
        List<Collider2D> OverlapAll(ContactFilter2D f)
        {
            var res = new List<Collider2D>();
            foreach (var c in AttachedColliders()) foreach (var o in Physics2D.OverlapColliderQ(c, f)) if (!res.Contains(o) && o.attachedRigidbody != this) res.Add(o);
            return res;
        }
        List<ContactPoint2D> ContactPoints(ContactFilter2D f) { var l = new List<ContactPoint2D>(); foreach (var c in AttachedColliders()) l.AddRange(Physics2D.ContactPointsQ(c, null, f)); return l; }
        List<Collider2D> ContactColliders(ContactFilter2D f) { var l = new List<Collider2D>(); foreach (var c in AttachedColliders()) foreach (var o in Physics2D.ContactCollidersQ(c, f)) if (!l.Contains(o)) l.Add(o); return l; }
        public int GetContacts(ContactPoint2D[] contacts) => Physics2D.ToBuf(ContactPoints(new ContactFilter2D().NoFilter()), contacts);
        public int GetContacts(List<ContactPoint2D> contacts) => Physics2D.ToList(ContactPoints(new ContactFilter2D().NoFilter()), contacts);
        public int GetContacts(ContactFilter2D contactFilter, ContactPoint2D[] contacts) => Physics2D.ToBuf(ContactPoints(contactFilter), contacts);
        public int GetContacts(ContactFilter2D contactFilter, List<ContactPoint2D> contacts) => Physics2D.ToList(ContactPoints(contactFilter), contacts);
        public int GetContacts(Collider2D[] colliders) => Physics2D.ToBuf(ContactColliders(new ContactFilter2D().NoFilter()), colliders);
        public int GetContacts(List<Collider2D> colliders) => Physics2D.ToList(ContactColliders(new ContactFilter2D().NoFilter()), colliders);
        public int GetContacts(ContactFilter2D contactFilter, Collider2D[] colliders) => Physics2D.ToBuf(ContactColliders(contactFilter), colliders);
        public int GetContacts(ContactFilter2D contactFilter, List<Collider2D> colliders) => Physics2D.ToList(ContactColliders(contactFilter), colliders);

        List<RaycastHit2D> CastAll(Vector2 direction, ContactFilter2D f, float distance)
        {
            var b = B; if (b == null) return new List<RaycastHit2D>();
            var q = b.Fixtures.Where(x => !x.Sensor).Select(x => (x.Shape, b.Xf)).ToList();
            if (q.Count == 0) return new List<RaycastHit2D>();
            return Physics2DWorld.Cast(q, direction, distance, f, b).Select(RaycastHit2D.From).ToList();
        }
        static ContactFilter2D CastFilter() => ContactFilter2D.CreateLegacyFilter(Physics2D.AllLayers, float.NegativeInfinity, float.PositiveInfinity);
        public int Cast(Vector2 direction, RaycastHit2D[] results) => Physics2D.ToBuf(CastAll(direction, CastFilter(), float.PositiveInfinity), results);
        public int Cast(Vector2 direction, RaycastHit2D[] results, float distance) => Physics2D.ToBuf(CastAll(direction, CastFilter(), distance), results);
        public int Cast(Vector2 direction, List<RaycastHit2D> results, float distance = float.PositiveInfinity) => Physics2D.ToList(CastAll(direction, CastFilter(), distance), results);
        public int Cast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results) => Physics2D.ToBuf(CastAll(direction, contactFilter, float.PositiveInfinity), results);
        public int Cast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results, float distance) => Physics2D.ToBuf(CastAll(direction, contactFilter, distance), results);
        public int Cast(Vector2 direction, ContactFilter2D contactFilter, List<RaycastHit2D> results, float distance) => Physics2D.ToList(CastAll(direction, contactFilter, distance), results);
    }

    // ====================================================================================================== Collider2D
    public partial class Collider2D
    {
        public Collider2D() { }
        internal int m_IncludeLayers => NInt("m_IncludeLayers.m_Bits");
        internal int m_ExcludeLayers => NInt("m_ExcludeLayers.m_Bits");
        public float density { get => NFloat("m_Density", 1); set { NSetF("m_Density", value); } }
        public bool isTrigger { get => NBool("m_IsTrigger"); set => NSetB("m_IsTrigger", value); }
        public bool usedByEffector { get => NBool("m_UsedByEffector"); set => NSetB("m_UsedByEffector", value); }
        public bool usedByComposite { get => NBool("m_UsedByComposite"); set => NSetB("m_UsedByComposite", value); }
        public CompositeCollider2D composite
        {
            get
            {
                if (!usedByComposite) return null;
                var rb = attachedRigidbody; if (rb == null) return null;
                var c = rb.GetComponent<CompositeCollider2D>();
                return c != null && c.enabled ? c : null;
            }
        }
        public Vector2 offset { get => NVec2("m_Offset"); set => NSetVec2("m_Offset", value); }
        public Rigidbody2D attachedRigidbody { get { for (var t = transform; t != null; t = t.m_Parent) { var rb = t.gameObject.GetComponent<Rigidbody2D>(); if (rb != null) return rb; } return null; } }
        public int shapeCount { get { Physics2DWorld.PrepareQuery(); return Physics2DWorld.FixturesOf(this).Count(); } }
        public Bounds bounds
        {
            get
            {
                Physics2DWorld.PrepareQuery();
                var fs = Physics2DWorld.FixturesOf(this).ToList();
                if (fs.Count == 0) return new Bounds(transform.position, Vector3.zero);
                var bb = fs[0].Shape.ComputeAABB(fs[0].Body.Xf);
                foreach (var f in fs.Skip(1)) bb = AABB2.Union(bb, f.Shape.ComputeAABB(f.Body.Xf));
                // Unity reports the collider bounds without the polygon skin radius
                float skin = fs[0].Shape.Type == ShapeType.Polygon && !fs[0].Shape.IsCapsule ? Physics2DWorld.DefaultContactOffset : 0;
                var lo = bb.lo + new Vector2(skin, skin); var hi = bb.hi - new Vector2(skin, skin);
                float z = transform.position.z;
                var b = new Bounds(); b.SetMinMax(new Vector3(lo.x, lo.y, z), new Vector3(hi.x, hi.y, z)); return b;
            }
        }
        public ColliderErrorState2D errorState => ColliderErrorState2D.None;
        internal bool compositeCapable => this is BoxCollider2D or PolygonCollider2D or CircleCollider2D or CapsuleCollider2D or EdgeCollider2D;
        public PhysicsMaterial2D sharedMaterial { get => NRef<PhysicsMaterial2D>("m_Material"); set => NSetRef("m_Material", value); }
        public int layerOverridePriority { get => NInt("m_LayerOverridePriority"); set => NSetI("m_LayerOverridePriority", value); }
        public LayerMask excludeLayers { get => m_ExcludeLayers; set => NSetI("m_ExcludeLayers.m_Bits", value.value); }
        public LayerMask includeLayers { get => m_IncludeLayers; set => NSetI("m_IncludeLayers.m_Bits", value.value); }
        public LayerMask forceSendLayers { get => NGet("m_ForceSendLayers.m_Bits") != null ? NInt("m_ForceSendLayers.m_Bits") : -1; set => NSetI("m_ForceSendLayers.m_Bits", value.value); }
        public LayerMask forceReceiveLayers { get => NGet("m_ForceReceiveLayers.m_Bits") != null ? NInt("m_ForceReceiveLayers.m_Bits") : -1; set => NSetI("m_ForceReceiveLayers.m_Bits", value.value); }
        public LayerMask contactCaptureLayers { get => NGet("m_ContactCaptureLayers.m_Bits") != null ? NInt("m_ContactCaptureLayers.m_Bits") : -1; set => NSetI("m_ContactCaptureLayers.m_Bits", value.value); }
        public LayerMask callbackLayers { get => NGet("m_CallbackLayers.m_Bits") != null ? NInt("m_CallbackLayers.m_Bits") : -1; set => NSetI("m_CallbackLayers.m_Bits", value.value); }
        public float friction { get { var m = sharedMaterial ?? attachedRigidbody?.sharedMaterial ?? Physics2DWorld.DefaultMaterial; return m != null ? m.friction : 0.4f; } }
        public float bounciness { get { var m = sharedMaterial ?? attachedRigidbody?.sharedMaterial ?? Physics2DWorld.DefaultMaterial; return m != null ? m.bounciness : 0; } }
        public uint GetShapeHash() => (uint)ShapeKey().GetHashCode();

        /// <summary>Key of the collider geometry (fixtures are rebuilt when it changes).</summary>
        internal virtual string ShapeKey() => GetType().Name + offset;
        /// <summary>Box2D shapes in the collider's local frame (Transform position + z rotation), scale applied.</summary>
        internal virtual IEnumerable<Shape2> BuildShapes(Vector2 scale, float contactOffset) => Enumerable.Empty<Shape2>();
        internal static Vector2 Abs(Vector2 v) => new(Math.Abs(v.x), Math.Abs(v.y));

        public bool IsTouching(Collider2D collider) => IsTouching(collider, new ContactFilter2D().NoFilter());
        public bool IsTouching(Collider2D collider, ContactFilter2D contactFilter)
        {
            Physics2DWorld.PrepareQuery();
            return Physics2DWorld.ContactsOf(this).TryGetValue(collider, out var l) && Physics2D.PassContact(collider, l, this, contactFilter);
        }
        public bool IsTouching(ContactFilter2D contactFilter)
        {
            Physics2DWorld.PrepareQuery();
            foreach (var (o, l) in Physics2DWorld.ContactsOf(this)) if (Physics2D.PassContact(o, l, this, contactFilter)) return true;
            return false;
        }
        public bool IsTouchingLayers() => IsTouchingLayers(Physics2D.AllLayers);
        public bool IsTouchingLayers(int layerMask)
        {
            Physics2DWorld.PrepareQuery();
            foreach (var o in Physics2DWorld.ContactsOf(this).Keys) if ((layerMask & (1 << o.gameObject.layer)) != 0) return true;
            return false;
        }
        public bool OverlapPoint(Vector2 point)
        {
            Physics2DWorld.PrepareQuery();
            foreach (var f in Physics2DWorld.FixturesOf(this)) if (f.Shape.TestPoint(f.Body.Xf, point)) return true;
            return false;
        }
        public ColliderDistance2D Distance(Collider2D collider) => Physics2DWorld.Distance(this, collider);
        public int OverlapCollider(ContactFilter2D contactFilter, Collider2D[] results) => Physics2D.ToBuf(Physics2D.OverlapColliderQ(this, contactFilter), results);
        public int OverlapCollider(ContactFilter2D contactFilter, List<Collider2D> results) => Physics2D.ToList(Physics2D.OverlapColliderQ(this, contactFilter), results);
        public int GetContacts(ContactPoint2D[] contacts) => Physics2D.ToBuf(Physics2D.ContactPointsQ(this, null, new ContactFilter2D().NoFilter()), contacts);
        public int GetContacts(List<ContactPoint2D> contacts) => Physics2D.ToList(Physics2D.ContactPointsQ(this, null, new ContactFilter2D().NoFilter()), contacts);
        public int GetContacts(ContactFilter2D contactFilter, ContactPoint2D[] contacts) => Physics2D.ToBuf(Physics2D.ContactPointsQ(this, null, contactFilter), contacts);
        public int GetContacts(ContactFilter2D contactFilter, List<ContactPoint2D> contacts) => Physics2D.ToList(Physics2D.ContactPointsQ(this, null, contactFilter), contacts);
        public int GetContacts(Collider2D[] colliders) => Physics2D.ToBuf(Physics2D.ContactCollidersQ(this, new ContactFilter2D().NoFilter()), colliders);
        public int GetContacts(List<Collider2D> colliders) => Physics2D.ToList(Physics2D.ContactCollidersQ(this, new ContactFilter2D().NoFilter()), colliders);
        public int GetContacts(ContactFilter2D contactFilter, Collider2D[] colliders) => Physics2D.ToBuf(Physics2D.ContactCollidersQ(this, contactFilter), colliders);
        public int GetContacts(ContactFilter2D contactFilter, List<Collider2D> colliders) => Physics2D.ToList(Physics2D.ContactCollidersQ(this, contactFilter), colliders);

        List<RaycastHit2D> CastAll(Vector2 direction, ContactFilter2D f, float distance, bool ignoreSiblings)
        {
            Physics2DWorld.PrepareQuery();
            var fs = Physics2DWorld.FixturesOf(this).ToList();
            if (fs.Count == 0) return new List<RaycastHit2D>();
            var q = fs.Select(x => (x.Shape, x.Body.Xf)).ToList();
            var body = fs[0].Body;
            var hits = Physics2DWorld.Cast(q, direction, distance, f, ignoreSiblings && body != Physics2DWorld.Ground ? body : null);
            return hits.Where(h => h.Col != this).Select(RaycastHit2D.From).ToList();
        }
        static ContactFilter2D DefaultCastFilter() => ContactFilter2D.CreateLegacyFilter(Physics2D.AllLayers, float.NegativeInfinity, float.PositiveInfinity);
        public int Cast(Vector2 direction, RaycastHit2D[] results) => Physics2D.ToBuf(CastAll(direction, DefaultCastFilter(), float.PositiveInfinity, true), results);
        public int Cast(Vector2 direction, RaycastHit2D[] results, float distance) => Physics2D.ToBuf(CastAll(direction, DefaultCastFilter(), distance, true), results);
        public int Cast(Vector2 direction, RaycastHit2D[] results, float distance, bool ignoreSiblingColliders) => Physics2D.ToBuf(CastAll(direction, DefaultCastFilter(), distance, ignoreSiblingColliders), results);
        public int Cast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results) => Physics2D.ToBuf(CastAll(direction, contactFilter, float.PositiveInfinity, true), results);
        public int Cast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results, float distance) => Physics2D.ToBuf(CastAll(direction, contactFilter, distance, true), results);
        public int Cast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results, float distance, bool ignoreSiblingColliders) => Physics2D.ToBuf(CastAll(direction, contactFilter, distance, ignoreSiblingColliders), results);
        public int Cast(Vector2 direction, ContactFilter2D contactFilter, List<RaycastHit2D> results, float distance = float.PositiveInfinity, bool ignoreSiblingColliders = true) => Physics2D.ToList(CastAll(direction, contactFilter, distance, ignoreSiblingColliders), results);

        List<RaycastHit2D> RayAll(Vector2 direction, ContactFilter2D f, float distance)
        {
            var origin = (Vector2)transform.TransformPoint(offset);
            return Physics2DWorld.Raycast(origin, direction, distance, f).Where(h => h.Col != this).Select(RaycastHit2D.From).ToList();
        }
        public int Raycast(Vector2 direction, RaycastHit2D[] results) => Raycast(direction, results, float.PositiveInfinity, Physics2D.AllLayers, float.NegativeInfinity, float.PositiveInfinity);
        public int Raycast(Vector2 direction, RaycastHit2D[] results, float distance) => Raycast(direction, results, distance, Physics2D.AllLayers, float.NegativeInfinity, float.PositiveInfinity);
        public int Raycast(Vector2 direction, RaycastHit2D[] results, float distance, int layerMask) => Raycast(direction, results, distance, layerMask, float.NegativeInfinity, float.PositiveInfinity);
        public int Raycast(Vector2 direction, RaycastHit2D[] results, float distance, int layerMask, float minDepth) => Raycast(direction, results, distance, layerMask, minDepth, float.PositiveInfinity);
        public int Raycast(Vector2 direction, RaycastHit2D[] results, float distance, int layerMask, float minDepth, float maxDepth) => Physics2D.ToBuf(RayAll(direction, ContactFilter2D.CreateLegacyFilter(layerMask, minDepth, maxDepth), distance), results);
        public int Raycast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results) => Physics2D.ToBuf(RayAll(direction, contactFilter, float.PositiveInfinity), results);
        public int Raycast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results, float distance) => Physics2D.ToBuf(RayAll(direction, contactFilter, distance), results);
        public int Raycast(Vector2 direction, ContactFilter2D contactFilter, List<RaycastHit2D> results, float distance = float.PositiveInfinity) => Physics2D.ToList(RayAll(direction, contactFilter, distance), results);

        public Vector2 ClosestPoint(Vector2 position)
        {
            Physics2DWorld.PrepareQuery();
            var best = position; float bd = float.MaxValue;
            var pt = Shape2.Circle(Vector2.zero, 0);
            foreach (var f in Physics2DWorld.FixturesOf(this))
            {
                if (f.Shape.TestPoint(f.Body.Xf, position)) return position;
                float d = Gjk2.ShapeDistance(pt, new Xf(position, 0), f.Shape, f.Body.Xf, out _, out var pB);
                var dir = d > 1e-9f ? (position - pB) / d : Vector2.zero;
                // Unity reports the point on the collider surface (polygon skin excluded)
                float skin = f.Shape.Type == ShapeType.Polygon && !f.Shape.IsCapsule ? Physics2DWorld.DefaultContactOffset : 0;
                var surf = pB + dir * (f.Shape.Radius - skin);
                float dd = (surf - position).sqrMagnitude;
                if (dd < bd) { bd = dd; best = surf; }
            }
            return best;
        }
        public Mesh CreateMesh(bool useBodyPosition, bool useBodyRotation) => null;
    }

    public partial class BoxCollider2D
    {
        public BoxCollider2D() { }
        public Vector2 size { get => NGet("m_Size") != null ? NVec2("m_Size") : Vector2.one; set => NSetVec2("m_Size", value); }
        public float edgeRadius { get => NFloat("m_EdgeRadius"); set => NSetF("m_EdgeRadius", Math.Max(0, value)); }
        public bool autoTiling { get => NBool("m_AutoTiling"); set => NSetB("m_AutoTiling", value); }
        internal override string ShapeKey() => $"box{size}{offset}{edgeRadius}";
        internal override IEnumerable<Shape2> BuildShapes(Vector2 scale, float contactOffset)
        {
            var s = Abs(scale); var h = Vector2.Max(Vector2.Scale(size, s) * 0.5f, new Vector2(1e-4f, 1e-4f));
            float r = edgeRadius * Math.Max(s.x, s.y);
            yield return Shape2.Box(h.x, h.y, Vector2.Scale(offset, scale), 0, r + contactOffset);
        }
    }

    public partial class CircleCollider2D
    {
        public CircleCollider2D() { }
        public float radius { get => NFloat("m_Radius", 0.5f); set => NSetF("m_Radius", Math.Max(1e-4f, value)); }
        internal override string ShapeKey() => $"circle{radius}{offset}";
        internal override IEnumerable<Shape2> BuildShapes(Vector2 scale, float contactOffset)
        {
            var s = Abs(scale);
            yield return Shape2.Circle(Vector2.Scale(offset, scale), Math.Max(1e-4f, radius * Math.Max(s.x, s.y)));
        }
    }

    public partial class CapsuleCollider2D
    {
        public CapsuleCollider2D() { }
        public Vector2 size { get => NGet("m_Size") != null ? NVec2("m_Size") : new Vector2(0.5f, 1); set => NSetVec2("m_Size", value); }
        public CapsuleDirection2D direction { get => (CapsuleDirection2D)NInt("m_Direction"); set => NSetI("m_Direction", (int)value); }
        internal override string ShapeKey() => $"capsule{size}{direction}{offset}";
        internal override IEnumerable<Shape2> BuildShapes(Vector2 scale, float contactOffset)
        {
            var sz = Vector2.Max(Vector2.Scale(size, Abs(scale)), new Vector2(2e-4f, 2e-4f)); var c = Vector2.Scale(offset, scale);
            yield return Physics2D.CapsuleShape(c, sz, direction, 0);
        }
    }

    public partial class EdgeCollider2D
    {
        public EdgeCollider2D() { }
        public float edgeRadius { get => NFloat("m_EdgeRadius"); set => NSetF("m_EdgeRadius", Math.Max(0, value)); }
        public Vector2[] points
        {
            get { var l = Serializer.ReadV2List(NGet("m_Points")); return l.Count >= 2 ? l.ToArray() : new[] { new Vector2(-0.5f, 0), new Vector2(0.5f, 0) }; }
            set { if (value == null || value.Length < 2) return; NSet("m_Points", Serializer.V2List(value)); }
        }
        public int pointCount => points.Length;
        public int edgeCount => points.Length - 1;
        public bool useAdjacentStartPoint { get => NBool("m_UseAdjacentStartPoint"); set => NSetB("m_UseAdjacentStartPoint", value); }
        public bool useAdjacentEndPoint { get => NBool("m_UseAdjacentEndPoint"); set => NSetB("m_UseAdjacentEndPoint", value); }
        public Vector2 adjacentStartPoint { get => NVec2("m_AdjacentStartPoint"); set => NSetVec2("m_AdjacentStartPoint", value); }
        public Vector2 adjacentEndPoint { get => NVec2("m_AdjacentEndPoint"); set => NSetVec2("m_AdjacentEndPoint", value); }
        public void Reset() { points = new[] { new Vector2(-0.5f, 0), new Vector2(0.5f, 0) }; }
        public int GetPoints(List<Vector2> points) { points.Clear(); points.AddRange(this.points); return points.Count; }
        public bool SetPoints(List<Vector2> points) { if (points == null || points.Count < 2) return false; this.points = points.ToArray(); return true; }
        internal override string ShapeKey() => "edge" + string.Join(";", points) + offset + edgeRadius;
        internal override IEnumerable<Shape2> BuildShapes(Vector2 scale, float contactOffset)
        {
            var ps = points.Select(p => Vector2.Scale(p + offset, scale)).ToArray();
            var s = Abs(scale); float r = edgeRadius * Math.Max(s.x, s.y) + contactOffset;
            for (int i = 0; i + 1 < ps.Length; i++)
            {
                if ((ps[i + 1] - ps[i]).sqrMagnitude < 1e-10f) continue;
                var e = Shape2.Edge(ps[i], ps[i + 1], r);
                e.V0 = i > 0 ? ps[i - 1] : useAdjacentStartPoint ? Vector2.Scale(adjacentStartPoint + offset, scale) : ps[i];
                e.V3 = i + 2 < ps.Length ? ps[i + 2] : useAdjacentEndPoint ? Vector2.Scale(adjacentEndPoint + offset, scale) : ps[i + 1];
                yield return e;
            }
        }
    }

    public partial class PolygonCollider2D
    {
        public PolygonCollider2D() { }
        List<List<Vector2>> Paths()
        {
            var res = new List<List<Vector2>>();
            if (NGet("m_Points.m_Paths") is YSeq seq) foreach (var p in seq.Items) res.Add(Serializer.ReadV2List(p));
            if (res.Count == 0 && NGet("m_Points.m_Paths") == null) res.Add(new List<Vector2> { new(-0.5f, -0.5f), new(0.5f, -0.5f), new(0.5f, 0.5f), new(-0.5f, 0.5f) });
            return res;
        }
        void SetPaths(List<List<Vector2>> paths)
        {
            var seq = new YSeq();
            foreach (var p in paths) seq.Items.Add(Serializer.V2List(p));
            NSet("m_Points.m_Paths", seq);
        }
        public bool useDelaunayMesh { get => NBool("m_UseDelaunayMesh"); set => NSetB("m_UseDelaunayMesh", value); }
        public bool autoTiling { get => NBool("m_AutoTiling"); set => NSetB("m_AutoTiling", value); }
        public Vector2[] points { get => Paths().FirstOrDefault()?.ToArray() ?? Array.Empty<Vector2>(); set { var p = Paths(); if (p.Count == 0) p.Add(new()); p[0] = value.ToList(); SetPaths(p); } }
        public int pathCount
        {
            get => Paths().Count;
            set { var p = Paths(); while (p.Count < value) p.Add(new()); if (p.Count > value) p.RemoveRange(value, p.Count - value); SetPaths(p); }
        }
        public int GetTotalPointCount() => Paths().Sum(p => p.Count);
        public Vector2[] GetPath(int index)
        {
            var p = Paths(); if (index < 0 || index >= p.Count) throw new ArgumentOutOfRangeException(nameof(index), $"Path {index} does not exist.");
            return p[index].ToArray();
        }
        public void SetPath(int index, Vector2[] points)
        {
            var p = Paths(); while (p.Count <= index) p.Add(new());
            p[index] = points.ToList(); SetPaths(p);
        }
        public int GetPath(int index, List<Vector2> points) { points.Clear(); points.AddRange(GetPath(index)); return points.Count; }
        public void SetPath(int index, List<Vector2> points) => SetPath(index, points.ToArray());
        public void CreatePrimitive(int sides) => CreatePrimitive(sides, Vector2.one, Vector2.zero);
        public void CreatePrimitive(int sides, Vector2 scale) => CreatePrimitive(sides, scale, Vector2.zero);
        public void CreatePrimitive(int sides, Vector2 scale, Vector2 offset)
        {
            if (sides < 3) { Debug.LogWarning("Cannot create a 2D polygon primitive collider with less than three sides."); return; }
            var pts = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = 2 * MathF.PI * i / sides + MathF.PI / 2;
                pts[i] = new Vector2(MathF.Cos(a) * scale.x * 0.5f, MathF.Sin(a) * scale.y * 0.5f) + offset;
            }
            SetPaths(new List<List<Vector2>> { pts.ToList() });
        }
        internal override string ShapeKey() => "poly" + string.Join("|", Paths().Select(p => string.Join(";", p))) + offset;
        internal override IEnumerable<Shape2> BuildShapes(Vector2 scale, float contactOffset)
        {
            foreach (var path in Paths())
            {
                var ps = path.Select(p => Vector2.Scale(p + offset, scale)).ToList();
                foreach (var piece in Decompose.ConvexPieces(ps))
                {
                    var s = Shape2.Polygon(piece, contactOffset);
                    if (s != null) yield return s;
                }
            }
        }
    }

    public partial class CompositeCollider2D
    {
        public CompositeCollider2D() { }
        public GeometryType geometryType { get => (GeometryType)NInt("m_GeometryType"); set => NSetI("m_GeometryType", (int)value); }
        public GenerationType generationType { get => (GenerationType)NInt("m_GenerationType"); set => NSetI("m_GenerationType", (int)value); }
        public bool useDelaunayMesh { get => NBool("m_UseDelaunayMesh"); set => NSetB("m_UseDelaunayMesh", value); }
        public float vertexDistance { get => NFloat("m_VertexDistance", 0.0005f); set => NSetF("m_VertexDistance", value); }
        public float edgeRadius { get => NFloat("m_EdgeRadius"); set => NSetF("m_EdgeRadius", value); }
        public float offsetDistance { get => NFloat("m_OffsetDistance", 5e-5f); set => NSetF("m_OffsetDistance", value); }
        // the welded outline of the source colliders (Box, Polygon, Tilemap) in this collider's space, rebuilt when they change
        string m_PathsKey; List<List<Vector2>> m_Paths;
        internal List<List<Vector2>> MergedPaths()
        {
            var key = ShapeKey();
            if (m_Paths != null && m_PathsKey == key) return m_Paths;
            var polys = new List<IList<Vector2>>();
            foreach (var c in Sources())
                if (SourceOutlines(c) is List<List<Vector2>> outs)
                    foreach (var o in outs) polys.Add(o.Select(v => (Vector2)transform.InverseTransformPoint(c.transform.TransformPoint(v))).ToList());
            m_Paths = PolygonUnion.Outline(polys, Math.Max(vertexDistance, 1e-6f));
            m_PathsKey = key;
            return m_Paths;
        }
        /// <summary>Outlines a source contributes to the merge (its own space); null: kept as its own shapes (circles, capsules, edges).</summary>
        static List<List<Vector2>> SourceOutlines(Collider2D c)
        {
            switch (c)
            {
                case BoxCollider2D b:
                {
                    var h = b.size * 0.5f; var o = b.offset;
                    return new List<List<Vector2>> { new() { o + new Vector2(-h.x, -h.y), o + new Vector2(h.x, -h.y), o + new Vector2(h.x, h.y), o + new Vector2(-h.x, h.y) } };
                }
                case PolygonCollider2D p:
                {
                    var res = new List<List<Vector2>>();
                    for (int i = 0; i < p.pathCount; i++) res.Add(p.GetPath(i).Select(v => v + p.offset).ToList());
                    return res;
                }
                case UnityEngine.Tilemaps.TilemapCollider2D t: return t.CellOutlines();
                default: return null;
            }
        }

        public int pathCount => MergedPaths().Count;
        public int pointCount => MergedPaths().Sum(p => p.Count);
        public void GenerateGeometry() { m_Paths = null; Physics2DWorld.Sync(); }
        public int GetPathPointCount(int index) { var ps = MergedPaths(); return index >= 0 && index < ps.Count ? ps[index].Count : 0; }
        public int GetPath(int index, Vector2[] points)
        {
            var ps = MergedPaths();
            if (index < 0 || index >= ps.Count) throw new ArgumentOutOfRangeException(nameof(index), $"Path index {index} must be in the range of 0 to {ps.Count - 1}.");
            if (points == null) throw new ArgumentNullException(nameof(points));
            int n = Math.Min(points.Length, ps[index].Count);
            for (int i = 0; i < n; i++) points[i] = ps[index][i];
            return n;
        }
        public int GetPath(int index, List<Vector2> points)
        {
            var ps = MergedPaths();
            if (index < 0 || index >= ps.Count) throw new ArgumentOutOfRangeException(nameof(index), $"Path index {index} must be in the range of 0 to {ps.Count - 1}.");
            if (points == null) throw new ArgumentNullException(nameof(points));
            points.Clear(); points.AddRange(ps[index]);
            return points.Count;
        }

        IEnumerable<Collider2D> Sources()
        {
            var rb = attachedRigidbody;
            var root = rb != null ? rb.gameObject : gameObject;
            foreach (var g in Hier.Walk(root))
            {
                if (!g.activeInHierarchy) continue;
                foreach (var c in g.m_Components) if (c is Collider2D col && col != this && !col.m_Destroyed && col.enabled && col.usedByComposite) yield return col;
            }
        }
        internal override string ShapeKey()
        {
            return $"composite{geometryType}{edgeRadius}{vertexDistance}" + string.Join("|", Sources().Select(c => c.ShapeKey() + c.transform.position + c.transform.eulerAngles.z + c.transform.lossyScale)) + transform.position + transform.eulerAngles.z + transform.lossyScale;
        }
        internal override IEnumerable<Shape2> BuildShapes(Vector2 scale, float contactOffset)
        {
            var s = Abs(scale); float r = edgeRadius * Math.Max(s.x, s.y) + contactOffset;
            var paths = MergedPaths().Select(p => p.Select(v => Vector2.Scale(v, scale)).ToList()).ToList();
            if (scale.x * scale.y < 0) foreach (var p in paths) p.Reverse();   // mirrored: keep outer boundaries counter-clockwise
            if (geometryType == GeometryType.Outlines)
            {
                // closed chains of edges with ghost vertices: bodies slide over the welded seams without catching on them
                foreach (var p in paths)
                    for (int i = 0; i < p.Count; i++)
                    {
                        var e = Shape2.Edge(p[i], p[(i + 1) % p.Count], r);
                        e.V0 = p[(i - 1 + p.Count) % p.Count]; e.V3 = p[(i + 2) % p.Count];
                        yield return e;
                    }
            }
            else
            {
                var outers = paths.Where(p => PolygonUnion.SignedArea(p) > 0).ToList();
                var holes = paths.Where(p => PolygonUnion.SignedArea(p) < 0).ToList();
                foreach (var outer in outers)
                {
                    var mine = holes.Where(h => Inside(outer, h[0])).ToList();
                    foreach (var piece in PolygonUnion.ConvexPieces(mine.Count > 0 ? PolygonUnion.Bridge(outer, mine) : outer))
                    {
                        var sh = Shape2.Polygon(piece, r);
                        if (sh != null) yield return sh;
                    }
                }
            }
            // sources that are not merged (circles, capsules, edges) keep their own shapes, in this collider's frame
            var me = new Xf(transform.position, transform.eulerAngles.z * Mathf.Deg2Rad);
            foreach (var c in Sources())
            {
                if (SourceOutlines(c) != null) continue;
                var rel = Xf.MulT(me, new Xf(c.transform.position, c.transform.eulerAngles.z * Mathf.Deg2Rad));
                foreach (var sh in c.BuildShapes(c.transform.lossyScale, contactOffset)) yield return sh.Transformed(rel);
            }
        }
        static bool Inside(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            return inside;
        }
    }

    public partial class CustomCollider2D
    {
        public CustomCollider2D() { }
        public int customShapeCount => 0;
        public int customVertexCount => 0;
    }

    // ====================================================================================================== joints
    public partial class Joint2D
    {
        public Joint2D() { }
        public Rigidbody2D attachedRigidbody => GetComponent<Rigidbody2D>();
        public Rigidbody2D connectedBody { get => NRef<Rigidbody2D>("m_ConnectedRigidBody"); set { NSetRef("m_ConnectedRigidBody", value); } }
        public bool enableCollision { get => NBool("m_EnableCollision"); set => NSetB("m_EnableCollision", value); }
        public float breakForce { get => NFloat("m_BreakForce", float.PositiveInfinity); set => NSetF("m_BreakForce", value); }
        public float breakTorque { get => NFloat("m_BreakTorque", float.PositiveInfinity); set => NSetF("m_BreakTorque", value); }
        public JointBreakAction2D breakAction { get => (JointBreakAction2D)NInt("m_BreakAction", (int)JointBreakAction2D.Destroy); set => NSetI("m_BreakAction", (int)value); }
        public Vector2 reactionForce => GetReactionForce(Time.fixedDeltaTime);
        public float reactionTorque => GetReactionTorque(Time.fixedDeltaTime);
        public Vector2 GetReactionForce(float timeStep) { var j = Physics2DWorld.JointOf(this); return j != null && timeStep > 0 ? j.ReactionForce(1 / timeStep) : Vector2.zero; }
        public float GetReactionTorque(float timeStep) { var j = Physics2DWorld.JointOf(this); return j != null && timeStep > 0 ? j.ReactionTorque(1 / timeStep) : 0; }
        internal virtual bool SelfIsBodyA => false;
        internal virtual Joint2 CreateBox2DJoint(Body2 a, Body2 b) => null;
        internal virtual void UpdateBox2DJoint(Joint2 j) { }
        internal Vector2 LocalAnchorOnSelf(Vector2 anchor) => Vector2.Scale(anchor, (Vector2)transform.lossyScale);
        internal Vector2 LocalAnchorOnConnected(Vector2 connectedAnchor)
        {
            var cb = connectedBody;
            return cb != null ? Vector2.Scale(connectedAnchor, (Vector2)cb.transform.lossyScale) : connectedAnchor;
        }
    }

    public partial class AnchoredJoint2D
    {
        public AnchoredJoint2D() { }
        public Vector2 anchor { get => NVec2("m_Anchor"); set => NSetVec2("m_Anchor", value); }
        public Vector2 connectedAnchor { get => NVec2("m_ConnectedAnchor"); set => NSetVec2("m_ConnectedAnchor", value); }
        public bool autoConfigureConnectedAnchor { get => NBool("m_AutoConfigureConnectedAnchor", true); set => NSetB("m_AutoConfigureConnectedAnchor", value); }

        /// <summary>Anchors in body-local frames (A = connected body / ground, B = own body); auto-configures the connected anchor.</summary>
        internal void Anchors(AnchoredJoint2 j)
        {
            Body2 self = SelfIsBodyA ? j.A : j.B, other = SelfIsBodyA ? j.B : j.A;
            var selfLocal = LocalAnchorOnSelf(anchor);
            Vector2 otherLocal;
            if (autoConfigureConnectedAnchor && !j.AnchorConfigured)
            {
                // once, when the joint is created: the connected anchor sits where this joint's anchor is
                otherLocal = other.Xf.MulT(self.Xf.Mul(selfLocal));
                var cb = connectedBody;
                var s = cb != null ? (Vector2)cb.transform.lossyScale : Vector2.one;
                NSetVec2("m_ConnectedAnchor", new Vector2(s.x != 0 ? otherLocal.x / s.x : 0, s.y != 0 ? otherLocal.y / s.y : 0));
                j.AnchorConfigured = true;
            }
            else otherLocal = LocalAnchorOnConnected(connectedAnchor);
            if (SelfIsBodyA) { j.LocalAnchorA = selfLocal; j.LocalAnchorB = otherLocal; }
            else { j.LocalAnchorA = otherLocal; j.LocalAnchorB = selfLocal; }
        }
    }

    public partial class HingeJoint2D
    {
        public HingeJoint2D() { }
        public bool useMotor { get => NBool("m_UseMotor"); set => NSetB("m_UseMotor", value); }
        public bool useLimits { get => NBool("m_UseLimits"); set => NSetB("m_UseLimits", value); }
        public JointMotor2D motor
        {
            get => new() { m_MotorSpeed = NFloat("m_Motor.m_MotorSpeed"), m_MaximumMotorTorque = NFloat("m_Motor.m_MaximumMotorForce", 10000) };
            set { NSetF("m_Motor.m_MotorSpeed", value.motorSpeed); NSetF("m_Motor.m_MaximumMotorForce", value.maxMotorTorque); }
        }
        public JointAngleLimits2D limits
        {
            get => new() { m_LowerAngle = NFloat("m_AngleLimits.m_LowerAngle"), m_UpperAngle = NFloat("m_AngleLimits.m_UpperAngle", 359) };
            set { NSetF("m_AngleLimits.m_LowerAngle", value.min); NSetF("m_AngleLimits.m_UpperAngle", value.max); }
        }
        RevoluteJoint2 J => Physics2DWorld.JointOf(this) as RevoluteJoint2;
        public JointLimitState2D limitState
        {
            get
            {
                var j = J; if (j == null || !useLimits) return JointLimitState2D.Inactive;
                float a = j.JointAngle * Mathf.Rad2Deg, lo = limits.min, hi = limits.max;
                if (Math.Abs(hi - lo) < 2 * B2.AngularSlop * Mathf.Rad2Deg) return JointLimitState2D.EqualLimits;
                if (a <= lo) return JointLimitState2D.LowerLimit;
                if (a >= hi) return JointLimitState2D.UpperLimit;
                return JointLimitState2D.Inactive;
            }
        }
        public float referenceAngle => (J?.ReferenceAngle ?? 0) * Mathf.Rad2Deg;
        public float jointAngle => (J?.JointAngle ?? 0) * Mathf.Rad2Deg;
        public float jointSpeed => (J?.JointSpeed ?? 0) * Mathf.Rad2Deg;
        public float GetMotorTorque(float timeStep) => timeStep > 0 ? (J?.MotorImpulse ?? 0) / timeStep : 0;
        internal override Joint2 CreateBox2DJoint(Body2 a, Body2 b) => new RevoluteJoint2 { ReferenceAngle = b.A - a.A };
        internal override void UpdateBox2DJoint(Joint2 jj)
        {
            var j = (RevoluteJoint2)jj; Anchors(j);
            var m = motor; var l = limits;
            j.EnableMotor = useMotor; j.MotorSpeed = m.motorSpeed * Mathf.Deg2Rad; j.MaxMotorTorque = m.maxMotorTorque;
            j.EnableLimit = useLimits; j.LowerAngle = Math.Min(l.min, l.max) * Mathf.Deg2Rad; j.UpperAngle = Math.Max(l.min, l.max) * Mathf.Deg2Rad;
        }
    }

    public partial class DistanceJoint2D
    {
        public DistanceJoint2D() { }
        public bool autoConfigureDistance { get => NBool("m_AutoConfigureDistance", true); set => NSetB("m_AutoConfigureDistance", value); }
        public float distance { get => NFloat("m_Distance", 1); set => NSetF("m_Distance", Math.Max(0.005f, value)); }
        public bool maxDistanceOnly { get => NBool("m_MaxDistanceOnly"); set => NSetB("m_MaxDistanceOnly", value); }
        internal override Joint2 CreateBox2DJoint(Body2 a, Body2 b) => new DistanceJoint2();
        internal override void UpdateBox2DJoint(Joint2 jj)
        {
            var j = (DistanceJoint2)jj; Anchors(j);
            if (autoConfigureDistance && !configured) { distance = Math.Max(0.005f, (j.WorldAnchorB - j.WorldAnchorA).magnitude); configured = true; }
            j.Length = distance; j.MaxLength = distance; j.MinLength = maxDistanceOnly ? B2.LinearSlop : distance; j.Stiffness = 0; j.Damping = 0;
        }
        bool configured;
    }

    public partial class SpringJoint2D
    {
        public SpringJoint2D() { }
        public bool autoConfigureDistance { get => NBool("m_AutoConfigureDistance", true); set => NSetB("m_AutoConfigureDistance", value); }
        public float distance { get => NFloat("m_Distance", 1); set => NSetF("m_Distance", Math.Max(0.005f, value)); }
        public float dampingRatio { get => NFloat("m_DampingRatio"); set => NSetF("m_DampingRatio", Mathf.Clamp01(value)); }
        public float frequency { get => NFloat("m_Frequency", 1); set => NSetF("m_Frequency", Math.Max(0, value)); }
        bool configured;
        internal override Joint2 CreateBox2DJoint(Body2 a, Body2 b) => new DistanceJoint2();
        internal override void UpdateBox2DJoint(Joint2 jj)
        {
            var j = (DistanceJoint2)jj; Anchors(j);
            if (autoConfigureDistance && !configured) { distance = Math.Max(0.005f, (j.WorldAnchorB - j.WorldAnchorA).magnitude); configured = true; }
            j.Length = distance;
            if (frequency <= 0) { j.MinLength = j.MaxLength = distance; j.Stiffness = 0; j.Damping = 0; }
            else
            {
                j.MinLength = B2.LinearSlop; j.MaxLength = float.MaxValue;
                AnchoredJoint2.LinearStiffness(out j.Stiffness, out j.Damping, frequency, dampingRatio, j.A, j.B);
            }
        }
    }

    public partial class FrictionJoint2D
    {
        public FrictionJoint2D() { }
        public float maxForce { get => NFloat("m_MaxForce", 1); set => NSetF("m_MaxForce", Math.Max(0, value)); }
        public float maxTorque { get => NFloat("m_MaxTorque", 1); set => NSetF("m_MaxTorque", Math.Max(0, value)); }
        internal override Joint2 CreateBox2DJoint(Body2 a, Body2 b) => new FrictionJoint2();
        internal override void UpdateBox2DJoint(Joint2 jj) { var j = (FrictionJoint2)jj; Anchors(j); j.MaxForce = maxForce; j.MaxTorque = maxTorque; }
    }

    public partial class FixedJoint2D
    {
        public FixedJoint2D() { }
        public float dampingRatio { get => NFloat("m_DampingRatio"); set => NSetF("m_DampingRatio", Mathf.Clamp01(value)); }
        public float frequency { get => NFloat("m_Frequency"); set => NSetF("m_Frequency", Math.Max(0, value)); }
        public float referenceAngle => ((Physics2DWorld.JointOf(this) as WeldJoint2)?.ReferenceAngle ?? 0) * Mathf.Rad2Deg;
        internal override Joint2 CreateBox2DJoint(Body2 a, Body2 b) => new WeldJoint2 { ReferenceAngle = b.A - a.A };
        internal override void UpdateBox2DJoint(Joint2 jj)
        {
            var j = (WeldJoint2)jj; Anchors(j);
            if (frequency > 0) AnchoredJoint2.AngularStiffness(out j.Stiffness, out j.Damping, frequency, dampingRatio, j.A, j.B);
            else { j.Stiffness = 0; j.Damping = 0; }
        }
    }

    public partial class SliderJoint2D
    {
        public SliderJoint2D() { }
        public bool autoConfigureAngle { get => NBool("m_AutoConfigureAngle", true); set => NSetB("m_AutoConfigureAngle", value); }
        public float angle { get => NFloat("m_Angle"); set => NSetF("m_Angle", value); }
        public bool useMotor { get => NBool("m_UseMotor"); set => NSetB("m_UseMotor", value); }
        public bool useLimits { get => NBool("m_UseLimits"); set => NSetB("m_UseLimits", value); }
        public JointMotor2D motor
        {
            get => new() { m_MotorSpeed = NFloat("m_Motor.m_MotorSpeed"), m_MaximumMotorTorque = NFloat("m_Motor.m_MaximumMotorForce", 10000) };
            set { NSetF("m_Motor.m_MotorSpeed", value.motorSpeed); NSetF("m_Motor.m_MaximumMotorForce", value.maxMotorTorque); }
        }
        public JointTranslationLimits2D limits
        {
            get => new() { m_LowerTranslation = NFloat("m_TranslationLimits.m_LowerTranslation"), m_UpperTranslation = NFloat("m_TranslationLimits.m_UpperTranslation") };
            set { NSetF("m_TranslationLimits.m_LowerTranslation", value.min); NSetF("m_TranslationLimits.m_UpperTranslation", value.max); }
        }
        PrismaticJoint2 J => Physics2DWorld.JointOf(this) as PrismaticJoint2;
        public JointLimitState2D limitState
        {
            get
            {
                var j = J; if (j == null || !useLimits) return JointLimitState2D.Inactive;
                float t = j.JointTranslation, lo = limits.min, hi = limits.max;
                if (Math.Abs(hi - lo) < 2 * B2.LinearSlop) return JointLimitState2D.EqualLimits;
                if (t <= lo) return JointLimitState2D.LowerLimit;
                if (t >= hi) return JointLimitState2D.UpperLimit;
                return JointLimitState2D.Inactive;
            }
        }
        public float referenceAngle => (J?.ReferenceAngle ?? 0) * Mathf.Rad2Deg;
        public float jointTranslation => J?.JointTranslation ?? 0;
        public float jointSpeed => J?.JointSpeed ?? 0;
        public float GetMotorForce(float timeStep) => timeStep > 0 ? (J?.MotorImpulse ?? 0) / timeStep : 0;
        bool configured;
        internal override Joint2 CreateBox2DJoint(Body2 a, Body2 b) => new PrismaticJoint2 { ReferenceAngle = b.A - a.A };
        internal override void UpdateBox2DJoint(Joint2 jj)
        {
            var j = (PrismaticJoint2)jj; Anchors(j);
            if (autoConfigureAngle && !configured)
            {
                var d = j.WorldAnchorB - j.A.Xf.Mul(j.LocalAnchorA);
                d = d.sqrMagnitude > 1e-10f ? d : (Vector2)(j.B.Xf.p - j.A.Xf.p);
                if (d.sqrMagnitude > 1e-10f) angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                configured = true;
            }
            float a = angle * Mathf.Deg2Rad;
            j.LocalXAxisA = j.A.Xf.q.MulT(new Vector2(MathF.Cos(a), MathF.Sin(a)));
            var m = motor; var l = limits;
            j.EnableMotor = useMotor; j.MotorSpeed = m.motorSpeed; j.MaxMotorForce = m.maxMotorTorque;
            j.EnableLimit = useLimits; j.Lower = Math.Min(l.min, l.max); j.Upper = Math.Max(l.min, l.max);
        }
    }

    public partial class WheelJoint2D
    {
        public WheelJoint2D() { }
        public JointSuspension2D suspension
        {
            get => new() { m_DampingRatio = NFloat("m_Suspension.m_DampingRatio", 0.7f), m_Frequency = NFloat("m_Suspension.m_Frequency", 2), m_Angle = NFloat("m_Suspension.m_Angle", 90) };
            set { NSetF("m_Suspension.m_DampingRatio", value.dampingRatio); NSetF("m_Suspension.m_Frequency", value.frequency); NSetF("m_Suspension.m_Angle", value.angle); }
        }
        public bool useMotor { get => NBool("m_UseMotor"); set => NSetB("m_UseMotor", value); }
        public JointMotor2D motor
        {
            get => new() { m_MotorSpeed = NFloat("m_Motor.m_MotorSpeed"), m_MaximumMotorTorque = NFloat("m_Motor.m_MaximumMotorForce", 10000) };
            set { NSetF("m_Motor.m_MotorSpeed", value.motorSpeed); NSetF("m_Motor.m_MaximumMotorForce", value.maxMotorTorque); }
        }
        internal override bool SelfIsBodyA => true;
        WheelJoint2 J => Physics2DWorld.JointOf(this) as WheelJoint2;
        public float jointTranslation => J?.JointTranslation ?? 0;
        public float jointLinearSpeed => J?.JointLinearSpeed ?? 0;
        public float jointSpeed => (J?.JointAngularSpeed ?? 0) * Mathf.Rad2Deg;
        public float jointAngle => (J?.JointAngle ?? 0) * Mathf.Rad2Deg;
        public float GetMotorTorque(float timeStep) => timeStep > 0 ? (J?.MotorImpulse ?? 0) / timeStep : 0;
        internal override Joint2 CreateBox2DJoint(Body2 a, Body2 b) => new WheelJoint2();
        internal override void UpdateBox2DJoint(Joint2 jj)
        {
            var j = (WheelJoint2)jj; Anchors(j);
            var s = suspension; float a = s.angle * Mathf.Deg2Rad;
            j.LocalXAxisA = j.A.Xf.q.MulT(new Vector2(MathF.Cos(a), MathF.Sin(a)));
            if (s.frequency > 0) AnchoredJoint2.LinearStiffness(out j.Stiffness, out j.Damping, s.frequency, s.dampingRatio, j.A, j.B);
            else { j.Stiffness = 0; j.Damping = 0; }
            var m = motor;
            j.EnableMotor = useMotor; j.MotorSpeed = m.motorSpeed * Mathf.Deg2Rad; j.MaxMotorTorque = m.maxMotorTorque;
        }
    }

    public partial class TargetJoint2D
    {
        public TargetJoint2D() { }
        public Vector2 anchor { get => NVec2("m_Anchor"); set => NSetVec2("m_Anchor", value); }
        public Vector2 target { get => NVec2("m_Target"); set { NSetVec2("m_Target", value); configured = true; } }
        public bool autoConfigureTarget { get => NBool("m_AutoConfigureTarget", true); set => NSetB("m_AutoConfigureTarget", value); }
        public float maxForce { get => NFloat("m_MaxForce", 1000); set => NSetF("m_MaxForce", Math.Max(0, value)); }
        public float dampingRatio { get => NFloat("m_DampingRatio", 1); set => NSetF("m_DampingRatio", Mathf.Clamp01(value)); }
        public float frequency { get => NFloat("m_Frequency", 5); set => NSetF("m_Frequency", Math.Max(0, value)); }
        bool configured;
        internal override Joint2 CreateBox2DJoint(Body2 a, Body2 b) => new MouseJoint2();
        internal override void UpdateBox2DJoint(Joint2 jj)
        {
            var j = (MouseJoint2)jj;
            j.LocalAnchorB = LocalAnchorOnSelf(anchor);
            if (autoConfigureTarget && !configured) { target = j.B.Xf.Mul(j.LocalAnchorB); configured = true; }
            if (j.Target != target) j.B.SetAwake(true);
            j.Target = target; j.MaxForce = maxForce;
            AnchoredJoint2.LinearStiffness(out j.Stiffness, out j.Damping, frequency, dampingRatio, j.A, j.B);
        }
    }

    public partial class RelativeJoint2D
    {
        public RelativeJoint2D() { }
        public float maxForce { get => NFloat("m_MaxForce", 10000); set => NSetF("m_MaxForce", Math.Max(0, value)); }
        public float maxTorque { get => NFloat("m_MaxTorque", 10000); set => NSetF("m_MaxTorque", Math.Max(0, value)); }
        public float correctionScale { get => NFloat("m_CorrectionScale", 0.3f); set => NSetF("m_CorrectionScale", Mathf.Clamp01(value)); }
        public bool autoConfigureOffset { get => NBool("m_AutoConfigureOffset", true); set => NSetB("m_AutoConfigureOffset", value); }
        public Vector2 linearOffset { get => NVec2("m_LinearOffset"); set => NSetVec2("m_LinearOffset", value); }
        public float angularOffset { get => NFloat("m_AngularOffset"); set => NSetF("m_AngularOffset", value); }
        public Vector2 target { get { var j = Physics2DWorld.JointOf(this); return j != null ? j.A.Xf.Mul(((MotorJoint2)j).LinearOffset) : Vector2.zero; } }
        bool configured;
        internal override Joint2 CreateBox2DJoint(Body2 a, Body2 b) => new MotorJoint2();
        internal override void UpdateBox2DJoint(Joint2 jj)
        {
            var j = (MotorJoint2)jj;
            if (autoConfigureOffset && !configured) { linearOffset = j.A.Xf.MulT(j.B.Xf.p); angularOffset = (j.B.A - j.A.A) * Mathf.Rad2Deg; configured = true; }
            j.LinearOffset = linearOffset; j.AngularOffset = angularOffset * Mathf.Deg2Rad;
            j.MaxForce = maxForce; j.MaxTorque = maxTorque; j.CorrectionFactor = correctionScale;
        }
    }

    // ====================================================================================================== effectors / misc components
    public partial class Effector2D
    {
        public Effector2D() { }
        public bool useColliderMask { get => NBool("m_UseColliderMask", true); set => NSetB("m_UseColliderMask", value); }
        public int colliderMask { get => NGet("m_ColliderMask.m_Bits") != null ? NInt("m_ColliderMask.m_Bits") : -1; set => NSetI("m_ColliderMask.m_Bits", value); }
        internal bool requiresCollider => true;
        internal bool designedForTrigger => this is AreaEffector2D or PointEffector2D or BuoyancyEffector2D;
        internal bool designedForNonTrigger => this is PlatformEffector2D or SurfaceEffector2D;
    }
    public partial class AreaEffector2D
    {
        public AreaEffector2D() { }
        public float forceAngle { get => NFloat("m_ForceAngle"); set => NSetF("m_ForceAngle", value); }
        public bool useGlobalAngle { get => NBool("m_UseGlobalAngle"); set => NSetB("m_UseGlobalAngle", value); }
        public float forceMagnitude { get => NFloat("m_ForceMagnitude"); set => NSetF("m_ForceMagnitude", value); }
        public float forceVariation { get => NFloat("m_ForceVariation"); set => NSetF("m_ForceVariation", value); }
        public float drag { get => NFloat("m_Drag"); set => NSetF("m_Drag", value); }
        public float angularDrag { get => NFloat("m_AngularDrag"); set => NSetF("m_AngularDrag", value); }
        public EffectorSelection2D forceTarget { get => (EffectorSelection2D)NInt("m_ForceTarget", 1); set => NSetI("m_ForceTarget", (int)value); }
    }
    public partial class BuoyancyEffector2D
    {
        public BuoyancyEffector2D() { }
        public float surfaceLevel { get => NFloat("m_SurfaceLevel"); set => NSetF("m_SurfaceLevel", value); }
        public float density { get => NFloat("m_Density", 2); set => NSetF("m_Density", value); }
        public float linearDrag { get => NFloat("m_LinearDrag", 1); set => NSetF("m_LinearDrag", value); }
        public float angularDrag { get => NFloat("m_AngularDrag", 1); set => NSetF("m_AngularDrag", value); }
        public float flowAngle { get => NFloat("m_FlowAngle"); set => NSetF("m_FlowAngle", value); }
        public float flowMagnitude { get => NFloat("m_FlowMagnitude"); set => NSetF("m_FlowMagnitude", value); }
        public float flowVariation { get => NFloat("m_FlowVariation"); set => NSetF("m_FlowVariation", value); }
    }
    public partial class PointEffector2D
    {
        public PointEffector2D() { }
        public float forceMagnitude { get => NFloat("m_ForceMagnitude"); set => NSetF("m_ForceMagnitude", value); }
        public float forceVariation { get => NFloat("m_ForceVariation"); set => NSetF("m_ForceVariation", value); }
        public float distanceScale { get => NFloat("m_DistanceScale", 1); set => NSetF("m_DistanceScale", value); }
        public float drag { get => NFloat("m_Drag"); set => NSetF("m_Drag", value); }
        public float angularDrag { get => NFloat("m_AngularDrag"); set => NSetF("m_AngularDrag", value); }
        public EffectorSelection2D forceSource { get => (EffectorSelection2D)NInt("m_ForceSource"); set => NSetI("m_ForceSource", (int)value); }
        public EffectorSelection2D forceTarget { get => (EffectorSelection2D)NInt("m_ForceTarget", 1); set => NSetI("m_ForceTarget", (int)value); }
        public EffectorForceMode2D forceMode { get => (EffectorForceMode2D)NInt("m_ForceMode"); set => NSetI("m_ForceMode", (int)value); }
    }
    public partial class PlatformEffector2D
    {
        public PlatformEffector2D() { }
        public bool useOneWay { get => NBool("m_UseOneWay", true); set => NSetB("m_UseOneWay", value); }
        public bool useOneWayGrouping { get => NBool("m_UseOneWayGrouping"); set => NSetB("m_UseOneWayGrouping", value); }
        public bool useSideFriction { get => NBool("m_UseSideFriction"); set => NSetB("m_UseSideFriction", value); }
        public bool useSideBounce { get => NBool("m_UseSideBounce"); set => NSetB("m_UseSideBounce", value); }
        public float surfaceArc { get => NFloat("m_SurfaceArc", 180); set => NSetF("m_SurfaceArc", value); }
        public float sideArc { get => NFloat("m_SideArc", 1); set => NSetF("m_SideArc", value); }
        public float rotationalOffset { get => NFloat("m_RotationalOffset"); set => NSetF("m_RotationalOffset", value); }
    }
    public partial class SurfaceEffector2D
    {
        public SurfaceEffector2D() { }
        public float speed { get => NFloat("m_Speed"); set => NSetF("m_Speed", value); }
        public float speedVariation { get => NFloat("m_SpeedVariation"); set => NSetF("m_SpeedVariation", value); }
        public float forceScale { get => NFloat("m_ForceScale", 0.1f); set => NSetF("m_ForceScale", value); }
        public bool useContactForce { get => NBool("m_UseContactForce"); set => NSetB("m_UseContactForce", value); }
        public bool useFriction { get => NBool("m_UseFriction"); set => NSetB("m_UseFriction", value); }
        public bool useBounce { get => NBool("m_UseBounce"); set => NSetB("m_UseBounce", value); }
    }
    public partial class PhysicsUpdateBehaviour2D { public PhysicsUpdateBehaviour2D() { } }
    public partial class ConstantForce2D
    {
        public ConstantForce2D() { }
        public Vector2 force { get => NVec2("m_Force"); set => NSetVec2("m_Force", value); }
        public Vector2 relativeForce { get => NVec2("m_RelativeForce"); set => NSetVec2("m_RelativeForce", value); }
        public float torque { get => NFloat("m_Torque"); set => NSetF("m_Torque", value); }
    }
}

namespace MiniUnity.Box2D
{
    /// <summary>Simple polygon → convex pieces of at most 8 vertices (ear clipping, then Hertel–Mehlhorn merging).</summary>
    public static class Decompose
    {
        static float Area(IList<Vector2> p) { float a = 0; for (int i = 0; i < p.Count; i++) a += B2.Cross(p[i], p[(i + 1) % p.Count]); return a * 0.5f; }

        public static List<List<Vector2>> ConvexPieces(List<Vector2> poly)
        {
            var pts = new List<Vector2>();
            foreach (var p in poly) if (pts.Count == 0 || (pts[^1] - p).sqrMagnitude > 1e-10f) pts.Add(p);
            if (pts.Count > 1 && (pts[0] - pts[^1]).sqrMagnitude <= 1e-10f) pts.RemoveAt(pts.Count - 1);
            if (pts.Count < 3) return new();
            if (Area(pts) < 0) pts.Reverse();
            if (IsConvex(pts) && pts.Count <= B2.MaxPolygonVertices) return new() { pts };
            // ear clipping
            var idx = Enumerable.Range(0, pts.Count).ToList();
            var tris = new List<int[]>();
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int a = idx[(i + idx.Count - 1) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                    if (B2.Cross(pts[b] - pts[a], pts[c] - pts[b]) <= 1e-9f) continue;
                    bool inside = false;
                    foreach (int o in idx) if (o != a && o != b && o != c && InTri(pts[o], pts[a], pts[b], pts[c])) { inside = true; break; }
                    if (inside) continue;
                    tris.Add(new[] { a, b, c }); idx.RemoveAt(i); clipped = true; break;
                }
                if (!clipped) { // degenerate input: drop a collinear vertex
                    idx.RemoveAt(0);
                }
            }
            if (idx.Count == 3) tris.Add(idx.ToArray());
            // Hertel–Mehlhorn: merge neighbouring pieces while the result stays convex and small enough
            var pieces = tris.Select(t => t.ToList()).ToList();
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < pieces.Count && !merged; i++)
                    for (int j = i + 1; j < pieces.Count && !merged; j++)
                    {
                        var m = Merge(pieces[i], pieces[j]);
                        if (m == null || m.Count > B2.MaxPolygonVertices || !IsConvex(m.Select(k => pts[k]).ToList())) continue;
                        pieces[i] = m; pieces.RemoveAt(j); merged = true;
                    }
            }
            return pieces.Select(p => p.Select(k => pts[k]).ToList()).ToList();
        }

        static List<int> Merge(List<int> a, List<int> b)
        {
            for (int i = 0; i < a.Count; i++)
            {
                int a0 = a[i], a1 = a[(i + 1) % a.Count];
                for (int j = 0; j < b.Count; j++)
                {
                    int b0 = b[j], b1 = b[(j + 1) % b.Count];
                    if (a0 != b1 || a1 != b0) continue;
                    var res = new List<int>();
                    for (int k = 0; k < a.Count; k++) res.Add(a[(i + 1 + k) % a.Count]);     // a1 … a0
                    for (int k = 2; k < b.Count; k++) res.Add(b[(j + k) % b.Count]);         // b's other vertices
                    return res;
                }
            }
            return null;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
            => B2.Cross(b - a, p - a) >= 0 && B2.Cross(c - b, p - b) >= 0 && B2.Cross(a - c, p - c) >= 0;

        static bool IsConvex(IList<Vector2> p)
        {
            for (int i = 0; i < p.Count; i++)
                if (B2.Cross(p[(i + 1) % p.Count] - p[i], p[(i + 2) % p.Count] - p[(i + 1) % p.Count]) < -1e-9f) return false;
            return true;
        }
    }
}
