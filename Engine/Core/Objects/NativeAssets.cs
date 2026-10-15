using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniUnity;
using MiniUnity.Yaml;

namespace UnityEngine
{
    public partial class Shader
    {
        internal string m_ShaderName = "";
        static readonly Dictionary<string, int> s_Ids = new();
        public static int PropertyToID(string name)
        {
            if (name == null) return -1;
            if (!s_Ids.TryGetValue(name, out var id)) { id = s_Ids.Count + 1; s_Ids[name] = id; }
            return id;
        }
        internal static string IdToName(int id) => s_Ids.FirstOrDefault(k => k.Value == id).Key;
        public static Shader Find(string name) => ShaderIndex.Find(name);
        public static void EnableKeyword(string keyword) { }
        public static void DisableKeyword(string keyword) { }
        public static bool IsKeywordEnabled(string keyword) => false;
        public static void SetGlobalFloat(string name, float value) { }
        public static void SetGlobalFloat(int id, float value) { }
        public static void SetGlobalColor(int id, Color value) { }
        public static void SetGlobalTexture(int id, Texture value) { }
        public static void SetGlobalVector(int id, Vector4 value) { }
        public static void SetGlobalMatrix(int id, Matrix4x4 value) { }
        public bool isSupported => true;
        public int renderQueue => 3000;
        public int passCount => 1;
        public int maximumLOD { get; set; }
        public int FindPropertyIndex(string propertyName) => -1;
        public int GetPropertyCount() => 0;
        public new string name { get => m_ShaderName.Length > 0 ? m_ShaderName : m_Name; set => m_Name = value; }
    }

    public partial class Material
    {
        internal Material() { }
        public Material(Shader shader) { NSetRef("m_Shader", shader); }
        public Material(Material source)
        {
            if (source?.m_Native != null) m_Native = (YMap)source.m_Native.Clone();
            m_Name = source?.m_Name ?? "";
        }
        public Material(string contents) { }
        public Shader shader { get => NRef<Shader>("m_Shader"); set => NSetRef("m_Shader", value); }
        /// <summary>Custom queue, else the shader's: UI / TextMeshPro / Sprites are Transparent (3000), the rest Geometry (2000).</summary>
        public int renderQueue
        {
            get
            {
                if (NInt("m_CustomRenderQueue", -1) is int q && q >= 0) return q;
                var n = MiniUnity.UIRenderer.ShaderName(shader);
                return n.StartsWith("UI/") || n.StartsWith("TextMeshPro/") || n.StartsWith("Sprites/") ? 3000 : 2000;
            }
            set => NSetI("m_CustomRenderQueue", value);
        }
        public int rawRenderQueue => NInt("m_CustomRenderQueue", -1);
        public int passCount => 1;
        public bool enableInstancing { get; set; }
        public bool doubleSidedGI { get; set; }
        public MaterialGlobalIlluminationFlags globalIlluminationFlags { get; set; }
        public Material parent { get; set; }
        public bool isVariant => false;

