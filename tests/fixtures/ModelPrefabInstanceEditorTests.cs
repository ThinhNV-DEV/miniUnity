using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// A prefab whose content is an instance of a model (.fbx): Unity treats the model as a prefab, its root objects having the same
// file ids in every model (GameObject 919132149155446097, Transform -8679921383154817045), so the instance's modifications and
// stripped stand-ins target them.
public class ModelPrefabInstanceEditorTests
{
    const string Cube = "6a1f0c3a9b7d4e2f8a6c0b1d3e5f7a03";
    const string Path = "Assets/Models/CubeHolder.prefab";

    [Test] public void ModelRootHasUnitysFixedFileIds()
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Cube.fbx");
        Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(root, out _, out long go));
        Assert.AreEqual(919132149155446097, go);
        Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(root.transform, out _, out long tr));
        Assert.AreEqual(-8679921383154817045, tr);
    }

    [Test] public void PrefabInstanceOfAModelLoads()
    {
        File.WriteAllText(Path, $@"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1001 &100
PrefabInstance:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Modification:
    serializedVersion: 3
    m_TransformParent: {{fileID: 0}}
    m_Modifications:
    - target: {{fileID: 919132149155446097, guid: {Cube}, type: 3}}
      propertyPath: m_Name
      value: Holder
      objectReference: {{fileID: 0}}
    - target: {{fileID: -8679921383154817045, guid: {Cube}, type: 3}}
      propertyPath: m_LocalPosition.y
      value: 2
      objectReference: {{fileID: 0}}
    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {{fileID: 100100000, guid: {Cube}, type: 3}}
");
        AssetDatabase.ImportAsset(Path);
        try
        {
            var holder = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            Assert.IsNotNull(holder, "the model is the prefab's content");
            Assert.AreEqual("Holder", holder.name);
            Assert.AreEqual(new Vector3(-1, 2, 0), holder.transform.localPosition, "modification of the model root's Transform");
            var box = holder.transform.Find("Box");
            Assert.IsNotNull(box);
            Assert.AreEqual(24, box.GetComponent<MeshFilter>().sharedMesh.vertexCount, "the model's mesh comes along");
        }
        finally { AssetDatabase.DeleteAsset(Path); }
    }
}

// ScriptableSingleton<T>.instance: one instance per type, created on first use.
public class ScriptableSingletonEditorTests
{
    class Settings : ScriptableSingleton<Settings> { public int Value = 7; }

    [Test] public void InstanceIsCreatedOnceAndShared()
    {
        Assert.IsNotNull(Settings.instance);
        Assert.AreSame(Settings.instance, Settings.instance);
        Assert.AreEqual(7, Settings.instance.Value);
    }
}
