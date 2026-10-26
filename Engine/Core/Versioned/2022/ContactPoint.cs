// Collider ids of a contact: instance ids in Unity 2022.3, EntityIds in Unity 6 (Versioned/6000/EntityId.cs).
namespace UnityEngine
{
    public partial struct ContactPoint
    {
        // the collider ids are instance ids here and EntityIds in Unity 6 (Versioned/6000)
        internal int ThisColliderId { get => m_ThisColliderInstanceID; set => m_ThisColliderInstanceID = value; }
        internal int OtherColliderId { get => m_OtherColliderInstanceID; set => m_OtherColliderInstanceID = value; }
    }
}