        YSeq Section(string name)
        {
            var sp = NGet("m_SavedProperties") as YMap;
            if (sp == null) { sp = new YMap().Add("serializedVersion", "3").Add("m_TexEnvs", new YSeq()).Add("m_Ints", new YSeq()).Add("m_Floats", new YSeq()).Add("m_Colors", new YSeq()); NSet("m_SavedProperties", sp); }
            if (sp[name] is not YSeq s) { s = new YSeq(); sp[name] = s; }
            return s;
        }
        static string N(int id) => Shader.IdToName(id);
        YNode FindProp(string section, string name)
        {
            foreach (var it in Section(section).Items) if (it is YMap m && m.Entries.Count == 1 && m.Entries[0].Key == name) return m.Entries[0].Value;
            return null;
        }
        void SetProp(string section, string name, YNode v)
        {
            var s = Section(section);
            foreach (var it in s.Items) if (it is YMap m && m.Entries.Count == 1 && m.Entries[0].Key == name) { m.Entries[0] = new(name, v); return; }
            // keep sorted by name like Unity
            int idx = s.Items.FindIndex(i => i is YMap m && string.CompareOrdinal(m.Entries[0].Key, name) > 0);
            var e = new YMap().Add(name, v);
            if (idx < 0) s.Items.Add(e); else s.Items.Insert(idx, e);
        }
        public bool HasProperty(string name) => FindProp("m_Floats", name) != null || FindProp("m_Colors", name) != null || FindProp("m_TexEnvs", name) != null || FindProp("m_Ints", name) != null;
        public bool HasProperty(int id) => HasProperty(N(id) ?? "");
        public bool HasFloat(string n) => FindProp("m_Floats", n) != null;
        public bool HasFloat(int id) => HasFloat(N(id) ?? "");
        public bool HasColor(int id) => FindProp("m_Colors", N(id) ?? "") != null;
        public bool HasTexture(int id) => FindProp("m_TexEnvs", N(id) ?? "") != null;
        public bool HasInt(int id) => FindProp("m_Ints", N(id) ?? "") != null;
        public float GetFloat(string name) => FindProp("m_Floats", name) is YScalar s ? s.F : 0;
        public float GetFloat(int id) => GetFloat(N(id) ?? "");
        public void SetFloat(string name, float v) => SetProp("m_Floats", name, YScalar.Of(v));
        public void SetFloat(int id, float v) => SetFloat(N(id), v);
        public int GetInt(string name) => (int)GetFloat(name);
        public int GetInt(int id) => (int)GetFloat(id);
        public void SetInt(string name, int v) => SetFloat(name, v);
        public void SetInt(int id, int v) => SetFloat(id, v);
        public int GetInteger(int id) => FindProp("m_Ints", N(id) ?? "") is YScalar s ? s.I : 0;
        public void SetInteger(int id, int v) => SetProp("m_Ints", N(id), YScalar.Of(v));
        public Color GetColor(string name) => FindProp("m_Colors", name) is YMap m ? Serializer.ReadColor(m) : default;
        public Color GetColor(int id) => GetColor(N(id) ?? "");
        public void SetColor(string name, Color c) => SetProp("m_Colors", name, Serializer.Col(c));
        public void SetColor(int id, Color c) => SetColor(N(id), c);
        public Vector4 GetVector(string name) { var c = GetColor(name); return new Vector4(c.r, c.g, c.b, c.a); }
        public Vector4 GetVector(int id) => GetVector(N(id) ?? "");
        public void SetVector(string name, Vector4 v) => SetColor(name, new Color(v.x, v.y, v.z, v.w));
        public void SetVector(int id, Vector4 v) => SetVector(N(id), v);
        public Texture GetTexture(string name)
        {
            if (FindProp("m_TexEnvs", name) is YMap m && m["m_Texture"] is YRef r) return (r.Raw ? AssetLoader.ResolveExternal(r) : r.Target) as Texture;
            return null;
        }
        public Texture GetTexture(int id) => GetTexture(N(id) ?? "");
        public void SetTexture(string name, Texture t)
        {
            var cur = FindProp("m_TexEnvs", name) as YMap;
            var m = new YMap().Add("m_Texture", new YRef(t)).Add("m_Scale", cur?["m_Scale"] ?? Serializer.Vec(Vector2.one)).Add("m_Offset", cur?["m_Offset"] ?? Serializer.Vec(Vector2.zero));
            SetProp("m_TexEnvs", name, m);
        }
        public void SetTexture(int id, Texture t) => SetTexture(N(id), t);
        public Vector2 GetTextureScale(string name) => FindProp("m_TexEnvs", name ?? "") is YMap m ? Serializer.ReadV2(m["m_Scale"]) : Vector2.one;
        public Vector2 GetTextureScale(int id) => GetTextureScale(N(id));
        public Vector2 GetTextureOffset(string name) => FindProp("m_TexEnvs", name ?? "") is YMap m ? Serializer.ReadV2(m["m_Offset"]) : Vector2.zero;
        public Vector2 GetTextureOffset(int id) => GetTextureOffset(N(id));
        public void SetTextureScale(string name, Vector2 v) => SetTexEnv(name, "m_Scale", v);
        public void SetTextureScale(int id, Vector2 v) => SetTextureScale(N(id), v);
        public void SetTextureOffset(string name, Vector2 v) => SetTexEnv(name, "m_Offset", v);
        public void SetTextureOffset(int id, Vector2 v) => SetTextureOffset(N(id), v);
        public Vector2 mainTextureScale { get => GetTextureScale("_MainTex"); set => SetTextureScale("_MainTex", value); }
        public Vector2 mainTextureOffset { get => GetTextureOffset("_MainTex"); set => SetTextureOffset("_MainTex", value); }
        void SetTexEnv(string name, string key, Vector2 v)
        {
            if (string.IsNullOrEmpty(name)) return;
            var cur = FindProp("m_TexEnvs", name) as YMap;
            var m = new YMap().Add("m_Texture", cur?["m_Texture"] ?? new YRef((Object)null)).Add("m_Scale", key == "m_Scale" ? Serializer.Vec(v) : cur?["m_Scale"] ?? Serializer.Vec(Vector2.one))
                .Add("m_Offset", key == "m_Offset" ? Serializer.Vec(v) : cur?["m_Offset"] ?? Serializer.Vec(Vector2.zero));
            SetProp("m_TexEnvs", name, m);
        }
        public Texture mainTexture { get => GetTexture("_MainTex"); set => SetTexture("_MainTex", value); }
        public Color color { get => GetColor("_Color"); set => SetColor("_Color", value); }
        public string[] shaderKeywords
        {
            get => NGet("m_ValidKeywords") is YSeq s ? s.Items.OfType<YScalar>().Select(x => x.Value).Concat(NGet("m_InvalidKeywords") is YSeq s2 ? s2.Items.OfType<YScalar>().Select(x => x.Value) : Array.Empty<string>()).ToArray() : Array.Empty<string>();
            set { var s = new YSeq(); foreach (var k in (value ?? Array.Empty<string>()).Distinct().OrderBy(x => x, StringComparer.Ordinal)) s.Items.Add(new YScalar(k)); NSet("m_ValidKeywords", s); NSet("m_InvalidKeywords", new YSeq()); }
        }
        public bool IsKeywordEnabled(string k) => shaderKeywords.Contains(k);
        public void EnableKeyword(string k) { if (!IsKeywordEnabled(k)) shaderKeywords = shaderKeywords.Append(k).ToArray(); }
        public void DisableKeyword(string k) { if (IsKeywordEnabled(k)) shaderKeywords = shaderKeywords.Where(x => x != k).ToArray(); }
        public void CopyPropertiesFromMaterial(Material mat) { if (mat?.NGet("m_SavedProperties") is YMap sp) NSet("m_SavedProperties", sp.Clone()); }
        /// <summary>CRC of the serialized material state (TMP uses it as a material identity key).</summary>
        public int ComputeCRC()
        {
            var w = new MiniUnity.Yaml.YamlWriter { RefResolver = o => o.m_Asset != null ? (o.m_Asset.FileID, o.m_Asset.Guid, o.m_Asset.RefType) : ((long)o.m_InstanceID, null, 0) };
            w.WriteDoc(21, 0, "Material", m_Native ?? new YMap());
            uint h = 2166136261; foreach (char c in w.ToString()) { h ^= c; h *= 16777619; }
            return (int)h;
        }
        public string GetTag(string tag, bool searchFallbacks) => "";
        public string GetTag(string tag, bool searchFallbacks, string defaultValue) => defaultValue;
        public bool SetPass(int pass) => true;
        public void SetOverrideTag(string tag, string val) { }
        public int FindPass(string passName) => -1;
        public string[] GetTexturePropertyNames() => Section("m_TexEnvs").Items.OfType<YMap>().Select(m => m.Entries[0].Key).ToArray();
        public int[] GetTexturePropertyNameIDs() => GetTexturePropertyNames().Select(Shader.PropertyToID).ToArray();
    }

