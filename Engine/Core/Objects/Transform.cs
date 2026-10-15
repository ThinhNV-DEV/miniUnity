using System;
using System.Collections;
using System.Collections.Generic;
using MiniUnity;

namespace UnityEngine
{
    public partial class Transform
    {
        internal Transform m_Parent;
        internal readonly List<Transform> m_Children = new();
        internal Vector3 m_LocalPositionRaw;          // for RectTransform only z is authoritative (xy derived)
        internal Quaternion m_LocalRotation = Quaternion.identity;
        internal Vector3 m_LocalScale = Vector3.one;
        internal Vector3 m_LocalEulerAnglesHint;
        internal bool m_ConstrainProportionsScale;
        bool m_HasChanged = true;

        protected Transform() { }

        public Transform parent { get => m_Parent; set => SetParent(value, true); }
        public Transform parentInternal { get => m_Parent; set => SetParent(value); }
        public int childCount => m_Children.Count;
        public Transform GetChild(int index) => m_Children[index];
        public Transform root { get { var t = this; while (t.m_Parent != null) t = t.m_Parent; return t; } }
        public bool hasChanged { get => m_HasChanged; set { m_HasChanged = value; if (value) s_SceneEdits++; } }
        public int hierarchyCount => 0;
        public int hierarchyCapacity { get => 0; set { } }

        public virtual Vector3 localPosition
        {
            get => m_LocalPositionRaw;
            set { m_LocalPositionRaw = value; m_HasChanged = true; s_SceneEdits++; }
        }
        public Quaternion localRotation
        {
            get => EffRotation;
            set { m_LocalRotation = value; m_HasChanged = true; s_SceneEdits++; } // Unity: script rotation does not touch the inspector euler hint
        }
        public Vector3 localScale { get => EffScale; set { m_LocalScale = value; m_HasChanged = true; s_SceneEdits++; } }
        public Vector3 localEulerAngles
        {
            get => m_LocalRotation.eulerAngles;
            set { m_LocalRotation = Quaternion.Euler(value); m_HasChanged = true; s_SceneEdits++; }   // Unity (script) leaves the inspector hint alone – see 12_Jitter oracle
        }
        public Vector3 eulerAngles { get => rotation.eulerAngles; set => rotation = Quaternion.Euler(value); }

        /// <summary>Transform values the engine drives (root Screen Space canvases follow the screen / their camera).
        /// The stored m_Local* fields keep what is serialized (Unity writes zeros for driven properties).</summary>
        internal virtual bool Driven(out Vector3 pos, out Quaternion rot, out Vector3 scale) { pos = default; rot = default; scale = default; return false; }
        internal Vector3 EffScale => Driven(out _, out _, out var s) ? s : m_LocalScale;
        internal Quaternion EffRotation => Driven(out _, out var r, out _) ? r : m_LocalRotation;
        public Matrix4x4 localToWorldMatrix
        {
            get
            {
                var m = Driven(out var dp, out var dr, out var ds) ? Matrix4x4.TRS(dp, dr, ds) : Matrix4x4.TRS(localPosition, m_LocalRotation, m_LocalScale);
                return m_Parent != null ? m_Parent.localToWorldMatrix * m : m;
            }
        }
        public Matrix4x4 worldToLocalMatrix => localToWorldMatrix.inverse;

        public Vector3 position
        {
            get => m_Parent != null ? m_Parent.localToWorldMatrix.MultiplyPoint3x4(localPosition) : localPosition;
            set => localPosition = m_Parent != null ? m_Parent.worldToLocalMatrix.MultiplyPoint3x4(value) : value;
        }
        public Quaternion rotation
        {
            get => m_Parent != null ? m_Parent.rotation * EffRotation : EffRotation;
            set => localRotation = m_Parent != null ? Quaternion.Inverse(m_Parent.rotation) * value : value;
        }
        public Vector3 lossyScale
        {
            get { var s = EffScale; for (var p = m_Parent; p != null; p = p.m_Parent) s = Vector3.Scale(s, p.EffScale); return s; }
        }
        public Vector3 right { get => rotation * Vector3.right; set => rotation = Quaternion.FromToRotation(Vector3.right, value); }
        public Vector3 up { get => rotation * Vector3.up; set => rotation = Quaternion.FromToRotation(Vector3.up, value); }
        public Vector3 forward { get => rotation * Vector3.forward; set => rotation = Quaternion.LookRotation(value); }

