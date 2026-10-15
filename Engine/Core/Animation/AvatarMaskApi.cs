// AvatarMask (.mask asset, AvatarMask 319): humanoid body-part flags (m_Mask) and transform elements (m_Elements: path +
// weight). An Animator layer with a mask that lists transforms only animates the listed, active paths (relative to the
// Animator); a mask without transform elements does not filter generic (non-humanoid) bindings.
using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity.Yaml;

namespace UnityEngine
{
    public sealed partial class AvatarMask
    {
        internal int m_Version;
        HashSet<string> m_ActiveCache; int m_CacheVersion = -1;

        YSeq Elements { get { if (NGet("m_Elements") is YSeq s) return s; var n = new YSeq(); NSet("m_Elements", n); return n; } }
        YSeq Parts
        {
            get
            {
                if (NGet("m_Mask") is YSeq s) return s;
                var n = new YSeq();
                for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) n.Items.Add(YScalar.Of(1));
                NSet("m_Mask", n); return n;
            }
        }

        public int humanoidBodyPartCount => (int)AvatarMaskBodyPart.LastBodyPart;
        public bool GetHumanoidBodyPartActive(AvatarMaskBodyPart index) => (int)index < Parts.Items.Count && (Parts.Items[(int)index] as YScalar)?.I != 0;
        public void SetHumanoidBodyPartActive(AvatarMaskBodyPart index, bool value)
        {
            var p = Parts;
            while (p.Items.Count <= (int)index) p.Items.Add(YScalar.Of(1));
            p.Items[(int)index] = YScalar.Of(value ? 1 : 0); m_Version++;
        }

        public int transformCount
        {
            get => Elements.Items.Count;
            set
            {
                var e = Elements;
                while (e.Items.Count > value) e.Items.RemoveAt(e.Items.Count - 1);
                while (e.Items.Count < value) e.Items.Add(Element("", true));
                m_Version++;
            }
        }
        static YMap Element(string path, bool active) => new YMap().Add("m_Path", path ?? "").Add("m_Weight", YScalar.Of(active ? 1 : 0));
        YMap At(int i) => i >= 0 && i < Elements.Items.Count ? Elements.Items[i] as YMap : null;
        public string GetTransformPath(int index) => (At(index)?["m_Path"] as YScalar)?.Value ?? "";
        public void SetTransformPath(int index, string path) { if (At(index) is YMap m) { m["m_Path"] = new YScalar(path ?? ""); m_Version++; } }
        public bool GetTransformActive(int index) => ((At(index)?["m_Weight"] as YScalar)?.F ?? 0) > 0;
        public void SetTransformActive(int index, bool value) { if (At(index) is YMap m) { m["m_Weight"] = YScalar.Of(value ? 1 : 0); m_Version++; } }

        public void AddTransformPath(Transform transform) => AddTransformPath(transform, true);
        /// <summary>Adds the transform (path relative to its root, "" for the root itself) and, when recursive, its children; all active.</summary>
        public void AddTransformPath(Transform transform, bool recursive)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            var root = transform.root;
            void Add(Transform t)
            {
                var path = RelativePath(root, t);
                if (!Elements.Items.OfType<YMap>().Any(m => (m["m_Path"] as YScalar)?.Value == path)) Elements.Items.Add(Element(path, true));
                if (recursive) for (int i = 0; i < t.childCount; i++) Add(t.GetChild(i));
            }
            Add(transform);
            m_Version++;
        }
        public void RemoveTransformPath(Transform transform) => RemoveTransformPath(transform, true);
        public void RemoveTransformPath(Transform transform, bool recursive)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            var path = RelativePath(transform.root, transform);
            Elements.Items.RemoveAll(i => i is YMap m && ((m["m_Path"] as YScalar)?.Value is string p) && (p == path || recursive && p.StartsWith(path.Length == 0 ? "" : path + "/")));
            m_Version++;
        }

        internal static string RelativePath(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>Whether a binding on <paramref name="target"/> (under <paramref name="animatorRoot"/>) is animated by a layer using this mask.</summary>
        internal bool Allows(Transform animatorRoot, Transform target)
        {
            if (Elements.Items.Count == 0 || target == null) return true;
            if (m_ActiveCache == null || m_CacheVersion != m_Version)
            {
                m_ActiveCache = new HashSet<string>(Elements.Items.OfType<YMap>().Where(m => ((m["m_Weight"] as YScalar)?.F ?? 0) > 0).Select(m => (m["m_Path"] as YScalar)?.Value ?? ""));
                m_CacheVersion = m_Version;
            }
            return m_ActiveCache.Contains(RelativePath(animatorRoot, target));
        }
    }
}
