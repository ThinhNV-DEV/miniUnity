// Unity 6 extern names and signatures of the decompiled math types (Unity 2022.3: Versioned/2022/NativeMath.cs): `in` instead of
// `ref`, an Internal_ prefix, and instance externs (`readonly extern`), which tools/fixdecomp.py redirects with the struct as the
// first argument. Same behaviour as the 2022.3 side, including what it does not implement.
using UnityEngine;
namespace MiniUnity {
    public static partial class Native {
        public static bool Bounds_Internal_Contains(Bounds _unity_self, in Vector3 point) => BoundsContains(_unity_self, point);
        public static float Bounds_Internal_SqrDistance(Bounds _unity_self, in Vector3 point) => throw new System.NotImplementedException("Bounds.Internal_SqrDistance");
        public static bool Bounds_IntersectRayAABB(in Ray ray, in Bounds bounds, out float dist) => throw new System.NotImplementedException("Bounds.IntersectRayAABB");
        public static void Bounds_Internal_ClosestPoint_Injected(ref Bounds _unity_self, in Vector3 point, out Vector3 ret) => throw new System.NotImplementedException("Bounds.Internal_ClosestPoint_Injected");
        public static bool Matrix4x4_IsIdentity(Matrix4x4 _unity_self) => _unity_self == Matrix4x4.identity;
        public static float Matrix4x4_GetDeterminant(Matrix4x4 _unity_self) => MiniUnity.MathImpl.Det(_unity_self);
        public static bool Matrix4x4_ValidTRS(Matrix4x4 _unity_self) => true;
        public static void Matrix4x4_Internal_SetTRS(ref Matrix4x4 m, in Vector3 pos, in Quaternion q, in Vector3 s) { m = MiniUnity.MathImpl.TRS(pos, q, s); }
        public static bool Matrix4x4_Internal_Inverse3DAffine(in Matrix4x4 input, ref Matrix4x4 result) { result = MiniUnity.MathImpl.Inverse(input); return true; }
        public static bool Matrix4x4_Internal_CompareApproximately(in Matrix4x4 a, in Matrix4x4 b, float threshold) => throw new System.NotImplementedException("Matrix4x4.Internal_CompareApproximately");
        public static void Matrix4x4_Internal_TRS_Injected(in Vector3 pos, in Quaternion q, in Vector3 s, out Matrix4x4 ret) { ret = MiniUnity.MathImpl.TRS(pos, q, s); }
        public static void Matrix4x4_Internal_Inverse_Injected(in Matrix4x4 m, out Matrix4x4 ret) { ret = MiniUnity.MathImpl.Inverse(m); }
        public static void Matrix4x4_Internal_Transpose_Injected(in Matrix4x4 m, out Matrix4x4 ret) { ret = Transpose(m); }
        public static void Matrix4x4_Internal_LookAt_Injected(in Vector3 from, in Vector3 to, in Vector3 up, out Matrix4x4 ret) => throw new System.NotImplementedException("Matrix4x4.Internal_LookAt_Injected");
        public static void Quaternion_Internal_ToAxisAngleRad(in Quaternion q, out Vector3 axis, out float angle) => ToAxisAngleRad(q, out axis, out angle);
        public static void Quaternion_Internal_FromToRotation_Injected(in Vector3 fromDirection, in Vector3 toDirection, out Quaternion ret) { ret = MiniUnity.MathImpl.FromTo(fromDirection, toDirection); }
        public static void Quaternion_Internal_Inverse_Injected(in Quaternion rotation, out Quaternion ret) { ret = Inverse(rotation); }
        public static void Quaternion_Internal_Slerp_Injected(in Quaternion a, in Quaternion b, float t, out Quaternion ret) { ret = MiniUnity.MathImpl.Slerp(a, b, Mathf.Clamp01(t)); }
        public static void Quaternion_Internal_SlerpUnclamped_Injected(in Quaternion a, in Quaternion b, float t, out Quaternion ret) { ret = MiniUnity.MathImpl.Slerp(a, b, t); }
        public static void Quaternion_Internal_Lerp_Injected(in Quaternion a, in Quaternion b, float t, out Quaternion ret) { ret = MiniUnity.MathImpl.Lerp(a, b, Mathf.Clamp01(t)); }
        public static void Quaternion_Internal_LerpUnclamped_Injected(in Quaternion a, in Quaternion b, float t, out Quaternion ret) { ret = MiniUnity.MathImpl.Lerp(a, b, t); }
        public static void Quaternion_Internal_FromEulerRad_Injected(in Vector3 euler, out Quaternion ret) { ret = MiniUnity.MathImpl.FromEulerRad(euler); }
        public static void Quaternion_Internal_ToEulerRad_Injected(in Quaternion rotation, out Vector3 ret) { ret = MiniUnity.MathImpl.ToEulerRad(rotation); }
        public static void Quaternion_Internal_AngleAxis_Injected(float angle, in Vector3 axis, out Quaternion ret) { ret = AngleAxis(angle, axis); }
        public static void Quaternion_Internal_LookRotation_Injected(in Vector3 forward, in Vector3 upwards, out Quaternion ret) { ret = MiniUnity.MathImpl.LookRotation(forward, upwards); }
        public static void Vector3_Internal_Slerp_Injected(in Vector3 a, in Vector3 b, float t, out Vector3 ret) { ret = MiniUnity.MathImpl.SlerpV(a, b, Mathf.Clamp01(t)); }
        public static void Vector3_Internal_SlerpUnclamped_Injected(in Vector3 a, in Vector3 b, float t, out Vector3 ret) { ret = MiniUnity.MathImpl.SlerpV(a, b, t); }
        public static void Vector3_Internal_RotateTowards_Injected(in Vector3 current, in Vector3 target, float maxRadiansDelta, float maxMagnitudeDelta, out Vector3 ret) => throw new System.NotImplementedException("Vector3.Internal_RotateTowards_Injected");
    }
}
