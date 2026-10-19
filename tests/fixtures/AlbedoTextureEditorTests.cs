using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 3D shaders sample the albedo texture: Standard's _MainTex and URP-style _BaseMap / _BaseColor (shaders MiniUnity has no port
// for draw as Standard), from PNG, JPEG (baseline / progressive) and TGA files, and on an FBX model's imported materials.
// Textures under Assets/Models/Textures are 16×16: left half red, right half blue; point filtered.
public class AlbedoTextureEditorTests
{
    const int N = 32;
    RenderTexture rt; Camera cam; GameObject quad, light;

    [SetUp] public void Rig()
    {
        rt = new RenderTexture(N, N, 24);
        cam = new GameObject("Cam").AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 0.5f; cam.transform.position = new Vector3(0, 0, -10);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.targetTexture = rt;
        light = new GameObject("Light");
        var l = light.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1; light.transform.forward = Vector3.forward;
        RenderSettings.ambientLight = Color.white;
        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
    }

    [TearDown] public void Clean() { Object.DestroyImmediate(quad); Object.DestroyImmediate(cam.gameObject); Object.DestroyImmediate(light); rt.Release(); }

    Color32 Pixel(int x, int y)
    {
        cam.Render();
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, N, N), 0, 0); tex.Apply(); RenderTexture.active = null;
        return tex.GetPixel(x, y);
    }

    void AssertHalves(string what)
    {
        Color32 left = Pixel(8, 16), right = Pixel(24, 16);
        Assert.Greater(left.r, left.b + 60, what + ": left half red, got " + left);
        Assert.Greater(right.b, right.r + 60, what + ": right half blue, got " + right);
    }

    static Texture2D Tex(string file)
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Models/Textures/" + file);
        Assert.IsNotNull(t, file);
        return t;
    }

    [TestCase("halves.jpg")]
    [TestCase("halves_prog.jpg")]
    [TestCase("halves.tga")]
    public void StandardSamplesMainTex(string file)
    {
        var t = Tex(file);
        Assert.AreEqual(16, t.width); Assert.AreEqual(16, t.height);
        var px = t.GetPixel(2, 8);
        Assert.Greater(px.r, 0.8f, "decoded pixels"); Assert.Less(px.b, 0.25f);
        var m = new Material(Shader.Find("Standard"));
        m.SetTexture("_MainTex", t);
        quad.GetComponent<MeshRenderer>().sharedMaterial = m;
        AssertHalves(file);
    }

    [Test] public void UrpStyleBaseMapIsSampled()
    {
        var m = new Material(Shader.Find("Standard"));
        m.shader = Shader.Find("Universal Render Pipeline/Lit") ?? m.shader;
        m.SetTexture("_BaseMap", Tex("halves.jpg"));
        m.SetColor("_BaseColor", Color.white);
        quad.GetComponent<MeshRenderer>().sharedMaterial = m;
        AssertHalves("_BaseMap");
    }

    [Test] public void TintMultipliesTheTexture()
    {
        var m = new Material(Shader.Find("Standard"));
        m.SetTexture("_MainTex", Tex("halves.tga"));
        quad.GetComponent<MeshRenderer>().sharedMaterial = m;
        var plain = Pixel(8, 16);
        m.SetColor("_Color", new Color(0, 0, 1));
        var tinted = Pixel(8, 16);
        Assert.Less(tinted.r, plain.r - 80, $"red × blue tint darkens the red half ({plain} → {tinted})");
    }

    [Test] public void FbxMaterialTextureIsDrawn()
    {
        var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Cube.fbx"));
        try
        {
            // the Box's textured faces (material 1, the checker) are its ±x faces
            model.transform.position = Vector3.zero;
            var box = model.transform.Find("Box");
            box.localPosition = Vector3.zero;
            box.localRotation = Quaternion.Euler(0, 90, 0);   // turn the checker faces towards the camera
            Object.DestroyImmediate(quad); quad = new GameObject("none");
            cam.orthographicSize = 0.6f;
            cam.Render();
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, N, N), 0, 0); tex.Apply(); RenderTexture.active = null;
            int white = 0, blue = 0;
            foreach (Color32 p in tex.GetPixels32())
            {
                if (p.r > 200 && p.g > 200 && p.b > 200) white++;
                else if (p.b > p.r + 60 && p.b > p.g + 60) blue++;
            }
            // the 4×4 checker (white / blue) fills the face
            Assert.Greater(white, 150, "white cells"); Assert.Greater(blue, 150, "blue cells");
        }
        finally { Object.DestroyImmediate(model); }
    }
}
