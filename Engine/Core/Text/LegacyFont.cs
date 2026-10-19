// uGUI's legacy Text: UnityEngine.Font as a dynamic font (glyph bitmaps rasterised on request into an Alpha8 atlas that
// grows and fires Font.textureRebuilt, like Unity's FreeType-backed font texture) and UnityEngine.TextGenerator (rich text,
// word wrap, vertical truncation, best fit, alignment, line spacing) producing the vertices, characters and lines
// UnityEngine.UI.Text / InputField read. Glyphs come from the font asset's .ttf / .otf; the builtin font (Arial /
// LegacyRuntime.ttf, not part of the downloaded Editor subset) is stood in by Liberation Sans, which has Arial's metrics.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniUnity;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

#pragma warning disable CS0618   // CharacterInfo.uv / vert / width: the storage behind its public properties

namespace UnityEngine
{
    public partial class Font
    {
        internal int m_FontSize = 16;
        internal LegacyFontFace m_Face;
        Material m_Material;

        public Font() { }
        public Font(string name) { m_Name = name; }

        internal LegacyFontFace Face => m_Face ??= new LegacyFontFace(this);
        public Material material
        {
            get
            {
                if (m_Material == null && Face.Valid)
                    m_Material = new Material(ShaderIndex.Find("GUI/Text Shader")) { name = "Font Material", mainTexture = Face.Texture };
                return m_Material;
            }
            set => m_Material = value;
        }
        public bool dynamic => true;
        public int fontSize => m_FontSize;
        public string[] fontNames { get; set; } = Array.Empty<string>();
        public int ascent => Face.Valid ? Mathf.RoundToInt(Face.Ascent(m_FontSize)) : 0;
        public int lineHeight => Face.Valid ? Mathf.RoundToInt(Face.LineHeight(m_FontSize)) : 0;
        public bool HasCharacter(char c) => Face.Valid && Face.Has(c);
        public CharacterInfo[] characterInfo { get => Face.All(); set { } }
        public FontTextureRebuildCallback textureRebuildCallback { get; set; }
        public static event Action<Font> textureRebuilt;
        internal static void InvokeTextureRebuilt_Internal(Font font) { textureRebuilt?.Invoke(font); font.textureRebuildCallback?.Invoke(); }

        public void RequestCharactersInTexture(string characters) => RequestCharactersInTexture(characters, 0, FontStyle.Normal);
        public void RequestCharactersInTexture(string characters, int size) => RequestCharactersInTexture(characters, size, FontStyle.Normal);
        public void RequestCharactersInTexture(string characters, int size, FontStyle style)
        {
            if (characters == null || !Face.Valid) return;
            bool rebuilt = false;
            foreach (var c in characters) rebuilt |= Face.Request(c, size <= 0 ? m_FontSize : size, style);
            if (rebuilt) InvokeTextureRebuilt_Internal(this);
        }
        public bool GetCharacterInfo(char ch, out CharacterInfo info) => GetCharacterInfo(ch, out info, 0, FontStyle.Normal);
        public bool GetCharacterInfo(char ch, out CharacterInfo info, int size) => GetCharacterInfo(ch, out info, size, FontStyle.Normal);
        public bool GetCharacterInfo(char ch, out CharacterInfo info, int size, FontStyle style)
        {
            info = default;
            return Face.Valid && Face.TryGet(ch, size <= 0 ? m_FontSize : size, style, out info);
        }

