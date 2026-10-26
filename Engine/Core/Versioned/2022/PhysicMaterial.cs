// Unity 2022.3 names of the 3D physics material (Unity 6: PhysicsMaterial, Versioned/6000). Core code says PhysicMaterial and
// PhysicMaterialCombine throughout; on Unity 6 those names are aliases (Versioned/6000/Unity6Names.cs).
using MiniUnity;

namespace UnityEngine
{
    public partial class PhysicMaterial
    {
        public PhysicMaterial() { m_Name = "DynamicMaterial"; }
        public PhysicMaterial(string name) { m_Name = name; }
        public float bounciness { get => NFloat("bounciness", 0); set => NSetF("bounciness", value); }
        public float dynamicFriction { get => NFloat("dynamicFriction", 0.6f); set => NSetF("dynamicFriction", value); }
        public float staticFriction { get => NFloat("staticFriction", 0.6f); set => NSetF("staticFriction", value); }
        public PhysicMaterialCombine frictionCombine { get => (PhysicMaterialCombine)NInt("frictionCombine"); set => NSetI("frictionCombine", (int)value); }
        public PhysicMaterialCombine bounceCombine { get => (PhysicMaterialCombine)NInt("bounceCombine"); set => NSetI("bounceCombine", (int)value); }
        public float bouncyness { get => bounciness; set => bounciness = value; }
    }

    public partial struct JointDrive
    {
        public JointDriveMode mode { get => JointDriveMode.None; set { } }   // removed in Unity 6
    }
}
