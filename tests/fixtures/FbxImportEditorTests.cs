using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// Models read from the .fbx itself when there is no Library/Artifacts import result: Assets/Models/Cube.fbx (ASCII, written for
// these tests: a Null "Body" 100 cm along x with a child cube "Box" rotated 90° about y, two materials, a texture next to the
// model, no normals) and, when the Editor's packages are there, a binary FBX exported by a DCC tool (Models/UVSphere.fbx).
public class FbxImportEditorTests
{
    static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

    [Test] public void HierarchyAndTransformsFollowUnitysConversion()
    {
        var root = Load("Assets/Models/Cube.fbx");
        Assert.IsNotNull(root, "the model asset's main object is its root GameObject");
        Assert.AreEqual("Cube", root.name, "a single top node becomes the root, named after the file");
        Assert.AreEqual(new Vector3(-1, 0, 0), root.transform.localPosition, "cm → m and x mirrored");
        var box = root.transform.Find("Box");
        Assert.IsNotNull(box);
        Assert.Less(Quaternion.Angle(Quaternion.Euler(0, -90, 0), box.localRotation), 0.01f, "rotation about y mirrored");
        Assert.AreEqual(Vector3.one, box.localScale);
    }

    [Test] public void MeshIsSplitPerFaceWithOutwardNormals()
    {
        var mesh = Load("Assets/Models/Cube.fbx").transform.Find("Box").GetComponent<MeshFilter>().sharedMesh;
        Assert.IsNotNull(mesh);
        Assert.AreEqual("Box", mesh.name);
        Assert.AreEqual(24, mesh.vertexCount, "computed normals (60°) split every corner per face");
        Assert.AreEqual(2, mesh.subMeshCount);
        Assert.AreEqual(4 * 2 * 3, mesh.GetTriangles(0).Length);
        Assert.AreEqual(2 * 2 * 3, mesh.GetTriangles(1).Length);
        Assert.AreEqual(Vector3.one, mesh.bounds.size);
        var v = mesh.vertices; var n = mesh.normals; var t = mesh.triangles;
        for (int i = 0; i < v.Length; i++) Assert.Greater(Vector3.Dot(n[i], v[i]), 0, "normal points away from the centre");
        // Unity's winding (clockwise seen from outside, left-handed): (b-a)×(c-a) points outwards, as RecalculateNormals has it
        for (int i = 0; i < t.Length; i += 3)
        {
            var c = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            Assert.Greater(Vector3.Dot(c, v[t[i]] + v[t[i + 1]] + v[t[i + 2]]), 0);
        }
        CollectionAssert.Contains(mesh.uv, new Vector2(1, 1));
    }

    [Test] public void MaterialsComeFromTheFile()
    {
        var mr = Load("Assets/Models/Cube.fbx").transform.Find("Box").GetComponent<MeshRenderer>();
        var mats = mr.sharedMaterials;
        CollectionAssert.AreEqual(new[] { "Red", "Checker" }, mats.Select(m => m.name).ToArray());
        Assert.AreEqual("Standard", mats[0].shader.name);
        var red = mats[0].GetColor("_Color");
        Assert.AreEqual(1f, red.r, 1e-3); Assert.AreEqual(0f, red.g, 1e-3);
        var tex = mats[1].GetTexture("_MainTex");
        Assert.IsNotNull(tex, "the texture found next to the model (Textures/checker.png)");
        Assert.AreEqual("Assets/Models/Textures/checker.png", AssetDatabase.GetAssetPath(tex));
        var subs = AssetDatabase.LoadAllAssetsAtPath("Assets/Models/Cube.fbx");
        Assert.IsTrue(subs.OfType<Mesh>().Any(m => m.name == "Box"));
        Assert.IsTrue(subs.OfType<Material>().Any(m => m.name == "Checker"));
    }

    [Test] public void InstancesCarryTheModel()
    {
        var go = (GameObject)Object.Instantiate(Load("Assets/Models/Cube.fbx"));
        try
        {
            var mf = go.GetComponentInChildren<MeshFilter>();
            Assert.AreEqual(24, mf.sharedMesh.vertexCount);
            Assert.AreEqual(new Vector3(-1, 0, 0), go.transform.position);
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test] public void BinaryFbxFromATool()
    {
        var root = Load("Assets/Models/UVSphere.fbx");
        if (root == null) Assert.Ignore("Editor package samples not available");
        var mf = root.GetComponentInChildren<MeshFilter>();
        Assert.IsNotNull(mf);
        var b = mf.sharedMesh.bounds;
        Assert.Greater(mf.sharedMesh.vertexCount, 100);
        Assert.AreEqual(b.size.x, b.size.y, b.size.x * 0.05f, "a sphere");
        Assert.AreEqual(b.size.x, b.size.z, b.size.x * 0.05f);
        Assert.Greater(Vector3.Dot(mf.sharedMesh.normals[0], mf.sharedMesh.vertices[0] - b.center), 0, "outward normals");
    }
}