        public Vector3 TransformPoint(Vector3 p) => localToWorldMatrix.MultiplyPoint3x4(p);
        public Vector3 TransformPoint(float x, float y, float z) => TransformPoint(new Vector3(x, y, z));
        public Vector3 InverseTransformPoint(Vector3 p) => worldToLocalMatrix.MultiplyPoint3x4(p);
        public Vector3 InverseTransformPoint(float x, float y, float z) => InverseTransformPoint(new Vector3(x, y, z));
        public Vector3 TransformDirection(Vector3 d) => rotation * d;
        public Vector3 TransformDirection(float x, float y, float z) => TransformDirection(new Vector3(x, y, z));
        public Vector3 InverseTransformDirection(Vector3 d) => Quaternion.Inverse(rotation) * d;
        public Vector3 InverseTransformDirection(float x, float y, float z) => InverseTransformDirection(new Vector3(x, y, z));
        public Vector3 TransformVector(Vector3 v) => localToWorldMatrix.MultiplyVector(v);
        public Vector3 TransformVector(float x, float y, float z) => TransformVector(new Vector3(x, y, z));
        public Vector3 InverseTransformVector(Vector3 v) => worldToLocalMatrix.MultiplyVector(v);
        public Vector3 InverseTransformVector(float x, float y, float z) => InverseTransformVector(new Vector3(x, y, z));
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation) { this.position = position; this.rotation = rotation; }
        public void SetLocalPositionAndRotation(Vector3 localPosition, Quaternion localRotation) { this.localPosition = localPosition; this.localRotation = localRotation; }
        public void GetPositionAndRotation(out Vector3 position, out Quaternion rotation) { position = this.position; rotation = this.rotation; }
        public void GetLocalPositionAndRotation(out Vector3 localPosition, out Quaternion localRotation) { localPosition = this.localPosition; localRotation = this.localRotation; }

        public void SetParent(Transform p) => SetParent(p, true);
        public void SetParent(Transform parent, bool worldPositionStays)
        {
            if (parent == m_Parent) return;
            s_SceneEdits++; Hier.Version++;
            for (var c = parent; c != null; c = c.m_Parent) if (c == this) throw new InvalidOperationException("Cannot parent to own child");
            Vector3 wp = default; Quaternion wr = default; Vector3 ws = default;
            if (worldPositionStays) { wp = position; wr = rotation; ws = lossyScale; }
            bool wasActive = gameObject.activeInHierarchy;
            var go = gameObject;
            if (m_Parent != null) m_Parent.m_Children.Remove(this);
            else go.m_Scene?.RemoveRoot(go);
            m_Parent = parent;
            if (parent != null) { parent.m_Children.Add(this); go.m_Scene = null; if (parent.gameObject.m_InPrefabAsset) MarkPrefabAsset(go, true); }
            else { var s = SceneData.Active; go.m_Scene = s; s?.AddRoot(go); }
            if (worldPositionStays)
            {
                OnBeforeWorldStays();
                position = wp; rotation = wr;
                var ps = parent != null ? parent.lossyScale : Vector3.one;
                m_LocalScale = new Vector3(ps.x != 0 ? ws.x / ps.x : 0, ps.y != 0 ? ws.y / ps.y : 0, ps.z != 0 ? ws.z / ps.z : 0);
            }
            OnParentChanged();
            bool nowActive = gameObject.activeInHierarchy;
            if (wasActive != nowActive) Lifecycle.ActiveChanged(go, nowActive);
            Lifecycle.ParentChanged(this);
        }
        static void MarkPrefabAsset(GameObject go, bool v) { foreach (var g in Hier.Walk(go)) g.m_InPrefabAsset = v; }
        internal virtual void OnParentChanged() { }
        internal virtual void OnBeforeWorldStays() { }