        public static int GetMaxVertsForString(string str) => (str?.Length ?? 0) * 4 + 4;
        internal static Font GetDefault() => LegacyFontFace.Builtin;
        public static string[] GetOSInstalledFontNames() => LegacyFontFace.OSFonts().Select(Path.GetFileNameWithoutExtension).ToArray();
        public static string[] GetPathsToOSFonts() => LegacyFontFace.OSFonts().ToArray();
        public static Font CreateDynamicFontFromOSFont(string fontname, int size) => CreateDynamicFontFromOSFont(new[] { fontname }, size);
        public static Font CreateDynamicFontFromOSFont(string[] fontnames, int size)
        {
            var f = new Font(fontnames?.FirstOrDefault() ?? "Arial") { m_FontSize = Math.Max(1, size), fontNames = fontnames ?? Array.Empty<string>() };
            foreach (var n in f.fontNames)
                if (LegacyFontFace.OSFonts().FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), n, StringComparison.OrdinalIgnoreCase)) is string p) { f.m_Face = new LegacyFontFace(f, p); break; }
            return f;
        }
    }

    public partial class TextGenerator
    {
        readonly List<UIVertex> m_Verts = new();
        readonly List<UICharInfo> m_Chars = new();
        readonly List<UILineInfo> m_Lines = new();
        string m_LastString; TextGenerationSettings m_LastSettings; bool m_HasGenerated, m_LastValid;
        int m_VisibleChars, m_BestFitSize;
        Rect m_Extents;

        public IList<UIVertex> verts => m_Verts;
        public IList<UICharInfo> characters => m_Chars;
        public IList<UILineInfo> lines => m_Lines;
        public int vertexCount => m_Verts.Count;
        public int characterCount => m_Chars.Count;
        public int lineCount => m_Lines.Count;
        public int characterCountVisible => m_VisibleChars;
        public int fontSizeUsedForBestFit => m_BestFitSize;
        public Rect rectExtents => m_Extents;
        public void Invalidate() => m_HasGenerated = false;
        internal static void InvalidateAll() { }
        public void Dispose() { }
        public void GetVertices(List<UIVertex> vertices) { vertices.Clear(); vertices.AddRange(m_Verts); }
        public void GetCharacters(List<UICharInfo> characters) { characters.Clear(); characters.AddRange(m_Chars); }
        public void GetLines(List<UILineInfo> lines) { lines.Clear(); lines.AddRange(m_Lines); }
        public UIVertex[] GetVerticesArray() => m_Verts.ToArray();
        public UICharInfo[] GetCharactersArray() => m_Chars.ToArray();
        public UILineInfo[] GetLinesArray() => m_Lines.ToArray();

        public float GetPreferredWidth(string str, TextGenerationSettings settings)
        {
            settings.horizontalOverflow = HorizontalWrapMode.Overflow; settings.verticalOverflow = VerticalWrapMode.Overflow; settings.updateBounds = true;
            Populate(str, settings);
            return m_Extents.width;
        }
        public float GetPreferredHeight(string str, TextGenerationSettings settings)
        {
            settings.verticalOverflow = VerticalWrapMode.Overflow; settings.updateBounds = true;
            Populate(str, settings);
            return m_Extents.height;
        }

        public bool PopulateWithErrors(string str, TextGenerationSettings settings, GameObject context)
        {
            var error = PopulateWithError(str, settings);
            if (error == TextGenerationError.None) return true;
            if ((error & TextGenerationError.CustomSizeOnNonDynamicFont) != 0) Debug.LogErrorFormat(context, "Font '{0}' is not dynamic, which is required to override its size", settings.font);
            if ((error & TextGenerationError.CustomStyleOnNonDynamicFont) != 0) Debug.LogErrorFormat(context, "Font '{0}' is not dynamic, which is required to override its style", settings.font);
            return false;
        }
        public bool Populate(string str, TextGenerationSettings settings) => PopulateWithError(str, settings) == TextGenerationError.None;

        TextGenerationError PopulateWithError(string str, TextGenerationSettings settings)
        {
            if (m_HasGenerated && str == m_LastString && settings.Equals(m_LastSettings)) return m_LastValid ? TextGenerationError.None : TextGenerationError.NoFont;
            m_LastString = str; m_LastSettings = settings; m_HasGenerated = true;
            m_LastValid = settings.font != null && settings.font.Face.Valid;
            m_Verts.Clear(); m_Chars.Clear(); m_Lines.Clear(); m_VisibleChars = 0;
            if (!m_LastValid) { m_Extents = default; return TextGenerationError.NoFont; }
            LegacyTextLayout.Run(str ?? "", settings, m_Verts, m_Chars, m_Lines, out m_VisibleChars, out m_BestFitSize, out m_Extents);
            return TextGenerationError.None;
        }
    }
}

