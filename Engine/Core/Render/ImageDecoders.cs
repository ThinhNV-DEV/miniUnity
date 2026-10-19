// Managed decoders for the texture formats Unity imports besides PNG: JPEG (baseline and progressive, Huffman, 1 / 3 / 4
// components, any sampling factors, restart intervals; the scan structure follows pdf.js's jpg.js) and Truevision TGA
// (true-colour, grey and colour-mapped, raw and RLE), and Photoshop PSD's merged image. Output is 8-bit RGBA, rows bottom-up like Texture2D.
using System;
using System.Collections.Generic;
using System.IO;

namespace MiniUnity
{
    public static class ImageDecoders
    {
        public static bool TryDecodeFile(string path, out int w, out int h, out byte[] rgba)
        {
            w = h = 0; rgba = null;
            byte[] d;
            try { d = File.ReadAllBytes(path); } catch { return false; }
            return TryDecode(d, Path.GetExtension(path).ToLowerInvariant(), out w, out h, out rgba);
        }

        public static bool TryDecode(byte[] d, string ext, out int w, out int h, out byte[] rgba)
        {
            w = h = 0; rgba = null;
            try
            {
                if (d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8) { rgba = new Jpeg(d).Decode(out w, out h); return rgba != null; }
                if (ext == ".tga") { rgba = Tga(d, out w, out h); return rgba != null; }
                if (d.Length > 26 && d[0] == '8' && d[1] == 'B' && d[2] == 'P' && d[3] == 'S') { rgba = Psd(d, out w, out h); return rgba != null; }
            }
            catch (Exception) { rgba = null; }
            return false;
        }

        /// <summary>Width / height from the file header (JPEG SOF, TGA header), without decoding.</summary>
        public static (int, int) Size(byte[] d, string ext)
        {
            if (d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8)
            {
                int i = 2;
                while (i + 9 < d.Length)
                {
                    if (d[i] != 0xFF) { i++; continue; }
                    int m = d[i + 1];
                    if (m == 0xD8 || (m >= 0xD0 && m <= 0xD7) || m == 0x01 || m == 0xFF) { i += m == 0xFF ? 1 : 2; continue; }
                    int len = (d[i + 2] << 8) | d[i + 3];
                    if (m >= 0xC0 && m <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC) return ((d[i + 7] << 8) | d[i + 8], (d[i + 5] << 8) | d[i + 6]);
                    i += 2 + len;
                }
                return (0, 0);
            }
            if (ext == ".tga" && d.Length >= 18) return (d[12] | (d[13] << 8), d[14] | (d[15] << 8));
            if (d.Length > 26 && d[0] == '8' && d[1] == 'B' && d[2] == 'P' && d[3] == 'S') return (BE32(d, 18), BE32(d, 14));
            return (0, 0);
        }

        static int BE32(byte[] d, int i) => (d[i] << 24) | (d[i + 1] << 16) | (d[i + 2] << 8) | d[i + 3];
        static int BE16(byte[] d, int i) => (d[i] << 8) | d[i + 1];

        // ------------------------------------------------------------------ PSD: the merged (composite) image, 8 / 16-bit grey or RGB
        static byte[] Psd(byte[] d, out int w, out int h)
        {
            int version = BE16(d, 4), channels = BE16(d, 12);
            h = BE32(d, 14); w = BE32(d, 18);
            int depth = BE16(d, 22), mode = BE16(d, 24);
            if (version != 1 || (depth != 8 && depth != 16) || (mode != 1 && mode != 3) || w <= 0 || h <= 0) return null;
            int p = 26;
            for (int s = 0; s < 3; s++) p += 4 + BE32(d, p);   // colour mode data, image resources, layer and mask info
            int comp = BE16(d, p); p += 2;
            int bpc = depth / 8, rowBytes = w * bpc;
            var planes = new byte[channels][];
            if (comp == 0)
                for (int c = 0; c < channels; c++) { planes[c] = new byte[rowBytes * h]; Array.Copy(d, p, planes[c], 0, rowBytes * h); p += rowBytes * h; }
            else if (comp == 1)
            {
                var counts = new int[channels * h];
                for (int i = 0; i < counts.Length; i++) { counts[i] = BE16(d, p); p += 2; }
                for (int c = 0; c < channels; c++)
                {
                    planes[c] = new byte[rowBytes * h];
                    for (int y = 0; y < h; y++)
                    {
                        int end = p + counts[c * h + y], o = y * rowBytes;
                        while (p < end && o < (y + 1) * rowBytes)
                        {
                            int n = (sbyte)d[p++];
                            if (n >= 0) { for (int k = 0; k <= n && o < (y + 1) * rowBytes; k++) planes[c][o++] = d[p++]; }
                            else if (n != -128) { byte v = d[p++]; for (int k = 0; k < 1 - n && o < (y + 1) * rowBytes; k++) planes[c][o++] = v; }
                        }
                        p = end;
                    }
                }
            }
            else return null;
            int colour = mode == 3 ? 3 : 1;
            var rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int s = y * rowBytes + x * bpc, i = ((h - 1 - y) * w + x) * 4;
                    byte G(int c) => planes[c][s];   // 16-bit: the high byte
                    if (colour == 3) { rgba[i] = G(0); rgba[i + 1] = G(1); rgba[i + 2] = G(2); }
                    else rgba[i] = rgba[i + 1] = rgba[i + 2] = G(0);
                    rgba[i + 3] = channels > colour ? G(colour) : (byte)255;
                }
            return rgba;
        }

