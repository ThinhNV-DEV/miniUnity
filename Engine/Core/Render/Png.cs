// PNG decoder (8/16-bit, gray/gray+alpha/RGB/RGBA/palette, non-interlaced and Adam7) → RGBA8, rows bottom-up like Unity.
using System;
using System.IO;
using System.IO.Compression;

namespace MiniUnity
{
    public static class Png
    {
        public static bool TryDecode(byte[] data, out int w, out int h, out byte[] rgba)
        {
            w = h = 0; rgba = null;
            if (data == null || data.Length < 8 || data[0] != 0x89 || data[1] != 'P' || data[2] != 'N' || data[3] != 'G') return false;
            int pos = 8, bitDepth = 8, colorType = 6, interlace = 0;
            byte[] palette = null, trns = null;
            using var idat = new MemoryStream();
            while (pos + 8 <= data.Length)
            {
                int len = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
                string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
                int d = pos + 8;
                switch (type)
                {
                    case "IHDR":
                        w = (data[d] << 24) | (data[d + 1] << 16) | (data[d + 2] << 8) | data[d + 3];
                        h = (data[d + 4] << 24) | (data[d + 5] << 16) | (data[d + 6] << 8) | data[d + 7];
                        bitDepth = data[d + 8]; colorType = data[d + 9]; interlace = data[d + 12];
                        break;
                    case "PLTE": palette = new byte[len]; Array.Copy(data, d, palette, 0, len); break;
                    case "tRNS": trns = new byte[len]; Array.Copy(data, d, trns, 0, len); break;
                    case "IDAT": idat.Write(data, d, len); break;
                }
                if (type == "IEND") break;
                pos = d + len + 4;
            }
            if (w <= 0 || h <= 0) return false;
            int W = w, H = h;
            byte[] raw;
            idat.Position = 0;
            using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            using (var o = new MemoryStream()) { z.CopyTo(o); raw = o.ToArray(); }

            int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 4 };
            int bpp = Math.Max(1, channels * bitDepth / 8);   // bytes per complete pixel (filter unit)
            var outPx = new byte[W * H * 4];
            int rp = 0;

            void Pass(int x0, int y0, int dx, int dy)
            {
                int pw = (W - x0 + dx - 1) / dx, ph = (H - y0 + dy - 1) / dy;
                if (pw <= 0 || ph <= 0) return;
                int stride = (pw * channels * bitDepth + 7) / 8;
                var prev = new byte[stride]; var cur = new byte[stride];
                for (int y = 0; y < ph; y++)
                {
                    int ft = raw[rp++];
                    Array.Copy(raw, rp, cur, 0, stride); rp += stride;
                    for (int i = 0; i < stride; i++)
                    {
                        int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                        int v = cur[i];
                        switch (ft)
                        {
                            case 1: v += a; break;
                            case 2: v += b; break;
                            case 3: v += (a + b) >> 1; break;
                            case 4: { int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c); v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c; break; }
                        }
                        cur[i] = (byte)v;
                    }
                    for (int x = 0; x < pw; x++)
                    {
                        int r, g, bl, al = 255;
                        int Sample(int ch)
                        {
                            if (bitDepth == 8) return cur[x * channels + ch];
                            if (bitDepth == 16) return cur[(x * channels + ch) * 2];
                            int bit = (x * channels + ch) * bitDepth; int by = cur[bit >> 3]; int sh = 8 - bitDepth - (bit & 7);
                            int val = (by >> sh) & ((1 << bitDepth) - 1);
                            return colorType == 3 ? val : val * 255 / ((1 << bitDepth) - 1);
                        }
                        switch (colorType)
                        {
                            case 0: r = g = bl = Sample(0); if (trns != null && trns.Length >= 2 && r == trns[1]) al = 0; break;
                            case 2: r = Sample(0); g = Sample(1); bl = Sample(2); break;
                            case 3: { int i = Sample(0); r = palette[i * 3]; g = palette[i * 3 + 1]; bl = palette[i * 3 + 2]; al = trns != null && i < trns.Length ? trns[i] : 255; break; }
                            case 4: r = g = bl = Sample(0); al = Sample(1); break;
                            default: r = Sample(0); g = Sample(1); bl = Sample(2); al = Sample(3); break;
                        }
                        int X = x0 + x * dx, Y = y0 + y * dy;
                        int o = ((H - 1 - Y) * W + X) * 4;   // Unity rows: bottom → top
                        outPx[o] = (byte)r; outPx[o + 1] = (byte)g; outPx[o + 2] = (byte)bl; outPx[o + 3] = (byte)al;
                    }
                    (prev, cur) = (cur, prev);
                }
            }

            if (interlace == 0) Pass(0, 0, 1, 1);
            else
            {
                Pass(0, 0, 8, 8); Pass(4, 0, 8, 8); Pass(0, 4, 4, 8); Pass(2, 0, 4, 4);
                Pass(0, 2, 2, 4); Pass(1, 0, 2, 2); Pass(0, 1, 1, 2);
            }
            rgba = outPx;
            return true;
        }

        public static bool TryDecodeFile(string path, out int w, out int h, out byte[] rgba)
        {
            w = h = 0; rgba = null;
            try { return TryDecode(File.ReadAllBytes(path), out w, out h, out rgba); }
            catch { return false; }
        }
    }
}
