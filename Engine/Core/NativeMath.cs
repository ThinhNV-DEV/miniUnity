// Native side of the decompiled math types (Engine/Decompiled/Math). Externs whose name or signature changed in Unity 6 are in
// Versioned/<level>/NativeMath.cs; this file holds the ones both versions share and the implementations behind both.
using UnityEngine;
namespace MiniUnity {
    public static partial class Native {
        public static int Mathf_ClosestPowerOfTwo(int value) { int n=Mathf_NextPowerOfTwo(value); int p=n>>1; return (value-p)<(n-value)?p:n; }
        public static bool Mathf_IsPowerOfTwo(int value) => value>0 && (value & (value-1))==0;
        public static int Mathf_NextPowerOfTwo(int value) { value-=1; value|=value>>16; value|=value>>8; value|=value>>4; value|=value>>2; value|=value>>1; return value+1; }
        public static float Mathf_GammaToLinearSpace(float value) => value<=0.04045f? value/12.92f : (value<1f? (float)System.Math.Pow((value+0.055)/1.055,2.4) : (float)System.Math.Pow(value,2.2));
        public static float Mathf_LinearToGammaSpace(float value) => value<=0f?0f: value<=0.0031308f? 12.92f*value : (value<1f? 1.055f*(float)System.Math.Pow(value,0.4166667)-0.055f : (float)System.Math.Pow(value,0.45454545));
        public static ushort Mathf_FloatToHalf(float val) => System.BitConverter.HalfToUInt16Bits((System.Half)val);
        public static float Mathf_HalfToFloat(ushort val) => (float)System.BitConverter.UInt16BitsToHalf(val);
        public static float Mathf_PerlinNoise(float x, float y) => throw new System.NotImplementedException("Mathf.PerlinNoise");
        public static float Mathf_PerlinNoise1D(float x) => throw new System.NotImplementedException("Mathf.PerlinNoise1D");
        public static void Mathf_CorrelatedColorTemperatureToRGB_Injected(float kelvin, out Color ret) => throw new System.NotImplementedException("Mathf.CorrelatedColorTemperatureToRGB_Injected");
        public static void Matrix4x4_GetRotation_Injected(ref Matrix4x4 _unity_self, out Quaternion ret) { ret = MiniUnity.MathImpl.RotationFromMatrix(_unity_self); }
        public static void Matrix4x4_GetLossyScale_Injected(ref Matrix4x4 _unity_self, out Vector3 ret) { var m=_unity_self; ret = new Vector3(new Vector3(m.m00,m.m10,m.m20).magnitude, new Vector3(m.m01,m.m11,m.m21).magnitude, new Vector3(m.m02,m.m12,m.m22).magnitude); }
        public static void Matrix4x4_DecomposeProjection_Injected(ref Matrix4x4 _unity_self, out FrustumPlanes ret) => throw new System.NotImplementedException("Matrix4x4.DecomposeProjection_Injected");
        public static void Matrix4x4_Ortho_Injected(float left, float right, float bottom, float top, float zNear, float zFar, out Matrix4x4 ret) { var r=Matrix4x4.identity; r.m00=2f/(right-left); r.m11=2f/(top-bottom); r.m22=-2f/(zFar-zNear); r.m03=-(right+left)/(right-left); r.m13=-(top+bottom)/(top-bottom); r.m23=-(zFar+zNear)/(zFar-zNear); ret=r; }
        public static void Matrix4x4_Perspective_Injected(float fov, float aspect, float zNear, float zFar, out Matrix4x4 ret) { float c=1f/(float)System.Math.Tan(fov*Mathf.Deg2Rad*0.5f); var r=new Matrix4x4(); r.m00=c/aspect; r.m11=c; r.m22=-(zFar+zNear)/(zFar-zNear); r.m23=-2f*zFar*zNear/(zFar-zNear); r.m32=-1f; ret=r; }
        public static void Matrix4x4_Frustum_Injected(float left, float right, float bottom, float top, float zNear, float zFar, out Matrix4x4 ret) => throw new System.NotImplementedException("Matrix4x4.Frustum_Injected");
        public static void Vector3_OrthoNormalize2(ref Vector3 a, ref Vector3 b) => throw new System.NotImplementedException("Vector3.OrthoNormalize2");
        public static void Vector3_OrthoNormalize3(ref Vector3 a, ref Vector3 b, ref Vector3 c) => throw new System.NotImplementedException("Vector3.OrthoNormalize3");
        // ---- implementations shared by the 2022.3 and the Unity 6 bindings (Versioned/<level>/NativeMath.cs hold the extern names)
        internal static bool BoundsContains(Bounds b, Vector3 point) { var mn=b.min; var mx=b.max; return point.x>=mn.x&&point.y>=mn.y&&point.z>=mn.z&&point.x<=mx.x&&point.y<=mx.y&&point.z<=mx.z; }
        internal static Matrix4x4 Transpose(Matrix4x4 m) { var r=new Matrix4x4(); for(int i=0;i<4;i++) for(int j=0;j<4;j++) r[i,j]=m[j,i]; return r; }
        internal static Quaternion Inverse(Quaternion q) { float n=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w; if(n<=0) return q; return new Quaternion(-q.x/n,-q.y/n,-q.z/n,q.w/n); }
        internal static void ToAxisAngleRad(Quaternion q, out Vector3 axis, out float angle) { var n=Quaternion.Normalize(q); angle=2f*(float)System.Math.Acos(Mathf.Clamp(n.w,-1,1)); float s=(float)System.Math.Sqrt(1-n.w*n.w); axis = s<1e-6f? Vector3.right : new Vector3(n.x/s,n.y/s,n.z/s); }
        internal static Quaternion AngleAxis(float angle, Vector3 axis) { var ax=axis.normalized; float h=angle*Mathf.Deg2Rad*0.5f; float s=(float)System.Math.Sin(h); return new Quaternion(ax.x*s,ax.y*s,ax.z*s,(float)System.Math.Cos(h)); }
    }
}
