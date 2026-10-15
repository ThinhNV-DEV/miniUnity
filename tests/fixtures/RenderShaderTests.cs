using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

// 3D software renderer: built-in C# shaders (Unlit/*, Sprites/Default, textured / cutout Standard), MaterialPropertyBlock on a
// renderer, and draws submitted from script (Graphics.DrawMesh, RenderMeshInstanced with per-instance colours).
public class RenderShaderTests
{
    RenderTexture rt;
    Camera cam;

    [SetUp] public void Rig()
    {
        rt = new RenderTexture(64, 64, 24);
        cam = new GameObject("Cam").AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 1; cam.transform.position = new Vector3(0, 0, -10);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.targetTexture = rt;
    }

    Color32 Pixel(int x, int y)
    {
        cam.Render();
        var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); tex.Apply(); RenderTexture.active = null;
        return tex.GetPixel(x, y);
    }

    static GameObject Quad(Material m, Vector3 pos, float size = 2)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.transform.position = pos; q.transform.localScale = Vector3.one * size;
        q.GetComponent<Renderer>().sharedMaterial = m;
        return q;
    }

    static Texture2D HalfTransparent(Color c)
    {
        var t = new Texture2D(2, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        t.SetPixel(0, 0, new Color(c.r, c.g, c.b, 0)); t.SetPixel(1, 0, c); t.Apply();
        return t;
    }

    [Test] public void UnlitColorIsFlat()
    {
        var m = new Material(Shader.Find("Unlit/Color")); m.color = new Color(1, 0, 0, 1);
        Quad(m, Vector3.zero);
        var p = Pixel(32, 32);
        Assert.AreEqual(255, p.r); Assert.AreEqual(0, p.g); Assert.AreEqual(0, p.b);
    }

    [Test] public void CutoutDiscardsTransparentTexels()
    {
        var m = new Material(Shader.Find("Unlit/Transparent Cutout")); m.mainTexture = HalfTransparent(Color.green);
        Quad(m, Vector3.zero);
        Assert.AreEqual(0, Pixel(16, 32).g, "left half: alpha 0, discarded");
        Assert.AreEqual(255, Pixel(48, 32).g, "right half drawn");
    }

    [Test] public void SpritesDefaultBlendsByAlpha()
    {
        var back = new Material(Shader.Find("Unlit/Color")); back.color = Color.blue;
        Quad(back, new Vector3(0, 0, 1));
        var m = new Material(Shader.Find("Sprites/Default")); m.color = new Color(1, 1, 1, 0.5f);
        Quad(m, Vector3.zero);
        var p = Pixel(32, 32);
        Assert.Greater(p.r, 150); Assert.Greater(p.b, 150, "half of the blue shows through");
    }

    [Test] public void StandardSamplesItsAlbedoTexture()
    {
        var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(0, 0, 0);
        var m = new Material(Shader.Find("Standard"));
        var t = new Texture2D(1, 1); t.SetPixel(0, 0, Color.green); t.Apply();
        m.mainTexture = t;
        Quad(m, Vector3.zero);
        var p = Pixel(32, 32);
        Assert.Greater(p.g, p.r + 60, "green albedo (plus a white specular highlight: the light is behind the camera)"); Assert.Greater(p.g, p.b + 60);
        m.mainTexture = null;
        var white = Pixel(32, 32);
        Assert.Greater(white.r, p.r + 60, "without the texture the albedo is white");
    }

    [Test] public void PropertyBlockOverridesTheRendererColour()
    {
        var m = new Material(Shader.Find("Unlit/Color")); m.color = Color.red;
        var r = Quad(m, Vector3.zero).GetComponent<Renderer>();
        var b = new MaterialPropertyBlock(); b.SetColor("_Color", Color.green);
        r.SetPropertyBlock(b);
        var p = Pixel(32, 32);
        Assert.AreEqual(0, p.r); Assert.AreEqual(255, p.g);
    }

    [Test] public void DrawMeshIsRendered()
    {
        var m = new Material(Shader.Find("Unlit/Color")); m.color = Color.white;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var mesh = quad.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(quad);
        Graphics.DrawMesh(mesh, Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * 2), m, 0);
        Assert.AreEqual(255, Pixel(32, 32).r);
    }

    [Test] public void RenderMeshInstancedUsesPerInstanceColours()
    {
        var m = new Material(Shader.Find("Unlit/Color"));
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var mesh = quad.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(quad);
        var block = new MaterialPropertyBlock();
        block.SetVectorArray("_Color", new[] { new Vector4(1, 0, 0, 1), new Vector4(0, 0, 1, 1) });
        var rp = new RenderParams(m) { matProps = block };
        var matrices = new[] { Matrix4x4.Translate(new Vector3(-0.5f, 0, 0)), Matrix4x4.Translate(new Vector3(0.5f, 0, 0)) };
        Graphics.RenderMeshInstanced(rp, mesh, 0, matrices);
        var left = Pixel(16, 32); var right = Pixel(48, 32);
        Assert.AreEqual(255, left.r); Assert.AreEqual(0, left.b);
        Assert.AreEqual(0, right.r); Assert.AreEqual(255, right.b);
    }

    [Test] public void MaterialTextureScaleAndOffset()
    {
        var m = new Material(Shader.Find("Unlit/Texture"));
        m.SetTextureScale("_MainTex", new Vector2(2, 3)); m.mainTextureOffset = new Vector2(0.5f, 0.25f);
        Assert.AreEqual(new Vector2(2, 3), m.GetTextureScale("_MainTex"));
        Assert.AreEqual(new Vector2(0.5f, 0.25f), m.GetTextureOffset(Shader.PropertyToID("_MainTex")));
    }

    [Test] public void ColorSpaceComesFromPlayerSettings()
    {
        Assert.AreEqual(ColorSpace.Linear, QualitySettings.activeColorSpace, "the fixture project's ProjectSettings are linear");
        var b = new MaterialPropertyBlock();
        b.SetColor("_C", new Color(0.5f, 0.5f, 0.5f, 1));
        Assert.AreEqual(Mathf.GammaToLinearSpace(0.5f), b.GetVector("_C").x, 1e-5f, "SetColor stores the linear value");
        Assert.AreEqual(0.5f, b.GetColor("_C").r, 1e-4f, "GetColor gives the sRGB value back");
    }
}
