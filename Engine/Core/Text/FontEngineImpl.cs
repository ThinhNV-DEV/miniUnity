// FontEngine without FreeType: TrueType outlines (TrueType.cs) scaled like FreeType's unhinted 26.6 loader, glyph
// metrics from the control box, bitmaps on the pixel grid (floor(xMin) .. ceil(xMax)), SDF from exact distances to the
// flattened outline, packed MaxRects-style into the atlas with Unity's layout (glyph rect + 2·padding + 1). Lets TMP's
// dynamic font assets add characters (e.g. Vietnamese letters met in new subtitles) the way Unity does.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniUnity;
using UnityEngine.TextCore.LowLevel;

namespace UnityEngine.TextCore.LowLevel
{
    public partial class FontEngine
    {
        static TrueTypeFont s_Face;
        static string s_FacePath;
        static int s_PointSize = 90;
        static readonly Dictionary<string, TrueTypeFont> s_Faces = new();
        /// <summary>SDF value = 0.5 + signedDistancePx · SdfScale / padding (calibrated against Unity-made atlases).</summary>
        public static float SdfScale = 0.465f;

        public static FontEngineError InitializeFontEngine() => FontEngineError.Success;
        public static FontEngineError DestroyFontEngine() => FontEngineError.Success;
        public static FontEngineError UnloadFontFace() { s_Face = null; s_FacePath = null; return FontEngineError.Success; }
        public static FontEngineError UnloadAllFontFaces() => UnloadFontFace();
        static FontEngineError Use(string path, int pointSize)
        {
            if (path == null || !File.Exists(path)) return FontEngineError.Invalid_File_Path;
            try
            {
                if (!s_Faces.TryGetValue(path, out var f)) s_Faces[path] = f = TrueTypeFont.Load(path);
                s_Face = f; s_FacePath = path; s_PointSize = pointSize;
                return FontEngineError.Success;
            }
            catch { return FontEngineError.Invalid_File; }
        }
        static string PathOf(Font font) => font?.m_Asset?.Path is string p ? AssetIndex.Abs(p) : null;
        public static FontEngineError LoadFontFace(string filePath) => Use(filePath, s_PointSize);
        public static FontEngineError LoadFontFace(string filePath, int pointSize) => Use(filePath, pointSize);
        public static FontEngineError LoadFontFace(string filePath, int pointSize, int faceIndex) => Use(filePath, pointSize);
        public static FontEngineError LoadFontFace(byte[] sourceFontFile) => FontEngineError.Invalid_File;
        public static FontEngineError LoadFontFace(byte[] sourceFontFile, int pointSize) => FontEngineError.Invalid_File;
        public static FontEngineError LoadFontFace(byte[] sourceFontFile, int pointSize, int faceIndex) => FontEngineError.Invalid_File;
        public static FontEngineError LoadFontFace(Font font) => Use(PathOf(font), s_PointSize);
        public static FontEngineError LoadFontFace(Font font, int pointSize) => Use(PathOf(font), pointSize);
        public static FontEngineError LoadFontFace(Font font, int pointSize, int faceIndex) => Use(PathOf(font), pointSize);
        public static FontEngineError LoadFontFace(string familyName, string styleName) => FontEngineError.Invalid_File;
        public static FontEngineError LoadFontFace(string familyName, string styleName, int pointSize) => FontEngineError.Invalid_File;
        public static FontEngineError SetFaceSize(int pointSize) { s_PointSize = pointSize; return FontEngineError.Success; }

        public static FaceInfo GetFaceInfo()
        {
            var fi = new FaceInfo();
            if (s_Face == null) return fi;
            float k = s_PointSize / (float)s_Face.UnitsPerEm;
            fi.pointSize = s_PointSize; fi.scale = 1;
            fi.ascentLine = s_Face.Ascender * k; fi.descentLine = s_Face.Descender * k;
            fi.lineHeight = (s_Face.Ascender - s_Face.Descender + s_Face.LineGap) * k;
            fi.familyName = Path.GetFileNameWithoutExtension(s_FacePath);
            return fi;
        }

        internal static uint GetGlyphIndex(uint unicode) => s_Face?.GlyphIndex(unicode) ?? 0;
        public static bool TryGetGlyphIndex(uint unicode, out uint glyphIndex) { glyphIndex = GetGlyphIndex(unicode); return glyphIndex != 0; }

        // ------------------------------------------------------------------ outline + metrics
        sealed class Outline
        {
            public List<(float x0, float y0, float x1, float y1)> segs = new();   // pixels (y up), flattened
            public long xMin, yMin, xMax, yMax;                                 // control box, 26.6
            public long adv;                                                    // 26.6
            public bool empty;
        }