        // ------------------------------------------------------------------ TGA
        static byte[] Tga(byte[] d, out int w, out int h)
        {
            w = h = 0;
            if (d.Length < 18) return null;
            int idLen = d[0], cmType = d[1], type = d[2];
            int cmFirst = d[3] | (d[4] << 8), cmLen = d[5] | (d[6] << 8), cmBits = d[7];
            w = d[12] | (d[13] << 8); h = d[14] | (d[15] << 8);
            int bpp = d[16], desc = d[17];
            bool rle = type >= 9, topDown = (desc & 0x20) != 0, rightLeft = (desc & 0x10) != 0;
            int baseType = type & 7;   // 1 colour-mapped, 2 true-colour, 3 grey
            if (w <= 0 || h <= 0 || baseType < 1 || baseType > 3) return null;
            int p = 18 + idLen;
            byte[][] palette = null;
            if (cmType == 1)
            {
                int cb = (cmBits + 7) / 8;
                palette = new byte[cmFirst + cmLen][];
                for (int i = 0; i < cmLen; i++) { palette[cmFirst + i] = Px(d, p, cmBits); p += cb; }
            }
            int bytes = (bpp + 7) / 8;
            var px = new byte[w * h][];
            byte[] Read(ref int q)
            {
                byte[] c;
                if (baseType == 1) { int idx = bytes == 2 ? d[q] | (d[q + 1] << 8) : d[q]; c = palette != null && idx < palette.Length ? palette[idx] ?? new byte[4] : new byte[4]; }
                else if (baseType == 3) { byte g = d[q]; c = new[] { g, g, g, bytes == 2 ? d[q + 1] : (byte)255 }; }
                else c = Px(d, q, bpp);
                q += bytes;
                return c;
            }
            int n = 0;
            while (n < w * h)
            {
                if (!rle) { px[n++] = Read(ref p); continue; }
                int hdr = d[p++], count = (hdr & 0x7F) + 1;
                if ((hdr & 0x80) != 0) { var c = Read(ref p); for (int k = 0; k < count && n < w * h; k++) px[n++] = c; }
                else for (int k = 0; k < count && n < w * h; k++) px[n++] = Read(ref p);
            }
            var o = new byte[w * h * 4];
            bool alpha = bpp == 32 || (bpp == 16 && (desc & 0x0F) > 0) || (baseType == 3 && bytes == 2) || (baseType == 1 && cmBits == 32);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = px[y * w + x];
                    int ox = rightLeft ? w - 1 - x : x, oy = topDown ? h - 1 - y : y;   // output rows bottom-up
                    int i = (oy * w + ox) * 4;
                    o[i] = c[0]; o[i + 1] = c[1]; o[i + 2] = c[2]; o[i + 3] = alpha ? c[3] : (byte)255;
                }
            return o;
        }

        static byte[] Px(byte[] d, int q, int bits)
        {
            switch (bits)
            {
                case 15: case 16:
                {
                    int v = d[q] | (d[q + 1] << 8);
                    byte r = (byte)(((v >> 10) & 31) * 255 / 31), g = (byte)(((v >> 5) & 31) * 255 / 31), b = (byte)((v & 31) * 255 / 31);
                    return new[] { r, g, b, (byte)((v & 0x8000) != 0 ? 255 : 0) };
                }
                case 24: return new[] { d[q + 2], d[q + 1], d[q], (byte)255 };
                case 32: return new[] { d[q + 2], d[q + 1], d[q], d[q + 3] };
                default: return new[] { d[q], d[q], d[q], (byte)255 };
            }
        }

        // ------------------------------------------------------------------ JPEG
        sealed class Jpeg
        {
            static readonly int[] ZigZag =
            {
                0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21,
                28, 35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61,
                54, 47, 55, 62, 63,
            };

            sealed class Huff
            {
                public readonly int[] MaxCode = new int[18], ValPtr = new int[17], MinCode = new int[17];
                public byte[] Vals;
                public Huff(byte[] counts, byte[] vals)
                {
                    Vals = vals;
                    int code = 0, k = 0;
                    for (int l = 1; l <= 16; l++)
                    {
                        ValPtr[l] = k; MinCode[l] = code;
                        code += counts[l - 1]; k += counts[l - 1];
                        MaxCode[l] = counts[l - 1] > 0 ? code - 1 : -1;
                        code <<= 1;
                    }
                    MaxCode[17] = int.MaxValue;
                }
            }

            sealed class Comp
            {
                public int Id, H, V, Tq, Td, Ta;
                public int BlocksPerLine, BlocksPerColumn;
                public short[] Coefs;   // 64 per block, natural order after decoding (zig-zag de-applied)
                public int Pred;
                public byte[] Plane; public int PlaneW;
            }

            readonly byte[] d;
            int pos;
            readonly ushort[][] qt = new ushort[4][];
            readonly Huff[] dcT = new Huff[4], acT = new Huff[4];
            readonly List<Comp> comps = new();
            int width, height, hMax, vMax, mcusX, mcusY, restart;
            bool progressive, adobe; int adobeTransform = -1;

            public Jpeg(byte[] data) { d = data; }

            int U16() { int v = (d[pos] << 8) | d[pos + 1]; pos += 2; return v; }

            public byte[] Decode(out int w, out int h)
            {
                w = h = 0;
                pos = 2;
                bool frame = false;
                while (pos < d.Length)
                {
                    if (d[pos] != 0xFF) { pos++; continue; }
                    int m = d[pos + 1]; pos += 2;
                    if (m == 0xD8 || m == 0x01 || (m >= 0xD0 && m <= 0xD7)) continue;
                    if (m == 0xFF) { pos--; continue; }
                    if (m == 0xD9) break;
                    int len = U16(), end = pos + len - 2;
                    switch (m)
                    {
                        case 0xDB:
                            while (pos < end)
                            {
                                int pq = d[pos] >> 4, tq = d[pos] & 15; pos++;
                                var t = new ushort[64];
                                for (int i = 0; i < 64; i++) t[ZigZag[i]] = pq == 0 ? d[pos++] : (ushort)U16();
                                qt[tq & 3] = t;
                            }
                            break;
                        case 0xC4:
                            while (pos < end)
                            {
                                int tc = d[pos] >> 4, th = d[pos] & 15; pos++;
                                var counts = new byte[16]; Array.Copy(d, pos, counts, 0, 16); pos += 16;
                                int total = 0; foreach (var c in counts) total += c;
                                var vals = new byte[total]; Array.Copy(d, pos, vals, 0, total); pos += total;
                                (tc == 0 ? dcT : acT)[th & 3] = new Huff(counts, vals);
                            }
                            break;
                        case 0xC0: case 0xC1: case 0xC2:
                        {
                            progressive = m == 0xC2;
                            pos++;   // precision
                            height = U16(); width = U16();
                            int n = d[pos++];
                            for (int i = 0; i < n; i++)
                            {
                                var c = new Comp { Id = d[pos], H = d[pos + 1] >> 4, V = d[pos + 1] & 15, Tq = d[pos + 2] };
                                pos += 3; comps.Add(c);
                            }
                            Prepare(); frame = true;
                            break;
                        }
                        case 0xC3: case 0xC5: case 0xC6: case 0xC7: case 0xC9: case 0xCA: case 0xCB: case 0xCD: case 0xCE: case 0xCF:
                            return null;   // lossless / arithmetic coding: not used by texture files
                        case 0xDD: restart = U16(); break;
                        case 0xEE:
                            if (len >= 12 && d[pos] == 'A' && d[pos + 1] == 'd' && d[pos + 2] == 'o' && d[pos + 3] == 'b' && d[pos + 4] == 'e') { adobe = true; adobeTransform = d[pos + 11]; }
                            break;
                        case 0xDA:
                        {
                            if (!frame) return null;
                            int n = d[pos++];
                            var sc = new List<Comp>();
                            for (int i = 0; i < n; i++)
                            {
                                int id = d[pos], t = d[pos + 1]; pos += 2;
                                var c = comps.Find(x => x.Id == id) ?? throw new InvalidDataException("scan component");
                                c.Td = t >> 4; c.Ta = t & 15; sc.Add(c);
                            }
                            int ss = d[pos], se = d[pos + 1], ah = d[pos + 2] >> 4, al = d[pos + 2] & 15; pos += 3;
                            Scan(sc, ss, se, ah, al);
                            continue;   // Scan left pos on the next marker
                        }
                    }
                    pos = end;
                }
                if (!frame) return null;
                w = width; h = height;
                foreach (var c in comps) Idct(c);
                return Color();
            }

            void Prepare()
            {
                foreach (var c in comps) { hMax = Math.Max(hMax, c.H); vMax = Math.Max(vMax, c.V); }
                mcusX = (width + 8 * hMax - 1) / (8 * hMax); mcusY = (height + 8 * vMax - 1) / (8 * vMax);
                foreach (var c in comps)
                {
                    c.BlocksPerLine = (int)Math.Ceiling(Math.Ceiling(width * c.H / (double)hMax) / 8);
                    c.BlocksPerColumn = (int)Math.Ceiling(Math.Ceiling(height * c.V / (double)vMax) / 8);
                    int bpl = mcusX * c.H, bpc = mcusY * c.V;
                    c.Coefs = new short[bpl * bpc * 64];
                    c.PlaneW = bpl * 8;
                }
            }

            // ------------------------------------------------ entropy decoding
            int bitBuf, bitCnt;
            int Bit()
            {
                if (bitCnt == 0)
                {
                    if (pos >= d.Length) return 0;
                    int b = d[pos];
                    if (b == 0xFF)
                    {
                        int nx = pos + 1 < d.Length ? d[pos + 1] : 0;
                        if (nx == 0) pos += 2;
                        else return 0;   // a marker: feed zeros
                    }
                    else pos++;
                    bitBuf = b; bitCnt = 8;
                }
                bitCnt--;
                return (bitBuf >> bitCnt) & 1;
            }
            int Bits(int n) { int v = 0; while (n-- > 0) v = (v << 1) | Bit(); return v; }
            int Receive(int n) { if (n == 0) return 0; int v = Bits(n); return v < (1 << (n - 1)) ? v - (1 << n) + 1 : v; }
            int DecodeHuff(Huff t)
            {
                int code = 0;
                for (int l = 1; l <= 16; l++)
                {
                    code = (code << 1) | Bit();
                    if (code <= t.MaxCode[l]) return t.Vals[t.ValPtr[l] + code - t.MinCode[l]];
                }
                return 0;
            }

            int eobrun;

            void Scan(List<Comp> sc, int ss, int se, int ah, int al)
            {
                bitCnt = 0; eobrun = 0;
                foreach (var c in comps) c.Pred = 0;
                Action<Comp, int> block;
                if (!progressive) block = (c, o) => Baseline(c, o);
                else if (ss == 0) block = ah == 0 ? (c, o) => DcFirst(c, o, al) : (c, o) => DcRefine(c, o, al);
                else block = ah == 0 ? (c, o) => AcFirst(c, o, ss, se, al) : (c, o) => AcRefine(c, o, ss, se, al);

                int total = sc.Count == 1 ? sc[0].BlocksPerLine * sc[0].BlocksPerColumn : mcusX * mcusY;
                int interval = restart > 0 ? restart : total;
                int n = 0;
                while (n < total)
                {
                    for (int i = 0; i < interval && n < total; i++, n++)
                    {
                        if (sc.Count == 1)
                        {
                            var c = sc[0];
                            int row = n / c.BlocksPerLine, col = n % c.BlocksPerLine;
                            block(c, (row * mcusX * c.H + col) * 64);
                        }
                        else
                        {
                            int mr = n / mcusX, mc = n % mcusX;
                            foreach (var c in sc)
                                for (int v = 0; v < c.V; v++)
                                    for (int hh = 0; hh < c.H; hh++)
                                    {
                                        int row = mr * c.V + v, col = mc * c.H + hh;
                                        block(c, (row * mcusX * c.H + col) * 64);
                                    }
                        }
                    }
                    // restart marker (or the end of the scan): realign to the next marker
                    bitCnt = 0; eobrun = 0;
                    while (pos + 1 < d.Length && !(d[pos] == 0xFF && d[pos + 1] != 0 && d[pos + 1] != 0xFF)) pos++;
                    if (pos + 1 < d.Length && d[pos + 1] >= 0xD0 && d[pos + 1] <= 0xD7) { pos += 2; foreach (var c in sc) c.Pred = 0; }
                    else break;
                }
            }

            void Baseline(Comp c, int o)
            {
                int t = DecodeHuff(dcT[c.Td]);
                c.Pred += Receive(t);
                c.Coefs[o] = (short)c.Pred;
                var ac = acT[c.Ta];
                for (int k = 1; k < 64;)
                {
                    int rs = DecodeHuff(ac), s = rs & 15, r = rs >> 4;
                    if (s == 0) { if (r < 15) break; k += 16; continue; }
                    k += r;
                    if (k > 63) break;
                    c.Coefs[o + ZigZag[k]] = (short)Receive(s);
                    k++;
                }
            }

            void DcFirst(Comp c, int o, int al)
            {
                int t = DecodeHuff(dcT[c.Td]);
                c.Pred += Receive(t);
                c.Coefs[o] = (short)(c.Pred << al);
            }
            void DcRefine(Comp c, int o, int al) { if (Bit() != 0) c.Coefs[o] |= (short)(1 << al); }

            void AcFirst(Comp c, int o, int ss, int se, int al)
            {
                if (eobrun > 0) { eobrun--; return; }
                var ac = acT[c.Ta];
                for (int k = ss; k <= se;)
                {
                    int rs = DecodeHuff(ac), s = rs & 15, r = rs >> 4;
                    if (s == 0)
                    {
                        if (r < 15) { eobrun = Bits(r) + (1 << r) - 1; break; }
                        k += 16; continue;
                    }
                    k += r;
                    if (k > 63) break;
                    c.Coefs[o + ZigZag[k]] = (short)(Receive(s) * (1 << al));
                    k++;
                }
            }

            void AcRefine(Comp c, int o, int ss, int se, int al)
            {
                int p1 = 1 << al, m1 = -1 << al;
                int k = ss;
                if (eobrun <= 0)
                {
                    var ac = acT[c.Ta];
                    for (; k <= se;)
                    {
                        int rs = DecodeHuff(ac), s = rs & 15, r = rs >> 4, val = 0;
                        if (s == 0)
                        {
                            if (r < 15) { eobrun = (1 << r) + Bits(r); break; }
                        }
                        else val = Bit() != 0 ? p1 : m1;
                        // advance over r zero-history coefficients, refining non-zero ones on the way
                        while (k <= se)
                        {
                            int z = o + ZigZag[k];
                            if (c.Coefs[z] != 0)
                            {
                                if (Bit() != 0 && (c.Coefs[z] & p1) == 0) c.Coefs[z] += (short)(c.Coefs[z] >= 0 ? p1 : m1);
                            }
                            else
                            {
                                if (r == 0) { if (val != 0) c.Coefs[z] = (short)val; k++; break; }
                                r--;
                            }
                            k++;
                        }
                    }
                }
                if (eobrun > 0)
                {
                    for (; k <= se; k++)
                    {
                        int z = o + ZigZag[k];
                        if (c.Coefs[z] != 0 && Bit() != 0 && (c.Coefs[z] & p1) == 0) c.Coefs[z] += (short)(c.Coefs[z] >= 0 ? p1 : m1);
                    }
                    eobrun--;
                }
            }

            // ------------------------------------------------ dequantise + IDCT
            static readonly float[] Cos = BuildCos();
            static float[] BuildCos()
            {
                var t = new float[64];
                for (int x = 0; x < 8; x++)
                    for (int u = 0; u < 8; u++)
                        t[x * 8 + u] = (float)((u == 0 ? Math.Sqrt(0.5) : 1) * Math.Cos((2 * x + 1) * u * Math.PI / 16) / 2);
                return t;
            }

            void Idct(Comp c)
            {
                var q = qt[c.Tq] ?? new ushort[64];
                int bpl = mcusX * c.H, bpc = mcusY * c.V;
                c.Plane = new byte[bpl * 8 * bpc * 8];
                var tmp = new float[64]; var blk = new float[64];
                for (int by = 0; by < bpc; by++)
                    for (int bx = 0; bx < bpl; bx++)
                    {
                        int o = (by * bpl + bx) * 64;
                        for (int i = 0; i < 64; i++) blk[i] = c.Coefs[o + i] * q[i];
                        // rows: tmp[y][x] = Σu blk[y][u] C(x,u)
                        for (int y = 0; y < 8; y++)
                            for (int x = 0; x < 8; x++)
                            {
                                float s = 0;
                                for (int u = 0; u < 8; u++) s += blk[y * 8 + u] * Cos[x * 8 + u];
                                tmp[y * 8 + x] = s;
                            }
                        for (int x = 0; x < 8; x++)
                            for (int y = 0; y < 8; y++)
                            {
                                float s = 0;
                                for (int v = 0; v < 8; v++) s += tmp[v * 8 + x] * Cos[y * 8 + v];
                                int val = (int)Math.Round(s + 128);
                                c.Plane[(by * 8 + y) * c.PlaneW + bx * 8 + x] = (byte)(val < 0 ? 0 : val > 255 ? 255 : val);
                            }
                    }
                c.Coefs = null;
            }

            byte[] Color()
            {
                var o = new byte[width * height * 4];
                int n = comps.Count;
                bool transform = n == 3 ? (!adobe || adobeTransform != 0) : n == 4 && adobeTransform == 2;
                for (int y = 0; y < height; y++)
                {
                    int oy = height - 1 - y;   // bottom-up
                    for (int x = 0; x < width; x++)
                    {
                        // libjpeg's "fancy" upsampling: subsampled planes interpolated between sample centres (3/4, 1/4)
                        float S(Comp c)
                        {
                            if (c.H == hMax && c.V == vMax) return c.Plane[y * c.PlaneW + x];
                            float fx = (x + 0.5f) * c.H / hMax - 0.5f, fy = (y + 0.5f) * c.V / vMax - 0.5f;
                            int cw = (width * c.H + hMax - 1) / hMax, ch = (height * c.V + vMax - 1) / vMax;
                            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
                            float tx = fx - x0, ty = fy - y0;
                            int xa = Math.Clamp(x0, 0, cw - 1), xb = Math.Clamp(x0 + 1, 0, cw - 1), ya = Math.Clamp(y0, 0, ch - 1), yb = Math.Clamp(y0 + 1, 0, ch - 1);
                            float top = c.Plane[ya * c.PlaneW + xa] * (1 - tx) + c.Plane[ya * c.PlaneW + xb] * tx;
                            float bot = c.Plane[yb * c.PlaneW + xa] * (1 - tx) + c.Plane[yb * c.PlaneW + xb] * tx;
                            return top * (1 - ty) + bot * ty;
                        }
                        int i = (oy * width + x) * 4;
                        if (n == 1) { byte g = (byte)S(comps[0]); o[i] = o[i + 1] = o[i + 2] = g; o[i + 3] = 255; continue; }
                        float a = S(comps[0]), b = S(comps[1]), cc = S(comps[2]);
                        float r, g2, bl;
                        if (transform) { r = a + 1.402f * (cc - 128); g2 = a - 0.344136f * (b - 128) - 0.714136f * (cc - 128); bl = a + 1.772f * (b - 128); }
                        else { r = a; g2 = b; bl = cc; }
                        if (n == 4)
                        {
                            // CMYK (Adobe stores it inverted)
                            float k = S(comps[3]);
                            r = r * k / 255; g2 = g2 * k / 255; bl = bl * k / 255;
                            if (transform) { r = 255 - r; g2 = 255 - g2; bl = 255 - bl; }
                        }
                        o[i] = Clamp(r); o[i + 1] = Clamp(g2); o[i + 2] = Clamp(bl); o[i + 3] = 255;
                    }
                }
                return o;
            }
            static byte Clamp(float v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : (int)(v + 0.5f));
        }
    }
}
