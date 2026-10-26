// The rest of Engine/Core is written against the Unity 2022.3 names; on Unity 6 they resolve to the renamed types. An alias is
// only found when no type of that name exists in UnityEngine, which is exactly the Unity 6 case (the 6.x DLLs carry no
// PhysicMaterial), so it can never shadow a real type.
global using PhysicMaterial = UnityEngine.PhysicsMaterial;
global using PhysicMaterialCombine = UnityEngine.PhysicsMaterialCombine;
