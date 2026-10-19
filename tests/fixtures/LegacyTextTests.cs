using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

// uGUI's legacy Text: Font (dynamic glyph atlas, Font.textureRebuilt) and TextGenerator (rich text, word wrap, vertical
// truncation, best fit, alignment), and the Text component drawn on a canvas. The builtin font (LegacyRuntime.ttf / Arial)
// is stood in by Liberation Sans from the Editor's packages, which has Arial's metrics.
public class LegacyTextTests
{
    static Font Builtin => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    static TextGenerationSettings Settings(Vector2 extents, TextAnchor anchor = TextAnchor.UpperLeft, int size = 14) => new TextGenerationSettings
    {
        font = Builtin, fontSize = size, color = Color.white, scaleFactor = 1, lineSpacing = 1, richText = true, textAnchor = anchor,
        generationExtents = extents, pivot = new Vector2(0.5f, 0.5f), horizontalOverflow = HorizontalWrapMode.Wrap, verticalOverflow = VerticalWrapMode.Overflow,
        resizeTextMinSize = 10, resizeTextMaxSize = 40,
    };

    [Test] public void BuiltinFontHasArialMetrics()
    {
        var f = Builtin;
        Assert.IsNotNull(f);
        Assert.IsTrue(f.dynamic);
        Assert.AreEqual(16, f.fontSize);
        Assert.IsTrue(f.HasCharacter('A'));
        f.RequestCharactersInTexture("A", 32);
        Assert.IsTrue(f.GetCharacterInfo('A', out var a, 32));
        Assert.AreEqual(21, a.advance, "Arial 'A' advances 1366/2048 em");
        Assert.That(a.glyphHeight, Is.InRange(22, 25), "cap height ≈ 0.716 em");
        Assert.AreEqual(0, a.minY, 1);
        Assert.IsFalse(f.GetCharacterInfo('Z', out _, 32), "only requested characters are in the texture");
        Assert.IsNotNull(f.material.mainTexture);
        Assert.AreEqual(TextureFormat.Alpha8, ((Texture2D)f.material.mainTexture).format);
    }

    [Test] public void GeneratesAQuadPerVisibleCharacter()
    {
        var g = new TextGenerator();
        Assert.IsTrue(g.Populate("Hi you", Settings(new Vector2(200, 50))));
        Assert.AreEqual(5 * 4, g.vertexCount, "no quad for the space");
        Assert.AreEqual(7, g.characterCount, "one per character plus the end caret");
        Assert.AreEqual(1, g.lineCount);
        var v = g.verts;
        Assert.Less(v[0].position.x, v[4].position.x, "left to right");
        Assert.AreEqual(v[0].position.y, v[1].position.y, "top edge");
        Assert.Greater(v[0].position.y, v[3].position.y, "top above bottom");
        Assert.AreEqual(-100f, g.characters[0].cursorPos.x, 1f, "upper left of a 200 wide rect around its pivot");
        Assert.Greater(g.GetPreferredWidth("Hi you", Settings(Vector2.zero)), 30);
    }

    [Test] public void WrapsAtSpacesAndTruncatesWholeLines()
    {
        var g = new TextGenerator();
        var s = Settings(new Vector2(40, 1000));
        g.Populate("aaa bbb ccc", s);
        Assert.AreEqual(3, g.lineCount);
        Assert.AreEqual(4, g.lines[1].startCharIdx);
        Assert.AreEqual(8, g.lines[2].startCharIdx);
        Assert.Greater(g.lines[0].topY, g.lines[1].topY);
        Assert.AreEqual(9 * 4, g.vertexCount);

        s.verticalOverflow = VerticalWrapMode.Truncate;
        s.generationExtents = new Vector2(40, g.lines[0].height + 2);
        g.Populate("aaa bbb ccc", s);
        Assert.AreEqual(3 * 4, g.vertexCount, "only the first line fits");
        Assert.AreEqual(4, g.characterCountVisible);

        s.horizontalOverflow = HorizontalWrapMode.Overflow; s.verticalOverflow = VerticalWrapMode.Overflow;
        g.Populate("aaa bbb ccc", s);
        Assert.AreEqual(1, g.lineCount, "overflow: no wrapping");
        g.Populate("one\ntwo", s);
        Assert.AreEqual(2, g.lineCount, "explicit newline");
    }

