// Unity 6 renamed PhysicMaterial → PhysicsMaterial and PhysicMaterialCombine → PhysicsMaterialCombine (class ID 134 is unchanged).
// The engine code keeps the 2022.3 names; against Unity 6 stubs they are aliases of the new types.
#if UNITY_6000_0_OR_NEWER
global using PhysicMaterial = UnityEngine.PhysicsMaterial;
global using PhysicMaterialCombine = UnityEngine.PhysicsMaterialCombine;
#endif

namespace MiniUnity
{
    /// <summary>Serialized class names that differ between the Unity versions the engine builds against.</summary>
    public static class UnityVersionNames
    {
#if UNITY_6000_0_OR_NEWER
        public const string PhysicMaterial = "PhysicsMaterial";
#else
        public const string PhysicMaterial = "PhysicMaterial";
#endif
    }
}
