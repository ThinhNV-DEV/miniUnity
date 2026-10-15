// Minimal TrueType reader for the FontEngine: cmap (formats 4 and 12), hmtx, loca/glyf (simple + composite glyphs,
// quadratic contours). Coordinates are scaled like FreeType without hinting (26.6 fixed point).
using System;
using System.Collections.Generic;
using System.IO;

namespace MiniUnity
{
    public sealed class TrueTypeFont
    {
        readonly byte[] d;
        readonly Dictionary<string, (int off, int len)> tables = new();
        public int UnitsPerEm, NumGlyphs, Ascender, Descender, LineGap, NumHMetrics, IndexToLoc;
        int[] loca;
        readonly Dictionary<uint, uint> cmap = new();

        public TrueTypeFont(byte[] data)
        {
            d = data;
            int numTables = U16(4);
            for (int i = 0; i < numTables; i++)
            {
                int r = 12 + i * 16;
                string tag = System.Text.Encoding.ASCII.GetString(d, r, 4);
                tables[tag] = ((int)U32(r + 8), (int)U32(r + 12));
            }
            var head = tables["head"].off;
            UnitsPerEm = U16(head + 18);
            IndexToLoc = S16(head + 50);
            NumGlyphs = U16(tables["maxp"].off + 4);
            var hhea = tables["hhea"].off;
            Ascender = S16(hhea + 4); Descender = S16(hhea + 6); LineGap = S16(hhea + 8); NumHMetrics = U16(hhea + 34);
            loca = new int[NumGlyphs + 1];
            int lo = tables["loca"].off;
            for (int i = 0; i <= NumGlyphs; i++) loca[i] = IndexToLoc == 0 ? U16(lo + i * 2) * 2 : (int)U32(lo + i * 4);
            ReadCmap();
        }

        public static TrueTypeFont Load(string path) => new TrueTypeFont(File.ReadAllBytes(path));

        int U16(int o) => (d[o] << 8) | d[o + 1];
        short S16(int o) => (short)((d[o] << 8) | d[o + 1]);
        uint U32(int o) => (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);

        void ReadCmap()
        {
            int c = tables["cmap"].off;
            int n = U16(c + 2);
            int best = -1, bestScore = -1;
            for (int i = 0; i < n; i++)
            {
                int pid = U16(c + 4 + i * 8), eid = U16(c + 6 + i * 8); int off = (int)U32(c + 8 + i * 8);
                int fmt = U16(c + off);
                int score = (pid == 3 && eid == 10 && fmt == 12) ? 4 : (pid == 0 && fmt == 12) ? 3 : (pid == 3 && eid == 1 && fmt == 4) ? 2 : (pid == 0 && fmt == 4) ? 1 : -1;
                if (score > bestScore) { bestScore = score; best = c + off; }
            }
            if (best < 0) return;
            int f = U16(best);
            if (f == 4)
            {
                int segX2 = U16(best + 6), seg = segX2 / 2;
                int ends = best + 14, starts = ends + segX2 + 2, deltas = starts + segX2, ranges = deltas + segX2;
                for (int s = 0; s < seg; s++)
                {
                    int end = U16(ends + s * 2), start = U16(starts + s * 2), delta = S16(deltas + s * 2), ro = U16(ranges + s * 2);
                    for (int ch = start; ch <= end && ch != 0xFFFF; ch++)
                    {
                        int g;
                        if (ro == 0) g = (ch + delta) & 0xFFFF;
                        else
                        {
                            int gi = ranges + s * 2 + ro + (ch - start) * 2;
                            g = U16(gi); if (g != 0) g = (g + delta) & 0xFFFF;
                        }
                        if (g != 0) cmap[(uint)ch] = (uint)g;
                    }
                }
            }
            else if (f == 12)
            {
                uint groups = U32(best + 12);
                for (uint i = 0; i < groups; i++)
                {
                    int o = best + 16 + (int)i * 12;
                    uint s = U32(o), e = U32(o + 4), g = U32(o + 8);
                    for (uint ch = s; ch <= e; ch++) cmap[ch] = g + (ch - s);
                }
            }
        }

