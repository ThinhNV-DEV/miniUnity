using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

// Skybox clear, ambient from the sky (AmbientMode.Skybox) and the default reflection, with the Default-Skybox
// (Skybox/Procedural) of a new scene and a sun like Unity's default Directional Light.
public class SkyboxTests
{
    const int N = 64;
    RenderTexture rt;
    Camera cam;
    Light sun;

    [SetUp] public void Rig()
    {
        rt = new RenderTexture(N, N, 24);
        cam = new GameObject("Cam").AddComponent<Camera>();
        cam.fieldOfView = 90; cam.clearFlags = CameraClearFlags.Skybox; cam.backgroundColor = Color.red; cam.targetTexture = rt;
        sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1; sun.color = new Color(1, .957f, .839f);
        sun.transform.rotation = Quaternion.Euler(50, -30, 0);
    }

    [TearDown] public void Clean() { Object.DestroyImmediate(cam.gameObject); Object.DestroyImmediate(sun.gameObject); rt.Release(); }

    static int s_Dumps;
    Texture2D Render()
    {
        cam.Render();
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, N, N), 0, 0); tex.Apply(); RenderTexture.active = null;
        var dump = System.Environment.GetEnvironmentVariable("MU_SKY_PNG");
        if (!string.IsNullOrEmpty(dump)) System.IO.File.WriteAllBytes($"{dump}_{++s_Dumps}.png", tex.EncodeToPNG());
        return tex;
    }

    [Test] public void NewSceneUsesTheProceduralDefaultSkybox()
    {
        Assert.IsNotNull(RenderSettings.skybox);
        Assert.AreEqual("Default-Skybox", RenderSettings.skybox.name);
        Assert.AreEqual("Skybox/Procedural", RenderSettings.skybox.shader.name);
        Assert.AreEqual(AmbientMode.Skybox, RenderSettings.ambientMode);
    }

    [Test] public void SkyboxClearDrawsSkyHorizonAndGround()
    {
        cam.transform.rotation = Quaternion.identity;   // looking along +Z: upper half sky, lower half ground
        var t = Render();
        Color up = t.GetPixel(N / 2, N - 2), horizon = t.GetPixel(N / 2, N / 2 + 1), ground = t.GetPixel(N / 2, 1);
        Assert.Greater(up.b, up.r, "sky is blue");
        Assert.Greater(horizon.r + horizon.g + horizon.b, up.r + up.g + up.b, "brighter towards the horizon");
        Assert.Less(Mathf.Abs(ground.r - ground.b), 0.12f, "ground is greyish");
        Assert.AreNotEqual(Color.red, t.GetPixel(N / 2, N / 2), "not the background colour");
    }

    [Test] public void SunDiskIsBrightestWhereTheSunIs()
    {
        cam.transform.rotation = Quaternion.LookRotation(-sun.transform.forward);
        var t = Render();
        var c = t.GetPixel(N / 2, N / 2);
        Assert.Greater(c.r + c.g + c.b, 2.7f, "looking straight at the sun");
    }

    [Test] public void WithoutSkyboxMaterialTheBackgroundColourIsUsed()
    {
        RenderSettings.skybox = null;
        try { Assert.AreEqual((Color)new Color32(255, 0, 0, 255), (Color)(Color32)Render().GetPixel(N / 2, N / 2)); }
        finally { RenderSettings.skybox = SkyOf(); }
    }

    static Material s_Sky;
    static Material SkyOf() => s_Sky;
    [OneTimeSetUp] public void Remember() => s_Sky = RenderSettings.skybox;

    GameObject Ball(float metallic, float smoothness)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.transform.position = new Vector3(0, 0, 2);
        var m = new Material(Shader.Find("Standard")) { color = Color.white };
        m.SetFloat("_Metallic", metallic); m.SetFloat("_Glossiness", smoothness);
        ball.GetComponent<Renderer>().sharedMaterial = m;
        cam.transform.rotation = Quaternion.identity;
        cam.fieldOfView = 40;
        return ball;
    }

    [Test] public void SkyAmbientLightsFromAbove()
    {
        sun.enabled = false;
        var ball = Ball(0, 0);
        var t = Render();
        Color top = t.GetPixel(N / 2, N / 2 + 18), bottom = t.GetPixel(N / 2, N / 2 - 18);
        Assert.Greater(top.b, top.r, "top lit by the blue sky");
        Assert.Greater(top.r + top.g + top.b, 0.05f);
        RenderSettings.skybox = null;   // Skybox mode without a skybox: the flat Ambient Color
        try
        {
            var flat = Render();
            Color a = flat.GetPixel(N / 2, N / 2 + 18), b = flat.GetPixel(N / 2, N / 2 - 18);
            Assert.AreEqual(a.r, b.r, 2 / 255f, "flat ambient: same top and bottom");
        }
        finally { RenderSettings.skybox = SkyOf(); Object.DestroyImmediate(ball); }
    }

    [Test] public void MirrorBallReflectsTheSky()
    {
        sun.enabled = false;
        var ball = Ball(1, 1);
        var t = Render();
        var top = t.GetPixel(N / 2, N / 2 + 18);
        Assert.Greater(top.r + top.g + top.b, 0.3f, "metal reflects the sky");
        RenderSettings.skybox = null;
        try
        {
            var dark = Render().GetPixel(N / 2, N / 2 + 18);
            Assert.Less(dark.r + dark.g + dark.b, top.r + top.g + top.b, "no skybox: no reflection");
        }
        finally { RenderSettings.skybox = SkyOf(); Object.DestroyImmediate(ball); }
    }
}