    [Test] public void RichTextTagsColourAndDrawNothing()
    {
        var g = new TextGenerator();
        g.Populate("<color=#ff0000>A</color>B<b>C</b>", Settings(new Vector2(200, 50)));
        Assert.AreEqual(3 * 4, g.vertexCount);
        Assert.AreEqual(new Color32(255, 0, 0, 255), g.verts[0].color);
        Assert.AreEqual(new Color32(255, 255, 255, 255), g.verts[4].color);
        var plain = new TextGenerator(); plain.Populate("C", Settings(new Vector2(200, 50)));
        var bold = new TextGenerator(); bold.Populate("<b>C</b>", Settings(new Vector2(200, 50)));
        Assert.Greater(bold.verts[1].position.x - bold.verts[0].position.x, plain.verts[1].position.x - plain.verts[0].position.x, "bold glyphs are wider");
        var off = Settings(new Vector2(400, 50)); off.richText = false;
        g.Populate("<b>C</b>", off);
        Assert.AreEqual(8 * 4, g.vertexCount, "rich text off: tags are text");
    }

    [Test] public void AlignsAndFitsTheBestSize()
    {
        var g = new TextGenerator();
        g.Populate("Middle", Settings(new Vector2(300, 100), TextAnchor.MiddleCenter, 20));
        float minX = g.verts.Min(v => v.position.x), maxX = g.verts.Max(v => v.position.x);
        float minY = g.verts.Min(v => v.position.y), maxY = g.verts.Max(v => v.position.y);
        Assert.AreEqual(0, (minX + maxX) / 2, 3, "centred horizontally");
        Assert.AreEqual(0, (minY + maxY) / 2, 6, "centred vertically");

        var s = Settings(new Vector2(60, 30)); s.resizeTextForBestFit = true;
        g.Populate("Fit me", s);
        Assert.That(g.fontSizeUsedForBestFit, Is.InRange(11, 30), "between the min and max, below the 40 max");
        Assert.LessOrEqual(g.lines.Sum(l => l.height), 30);
        Assert.LessOrEqual(g.verts.Max(v => v.position.x) - g.verts.Min(v => v.position.x), 61);
    }

    [Test] public void GrowingTheAtlasRaisesTextureRebuilt()
    {
        var font = Font.CreateDynamicFontFromOSFont("NoSuchFont", 16);
        var tex = Builtin.material.mainTexture;
        int calls = 0;
        System.Action<Font> on = f => { if (f == Builtin) calls++; };
        Font.textureRebuilt += on;
        try
        {
            int w = tex.width * tex.height;
            for (int size = 40; size < 120 && tex.width * tex.height == w; size += 10)
                Builtin.RequestCharactersInTexture("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", size);
            Assert.Greater(tex.width * tex.height, w);
            Assert.GreaterOrEqual(calls, 1);
            Assert.IsTrue(Builtin.GetCharacterInfo('A', out var a, 32), "glyphs survive the rebuild");
            Assert.Greater(a.uvTopRight.x, a.uvBottomLeft.x);
        }
        finally { Font.textureRebuilt -= on; }
        Assert.IsNotNull(font);
    }

    [Test] public void TextComponentDrawsOnACanvas()
    {
        const int N = 64;
        var rt = new RenderTexture(N, N, 24);
        var cam = new GameObject("Cam").AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 1; cam.transform.position = new Vector3(0, 0, -10);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.targetTexture = rt;
        var canvas = new GameObject("Canvas", typeof(RectTransform)).AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 5;
        var text = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
        text.transform.SetParent(canvas.transform, false);
        text.rectTransform.sizeDelta = new Vector2(N, N);
        text.font = Builtin; text.fontSize = 48; text.text = "WM"; text.color = Color.green;
        text.alignment = TextAnchor.MiddleCenter; text.horizontalOverflow = HorizontalWrapMode.Overflow;
        Canvas.ForceUpdateCanvases();
        Assert.Greater(text.cachedTextGenerator.vertexCount, 0);
        Assert.Greater(text.preferredWidth, 10);
        cam.Render();
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, N, N), 0, 0); tex.Apply(); RenderTexture.active = null;
        var px = tex.GetPixels32();
        int green = px.Count(p => p.g > 200 && p.r < 40 && p.b < 40);
        Assert.Greater(green, 50, "the glyphs are drawn in the text colour");
        Assert.AreEqual(new Color32(0, 0, 0, 255), (Color32)tex.GetPixel(0, N - 1), "background untouched");
        Object.DestroyImmediate(canvas.gameObject); Object.DestroyImmediate(cam.gameObject); rt.Release();
    }
}
