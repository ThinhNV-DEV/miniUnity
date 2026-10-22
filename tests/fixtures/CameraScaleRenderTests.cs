using NUnit.Framework;
using UnityEngine;

// A camera's Transform scale does not change what it sees: Unity's view matrix (worldToCameraMatrix) uses position and
// rotation only, so a camera scaled to 0.05 renders exactly like an unscaled one.
public class CameraScaleRenderTests
{
    [Test] public void ScaledCameraRendersLikeUnscaled()
    {
        var tex = new Texture2D(4, 4); var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; tex.SetPixels(px); tex.Apply();
        var sr = new GameObject("Quad").AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);   // 1×1 unit at the origin
        sr.color = Color.red;
        var rt = new RenderTexture(32, 32, 24);
        var cam = new GameObject("Cam").AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 2; cam.aspect = 1;
        cam.transform.position = new Vector3(0, 0, -10); cam.transform.localScale = Vector3.one * 0.05f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.targetTexture = rt;
        cam.Render();
        var t2 = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        RenderTexture.active = rt; t2.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); t2.Apply(); RenderTexture.active = null;
        Assert.AreEqual(255, ((Color32)t2.GetPixel(16, 16)).r, "the quad at the centre");
        Assert.AreEqual(0, ((Color32)t2.GetPixel(4, 4)).r, "4 units of view: the 1-unit quad covers only the middle quarter");
        Assert.AreEqual(0, ((Color32)t2.GetPixel(24, 16)).r);
    }
}