namespace MiniUnity
{
    /// <summary>One font's glyph cache and atlas: glyphs per (character, pixel size, style), rasterised from the TrueType
    /// outline with exact horizontal coverage over 16 sub-scanlines; synthetic bold / italic like FreeType's embolden and
    /// oblique transforms when the face has no such style.</summary>
    public sealed class LegacyFontFace
    {
        const int Pad = 1;
        readonly Font font;
        readonly TrueTypeFont tt;
        public readonly Texture2D Texture;
        sealed class Entry { public CharacterInfo info; public byte[] cov; public int w, h; public int x, y; }
        readonly Dictionary<(char, int, FontStyle), Entry> glyphs = new();
        readonly List<Entry> order = new();
        int shelfX, shelfY, shelfH;

        public bool Valid => tt != null;

        public LegacyFontFace(Font font, string path = null)
        {
            this.font = font;
            path ??= font.m_Asset?.Path is string p && (p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)) ? AssetIndex.Abs(p) : BuiltinPath();
            try { if (path != null && File.Exists(path)) tt = Faces.TryGetValue(path, out var f) ? f : Faces[path] = TrueTypeFont.Load(path); }
            catch (Exception e) { Debug.LogWarning($"[Font] cannot read '{path}': {e.Message}"); }
            if (tt == null && s_WarnedMissing.Add(font.name)) Debug.LogWarning($"[Font] no font file for '{font.name}': legacy Text with it draws nothing");
            Texture = new Texture2D(256, 256, TextureFormat.Alpha8, false, true) { name = "Font Texture", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < Texture.m_Pixels.Length; i++) Texture.m_Pixels[i] = new Color(1, 1, 1, 0);
        }
        static readonly Dictionary<string, TrueTypeFont> Faces = new();
        static readonly HashSet<string> s_WarnedMissing = new();

        static Font s_Builtin;
        /// <summary>Resources.GetBuiltinResource&lt;Font&gt;("LegacyRuntime.ttf") / the default font.</summary>
        public static Font Builtin => s_Builtin ??= new Font("LegacyRuntime") { m_Asset = new AssetIdentity { Guid = "0000000000000000e000000000000000", FileID = 10102, RefType = 0, Path = "builtin" } };

        static string BuiltinPath()
        {
            var env = Environment.GetEnvironmentVariable("MU_LEGACY_FONT");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            var pk = MiniUnity.Agent.UnityEditorInstall.BuiltInPackages;
            var lib = pk == null ? null : Path.Combine(pk, "com.unity.render-pipelines.high-definition/Samples~/MaterialSamples/TextMesh Pro/Fonts/LiberationSans.ttf");
            if (lib != null && File.Exists(lib)) return lib;
            return OSFonts().FirstOrDefault(f => Path.GetFileName(f) is "LiberationSans-Regular.ttf" or "Arial.ttf" or "arial.ttf" or "DejaVuSans.ttf");
        }

