using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity
{
    /// <summary>Writes a prefab asset from a GameObject hierarchy (SceneWriter in prefab mode): nested prefab instances are written as
    /// PrefabInstances, and when the root is itself a prefab instance the asset is a prefab variant of that prefab.</summary>
    public static class PrefabWriter
    {
        /// <param name="srcToClone">objects loaded from the asset being overwritten (LoadPrefabContents) → their editable copies: the
        /// copies keep the asset's file ids.</param>
        public static string Write(GameObject root, string path, Dictionary<Object, Object> srcToClone)
        {
            var keep = new Dictionary<Object, long>();
            if (srcToClone != null) foreach (var kv in srcToClone) if (kv.Key.m_Asset != null) keep[kv.Value] = kv.Key.m_Asset.FileID;
            return new SceneWriter(root, keep).Write();
        }
    }
}
