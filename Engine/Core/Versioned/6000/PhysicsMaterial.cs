// Unity 6 renamed the 3D physics material: PhysicMaterial → PhysicsMaterial, PhysicMaterialCombine → PhysicsMaterialCombine
// (Unity 2022.3: Versioned/2022). Same native body as before: the serialized fields did not change.
using MiniUnity;

namespace UnityEngine
{
    public partial class PhysicsMaterial
    {
        public PhysicsMaterial() { m_Name = "DynamicMaterial"; }
        public PhysicsMaterial(string name) { m_Name = name; }
        public float bounciness { get => NFloat("bounciness", 0); set => NSetF("bounciness", value); }
        public float dynamicFriction { get => NFloat("dynamicFriction", 0.6f); set => NSetF("dynamicFriction", value); }
        public float staticFriction { get => NFloat("staticFriction", 0.6f); set => NSetF("staticFriction", value); }
        public PhysicsMaterialCombine frictionCombine { get => (PhysicsMaterialCombine)NInt("frictionCombine"); set => NSetI("frictionCombine", (int)value); }
        public PhysicsMaterialCombine bounceCombine { get => (PhysicsMaterialCombine)NInt("bounceCombine"); set => NSetI("bounceCombine", (int)value); }
    }
}
