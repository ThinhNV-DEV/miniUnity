using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// Nested prefabs and prefab variants written by SaveAsPrefabAsset and loaded back; PrefabUtility queries; AssetDatabase move / copy.
public class PrefabEditorTests
{
    const string Dir = "Assets/PrefabFixture";

    [SetUp] public void Folder() { if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "PrefabFixture"); }
    [TearDown] public void Clean() { AssetDatabase.DeleteAsset(Dir); }

    static GameObject Leaf()
    {
        var a = new GameObject("Leaf");
        var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
        child.name = "Body"; child.transform.SetParent(a.transform, false);
        a.AddComponent<BoxCollider>().size = new Vector3(1, 2, 3);
        return a;
    }

    [Test] public void NestedPrefabIsWrittenAsAPrefabInstance()
    {
        var leaf = PrefabUtility.SaveAsPrefabAsset(Leaf(), Dir + "/Leaf.prefab");
        var holder = new GameObject("Holder");
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(leaf, holder.transform);
        inst.transform.localPosition = new Vector3(5, 0, 0);
        inst.GetComponent<BoxCollider>().size = new Vector3(7, 7, 7);
        var holderAsset = PrefabUtility.SaveAsPrefabAsset(holder, Dir + "/Holder.prefab");

        var text = File.ReadAllText(Dir + "/Holder.prefab");
        StringAssert.Contains("--- !u!1001 ", text, "the leaf stays a nested PrefabInstance");
        StringAssert.Contains(AssetDatabase.AssetPathToGUID(Dir + "/Leaf.prefab"), text);
        StringAssert.DoesNotContain("m_Name: Body", text, "the leaf's own objects are not copied into the holder");

        var nested = holderAsset.transform.Find("Leaf");
        Assert.NotNull(nested, "the nested instance is loaded");
        Assert.NotNull(nested.Find("Body"), "with its children");
        Assert.AreEqual(new Vector3(5, 0, 0), nested.localPosition);
        Assert.AreEqual(new Vector3(7, 7, 7), nested.GetComponent<BoxCollider>().size, "with the override");
        Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(nested.gameObject));
        Assert.AreEqual(leaf, PrefabUtility.GetCorrespondingObjectFromSource(nested.gameObject));

        var placed = (GameObject)PrefabUtility.InstantiatePrefab(holderAsset);
        var placedLeaf = placed.transform.Find("Leaf").gameObject;
        Assert.IsTrue(PrefabUtility.IsOutermostPrefabInstanceRoot(placed));
        Assert.IsFalse(PrefabUtility.IsOutermostPrefabInstanceRoot(placedLeaf));
        Assert.IsTrue(PrefabUtility.IsAnyPrefabInstanceRoot(placedLeaf), "the nested instance root");
        Assert.AreEqual(leaf, PrefabUtility.GetCorrespondingObjectFromOriginalSource(placedLeaf));
        Assert.NotNull(PrefabUtility.GetPrefabInstanceHandle(placedLeaf));
        Assert.AreSame(PrefabUtility.GetPrefabInstanceHandle(placed), PrefabUtility.GetPrefabInstanceHandle(placedLeaf));
        Assert.IsNull(PrefabUtility.GetPrefabInstanceHandle(new GameObject("Plain")));
    }

    [Test] public void SavingAnInstanceRootMakesAVariant()
    {
        var leaf = PrefabUtility.SaveAsPrefabAsset(Leaf(), Dir + "/Leaf.prefab");
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(leaf);
        Assert.IsFalse(PrefabUtility.HasPrefabInstanceAnyOverrides(inst, false));
        inst.GetComponent<BoxCollider>().size = new Vector3(4, 4, 4);
        new GameObject("Extra").transform.SetParent(inst.transform, false);
        Assert.IsTrue(PrefabUtility.HasPrefabInstanceAnyOverrides(inst, false));
        var variant = PrefabUtility.SaveAsPrefabAsset(inst, Dir + "/LeafVariant.prefab");

        Assert.AreEqual(PrefabAssetType.Variant, PrefabUtility.GetPrefabAssetType(variant));
        Assert.AreEqual(PrefabAssetType.Regular, PrefabUtility.GetPrefabAssetType(leaf));
        Assert.IsTrue(PrefabUtility.IsPartOfVariantPrefab(variant));
        Assert.AreEqual(new Vector3(4, 4, 4), variant.GetComponent<BoxCollider>().size);
        Assert.NotNull(variant.transform.Find("Extra"), "added child");
        Assert.NotNull(variant.transform.Find("Body"), "base child");
        Assert.AreEqual(leaf, PrefabUtility.GetCorrespondingObjectFromSource(variant));
    }

    [Test] public void PrefabContentsKeepNestedInstances()
    {
        var leaf = PrefabUtility.SaveAsPrefabAsset(Leaf(), Dir + "/Leaf.prefab");
        var holder = new GameObject("Holder");
        PrefabUtility.InstantiatePrefab(leaf, holder.transform);
        PrefabUtility.SaveAsPrefabAsset(holder, Dir + "/Holder.prefab");

        var contents = PrefabUtility.LoadPrefabContents(Dir + "/Holder.prefab");
        new GameObject("Added").transform.SetParent(contents.transform, false);
        PrefabUtility.SaveAsPrefabAsset(contents, Dir + "/Holder.prefab");
        PrefabUtility.UnloadPrefabContents(contents);
        var text = File.ReadAllText(Dir + "/Holder.prefab");
        StringAssert.Contains("--- !u!1001 ", text, "still nested after an edit through LoadPrefabContents");
        StringAssert.Contains("m_Name: Added", text);
    }

    [Test] public void TilemapTilesSurviveSaveAndLoad()
    {
        var tile = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
        tile.colliderType = UnityEngine.Tilemaps.Tile.ColliderType.Grid; tile.color = Color.green;
        AssetDatabase.CreateAsset(tile, Dir + "/Ground.asset");
        var grid = new GameObject("Level").AddComponent<Grid>();
        var tm = new GameObject("Ground").AddComponent<UnityEngine.Tilemaps.Tilemap>();
        tm.transform.SetParent(grid.transform, false);
        tm.SetTile(new Vector3Int(2, 3, 0), tile); tm.SetTile(new Vector3Int(-1, 0, 0), tile);
        var saved = PrefabUtility.SaveAsPrefabAsset(grid.gameObject, Dir + "/Level.prefab");
        StringAssert.Contains("m_TileAssetArray", File.ReadAllText(Dir + "/Level.prefab"));
        var map = saved.GetComponentInChildren<UnityEngine.Tilemaps.Tilemap>();
        Assert.AreEqual(2, map.GetTilesRangeCount(new Vector3Int(-9, -9, 0), new Vector3Int(9, 9, 0)));
        Assert.AreEqual(AssetDatabase.LoadAssetAtPath<UnityEngine.Tilemaps.Tile>(Dir + "/Ground.asset"), map.GetTile(new Vector3Int(2, 3, 0)));
        Assert.AreEqual(Color.green, map.GetColor(new Vector3Int(2, 3, 0)));
        Assert.AreEqual(new BoundsInt(-1, 0, 0, 4, 4, 1), map.cellBounds);
    }

    [Test] public void MoveAndCopyAssets()
    {
        var leaf = PrefabUtility.SaveAsPrefabAsset(Leaf(), Dir + "/Leaf.prefab");
        var guid = AssetDatabase.AssetPathToGUID(Dir + "/Leaf.prefab");
        Assert.AreEqual("", AssetDatabase.MoveAsset(Dir + "/Leaf.prefab", Dir + "/Renamed.prefab"));
        Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(Dir + "/Renamed.prefab"), "GUID kept");
        Assert.AreEqual(Dir + "/Renamed.prefab", AssetDatabase.GetAssetPath(leaf), "loaded object follows the move");
        Assert.IsFalse(File.Exists(Dir + "/Leaf.prefab"));
        StringAssert.Contains("does already exist", AssetDatabase.MoveAsset(Dir + "/Renamed.prefab", Dir + "/Renamed.prefab"));
        StringAssert.Contains("could not be found", AssetDatabase.MoveAsset(Dir + "/Nope.prefab", Dir + "/X.prefab"));

        Assert.IsTrue(AssetDatabase.CopyAsset(Dir + "/Renamed.prefab", Dir + "/Copy.prefab"));
        var copyGuid = AssetDatabase.AssetPathToGUID(Dir + "/Copy.prefab");
        Assert.IsNotEmpty(copyGuid); Assert.AreNotEqual(guid, copyGuid, "a copy gets a new GUID");
        Assert.NotNull(AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Copy.prefab"));
    }
}
