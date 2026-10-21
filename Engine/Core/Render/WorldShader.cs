// Shaders written in C# for the software renderer's 3D pass. WorldRenderer.Shaders maps a shader name (as in the .shader file or
// Unity's built-in name) to a WorldShader; a host registers its project's shaders there. Built in: Unlit/Color, Unlit/Texture,
// Unlit/Transparent, Unlit/Transparent Cutout, Sprites/Default, and Standard with textures (albedo, cutout, normal map), which
// lights through the same BRDF as the untextured Standard path.
using System.Collections.Generic;
using UnityEngine;

namespace MiniUnity
{
    /// <summary>A material as a shader sees it: the renderer's MaterialPropertyBlock (for an instanced draw, its arrays at the
    /// instance's index) over the material's own values.</summary>
    public sealed class ShaderInputs
    {
        public Material material { get; internal set; }
        public MaterialPropertyBlock block { get; internal set; }
        public int instance { get; internal set; }
        /// <summary>Object-to-world matrix of the draw, the camera's view-projection and the target size in pixels (_ScreenParams.xy).</summary>
        public Matrix4x4 objectToWorld { get; internal set; }
        public Matrix4x4 viewProjection { get; internal set; }
        public Vector2 screenSize { get; internal set; }
        /// <summary>Clip-space position of an object-space point (UnityObjectToClipPos).</summary>
        public Vector4 ObjectToClip(Vector3 p) { var w = objectToWorld.MultiplyPoint3x4(p); return viewProjection * new Vector4(w.x, w.y, w.z, 1); }
        /// <summary>Anything the shader wants to keep for this draw (computed once in <see cref="WorldShader.Prepare"/>).</summary>
        public object state;

        /// <summary>Colour in the shader's space (linear in a linear-space project): material colours are converted, block values are
        /// already shader values.</summary>
        public Color ColorLinear(string name, Color fallback)
        {
            if (block != null && block.TryGetShaderColor(name, out var bc)) return bc;
            if (block != null && block.TryGetInstanceVector(name, instance, out var iv)) return new Color(iv.x, iv.y, iv.z, iv.w);
            if (material != null && material.HasProperty(name))
            {
                var c = material.GetColor(name);
                return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
            }
            return fallback;
        }
        public float Float(string name, float fallback)
        {
            if (block != null && block.TryGetFloat(name, out var f)) return f;
            if (block != null && block.TryGetInstanceFloat(name, instance, out var fi)) return fi;
            return material != null && material.HasProperty(name) ? material.GetFloat(name) : fallback;
        }
        public Texture Texture(string name)
        {
            var t = block?.GetTexture(name);
            if (t != null) return t;
            return material != null && material.HasProperty(name) ? material.GetTexture(name) : null;
        }
        /// <summary>Sampler of a texture property, null when none is assigned.</summary>
        public TexSampler Sampler(string name) { var t = Texture(name); return t != null ? TextureData.Sampler(t) : null; }
        /// <summary>Tiling (xy) and offset (zw) of a texture property.</summary>
        public Vector4 ST(string name)
        {
            if (material == null || !material.HasProperty(name)) return new Vector4(1, 1, 0, 0);
            var s = material.GetTextureScale(name); var o = material.GetTextureOffset(name);
            return new Vector4(s.x, s.y, o.x, o.y);
        }
        public bool Keyword(string k) => material != null && material.IsKeywordEnabled(k);
    }

    /// <summary>Inputs of one fragment, interpolated perspective-correctly.</summary>
    public struct FragmentInput
    {
        public Vector3 worldPos, normal, objectPos;
        public Vector4 tangent;   // world-space xyz, w = bitangent sign (zero when the mesh has no tangents)
        public Vector2 uv;
        public Color vertexColor;
        public Vector3 viewDir;   // from the surface to the camera, normalized
        public float viewDepth;
        /// <summary>Lighting of the scene (Standard BRDF, ambient, shadows, fog) for shaders that are lit.</summary>
        public Lighting lighting;
    }

