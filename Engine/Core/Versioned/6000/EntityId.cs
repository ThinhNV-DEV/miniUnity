// Unity 6.2 introduced EntityId (and the transitional InstanceID) where APIs used to take an int instance id. Both wrap the id
// MiniUnity already gives every Object (Object.m_InstanceID) and convert to and from int, as in Unity 6.3.
namespace UnityEngine
{
    public partial struct EntityId
    {
        int m_Data;
        public override bool Equals(object obj) => obj is EntityId e && e.m_Data == m_Data;
        public bool Equals(EntityId other) => other.m_Data == m_Data;
        public bool Equals(int other) => m_Data == other;
        public int CompareTo(EntityId other) => m_Data.CompareTo(other.m_Data);
        public static bool operator ==(EntityId left, EntityId right) => left.m_Data == right.m_Data;
        public static bool operator !=(EntityId left, EntityId right) => left.m_Data != right.m_Data;
        public static bool operator <(EntityId left, EntityId right) => left.m_Data < right.m_Data;
        public static bool operator >(EntityId left, EntityId right) => left.m_Data > right.m_Data;
        public static bool operator <=(EntityId left, EntityId right) => left.m_Data <= right.m_Data;
        public static bool operator >=(EntityId left, EntityId right) => left.m_Data >= right.m_Data;
        public override int GetHashCode() => m_Data;
        public bool IsValid() => m_Data != 0;
        public static implicit operator int(EntityId entityId) => entityId.m_Data;
        public static implicit operator EntityId(int intValue) => new() { m_Data = intValue };
        public static implicit operator EntityId(InstanceID entityId) => new() { m_Data = (int)entityId };
        public static implicit operator InstanceID(EntityId entityId) => entityId.m_Data;
        public override string ToString() => m_Data.ToString();
        public string ToString(string format) => m_Data.ToString(format);
        internal static EntityId From(int input) => input;
        internal static EntityId From(ulong input) => (int)input;
        internal static EntityId Parse(string input) => int.Parse(input);
        internal int GetRawData() => m_Data;
        public static EntityId None => default;
    }

    public partial struct InstanceID
    {
        int m_Data;
        public override bool Equals(object obj) => obj is InstanceID e && e.m_Data == m_Data;
        public bool Equals(InstanceID other) => other.m_Data == m_Data;
        public bool Equals(int other) => m_Data == other;
        public int CompareTo(InstanceID other) => m_Data.CompareTo(other.m_Data);
        public static bool operator ==(InstanceID left, InstanceID right) => left.m_Data == right.m_Data;
        public static bool operator !=(InstanceID left, InstanceID right) => left.m_Data != right.m_Data;
        public static bool operator <(InstanceID left, InstanceID right) => left.m_Data < right.m_Data;
        public static bool operator >(InstanceID left, InstanceID right) => left.m_Data > right.m_Data;
        public static bool operator <=(InstanceID left, InstanceID right) => left.m_Data <= right.m_Data;
        public static bool operator >=(InstanceID left, InstanceID right) => left.m_Data >= right.m_Data;
        public override int GetHashCode() => m_Data;
        public bool IsValid() => m_Data != 0;
        public static implicit operator int(InstanceID entityId) => entityId.m_Data;
        public static implicit operator InstanceID(int intValue) => new() { m_Data = intValue };
        public override string ToString() => m_Data.ToString();
        public string ToString(string format) => m_Data.ToString(format);
        public static InstanceID None => default;
    }

    public partial class Object
    {
        public EntityId GetEntityId() => m_InstanceID;
    }

    public partial struct ContactPoint
    {
        internal int ThisColliderId { get => m_ThisColliderEntityId; set => m_ThisColliderEntityId = value; }
        internal int OtherColliderId { get => m_OtherColliderEntityId; set => m_OtherColliderEntityId = value; }
    }
}
