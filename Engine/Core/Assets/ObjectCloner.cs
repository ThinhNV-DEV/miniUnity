using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity.Yaml;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity
{
    /// <summary>Instantiate = serialize + deserialize with internal references remapped (as Unity does).</summary>
    public static class ObjectCloner
    {
        public static Transform TransformOf(Object o) => o switch { GameObject g => g.m_Transform, Component c => c.m_GameObject?.m_Transform, _ => null };

        public static Object Instantiate(Object original, Transform parent, bool worldStays)
        {
            if (!Object.IsAlive(original)) throw new ArgumentException("The Object you want to instantiate is null.");
            if (original is GameObject || original is Component)
            {
                var srcGo = original is GameObject g ? g : ((Component)original).m_GameObject;
                var map = new Dictionary<Object, Object>();
                var root = CloneHierarchy(srcGo, map);
                root.m_Name = srcGo.m_Name + "(Clone)";
                Place(root, parent, worldStays, srcGo);
                Lifecycle.AwakeHierarchy(root);
                return original is GameObject ? root : map[original];
            }
            // assets / scriptable objects
            var clone = CloneSingle(original);
            clone.m_Name = original.m_Name + "(Clone)";
            return clone;
        }

        public static void Place(GameObject root, Transform parent, bool worldStays, GameObject src)
        {
            if (parent != null) root.m_Transform.SetParent(parent, worldStays);
            else { var s = SceneData.Active; root.m_Scene = s; s?.AddRoot(root); }
        }

        public static Object CloneSingle(Object original)
        {
            Object clone;
            if (original is ScriptableObject) clone = Lifecycle.CreateScriptableObject(original.GetType(), false);
            else clone = (Object)Activator.CreateInstance(original.GetType(), true);
            var body = Serializer.Serialize(original);
            Serializer.Deserialize(clone, body, (r, t) => r.Raw ? AssetLoader.ResolveExternal(r) : r.Target);
            if (clone is ScriptableObject so) { Lifecycle.Invoke(so, "Awake"); Lifecycle.Invoke(so, "OnEnable"); }
            if (original.m_Native != null && clone.m_Native == null) clone.m_Native = (YMap)original.m_Native.Clone();
            if (original is Texture2D t2 && clone is Texture2D c2) { c2.m_W = t2.width; c2.m_H = t2.height; c2.m_Pixels = (Color[])t2.m_Pixels?.Clone(); }
            return clone;
        }

        /// <summary>Clone a GameObject hierarchy. map: source object → clone (GameObjects + components).</summary>
        public static GameObject CloneHierarchy(GameObject src, Dictionary<Object, Object> map)
        {
            var srcObjs = new List<Object>();
            foreach (var go in Hier.Walk(src))
            {
                srcObjs.Add(go);
                foreach (var c in go.m_Components) if (!c.m_Destroyed) srcObjs.Add(c);
            }
            foreach (var o in srcObjs)
            {
                Object clone = o is GameObject ? AssetLoader.CreateBareGameObject() : (Object)Activator.CreateInstance(o.GetType(), true);
                map[o] = clone;
            }
            Object Resolve(YRef r, Type expected)
            {
                if (r.Raw) return r.FileID == 0 ? null : AssetLoader.ResolveExternal(r);
                if (r.Target == null) return null;
                return map.TryGetValue(r.Target, out var m) ? m : r.Target;
            }
            foreach (var o in srcObjs)
            {
                var body = Serializer.Serialize(o);
                var clone = map[o];
                Serializer.Deserialize(clone, body, Resolve);
                if (clone is Component cc && o is Component oc) cc.m_GameObject = (GameObject)map[oc.m_GameObject];
            }
            var root = (GameObject)map[src];
            root.m_Transform.m_Parent = null; Hier.Version++;
            return root;
        }
    }
}
