using NUnit.Framework;
using UnityEngine;

// Camera MSAA: the fixture project's quality level ("Ultra", antiAliasing 2) multisamples a camera rendering to a render texture
// made by script (Unity's HDR intermediate): an edge through a pixel centre covers one of the two samples → 50 % in linear
// space (sRGB 188 for white on black). Off with QualitySettings.antiAliasing = 0 or Camera.allowMSAA = false.
public class MsaaTests
{
    const int N = 64;
    RenderTexture rt; Camera cam; GameObject quad;

    [SetUp] public void Rig()
    {
        rt = new RenderTexture(N, N, 24);
        cam = new GameObject("Cam").AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 1; cam.transform.position = new Vector3(0, 0, -10);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.targetTexture = rt;
        // white quad from the left border to x = 32.5 px (32 px per unit): its right edge runs through the centres of column 32
        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        float right = 32.5f / 32 - 1;
        quad.transform.position = new Vector3((-1 + right) / 2, 0, 0); quad.transform.localScale = new Vector3(right + 1, 2, 1);
        var m = new Material(Shader.Find("Unlit/Color")) { color = Color.white };
        quad.GetComponent<Renderer>().sharedMaterial = m;
    }

    [TearDown] public void Clean()
    {
        QualitySettings.antiAliasing = 2;
        Object.DestroyImmediate(quad); Object.DestroyImmediate(cam.gameObject); rt.Release();
    }

    Color32 Pixel(int x)
    {
        cam.Render();
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, N, N), 0, 0); tex.Apply(); RenderTexture.active = null;
        return tex.GetPixel(x, N / 2);
    }

    [Test] public void QualityLevelHasTwoSamples() => Assert.AreEqual(2, QualitySettings.antiAliasing);

    [Test] public void EdgeThroughPixelCentreIsHalfCovered()
    {
        Assert.AreEqual(255, Pixel(31).r, "inside");
        Assert.AreEqual(188, Pixel(32).r, 2, "one of two samples: 0.5 linear");
        Assert.AreEqual(0, Pixel(33).r, "outside");
    }

    [Test] public void NoMsaaWhenTheQualityLevelHasNone()
    {
        QualitySettings.antiAliasing = 0;
        var p = Pixel(32).r;
        Assert.IsTrue(p == 0 || p == 255, $"single sample: covered or not, got {p}");
    }

    [Test] public void NoMsaaWhenTheCameraDisallowsIt()
    {
        cam.allowMSAA = false;
        var p = Pixel(32).r;
        Assert.IsTrue(p == 0 || p == 255, $"single sample: covered or not, got {p}");
    }
}