        public static IEnumerable<string> OSFonts()
        {
            foreach (var d in new[] { "/usr/share/fonts", "/usr/local/share/fonts", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts"), "/Library/Fonts", "/System/Library/Fonts", "C:/Windows/Fonts" })
            {
                if (!Directory.Exists(d)) continue;
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)).ToList(); }
                catch { continue; }
                foreach (var f in files) yield return f;
            }
        }

        float K(float size) => size / tt.UnitsPerEm;
        public float Ascent(float size) => tt.Ascender * K(size);
        public float Descent(float size) => tt.Descender * K(size);
        public float LineHeight(float size) => (tt.Ascender - tt.Descender + tt.LineGap) * K(size);
        public bool Has(char c) => tt.GlyphIndex(c) != 0;
        public CharacterInfo[] All() => order.Select(e => e.info).ToArray();

        public bool TryGet(char c, int size, FontStyle style, out CharacterInfo info)
        {
            if (glyphs.TryGetValue((c, size, style), out var e)) { info = e.info; return true; }
            info = default; return false;
        }

        /// <summary>Adds a glyph to the atlas; true when the atlas had to grow (every glyph's uv changed).</summary>
        public bool Request(char c, int size, FontStyle style)
        {
            if (glyphs.ContainsKey((c, size, style))) return false;
            var e = Rasterize(c, size, style);
            glyphs[(c, size, style)] = e; order.Add(e);
            if (e.w == 0) { SetUv(e); return false; }
            bool grew = false;
            while (!Place(e)) { Grow(); grew = true; }
            if (!grew) { Blit(e); SetUv(e); Texture.m_Version++; }
            return grew;
        }

        bool Place(Entry e)
        {
            int W = Texture.width, H = Texture.height, w = e.w + Pad, h = e.h + Pad;
            if (w > W || h > H) return false;
            if (shelfX + w > W) { shelfY += shelfH; shelfX = 0; shelfH = 0; }
            if (shelfY + h > H) return false;
            e.x = shelfX; e.y = shelfY; shelfX += w; shelfH = Math.Max(shelfH, h);
            return true;
        }

        void Grow()
        {
            int W = Texture.width, H = Texture.height;
            if (W <= H) W *= 2; else H *= 2;
            if (W > 8192 || H > 8192) throw new InvalidOperationException("font atlas full");
            Texture.Reinitialize(W, H);
            for (int i = 0; i < Texture.m_Pixels.Length; i++) Texture.m_Pixels[i] = new Color(1, 1, 1, 0);
            shelfX = shelfY = shelfH = 0;
            foreach (var g in order.Where(g => g.w > 0).OrderByDescending(g => g.h).ToList())
                if (!Place(g)) { Grow(); return; }
            foreach (var g in order) { if (g.w > 0) Blit(g); SetUv(g); }
            Texture.m_Version++;
        }

        void Blit(Entry e)
        {
            int W = Texture.width;
            for (int j = 0; j < e.h; j++)
                for (int i = 0; i < e.w; i++)
                    Texture.m_Pixels[(e.y + j) * W + e.x + i] = new Color(1, 1, 1, e.cov[j * e.w + i] / 255f);
        }

        void SetUv(Entry e)
        {
            float W = Texture.width, H = Texture.height;
            e.info.uv = e.w == 0 ? default : new Rect(e.x / W, e.y / H, e.w / W, e.h / H);
            e.info.flipped = false;
        }

        Entry Rasterize(char c, int size, FontStyle style)
        {
            uint gi = tt.GlyphIndex(c);
            var o = FontEngine.LoadOutline(tt, size, gi);
            bool bold = style is FontStyle.Bold or FontStyle.BoldAndItalic, italic = style is FontStyle.Italic or FontStyle.BoldAndItalic;
            var segs = o.segs;
            if (italic) segs = segs.Select(s => (s.x0 + s.y0 * 0.2125f, s.y0, s.x1 + s.y1 * 0.2125f, s.y1)).ToList();
            float adv = o.adv / 64f, embolden = bold ? Math.Max(1f, size / 24f) : 0f;
            var e = new Entry();
            e.info.index = c; e.info.size = size; e.info.style = style;
            e.info.width = (float)Math.Round(adv + embolden, MidpointRounding.AwayFromZero);
            if (segs.Count == 0 || char.IsWhiteSpace(c)) { e.info.vert = new Rect(0, 0, 0, 0); return e; }
            float xMin = segs.Min(s => Math.Min(s.x0, s.x1)), xMax = segs.Max(s => Math.Max(s.x0, s.x1)) + embolden;
            float yMin = segs.Min(s => Math.Min(s.y0, s.y1)), yMax = segs.Max(s => Math.Max(s.y0, s.y1));
            int l = (int)Math.Floor(xMin), r = (int)Math.Ceiling(xMax), b = (int)Math.Floor(yMin), t = (int)Math.Ceiling(yMax);
            int w = Math.Max(1, r - l), h = Math.Max(1, t - b);
            var acc = new float[w * h];
            const int S = 16;
            var xs = new List<(float x, int dir)>();
            for (int j = 0; j < h; j++)
                for (int s = 0; s < S; s++)
                {
                    float y = b + j + (s + 0.5f) / S;
                    xs.Clear();
                    foreach (var (x0, y0, x1, y1) in segs)
                    {
                        if (y0 <= y && y1 > y) xs.Add((x0 + (y - y0) * (x1 - x0) / (y1 - y0), 1));
                        else if (y1 <= y && y0 > y) xs.Add((x0 + (y - y0) * (x1 - x0) / (y1 - y0), -1));
                    }
                    xs.Sort((a, z) => a.x.CompareTo(z.x));
                    int wind = 0; float from = 0;
                    foreach (var (x, dir) in xs)
                    {
                        int before = wind; wind += dir;
                        if (before == 0 && wind != 0) from = x;
                        else if (before != 0 && wind == 0) Span(acc, w, j, from - l, x - l + embolden, 1f / S);
                    }
                }
            e.cov = new byte[w * h];
            for (int i = 0; i < acc.Length; i++) e.cov[i] = (byte)Math.Clamp((int)Math.Round(acc[i] * 255f), 0, 255);
            e.w = w; e.h = h;
            e.info.vert = new Rect(l, t, w, -h);
            return e;
        }

        static void Span(float[] acc, int w, int row, float a, float z, float weight)
        {
            if (z <= a) return;
            a = Math.Max(0, a); z = Math.Min(w, z);
            int i0 = (int)a, i1 = (int)Math.Ceiling(z) - 1;
            for (int i = i0; i <= i1 && i < w; i++)
            {
                float cov = Math.Min(z, i + 1) - Math.Max(a, i);
                if (cov > 0) acc[row * w + i] = Math.Min(1, acc[row * w + i] + cov * weight);
            }
        }
    }

    /// <summary>The legacy text generator's layout, in pixels (sizes and extents times settings.scaleFactor), the rect placed
    /// around the pivot like Unity's: each line's glyphs on integer pen positions (rounded advances), the baseline one ascent
    /// below the line top, lines lineHeight · lineSpacing apart.</summary>
    static class LegacyTextLayout
    {
        struct Item { public int index; public char c; public int size; public FontStyle style; public Color32 color; public bool tag; public int adv; }
        struct Line { public int start, end, next; public int width; public int size; }

        public static void Run(string str, TextGenerationSettings s, List<UIVertex> verts, List<UICharInfo> chars, List<UILineInfo> lines, out int visible, out int bestFit, out Rect extents)
        {
            var font = s.font; var face = font.Face;
            float scale = s.scaleFactor > 0 ? s.scaleFactor : 1;
            Vector2 ext = s.generationExtents * scale;
            int baseSize = s.fontSize > 0 ? s.fontSize : font.fontSize;
            bool wrap = s.horizontalOverflow == HorizontalWrapMode.Wrap, truncate = s.verticalOverflow == VerticalWrapMode.Truncate;

            List<Item> items; List<Line> ls; float totalH; int px;
            if (s.resizeTextForBestFit)
            {
                int lo = Math.Max(1, s.resizeTextMinSize), hi = Math.Max(lo, s.resizeTextMaxSize), best = lo;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    Layout(str, s, face, font, Px(mid, scale), ext, wrap, out var it, out var ln, out var th);
                    bool fits = th <= ext.y + 0.001f && (wrap ? !ln.Any(l => l.width > ext.x + 0.001f) : ln.All(l => l.width <= ext.x + 0.001f));
                    if (fits) { best = mid; lo = mid + 1; } else hi = mid - 1;
                }
                baseSize = best;
            }
            px = Px(baseSize, scale);
            bestFit = px;
            Layout(str, s, face, font, px, ext, wrap, out items, out ls, out totalH);

            // vertical truncation: whole lines only
            float spacing = s.lineSpacing;
            int shown = ls.Count;
            if (truncate)
            {
                float y = 0;
                for (shown = 0; shown < ls.Count; shown++)
                {
                    float lh = face.LineHeight(ls[shown].size) * spacing;
                    if (y + lh > ext.y + 0.001f) break;
                    y += lh;
                }
            }
            float usedH = 0; for (int i = 0; i < shown; i++) usedH += face.LineHeight(ls[i].size) * spacing;

            var pivot = s.pivot;
            float left = -pivot.x * ext.x, bottom = -pivot.y * ext.y, right = left + ext.x, top = bottom + ext.y;
            int anchor = (int)s.textAnchor, ha = anchor % 3, va = anchor / 3;
            float blockH = truncate ? usedH : totalH;
            float y0 = va == 0 ? top : va == 1 ? top - (ext.y - blockH) / 2 : bottom + blockH;
            y0 = Mathf.Floor(y0 + 0.5f);

            chars.Clear(); lines.Clear(); verts.Clear();
            var charInfos = new UICharInfo[str.Length + 1];
            float lineTop = y0; float maxW = 0;
            visible = 0;
            for (int li = 0; li < ls.Count; li++)
            {
                var ln = ls[li];
                float lh = face.LineHeight(ln.size), asc = face.Ascent(ln.size);
                int x = ha == 0 ? 0 : ha == 1 ? (int)Mathf.Floor((ext.x - ln.width) / 2) : (int)(ext.x - ln.width);
                if (s.alignByGeometry && ha != 0) x += GeometryOffset(face, items, ln, ha);
                float pen = left + x;
                float baseline = Mathf.Round(lineTop - asc);
                lines.Add(new UILineInfo { startCharIdx = ln.start < items.Count ? items[ln.start].index : str.Length, height = Mathf.RoundToInt(lh), topY = lineTop, leading = lh * (spacing - 1) });
                maxW = Math.Max(maxW, ln.width);
                for (int k = ln.start; k < ln.next && k < items.Count; k++)
                {
                    var it = items[k];
                    charInfos[it.index] = new UICharInfo { cursorPos = new Vector2(pen, lineTop), charWidth = k < ln.end ? it.adv : 0 };
                    if (k >= ln.end) continue;
                    if (li < shown && !it.tag && !char.IsWhiteSpace(it.c) && face.TryGet(it.c, it.size, it.style, out var ci) && ci.glyphWidth > 0)
                    {
                        float x0 = pen + ci.bearing, x1 = x0 + ci.glyphWidth, yt = baseline + ci.maxY, yb = baseline + ci.minY;
                        Quad(verts, x0, x1, yb, yt, ci, it.color);
                    }
                    pen += it.adv;
                }
                if (li < shown) visible = ln.next < items.Count ? items[ln.next].index : str.Length;
                lineTop -= lh * spacing;
            }
            if (shown == ls.Count) visible = str.Length;
            var lastLine = ls.Count > 0 ? ls[^1] : default;
            float endX = left + (ls.Count > 0 ? (ha == 0 ? 0 : ha == 1 ? Mathf.Floor((ext.x - lastLine.width) / 2) : ext.x - lastLine.width) + lastLine.width : 0);
            charInfos[str.Length] = new UICharInfo { cursorPos = new Vector2(endX, ls.Count > 0 ? lines[^1].topY : y0), charWidth = 0 };
            chars.AddRange(charInfos);
            float h = truncate ? usedH : totalH;
            extents = new Rect(left, top - h, s.updateBounds ? maxW : ext.x, h);
            // results in the caller's units (Text divides vertex positions by pixelsPerUnit itself; extents are in pixels too)
        }

        static int Px(int size, float scale) => Math.Max(1, (int)Math.Round(size * scale, MidpointRounding.AwayFromZero));

        static int GeometryOffset(LegacyFontFace face, List<Item> items, Line ln, int ha)
        {
            int first = -1, last = -1;
            for (int k = ln.start; k < ln.end; k++) if (!items[k].tag && !char.IsWhiteSpace(items[k].c)) { if (first < 0) first = k; last = k; }
            if (first < 0) return 0;
            face.TryGet(items[first].c, items[first].size, items[first].style, out var a);
            face.TryGet(items[last].c, items[last].size, items[last].style, out var z);
            int lead = a.bearing, trail = items[last].adv - (z.bearing + z.glyphWidth);
            return ha == 1 ? (trail - lead) / 2 : trail;
        }

        static void Quad(List<UIVertex> verts, float x0, float x1, float yb, float yt, CharacterInfo ci, Color32 col)
        {
            UIVertex V(float x, float y, Vector2 uv) => new UIVertex { position = new Vector3(x, y, 0), normal = new Vector3(0, 0, -1), tangent = new Vector4(1, 0, 0, -1), color = col, uv0 = uv };
            verts.Add(V(x0, yt, ci.uvTopLeft));
            verts.Add(V(x1, yt, ci.uvTopRight));
            verts.Add(V(x1, yb, ci.uvBottomRight));
            verts.Add(V(x0, yb, ci.uvBottomLeft));
        }

        static void Layout(string str, TextGenerationSettings s, LegacyFontFace face, Font font, int px, Vector2 ext, bool wrap, out List<Item> itemsOut, out List<Line> linesOut, out float totalH)
        {
            var items = Parse(str, s, px);
            // glyphs first (the atlas may grow; uvs are read when the quads are made)
            bool rebuilt = false;
            foreach (var it in items) if (!it.tag) rebuilt |= face.Request(it.c, it.size, it.style);
            if (rebuilt) Font.InvokeTextureRebuilt_Internal(font);
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.tag || it.c == '\n') it.adv = 0;
                else if (it.c == '\t') { face.TryGet(' ', it.size, it.style, out var sp); it.adv = sp.advance * 4; }
                else { face.TryGet(it.c, it.size, it.style, out var ci); it.adv = ci.advance; }
                items[i] = it;
            }
            var lines = new List<Line>();
            int start = 0, pen = 0, lastSpace = -1, maxSize = 0;
            void End(int end, int next)
            {
                int w = 0, sz = 0;
                for (int k = start; k < end; k++) { w += items[k].adv; if (!items[k].tag) sz = Math.Max(sz, items[k].size); }
                lines.Add(new Line { start = start, end = end, next = next, width = w, size = sz > 0 ? sz : px });
                start = next; pen = 0; lastSpace = -1; maxSize = 0;
            }
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.tag) continue;
                if (it.c == '\n') { End(i, i + 1); continue; }
                if (it.c == ' ' || it.c == '\t') { lastSpace = i; pen += it.adv; continue; }
                if (wrap && pen + it.adv > ext.x + 0.001f && HasGlyphBefore(items, start, i))
                {
                    if (lastSpace >= start) { End(lastSpace, lastSpace + 1); i = start - 1; continue; }
                    End(i, i); i = start - 1; continue;
                }
                pen += it.adv;
            }
            End(items.Count, items.Count);
            itemsOut = items; linesOut = lines;
            totalH = 0;
            foreach (var l in lines) totalH += face.LineHeight(l.size) * s.lineSpacing;
        }

        static bool HasGlyphBefore(List<Item> items, int start, int i)
        {
            for (int k = start; k < i; k++) if (!items[k].tag) return true;
            return false;
        }

        // ------------------------------------------------------------------ rich text: <b> <i> <size=n> <color=…> <material=n> <quad …>
        static List<Item> Parse(string str, TextGenerationSettings s, int px)
        {
            var items = new List<Item>(str.Length);
            var sizes = new Stack<int>(); var colors = new Stack<Color32>();
            int bold = 0, italic = 0;
            Color32 baseColor = s.color;
            float scale = s.scaleFactor > 0 ? s.scaleFactor : 1;
            for (int i = 0; i < str.Length; i++)
            {
                if (s.richText && str[i] == '<' && TryTag(str, i, out int end, out string name, out string value, out bool closing))
                {
                    bool ok = true;
                    switch (name)
                    {
                        case "b": if (closing) { if (bold > 0) bold--; else ok = false; } else bold++; break;
                        case "i": if (closing) { if (italic > 0) italic--; else ok = false; } else italic++; break;
                        case "size":
                            if (closing) { if (sizes.Count > 0) sizes.Pop(); else ok = false; }
                            else if (int.TryParse(value, out int sz) && sz > 0) sizes.Push(Math.Max(1, (int)Math.Round(sz * scale))); else ok = false;
                            break;
                        case "color":
                            if (closing) { if (colors.Count > 0) colors.Pop(); else ok = false; }
                            else if (ParseColor(value, out var c)) colors.Push(c); else ok = false;
                            break;
                        case "material": case "quad": break;
                        default: ok = false; break;
                    }
                    if (ok)
                    {
                        for (int k = i; k <= end; k++) items.Add(new Item { index = k, c = str[k], tag = true });
                        i = end;
                        continue;
                    }
                }
                var style = s.fontStyle;
                if (bold > 0) style = style == FontStyle.Italic || style == FontStyle.BoldAndItalic ? FontStyle.BoldAndItalic : FontStyle.Bold;
                if (italic > 0) style = style == FontStyle.Bold || style == FontStyle.BoldAndItalic ? FontStyle.BoldAndItalic : FontStyle.Italic;
                items.Add(new Item { index = i, c = str[i], size = sizes.Count > 0 ? sizes.Peek() : px, style = style, color = colors.Count > 0 ? colors.Peek() : baseColor });
            }
            return items;
        }

        static bool TryTag(string str, int i, out int end, out string name, out string value, out bool closing)
        {
            end = str.IndexOf('>', i + 1); name = value = null; closing = false;
            if (end < 0) return false;
            var body = str.Substring(i + 1, end - i - 1);
            if (body.StartsWith("/")) { closing = true; body = body.Substring(1); }
            int eq = body.IndexOf('=');
            name = (eq >= 0 ? body.Substring(0, eq) : body.Split(' ')[0]).Trim().ToLowerInvariant();
            value = eq >= 0 ? body.Substring(eq + 1).Trim().Trim('"', '\'') : null;
            if (name == "quad") return !closing;
            if (!closing && (name == "size" || name == "color" || name == "material") && value == null) return false;
            return name.Length > 0;
        }

        static readonly Dictionary<string, string> Named = new()
        {
            ["aqua"] = "00ffff", ["black"] = "000000", ["blue"] = "0000ff", ["brown"] = "a52a2a", ["cyan"] = "00ffff", ["darkblue"] = "0000a0",
            ["fuchsia"] = "ff00ff", ["green"] = "008000", ["grey"] = "808080", ["gray"] = "808080", ["lightblue"] = "add8e6", ["lime"] = "00ff00",
            ["magenta"] = "ff00ff", ["maroon"] = "800000", ["navy"] = "000080", ["olive"] = "808000", ["orange"] = "ffa500", ["purple"] = "800080",
            ["red"] = "ff0000", ["silver"] = "c0c0c0", ["teal"] = "008080", ["white"] = "ffffff", ["yellow"] = "ffff00",
        };

        static bool ParseColor(string v, out Color32 c)
        {
            c = default;
            if (string.IsNullOrEmpty(v)) return false;
            string hex = v.StartsWith("#") ? v.Substring(1) : Named.TryGetValue(v.ToLowerInvariant(), out var n) ? n : null;
            if (hex == null) return false;
            if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(ch => new string(ch, 2)));
            if (hex.Length == 6) hex += "ff";
            if (hex.Length != 8 || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint u)) return false;
            c = new Color32((byte)(u >> 24), (byte)(u >> 16), (byte)(u >> 8), (byte)u);
            return true;
        }
    }
}
