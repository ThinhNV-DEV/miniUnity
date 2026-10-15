// RectTransformUtility: the managed half follows Unity's own C# (UnityCsReference); PointInRectangle (RectangleContainsScreenPoint)
// and the pixel-snapping helpers are native in Unity and written here from their documented behaviour.
using System;

namespace UnityEngine
{
    public sealed partial class RectTransformUtility
    {
        static readonly Vector3[] s_Corners = new Vector3[4];

        public static Ray ScreenPointToRay(Camera cam, Vector2 screenPos)
        {
            if (cam != null) return cam.ScreenPointToRay(screenPos);
            Vector3 pos = screenPos;
            pos.z -= 100f;
            return new Ray(pos, Vector3.forward);
        }

        public static bool ScreenPointToWorldPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector3 worldPoint)
        {
            worldPoint = Vector2.zero;
            Ray ray = ScreenPointToRay(cam, screenPoint);
            var plane = new Plane(rect.rotation * Vector3.back, rect.position);
            float dist = 0f;
            float dot = Vector3.Dot(Vector3.Normalize(rect.position - ray.origin), plane.normal);
            if (dot != 0 && !plane.Raycast(ray, out dist)) return false;
            worldPoint = ray.GetPoint(dist);
            return true;
        }

        public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (ScreenPointToWorldPointInRectangle(rect, screenPoint, cam, out var worldPoint))
            {
                localPoint = rect.InverseTransformPoint(worldPoint);
                return true;
            }
            return false;
        }

        public static Vector2 WorldToScreenPoint(Camera cam, Vector3 worldPoint)
        {
            if (cam == null) return new Vector2(worldPoint.x, worldPoint.y);
            return cam.WorldToScreenPoint(worldPoint);
        }

        public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint) => RectangleContainsScreenPoint(rect, screenPoint, null, Vector4.zero);
        public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint, Camera cam) => RectangleContainsScreenPoint(rect, screenPoint, cam, Vector4.zero);
        /// <summary>The screen point, projected onto the rectangle's plane, lies in rect.rect shrunk by offset (left, bottom, right, top).</summary>
        public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint, Camera cam, Vector4 offset)
        {
            if (rect == null) return false;
            if (!ScreenPointToLocalPointInRectangle(rect, screenPoint, cam, out var local)) return false;
            var r = rect.rect;
            return local.x >= r.xMin + offset.x && local.x <= r.xMax - offset.z && local.y >= r.yMin + offset.y && local.y <= r.yMax - offset.w;
        }

        /// <summary>Snaps to whole screen pixels on a pixel-perfect canvas (Screen Space); otherwise unchanged.</summary>
        public static Vector2 PixelAdjustPoint(Vector2 point, Transform elementTransform, Canvas canvas)
        {
            if (canvas == null || !canvas.pixelPerfect || elementTransform == null || canvas.renderMode == RenderMode.WorldSpace) return point;
            var cam = canvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.worldCamera : null;
            var world = elementTransform.TransformPoint(point);
            var screen = WorldToScreenPoint(cam, world);
            screen = new Vector2(Mathf.Round(screen.x), Mathf.Round(screen.y));
            var rt = elementTransform as RectTransform;
            if (rt != null && ScreenPointToLocalPointInRectangle(rt, screen, cam, out var local)) return local;
            return point;
        }

        public static Rect PixelAdjustRect(RectTransform rectTransform, Canvas canvas)
        {
            var r = rectTransform.rect;
            if (canvas == null || !canvas.pixelPerfect || canvas.renderMode == RenderMode.WorldSpace) return r;
            var min = PixelAdjustPoint(r.min, rectTransform, canvas);
            var max = PixelAdjustPoint(r.max, rectTransform, canvas);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        public static Bounds CalculateRelativeRectTransformBounds(Transform root, Transform child)
        {
            RectTransform[] rects = child.GetComponentsInChildren<RectTransform>(false);
            if (rects.Length > 0)
            {
                var vMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var vMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                Matrix4x4 toLocal = root.worldToLocalMatrix;
                for (int i = 0; i < rects.Length; i++)
                {
                    rects[i].GetWorldCorners(s_Corners);
                    for (int j = 0; j < 4; j++)
                    {
                        Vector3 v = toLocal.MultiplyPoint3x4(s_Corners[j]);
                        vMin = Vector3.Min(v, vMin);
                        vMax = Vector3.Max(v, vMax);
                    }
                }
                var b = new Bounds(vMin, Vector3.zero);
                b.Encapsulate(vMax);
                return b;
            }
            return new Bounds(Vector3.zero, Vector3.zero);
        }

        public static Bounds CalculateRelativeRectTransformBounds(Transform trans) => CalculateRelativeRectTransformBounds(trans, trans);

        public static void FlipLayoutOnAxis(RectTransform rect, int axis, bool keepPositioning, bool recursive)
        {
            if (rect == null) return;
            if (recursive)
                for (int i = 0; i < rect.childCount; i++)
                    if (rect.GetChild(i) is RectTransform childRect) FlipLayoutOnAxis(childRect, axis, false, true);
            Vector2 pivot = rect.pivot;
            pivot[axis] = 1.0f - pivot[axis];
            rect.pivot = pivot;
            if (keepPositioning) return;
            Vector2 anchoredPosition = rect.anchoredPosition;
            anchoredPosition[axis] = -anchoredPosition[axis];
            rect.anchoredPosition = anchoredPosition;
            Vector2 anchorMin = rect.anchorMin, anchorMax = rect.anchorMax;
            float temp = anchorMin[axis];
            anchorMin[axis] = 1 - anchorMax[axis];
            anchorMax[axis] = 1 - temp;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
        }

        public static void FlipLayoutAxes(RectTransform rect, bool keepPositioning, bool recursive)
        {
            if (rect == null) return;
            if (recursive)
                for (int i = 0; i < rect.childCount; i++)
                    if (rect.GetChild(i) is RectTransform childRect) FlipLayoutAxes(childRect, false, true);
            rect.pivot = GetTransposed(rect.pivot);
            rect.sizeDelta = GetTransposed(rect.sizeDelta);
            if (keepPositioning) return;
            rect.anchoredPosition = GetTransposed(rect.anchoredPosition);
            rect.anchorMin = GetTransposed(rect.anchorMin);
            rect.anchorMax = GetTransposed(rect.anchorMax);
        }

        static Vector2 GetTransposed(Vector2 input) => new Vector2(input.y, input.x);
    }
}
