using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using MiniUnity;

namespace UnityEngine
{
    public static partial class ImageConversion
    {
        /// <summary>Minimal RGBA8 PNG encoder (zlib via System.IO.Compression).</summary>
        public static int ReusedPngs, NewPngs;
        public static byte[] EncodeToPNG(this Texture2D tex)
        {
            var existing = MiniUnity.PngReuse.FindIdentical(tex);
            if (existing != null) { ReusedPngs++; return existing; }
            NewPngs++;
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels();
            var raw = new byte[(w * 4 + 1) * h];
            int k = 0;
            for (int y = h - 1; y >= 0; y--) // PNG rows top→bottom; Unity pixels bottom→top
            {
                raw[k++] = 0;
                for (int x = 0; x < w; x++)
                {
                    Color32 c = px[y * w + x];
                    raw[k++] = c.r; raw[k++] = c.g; raw[k++] = c.b; raw[k++] = c.a;
                }
            }
            using var ms = new MemoryStream();
            ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            var ihdr = new byte[13];
            BE(ihdr, 0, w); BE(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 6;
            Chunk(ms, "IHDR", ihdr);
            using (var z = new MemoryStream())
            {
                using (var zs = new ZLibStream(z, System.IO.Compression.CompressionLevel.Optimal, true)) zs.Write(raw);
                Chunk(ms, "IDAT", z.ToArray());
            }
            Chunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }
        public static byte[] EncodeToJPG(this Texture2D tex) => EncodeToPNG(tex);
        public static byte[] EncodeToJPG(this Texture2D tex, int quality) => EncodeToPNG(tex);
        public static bool LoadImage(this Texture2D tex, byte[] data) => LoadImage(tex, data, false);
        public static bool LoadImage(this Texture2D tex, byte[] data, bool markNonReadable) => throw new NotImplementedException("ImageConversion.LoadImage (PNG decode)");

        static void BE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; BE(len, 0, data.Length); s.Write(len);
            var t = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(t); s.Write(data);
            uint crc = Crc(t, 0xFFFFFFFFu); crc = Crc(data, crc) ^ 0xFFFFFFFFu;
            var c = new byte[4]; BE(c, 0, (int)crc); s.Write(c);
        }
        static uint[] table;
        static uint Crc(byte[] d, uint c)
        {
            if (table == null) { table = new uint[256]; for (uint n = 0; n < 256; n++) { uint x = n; for (int k = 0; k < 8; k++) x = (x & 1) != 0 ? 0xEDB88320u ^ (x >> 1) : x >> 1; table[n] = x; } }
            foreach (var b in d) c = table[(c ^ b) & 0xFF] ^ (c >> 8);
            return c;
        }
    }
}

namespace MiniUnity
{
    /// <summary>Unity's PNG encoder output is not reproducible here; when a PNG with (practically) the same pixels already
    /// exists in the project, return its bytes so re-running the builder doesn't rewrite unchanged images.</summary>
    public static class PngReuse
    {
        public static int Tolerance = 1;
        static Dictionary<(int, int), List<string>> bySize;
        public static byte[] FindIdentical(UnityEngine.Texture2D tex)
        {
            if (bySize == null)
            {
                bySize = new();
                foreach (var f in Directory.EnumerateFiles(Path.Combine(AssetIndex.Root, "Assets"), "*.png", SearchOption.AllDirectories))
                {
                    var (w, h) = Importers.ImageSize(f);
                    if (!bySize.TryGetValue((w, h), out var l)) bySize[(w, h)] = l = new();
                    l.Add(f);
                }
            }
            if (!bySize.TryGetValue((tex.width, tex.height), out var cands)) return null;
            var px = tex.GetPixels();
            foreach (var f in cands)
            {
                byte[] bytes; byte[] rgba;
                try { bytes = File.ReadAllBytes(f); rgba = Decode(bytes, out _, out _); } catch { continue; }
                if (rgba == null) continue;
                int w = tex.width, h = tex.height; bool same = true;
                for (int y = 0; y < h && same; y++)
                    for (int x = 0; x < w; x++)
                    {
                        UnityEngine.Color32 c = px[y * w + x];
                        int o = ((h - 1 - y) * w + x) * 4;
                        if (Math.Abs(c.r - rgba[o]) > Tolerance || Math.Abs(c.g - rgba[o + 1]) > Tolerance || Math.Abs(c.b - rgba[o + 2]) > Tolerance || Math.Abs(c.a - rgba[o + 3]) > Tolerance) { same = false; break; }
                    }
                if (same) return bytes;
            }
            return null;
        }

        /// <summary>8-bit RGB/RGBA/Gray(+A) non-interlaced PNG → RGBA rows top→bottom.</summary>
        public static byte[] Decode(byte[] d, out int w, out int h)
        {
            w = h = 0; int ct = 0, bd = 0, il = 0; var idat = new MemoryStream();
            int i = 8;
            while (i + 8 <= d.Length)
            {
                int len = (d[i] << 24) | (d[i + 1] << 16) | (d[i + 2] << 8) | d[i + 3];
                string t = System.Text.Encoding.ASCII.GetString(d, i + 4, 4);
                if (t == "IHDR") { w = (d[i + 8] << 24) | (d[i + 9] << 16) | (d[i + 10] << 8) | d[i + 11]; h = (d[i + 12] << 24) | (d[i + 13] << 16) | (d[i + 14] << 8) | d[i + 15]; bd = d[i + 16]; ct = d[i + 17]; il = d[i + 20]; }
                else if (t == "IDAT") idat.Write(d, i + 8, len);
                else if (t == "IEND") break;
                i += 12 + len;
            }
            if (bd != 8 || il != 0) return null;
            int ch = ct switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
            if (ch == 0) return null;
            idat.Position = 0;
            using var z = new ZLibStream(idat, CompressionMode.Decompress);
            var raw = new MemoryStream(); z.CopyTo(raw);
            var r = raw.ToArray();
            int stride = w * ch; var cur = new byte[stride]; var prev = new byte[stride];
            var outp = new byte[w * h * 4]; int p = 0;
            for (int y = 0; y < h; y++)
            {
                int f = r[p++];
                Array.Copy(r, p, cur, 0, stride); p += stride;
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= ch ? cur[x - ch] : 0, b = prev[x], c = x >= ch ? prev[x - ch] : 0;
                    int v = cur[x];
                    switch (f)
                    {
                        case 1: v += a; break;
                        case 2: v += b; break;
                        case 3: v += (a + b) >> 1; break;
                        case 4: { int pp = a + b - c, pa = Math.Abs(pp - a), pb = Math.Abs(pp - b), pc = Math.Abs(pp - c); v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c; break; }
                    }
                    cur[x] = (byte)v;
                }
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4, s = x * ch;
                    switch (ch)
                    {
                        case 4: outp[o] = cur[s]; outp[o + 1] = cur[s + 1]; outp[o + 2] = cur[s + 2]; outp[o + 3] = cur[s + 3]; break;
                        case 3: outp[o] = cur[s]; outp[o + 1] = cur[s + 1]; outp[o + 2] = cur[s + 2]; outp[o + 3] = 255; break;
                        case 2: outp[o] = outp[o + 1] = outp[o + 2] = cur[s]; outp[o + 3] = cur[s + 1]; break;
                        case 1: outp[o] = outp[o + 1] = outp[o + 2] = cur[s]; outp[o + 3] = 255; break;
                    }
                }
                (prev, cur) = (cur, prev);
            }
            return outp;
        }
    }
}
