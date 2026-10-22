using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

// SortingGroup: renderers inside a group sort with the group's sorting layer / order against everything outside it,
// whatever their own order in layer (a cell sprite of order 0 in a group of order 3 draws over a board of order 1).
public class SortingGroupRenderTests
{
    static SpriteRenderer Quad(string name, Color color, int order)
    {
        var tex = new Texture2D(4, 4); var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; tex.SetPixels(px); tex.Apply();
        var sr = new GameObject(name).AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
        sr.color = color; sr.sortingOrder = order;
        return sr;
    }

    static Color32 Centre()
    {
        var rt = new RenderTexture(16, 16, 24);
        var cam = new GameObject("Cam").AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 1; cam.aspect = 1; cam.transform.position = new Vector3(0, 0, -10);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.targetTexture = rt;
        cam.Render();
        var t = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        RenderTexture.active = rt; t.ReadPixels(new Rect(0, 0, 16, 16), 0, 0); t.Apply(); RenderTexture.active = null;
        Object.DestroyImmediate(cam.gameObject);
        return t.GetPixel(8, 8);
    }

    [Test] public void GroupOrderBeatsTheRenderersOwnOrder()
    {
        Quad("Board", Color.red, 1);
        var cell = Quad("Cell", Color.green, 0);
        Assert.AreEqual(255, Centre().r, "without a group the board (order 1) is on top");
        var g = cell.gameObject.AddComponent<SortingGroup>();
        g.sortingOrder = 3;
        Assert.AreEqual(3, g.sortingOrder);
        Color32 c = Centre();
        Assert.AreEqual(255, c.g, "the cell's group (order 3) draws over the board"); Assert.AreEqual(0, c.r);
        g.sortingOrder = 0;
        Assert.AreEqual(255, Centre().r, "group order 0 sorts below the board again");
    }
}
