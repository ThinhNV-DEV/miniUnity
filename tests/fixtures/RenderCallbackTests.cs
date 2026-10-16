using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Play-mode render phase: camera callbacks in Unity's order each frame, OnBecameVisible / Invisible / OnWillRenderObject from
// frustum culling, WaitForEndOfFrame after rendering, OnRenderImage chains and Graphics.Blit.
public class RenderCallbackTests
{
    static readonly List<string> log = new List<string>();

    class CamHooks : MonoBehaviour
    {
        void LateUpdate() => log.Add("LateUpdate");
        void OnPreCull() => log.Add("OnPreCull");
        void OnPreRender() => log.Add("OnPreRender:" + (Camera.current == GetComponent<Camera>()));
        void OnPostRender() => log.Add("OnPostRender");
    }

    class Watcher : MonoBehaviour
    {
        public int visible, invisible, willRender;
        void OnBecameVisible() => visible++;
        void OnBecameInvisible() => invisible++;
        void OnWillRenderObject() => willRender++;
    }

    class EndOfFrame : MonoBehaviour
    {
        IEnumerator Start() { while (true) { yield return new WaitForEndOfFrame(); log.Add("EndOfFrame"); } }
    }

    class Tint : MonoBehaviour
    {
        public Material mat;
        void OnRenderImage(RenderTexture src, RenderTexture dst) { log.Add("OnRenderImage"); Graphics.Blit(src, dst, mat); }
    }

    static Camera Cam()
    {
        var cam = new GameObject("Cam").AddComponent<Camera>();
        cam.transform.position = new Vector3(0, 0, -10);
        return cam;
    }

    [UnityTest] public IEnumerator CameraCallbacksRunInUnitysOrderEveryFrame()
    {
        log.Clear();
        var cam = Cam();
        cam.gameObject.AddComponent<CamHooks>();
        cam.gameObject.AddComponent<EndOfFrame>();
        int delegates = 0;
        Camera.CameraCallback cb = c => { if (c == cam) delegates++; };
        Camera.onPreCull += cb;
        try
        {
            yield return null; log.Clear();
            yield return null;
            CollectionAssert.AreEqual(new[] { "LateUpdate", "OnPreCull", "OnPreRender:True", "OnPostRender", "EndOfFrame" }, log);
            Assert.GreaterOrEqual(delegates, 2, "Camera.onPreCull");
            Assert.IsNull(Camera.current, "only set while the camera renders");
        }
        finally { Camera.onPreCull -= cb; }
    }

    [UnityTest] public IEnumerator VisibilityFollowsTheFrustum()
    {
        Cam();
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var w = cube.AddComponent<Watcher>();
        yield return null; yield return null;
        Assert.AreEqual(1, w.visible); Assert.AreEqual(0, w.invisible);
        Assert.GreaterOrEqual(w.willRender, 2, "once per camera per frame while visible");
        cube.transform.position = new Vector3(500, 0, 0);
        yield return null;
        Assert.AreEqual(1, w.invisible);
        int wr = w.willRender;
        yield return null;
        Assert.AreEqual(wr, w.willRender, "not rendered off screen");
        cube.transform.position = Vector3.zero;
        yield return null;
        Assert.AreEqual(2, w.visible);
    }

    [UnityTest] public IEnumerator OnRenderImageProcessesTheCameraImage()
    {
        log.Clear();
        var cam = Cam();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.blue;
        var rt = new RenderTexture(32, 32, 24); cam.targetTexture = rt;
        var red = new Material(Shader.Find("Unlit/Color")) { color = Color.red };
        cam.gameObject.AddComponent<Tint>().mat = red;
        yield return null;
        cam.Render();
        var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); tex.Apply(); RenderTexture.active = null;
        Color32 p = tex.GetPixel(16, 16);
        Assert.AreEqual(255, p.r); Assert.AreEqual(0, p.b, "the effect replaced the blue background");
        Assert.Contains("OnRenderImage", log);
    }

    [Test] public void BlitCopiesAndScales()
    {
        var src = new Texture2D(2, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        src.SetPixel(0, 0, Color.black); src.SetPixel(1, 0, Color.white); src.Apply();
        var dst = new RenderTexture(8, 4, 0);
        Graphics.Blit(src, dst);
        var tex = new Texture2D(8, 4, TextureFormat.RGBA32, false);
        RenderTexture.active = dst; tex.ReadPixels(new Rect(0, 0, 8, 4), 0, 0); tex.Apply(); RenderTexture.active = null;
        Assert.AreEqual(0, ((Color32)tex.GetPixel(1, 2)).r); Assert.AreEqual(255, ((Color32)tex.GetPixel(6, 2)).r);
    }
}