    public partial class Texture
    {
        internal int m_W, m_H;
        public virtual int width { get => m_W > 0 ? m_W : NInt("m_Width"); set => m_W = value; }
        public virtual int height { get => m_H > 0 ? m_H : NInt("m_Height"); set => m_H = value; }
        public FilterMode filterMode { get; set; } = FilterMode.Bilinear;
        public TextureWrapMode wrapMode { get; set; }
        public int anisoLevel { get; set; } = 1;
        public float mipMapBias { get; set; }
        public int mipmapCount => 1;
        public Vector2 texelSize => new Vector2(1f / Math.Max(1, width), 1f / Math.Max(1, height));
        public virtual bool isReadable => true;
        public uint updateCount => 0;
        public void IncrementUpdateCount() { }
        public virtual UnityEngine.Rendering.TextureDimension dimension { get => UnityEngine.Rendering.TextureDimension.Tex2D; set { } }
    }

    public partial class Texture2D
    {
        internal Color[] m_Pixels;
        internal TextureFormat m_Format = TextureFormat.RGBA32;
        internal string m_SourceFile;      // imported image (decoded on first pixel access)
        internal bool m_Linear;            // false = sRGB (sampled through the sRGB curve)
        internal int m_Version;
        internal Texture2D() { }
        public Texture2D(int width, int height) : this(width, height, TextureFormat.RGBA32, true) { }
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { m_W = width; m_H = height; m_Format = textureFormat; m_Pixels = new Color[width * height]; }
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain, bool linear) : this(width, height, textureFormat, mipChain) { m_Linear = linear; }
        public Texture2D(int width, int height, TextureFormat textureFormat, int mipCount, bool linear) : this(width, height, textureFormat, mipCount > 1) { m_Linear = linear; }
        public override bool isReadable => true;
        public TextureFormat format => m_Format != 0 ? m_Format : (TextureFormat)NInt("m_TextureFormat", 4);
        /// <summary>Decode the source (image file / YAML image data) into m_Pixels the first time pixels are read or written.</summary>
        internal void EnsurePixels()
        {
            if (m_Pixels != null) return;
            var b = MiniUnity.TextureData.RGBA8(this, out int w, out int h);
            if (b == null) { m_Pixels = new Color[Math.Max(0, width * height)]; return; }
            m_W = w; m_H = h;
            m_Pixels = new Color[w * h];
            for (int i = 0; i < m_Pixels.Length; i++) m_Pixels[i] = new Color32(b[i * 4], b[i * 4 + 1], b[i * 4 + 2], b[i * 4 + 3]);
        }
        static Color Fmt(TextureFormat f, Color c) => f switch
        {
            TextureFormat.RGB24 => new Color(c.r, c.g, c.b, 1),
            TextureFormat.Alpha8 => new Color(1, 1, 1, c.a),
            _ => c,
        };
        public void SetPixels(Color[] colors) { m_Pixels = (Color[])colors.Clone(); m_Version++; }
        public void SetPixels(Color[] colors, int miplevel) => SetPixels(colors);
        public void SetPixels(int x, int y, int blockWidth, int blockHeight, Color[] colors) { EnsurePixels(); for (int j = 0; j < blockHeight; j++) for (int i = 0; i < blockWidth; i++) m_Pixels[(y + j) * width + x + i] = colors[j * blockWidth + i]; m_Version++; }
        public void SetPixels(int x, int y, int blockWidth, int blockHeight, Color[] colors, int miplevel) => SetPixels(x, y, blockWidth, blockHeight, colors);
        public void SetPixel(int x, int y, Color c) { EnsurePixels(); if (x < 0 || y < 0 || x >= width || y >= height) return; m_Pixels[y * width + x] = c; m_Version++; }
        public void SetPixel(int x, int y, Color c, int mipLevel) => SetPixel(x, y, c);
        public Color GetPixel(int x, int y) { EnsurePixels(); x = Mathf.Clamp(x, 0, width - 1); y = Mathf.Clamp(y, 0, height - 1); return m_Pixels.Length > 0 ? Fmt(format, m_Pixels[y * width + x]) : default; }
        public Color GetPixel(int x, int y, int mipLevel) => GetPixel(x, y);
        public Color GetPixelBilinear(float u, float v)
        {
            EnsurePixels();
            if (m_Pixels.Length == 0) return default;
            float x = u * width - 0.5f, y = v * height - 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            Color P(int i, int j) => m_Pixels[Mathf.Clamp(j, 0, height - 1) * width + Mathf.Clamp(i, 0, width - 1)];
            return Fmt(format, Color.Lerp(Color.Lerp(P(x0, y0), P(x0 + 1, y0), fx), Color.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), fx), fy));
        }
        public Color GetPixelBilinear(float u, float v, int mipLevel) => GetPixelBilinear(u, v);
        public Color[] GetPixels() { EnsurePixels(); return m_Pixels; }
        public Color[] GetPixels(int miplevel) => GetPixels();
        public Color[] GetPixels(int x, int y, int blockWidth, int blockHeight) { EnsurePixels(); var r = new Color[blockWidth * blockHeight]; for (int j = 0; j < blockHeight; j++) for (int i = 0; i < blockWidth; i++) r[j * blockWidth + i] = m_Pixels[(y + j) * width + x + i]; return r; }
        public Color[] GetPixels(int x, int y, int blockWidth, int blockHeight, int miplevel) => GetPixels(x, y, blockWidth, blockHeight);
        public Color32[] GetPixels32() => GetPixels().Select(c => (Color32)c).ToArray();
        public Color32[] GetPixels32(int miplevel) => GetPixels32();
        public void SetPixels32(Color32[] colors) { m_Pixels = colors.Select(c => (Color)c).ToArray(); m_Version++; }
        public void SetPixels32(Color32[] colors, int miplevel) => SetPixels32(colors);
        public void Apply() { m_Version++; }
        public void Apply(bool updateMipmaps) => Apply();
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) => Apply();
        public bool Reinitialize(int w, int h) { m_W = w; m_H = h; m_Pixels = new Color[w * h]; m_SourceFile = null; m_Version++; return true; }
        public bool Reinitialize(int w, int h, TextureFormat f, bool hasMipMap) { m_Format = f; return Reinitialize(w, h); }
        public bool Resize(int w, int h) => Reinitialize(w, h);
        public bool Resize(int w, int h, TextureFormat f, bool hasMipMap) => Reinitialize(w, h, f, hasMipMap);
        public static Texture2D whiteTexture => BuiltinResources.WhiteTexture;
        public static Texture2D blackTexture => BuiltinResources.BlackTexture;
        /// <summary>Copy from RenderTexture.active (sRGB-encoded like an ARGB32 sRGB target) into this texture.</summary>
        public void ReadPixels(Rect source, int destX, int destY)
        {
            EnsurePixels();
            var rt = RenderTexture.active;
            if (rt == null) { Debug.LogError("ReadPixels: no active RenderTexture (MiniUnity has no screen buffer)"); return; }
            rt.ReadInto(this, (int)source.x, (int)source.y, (int)source.width, (int)source.height, destX, destY);
            m_Version++;
        }
        public void ReadPixels(Rect source, int destX, int destY, bool recalculateMipMaps) => ReadPixels(source, destX, destY);
    }

    public partial class Sprite
    {
        internal Texture2D m_Texture;
        internal Rect m_Rect;
        internal Vector2 m_Pivot;
        internal Vector4 m_Border;
        internal float m_PPU = 100;
        public Texture2D texture => m_Texture;
        public Rect rect => m_Rect;
        public Rect textureRect => m_Rect;
        public Vector2 textureRectOffset => Vector2.zero;
        public Vector2 pivot => m_Pivot;
        public Vector4 border => m_Border;
        public float pixelsPerUnit => m_PPU;
        public bool packed => false;
        public SpritePackingMode packingMode => SpritePackingMode.Rectangle;
        public SpritePackingRotation packingRotation => SpritePackingRotation.None;
        public Texture2D associatedAlphaSplitTexture => null;
        public Bounds bounds => new Bounds(new Vector3((m_Rect.width * 0.5f - m_Pivot.x) / m_PPU, (m_Rect.height * 0.5f - m_Pivot.y) / m_PPU, 0), new Vector3(m_Rect.width / m_PPU, m_Rect.height / m_PPU, 0.2f));
        public Vector2[] vertices => new[] { new Vector2(0, 0), new Vector2(0, m_Rect.height / m_PPU), new Vector2(m_Rect.width / m_PPU, m_Rect.height / m_PPU), new Vector2(m_Rect.width / m_PPU, 0) };
        public ushort[] triangles => new ushort[] { 0, 1, 2, 2, 3, 0 };
        public Vector2[] uv => new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot) => Create(texture, rect, pivot, 100f);
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit) => new Sprite { m_Texture = texture, m_Rect = rect, m_Pivot = new Vector2(pivot.x * rect.width, pivot.y * rect.height), m_PPU = pixelsPerUnit };
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude, SpriteMeshType meshType, Vector4 border) => new Sprite { m_Texture = texture, m_Rect = rect, m_Pivot = new Vector2(pivot.x * rect.width, pivot.y * rect.height), m_PPU = pixelsPerUnit, m_Border = border };
    }

    public partial class Font
    {
        public Font() { }
        public Font(string name) { m_Name = name; }
        public Material material { get; set; }
        public bool dynamic => true;
        public int fontSize => 16;
        public string[] fontNames { get; set; } = Array.Empty<string>();
        public bool HasCharacter(char c) => true;
    }

    public partial class TextAsset
    {
        internal string m_Text = ""; internal byte[] m_Bytes;
        public TextAsset() { }
        public TextAsset(string text) { m_Text = text; }
        public string text => m_Text;
        public byte[] bytes => m_Bytes ?? System.Text.Encoding.UTF8.GetBytes(m_Text);
        public long dataSize => bytes.LongLength;
        public override string ToString() => m_Text;
    }
}

