// 3D part of Camera.Render: MeshRenderers drawn with a depth buffer before the canvases of the same camera.
// Shading follows the built-in forward path for the materials the builders make: Standard (BRDF1_Unity_PBS, GGX +
// Disney diffuse + Schlick Fresnel), ambient Trilight, a main directional light with cascaded shadows (stable fit, 5×5 tent PCF,
// settings from ProjectSettings/QualitySettings.asset), additional directional / point / spot pixel lights (no shadows),
// emission, linear fog, Fade transparency (LDR target: colour clamped before blending, no received shadows, dithered
// shadow casting), and "rounded box" shaders registered in RoundedBoxShaders (vertex rounding + rim emission; a project's
// own shader, e.g. VideoRecreate/RoundedBox, registered by its host).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniUnity
{
    public static class WorldRenderer
    {
        /// <summary>Shader names drawn as rounded, rim-lit boxes (_Radius, _RimColor, _RimPower, _RimStrength, emission on); a host
        /// registers its project's shader here.</summary>
        public static readonly HashSet<string> RoundedBoxShaders = new(StringComparer.Ordinal);

        public static int Triangles;

        /// <summary>Shaders written in C# by name (see WorldShader); everything else draws as Standard.</summary>
        public static readonly Dictionary<string, WorldShader> Shaders = new(StringComparer.Ordinal);
        static WorldRenderer() => BuiltinShaders.Register(Shaders);
        static readonly WorldShader StandardTextured = new BuiltinShaders.StandardTextured();

        sealed class Draw
        {
            public Mesh mesh; public Material mat; public int sub; public Matrix4x4 M; public bool transparent, rounded, receive; public float dist, alpha = 1;
            public Quaternion rot; public Bounds bounds; public ShadowCastingMode cast; public MaterialPropertyBlock block;
            public WorldShader shader; public ShaderInputs inputs; public WorldShader.RenderQueue queue;   // C# shader draws
        }

        // ------------------------------------------------------------------ draws submitted from script (Graphics.RenderMesh*, DrawMesh*)
        internal struct Queued { public Mesh mesh; public int sub; public Material mat; public MaterialPropertyBlock block; public Matrix4x4 M; public int instance, layer; public Camera camera; public ShadowCastingMode cast; public bool receive; }
        static readonly List<Queued> queued = new();
        /// <summary>Unity draws what scripts submit during a frame with that frame's cameras, then drops it.</summary>
        internal static void Submit(in Queued q) => queued.Add(q);
        internal static void ClearSubmitted() => queued.Clear();

        sealed class Mat
        {
            public Color albedo; public float smooth, metal; public Color emission; public bool fade, rounded, receive = true;
            public float radius; public Color rim; public float rimPower, rimStrength;
            public static Mat Of(Draw d) => Of(d.mat, d.block);
            public static Mat Of(Material m, MaterialPropertyBlock block = null)
            {
                var x = new Mat();
                // a renderer's MaterialPropertyBlock overrides the material's values
                float F(string n, float d) => block != null && block.TryGetFloat(n, out var bf) ? bf : m != null && m.HasProperty(n) ? m.GetFloat(n) : d;
                // material colours are sRGB (linearized below); a block holds shader values, converted back so both take one path
                Color C(string n, Color d) => block != null && block.TryGetShaderColor(n, out var bc) ? bc.gamma : m != null && m.HasProperty(n) ? m.GetColor(n) : d;
                // URP / custom shaders name the base colour _BaseColor
                bool hasColor = (block != null && block.HasVector("_Color")) || (m != null && m.HasProperty("_Color"));
                bool hasBase = (block != null && block.HasVector("_BaseColor")) || (m != null && m.HasProperty("_BaseColor"));
                var c = !hasColor && hasBase ? C("_BaseColor", Color.white) : C("_Color", Color.white);
                x.albedo = new Color(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), c.a);
                x.smooth = F("_Glossiness", .5f); x.metal = F("_Metallic", 0);
                bool rounded = m != null && RoundedBoxShaders.Contains(UIRenderer.ShaderName(m.shader));
                bool emissive = m != null && (m.IsKeywordEnabled("_EMISSION") || rounded);
                x.emission = emissive ? C("_EmissionColor", Color.black).linear : Color.black;
                x.fade = m != null && (int)F("_Mode", 0) >= 2;
                x.rounded = rounded;
                x.radius = F("_Radius", .15f); x.rim = C("_RimColor", Color.white).linear; x.rimPower = F("_RimPower", 3); x.rimStrength = F("_RimStrength", .12f);
                return x;
            }
        }

        // ------------------------------------------------------------------ entry
        internal static void Render(Camera cam, UIRenderer.View view)
        {
            var draws = Collect(cam, view);
            if (draws.Count == 0) return;
            var lights = ObjectRegistry.SceneObjects(typeof(Light), false).Cast<Light>()
                .Where(l => l.enabled && l.gameObject.activeInHierarchy && l.intensity > 0 && (cam.cullingMask & l.cullingMask) != 0).ToList();
            // main light = brightest directional (Unity: RenderSettings.sun or the brightest one)
            var sun = lights.Where(l => l.type == LightType.Directional).OrderByDescending(l => l.intensity * l.color.grayscale).FirstOrDefault();
            var shadow = sun != null && sun.shadows != LightShadows.None ? ShadowMap.Build(sun, draws, cam, view.camPos) : null;
            var env = new Env(sun, shadow, lights.Where(l => l != sun && l.type != LightType.Area));
            env.Prepare();
            // opaque: depth-only pass that keeps the visible triangle of every pixel (same depth test and order as drawing them
            // one by one, so the same triangle wins), then each visible pixel is shaded once, rows in parallel
            BeginVisibility(view);
            foreach (var d in draws.Where(d => !d.transparent && d.shader == null)) DrawOne(d, view, env, true);
            ShadeVisible(view, env);
            // C# shaders: opaque, then alpha test (queue 2450), then everything transparent back to front with the Standard ones
            foreach (var d in draws.Where(d => d.shader != null && d.queue == WorldShader.RenderQueue.Opaque)) DrawCustom(d, view, env);
            foreach (var d in draws.Where(d => d.shader != null && d.queue == WorldShader.RenderQueue.AlphaTest)) DrawCustom(d, view, env);
            foreach (var d in draws.Where(d => d.transparent).OrderByDescending(d => d.dist))
                if (d.shader != null) DrawCustom(d, view, env); else DrawOne(d, view, env, false);
            shadow?.Release();
        }

        static List<Draw> Collect(Camera cam, UIRenderer.View view)
        {
            var list = new List<Draw>();
            foreach (MeshRenderer r in ObjectRegistry.SceneObjects(typeof(MeshRenderer), false))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || (cam.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                mesh.EnsureData();
                var mats = r.sharedMaterials;
                var M = r.transform.localToWorldMatrix;
                float dist = (M.MultiplyPoint3x4(Vector3.zero) - view.camPos).sqrMagnitude;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var m = s < mats.Length ? mats[s] : mats.LastOrDefault();
                    if (m == null) continue;
                    var d = MakeDraw(mesh, s, m, r.PropertyBlockFor(s), -1, M, dist, r.receiveShadows);
                    d.rot = r.transform.rotation; d.bounds = r.bounds; d.cast = r.shadowCastingMode;
                    list.Add(d);
                }
            }
            foreach (var q in queued)
            {
                if (q.mesh == null || q.mat == null || (q.camera != null && q.camera != cam) || (cam.cullingMask & (1 << q.layer)) == 0) continue;
                q.mesh.EnsureData();
                if (q.sub < 0 || q.sub >= q.mesh.subMeshCount) continue;
                float dist = (q.M.MultiplyPoint3x4(Vector3.zero) - view.camPos).sqrMagnitude;
                var d = MakeDraw(q.mesh, q.sub, q.mat, q.block, q.instance, q.M, dist, q.receive);
                d.rot = q.M.rotation; d.bounds = WorldBounds(q.mesh.bounds, q.M); d.cast = q.cast;
                list.Add(d);
            }
            return list;
        }

        static Draw MakeDraw(Mesh mesh, int s, Material m, MaterialPropertyBlock block, int instance, Matrix4x4 M, float dist, bool receive)
        {
            string sname = UIRenderer.ShaderName(m.shader);
            Shaders.TryGetValue(sname, out var shader);
            // Standard with a texture, Cutout mode or a normal map goes through the C# Standard (same lighting); plain Standard keeps
            // the visibility-buffer path
            if (shader == null && !RoundedBoxShaders.Contains(sname) && (m.GetTexture("_MainTex") != null || m.GetTexture("_BumpMap") != null || (m.HasProperty("_Mode") && (int)m.GetFloat("_Mode") == 1)))
                shader = StandardTextured;
            if (shader != null)
            {
                var inputs = new ShaderInputs { material = m, block = block, instance = Math.Max(0, instance) };
                var queue = shader.Queue(inputs);
                bool trq = queue == WorldShader.RenderQueue.Transparent;
                return new Draw { mesh = mesh, mat = m, sub = s, M = M, transparent = trq, dist = dist, block = block, shader = shader, inputs = inputs, queue = queue,
                    alpha = trq ? inputs.ColorLinear("_Color", Color.white).a : 1, receive = receive && !trq };
            }
            bool tr = m.renderQueue >= 2500 || (m.HasProperty("_Mode") && m.GetFloat("_Mode") >= 2);
            float alpha = tr && block != null && block.TryGetShaderColor("_Color", out var bc) ? bc.a : tr && m.HasProperty("_Color") ? m.GetColor("_Color").a : 1;
            return new Draw { mesh = mesh, mat = m, sub = s, M = M, transparent = tr, rounded = RoundedBoxShaders.Contains(sname), dist = dist, alpha = alpha, receive = receive && !tr, block = block };
        }

        static Bounds WorldBounds(Bounds local, Matrix4x4 M)
        {
            var c = M.MultiplyPoint3x4(local.center); var e = local.extents;
            var ext = new Vector3(Math.Abs(M.m00) * e.x + Math.Abs(M.m01) * e.y + Math.Abs(M.m02) * e.z,
                                  Math.Abs(M.m10) * e.x + Math.Abs(M.m11) * e.y + Math.Abs(M.m12) * e.z,
                                  Math.Abs(M.m20) * e.x + Math.Abs(M.m21) * e.y + Math.Abs(M.m22) * e.z);
            return new Bounds(c, ext * 2);
        }

        // ------------------------------------------------------------------ lighting environment
        sealed class Env
        {
            public bool hasSun; public Vector3 L; public Color lightCol; public float shadowStrength;
            public ShadowMap shadow;
            public sealed class Extra { public LightType type; public Vector3 pos, dir; public Color col; public float range, cosOuter, cosInner; }
            public readonly List<Extra> extras = new List<Extra>();
            static Color Lin(Light l) { var c = l.color * l.intensity; return new Color(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b)); }
            public Color sky, equator, ground; public float ambI;
            public bool fog; public FogMode fogMode; public Color fogCol; public float fogStart, fogEnd, fogDensity;
            public Env(Light sun, ShadowMap sm, IEnumerable<Light> others)
            {
                shadow = sm;
                foreach (var l in others)
                {
                    float outer = Mathf.Clamp(l.spotAngle, 1, 179) * .5f * Mathf.Deg2Rad;
                    // built-in spot cookie: soft edge over roughly the outer fifth of the cone
                    extras.Add(new Extra
                    {
                        type = l.type, pos = l.transform.position, dir = (l.transform.rotation * Vector3.forward).normalized, col = Lin(l), range = Mathf.Max(1e-3f, l.range),
                        cosOuter = Mathf.Cos(outer), cosInner = Mathf.Cos(outer * .8f),
                    });
                }
                if (sun != null)
                {
                    hasSun = true;
                    L = -(sun.transform.rotation * Vector3.forward).normalized;
                    lightCol = Lin(sun);   // GraphicsSettings.lightsUseLinearIntensity = false: scale in gamma, then linearize
                    shadowStrength = sun.shadowStrength;
                }
                sky = RenderSettings.ambientSkyColor.linear; equator = RenderSettings.ambientEquatorColor.linear; ground = RenderSettings.ambientGroundColor.linear;
                ambI = 1;
                if (RenderSettings.ambientMode == AmbientMode.Flat) equator = ground = sky;
                fog = RenderSettings.fog; fogMode = RenderSettings.fogMode; fogCol = RenderSettings.fogColor.linear;
                fogStart = RenderSettings.fogStartDistance; fogEnd = RenderSettings.fogEndDistance; fogDensity = RenderSettings.fogDensity;
            }
            /// <summary>Radiance of the Trilight gradient in direction d (sky above, equator at the horizon, ground below).</summary>
            Color Radiance(Vector3 d) => d.y >= 0 ? Color.Lerp(equator, sky, d.y) : Color.Lerp(equator, ground, -d.y);
            Vector3[] sh;   // L2 irradiance SH (9 RGB coefficients), like the ambient probe Unity bakes from the gradient
            static void Basis(Vector3 d, Span<float> b)
            {
                b[0] = 0.282095f; b[1] = 0.488603f * d.y; b[2] = 0.488603f * d.z; b[3] = 0.488603f * d.x;
                b[4] = 1.092548f * d.x * d.y; b[5] = 1.092548f * d.y * d.z; b[6] = 0.315392f * (3 * d.z * d.z - 1); b[7] = 1.092548f * d.x * d.z; b[8] = 0.546274f * (d.x * d.x - d.y * d.y);
            }
            /// <summary>Builds what shading reads lazily, before fragments are shaded in parallel.</summary>
            public void Prepare() { if (sh == null) BuildSH(); }
            void BuildSH()
            {
                var sh = new Vector3[9];
                Span<float> b = stackalloc float[9];
                const int nt = 64, np = 128; float dw = (Mathf.PI / nt) * (2 * Mathf.PI / np);
                for (int i = 0; i < nt; i++)
                {
                    float th = (i + .5f) * Mathf.PI / nt, st = Mathf.Sin(th);
                    for (int j = 0; j < np; j++)
                    {
                        float ph = (j + .5f) * 2 * Mathf.PI / np;
                        var d = new Vector3(st * Mathf.Cos(ph), Mathf.Cos(th), st * Mathf.Sin(ph));
                        var L = Radiance(d); Basis(d, b);
                        for (int k = 0; k < 9; k++) sh[k] += new Vector3(L.r, L.g, L.b) * (b[k] * st * dw);
                    }
                }
                float[] A = { Mathf.PI, 2 * Mathf.PI / 3, 2 * Mathf.PI / 3, 2 * Mathf.PI / 3, Mathf.PI / 4, Mathf.PI / 4, Mathf.PI / 4, Mathf.PI / 4, Mathf.PI / 4 };
                for (int k = 0; k < 9; k++) sh[k] *= A[k] / Mathf.PI;
                this.sh = sh;
            }
            /// <summary>Reflection probe: the default reflection is baked from the skybox – none here (RenderSettings.skybox = null) → black.</summary>
            public static float SpecularScale = float.TryParse(Environment.GetEnvironmentVariable("MINIUNITY_SPECGI"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f;
            public Color SpecularGI(Vector3 r, float perceptualRoughness) => SpecularScale > 0 ? Ambient(r) * SpecularScale : Color.black;
            /// <summary>ShadeSH9: irradiance / π for normal n.</summary>
            public Color Ambient(Vector3 n)
            {
                if (sh == null) BuildSH();
                Span<float> b = stackalloc float[9]; Basis(n, b);
                var c = Vector3.zero;
                for (int k = 0; k < 9; k++) c += sh[k] * b[k];
                return new Color(Mathf.Max(0, c.x), Mathf.Max(0, c.y), Mathf.Max(0, c.z));
            }
        }

        /// <summary>World-space vertices as the vertex shader places them (RoundedBox rounds the corners; used by the shadow pass too).</summary>
        static Vector3[] WorldPositions(Draw d)
        {
            var mesh = d.mesh; var M = d.M; int n = mesh.m_V.Length;
            var res = new Vector3[n];
            if (!d.rounded) { for (int i = 0; i < n; i++) res[i] = M.MultiplyPoint3x4(mesh.m_V[i]); return res; }
            var mat = Mat.Of(d);
            var R = Matrix4x4.Rotate(d.rot);
            var scale = new Vector3(new Vector3(M.m00, M.m10, M.m20).magnitude, new Vector3(M.m01, M.m11, M.m21).magnitude, new Vector3(M.m02, M.m12, M.m22).magnitude);
            var hs = scale * .5f;
            float r = Mathf.Max(0, Mathf.Min(mat.radius, Mathf.Min(hs.x, Mathf.Min(hs.y, hs.z))));
            var inner = hs - Vector3.one * r;
            for (int i = 0; i < n; i++)
            {
                var ps = Vector3.Scale(mesh.m_V[i], scale);
                var q = new Vector3(Mathf.Clamp(ps.x, -inner.x, inner.x), Mathf.Clamp(ps.y, -inner.y, inner.y), Mathf.Clamp(ps.z, -inner.z, inner.z));
                var dd = ps - q; float len = dd.magnitude;
                if (len > 1e-6f && r > 0) ps = q + dd / len * r;
                res[i] = R.MultiplyVector(ps) + new Vector3(M.m03, M.m13, M.m23);
            }
            return res;
        }

        // ------------------------------------------------------------------ one draw
        static void DrawOne(Draw d, UIRenderer.View view, Env env, bool visibility)
        {
            var mat = Mat.Of(d);
            mat.receive = d.receive;
            var mesh = d.mesh; var idx = mesh.m_Sub[d.sub];
            int n = mesh.m_V.Length;
            if (n == 0 || idx.Length < 3) return;
            var M = d.M;
            var R = Matrix4x4.Rotate(d.rot);
            var scale = new Vector3(new Vector3(M.m00, M.m10, M.m20).magnitude, new Vector3(M.m01, M.m11, M.m21).magnitude, new Vector3(M.m02, M.m12, M.m22).magnitude);
            var invT = M.inverse.transpose;
            var wp = new Vector3[n]; var wn = new Vector3[n]; var clip = new Vector4[n]; var rim = new float[n];
            for (int i = 0; i < n; i++)
            {
                var p = mesh.m_V[i]; var nn = mesh.m_N != null && i < mesh.m_N.Length ? mesh.m_N[i] : Vector3.up;
                if (mat.rounded)
                {
                    var ps = Vector3.Scale(p, scale); var hs = scale * .5f;
                    float r = Mathf.Max(0, Mathf.Min(mat.radius, Mathf.Min(hs.x, Mathf.Min(hs.y, hs.z))));
                    var inner = hs - Vector3.one * r;
                    var q = new Vector3(Mathf.Clamp(ps.x, -inner.x, inner.x), Mathf.Clamp(ps.y, -inner.y, inner.y), Mathf.Clamp(ps.z, -inner.z, inner.z));
                    var dd = ps - q; float len = dd.magnitude;
                    if (len > 1e-6f && r > 0) { var nd = dd / len; ps = q + nd * r; wn[i] = (R.MultiplyVector(nd)).normalized; }
                    else wn[i] = invT.MultiplyVector(nn).normalized;
                    wp[i] = R.MultiplyVector(ps) + new Vector3(M.m03, M.m13, M.m23);
                    var vdir = (view.camPos - wp[i]).normalized;
                    rim[i] = Mathf.Pow(1 - Mathf.Clamp01(Vector3.Dot(vdir, wn[i])), mat.rimPower);
                }
                else { wp[i] = M.MultiplyPoint3x4(p); wn[i] = invT.MultiplyVector(nn).normalized; }
                clip[i] = view.VP * new Vector4(wp[i].x, wp[i].y, wp[i].z, 1);
            }

            var verts = new Vtx[n];
            for (int i = 0; i < n; i++) verts[i] = new Vtx { c = clip[i], p = wp[i], nrm = wn[i], rim = rim[i] };
            var poly = new List<Vtx>(6); var tmp = new List<Vtx>(6);
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                var A = verts[idx[t]]; var B = verts[idx[t + 1]]; var C = verts[idx[t + 2]];
                bool ia = A.c.z + A.c.w < 0, ib = B.c.z + B.c.w < 0, ic = C.c.z + C.c.w < 0;
                if (ia && ib && ic) continue;
                if (!ia && !ib && !ic) { if (visibility) DepthTri(A, B, C, mat, view); else RasterTri(A, B, C, mat, view, env); continue; }
                // clip against the near plane (z = -w in GL clip space)
                poly.Clear(); poly.Add(A); poly.Add(B); poly.Add(C); tmp.Clear();
                for (int k = 0; k < poly.Count; k++)
                {
                    var P0 = poly[k]; var P1 = poly[(k + 1) % poly.Count];
                    float d0 = P0.c.z + P0.c.w, d1 = P1.c.z + P1.c.w;
                    if (d0 >= 0) tmp.Add(P0);
                    if ((d0 >= 0) != (d1 >= 0)) tmp.Add(Vtx.Lerp(P0, P1, d0 / (d0 - d1)));
                }
                for (int k = 1; k + 1 < tmp.Count; k++)
                    if (visibility) DepthTri(tmp[0], tmp[k], tmp[k + 1], mat, view); else RasterTri(tmp[0], tmp[k], tmp[k + 1], mat, view, env);
            }
        }

        struct Vtx
        {
            public Vector4 c; public Vector3 p, nrm; public float rim;
            public static Vtx Lerp(Vtx a, Vtx b, float t) => new Vtx { c = Vector4.Lerp(a.c, b.c, t), p = Vector3.Lerp(a.p, b.p, t), nrm = Vector3.Lerp(a.nrm, b.nrm, t), rim = a.rim + (b.rim - a.rim) * t };
        }

        static void RasterTri(Vtx A, Vtx B, Vtx C, Mat mat, UIRenderer.View view, Env env)
        {
            int W = view.W, H = view.H;
            var color = view.target.m_SurfColor; var depth = view.target.m_SurfDepth;
            bool zwrite = !mat.fade;
            Span<float> sx = stackalloc float[3], sy = stackalloc float[3], sz = stackalloc float[3], iw = stackalloc float[3];
            Vtx[] v = { A, B, C };
            for (int k = 0; k < 3; k++)
            {
                var c = v[k].c;
                if (c.w <= 1e-6f) return;
                sx[k] = (c.x / c.w * .5f + .5f) * W; sy[k] = (c.y / c.w * .5f + .5f) * H; sz[k] = c.z / c.w * .5f + .5f; iw[k] = 1 / c.w;
            }
            float area = (sx[1] - sx[0]) * (sy[2] - sy[0]) - (sx[2] - sx[0]) * (sy[1] - sy[0]);
            if (area >= 0) return;   // Cull Back: front faces are clockwise on screen (negative area with y up)
            Triangles++;
            int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(sx[0], MathF.Min(sx[1], sx[2])))), maxX = Math.Min(W - 1, (int)MathF.Ceiling(MathF.Max(sx[0], MathF.Max(sx[1], sx[2]))));
            int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(sy[0], MathF.Min(sy[1], sy[2])))), maxY = Math.Min(H - 1, (int)MathF.Ceiling(MathF.Max(sy[0], MathF.Max(sy[1], sy[2]))));
            if (minX > maxX || minY > maxY) return;
            float ia = 1 / area;
            for (int y = minY; y <= maxY; y++)
            {
                float py = y + .5f;
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x + .5f;
                    float w0 = ((sx[2] - sx[1]) * (py - sy[1]) - (sy[2] - sy[1]) * (px - sx[1])) * ia;
                    float w1 = ((sx[0] - sx[2]) * (py - sy[2]) - (sy[0] - sy[2]) * (px - sx[2])) * ia;
                    float w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    float z = w0 * sz[0] + w1 * sz[1] + w2 * sz[2];
                    int pi = y * W + x;
                    if (z >= depth[pi] || z < 0) continue;
                    float p0 = w0 * iw[0], p1 = w1 * iw[1], p2 = w2 * iw[2], ps = 1 / (p0 + p1 + p2);
                    p0 *= ps; p1 *= ps; p2 *= ps;
                    var P = A.p * p0 + B.p * p1 + C.p * p2;
                    var N = (A.nrm * p0 + B.nrm * p1 + C.nrm * p2).normalized;
                    float rimv = A.rim * p0 + B.rim * p1 + C.rim * p2;
                    float viewDepth = 1 / (w0 * iw[0] + w1 * iw[1] + w2 * iw[2]);
                    var c = Shade(mat, env, P, N, view.camPos, rimv, viewDepth);
                    // LDR camera target: the fragment output is clamped to [0, 1] before blending (matters for Fade materials)
                    c = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), c.a);
                    if (zwrite) depth[pi] = z;
                    Blend(color, pi * 4, c, mat.fade ? mat.albedo.a : 1);
                }
            }
        }

        // ------------------------------------------------------------------ C# shaders
        struct CVtx
        {
            public Vector4 c; public Vector3 p, nrm, obj; public Vector4 tan; public Vector2 uv; public Color col;
            public static CVtx Lerp(CVtx a, CVtx b, float t) => new CVtx
            {
                c = Vector4.Lerp(a.c, b.c, t), p = Vector3.Lerp(a.p, b.p, t), nrm = Vector3.Lerp(a.nrm, b.nrm, t), obj = Vector3.Lerp(a.obj, b.obj, t),
                tan = Vector4.Lerp(a.tan, b.tan, t), uv = Vector2.Lerp(a.uv, b.uv, t), col = Color.LerpUnclamped(a.col, b.col, t),
            };
        }

        static void DrawCustom(Draw d, UIRenderer.View view, Env env)
        {
            var mesh = d.mesh; var idx = mesh.m_Sub[d.sub];
            int n = mesh.m_V.Length;
            if (n == 0 || idx.Length < 3) return;
            d.inputs.objectToWorld = d.M; d.inputs.viewProjection = view.VP; d.inputs.screenSize = new Vector2(view.W, view.H);
            d.shader.Prepare(d.inputs);
            // the Standard lighting for lit C# shaders: a Mat per draw whose albedo and surface values are set per fragment
            var scratch = new Mat { receive = d.receive };
            var lighting = new Lighting
            {
                standard = (albedo, smooth, metal, emission, receive, P, N, viewDepth) =>
                {
                    scratch.albedo = albedo; scratch.smooth = smooth; scratch.metal = metal; scratch.emission = emission; scratch.receive = receive && d.receive;
                    return Shade(scratch, env, P, N, view.camPos, 0, viewDepth);
                },
            };
            var M = d.M; var invT = M.inverse.transpose;
            var uv0 = mesh.m_UV[0]; var cols = mesh.m_C; var tans = mesh.m_T; var nrms = mesh.m_N;
            var verts = new CVtx[n];
            for (int i = 0; i < n; i++)
            {
                var p = d.shader.Vertex(d.inputs, mesh.m_V[i]);
                var wp = M.MultiplyPoint3x4(p);
                var nn = nrms != null && i < nrms.Length ? nrms[i] : Vector3.up;
                var t4 = tans != null && i < tans.Length ? tans[i] : Vector4.zero;
                var wt = M.MultiplyVector(new Vector3(t4.x, t4.y, t4.z));
                verts[i] = new CVtx
                {
                    c = view.VP * new Vector4(wp.x, wp.y, wp.z, 1), p = wp, nrm = invT.MultiplyVector(nn).normalized, obj = p,
                    tan = new Vector4(wt.x, wt.y, wt.z, t4.w), uv = uv0 != null && i < uv0.Length ? new Vector2(uv0[i].x, uv0[i].y) : Vector2.zero,
                    col = cols != null && i < cols.Length ? cols[i] : Color.white,
                };
            }
            var poly = new List<CVtx>(6); var tmp = new List<CVtx>(6);
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                var A = verts[idx[t]]; var B = verts[idx[t + 1]]; var C = verts[idx[t + 2]];
                bool ia = A.c.z + A.c.w < 0, ib = B.c.z + B.c.w < 0, ic = C.c.z + C.c.w < 0;
                if (ia && ib && ic) continue;
                if (!ia && !ib && !ic) { RasterCustom(A, B, C, d, view, lighting); continue; }
                poly.Clear(); poly.Add(A); poly.Add(B); poly.Add(C); tmp.Clear();
                for (int k = 0; k < poly.Count; k++)
                {
                    var P0 = poly[k]; var P1 = poly[(k + 1) % poly.Count];
                    float d0 = P0.c.z + P0.c.w, d1 = P1.c.z + P1.c.w;
                    if (d0 >= 0) tmp.Add(P0);
                    if ((d0 >= 0) != (d1 >= 0)) tmp.Add(CVtx.Lerp(P0, P1, d0 / (d0 - d1)));
                }
                for (int k = 1; k + 1 < tmp.Count; k++) RasterCustom(tmp[0], tmp[k], tmp[k + 1], d, view, lighting);
            }
        }

        static void RasterCustom(CVtx A, CVtx B, CVtx C, Draw d, UIRenderer.View view, Lighting lighting)
        {
            int W = view.W, H = view.H;
            var color = view.target.m_SurfColor; var depth = view.target.m_SurfDepth;
            bool blend = d.queue == WorldShader.RenderQueue.Transparent, zwrite = !blend;
            Span<float> sx = stackalloc float[3], sy = stackalloc float[3], sz = stackalloc float[3], iw = stackalloc float[3];
            for (int k = 0; k < 3; k++)
            {
                var c = (k == 0 ? A : k == 1 ? B : C).c;
                if (c.w <= 1e-6f) return;
                sx[k] = (c.x / c.w * .5f + .5f) * W; sy[k] = (c.y / c.w * .5f + .5f) * H; sz[k] = c.z / c.w * .5f + .5f; iw[k] = 1 / c.w;
            }
            float area = (sx[1] - sx[0]) * (sy[2] - sy[0]) - (sx[2] - sx[0]) * (sy[1] - sy[0]);
            if (area >= 0 && d.shader.CullBack) return;   // Cull Back: front faces are clockwise on screen
            if (MathF.Abs(area) < 1e-12f) return;
            Triangles++;
            int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(sx[0], MathF.Min(sx[1], sx[2])))), maxX = Math.Min(W - 1, (int)MathF.Ceiling(MathF.Max(sx[0], MathF.Max(sx[1], sx[2]))));
            int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(sy[0], MathF.Min(sy[1], sy[2])))), maxY = Math.Min(H - 1, (int)MathF.Ceiling(MathF.Max(sy[0], MathF.Max(sy[1], sy[2]))));
            if (minX > maxX || minY > maxY) return;
            float ia = 1 / area;
            bool backFace = area >= 0;
            var f = new FragmentInput { lighting = lighting };
            for (int y = minY; y <= maxY; y++)
            {
                float py = y + .5f;
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x + .5f;
                    float w0 = ((sx[2] - sx[1]) * (py - sy[1]) - (sy[2] - sy[1]) * (px - sx[1])) * ia;
                    float w1 = ((sx[0] - sx[2]) * (py - sy[2]) - (sy[0] - sy[2]) * (px - sx[2])) * ia;
                    float w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    float z = w0 * sz[0] + w1 * sz[1] + w2 * sz[2];
                    int pi = y * W + x;
                    if (z >= depth[pi] || z < 0) continue;
                    float p0 = w0 * iw[0], p1 = w1 * iw[1], p2 = w2 * iw[2], ps = 1 / (p0 + p1 + p2);
                    p0 *= ps; p1 *= ps; p2 *= ps;
                    f.worldPos = A.p * p0 + B.p * p1 + C.p * p2;
                    var N = (A.nrm * p0 + B.nrm * p1 + C.nrm * p2).normalized;
                    f.normal = backFace ? -N : N;
                    f.objectPos = A.obj * p0 + B.obj * p1 + C.obj * p2;
                    f.tangent = A.tan * p0 + B.tan * p1 + C.tan * p2;
                    f.uv = A.uv * p0 + B.uv * p1 + C.uv * p2;
                    f.vertexColor = A.col * p0 + B.col * p1 + C.col * p2;
                    f.viewDir = (view.camPos - f.worldPos).normalized;
                    f.viewDepth = 1 / (w0 * iw[0] + w1 * iw[1] + w2 * iw[2]);
                    if (!d.shader.Fragment(d.inputs, f, out var c)) continue;
                    c = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(c.a));
                    if (zwrite) depth[pi] = z;
                    Blend(color, pi * 4, c, blend ? c.a : 1);
                }
            }
        }

        // ------------------------------------------------------------------ opaque: visibility buffer
        /// <summary>A screen triangle as RasterTri sets it up (the shading pass recomputes the same barycentrics from it).</summary>
        struct Tri
        {
            public Vtx A, B, C; public Mat mat;
            public float sx0, sx1, sx2, sy0, sy1, sy2, iw0, iw1, iw2, ia;
        }
        static Tri[] tris = new Tri[256];
        static int triCount;
        static int[] vis = Array.Empty<int>();   // per pixel: index of the visible opaque triangle, -1 = none

        static void BeginVisibility(UIRenderer.View view)
        {
            int n = view.W * view.H;
            if (vis.Length < n) vis = new int[n];
            Array.Fill(vis, -1, 0, n);
            triCount = 0;
        }

        /// <summary>RasterTri's setup and depth test for an opaque triangle, writing depth and the visible triangle instead of colour.</summary>
        static void DepthTri(Vtx A, Vtx B, Vtx C, Mat mat, UIRenderer.View view)
        {
            int W = view.W, H = view.H;
            var depth = view.target.m_SurfDepth;
            Span<float> sx = stackalloc float[3], sy = stackalloc float[3], sz = stackalloc float[3], iw = stackalloc float[3];
            for (int k = 0; k < 3; k++)
            {
                var c = (k == 0 ? A : k == 1 ? B : C).c;
                if (c.w <= 1e-6f) return;
                sx[k] = (c.x / c.w * .5f + .5f) * W; sy[k] = (c.y / c.w * .5f + .5f) * H; sz[k] = c.z / c.w * .5f + .5f; iw[k] = 1 / c.w;
            }
            float area = (sx[1] - sx[0]) * (sy[2] - sy[0]) - (sx[2] - sx[0]) * (sy[1] - sy[0]);
            if (area >= 0) return;   // Cull Back
            Triangles++;
            int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(sx[0], MathF.Min(sx[1], sx[2])))), maxX = Math.Min(W - 1, (int)MathF.Ceiling(MathF.Max(sx[0], MathF.Max(sx[1], sx[2]))));
            int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(sy[0], MathF.Min(sy[1], sy[2])))), maxY = Math.Min(H - 1, (int)MathF.Ceiling(MathF.Max(sy[0], MathF.Max(sy[1], sy[2]))));
            if (minX > maxX || minY > maxY) return;
            float ia = 1 / area;
            if (triCount == tris.Length) Array.Resize(ref tris, tris.Length * 2);
            int ti = triCount++;
            tris[ti] = new Tri { A = A, B = B, C = C, mat = mat, sx0 = sx[0], sx1 = sx[1], sx2 = sx[2], sy0 = sy[0], sy1 = sy[1], sy2 = sy[2], iw0 = iw[0], iw1 = iw[1], iw2 = iw[2], ia = ia };
            var vis = WorldRenderer.vis;
            for (int y = minY; y <= maxY; y++)
            {
                float py = y + .5f;
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x + .5f;
                    float w0 = ((sx[2] - sx[1]) * (py - sy[1]) - (sy[2] - sy[1]) * (px - sx[1])) * ia;
                    float w1 = ((sx[0] - sx[2]) * (py - sy[2]) - (sy[0] - sy[2]) * (px - sx[2])) * ia;
                    float w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    float z = w0 * sz[0] + w1 * sz[1] + w2 * sz[2];
                    int pi = y * W + x;
                    if (z >= depth[pi] || z < 0) continue;
                    depth[pi] = z; vis[pi] = ti;
                }
            }
        }

        /// <summary>Shade every pixel that kept an opaque triangle, exactly as RasterTri would have for that triangle.</summary>
        static void ShadeVisible(UIRenderer.View view, Env env)
        {
            if (triCount == 0) return;
            int W = view.W, H = view.H;
            var color = view.target.m_SurfColor;
            var tris = WorldRenderer.tris; var vis = WorldRenderer.vis;
            System.Threading.Tasks.Parallel.For(0, H, y =>
            {
                float py = y + .5f;
                int row = y * W;
                for (int x = 0; x < W; x++)
                {
                    int ti = vis[row + x];
                    if (ti < 0) continue;
                    ref readonly var t = ref tris[ti];
                    float px = x + .5f;
                    float w0 = ((t.sx2 - t.sx1) * (py - t.sy1) - (t.sy2 - t.sy1) * (px - t.sx1)) * t.ia;
                    float w1 = ((t.sx0 - t.sx2) * (py - t.sy2) - (t.sy0 - t.sy2) * (px - t.sx2)) * t.ia;
                    float w2 = 1 - w0 - w1;
                    float p0 = w0 * t.iw0, p1 = w1 * t.iw1, p2 = w2 * t.iw2, ps = 1 / (p0 + p1 + p2);
                    p0 *= ps; p1 *= ps; p2 *= ps;
                    var P = t.A.p * p0 + t.B.p * p1 + t.C.p * p2;
                    var N = (t.A.nrm * p0 + t.B.nrm * p1 + t.C.nrm * p2).normalized;
                    float rimv = t.A.rim * p0 + t.B.rim * p1 + t.C.rim * p2;
                    float viewDepth = 1 / (w0 * t.iw0 + w1 * t.iw1 + w2 * t.iw2);
                    var c = Shade(t.mat, env, P, N, view.camPos, rimv, viewDepth);
                    c = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), c.a);
                    Blend(color, (row + x) * 4, c, t.mat.fade ? t.mat.albedo.a : 1);
                }
            });
        }

        static void Blend(byte[] color, int o, Color c, float a)
        {
            float ia = 1 - a;
            color[o] = TextureData.LinearToSrgbByte(c.r * a + TextureData.SrgbToLinear[color[o]] * ia);
            color[o + 1] = TextureData.LinearToSrgbByte(c.g * a + TextureData.SrgbToLinear[color[o + 1]] * ia);
            color[o + 2] = TextureData.LinearToSrgbByte(c.b * a + TextureData.SrgbToLinear[color[o + 2]] * ia);
            color[o + 3] = (byte)Math.Clamp((int)MathF.Round((a + color[o + 3] / 255f * ia) * 255), 0, 255);
        }

        // ------------------------------------------------------------------ Standard (BRDF1_Unity_PBS)
        static Color Shade(Mat m, Env e, Vector3 P, Vector3 N, Vector3 camPos, float rim, float viewDepth)
        {
            var V = (camPos - P).normalized;
            float oneMinusRefl = 0.96f * (1 - m.metal);
            var albedo = m.albedo;
            var diff = new Color(albedo.r * oneMinusRefl, albedo.g * oneMinusRefl, albedo.b * oneMinusRefl);
            var spec = Color.Lerp(new Color(.04f, .04f, .04f), new Color(albedo.r, albedo.g, albedo.b), m.metal);
            float pr = 1 - m.smooth, rough = Mathf.Max(pr * pr, .002f);
            float nv = Mathf.Abs(Vector3.Dot(N, V));
            var gDiffuse = e.Ambient(N);
            var Rv = Vector3.Reflect(-V, N);
            var gSpec = e.SpecularGI(Rv, pr);
            Color col = diff * gDiffuse;
            if (e.hasSun)
            {
                float atten = 1;
                float nl0 = Vector3.Dot(N, e.L);
                if (e.shadow != null && m.receive && nl0 > 0) atten = 1 - e.shadowStrength * (1 - e.shadow.Sample(P, N, nl0, camPos));
                col += Direct(e.L, e.lightCol * atten, N, V, nv, diff, spec, pr, rough);
            }
            foreach (var x in e.extras)
            {
                Vector3 L; float atten = 1;
                if (x.type == LightType.Directional) L = -x.dir;
                else
                {
                    var toL = x.pos - P; float d2 = toL.sqrMagnitude;
                    float r2 = d2 / (x.range * x.range);
                    if (r2 >= 1) continue;
                    // built-in attenuation texture: 1 / (1 + 25·(d/range)²), faded to 0 over the last 20 % of (d/range)²
                    atten = 1 / (1 + 25 * r2) * Mathf.Clamp01((1 - r2) * 5);
                    L = toL / Mathf.Sqrt(Mathf.Max(d2, 1e-8f));
                    if (x.type == LightType.Spot)
                    {
                        float cd = Vector3.Dot(-L, x.dir);
                        if (cd <= x.cosOuter) continue;
                        atten *= Mathf.SmoothStep(0, 1, Mathf.Clamp01((cd - x.cosOuter) / Mathf.Max(1e-4f, x.cosInner - x.cosOuter)));
                    }
                }
                col += Direct(L, x.col * atten, N, V, nv, diff, spec, pr, rough);
            }
            float surfaceReduction = 1 / (rough * rough + 1);
            float grazing = Mathf.Clamp01(m.smooth + (1 - oneMinusRefl));
            var Fl = Color.Lerp(spec, new Color(grazing, grazing, grazing), Pow5(1 - nv));
            col += surfaceReduction * new Color(gSpec.r * Fl.r, gSpec.g * Fl.g, gSpec.b * Fl.b);
            col += m.emission;
            if (m.rounded) col += m.rim * rim * m.rimStrength;
            if (e.fog)
            {
                float f = e.fogMode switch
                {
                    FogMode.Linear => Mathf.Clamp01((e.fogEnd - viewDepth) / Mathf.Max(1e-4f, e.fogEnd - e.fogStart)),
                    FogMode.Exponential => Mathf.Clamp01(Mathf.Exp(-e.fogDensity * viewDepth)),
                    _ => Mathf.Clamp01(Mathf.Exp(-(e.fogDensity * viewDepth) * (e.fogDensity * viewDepth))),
                };
                col = Color.Lerp(e.fogCol, col, f);
            }
            return col;
        }
        static float Pow5(float x) { float x2 = x * x; return x2 * x2 * x; }

        /// <summary>One light's contribution, BRDF1_Unity_PBS (Disney diffuse + GGX / Smith-joint + Schlick).</summary>
        static Color Direct(Vector3 L, Color lc, Vector3 N, Vector3 V, float nv, Color diff, Color spec, float pr, float rough)
        {
            var Hh = (L + V).normalized;
            float nl = Mathf.Clamp01(Vector3.Dot(N, L)), nh = Mathf.Clamp01(Vector3.Dot(N, Hh)), lh = Mathf.Clamp01(Vector3.Dot(L, Hh));
            if (nl <= 0) return Color.black;
            float fd90 = .5f + 2 * lh * lh * pr;
            float diffuseTerm = (1 + (fd90 - 1) * Pow5(1 - nl)) * (1 + (fd90 - 1) * Pow5(1 - nv)) * nl;
            float a = rough, lambdaV = nl * (nv * (1 - a) + a), lambdaL = nv * (nl * (1 - a) + a);
            float Vis = .5f / (lambdaV + lambdaL + 1e-5f);
            float a2 = rough * rough, dd = (nh * a2 - nh) * nh + 1;
            float D = (1 / Mathf.PI) * a2 / (dd * dd + 1e-7f);
            float specularTerm = Mathf.Max(0, Vis * D * Mathf.PI * nl);
            var F = spec + (Color.white - spec) * Pow5(1 - lh);
            return diff * lc * diffuseTerm + specularTerm * new Color(lc.r * F.r, lc.g * F.g, lc.b * F.b);
        }

        // ------------------------------------------------------------------ shadows
        /// <summary>Shadow settings of the active quality level (ProjectSettings/QualitySettings.asset, m_CurrentQuality).</summary>
        public static class ShadowQuality
        {
            public static float Distance = 150; public static int Cascades = 4, Resolution = 2;
            public static float Split2 = 1 / 3f; public static Vector3 Split4 = new Vector3(.0666667f, .2f, .4666667f);
            static bool loaded;
            public static void Load()
            {
                if (loaded) return;
                loaded = true;
                try
                {
                    var file = System.IO.Path.Combine(AssetIndex.Root, "ProjectSettings", "QualitySettings.asset");
                    if (!System.IO.File.Exists(file)) return;
                    var lines = System.IO.File.ReadAllLines(file);
                    int cur = 0;
                    foreach (var l in lines) if (l.Trim().StartsWith("m_CurrentQuality:")) int.TryParse(l.Split(':')[1].Trim(), out cur);
                    int level = -1;
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    float F(string v) => float.Parse(v.Trim(), inv);
                    foreach (var raw in lines)
                    {
                        var l = raw.Trim();
                        if (l.StartsWith("- serializedVersion:")) { level++; continue; }
                        if (level != cur) continue;
                        if (l.StartsWith("shadowDistance:")) Distance = F(l.Substring(15));
                        else if (l.StartsWith("shadowCascades:")) Cascades = (int)F(l.Substring(15));
                        else if (l.StartsWith("shadowResolution:")) Resolution = (int)F(l.Substring(17));
                        else if (l.StartsWith("shadowCascade2Split:")) Split2 = F(l.Substring(20));
                        else if (l.StartsWith("shadowCascade4Split:"))
                        {
                            var m = System.Text.RegularExpressions.Regex.Matches(l, @"[xyz]:\s*([-0-9.eE]+)");
                            if (m.Count == 3) Split4 = new Vector3(F(m[0].Groups[1].Value), F(m[1].Groups[1].Value), F(m[2].Groups[1].Value));
                        }
                    }
                }
                catch (Exception ex) { Console.Error.WriteLine("[Shadow] QualitySettings: " + ex.Message); }
            }
            /// <summary>Directional shadow atlas size: Low 512 · Medium 1024 · High 2048 · Very High 4096.</summary>
            public static int AtlasSize => 512 << Math.Clamp(Resolution, 0, 3);
        }

        /// <summary>
        /// Cascaded shadow map like the built-in pipeline with "Stable Fit": each cascade is a sphere around a slice of the view
        /// frustum, snapped to whole texels; cascades share the atlas (4 → quarters, 2 → halves). Receivers pick the first sphere
        /// that contains them and filter with the 5×5 tent PCF of "Soft Shadows" (1 tap for "Hard Shadows").
        /// </summary>
        sealed class ShadowMap
        {
            sealed class Cascade { public float[] depth; public int res; public float minX, minY, texel, bias, normalBias; public Vector3 center; public float r2; }
            readonly List<Cascade> cascades = new List<Cascade>();
            Matrix4x4 lightV; bool soft; float fadeStart, fadeEnd;
            static readonly float[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            static readonly List<float[]> depthPool = new();   // cascade depth maps of the previous frame, reused

            public static ShadowMap Build(Light sun, List<Draw> draws, Camera cam, Vector3 camPos)
            {
                ShadowQuality.Load();
                var sm = new ShadowMap { soft = sun.shadows == LightShadows.Soft };
                sm.lightV = Matrix4x4.Rotate(Quaternion.Inverse(sun.transform.rotation));
                float dist = Mathf.Min(ShadowQuality.Distance, cam.farClipPlane);
                int n = ShadowQuality.Cascades >= 4 ? 4 : ShadowQuality.Cascades >= 2 ? 2 : 1;
                var splits = n == 4 ? new[] { 0, ShadowQuality.Split4.x, ShadowQuality.Split4.y, ShadowQuality.Split4.z, 1f } : n == 2 ? new[] { 0, ShadowQuality.Split2, 1f } : new[] { 0f, 1f };
                int res = n == 1 ? ShadowQuality.AtlasSize : ShadowQuality.AtlasSize / 2;
                sm.fadeEnd = dist; sm.fadeStart = dist * .9f;

                // view-frustum slices → bounding spheres (centre on the view axis, equidistant to near and far corners)
                float tanV = Mathf.Tan(cam.fieldOfView * .5f * Mathf.Deg2Rad), k = Mathf.Sqrt(tanV * tanV * (1 + cam.aspect * cam.aspect));
                var fwd = cam.transform.forward;
                var casters = new List<(Draw d, Vector3[] lp)>();
                foreach (var d in draws)
                {
                    if (d.cast == ShadowCastingMode.Off || d.alpha <= 0 || (d.shader != null && !d.shader.CastsShadows(d.inputs))) continue;
                    var b = d.bounds;
                    if ((b.center - camPos).magnitude - b.extents.magnitude > dist * 1.5f) continue;
                    casters.Add((d, WorldPositions(d).Select(v => sm.lightV.MultiplyPoint3x4(v)).ToArray()));
                }
                if (casters.Count == 0) return null;
                var bounds = casters.Select(cs => { float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                    foreach (var p in cs.lp) { x0 = Math.Min(x0, p.x); x1 = Math.Max(x1, p.x); y0 = Math.Min(y0, p.y); y1 = Math.Max(y1, p.y); } return (x0, x1, y0, y1); }).ToArray();
                for (int c = 0; c < n; c++)
                {
                    float near = c == 0 ? cam.nearClipPlane : dist * splits[c], far = dist * splits[c + 1];
                    float rn = near * k, rf = far * k;
                    float cz = (far * far + rf * rf - near * near - rn * rn) / (2 * (far - near));
                    cz = Mathf.Clamp(cz, near, far);
                    float R = Mathf.Max(Mathf.Sqrt((cz - near) * (cz - near) + rn * rn), Mathf.Sqrt((far - cz) * (far - cz) + rf * rf));
                    var centre = camPos + fwd * cz;
                    var cs = new Cascade { res = res, texel = 2 * R / res, center = centre, r2 = R * R };
                    var lc = sm.lightV.MultiplyPoint3x4(centre);
                    // stable fit: snap the light-space origin to whole texels so shadows do not crawl when the camera moves
                    cs.minX = Mathf.Floor((lc.x - R) / cs.texel) * cs.texel; cs.minY = Mathf.Floor((lc.y - R) / cs.texel) * cs.texel;
                    cs.bias = sun.shadowBias * cs.texel * 10; cs.normalBias = sun.shadowNormalBias * cs.texel * 1.5f;
                    int pooled = depthPool.FindIndex(a => a.Length == res * res);
                    if (pooled >= 0) { cs.depth = depthPool[pooled]; depthPool.RemoveAt(pooled); } else cs.depth = new float[res * res];
                    sm.cascades.Add(cs);
                }
                // each cascade has its own depth map: rasterize them in parallel (casters in the same order within a cascade)
                System.Threading.Tasks.Parallel.For(0, sm.cascades.Count, ci =>
                {
                    var cs = sm.cascades[ci];
                    Array.Fill(cs.depth, float.MaxValue);
                    float maxX = cs.minX + cs.res * cs.texel, maxY = cs.minY + cs.res * cs.texel;
                    for (int k = 0; k < casters.Count; k++)
                    {
                        var (d, lp) = casters[k]; var (x0, x1, y0, y1) = bounds[k];
                        if (x1 < cs.minX || x0 > maxX || y1 < cs.minY || y0 > maxY) continue;
                        var idx = d.mesh.m_Sub[d.sub];
                        for (int t = 0; t + 2 < idx.Length; t += 3) sm.Raster(cs, lp[idx[t]], lp[idx[t + 1]], lp[idx[t + 2]], d.alpha);
                    }
                });

                if (Environment.GetEnvironmentVariable("MINIUNITY_SHADOW_DUMP") is string dump)
                {
                    var c0 = sm.cascades[0]; int q = Math.Max(1, c0.res / 1024), w = c0.res / q;
                    var tex = new Texture2D(w, w);
                    float zmin = c0.depth.Where(z => z != float.MaxValue).DefaultIfEmpty(0).Min(), zmax = c0.depth.Where(z => z != float.MaxValue).DefaultIfEmpty(1).Max();
                    for (int y = 0; y < w; y++) for (int x = 0; x < w; x++)
                        { float z = c0.depth[(y * q) * c0.res + x * q]; float v = z == float.MaxValue ? 1 : (z - zmin) / Math.Max(1e-4f, zmax - zmin); tex.SetPixel(x, y, new Color(v, v, v, 1)); }
                    System.IO.File.WriteAllBytes(dump, ImageConversion.EncodeToPNG(tex));
                    foreach (var c in sm.cascades) Console.Error.WriteLine($"[Shadow] cascade r={Mathf.Sqrt(c.r2):0.##} texel={c.texel:0.####} res={c.res}");
                }
                return sm;
            }

            void Raster(Cascade cs, Vector3 a, Vector3 b, Vector3 c, float alpha)
            {
                int res = cs.res; float k = 1 / cs.texel;
                float ax = (a.x - cs.minX) * k, ay = (a.y - cs.minY) * k, bx = (b.x - cs.minX) * k, by = (b.y - cs.minY) * k, cx = (c.x - cs.minX) * k, cy = (c.y - cs.minY) * k;
                float area = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
                if (MathF.Abs(area) < 1e-9f) return;
                int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx)))), x1 = Math.Min(res - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))));
                int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy)))), y1 = Math.Min(res - 1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))));
                if (x0 > x1 || y0 > y1) return;
                float ia = 1 / area;
                // Fade / Transparent materials cast dithered shadows (Standard shadow caster + 4×4 dither mask)
                float cut = alpha >= 1 ? -1 : alpha * 16;
                var depth = cs.depth;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        if (cut >= 0 && Bayer[(y & 3) * 4 + (x & 3)] >= cut) continue;
                        float px = x + .5f, py = y + .5f;
                        float w0 = ((cx - bx) * (py - by) - (cy - by) * (px - bx)) * ia, w1 = ((ax - cx) * (py - cy) - (ay - cy) * (px - cx)) * ia, w2 = 1 - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                        float z = w0 * a.z + w1 * b.z + w2 * c.z;
                        int i = y * res + x;
                        if (z < depth[i]) depth[i] = z;
                    }
            }

            /// <summary>Hands the depth maps back for the next shadow map (after the draws that sample this one).</summary>
            public void Release()
            {
                foreach (var cs in cascades) depthPool.Add(cs.depth);
                while (depthPool.Count > 8) depthPool.RemoveAt(0);
                cascades.Clear();
            }

            /// <summary>Area of the 5-texel-wide tent (unit area) left of t.</summary>
            static float TentCdf(float t)
            {
                if (t <= -2.5f) return 0;
                if (t >= 2.5f) return 1;
                return t < 0 ? (2.5f + t) * (2.5f + t) / 12.5f : 1 - (2.5f - t) * (2.5f - t) / 12.5f;
            }

            /// <summary>1 = lit, 0 = in shadow.</summary>
            public float Sample(Vector3 P, Vector3 N, float nl, Vector3 camPos)
            {
                float camDist = (P - camPos).magnitude;
                if (camDist >= fadeEnd) return 1;
                Cascade cs = null;
                foreach (var c in cascades) if ((P - c.center).sqrMagnitude < c.r2) { cs = c; break; }
                if (cs == null) return 1;
                // normal offset + slope-scaled depth bias (Unity: bias / normal bias in shadow-texel units)
                float tan = Mathf.Min(8f, Mathf.Sqrt(Mathf.Max(0, 1 - nl * nl)) / Mathf.Max(nl, .05f));
                var lp = lightV.MultiplyPoint3x4(P + N * cs.normalBias);
                float sx = (lp.x - cs.minX) / cs.texel, sy = (lp.y - cs.minY) / cs.texel;
                float z = lp.z - cs.bias - cs.texel * .5f * tan;
                int res = cs.res; var depth = cs.depth;
                float lit;
                if (!soft)
                {
                    int x = (int)MathF.Floor(sx), y = (int)MathF.Floor(sy);
                    lit = x < 0 || y < 0 || x >= res || y >= res || z <= depth[y * res + x] ? 1 : 0;
                }
                else
                {
                    lit = 0;
                    int bx = (int)MathF.Floor(sx - 2.5f), by = (int)MathF.Floor(sy - 2.5f);
                    Span<float> wx = stackalloc float[6], wy = stackalloc float[6], cx = stackalloc float[7], cy = stackalloc float[7];
                    // texel i spans [bx + i, bx + i + 1]: its right edge is texel i + 1's left edge
                    for (int i = 0; i < 7; i++) { cx[i] = TentCdf(bx + i - sx); cy[i] = TentCdf(by + i - sy); }
                    for (int i = 0; i < 6; i++) { wx[i] = cx[i + 1] - cx[i]; wy[i] = cy[i + 1] - cy[i]; }
                    for (int j = 0; j < 6; j++)
                    {
                        if (wy[j] <= 0) continue;
                        int y = by + j;
                        for (int i = 0; i < 6; i++)
                        {
                            float w = wx[i] * wy[j];
                            if (w <= 0) continue;
                            int x = bx + i;
                            bool l = x < 0 || y < 0 || x >= res || y >= res || z <= depth[y * res + x];
                            if (l) lit += w;
                        }
                    }
                }
                if (camDist > fadeStart) lit = Mathf.Lerp(lit, 1, (camDist - fadeStart) / (fadeEnd - fadeStart));
                return lit;
            }
        }
    }
}
