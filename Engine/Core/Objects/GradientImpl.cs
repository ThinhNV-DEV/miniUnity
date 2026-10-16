// UnityEngine.Gradient: up to 8 colour and 8 alpha keys, evaluated like Unity (clamped to the first / last key; Blend interpolates
// between the neighbouring keys, Fixed holds the next key's value, PerceptualBlend interpolates in Oklab); with colorSpace Linear the
// colour blend happens in linear space. Serialized as Unity writes it (key0..7, ctime0..7, atime0..7 as 0..65535).
using System;
using System.Linq;

namespace UnityEngine
{
    public partial struct GradientColorKey
    {
        public GradientColorKey(Color col, float time) { color = col; this.time = time; }
    }

    public partial struct GradientAlphaKey
    {
        public GradientAlphaKey(float alpha, float time) { this.alpha = alpha; this.time = time; }
    }

    public partial class Gradient
    {
        GradientColorKey[] m_Colors = { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) };
        GradientAlphaKey[] m_Alphas = { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) };
        GradientMode m_Mode = GradientMode.Blend;
        ColorSpace m_ColorSpace = ColorSpace.Uninitialized;

        public Gradient() { }

        public GradientColorKey[] colorKeys
        {
            get => (GradientColorKey[])m_Colors.Clone();
            set { if (value == null || value.Length == 0) throw new ArgumentException("colorKeys"); m_Colors = value.Take(8).OrderBy(k => k.time).ToArray(); }
        }
        public GradientAlphaKey[] alphaKeys
        {
            get => (GradientAlphaKey[])m_Alphas.Clone();
            set { if (value == null || value.Length == 0) throw new ArgumentException("alphaKeys"); m_Alphas = value.Take(8).OrderBy(k => k.time).ToArray(); }
        }
        public GradientMode mode { get => m_Mode; set => m_Mode = value; }
        public ColorSpace colorSpace { get => m_ColorSpace; set => m_ColorSpace = value; }
        internal Color constantColor { get => Evaluate(0); set => SetKeys(new[] { new GradientColorKey(value, 0) }, new[] { new GradientAlphaKey(value.a, 0) }); }

        public void SetKeys(GradientColorKey[] colorKeys, GradientAlphaKey[] alphaKeys) { this.colorKeys = colorKeys; this.alphaKeys = alphaKeys; }

        public Color Evaluate(float time)
        {
            var c = EvalColor(time);
            c.a = EvalAlpha(time);
            return c;
        }

        Color EvalColor(float t)
        {
            var k = m_Colors;
            if (t <= k[0].time || k.Length == 1) return Rgb(k[0].color);
            if (t >= k[^1].time) return Rgb(k[^1].color);
            int i = 1; while (i < k.Length - 1 && k[i].time < t) i++;
            var a = k[i - 1]; var b = k[i];
            if (m_Mode == GradientMode.Fixed) return Rgb(b.color);
            float f = b.time > a.time ? (t - a.time) / (b.time - a.time) : 1;
            if (m_Mode == GradientMode.PerceptualBlend) return Oklab(a.color, b.color, f);
            if (m_ColorSpace == ColorSpace.Linear)
            {
                var la = a.color.linear; var lb = b.color.linear;
                return Rgb(new Color(la.r + (lb.r - la.r) * f, la.g + (lb.g - la.g) * f, la.b + (lb.b - la.b) * f).gamma);
            }
            return Rgb(Color.LerpUnclamped(a.color, b.color, f));
        }

        float EvalAlpha(float t)
        {
            var k = m_Alphas;
            if (t <= k[0].time || k.Length == 1) return k[0].alpha;
            if (t >= k[^1].time) return k[^1].alpha;
            int i = 1; while (i < k.Length - 1 && k[i].time < t) i++;
            var a = k[i - 1]; var b = k[i];
            if (m_Mode == GradientMode.Fixed) return b.alpha;
            float f = b.time > a.time ? (t - a.time) / (b.time - a.time) : 1;
            return a.alpha + (b.alpha - a.alpha) * f;
        }

        static Color Rgb(Color c) => new Color(c.r, c.g, c.b, 1);

        static Color Oklab(Color a, Color b, float f)
        {
            Vector3 A = ToOklab(a.linear), B = ToOklab(b.linear);
            var m = FromOklab(A + (B - A) * f);
            return Rgb(new Color(m.x, m.y, m.z).gamma);
        }
        static Vector3 ToOklab(Color c)
        {
            float l = 0.4122214708f * c.r + 0.5363325363f * c.g + 0.0514459929f * c.b;
            float m = 0.2119034982f * c.r + 0.6806995451f * c.g + 0.1073969566f * c.b;
            float s = 0.0883024619f * c.r + 0.2817188376f * c.g + 0.6299787005f * c.b;
            l = MathF.Cbrt(l); m = MathF.Cbrt(m); s = MathF.Cbrt(s);
            return new Vector3(0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s, 1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s, 0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
        }
        static Vector3 FromOklab(Vector3 c)
        {
            float l = c.x + 0.3963377774f * c.y + 0.2158037573f * c.z, m = c.x - 0.1055613458f * c.y - 0.0638541728f * c.z, s = c.x - 0.0894841775f * c.y - 1.2914855480f * c.z;
            l = l * l * l; m = m * m * m; s = s * s * s;
            return new Vector3(4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s, -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s, -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
        }

        public override bool Equals(object o) => o is Gradient g && Equals(g);
        public bool Equals(Gradient other) => other != null && m_Mode == other.m_Mode && m_ColorSpace == other.m_ColorSpace
            && m_Colors.SequenceEqual(other.m_Colors) && m_Alphas.SequenceEqual(other.m_Alphas);
        public override int GetHashCode() => HashCode.Combine(m_Mode, m_Colors.Length, m_Alphas.Length, m_Colors[0].color);

        // ---- YAML (Unity's layout)
        internal MiniUnity.Yaml.YMap ToYaml()
        {
            var m = new MiniUnity.Yaml.YMap().Add("serializedVersion", "2");
            for (int i = 0; i < 8; i++)
            {
                var c = i < m_Colors.Length ? m_Colors[i].color : Color.white;
                float a = i < m_Alphas.Length ? m_Alphas[i].alpha : (i < m_Colors.Length ? 1 : 1);
                m.Add("key" + i, MiniUnity.Serializer.Col(new Color(c.r, c.g, c.b, a)));
            }
            for (int i = 0; i < 8; i++) m.Add("ctime" + i, MiniUnity.Yaml.YScalar.Of(i < m_Colors.Length ? (int)Math.Round(m_Colors[i].time * 65535) : 0));
            for (int i = 0; i < 8; i++) m.Add("atime" + i, MiniUnity.Yaml.YScalar.Of(i < m_Alphas.Length ? (int)Math.Round(m_Alphas[i].time * 65535) : 0));
            m.Add("m_Mode", MiniUnity.Yaml.YScalar.Of((int)m_Mode)).Add("m_ColorSpace", MiniUnity.Yaml.YScalar.Of(m_ColorSpace == ColorSpace.Linear ? 1 : m_ColorSpace == ColorSpace.Gamma ? 0 : -1))
             .Add("m_NumColorKeys", MiniUnity.Yaml.YScalar.Of(m_Colors.Length)).Add("m_NumAlphaKeys", MiniUnity.Yaml.YScalar.Of(m_Alphas.Length));
            return m;
        }

        internal static Gradient FromYaml(MiniUnity.Yaml.YNode n)
        {
            var g = new Gradient();
            if (n is not MiniUnity.Yaml.YMap m) return g;
            float F(string k, float d = 0) => m[k] is MiniUnity.Yaml.YScalar s && s.Value.Length > 0 ? s.F : d;
            int nc = Math.Clamp((int)F("m_NumColorKeys", 2), 1, 8), na = Math.Clamp((int)F("m_NumAlphaKeys", 2), 1, 8);
            var cols = new GradientColorKey[nc]; var alps = new GradientAlphaKey[na];
            for (int i = 0; i < nc; i++) { var c = MiniUnity.Serializer.ReadColor(m["key" + i]); cols[i] = new GradientColorKey(new Color(c.r, c.g, c.b, 1), F("ctime" + i) / 65535f); }
            for (int i = 0; i < na; i++) { var c = MiniUnity.Serializer.ReadColor(m["key" + i]); alps[i] = new GradientAlphaKey(c.a, F("atime" + i) / 65535f); }
            g.m_Colors = cols.OrderBy(k => k.time).ToArray(); g.m_Alphas = alps.OrderBy(k => k.time).ToArray();
            g.m_Mode = (GradientMode)(int)F("m_Mode");
            int cs = (int)F("m_ColorSpace", -1);
            g.m_ColorSpace = cs == 1 ? ColorSpace.Linear : cs == 0 ? ColorSpace.Gamma : ColorSpace.Uninitialized;
            return g;
        }
    }
}
