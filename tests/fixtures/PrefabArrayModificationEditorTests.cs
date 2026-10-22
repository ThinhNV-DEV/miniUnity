using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// A prefab instance growing an int list that is empty in its source: the source writes `indices: ` (an empty hex string), the
// instance `indices.Array.size` and only the elements that are not 0 (`indices.Array.data[1]`).
public class PrefabArrayModificationEditorTests
{
    // IndexListHolder lives in the runtime test assembly, which this Editor assembly does not reference
    static List<int> Indices(string prefab)
    {
        var c = AssetDatabase.LoadAssetAtPath<GameObject>(prefab).GetComponent("IndexListHolder");
        return (List<int>)c.GetType().GetField("indices").GetValue(c);
    }

    [Test] public void SizeAndDataModificationsGrowAnEmptyPrimitiveList()
    {
        const string script = "3c5e7a9b1d2f4e6a8b0c2d4e6f8a1b3c";   // IndexListHolder.cs.meta
        const string basePath = "Assets/PlayTests/IndexBase.prefab", instPath = "Assets/PlayTests/IndexHolder.prefab";
        File.WriteAllText(basePath, $@"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &10
GameObject:
  m_ObjectHideFlags: 0
  serializedVersion: 6
  m_Component:
  - component: {{fileID: 11}}
  - component: {{fileID: 12}}
  m_Layer: 0
  m_Name: IndexBase
  m_IsActive: 1
--- !u!4 &11
Transform:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: 10}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_Children: []
  m_Father: {{fileID: 0}}
--- !u!114 &12
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: 10}}
  m_Enabled: 1
  m_Script: {{fileID: 11500000, guid: {script}, type: 3}}
  indices: 
");
        AssetDatabase.ImportAsset(basePath);
        var baseGuid = AssetDatabase.AssetPathToGUID(basePath);
        File.WriteAllText(instPath, $@"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1001 &100
PrefabInstance:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Modification:
    serializedVersion: 3
    m_TransformParent: {{fileID: 0}}
    m_Modifications:
    - target: {{fileID: 12, guid: {baseGuid}, type: 3}}
      propertyPath: indices.Array.size
      value: 3
      objectReference: {{fileID: 0}}
    - target: {{fileID: 12, guid: {baseGuid}, type: 3}}
      propertyPath: 'indices.Array.data[1]'
      value: 7
      objectReference: {{fileID: 0}}
    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {{fileID: 100100000, guid: {baseGuid}, type: 3}}
");
        AssetDatabase.ImportAsset(instPath);
        try
        {
            CollectionAssert.AreEqual(new[] { 0, 7, 0 }, Indices(instPath));
            Assert.AreEqual(0, Indices(basePath).Count, "the source stays empty");
        }
        finally { AssetDatabase.DeleteAsset(instPath); AssetDatabase.DeleteAsset(basePath); }
    }
}
