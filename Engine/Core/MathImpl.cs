using System;
using UnityEngine;
namespace MiniUnity
{
    public static class MathImpl
    {
        public static Quaternion FromEulerRad(Vector3 e)
        {
            // Unity EulerToQuaternion: float sin/cos of half angles, q = (qY * qX) * qZ  (ZXY order)
            float cX = (float)Math.Cos(e.x / 2.0), sX = (float)Math.Sin(e.x / 2.0);
            float cY = (float)Math.Cos(e.y / 2.0), sY = (float)Math.Sin(e.y / 2.0);
            float cZ = (float)Math.Cos(e.z / 2.0), sZ = (float)Math.Sin(e.z / 2.0);
            var qX = new Quaternion(sX, 0, 0, cX);
            var qY = new Quaternion(0, sY, 0, cY);
            var qZ = new Quaternion(0, 0, sZ, cZ);
            return (qY * qX) * qZ;
        }
        public static Vector3 ToEulerRad(Quaternion q)
        {
            double x = q.x, y = q.y, z = q.z, w = q.w;
            double sinX = 2 * (w * x - y * z);
            double ex, ey, ez;
            if (Math.Abs(sinX) < 0.99999)
            {
                ex = Math.Asin(sinX);
                ey = Math.Atan2(2 * (x * z + w * y), 1 - 2 * (x * x + y * y));
                ez = Math.Atan2(2 * (x * y + w * z), 1 - 2 * (x * x + z * z));
            }
            else
            {
                ex = Math.Sign(sinX) * Math.PI / 2;
                ey = Math.Atan2(-2 * (x * z - w * y), 1 - 2 * (y * y + z * z));
                ez = 0;
            }
            return new Vector3((float)ex, (float)ey, (float)ez);
        }
        public static Quaternion Lerp(Quaternion a, Quaternion b, float t)
        {
            float d = Quaternion.Dot(a, b); float s = d < 0 ? -1 : 1;
            var r = new Quaternion(a.x + (s * b.x - a.x) * t, a.y + (s * b.y - a.y) * t, a.z + (s * b.z - a.z) * t, a.w + (s * b.w - a.w) * t);
            return Quaternion.Normalize(r);
        }
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            double d = Quaternion.Dot(a, b);
            if (d < 0) { b = new Quaternion(-b.x, -b.y, -b.z, -b.w); d = -d; }
            if (d > 0.9995) return Lerp(a, b, t);
            double th = Math.Acos(d), st = Math.Sin(th);
            double wa = Math.Sin((1 - t) * th) / st, wb = Math.Sin(t * th) / st;
            return new Quaternion((float)(a.x * wa + b.x * wb), (float)(a.y * wa + b.y * wb), (float)(a.z * wa + b.z * wb), (float)(a.w * wa + b.w * wb));
        }
        public static Vector3 SlerpV(Vector3 a, Vector3 b, float t)
        {
            float ma = a.magnitude, mb = b.magnitude;
            var q = Slerp(Quaternion.identity, FromTo(a, b), t);
            return (q * a.normalized) * Mathf.LerpUnclamped(ma, mb, t);
        }
        public static Quaternion FromTo(Vector3 from, Vector3 to)
        {
            var f = from.normalized; var tt = to.normalized;
            float d = Vector3.Dot(f, tt);
            if (d >= 1f - 1e-6f) return Quaternion.identity;
            if (d <= -1f + 1e-6f)
            {
                var axis = Vector3.Cross(Vector3.right, f);
                if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(Vector3.up, f);
                return Quaternion.AngleAxis(180, axis.normalized);
            }
            var c = Vector3.Cross(f, tt);
            var q = new Quaternion(c.x, c.y, c.z, 1 + d);
            return Quaternion.Normalize(q);
        }
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            forward = forward.normalized;
            if (forward.sqrMagnitude < 1e-12f) return Quaternion.identity;
            var right = Vector3.Cross(up, forward).normalized;
            if (right.sqrMagnitude < 1e-12f) return FromTo(Vector3.forward, forward);
            up = Vector3.Cross(forward, right);
            var m = Matrix4x4.identity;
            m.m00 = right.x; m.m10 = right.y; m.m20 = right.z;
            m.m01 = up.x; m.m11 = up.y; m.m21 = up.z;
            m.m02 = forward.x; m.m12 = forward.y; m.m22 = forward.z;
            return RotationFromMatrix(m);
        }
        public static Quaternion RotationFromMatrix(Matrix4x4 m)
        {
            float tr = m.m00 + m.m11 + m.m22; Quaternion q;
            if (tr > 0) { float s = Mathf.Sqrt(tr + 1f) * 2; q = new Quaternion((m.m21 - m.m12) / s, (m.m02 - m.m20) / s, (m.m10 - m.m01) / s, 0.25f * s); }
            else if (m.m00 > m.m11 && m.m00 > m.m22) { float s = Mathf.Sqrt(1f + m.m00 - m.m11 - m.m22) * 2; q = new Quaternion(0.25f * s, (m.m01 + m.m10) / s, (m.m02 + m.m20) / s, (m.m21 - m.m12) / s); }
            else if (m.m11 > m.m22) { float s = Mathf.Sqrt(1f + m.m11 - m.m00 - m.m22) * 2; q = new Quaternion((m.m01 + m.m10) / s, 0.25f * s, (m.m12 + m.m21) / s, (m.m02 - m.m20) / s); }
            else { float s = Mathf.Sqrt(1f + m.m22 - m.m00 - m.m11) * 2; q = new Quaternion((m.m02 + m.m20) / s, (m.m12 + m.m21) / s, 0.25f * s, (m.m10 - m.m01) / s); }
            return Quaternion.Normalize(q);
        }
        public static Matrix4x4 TRS(Vector3 p, Quaternion q, Vector3 s)
        {
            var r = Matrix4x4.Rotate(q);
            var m = new Matrix4x4();
            for (int i = 0; i < 3; i++) { m[i, 0] = r[i, 0] * s.x; m[i, 1] = r[i, 1] * s.y; m[i, 2] = r[i, 2] * s.z; }
            m.m03 = p.x; m.m13 = p.y; m.m23 = p.z; m.m33 = 1;
            return m;
        }
        public static float Det(Matrix4x4 m)
        {
            float a = m.m00, b = m.m01, c = m.m02, d = m.m03, e = m.m10, f = m.m11, g = m.m12, h = m.m13, i = m.m20, j = m.m21, k = m.m22, l = m.m23, mm = m.m30, n = m.m31, o = m.m32, p = m.m33;
            float kp_lo = k * p - l * o, jp_ln = j * p - l * n, jo_kn = j * o - k * n, ip_lm = i * p - l * mm, io_km = i * o - k * mm, in_jm = i * n - j * mm;
            return a * (f * kp_lo - g * jp_ln + h * jo_kn) - b * (e * kp_lo - g * ip_lm + h * io_km) + c * (e * jp_ln - f * ip_lm + h * in_jm) - d * (e * jo_kn - f * io_km + g * in_jm);
        }
        public static Matrix4x4 Inverse(Matrix4x4 m)
        {
            var a = new double[4, 8];
            for (int i = 0; i < 4; i++) { for (int j = 0; j < 4; j++) a[i, j] = m[i, j]; a[i, 4 + i] = 1; }
            for (int c = 0; c < 4; c++)
            {
                int piv = c; for (int r = c + 1; r < 4; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c])) piv = r;
                if (Math.Abs(a[piv, c]) < 1e-12) return Matrix4x4.zero;
                for (int j = 0; j < 8; j++) { var t = a[c, j]; a[c, j] = a[piv, j]; a[piv, j] = t; }
                double d = a[c, c]; for (int j = 0; j < 8; j++) a[c, j] /= d;
                for (int r = 0; r < 4; r++) if (r != c) { double f = a[r, c]; for (int j = 0; j < 8; j++) a[r, j] -= f * a[c, j]; }
            }
            var res = new Matrix4x4();
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) res[i, j] = (float)a[i, 4 + j];
            return res;
        }
    }
}