        public Transform Find(string n)
        {
            if (n == null) return null;
            var parts = n.Split('/');
            Transform cur = this;
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                Transform found = null;
                foreach (var c in cur.m_Children) if (c.gameObject.m_Name == part) { found = c; break; }
                if (found == null) return null;
                cur = found;
            }
            return cur;
        }
        public Transform FindChild(string n) => Find(n);
        public bool IsChildOf(Transform parent) { for (var t = this; t != null; t = t.m_Parent) if (t == parent) return true; return false; }

        public int GetSiblingIndex() => m_Parent != null ? m_Parent.m_Children.IndexOf(this) : (gameObject.m_Scene?.Roots.IndexOf(gameObject) ?? 0);
        public void SetSiblingIndex(int index)
        {
            Hier.Version++; s_SceneEdits++;
            if (m_Parent != null)
            {
                var l = m_Parent.m_Children; l.Remove(this);
                index = Math.Clamp(index, 0, l.Count); l.Insert(index, this);
            }
            else if (gameObject.m_Scene != null)
            {
                var l = gameObject.m_Scene.Roots; l.Remove(gameObject);
                index = Math.Clamp(index, 0, l.Count); l.Insert(index, gameObject);
            }
            Lifecycle.SiblingChanged(this);
        }
        public void SetAsFirstSibling() => SetSiblingIndex(0);
        public void SetAsLastSibling() => SetSiblingIndex(int.MaxValue);
        public void DetachChildren() { foreach (var c in m_Children.ToArray()) c.SetParent(null); }

        public void Translate(Vector3 translation) => Translate(translation, Space.Self);
        public void Translate(Vector3 translation, Space relativeTo)
        {
            if (relativeTo == Space.World) position += translation; else position += TransformDirection(translation);
        }
        public void Translate(float x, float y, float z) => Translate(new Vector3(x, y, z));
        public void Translate(float x, float y, float z, Space relativeTo) => Translate(new Vector3(x, y, z), relativeTo);
        public void Rotate(Vector3 eulers) => Rotate(eulers, Space.Self);
        public void Rotate(Vector3 eulers, Space relativeTo)
        {
            var q = Quaternion.Euler(eulers);
            if (relativeTo == Space.Self) localRotation = localRotation * q; else rotation = q * rotation;
        }
        public void Rotate(float x, float y, float z) => Rotate(new Vector3(x, y, z));
        public void Rotate(float x, float y, float z, Space relativeTo) => Rotate(new Vector3(x, y, z), relativeTo);
        public void Rotate(Vector3 axis, float angle) => Rotate(axis, angle, Space.Self);
        public void Rotate(Vector3 axis, float angle, Space relativeTo)
        {
            var q = Quaternion.AngleAxis(angle, axis);
            if (relativeTo == Space.Self) localRotation = localRotation * q; else rotation = q * rotation;
        }
        public void LookAt(Vector3 worldPosition) => LookAt(worldPosition, Vector3.up);
        public void LookAt(Vector3 worldPosition, Vector3 worldUp) => rotation = Quaternion.LookRotation(worldPosition - position, worldUp);
        public void LookAt(Transform target) => LookAt(target.position);
        public void LookAt(Transform target, Vector3 worldUp) => LookAt(target.position, worldUp);

        public IEnumerator GetEnumerator() => m_Children.ToArray().GetEnumerator();
        internal Vector3 GetLocalEulerAngles(RotationOrder order) => localEulerAngles;
        internal void SetLocalEulerAngles(Vector3 euler, RotationOrder order) => localEulerAngles = euler;
        internal void SetLocalEulerHint(Vector3 euler) => m_LocalEulerAnglesHint = euler;
        internal int GetRotationOrderInternal() => 4;
        internal void SetRotationOrderInternal(RotationOrder rotationOrder) { }
    }

    public sealed partial class RectTransform
    {
        internal Vector2 m_AnchorMin = new(0.5f, 0.5f);
        internal Vector2 m_AnchorMax = new(0.5f, 0.5f);
        internal Vector2 m_AnchoredPosition;
        internal Vector2 m_SizeDelta = new(100, 100);
        internal Vector2 m_Pivot = new(0.5f, 0.5f);
        internal DrivenTransformProperties m_DrivenProperties;
        internal Object m_DrivenBy;

        public RectTransform() { }

        public Vector2 anchorMin { get => m_AnchorMin; set { m_AnchorMin = value; Changed(); } }
        public Vector2 anchorMax { get => m_AnchorMax; set { m_AnchorMax = value; Changed(); } }
        public Vector2 anchoredPosition { get => m_AnchoredPosition; set { m_AnchoredPosition = value; Changed(false); } }
        public Vector2 sizeDelta { get => m_SizeDelta; set { m_SizeDelta = value; Changed(); } }
        public Vector2 pivot { get => m_Pivot; set { m_Pivot = value; Changed(); } }
        public Vector3 anchoredPosition3D
        {
            get => new(m_AnchoredPosition.x, m_AnchoredPosition.y, m_LocalPositionRaw.z);
            set { m_AnchoredPosition = new Vector2(value.x, value.y); m_LocalPositionRaw.z = value.z; Changed(false); }
        }
        public Vector2 offsetMin
        {
            get => m_AnchoredPosition - Vector2.Scale(m_SizeDelta, m_Pivot);
            set
            {
                var offset = value - (m_AnchoredPosition - Vector2.Scale(m_SizeDelta, m_Pivot));
                m_SizeDelta -= offset;
                m_AnchoredPosition += Vector2.Scale(offset, Vector2.one - m_Pivot);
                Changed();
            }
        }
        public Vector2 offsetMax
        {
            get => m_AnchoredPosition + Vector2.Scale(m_SizeDelta, Vector2.one - m_Pivot);
            set
            {
                var offset = value - (m_AnchoredPosition + Vector2.Scale(m_SizeDelta, Vector2.one - m_Pivot));
                m_SizeDelta += offset;
                m_AnchoredPosition += Vector2.Scale(offset, m_Pivot);
                Changed();
            }
        }

        /// <summary>Size of the parent rect (driven root canvases use the virtual screen).</summary>
        internal Rect ParentRect()
        {
            if (m_Parent is RectTransform prt) return prt.rect;
            return new Rect(0, 0, 0, 0);
        }

        internal bool IsDrivenRootCanvas(out Vector2 size)
        {
            size = default;
            if (m_Parent != null && m_Parent is RectTransform) return false;
            var c = m_GameObject?.GetComponent<Canvas>();
            if (c == null || c.renderMode == RenderMode.WorldSpace) return false;
            size = MiniUnity.Screen.CanvasSize(c);
            return true;
        }

        public Rect rect
        {
            get
            {
                if (IsDrivenRootCanvas(out var sz)) return new Rect(-sz.x * 0.5f, -sz.y * 0.5f, sz.x, sz.y);
                var pr = ParentRect();
                var size = Vector2.Scale(pr.size, m_AnchorMax - m_AnchorMin) + m_SizeDelta;
                return new Rect(-m_Pivot.x * size.x, -m_Pivot.y * size.y, size.x, size.y);
            }
        }

        /// <summary>Unity keeps m_LocalPosition as stored state, refreshed from anchoredPosition/anchors/pivot and the parent
        /// rect at the moment this RectTransform changes (parent size changes are not pushed to children synchronously).</summary>
        public static bool PropagateParentRect = Environment.GetEnvironmentVariable("MINIUNITY_PROPAGATE") == "1";
        internal Vector3 ComputeLocalPosition()
        {
            if (IsDrivenRootCanvas(out _)) return m_LocalPositionRaw;
            var pr = ParentRect();
            var amin = pr.min + Vector2.Scale(pr.size, m_AnchorMin);
            var amax = pr.min + Vector2.Scale(pr.size, m_AnchorMax);
            var refPt = amin + Vector2.Scale(amax - amin, m_Pivot);
            var p = refPt + m_AnchoredPosition;
            return new Vector3(p.x, p.y, m_LocalPositionRaw.z);
        }
        internal void RefreshLocalPosition()
        {
            m_LocalPositionRaw = ComputeLocalPosition();
            s_SceneEdits++;
        }
        internal override bool Driven(out Vector3 pos, out Quaternion rot, out Vector3 scale)
        {
            pos = default; rot = Quaternion.identity; scale = Vector3.one;
            if (!IsDrivenRootCanvas(out var size)) return false;
            var c = m_GameObject.GetComponent<Canvas>();
            var cam = c.renderMode == RenderMode.ScreenSpaceCamera ? c.worldCamera : null;
            if (cam == null)
            {
                float f = Mathf.Max(1e-6f, c.scaleFactor);
                pos = new Vector3(MiniUnity.Screen.Width * 0.5f, MiniUnity.Screen.Height * 0.5f, 0); scale = new Vector3(f, f, f);
                return true;
            }
            var ct = cam.transform;
            float d = c.planeDistance;
            float worldH = cam.orthographic ? 2f * cam.orthographicSize : 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float k = worldH / Mathf.Max(1e-6f, size.y);
            pos = ct.position + ct.forward * d; rot = ct.rotation; scale = new Vector3(k, k, k);
            return true;
        }
        public override Vector3 localPosition
        {
            get => Driven(out var dp, out _, out _) ? dp : m_LocalPositionRaw;
            set
            {
                m_LocalPositionRaw = value; s_SceneEdits++;
                if (IsDrivenRootCanvas(out _)) return;
                var pr = ParentRect();
                var amin = pr.min + Vector2.Scale(pr.size, m_AnchorMin);
                var amax = pr.min + Vector2.Scale(pr.size, m_AnchorMax);
                var refPt = amin + Vector2.Scale(amax - amin, m_Pivot);
                m_AnchoredPosition = new Vector2(value.x, value.y) - refPt;
                Changed(false);
            }
        }
        internal override void OnParentChanged() { RefreshLocalPosition(); }

        void ChildParentChanged() { RefreshLocalPosition(); if (PropagateParentRect) foreach (var c in m_Children) if (c is RectTransform rc) rc.ChildParentChanged(); }
        internal override void OnBeforeWorldStays() { }

        void Changed(bool size = true)
        {
            hasChanged = true;
            RefreshLocalPosition();
            if (size && PropagateParentRect) foreach (var c in m_Children) if (c is RectTransform rc) rc.ChildParentChanged();
            if (size) Lifecycle.RectDimensionsChanged(this);
        }

        public Object drivenByObject { get => m_DrivenBy; internal set => m_DrivenBy = value; }
        internal DrivenTransformProperties drivenProperties { get => m_DrivenProperties; set => m_DrivenProperties = value; }

        public void GetLocalCorners(Vector3[] corners)
        {
            var r = rect;
            corners[0] = new Vector3(r.xMin, r.yMin, 0); corners[1] = new Vector3(r.xMin, r.yMax, 0);
            corners[2] = new Vector3(r.xMax, r.yMax, 0); corners[3] = new Vector3(r.xMax, r.yMin, 0);
        }
        public void GetWorldCorners(Vector3[] corners)
        {
            GetLocalCorners(corners);
            var m = localToWorldMatrix;
            for (int i = 0; i < 4; i++) corners[i] = m.MultiplyPoint(corners[i]);
        }
        public void SetSizeWithCurrentAnchors(Axis axis, float size)
        {
            int i = (int)axis;
            var sd = m_SizeDelta;
            sd[i] = size - ParentRect().size[i] * (m_AnchorMax[i] - m_AnchorMin[i]);
            sizeDelta = sd;
        }
        public void SetInsetAndSizeFromParentEdge(Edge edge, float inset, float size)
        {
            int axis = (edge == Edge.Top || edge == Edge.Bottom) ? 1 : 0;
            bool end = edge == Edge.Top || edge == Edge.Right;
            float anchorValue = end ? 1 : 0;
            var amin = m_AnchorMin; amin[axis] = anchorValue; var amax = m_AnchorMax; amax[axis] = anchorValue;
            m_AnchorMin = amin; m_AnchorMax = amax;
            var sd = m_SizeDelta; sd[axis] = size; m_SizeDelta = sd;
            var ap = m_AnchoredPosition;
            ap[axis] = end ? -inset - size * (1 - m_Pivot[axis]) : inset + size * m_Pivot[axis];
            m_AnchoredPosition = ap;
            Changed();
        }
        public void ForceUpdateRectTransforms() { }
        internal static void SendReapplyDrivenProperties(RectTransform driven) => reapplyDrivenPropertiesHandlers?.Invoke(driven);
        internal Rect GetRectInParentSpace() { var r = rect; var lp = localPosition; r.x += lp.x; r.y += lp.y; return r; }
        static ReapplyDrivenProperties reapplyDrivenPropertiesHandlers;
        public static event ReapplyDrivenProperties reapplyDrivenProperties { add => reapplyDrivenPropertiesHandlers += value; remove => reapplyDrivenPropertiesHandlers -= value; }
    }
}

