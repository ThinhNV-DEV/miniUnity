// Unity 2022.3 extern names and signatures of the decompiled math types (Unity 6: Versioned/6000/NativeMath.cs).
using UnityEngine;
namespace MiniUnity {
    public static partial class Native {
        public static bool Bounds_Contains_Injected(ref Bounds _unity_self, ref Vector3 point) => BoundsContains(_unity_self, point);
        public static float Bounds_SqrDistance_Injected(ref Bounds _unity_self, ref Vector3 point) => throw new System.NotImplementedException("Bounds.SqrDistance_Injected");
        public static bool Bounds_IntersectRayAABB_Injected(ref Ray ray, ref Bounds bounds, out float dist) => throw new System.NotImplementedException("Bounds.IntersectRayAABB_Injected");
        public static void Bounds_ClosestPoint_Injected(ref Bounds _unity_self, ref Vector3 point, out Vector3 ret) => throw new System.NotImplementedException("Bounds.ClosestPoint_Injected");
        public static string LayerMask_LayerToName(int layer) => MiniUnity.Layers.LayerToName(layer);
        public static int LayerMask_NameToLayer(string layerName) => MiniUnity.Layers.NameToLayer(layerName);
        public static bool Matrix4x4_IsIdentity_Injected(ref Matrix4x4 _unity_self) => _unity_self == Matrix4x4.identity;
        public static float Matrix4x4_GetDeterminant_Injected(ref Matrix4x4 _unity_self) => MiniUnity.MathImpl.Det(_unity_self);
        public static bool Matrix4x4_ValidTRS_Injected(ref Matrix4x4 _unity_self) => true;
        public static void Matrix4x4_TRS_Injected(ref Vector3 pos, ref Quaternion q, ref Vector3 s, out Matrix4x4 ret) { ret = MiniUnity.MathImpl.TRS(pos,q,s); }
        public static bool Matrix4x4_Inverse3DAffine_Injected(ref Matrix4x4 input, ref Matrix4x4 result) { result = MiniUnity.MathImpl.Inverse(input); return true; }
        public static void Matrix4x4_Inverse_Injected(ref Matrix4x4 m, out Matrix4x4 ret) { ret = MiniUnity.MathImpl.Inverse(m); }
        public static void Matrix4x4_Transpose_Injected(ref Matrix4x4 m, out Matrix4x4 ret) { ret = Transpose(m); }
        public static void Matrix4x4_LookAt_Injected(ref Vector3 from, ref Vector3 to, ref Vector3 up, out Matrix4x4 ret) => throw new System.NotImplementedException("Matrix4x4.LookAt_Injected");
        public static bool Matrix4x4_CompareApproximately_Injected(ref Matrix4x4 a, ref Matrix4x4 b, float threshold) => throw new System.NotImplementedException("Matrix4x4.CompareApproximately_Injected");
        public static void Quaternion_FromToRotation_Injected(ref Vector3 fromDirection, ref Vector3 toDirection, out Quaternion ret) { ret = MiniUnity.MathImpl.FromTo(fromDirection,toDirection); }
        public static void Quaternion_Inverse_Injected(ref Quaternion rotation, out Quaternion ret) { ret = Inverse(rotation); }
        public static void Quaternion_Slerp_Injected(ref Quaternion a, ref Quaternion b, float t, out Quaternion ret) { ret = MiniUnity.MathImpl.Slerp(a,b,Mathf.Clamp01(t)); }
        public static void Quaternion_SlerpUnclamped_Injected(ref Quaternion a, ref Quaternion b, float t, out Quaternion ret) { ret = MiniUnity.MathImpl.Slerp(a,b,t); }
        public static void Quaternion_Lerp_Injected(ref Quaternion a, ref Quaternion b, float t, out Quaternion ret) { ret = MiniUnity.MathImpl.Lerp(a,b,Mathf.Clamp01(t)); }
        public static void Quaternion_LerpUnclamped_Injected(ref Quaternion a, ref Quaternion b, float t, out Quaternion ret) { ret = MiniUnity.MathImpl.Lerp(a,b,t); }
        public static void Quaternion_Internal_FromEulerRad_Injected(ref Vector3 euler, out Quaternion ret) { ret = MiniUnity.MathImpl.FromEulerRad(euler); }
        public static void Quaternion_Internal_ToEulerRad_Injected(ref Quaternion rotation, out Vector3 ret) { ret = MiniUnity.MathImpl.ToEulerRad(rotation); }
        public static void Quaternion_Internal_ToAxisAngleRad_Injected(ref Quaternion q, out Vector3 axis, out float angle) => ToAxisAngleRad(q, out axis, out angle);
        public static void Quaternion_AngleAxis_Injected(float angle, ref Vector3 axis, out Quaternion ret) { ret = AngleAxis(angle, axis); }
        public static void Quaternion_LookRotation_Injected(ref Vector3 forward, ref Vector3 upwards, out Quaternion ret) { ret = MiniUnity.MathImpl.LookRotation(forward,upwards); }
        public static void Vector3_Slerp_Injected(ref Vector3 a, ref Vector3 b, float t, out Vector3 ret) { ret = MiniUnity.MathImpl.SlerpV(a,b,Mathf.Clamp01(t)); }
        public static void Vector3_SlerpUnclamped_Injected(ref Vector3 a, ref Vector3 b, float t, out Vector3 ret) { ret = MiniUnity.MathImpl.SlerpV(a,b,t); }
        public static void Vector3_RotateTowards_Injected(ref Vector3 current, ref Vector3 target, float maxRadiansDelta, float maxMagnitudeDelta, out Vector3 ret) => throw new System.NotImplementedException("Vector3.RotateTowards_Injected");
    }
}
