// FBX document reader: the node tree of binary FBX (versions 7.x; 64-bit record headers from 7.5, zlib-compressed arrays)
// and of ASCII FBX ("Name: values { … }", "*N { a: … }" arrays). FbxImporter.cs turns the tree into a model asset.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MiniUnity
{
    public sealed class FbxNode
    {
        public string Name;
        public readonly List<object> Props = new();      // long, int, short, bool, float, double, string, byte[], or arrays of those
        public readonly List<FbxNode> Children = new();

        public FbxNode Child(string name) { foreach (var c in Children) if (c.Name == name) return c; return null; }
        public IEnumerable<FbxNode> All(string name) { foreach (var c in Children) if (c.Name == name) yield return c; }
        public object P(int i) => i < Props.Count ? Props[i] : null;
        public string S(int i) => P(i) as string;
        public long L(int i) => P(i) switch { long l => l, int n => n, short s => s, double d => (long)d, float f => (long)f, bool b => b ? 1 : 0, _ => 0 };
        public double D(int i) => P(i) switch { double d => d, float f => f, long l => l, int n => n, short s => s, _ => 0 };

        /// <summary>The single array value of a node such as Vertices: *N { a: … } (ASCII) or a typed array (binary).</summary>
        public double[] Doubles()
        {
            var p = P(0);
            return p switch
            {
                double[] d => d,
                float[] f => Array.ConvertAll(f, x => (double)x),
                long[] l => Array.ConvertAll(l, x => (double)x),
                int[] n => Array.ConvertAll(n, x => (double)x),
                _ => Props.Count > 0 ? Props.ConvertAll(x => x switch { double d => d, float f => f, long l => l, int n => n, _ => 0.0 }).ToArray() : Array.Empty<double>(),
            };
        }
        public int[] Ints()
        {
            var p = P(0);
            return p switch
            {
                int[] n => n,
                long[] l => Array.ConvertAll(l, x => (int)x),
                double[] d => Array.ConvertAll(d, x => (int)x),
                _ => Props.Count > 0 ? Props.ConvertAll(x => x switch { int n => n, long l => (int)l, double d => (int)d, short s => s, _ => 0 }).ToArray() : Array.Empty<int>(),
            };
        }
        public override string ToString() => $"{Name} ({Props.Count} props, {Children.Count} children)";
    }

    public static class FbxReader
    {
        public static FbxNode Read(string path) => Parse(File.ReadAllBytes(path));

        public static FbxNode Parse(byte[] d)
        {
            if (IsBinary(d)) return Binary(d);
            return Ascii(Encoding.UTF8.GetString(d));
        }

        public static bool IsBinary(byte[] d) => d.Length > 27 && Encoding.ASCII.GetString(d, 0, 18) == "Kaydara FBX Binary";

        // ------------------------------------------------------------------ binary
        static FbxNode Binary(byte[] d)
        {
            int version = BitConverter.ToInt32(d, 23);
            bool wide = version >= 7500;
            var root = new FbxNode { Name = "" };
            long pos = 27;
            while (pos < d.Length)
            {
                var n = Record(d, ref pos, wide);
                if (n == null) break;
                root.Children.Add(n);
            }
            root.Props.Add((long)version);
            return root;
        }

        static FbxNode Record(byte[] d, ref long pos, bool wide)
        {
            long end, count;
            if (wide) { end = BitConverter.ToInt64(d, (int)pos); count = BitConverter.ToInt64(d, (int)pos + 8); pos += 24; }
            else { end = BitConverter.ToUInt32(d, (int)pos); count = BitConverter.ToUInt32(d, (int)pos + 4); pos += 12; }
            int nameLen = d[pos++];
            if (end == 0) return null;   // the null record closing a node list
            var node = new FbxNode { Name = Encoding.ASCII.GetString(d, (int)pos, nameLen) };
            pos += nameLen;
            for (long i = 0; i < count; i++) node.Props.Add(Property(d, ref pos));
            while (pos < end)
            {
                var c = Record(d, ref pos, wide);
                if (c == null) break;
                node.Children.Add(c);
            }
            pos = end;
            return node;
        }

        static object Property(byte[] d, ref long pos)
        {
            char t = (char)d[pos++];
            int p = (int)pos;
            switch (t)
            {
                case 'Y': pos += 2; return BitConverter.ToInt16(d, p);
                case 'C': pos += 1; return d[p] != 0;
                case 'I': pos += 4; return BitConverter.ToInt32(d, p);
                case 'F': pos += 4; return BitConverter.ToSingle(d, p);
                case 'D': pos += 8; return BitConverter.ToDouble(d, p);
                case 'L': pos += 8; return BitConverter.ToInt64(d, p);
                case 'S': case 'R':
                {
                    int len = BitConverter.ToInt32(d, p); pos += 4 + len;
                    if (t == 'R') { var raw = new byte[len]; Array.Copy(d, p + 4, raw, 0, len); return raw; }
                    // "Name\0\x01Class" → "Class::Name" (the ASCII form)
                    var s = Encoding.UTF8.GetString(d, p + 4, len);
                    int sep = s.IndexOf("\0\x01", StringComparison.Ordinal);
                    return sep >= 0 ? s.Substring(sep + 2) + "::" + s.Substring(0, sep) : s;
                }
                case 'f': case 'd': case 'l': case 'i': case 'b':
                {
                    int n = BitConverter.ToInt32(d, p), enc = BitConverter.ToInt32(d, p + 4), clen = BitConverter.ToInt32(d, p + 8);
                    pos += 12 + clen;
                    byte[] raw;
                    if (enc == 1)
                    {
                        using var z = new ZLibStream(new MemoryStream(d, p + 12, clen), CompressionMode.Decompress);
                        int size = n * (t is 'd' or 'l' ? 8 : t == 'b' ? 1 : 4);
                        raw = new byte[size];
                        int got = 0;
                        while (got < size) { int r = z.Read(raw, got, size - got); if (r <= 0) break; got += r; }
                    }
                    else { raw = new byte[clen]; Array.Copy(d, p + 12, raw, 0, clen); }
                    switch (t)
                    {
                        case 'f': { var a = new float[n]; Buffer.BlockCopy(raw, 0, a, 0, n * 4); return a; }
                        case 'd': { var a = new double[n]; Buffer.BlockCopy(raw, 0, a, 0, n * 8); return a; }
                        case 'l': { var a = new long[n]; Buffer.BlockCopy(raw, 0, a, 0, n * 8); return a; }
                        case 'i': { var a = new int[n]; Buffer.BlockCopy(raw, 0, a, 0, n * 4); return a; }
                        default: { var a = new bool[n]; for (int i = 0; i < n; i++) a[i] = raw[i] != 0; return a; }
                    }
                }
                default: throw new InvalidDataException($"FBX: unknown property type '{t}'");
            }
        }

        // ------------------------------------------------------------------ ASCII
        static FbxNode Ascii(string s)
        {
            int i = 0;
            var root = new FbxNode { Name = "" };
            AsciiChildren(s, ref i, root, false);
            return root;
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ';') { while (i < s.Length && s[i] != '\n') i++; }
                else if (char.IsWhiteSpace(c)) i++;
                else break;
            }
        }

        static void AsciiChildren(string s, ref int i, FbxNode parent, bool inBraces)
        {
            while (true)
            {
                Skip(s, ref i);
                if (i >= s.Length) return;
                if (s[i] == '}') { if (inBraces) i++; return; }
                // Name:
                int st = i;
                while (i < s.Length && s[i] != ':' && s[i] != '{' && s[i] != '}' && s[i] != '\n') i++;
                if (i >= s.Length || s[i] != ':') { i++; continue; }
                var node = new FbxNode { Name = s.Substring(st, i - st).Trim() };
                i++;
                // values on the rest of the line(s), comma separated, until '{' or end of line (not inside a value list)
                while (true)
                {
                    while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r')) i++;
                    if (i >= s.Length || s[i] == '\n' || s[i] == '{' || s[i] == '}' || s[i] == ';') break;
                    if (s[i] == ',') { i++; Skip(s, ref i); continue; }
                    if (s[i] == '"')
                    {
                        int e = s.IndexOf('"', i + 1);
                        node.Props.Add(s.Substring(i + 1, e - i - 1));
                        i = e + 1; continue;
                    }
                    if (s[i] == '*')   // array: *N { a: v,v,v }
                    {
                        while (i < s.Length && s[i] != '{') i++;
                        i++;
                        Skip(s, ref i);
                        while (i < s.Length && s[i] != ':') i++;
                        i++;
                        var vals = new List<double>();
                        while (i < s.Length && s[i] != '}')
                        {
                            Skip(s, ref i);
                            if (s[i] == ',') { i++; continue; }
                            if (s[i] == '}') break;
                            int vs = i;
                            while (i < s.Length && s[i] != ',' && s[i] != '}' && !char.IsWhiteSpace(s[i])) i++;
                            vals.Add(double.Parse(s.AsSpan(vs, i - vs), NumberStyles.Float, CultureInfo.InvariantCulture));
                        }
                        i++;
                        node.Props.Add(vals.ToArray());
                        continue;
                    }
                    int ts = i;
                    while (i < s.Length && s[i] != ',' && s[i] != '\n' && s[i] != '{' && s[i] != '\r') i++;
                    var tok = s.Substring(ts, i - ts).Trim();
                    if (long.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) node.Props.Add(l);
                    else if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv)) node.Props.Add(dv);
                    else node.Props.Add(tok);
                    // a value list may continue on the next line after a trailing comma
                    int look = i; while (look < s.Length && (s[look] == ' ' || s[look] == '\t' || s[look] == '\r')) look++;
                    if (look < s.Length && s[look] == ',') { i = look + 1; Skip(s, ref i); }
                }
                if (i < s.Length && s[i] == '{') { i++; AsciiChildren(s, ref i, node, true); }
                parent.Children.Add(node);
            }
        }
    }
}