namespace MiniUnity
{
    public static class EulerHint
    {
        /// <summary>Unity keeps the euler hint in sync when localRotation is set: the hint becomes the euler of the new rotation
        /// closest to the previous hint.</summary>
        public static UnityEngine.Vector3 FromQuaternion(UnityEngine.Quaternion q, UnityEngine.Vector3 prev)
        {
            var e = q.eulerAngles;
            // map to the representation closest to prev (Unity prefers -180..180 range when hint is near 0)
            return new UnityEngine.Vector3(Closest(e.x, prev.x), Closest(e.y, prev.y), Closest(e.z, prev.z));
        }
        static float Closest(float a, float prev)
        {
            float d = a - prev;
            d = d - 360f * (float)System.Math.Round(d / 360f);
            float r = prev + d;
            if (System.Math.Abs(r) < 1e-4f) r = 0;
            return r;
        }
    }

    public static class Screen
    {
        public static float Width = 1920, Height = 1080;
        public static UnityEngine.Vector2 CanvasSize(UnityEngine.Canvas c)
        {
            var px = PixelSize(c);
            float scale = ScalerFactor(c, px) ?? c.scaleFactor;
            if (scale <= 0) scale = 1;
            return new UnityEngine.Vector2(px.x / scale, px.y / scale);
        }
        /// <summary>The factor a CanvasScaler (Scale With Screen Size) would set for this pixel size: the scaler runs every frame
        /// in Unity, so a camera retargeted to a smaller RenderTexture (thumbnails) still lays the canvas out at the reference size.</summary>
        static float? ScalerFactor(UnityEngine.Canvas c, UnityEngine.Vector2 px)
        {
            var s = c.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (s == null || !s.enabled || s.uiScaleMode != UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize) return null;
            var r = s.referenceResolution;
            if (r.x <= 0 || r.y <= 0) return null;
            switch (s.screenMatchMode)
            {
                case UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand: return System.Math.Min(px.x / r.x, px.y / r.y);
                case UnityEngine.UI.CanvasScaler.ScreenMatchMode.Shrink: return System.Math.Max(px.x / r.x, px.y / r.y);
                default:
                    float lw = (float)System.Math.Log(px.x / r.x, 2), lh = (float)System.Math.Log(px.y / r.y, 2);
                    return (float)System.Math.Pow(2, lw + (lh - lw) * s.matchWidthOrHeight);
            }
        }

        /// <summary>Pixel size a root canvas covers: the camera's target (RenderTexture or screen) in Screen Space – Camera, else the screen.</summary>
        public static UnityEngine.Vector2 PixelSize(UnityEngine.Canvas c)
        {
            var cam = c.renderMode == UnityEngine.RenderMode.ScreenSpaceCamera ? c.worldCamera : null;
            return cam != null ? new UnityEngine.Vector2(cam.pixelWidth, cam.pixelHeight) : new UnityEngine.Vector2(Width, Height);
        }
    }
}
