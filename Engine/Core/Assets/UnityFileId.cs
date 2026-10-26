using System;
using System.Text;

namespace MiniUnity
{
    /// <summary>The file id Unity gives the MonoScript of a class compiled into a DLL: the first four bytes (little endian) of
    /// MD4("s\0\0\0" + namespace + name). A scene names such a component by the DLL's guid and this id, where a .cs script is
    /// guid + 11500000.</summary>
    public static class UnityFileId
    {
        public static int Of(string ns, string name)
        {
            var h = Md4(Encoding.UTF8.GetBytes("s\0\0\0" + (ns ?? "") + name));
            return h[0] | h[1] << 8 | h[2] << 16 | h[3] << 24;
        }

        public static int Of(Type t) => Of(t.Namespace, t.Name);

        /// <summary>RFC 1320.</summary>
        public static byte[] Md4(byte[] input)
        {
            uint a = 0x67452301, b = 0xefcdab89, c = 0x98badcfe, d = 0x10325476;
            long bits = (long)input.Length * 8;
            int len = (input.Length + 8) / 64 * 64 + 64;
            var msg = new byte[len];
            Array.Copy(input, msg, input.Length);
            msg[input.Length] = 0x80;
            for (int i = 0; i < 8; i++) msg[len - 8 + i] = (byte)(bits >> (8 * i));
            var x = new uint[16];
            static uint Rot(uint v, int s) => v << s | v >> (32 - s);
            for (int off = 0; off < len; off += 64)
            {
                for (int i = 0; i < 16; i++) x[i] = BitConverter.ToUInt32(msg, off + i * 4);
                uint aa = a, bb = b, cc = c, dd = d;
                uint F(uint p, uint q, uint r) => (p & q) | (~p & r);
                uint G(uint p, uint q, uint r) => (p & q) | (p & r) | (q & r);
                uint H(uint p, uint q, uint r) => p ^ q ^ r;
                foreach (int i in new[] { 0, 4, 8, 12 })
                {
                    a = Rot(a + F(b, c, d) + x[i], 3); d = Rot(d + F(a, b, c) + x[i + 1], 7);
                    c = Rot(c + F(d, a, b) + x[i + 2], 11); b = Rot(b + F(c, d, a) + x[i + 3], 19);
                }
                foreach (int i in new[] { 0, 1, 2, 3 })
                {
                    a = Rot(a + G(b, c, d) + x[i] + 0x5a827999, 3); d = Rot(d + G(a, b, c) + x[i + 4] + 0x5a827999, 5);
                    c = Rot(c + G(d, a, b) + x[i + 8] + 0x5a827999, 9); b = Rot(b + G(c, d, a) + x[i + 12] + 0x5a827999, 13);
                }
                foreach (int i in new[] { 0, 2, 1, 3 })
                {
                    a = Rot(a + H(b, c, d) + x[i] + 0x6ed9eba1, 3); d = Rot(d + H(a, b, c) + x[i + 8] + 0x6ed9eba1, 9);
                    c = Rot(c + H(d, a, b) + x[i + 4] + 0x6ed9eba1, 11); b = Rot(b + H(c, d, a) + x[i + 12] + 0x6ed9eba1, 15);
                }
                a += aa; b += bb; c += cc; d += dd;
            }
            var res = new byte[16];
            BitConverter.GetBytes(a).CopyTo(res, 0); BitConverter.GetBytes(b).CopyTo(res, 4);
            BitConverter.GetBytes(c).CopyTo(res, 8); BitConverter.GetBytes(d).CopyTo(res, 12);
            return res;
        }
    }
}