    /// <summary>The Standard shader's lighting, for C# shaders that want it.</summary>
    public sealed class Lighting
    {
        internal System.Func<Color, float, float, Color, bool, Vector3, Vector3, float, Color> standard;
        /// <summary>Standard (BRDF1_Unity_PBS) lighting for a linear albedo; emission is added; shadows when receive is set.</summary>
        public Color Standard(Color albedoLinear, float smoothness, float metallic, Color emissionLinear, bool receiveShadows, in FragmentInput f)
            => standard(albedoLinear, smoothness, metallic, emissionLinear, receiveShadows, f.worldPos, f.normal, f.viewDepth);
        internal System.Func<Vector3, Vector3, bool, (bool has, Vector3 dir, Color color, float atten)> mainLight;
        internal System.Func<Vector3, Color> ambient;
        internal System.Func<Color, float, Color> fog;
        /// <summary>The main directional light: direction towards it, linear colour, shadow attenuation at the fragment (1 = lit).</summary>
        public bool MainLight(in FragmentInput f, bool receiveShadows, out Vector3 dir, out Color color, out float shadowAtten)
        {
            var r = mainLight(f.worldPos, f.normal, receiveShadows); dir = r.dir; color = r.color; shadowAtten = r.atten; return r.has;
        }
        /// <summary>Ambient irradiance (ShadeSH9) for a normal.</summary>
        public Color Ambient(Vector3 normal) => ambient(normal);
        /// <summary>Scene fog applied to a lit colour.</summary>
        public Color Fog(Color c, in FragmentInput f) => fog(c, f.viewDepth);
    }

    public abstract class WorldShader
    {
        /// <summary>The material's shader reads _BaseColor (URP and SRP-era custom shaders) rather than _Color: a material moved
        /// between shaders keeps a stale _Color, so only the built-in families below go by _Color.</summary>
        internal static bool UsesBaseColor(Material m)
        {
            if (m == null) return false;
            string n = UIRenderer.ShaderName(m.shader) ?? "";
            if (n.Length == 0) return true;   // shader not in the project (e.g. a URP shader without the package)
            foreach (var p in BuiltinColorShaders) if (n == p || n.StartsWith(p + "/", System.StringComparison.Ordinal) || n.StartsWith(p + " (", System.StringComparison.Ordinal)) return false;
            return true;
        }
        static readonly string[] BuiltinColorShaders = { "Standard", "Legacy Shaders", "Unlit", "Mobile", "Particles", "Sprites", "UI", "Nature", "Skybox", "Autodesk Interactive", "Hidden", "TextMeshPro" };

        public enum RenderQueue { Opaque, AlphaTest, Transparent }
        /// <summary>Where the draw goes: opaque (depth write), alpha test (depth write, fragments may be discarded) or transparent
        /// (alpha blended back to front, no depth write).</summary>
        public virtual RenderQueue Queue(ShaderInputs m) => RenderQueue.Opaque;
        /// <summary>Whether the draw casts into the main light's shadow map (unlit shaders have no shadow caster pass).</summary>
        public virtual bool CastsShadows(ShaderInputs m) => false;
        /// <summary>Cull Back (the default); false draws both faces (Cull Off).</summary>
        public virtual bool CullBack => true;
        /// <summary>Called once per draw (per instance for instanced draws) before its fragments; may fill <see cref="ShaderInputs.state"/>.</summary>
        public virtual void Prepare(ShaderInputs m) { }
        /// <summary>Vertex stage: the object-space position to draw for a mesh vertex (default: unchanged).</summary>
        public virtual Vector3 Vertex(ShaderInputs m, Vector3 positionOS) => positionOS;
        /// <summary>Linear colour (with alpha for blending) of one fragment; false discards it (clip).</summary>
        public abstract bool Fragment(ShaderInputs m, in FragmentInput f, out Color color);
    }

    public static class BuiltinShaders
    {
        sealed class Tex { public TexSampler s; public Vector4 st; public Color tint; public float cutoff; }

        static Color SampleTex(TexSampler s, Vector4 st, Vector2 uv)
        {
            if (s == null) return Color.white;
            s.Sample(uv.x * st.x + st.z, uv.y * st.y + st.w, out float r, out float g, out float b, out float a);
            return new Color(r, g, b, a);
        }

        /// <summary>Unlit/Color: _Color.</summary>
        public sealed class UnlitColor : WorldShader
        {
            public override bool Fragment(ShaderInputs m, in FragmentInput f, out Color c) { c = m.ColorLinear("_Color", Color.white); c.a = 1; return true; }
        }

