using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniUnity;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    public enum PrefabAssetType { NotAPrefab, Regular, Model, Variant, MissingAsset }
    public enum PrefabInstanceStatus { NotAPrefab, Connected, Disconnected, MissingAsset }
    public enum PrefabUnpackMode { OutermostRoot, Completely }

    /// <summary>Prefab API over MiniUnity's YAML prefab loader + instance tracking.</summary>
    public static class PrefabUtility
    {
        public delegate void PrefabInstanceUpdated(GameObject instance);
        public static PrefabInstanceUpdated prefabInstanceUpdated;

        public static Object InstantiatePrefab(Object assetComponentOrGameObject) => InstantiatePrefab(assetComponentOrGameObject, (Transform)null);
        public static Object InstantiatePrefab(Object assetComponentOrGameObject, Transform parent)
        {
            var src = assetComponentOrGameObject is Component c ? c.gameObject : (GameObject)assetComponentOrGameObject;
            if (src == null || src.m_Asset == null) throw new ArgumentException("InstantiatePrefab: not a prefab asset");
            var map = new Dictionary<Object, Object>();
            var root = ObjectCloner.CloneHierarchy(src, map);
            var inst = new PrefabInstanceData { PrefabAsset = src, PrefabGuid = src.m_Asset.Guid, InstanceRoot = root };
            foreach (var kv in map)
            {
                kv.Value.m_CorrespondingSource = kv.Key;
                inst.Objects.Add(kv.Value);
            }
            root.m_PrefabInstance = inst;
            ObjectCloner.Place(root, parent, false, src);
            Lifecycle.AwakeHierarchy(root);
            return assetComponentOrGameObject is Component ? map[assetComponentOrGameObject] : root;
        }
        public static Object InstantiatePrefab(Object assetComponentOrGameObject, UnityEngine.SceneManagement.Scene destinationScene) => InstantiatePrefab(assetComponentOrGameObject, (Transform)null);

        /// <summary>Editable copy of the prefab contents (not in any scene, not connected).</summary>
        public static GameObject LoadPrefabContents(string assetPath)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) ?? throw new ArgumentException("No prefab at " + assetPath);
            var map = new Dictionary<Object, Object>();
            var root = ObjectCloner.CloneHierarchy(src, map);
            root.m_Name = src.m_Name;
            // the contents keep the asset's nested prefab instances (and a variant's base): same sources, instance data remapped
            foreach (var (s, c) in map)
            {
                c.m_CorrespondingSource = s.m_CorrespondingSource;
                if (s is GameObject sg && sg.m_PrefabInstance is PrefabInstanceData pi && c is GameObject cg)
                    cg.m_PrefabInstance = new PrefabInstanceData
                    {
                        PrefabAsset = pi.PrefabAsset, PrefabGuid = pi.PrefabGuid, InstanceRoot = cg,
                        Objects = pi.Objects.Where(map.ContainsKey).Select(o => map[o]).ToList(),
                    };
            }
            s_Contents[root] = (assetPath, map);
            return root;
        }
        static readonly Dictionary<GameObject, (string path, Dictionary<Object, Object> map)> s_Contents = new();
        public static void UnloadPrefabContents(GameObject contentsRoot) { s_Contents.Remove(contentsRoot); Object.DestroyImmediate(contentsRoot); }

        public static GameObject SaveAsPrefabAsset(GameObject instanceRoot, string assetPath) => SaveAsPrefabAsset(instanceRoot, assetPath, out _);
        public static GameObject SaveAsPrefabAsset(GameObject instanceRoot, string assetPath, out bool success)
        {
            assetPath = AssetIndex.Norm(assetPath);
            var text = PrefabWriter.Write(instanceRoot, assetPath, s_Contents.TryGetValue(instanceRoot, out var c) ? c.map : null);
            SafeIO.WriteAllText(AssetIndex.Abs(assetPath), text);
            AssetIndex.Register(assetPath);
            AssetLoader.Forget(assetPath);
            success = true;
            return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        }
        public static GameObject SaveAsPrefabAssetAndConnect(GameObject instanceRoot, string assetPath, InteractionMode action) => SaveAsPrefabAssetAndConnect(instanceRoot, assetPath, action, out _);
        /// <summary>Save, then turn the scene hierarchy into an instance of the new asset (Unity writes it as a PrefabInstance).</summary>
        public static GameObject SaveAsPrefabAssetAndConnect(GameObject instanceRoot, string assetPath, InteractionMode action, out bool success)
        {
            var asset = SaveAsPrefabAsset(instanceRoot, assetPath, out success);
            if (asset == null || instanceRoot.m_InPrefabAsset) return asset;
            var inst = new PrefabInstanceData { PrefabAsset = asset, PrefabGuid = asset.m_Asset.Guid, InstanceRoot = instanceRoot };
            bool Map(GameObject a, GameObject b)
            {
                if (a.m_Transform.m_Children.Count != b.m_Transform.m_Children.Count) return false;
                var ca = a.m_Components.Where(c => !c.m_Destroyed).ToList(); var cb = b.m_Components.ToList();
                if (ca.Count != cb.Count) return false;
                for (int i = 0; i < ca.Count; i++) if (ca[i].GetType() != cb[i].GetType()) return false;
                a.m_CorrespondingSource = b; inst.Objects.Add(a);
                for (int i = 0; i < ca.Count; i++) { ca[i].m_CorrespondingSource = cb[i]; inst.Objects.Add(ca[i]); }
                for (int i = 0; i < a.m_Transform.m_Children.Count; i++)
                    if (!Map(a.m_Transform.m_Children[i].gameObject, b.m_Transform.m_Children[i].gameObject)) return false;
                return true;
            }
            if (Map(instanceRoot, asset)) instanceRoot.m_PrefabInstance = inst;
            else
            {
                foreach (var o in inst.Objects) o.m_CorrespondingSource = null;
                Debug.LogWarning($"[MiniUnity] SaveAsPrefabAssetAndConnect: hierarchy of '{instanceRoot.name}' differs from the saved asset – left unconnected");
            }
            return asset;
        }

        public static GameObject GetRootGameObject(Component component) => component?.transform.root.gameObject;
        public static bool IsPartOfPrefabAsset(Object componentOrGameObject) => componentOrGameObject != null && componentOrGameObject.m_Asset != null && (componentOrGameObject is GameObject || componentOrGameObject is Component);
        public static bool IsPartOfPrefabInstance(Object componentOrGameObject) => componentOrGameObject?.m_CorrespondingSource != null;
        public static bool IsPartOfAnyPrefab(Object o) => IsPartOfPrefabAsset(o) || IsPartOfPrefabInstance(o);
        public static bool IsPartOfNonAssetPrefabInstance(Object o) => IsPartOfPrefabInstance(o);
        /// <summary>Root of a prefab instance: the outermost one (which holds the instance) or a nested one (its source is the root of an
        /// instance inside the outer prefab).</summary>
        public static bool IsAnyPrefabInstanceRoot(GameObject go)
        {
            if (go == null || go.m_CorrespondingSource == null && go.m_PrefabInstance == null) return false;
            if (go.m_PrefabInstance != null) return true;
            return go.m_CorrespondingSource is GameObject s && (s.m_PrefabInstance != null || IsAnyPrefabInstanceRoot(s));
        }
        public static bool IsOutermostPrefabInstanceRoot(GameObject go) => go?.m_PrefabInstance != null;
        public static T GetCorrespondingObjectFromSource<T>(T componentOrGameObject) where T : Object => componentOrGameObject?.m_CorrespondingSource as T;
        /// <summary>Follows the chain of sources (instance → nested prefab → … → the prefab the object was created in).</summary>
        public static T GetCorrespondingObjectFromOriginalSource<T>(T componentOrGameObject) where T : Object
        {
            Object o = componentOrGameObject?.m_CorrespondingSource;
            if (o == null) return null;
            while (o.m_CorrespondingSource != null) o = o.m_CorrespondingSource;
            return o as T;
        }
        public static Object GetPrefabParent(Object obj) => obj?.m_CorrespondingSource;
        /// <summary>The PrefabInstance object of the outermost instance the object belongs to (null when it is not in one).</summary>
        public static Object GetPrefabInstanceHandle(Object instanceComponentOrGameObject)
        {
            var root = GetOutermostPrefabInstanceRoot(instanceComponentOrGameObject);
            var inst = root?.m_PrefabInstance;
            if (inst == null || !IsPartOfPrefabInstance(instanceComponentOrGameObject)) return null;
            return inst.Handle ??= new PrefabInstanceHandle { m_Name = root.m_Name + " (Prefab Instance)" };
        }
        public static GameObject GetOutermostPrefabInstanceRoot(Object componentOrGameObject)
        {
            var t = componentOrGameObject is GameObject g ? g.transform : (componentOrGameObject as Component)?.transform;
            GameObject r = null;
            for (; t != null; t = t.m_Parent) if (t.gameObject.m_PrefabInstance != null) r = t.gameObject;
            return r;
        }
        public static GameObject GetNearestPrefabInstanceRoot(Object componentOrGameObject)
        {
            var t = componentOrGameObject is GameObject g ? g.transform : (componentOrGameObject as Component)?.transform;
            for (; t != null; t = t.m_Parent) if (t.gameObject.m_PrefabInstance != null) return t.gameObject;
            return null;
        }
        /// <summary>Regular, Variant (the asset's root is itself a prefab instance), Model (imported model) or NotAPrefab; an instance
        /// object reports the type of the prefab it comes from.</summary>
        public static PrefabAssetType GetPrefabAssetType(Object componentOrGameObject)
        {
            if (!IsPartOfAnyPrefab(componentOrGameObject)) return PrefabAssetType.NotAPrefab;
            var a = componentOrGameObject.m_Asset != null ? componentOrGameObject : componentOrGameObject.m_CorrespondingSource;
            if (a?.m_Asset == null) return PrefabAssetType.MissingAsset;
            var path = a.m_Asset.Path ?? "";
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return PrefabAssetType.Model;
            var root = (a is Component c ? c.gameObject : a as GameObject)?.transform.root.gameObject;
            return root != null && root.m_PrefabInstance != null ? PrefabAssetType.Variant : PrefabAssetType.Regular;
        }
        public static bool IsPartOfVariantPrefab(Object componentOrGameObject) => GetPrefabAssetType(componentOrGameObject) == PrefabAssetType.Variant;
        public static bool IsPartOfRegularPrefab(Object componentOrGameObject) => GetPrefabAssetType(componentOrGameObject) == PrefabAssetType.Regular;
        public static bool IsPartOfModelPrefab(Object componentOrGameObject) => GetPrefabAssetType(componentOrGameObject) == PrefabAssetType.Model;
        public static bool IsAddedGameObjectOverride(GameObject go)
        {
            if (go == null || go.m_CorrespondingSource != null) return false;
            var p = go.transform.parent;
            return p != null && GetOutermostPrefabInstanceRoot(p.gameObject) != null;
        }
        public static bool IsAddedComponentOverride(Object component) => component is Component c && c.m_CorrespondingSource == null && IsPartOfPrefabInstance(c.gameObject);
        public static PrefabInstanceStatus GetPrefabInstanceStatus(Object componentOrGameObject) => IsPartOfPrefabInstance(componentOrGameObject) ? PrefabInstanceStatus.Connected : PrefabInstanceStatus.NotAPrefab;
        public static string GetPrefabAssetPathOfNearestInstanceRoot(Object instance) => GetNearestPrefabInstanceRoot(instance)?.m_PrefabInstance?.PrefabAsset.m_Asset.Path ?? "";
        public static void RecordPrefabInstancePropertyModifications(Object targetObject) { }
        public static void UnpackPrefabInstance(GameObject instanceRoot, PrefabUnpackMode unpackMode, InteractionMode action)
        {
            foreach (var o in instanceRoot.m_PrefabInstance?.Objects ?? new List<Object>()) o.m_CorrespondingSource = null;
            instanceRoot.m_PrefabInstance = null;
        }
        /// <summary>Property modifications (the root's position / rotation / name / parent count only with includeDefaultOverrides),
        /// added or removed objects.</summary>
        public static bool HasPrefabInstanceAnyOverrides(GameObject instanceRoot, bool includeDefaultOverrides)
        {
            var inst = instanceRoot?.m_PrefabInstance;
            if (inst == null) return false;
            var root = inst.InstanceRoot;
            foreach (var (target, path, _) in PrefabDiff.Modifications(inst))
            {
                bool rootDefault = (target == root.m_CorrespondingSource && path == "m_Name") ||
                                   (target == root.m_Transform.m_CorrespondingSource && (path.StartsWith("m_LocalPosition") || path.StartsWith("m_LocalRotation") || path.StartsWith("m_LocalEulerAnglesHint") || path == "m_RootOrder"
                                    || path.StartsWith("m_AnchoredPosition") || path.StartsWith("m_SizeDelta") || path.StartsWith("m_AnchorMin") || path.StartsWith("m_AnchorMax") || path.StartsWith("m_Pivot")));
                if (includeDefaultOverrides || !rootDefault) return true;
            }
            if (PrefabDiff.Removed(inst).Any()) return true;
            foreach (var o in inst.Objects)
            {
                if (o is GameObject g) { if (g.m_Components.Any(c => !c.m_Destroyed && c.m_CorrespondingSource == null)) return true; }
                else if (o is Transform t && t.m_Children.Any(ch => ch.m_CorrespondingSource == null)) return true;
            }
            return false;
        }
    }
}