        public uint GlyphIndex(uint unicode) => cmap.TryGetValue(unicode, out var g) ? g : 0;

        public int AdvanceWidth(uint glyph)
        {
            int h = tables["hmtx"].off;
            int i = (int)Math.Min(glyph, (uint)NumHMetrics - 1);
            return U16(h + i * 4);
        }

        /// <summary>Contours in font units; each point (x, y, onCurve).</summary>
        public List<List<(float x, float y, bool on)>> Contours(uint glyph, int depth = 0)
        {
            var result = new List<List<(float, float, bool)>>();
            if (glyph >= NumGlyphs || depth > 8) return result;
            int start = loca[glyph], end = loca[glyph + 1];
            if (end <= start) return result;
            int g = tables["glyf"].off + start;
            int nc = S16(g);
            if (nc >= 0)
            {
                var endPts = new int[nc];
                for (int i = 0; i < nc; i++) endPts[i] = U16(g + 10 + i * 2);
                int nPts = nc > 0 ? endPts[nc - 1] + 1 : 0;
                int insLen = U16(g + 10 + nc * 2);
                int p = g + 12 + nc * 2 + insLen;
                var flags = new byte[nPts];
                for (int i = 0; i < nPts;)
                {
                    byte fl = d[p++]; flags[i++] = fl;
                    if ((fl & 8) != 0) { int rep = d[p++]; for (int r = 0; r < rep && i < nPts; r++) flags[i++] = fl; }
                }
                var xs = new int[nPts]; var ys = new int[nPts];
                int v = 0;
                for (int i = 0; i < nPts; i++)
                {
                    byte fl = flags[i];
                    if ((fl & 2) != 0) { int dx = d[p++]; v += (fl & 16) != 0 ? dx : -dx; }
                    else if ((fl & 16) == 0) { v += S16(p); p += 2; }
                    xs[i] = v;
                }
                v = 0;
                for (int i = 0; i < nPts; i++)
                {
                    byte fl = flags[i];
                    if ((fl & 4) != 0) { int dy = d[p++]; v += (fl & 32) != 0 ? dy : -dy; }
                    else if ((fl & 32) == 0) { v += S16(p); p += 2; }
                    ys[i] = v;
                }
                int s0 = 0;
                for (int c = 0; c < nc; c++)
                {
                    var cont = new List<(float, float, bool)>();
                    for (int i = s0; i <= endPts[c]; i++) cont.Add((xs[i], ys[i], (flags[i] & 1) != 0));
                    s0 = endPts[c] + 1;
                    if (cont.Count > 0) result.Add(cont);
                }
                return result;
            }
            // composite
            int q = g + 10;
            while (true)
            {
                int fl = U16(q), gi = U16(q + 2); q += 4;
                float dx, dy;
                if ((fl & 1) != 0) { dx = S16(q); dy = S16(q + 2); q += 4; }
                else { dx = (sbyte)d[q]; dy = (sbyte)d[q + 1]; q += 2; }
                float a = 1, b = 0, c2 = 0, dd = 1;
                if ((fl & 8) != 0) { a = dd = F2Dot14(q); q += 2; }
                else if ((fl & 0x40) != 0) { a = F2Dot14(q); dd = F2Dot14(q + 2); q += 4; }
                else if ((fl & 0x80) != 0) { a = F2Dot14(q); b = F2Dot14(q + 2); c2 = F2Dot14(q + 4); dd = F2Dot14(q + 6); q += 8; }
                // (point-matching offsets – ARGS_ARE_XY_VALUES not set – are not used by these fonts)
                foreach (var cont in Contours((uint)gi, depth + 1))
                {
                    var t = new List<(float, float, bool)>(cont.Count);
                    foreach (var (x, y, on) in cont) t.Add((a * x + c2 * y + dx, b * x + dd * y + dy, on));
                    result.Add(t);
                }
                if ((fl & 0x20) == 0) break;
            }
            return result;
        }
        float F2Dot14(int o) => S16(o) / 16384f;
    }
}
