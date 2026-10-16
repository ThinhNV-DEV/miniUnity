using NUnit.Framework;
using UnityEngine;

// World-space TextMeshPro (TextMeshPro component on a MeshRenderer): meshes whose material uses the TMP SDF shader are drawn with
// the SDF text shader (coverage from the atlas alpha, face colour, alpha blended, depth tested), not as lit Standard quads.
// The fixture project has a stub "TextMeshPro/Mobile/Distance Field" shader; the atlas is a hand-made 2-texel SDF.
public class WorldTextTests
{
    const int N = 64;
    RenderTexture rt; Camera cam;

    [SetUp] public void Rig()
    {
        rt = new RenderTexture(N, N, 24);
        cam = new GameObject("Cam").AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 1; cam.transform.position = new Vector3(0, 0, -10);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.targetTexture = rt;
    }

    [TearDown] public void Clean() { Object.DestroyImmediate(cam.gameObject); rt.Release(); }

    Color32 Pixel(int x, int y)
    {
        cam.Render();
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, N, N), 0, 0); tex.Apply(); RenderTexture.active = null;
        return tex.GetPixel(x, y);
    }

    static GameObject TextQuad(Color face)
    {
        var shader = Shader.Find("TextMeshPro/Mobile/Distance Field");
        Assert.IsNotNull(shader, "stub TMP shader in the fixture project");
        // SDF atlas: left texel inside the glyph (alpha 1), right texel outside (alpha 0); point filtered
        var atlas = new Texture2D(2, 1, TextureFormat.Alpha8, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        atlas.SetPixel(0, 0, new Color(0, 0, 0, 1)); atlas.SetPixel(1, 0, new Color(0, 0, 0, 0)); atlas.Apply();
        var m = new Material(shader);
        m.SetTexture("_MainTex", atlas); m.SetColor("_FaceColor", face);
        m.SetFloat("_GradientScale", 5); m.SetFloat("_TextureWidth", 2); m.SetFloat("_TextureHeight", 1);
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        mesh.SetUVs(1, new[] { new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1) });   // uv1.y: glyph scale
        mesh.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
        mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        var go = new GameObject("Text");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        return go;
    }

    [Test] public void SdfCoverageNotAFilledQuad()
    {
        var go = TextQuad(Color.white);
        var inside = Pixel(8, 32); var outside = Pixel(56, 32);
        Assert.Greater(inside.r, 240, "glyph texel: face colour");
        Assert.Less(outside.r, 15, "outside the glyph: background shows through (a Standard quad would be lit grey)");
        Object.DestroyImmediate(go);
    }

    [Test] public void TextIsDepthTestedAgainstOpaqueGeometry()
    {
        var go = TextQuad(Color.white);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
        wall.transform.position = new Vector3(-0.5f, 0, -1); wall.transform.localScale = new Vector3(1, 2, 1);
        var wm = new Material(Shader.Find("Unlit/Color")); wm.color = Color.blue;
        wall.GetComponent<Renderer>().sharedMaterial = wm;
        var hidden = Pixel(8, 32);
        Assert.AreEqual(255, hidden.b, "the quad in front hides the text");
        Assert.Less(hidden.r, 15);
        Object.DestroyImmediate(wall); Object.DestroyImmediate(go);
    }
}
