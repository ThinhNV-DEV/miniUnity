// Unity 6 bindings of the decompiled math types (Engine/Decompiled/Math): `in` parameters, Internal_ names and instance externs
// that take the struct itself (see tools/fixdecomp.py). Each adapter forwards to the 2022.3 implementation in NativeMath.cs, so
// both Editor versions behave the same (including the members that still throw).
#if UNITY_6000_0_OR_NEWER
using UnityEngine;
namespace MiniUnity {
    public static partial class Native {
        // Bounds
        public static bool Bounds_Internal_Contains(in Bounds _unity_self, in Vector3 point) { var b = _unity_self; var p = point; return Bounds_Contains_Injected(ref b, ref p); }
        public static float Bounds_Internal_SqrDistance(in Bounds _unity_self, in Vector3 point) { var b = _unity_self; var p = point; return Bounds_SqrDistance_Injected(ref b, ref p); }
        public static bool Bounds_IntersectRayAABB(in Ray ray, in Bounds bounds, out float dist) { var r = ray; var b = bounds; return Bounds_IntersectRayAABB_Injected(ref r, ref b, out dist); }
        public static void Bounds_Internal_ClosestPoint_Injected(ref Bounds _unity_self, in Vector3 point, out Vector3 ret) { var p = point; Bounds_ClosestPoint_Injected(ref _unity_self, ref p, out ret); }

        // Matrix4x4
        public static bool Matrix4x4_IsIdentity(in Matrix4x4 _unity_self) { var m = _unity_self; return Matrix4x4_IsIdentity_Injected(ref m); }
        public static float Matrix4x4_GetDeterminant(in Matrix4x4 _unity_self) { var m = _unity_self; return Matrix4x4_GetDeterminant_Injected(ref m); }
        public static bool Matrix4x4_ValidTRS(in Matrix4x4 _unity_self) { var m = _unity_self; return Matrix4x4_ValidTRS_Injected(ref m); }
        public static void Matrix4x4_Internal_SetTRS(ref Matrix4x4 m, in Vector3 pos, in Quaternion q, in Vector3 s) { var p = pos; var r = q; var sc = s; Matrix4x4_TRS_Injected(ref p, ref r, ref sc, out m); }
        public static bool Matrix4x4_Internal_Inverse3DAffine(in Matrix4x4 input, ref Matrix4x4 result) { var m = input; return Matrix4x4_Inverse3DAffine_Injected(ref m, ref result); }
        public static bool Matrix4x4_Internal_CompareApproximately(in Matrix4x4 a, in Matrix4x4 b, float threshold) { var x = a; var y = b; return Matrix4x4_CompareApproximately_Injected(ref x, ref y, threshold); }
        public static void Matrix4x4_Internal_TRS_Injected(in Vector3 pos, in Quaternion q, in Vector3 s, out Matrix4x4 ret) { var p = pos; var r = q; var sc = s; Matrix4x4_TRS_Injected(ref p, ref r, ref sc, out ret); }
        public static void Matrix4x4_Internal_Inverse_Injected(in Matrix4x4 m, out Matrix4x4 ret) { var x = m; Matrix4x4_Inverse_Injected(ref x, out ret); }
        public static void Matrix4x4_Internal_Transpose_Injected(in Matrix4x4 m, out Matrix4x4 ret) { var x = m; Matrix4x4_Transpose_Injected(ref x, out ret); }
        public static void Matrix4x4_Internal_LookAt_Injected(in Vector3 from, in Vector3 to, in Vector3 up, out Matrix4x4 ret) { var f = from; var t = to; var u = up; Matrix4x4_LookAt_Injected(ref f, ref t, ref u, out ret); }

        // Quaternion
        public static void Quaternion_Internal_ToAxisAngleRad(in Quaternion q, out Vector3 axis, out float angle) { var x = q; Quaternion_Internal_ToAxisAngleRad_Injected(ref x, out axis, out angle); }
        public static void Quaternion_Internal_FromToRotation_Injected(in Vector3 fromDirection, in Vector3 toDirection, out Quaternion ret) { var a = fromDirection; var b = toDirection; Quaternion_FromToRotation_Injected(ref a, ref b, out ret); }
        public static void Quaternion_Internal_Inverse_Injected(in Quaternion rotation, out Quaternion ret) { var q = rotation; Quaternion_Inverse_Injected(ref q, out ret); }
        public static void Quaternion_Internal_Slerp_Injected(in Quaternion a, in Quaternion b, float t, out Quaternion ret) { var x = a; var y = b; Quaternion_Slerp_Injected(ref x, ref y, t, out ret); }
        public static void Quaternion_Internal_SlerpUnclamped_Injected(in Quaternion a, in Quaternion b, float t, out Quaternion ret) { var x = a; var y = b; Quaternion_SlerpUnclamped_Injected(ref x, ref y, t, out ret); }
        public static void Quaternion_Internal_Lerp_Injected(in Quaternion a, in Quaternion b, float t, out Quaternion ret) { var x = a; var y = b; Quaternion_Lerp_Injected(ref x, ref y, t, out ret); }
        public static void Quaternion_Internal_LerpUnclamped_Injected(in Quaternion a, in Quaternion b, float t, out Quaternion ret) { var x = a; var y = b; Quaternion_LerpUnclamped_Injected(ref x, ref y, t, out ret); }
        public static void Quaternion_Internal_FromEulerRad_Injected(in Vector3 euler, out Quaternion ret) { ret = MiniUnity.MathImpl.FromEulerRad(euler); }
        public static void Quaternion_Internal_ToEulerRad_Injected(in Quaternion rotation, out Vector3 ret) { ret = MiniUnity.MathImpl.ToEulerRad(rotation); }
        public static void Quaternion_Internal_AngleAxis_Injected(float angle, in Vector3 axis, out Quaternion ret) { var a = axis; Quaternion_AngleAxis_Injected(angle, ref a, out ret); }
        public static void Quaternion_Internal_LookRotation_Injected(in Vector3 forward, in Vector3 upwards, out Quaternion ret) { var f = forward; var u = upwards; Quaternion_LookRotation_Injected(ref f, ref u, out ret); }

        // Vector3
        public static void Vector3_Internal_Slerp_Injected(in Vector3 a, in Vector3 b, float t, out Vector3 ret) { var x = a; var y = b; Vector3_Slerp_Injected(ref x, ref y, t, out ret); }
        public static void Vector3_Internal_SlerpUnclamped_Injected(in Vector3 a, in Vector3 b, float t, out Vector3 ret) { var x = a; var y = b; Vector3_SlerpUnclamped_Injected(ref x, ref y, t, out ret); }
        public static void Vector3_Internal_RotateTowards_Injected(in Vector3 current, in Vector3 target, float maxRadiansDelta, float maxMagnitudeDelta, out Vector3 ret) { var c = current; var t = target; Vector3_RotateTowards_Injected(ref c, ref t, maxRadiansDelta, maxMagnitudeDelta, out ret); }
    }
}
#endif