        /// <summary>Unlit/Texture, Unlit/Transparent, Unlit/Transparent Cutout: _MainTex (with tiling / offset), _Cutoff.</summary>
        public sealed class UnlitTexture : WorldShader
        {
            readonly RenderQueue queue;
            public UnlitTexture(RenderQueue q) { queue = q; }
            public override RenderQueue Queue(ShaderInputs m) => queue;
            public override void Prepare(ShaderInputs m) => m.state = new Tex { s = m.Sampler("_MainTex"), st = m.ST("_MainTex"), cutoff = m.Float("_Cutoff", 0.5f) };
            public override bool Fragment(ShaderInputs m, in FragmentInput f, out Color c)
            {
                var t = (Tex)m.state;
                c = SampleTex(t.s, t.st, f.uv);
                if (queue == RenderQueue.AlphaTest && c.a < t.cutoff) return false;
                if (queue == RenderQueue.Opaque) c.a = 1;
                return true;
            }
        }

        /// <summary>Sprites/Default (world-space quads): _MainTex × _Color × vertex colour, alpha blended.</summary>
        public sealed class SpritesDefault : WorldShader
        {
            public override RenderQueue Queue(ShaderInputs m) => RenderQueue.Transparent;
            public override bool CullBack => false;
            public override void Prepare(ShaderInputs m) => m.state = new Tex { s = m.Sampler("_MainTex"), st = m.ST("_MainTex"), tint = m.ColorLinear("_Color", Color.white) };
            public override bool Fragment(ShaderInputs m, in FragmentInput f, out Color c)
            {
                var t = (Tex)m.state;
                c = SampleTex(t.s, t.st, f.uv) * t.tint * f.vertexColor;
                return true;
            }
        }

        /// <summary>Standard with an albedo texture (_MainTex), Cutout mode (_Mode 1, _Cutoff), normal map (_BumpMap, _BumpScale),
        /// metallic / smoothness values and emission; lit like the untextured Standard path.</summary>
        public sealed class StandardTextured : WorldShader
        {
            sealed class S { public TexSampler albedo, bump; public Vector4 st; public Color color, emission; public float smooth, metal, cutoff, bumpScale; public bool cutout, fade; }
            public override RenderQueue Queue(ShaderInputs m)
            {
                int mode = (int)m.Float("_Mode", 0);
                return mode >= 2 ? RenderQueue.Transparent : mode == 1 ? RenderQueue.AlphaTest : RenderQueue.Opaque;
            }
            public override bool CastsShadows(ShaderInputs m) => true;
            public override void Prepare(ShaderInputs m)
            {
                int mode = (int)m.Float("_Mode", 0);
                // URP-style materials (Lit / Simple Lit and custom shaders drawn as Standard) name albedo, tint and smoothness
                // _BaseMap, _BaseColor and _Smoothness
                bool urp = m.Texture("_MainTex") == null && m.Texture("_BaseMap") != null;
                bool baseColor = urp || (m.material != null && m.material.HasProperty("_BaseColor") && (!m.material.HasProperty("_Color") || UsesBaseColor(m.material)));
                m.state = new S
                {
                    albedo = m.Sampler(urp ? "_BaseMap" : "_MainTex"), bump = m.Sampler("_BumpMap"), st = m.ST(urp ? "_BaseMap" : "_MainTex"),
                    color = baseColor ? m.ColorLinear("_BaseColor", Color.white) : m.ColorLinear("_Color", Color.white),
                    smooth = urp ? m.Float("_Smoothness", .5f) : m.Float("_Glossiness", .5f), metal = m.Float("_Metallic", 0), cutoff = m.Float("_Cutoff", .5f), bumpScale = m.Float("_BumpScale", 1),
                    emission = m.Keyword("_EMISSION") ? m.ColorLinear("_EmissionColor", Color.black) : Color.black, cutout = mode == 1, fade = mode >= 2,
                };
            }
            public override bool Fragment(ShaderInputs m, in FragmentInput f, out Color c)
            {
                var s = (S)m.state;
                var uv = new Vector2(f.uv.x * s.st.x + s.st.z, f.uv.y * s.st.y + s.st.w);
                var albedo = s.color * SampleTex(s.albedo, new Vector4(1, 1, 0, 0), uv);
                if (s.cutout && albedo.a < s.cutoff) { c = default; return false; }
                var fin = f;
                if (s.bump != null && f.tangent.sqrMagnitude > 0)
                {
                    s.bump.Sample(uv.x, uv.y, out float nr, out float ng, out float nb, out float na);
                    // UnpackNormal (DXT5nm-agnostic): xy from the texture, z rebuilt; scaled by _BumpScale
                    float nx = (nr * 2 - 1) * s.bumpScale, ny = (ng * 2 - 1) * s.bumpScale;
                    float nz = Mathf.Sqrt(Mathf.Max(0, 1 - Mathf.Clamp01(nx * nx + ny * ny)));
                    var T = new Vector3(f.tangent.x, f.tangent.y, f.tangent.z); var N = f.normal;
                    T = (T - N * Vector3.Dot(N, T)).normalized;
                    var B = Vector3.Cross(N, T) * (f.tangent.w < 0 ? -1 : 1);
                    fin.normal = (T * nx + B * ny + N * nz).normalized;
                }
                c = f.lighting.Standard(albedo, s.smooth, s.metal, s.emission, !s.fade, fin);
                c.a = s.fade ? albedo.a : 1;
                return true;
            }
        }