        static long Fix(float v, float k) => (long)Math.Round(v * k * 64.0, MidpointRounding.AwayFromZero);

        static Outline Load(uint glyph)
        {
            var o = new Outline();
            float k = s_PointSize / (float)s_Face.UnitsPerEm;
            o.adv = Fix(s_Face.AdvanceWidth(glyph), k);
            var contours = s_Face.Contours(glyph);
            o.empty = contours.Count == 0;
            o.xMin = o.yMin = long.MaxValue; o.xMax = o.yMax = long.MinValue;
            foreach (var contour in contours)
            {
                var pts = contour.Select(pt => (x: Fix(pt.x, k), y: Fix(pt.y, k), pt.on)).ToList();
                foreach (var pt in pts) { o.xMin = Math.Min(o.xMin, pt.x); o.xMax = Math.Max(o.xMax, pt.x); o.yMin = Math.Min(o.yMin, pt.y); o.yMax = Math.Max(o.yMax, pt.y); }
                // quadratic B-spline: consecutive off-curve points imply an on-curve midpoint
                var l = pts.Select(pt => (x: pt.x / 64f, y: pt.y / 64f, pt.on)).ToList();
                if (l.Count == 0) continue;
                int fo = l.FindIndex(pt => pt.on);
                if (fo < 0) { l.Insert(0, ((l[0].x + l[1 % l.Count].x) / 2, (l[0].y + l[1 % l.Count].y) / 2, true)); fo = 0; }
                l = l.Skip(fo).Concat(l.Take(fo)).ToList();
                int n = l.Count;
                (float x, float y) cur = (l[0].x, l[0].y); (float x, float y)? ctrl = null;
                void Quad((float x, float y) p0, (float x, float y) cp, (float x, float y) p1)
                {
                    const int steps = 12;
                    var prev = p0;
                    for (int t = 1; t <= steps; t++)
                    {
                        float u = t / (float)steps, a = (1 - u) * (1 - u), b = 2 * u * (1 - u), cc = u * u;
                        (float x, float y) q = (a * p0.x + b * cp.x + cc * p1.x, a * p0.y + b * cp.y + cc * p1.y);
                        o.segs.Add((prev.x, prev.y, q.x, q.y)); prev = q;
                    }
                }
                for (int i = 1; i <= n; i++)
                {
                    var p = l[i % n];
                    if (p.on)
                    {
                        if (ctrl == null) o.segs.Add((cur.x, cur.y, p.x, p.y)); else Quad(cur, ctrl.Value, (p.x, p.y));
                        ctrl = null; cur = (p.x, p.y);
                    }
                    else if (ctrl == null) ctrl = (p.x, p.y);
                    else
                    {
                        var c = ctrl.Value; (float x, float y) mid = ((c.x + p.x) / 2, (c.y + p.y) / 2);
                        Quad(cur, c, mid); cur = mid; ctrl = (p.x, p.y);
                    }
                }
            }
            if (o.empty) o.xMin = o.yMin = o.xMax = o.yMax = 0;
            return o;
        }

        static GlyphMetrics Metrics(Outline o) => new GlyphMetrics((o.xMax - o.xMin) / 64f, (o.yMax - o.yMin) / 64f, o.xMin / 64f, o.yMax / 64f, o.adv / 64f);
        static (int left, int bottom, int w, int h) Bitmap(Outline o)
        {
            if (o.empty) return (0, 0, 0, 0);
            int l = (int)Math.Floor(o.xMin / 64.0), r = (int)Math.Ceiling(o.xMax / 64.0), b = (int)Math.Floor(o.yMin / 64.0), t = (int)Math.Ceiling(o.yMax / 64.0);
            return (l, b, r - l, t - b);
        }

        public static bool TryGetGlyphWithUnicodeValue(uint unicode, GlyphLoadFlags flags, out Glyph glyph)
        {
            glyph = null;
            uint gi = GetGlyphIndex(unicode);
            if (s_Face == null || gi == 0) return false;
            return TryGetGlyphWithIndexValue(gi, flags, out glyph);
        }
        public static bool TryGetGlyphWithIndexValue(uint glyphIndex, GlyphLoadFlags flags, out Glyph glyph)
        {
            glyph = null;
            if (s_Face == null || glyphIndex >= s_Face.NumGlyphs) return false;
            var o = Load(glyphIndex);
            var bm = Bitmap(o);
            glyph = new Glyph(glyphIndex, Metrics(o), new GlyphRect(0, 0, bm.w, bm.h), 1, 0);
            return true;
        }

