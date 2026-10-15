// Frustum culling for script queries: GeometryUtility (frustum planes, AABB test, bounds, polygon planes) and Renderer.isVisible
// (inside the frustum of some enabled camera whose culling mask has the renderer's layer), which drives Animator.cullingMode.
using System;
using UnityEngine;

namespace UnityEngine
{
    public sealed partial class GeometryUtility
    {
        public static Plane[] CalculateFrustumPlanes(Camera camera) { var p = new Plane[6]; CalculateFrustumPlanes(camera, p); return p; }
        public static Plane[] CalculateFrustumPlanes(Matrix4x4 worldToProjectionMatrix) { var p = new Plane[6]; CalculateFrustumPlanes(worldToProjectionMatrix, p); return p; }
        public static void CalculateFrustumPlanes(Camera camera, Plane[] planes)
        {
            if (camera == null) throw new NullReferenceException("The camera is null");
            CalculateFrustumPlanes(camera.projectionMatrix * camera.worldToCameraMatrix, planes);
        }

        /// <summary>Gribb–Hartmann extraction; Unity's order: left, right, bottom, top, near, far, normals pointing inside.</summary>
        public static void CalculateFrustumPlanes(Matrix4x4 m, Plane[] planes)
        {
            if (planes == null) throw new ArgumentNullException(nameof(planes));
            if (planes.Length != 6) throw new ArgumentException("Planes array must be of length 6.", nameof(planes));
            var r3 = new Vector4(m.m30, m.m31, m.m32, m.m33);
            var rows = new[] { new Vector4(m.m00, m.m01, m.m02, m.m03), new Vector4(m.m10, m.m11, m.m12, m.m13), new Vector4(m.m20, m.m21, m.m22, m.m23) };
            for (int i = 0; i < 6; i++)
            {
                var v = (i & 1) == 0 ? r3 + rows[i >> 1] : r3 - rows[i >> 1];
                var n = new Vector3(v.x, v.y, v.z); float len = n.magnitude;
                planes[i] = len > 0 ? new Plane { normal = n / len, distance = v.w / len } : new Plane { normal = n, distance = v.w };
            }
        }

        public static bool TestPlanesAABB(Plane[] planes, Bounds bounds)
        {
            var c = bounds.center; var e = bounds.extents;
            foreach (var p in planes)
            {
                var n = p.normal;
                float r = e.x * Mathf.Abs(n.x) + e.y * Mathf.Abs(n.y) + e.z * Mathf.Abs(n.z);
                if (Vector3.Dot(n, c) + p.distance + r < 0) return false;
            }
            return true;
        }

        public static Bounds CalculateBounds(Vector3[] positions, Matrix4x4 transform)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (positions.Length == 0) throw new ArgumentException("Zero-sized array is not allowed.", nameof(positions));
            var b = new Bounds(transform.MultiplyPoint3x4(positions[0]), Vector3.zero);
            for (int i = 1; i < positions.Length; i++) b.Encapsulate(transform.MultiplyPoint3x4(positions[i]));
            return b;
        }

        /// <summary>Newell's method: the plane through a (possibly non-planar) polygon; false for fewer than 3 points or zero area.</summary>
        public static bool TryCreatePlaneFromPolygon(Vector3[] vertices, out Plane plane)
        {
            plane = new Plane(Vector3.up, 0);
            if (vertices == null || vertices.Length < 3) return false;
            Vector3 n = Vector3.zero, c = Vector3.zero;
            for (int i = 0; i < vertices.Length; i++)
            {
                var a = vertices[i]; var b = vertices[(i + 1) % vertices.Length];
                n.x += (a.y - b.y) * (a.z + b.z); n.y += (a.z - b.z) * (a.x + b.x); n.z += (a.x - b.x) * (a.y + b.y);
                c += a;
            }
            float len = n.magnitude;
            if (len < 1e-12f) return false;
            n /= len; c /= vertices.Length;
            plane = new Plane(n, c);
            return true;
        }
    }

    public partial class Renderer
    {
        public bool isVisible => MiniUnity.Visibility.Visible(this);
    }
}

namespace MiniUnity
{
    public static class Visibility
    {
        /// <summary>World bounds as the culler sees them (sprite rect for sprite renderers, the mesh for mesh / skinned renderers).</summary>
        public static Bounds WorldBounds(Renderer r)
        {
            if (r is SpriteRenderer sr)
            {
                var s = sr.sprite;
                if (s == null) return new Bounds(r.transform.position, Vector3.zero);
                return Transformed(s.bounds, r.transform.localToWorldMatrix);
            }
            if (r is SkinnedMeshRenderer && r.NRef<Mesh>("m_Mesh") is Mesh m) return Transformed(m.bounds, r.transform.localToWorldMatrix);
            return r.bounds;
        }

        static Bounds Transformed(Bounds lb, Matrix4x4 M)
        {
            var c = M.MultiplyPoint3x4(lb.center); var e = lb.extents;
            var ax = new Vector3(Mathf.Abs(M.m00) * e.x + Mathf.Abs(M.m01) * e.y + Mathf.Abs(M.m02) * e.z,
                                 Mathf.Abs(M.m10) * e.x + Mathf.Abs(M.m11) * e.y + Mathf.Abs(M.m12) * e.z,
                                 Mathf.Abs(M.m20) * e.x + Mathf.Abs(M.m21) * e.y + Mathf.Abs(M.m22) * e.z);
            return new Bounds(c, ax * 2);
        }

        public static bool Visible(Renderer r)
        {
            if (r == null || r.m_Destroyed || !r.enabled || !r.gameObject.activeInHierarchy) return false;
            var planes = new Plane[6];
            Bounds? b = null;
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                if ((cam.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
                b ??= WorldBounds(r);
                GeometryUtility.CalculateFrustumPlanes(cam, planes);
                if (GeometryUtility.TestPlanesAABB(planes, b.Value)) return true;
            }
            return false;
        }
    }
}