        /// <summary>Toony Colors Pro 2 (URP / Hybrid shaders it generates): ramp between _SColor and _HColor from N·L with
        /// _RampThreshold / _RampSmoothing (and _BandsCount bands when set), × _BaseColor (× _BaseMap) × light, plus ambient.
        /// Rim, outlines, matcap and the stylised specular are not drawn.</summary>
        public sealed class ToonyColors : WorldShader
        {
            sealed class S { public TexSampler map; public Vector4 st; public Color baseColor, h, sh; public float threshold, smoothing, bands, cutoff; public bool cutout; }
            public override bool CastsShadows(ShaderInputs m) => true;
            public override RenderQueue Queue(ShaderInputs m) => m.Float("_RenderingMode", 0) >= 2 ? RenderQueue.Transparent : m.Keyword("_ALPHATEST_ON") ? RenderQueue.AlphaTest : RenderQueue.Opaque;
            public override void Prepare(ShaderInputs m)
            {
                bool baseMap = m.material != null && m.material.HasProperty("_BaseMap");
                m.state = new S
                {
                    map = m.Sampler(baseMap ? "_BaseMap" : "_MainTex"), st = m.ST(baseMap ? "_BaseMap" : "_MainTex"),
                    baseColor = m.material != null && m.material.HasProperty("_BaseColor") ? m.ColorLinear("_BaseColor", Color.white) : m.ColorLinear("_Color", Color.white),
                    h = m.ColorLinear("_HColor", new Color(.75f, .75f, .75f)), sh = m.ColorLinear("_SColor", new Color(.2f, .2f, .2f)),
                    threshold = m.Float("_RampThreshold", .5f), smoothing = Mathf.Max(1e-3f, m.Float("_RampSmoothing", .5f)),
                    bands = m.material != null && m.material.HasProperty("_BandsCount") ? Mathf.Max(1, m.Float("_BandsCount", 4)) : 0,
                    cutoff = m.Float("_Cutoff", .5f), cutout = m.Keyword("_ALPHATEST_ON"),
                };
            }
            public override bool Fragment(ShaderInputs m, in FragmentInput f, out Color c)
            {
                var s = (S)m.state;
                var albedo = s.baseColor * SampleTex(s.map, s.st, f.uv);
                if (s.cutout && albedo.a < s.cutoff) { c = default; return false; }
                c = albedo * f.lighting.Ambient(f.normal);
                if (f.lighting.MainLight(f, true, out var L, out var lc, out float atten))
                {
                    float ndl = Mathf.Max(0, Vector3.Dot(f.normal, L));
                    float ramp = Mathf.SmoothStep(0, 1, Mathf.Clamp01((ndl - (s.threshold - s.smoothing * .5f)) / s.smoothing));
                    if (s.bands > 1) ramp = Mathf.Round(ramp * s.bands) / s.bands;
                    ramp *= atten;
                    var tint = Color.Lerp(s.sh, s.h, ramp);
                    c += new Color(albedo.r * tint.r * lc.r, albedo.g * tint.g * lc.g, albedo.b * tint.b * lc.b);
                }
                c = f.lighting.Fog(c, f);
                c.a = albedo.a;
                return true;
            }
        }

        internal static void Register(Dictionary<string, WorldShader> d, Dictionary<string, WorldShader> prefixes)
        {
            prefixes["Toony Colors Pro 2/"] = new ToonyColors();
            d["Unlit/Color"] = new UnlitColor();
            d["Unlit/Texture"] = new UnlitTexture(WorldShader.RenderQueue.Opaque);
            d["Unlit/Transparent"] = new UnlitTexture(WorldShader.RenderQueue.Transparent);
            d["Unlit/Transparent Cutout"] = new UnlitTexture(WorldShader.RenderQueue.AlphaTest);
            d["Sprites/Default"] = new SpritesDefault();
        }
    }
}