        // ------------------------------------------------------------------ atlas packing (MaxRects)
        internal static bool TryPackGlyphInAtlas(Glyph glyph, int padding, GlyphPackingMode packingMode, GlyphRenderMode renderMode, int width, int height, List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects)
        {
            var gr = glyph.glyphRect;
            if (gr.width == 0 || gr.height == 0) { glyph.glyphRect = new GlyphRect(0, 0, 0, 0); return true; }
            int w = gr.width + padding * 2 + 1, h = gr.height + padding * 2 + 1;
            if (freeGlyphRects.Count == 0 && usedGlyphRects.Count == 0) freeGlyphRects.Add(new GlyphRect(0, 0, width, height));
            int best = -1; long bestShort = long.MaxValue, bestLong = long.MaxValue;
            for (int i = 0; i < freeGlyphRects.Count; i++)
            {
                var f = freeGlyphRects[i];
                if (f.width < w || f.height < h) continue;
                long ls = Math.Min(f.width - w, f.height - h), ll = Math.Max(f.width - w, f.height - h);
                if (ls < bestShort || (ls == bestShort && ll < bestLong)) { best = i; bestShort = ls; bestLong = ll; }
            }
            if (best < 0) return false;
            var placed = new GlyphRect(freeGlyphRects[best].x, freeGlyphRects[best].y, w, h);
            // split every free rect overlapping the placed one
            var next = new List<GlyphRect>();
            foreach (var f in freeGlyphRects)
            {
                if (placed.x >= f.x + f.width || placed.x + placed.width <= f.x || placed.y >= f.y + f.height || placed.y + placed.height <= f.y) { next.Add(f); continue; }
                if (placed.x > f.x) next.Add(new GlyphRect(f.x, f.y, placed.x - f.x, f.height));
                if (placed.x + placed.width < f.x + f.width) next.Add(new GlyphRect(placed.x + placed.width, f.y, f.x + f.width - placed.x - placed.width, f.height));
                if (placed.y > f.y) next.Add(new GlyphRect(f.x, f.y, f.width, placed.y - f.y));
                if (placed.y + placed.height < f.y + f.height) next.Add(new GlyphRect(f.x, placed.y + placed.height, f.width, f.y + f.height - placed.y - placed.height));
            }
            // prune rects contained in others
            var pruned = new List<GlyphRect>();
            for (int i = 0; i < next.Count; i++)
            {
                var a = next[i]; bool inside = false;
                for (int j = 0; j < next.Count && !inside; j++)
                {
                    if (i == j) continue;
                    var b = next[j];
                    if (a.x >= b.x && a.y >= b.y && a.x + a.width <= b.x + b.width && a.y + a.height <= b.y + b.height && (j < i || !(a.Equals(b)))) inside = true;
                }
                if (!inside) pruned.Add(a);
            }
            freeGlyphRects.Clear(); freeGlyphRects.AddRange(pruned);
            usedGlyphRects.Add(placed);
            glyph.glyphRect = new GlyphRect(placed.x + padding + 1, placed.y + padding + 1, gr.width, gr.height);
            return true;
        }
        internal static bool TryPackGlyphsInAtlas(List<Glyph> glyphsToAdd, List<Glyph> glyphsAdded, int padding, GlyphPackingMode packingMode, GlyphRenderMode renderMode, int width, int height, List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects)
        {
            bool all = true;
            foreach (var g in glyphsToAdd.ToList())
            {
                if (TryPackGlyphInAtlas(g, padding, packingMode, renderMode, width, height, freeGlyphRects, usedGlyphRects)) { glyphsAdded.Add(g); glyphsToAdd.Remove(g); }
                else all = false;
            }
            return all;
        }

        // ------------------------------------------------------------------ SDF rendering
        static void RenderSdf(Glyph glyph, int padding, Texture2D tex)
        {
            if (glyph.glyphRect.width == 0 || glyph.glyphRect.height == 0) return;
            var o = Load(glyph.index);
            var bm = Bitmap(o);
            tex.EnsurePixels();
            int W = tex.width, H = tex.height;
            var gr = glyph.glyphRect;
            float spread = Math.Max(1, padding);
            for (int j = -padding; j < gr.height + padding; j++)
            {
                int ty = gr.y + j;
                if (ty < 0 || ty >= H) continue;
                for (int i = -padding; i < gr.width + padding; i++)
                {
                    int tx = gr.x + i;
                    if (tx < 0 || tx >= W) continue;
                    float px = bm.left + i + 0.5f, py = bm.bottom + j + 0.5f;
                    float d = SignedDistance(o, px, py);
                    float a = Math.Clamp(0.5f + d * SdfScale / spread, 0, 1);
                    a = MathF.Round(a * 255f) / 255f;
                    tex.m_Pixels[ty * W + tx] = new Color(1, 1, 1, a);
                }
            }
            tex.m_Version++;
        }