namespace MiniUnity
{
    public static partial class BuiltinResources
    {
        static UnityEngine.Material s_UI; static UnityEngine.Texture2D s_White;
        public static UnityEngine.Material DefaultUIMaterial
        {
            get
            {
                if (s_UI == null)
                {
                    s_UI = new UnityEngine.Material(ShaderIndex.Find("UI/Default")) { name = "Default UI Material" };
                    s_UI.m_Asset = new AssetIdentity { Guid = "0000000000000000f000000000000000", FileID = 10754, RefType = 0, Path = "Resources/unity_builtin_extra" };
                }
                return s_UI;
            }
        }
        public static UnityEngine.Texture2D WhiteTexture => s_White ??= Solid("UnityWhite", UnityEngine.Color.white);
        static UnityEngine.Texture2D s_Black;
        public static UnityEngine.Texture2D BlackTexture => s_Black ??= Solid("UnityBlack", new UnityEngine.Color(0, 0, 0, 1));
        static UnityEngine.Texture2D Solid(string name, UnityEngine.Color c)
        {
            var t = new UnityEngine.Texture2D(4, 4) { name = name };
            for (int i = 0; i < 16; i++) t.m_Pixels[i] = c;
            return t;
        }
    }

    public static class ShaderIndex
    {
        static Dictionary<string, UnityEngine.Shader> byName;
        static readonly Dictionary<string, (long fid, string guid)> builtins = new()
        {
            ["UI/Default"] = (10770, "0000000000000000f000000000000000"),
            ["Sprites/Default"] = (10753, "0000000000000000f000000000000000"),
            ["Standard"] = (46, "0000000000000000f000000000000000"),
            ["Unlit/Color"] = (10755, "0000000000000000f000000000000000"),
            ["Unlit/Texture"] = (10752, "0000000000000000f000000000000000"),
            ["Unlit/Transparent"] = (10750, "0000000000000000f000000000000000"),
            ["Unlit/Transparent Cutout"] = (10751, "0000000000000000f000000000000000"),
            ["Particles/Standard Unlit"] = (211, "0000000000000000f000000000000000"),
            ["Hidden/InternalErrorShader"] = (17, "0000000000000000e000000000000000"),
        };
        /// <summary>Name of a built-in shader from its file id in Unity's built-in resources (null if unknown).</summary>
        internal static string BuiltinName(string guid, long fileID)
        {
            foreach (var kv in builtins) if (kv.Value.fid == fileID && kv.Value.guid == guid) return kv.Key;
            return null;
        }

        public static UnityEngine.Shader Find(string name)
        {
            if (byName == null)
            {
                byName = new();
                foreach (var path in AssetIndex.AllPaths().Where(p => p.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var first = File.ReadLines(AssetIndex.Abs(path)).FirstOrDefault(l => l.TrimStart().StartsWith("Shader "));
                        if (first == null) continue;
                        int a = first.IndexOf('"'), b = first.IndexOf('"', a + 1);
                        var sname = first.Substring(a + 1, b - a - 1);
                        var sh = AssetLoader.LoadMain(path) as UnityEngine.Shader;
                        if (sh != null) { sh.m_ShaderName = sname; byName[sname] = sh; }
                    }
                    catch { }
                }
            }
            if (byName.TryGetValue(name, out var s)) return s;
            if (builtins.TryGetValue(name, out var bi))
            {
                s = new UnityEngine.Shader { m_ShaderName = name, m_Name = name };
                s.m_Asset = new AssetIdentity { Guid = bi.guid, FileID = bi.fid, RefType = 0, Path = "builtin" };
                byName[name] = s;
                return s;
            }
            return null;
        }
    }
}
