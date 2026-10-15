using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MiniUnity.Yaml
{
    /// <summary>Unity-flavoured YAML node tree (ordered maps, sequences, scalars, live object refs).</summary>
    public abstract class YNode
    {
        public abstract YNode Clone();
        /// <summary>Flow node that was folded over several lines in the source file (older Unity emitters wrap at column 80); kept on write.</summary>
        public bool Wrapped;
    }

    public sealed class YScalar : YNode
    {
        public string Value;
        public YScalar(string v) { Value = v ?? ""; }
        public override YNode Clone() => new YScalar(Value);
        public override string ToString() => Value;
        public static YScalar Of(float f) => new YScalar(Fmt.F(f));
        public static YScalar Of(double d) => new YScalar(Fmt.D(d));
        public static YScalar Of(int i) => new YScalar(i.ToString(CultureInfo.InvariantCulture));
        public static YScalar Of(long i) => new YScalar(i.ToString(CultureInfo.InvariantCulture));
        public static YScalar Of(bool b) => new YScalar(b ? "1" : "0");
        // parsed values are kept while Value is the same string instance (Value is a public field and may be reassigned)
        string fSrc, lSrc; float f; long l;
        public float F { get { var v = Value; if (!ReferenceEquals(v, fSrc)) { f = Fmt.ParseF(v); fSrc = v; } return f; } }
        public double D => Fmt.ParseD(Value);
        public int I => (int)L;
        public long L { get { var v = Value; if (!ReferenceEquals(v, lSrc)) { l = Fmt.ParseL(v); lSrc = v; } return l; } }
        public bool B => Value != "0" && Value != "" && Value != "false";
    }

    public sealed class YSeq : YNode
    {
        public List<YNode> Items = new();
        public bool Flow; // e.g. []
        public override YNode Clone() { var s = new YSeq { Flow = Flow }; foreach (var i in Items) s.Items.Add(i.Clone()); return s; }
    }

    public sealed class YMap : YNode
    {
        public List<KeyValuePair<string, YNode>> Entries = new();
        public bool Flow;
        public YNode this[string key]
        {
            get { foreach (var e in Entries) if (e.Key == key) return e.Value; return null; }
            set
            {
                for (int i = 0; i < Entries.Count; i++) if (Entries[i].Key == key) { Entries[i] = new(key, value); return; }
                Entries.Add(new(key, value));
            }
        }
        public bool Has(string key) { foreach (var e in Entries) if (e.Key == key) return true; return false; }
        public YMap Add(string k, YNode v) { Entries.Add(new(k, v)); return this; }
        public YMap Add(string k, string v) { Entries.Add(new(k, new YScalar(v))); return this; }
        public override YNode Clone() { var m = new YMap { Flow = Flow, Wrapped = Wrapped }; foreach (var e in Entries) m.Entries.Add(new(e.Key, e.Value?.Clone())); return m; }
    }

    /// <summary>Reference to a live object (or unresolved external ref).</summary>
    public sealed class YRef : YNode
    {
        public UnityEngine.Object Target;
        // unresolved raw ref (as read from file)
        public long FileID; public string Guid; public int Type;
        public bool Raw;
        public YRef(UnityEngine.Object o) { Target = o; }
        public YRef(long fid, string guid, int type) { FileID = fid; Guid = guid; Type = type; Raw = true; }
        public override YNode Clone() => Raw ? new YRef(FileID, Guid, Type) { Wrapped = Wrapped } : new YRef(Target);
        public bool IsNull => Raw ? (FileID == 0) : ReferenceEquals(Target, null) || Target.m_Destroyed;
    }

    public static class Fmt
    {
        public static string F(float f)
        {
            if (float.IsNaN(f)) return "NaN";
            if (float.IsPositiveInfinity(f)) return "Infinity";
            if (float.IsNegativeInfinity(f)) return "-Infinity";
            if (f == 0) return (1 / f) < 0 ? "-0" : "0";
            var s = f.ToString("R", CultureInfo.InvariantCulture);
            return FixExp(s);
        }
        public static string D(double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsPositiveInfinity(d)) return "Infinity";
            if (double.IsNegativeInfinity(d)) return "-Infinity";
            if (d == 0) return (1 / d) < 0 ? "-0" : "0";
            var s = d.ToString("R", CultureInfo.InvariantCulture);
            return FixExp(s);
        }
        /// <summary>Unity writes floats with round-trip digits but never in exponent form (1.7887321e-06 → 0.0000017887321).</summary>
        static string FixExp(string s)
        {
            int e = s.IndexOf('E');
            if (e < 0) return s;
            string mant = s.Substring(0, e);
            int exp = int.Parse(s.Substring(e + 1), CultureInfo.InvariantCulture);
            bool neg = mant.StartsWith("-");
            if (neg) mant = mant.Substring(1);
            int dot = mant.IndexOf('.');
            string digits = dot < 0 ? mant : mant.Remove(dot, 1);
            int intLen = (dot < 0 ? mant.Length : dot) + exp; // position of decimal point within digits
            string res;
            if (intLen <= 0) res = "0." + new string('0', -intLen) + digits;
            else if (intLen >= digits.Length) res = digits + new string('0', intLen - digits.Length);
            else res = digits.Substring(0, intLen) + "." + digits.Substring(intLen);
            return (neg ? "-" : "") + res;
        }
        public static float ParseF(string s)
        {
            s = s.Trim();
            if (s == "Infinity" || s == "inf") return float.PositiveInfinity;
            if (s == "-Infinity" || s == "-inf") return float.NegativeInfinity;
            if (s == "NaN" || s == "nan") return float.NaN;
            if (s.Length == 0) return 0;
            return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        public static double ParseD(string s)
        {
            s = s.Trim();
            if (s == "Infinity") return double.PositiveInfinity;
            if (s == "-Infinity") return double.NegativeInfinity;
            if (s == "NaN") return double.NaN;
            if (s.Length == 0) return 0;
            return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        public static long ParseL(string s)
        {
            s = s.Trim();
            if (s.Length == 0) return 0;
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
            if (ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ul)) return unchecked((long)ul);
            return (long)double.Parse(s, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>One "--- !u!CLASS &amp;ID [stripped]" document.</summary>
    public sealed class YDoc
    {
        public int ClassId;
        public long FileId;
        public bool Stripped;
        public string TypeName;
        public YMap Body; // contents under TypeName
    }
}