        static float SignedDistance(Outline o, float px, float py)
        {
            float best = float.MaxValue; int winding = 0;
            foreach (var (x0, y0, x1, y1) in o.segs)
            {
                float dx = x1 - x0, dy = y1 - y0, l2 = dx * dx + dy * dy;
                float t = l2 > 0 ? Math.Clamp(((px - x0) * dx + (py - y0) * dy) / l2, 0, 1) : 0;
                float ex = x0 + t * dx - px, ey = y0 + t * dy - py, d2 = ex * ex + ey * ey;
                if (d2 < best) best = d2;
                // nonzero winding (scanline to the right)
                if (y0 <= py) { if (y1 > py && (dx * (py - y0) - (px - x0) * dy) > 0) winding++; }
                else if (y1 <= py && (dx * (py - y0) - (px - x0) * dy) < 0) winding--;
            }
            float dist = MathF.Sqrt(best);
            return winding != 0 ? dist : -dist;
        }

        internal static FontEngineError RenderGlyphsToTexture(List<Glyph> glyphs, int padding, GlyphRenderMode renderMode, Texture2D texture)
        {
            foreach (var g in glyphs) RenderSdf(g, padding, texture);
            return FontEngineError.Success;
        }
        internal static FontEngineError RenderGlyphsToTexture(List<Glyph> glyphs, int padding, GlyphRenderMode renderMode, byte[] texBuffer, int texWidth, int texHeight) => FontEngineError.Success;

        internal static bool TryAddGlyphToTexture(uint glyphIndex, int padding, GlyphPackingMode packingMode, List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects, GlyphRenderMode renderMode, Texture2D texture, out Glyph glyph)
        {
            glyph = null;
            if (!TryGetGlyphWithIndexValue(glyphIndex, GlyphLoadFlags.LOAD_DEFAULT, out var g)) return false;
            if (!TryPackGlyphInAtlas(g, padding, packingMode, renderMode, texture.width, texture.height, freeGlyphRects, usedGlyphRects)) return false;
            RenderSdf(g, padding, texture);
            glyph = g;
            return true;
        }
        internal static bool TryAddGlyphsToTexture(List<Glyph> glyphsToAdd, List<Glyph> glyphsAdded, int padding, GlyphPackingMode packingMode, List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects, GlyphRenderMode renderMode, Texture2D texture)
        {
            var added = new List<Glyph>();
            bool all = TryPackGlyphsInAtlas(glyphsToAdd, added, padding, packingMode, renderMode, texture.width, texture.height, freeGlyphRects, usedGlyphRects);
            foreach (var g in added) RenderSdf(g, padding, texture);
            glyphsAdded.AddRange(added);
            return all;
        }
        internal static bool TryAddGlyphsToTexture(List<uint> glyphIndexes, int padding, GlyphPackingMode packingMode, List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects, GlyphRenderMode renderMode, Texture2D texture, out Glyph[] glyphs)
        {
            var list = new List<Glyph>();
            bool all = true;
            foreach (var gi in glyphIndexes)
            {
                if (TryAddGlyphToTexture(gi, padding, packingMode, freeGlyphRects, usedGlyphRects, renderMode, texture, out var g)) list.Add(g);
                else all = false;
            }
            glyphs = list.ToArray();
            return all;
        }

        internal static void ResetAtlasTexture(Texture2D texture)
        {
            texture.EnsurePixels();
            for (int i = 0; i < texture.m_Pixels.Length; i++) texture.m_Pixels[i] = new Color(1, 1, 1, 0);
            texture.m_Version++;
        }

        // kerning / GPOS: not read (the committed font assets carry no adjustment records either)
        internal static GlyphPairAdjustmentRecord[] GetGlyphPairAdjustmentTable(uint[] glyphIndexes) => Array.Empty<GlyphPairAdjustmentRecord>();
        internal static GlyphPairAdjustmentRecord[] GetGlyphPairAdjustmentRecords(List<uint> glyphIndexes, out int recordCount) { recordCount = 0; return Array.Empty<GlyphPairAdjustmentRecord>(); }
        internal static GlyphPairAdjustmentRecord[] GetGlyphPairAdjustmentRecords(uint glyphIndex, out int recordCount) { recordCount = 0; return Array.Empty<GlyphPairAdjustmentRecord>(); }
        internal static GlyphPairAdjustmentRecord[] GetGlyphPairAdjustmentRecords(List<uint> newGlyphIndexes, List<uint> allGlyphIndexes) => Array.Empty<GlyphPairAdjustmentRecord>();
        internal static GlyphPairAdjustmentRecord GetGlyphPairAdjustmentRecord(uint firstGlyphIndex, uint secondGlyphIndex) => default;
    }
}
