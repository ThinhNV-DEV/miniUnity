using System.Linq;
using NUnit.Framework;
using UnityEngine;

// Import results read from Library/Artifacts (binary SerializedFile): run_fixtures.py links Unity's own unity_builtin_extra in as
// the artifact of Assets/Models/Builtin.fbx, so these are Unity's real Default-Skybox and UISprite objects.
public class ImportArtifactEditorTests
{
    static Object[] All()
    {
        var all = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Models/Builtin.fbx");
        if (all == null || all.Length == 0) Assert.Ignore("no Unity Editor data (unity_builtin_extra) in MiniUnity/.cache");
        return all;
    }

    [Test] public void MaterialFromTheArtifactKeepsUnitysValues()
    {
        var sky = All().OfType<Material>().FirstOrDefault(m => m.name == "Default-Skybox");
        Assert.IsNotNull(sky, "Default-Skybox among the artifact's objects");
        Assert.AreEqual(1.3f, sky.GetFloat("_Exposure"), 1e-5f);
        Assert.AreEqual(1f, sky.GetFloat("_SunDisk"), "Default-Skybox uses the simple sun disk");
        Assert.AreEqual(0.369f, sky.GetColor("_GroundColor").r, 1e-4f);
        Assert.IsTrue(sky.IsKeywordEnabled("_SUNDISK_SIMPLE"));
    }

    [Test] public void TextureFromTheArtifactDecodes()
    {
        var tex = All().OfType<Texture2D>().FirstOrDefault(t => t.name == "UISprite");
        Assert.IsNotNull(tex, "UISprite texture");
        Assert.AreEqual(32, tex.width); Assert.AreEqual(32, tex.height);
        var c = tex.GetPixel(16, 16);
        Assert.Greater(c.a, 0.9f, "the inside of the rounded box is opaque");
        Assert.Less(tex.GetPixel(0, 0).a, 0.5f, "its corner is transparent");
    }
}
