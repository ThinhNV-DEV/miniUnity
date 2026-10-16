// Software renderer for uGUI canvases, following Unity's native canvas: every CanvasRenderer keeps the mesh, materials,
// texture and rect clipping that uGUI / TextMeshPro hand it; a camera draws the canvases bound to it in hierarchy order
// (nested override-sorting canvases by sortingOrder) with ports of the shaders those materials use (UI/Default,
// TextMeshPro/Mobile/Distance Field), stencil (Mask), CanvasGroup alpha and an sRGB 8-bit target blended in linear
// space (the project uses the Linear colour space).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MiniUnity
{
    public sealed class MeshSnapshot
    {
        public Vector3[] pos;
        public Color[] col;          // as authored (gamma)
        public Vector4[] uv0, uv1;
        public Vector3[] nrm;
        public readonly List<int[]> sub = new();
        public Vector4 Bounds;

        public static MeshSnapshot Of(Mesh m)
        {
            var s = new MeshSnapshot { pos = (Vector3[])m.m_V.Clone(), col = m.m_C != null ? (Color[])m.m_C.Clone() : null, uv0 = m.m_UV[0]?.Clone() as Vector4[], uv1 = m.m_UV[1]?.Clone() as Vector4[], nrm = m.m_N?.Clone() as Vector3[] };
            for (int i = 0; i < m.subMeshCount; i++) s.sub.Add(m.GetTopology(i) == MeshTopology.Triangles ? (int[])m.m_Sub[i].Clone() : Array.Empty<int>());
            return s;
        }

        public static MeshSnapshot OfQuads(IList<UIVertex> v, int n)
        {
            var s = new MeshSnapshot { pos = new Vector3[n], col = new Color[n], uv0 = new Vector4[n], uv1 = new Vector4[n], nrm = new Vector3[n] };
            for (int i = 0; i < n; i++) { s.pos[i] = v[i].position; s.col[i] = v[i].color; s.uv0[i] = v[i].uv0; s.uv1[i] = v[i].uv1; s.nrm[i] = v[i].normal; }
            var idx = new List<int>();
            for (int q = 0; q + 3 < n; q += 4) idx.AddRange(new[] { q, q + 1, q + 2, q + 2, q + 3, q });
            s.sub.Add(idx.ToArray());
            return s;
        }
    }

    public static class UIRenderer
    {
        static RenderTexture s_Screen;
        /// <summary>Stand-in for the game view when a camera has no target texture.</summary>
        public static RenderTexture ScreenBuffer => s_Screen ??= new RenderTexture((int)Screen.Width, (int)Screen.Height, 24);
        public static bool QuantizeVertexColors = Environment.GetEnvironmentVariable("MINIUNITY_VCOLOR8") != "0";   // native canvas stores linear vertex colours as 8-bit
        public static int DrawCalls, Pixels;

        /// <summary>Product of the CanvasGroup alphas above (and on) t, stopping at ignoreParentGroups – what the native canvas applies.</summary>
        public static float GroupAlpha(Transform t)
        {
            float a = 1;
            for (var p = t; p != null; p = p.m_Parent)
            {
                var g = p.gameObject.GetComponent<CanvasGroup>();
                if (g != null && g.enabled) { a *= g.alpha; if (g.ignoreParentGroups) break; }
                if (p.gameObject.GetComponent<Canvas>() is Canvas c && c.enabled && c.isRootCanvas) break;
            }
            return a;
        }

        // ------------------------------------------------------------------ camera
        public static void RenderCamera(Camera cam)
        {
            var target = cam.targetTexture ?? ScreenBuffer;
            target.EnsureBuffers();
            int W = target.SurfW, H = target.SurfH;
            var clear = cam.clearFlags;
            if (clear == CameraClearFlags.Skybox && WorldRenderer.SkyFor(cam) is Sky sky)
            {
                DrawSky(cam, target, W, H, sky);
                Array.Clear(target.m_SurfStencil); Array.Fill(target.m_SurfDepth, 1f);
                clear = CameraClearFlags.Nothing;
            }
            switch (clear)
            {
                case CameraClearFlags.Skybox:   // no skybox material MiniUnity can draw: the background colour, like Unity without one
                case CameraClearFlags.SolidColor:
                {
                    var bg = cam.backgroundColor;
                    byte r = Enc(bg.r.Lin()), g = Enc(bg.g.Lin()), b = Enc(bg.b.Lin()), a = (byte)Mathf.Clamp(Mathf.RoundToInt(bg.a * 255), 0, 255);
                    if (!target.m_SRGB) { r = B(bg.r.Lin()); g = B(bg.g.Lin()); b = B(bg.b.Lin()); }
                    var c = target.m_SurfColor;
                    for (int i = 0; i < c.Length; i += 4) { c[i] = r; c[i + 1] = g; c[i + 2] = b; c[i + 3] = a; }
                    Array.Clear(target.m_SurfStencil); Array.Fill(target.m_SurfDepth, 1f);
                    break;
                }
                case CameraClearFlags.Depth: Array.Clear(target.m_SurfStencil); Array.Fill(target.m_SurfDepth, 1f); break;
            }

            // MSAA intermediate (Msaa.cs): both samples start from what the target holds after the clear
            if (Msaa.CameraSamples(cam, target) > 1) target.BeginMultisample();

            // driven canvas transforms + graphic rebuild + RectMask2D clipping (Unity does this before rendering)
            Canvas.ForceUpdateCanvases();

            var canvases = ObjectRegistry.SceneObjects(typeof(Canvas), false).Cast<Canvas>()
                .Where(c => c.enabled && c.gameObject.activeInHierarchy && c.isRootCanvas)
                .Where(c => (c.renderMode == RenderMode.ScreenSpaceCamera && c.worldCamera == cam) ||
                            (c.renderMode == RenderMode.WorldSpace && (cam.cullingMask & (1 << c.gameObject.layer)) != 0))
                .OrderBy(c => c.sortingOrder).ThenByDescending(c => c.planeDistance).ToList();

            var V = Matrix4x4.Scale(new Vector3(1, 1, -1)) * cam.transform.worldToLocalMatrix;
            var P = cam.projectionMatrix;
            var view = new View { W = W, H = H, P = P, VP = P * V, camPos = cam.transform.position, target = target };
            if (Environment.GetEnvironmentVariable("MINIUNITY_RENDER_DEBUG") == "1")
                foreach (var c in canvases)
                    Console.Error.WriteLine($"[Render] cam {cam.name} ortho={cam.orthographic} size={cam.orthographicSize} fov={cam.fieldOfView} pos={cam.transform.position} → canvas {c.name} scaleFactor={c.scaleFactor} rect={((RectTransform)c.transform).rect} lossy={c.transform.lossyScale} pos={c.transform.position} target {W}x{H}");
            WorldRenderer.Render(cam, view);
            // transparent queue: sprites and camera canvases sorted together like Unity (sorting layer, order in layer,
            // then back to front by distance along the view axis)
            var queue = new List<(int layer, int order, float dist, int seq, Renderer sprite, Canvas canvas)>();
            int seq = 0;
            var fwd = cam.transform.forward;
            foreach (var sr in ObjectRegistry.SceneObjects(typeof(SpriteRenderer), false).Cast<SpriteRenderer>())
            {
                if (!sr.enabled || !sr.gameObject.activeInHierarchy || (cam.cullingMask & (1 << sr.gameObject.layer)) == 0 || sr.sprite == null) continue;
                float dist = cam.orthographic ? Vector3.Dot(sr.transform.position - cam.transform.position, fwd) : (sr.transform.position - cam.transform.position).magnitude;
                queue.Add((sr.NInt("m_SortingLayer"), sr.sortingOrder, dist, seq++, sr, null));
            }
            foreach (var tr in ObjectRegistry.SceneObjects(typeof(UnityEngine.Tilemaps.TilemapRenderer), false).Cast<UnityEngine.Tilemaps.TilemapRenderer>())
            {
                if (!tr.enabled || !tr.gameObject.activeInHierarchy || (cam.cullingMask & (1 << tr.gameObject.layer)) == 0 || tr.GetComponent<UnityEngine.Tilemaps.Tilemap>() == null) continue;
                float dist = cam.orthographic ? Vector3.Dot(tr.transform.position - cam.transform.position, fwd) : (tr.transform.position - cam.transform.position).magnitude;
                queue.Add((tr.NInt("m_SortingLayer"), tr.sortingOrder, dist, seq++, tr, null));
            }
            foreach (var pr in ObjectRegistry.SceneObjects(typeof(ParticleSystemRenderer), false).Cast<ParticleSystemRenderer>())
            {
                if (!pr.enabled || !pr.gameObject.activeInHierarchy || (cam.cullingMask & (1 << pr.gameObject.layer)) == 0) continue;
                var ps = pr.GetComponent<ParticleSystem>();
                if (ps == null || ps.particleCount == 0) continue;
                float dist = cam.orthographic ? Vector3.Dot(pr.transform.position - cam.transform.position, fwd) : (pr.transform.position - cam.transform.position).magnitude;
                queue.Add((pr.NInt("m_SortingLayer"), pr.sortingOrder, dist + pr.sortingFudge, seq++, pr, null));
            }
            foreach (var c in canvases)
                queue.Add((c.NInt("m_SortingLayer"), c.sortingOrder, c.renderMode == RenderMode.ScreenSpaceCamera ? c.planeDistance : Vector3.Dot(c.transform.position - cam.transform.position, fwd), seq++, null, c));
            queue.Sort((a, b) => a.layer != b.layer ? a.layer.CompareTo(b.layer) : a.order != b.order ? a.order.CompareTo(b.order)
                : a.dist != b.dist ? b.dist.CompareTo(a.dist) : a.seq.CompareTo(b.seq));
            foreach (var q in queue)
                if (q.sprite is SpriteRenderer sr) DrawSprite(sr, view);
                else if (q.sprite is UnityEngine.Tilemaps.TilemapRenderer tmr) DrawTilemap(tmr, view);
                else if (q.sprite is ParticleSystemRenderer psr) DrawParticles(psr, cam, view);
                else DrawCanvas(q.canvas, view);
            target.EndMultisample();
            target.Resolve();
            target.m_RVersion++;
            ImageEffects.RunOnRenderImage(cam, target);
        }

        internal sealed class View
        {
            public int W, H; public Matrix4x4 P, VP; public Vector3 camPos; public RenderTexture target;
        }

        struct Item { public CanvasRenderer cr; public Matrix4x4 local; public float alpha; public int order, seq; }

        static void DrawCanvas(Canvas canvas, View view)
        {
            var items = new List<Item>();
            int seq = 0;
            void Walk(Transform t, Matrix4x4 m, float alpha, int order, bool root)
            {
                var go = t.gameObject;
                if (!go.activeSelf) return;
                if (!root)
                {
                    var lp = t is RectTransform rt ? rt.ComputeLocalPosition() : t.localPosition;
                    m = m * Matrix4x4.TRS(lp, t.EffRotation, t.EffScale);
                    var nested = go.GetComponent<Canvas>();
                    if (nested != null && nested.enabled && nested.overrideSorting) order = nested.sortingOrder;
                }
                var g = go.GetComponent<CanvasGroup>();
                if (g != null && g.enabled) alpha = g.ignoreParentGroups ? g.alpha : alpha * g.alpha;
                var cr = go.GetComponent<CanvasRenderer>();
                if (cr != null && cr.m_Mesh != null && !cr.cull) items.Add(new Item { cr = cr, local = m, alpha = alpha, order = order, seq = seq++ });
                for (int i = 0; i < t.m_Children.Count; i++) Walk(t.m_Children[i], m, alpha, order, false);
            }
            Walk(canvas.transform, Matrix4x4.identity, 1, canvas.sortingOrder, true);
            items.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : a.seq.CompareTo(b.seq));

            var model = canvas.transform.localToWorldMatrix;
            bool gammaVC = canvas.vertexColorAlwaysGammaSpace;
            foreach (var it in items) DrawRenderer(it, model, view, gammaVC);
        }

        // ------------------------------------------------------------------ SpriteRenderer (Sprites/Default)
        static void DrawSprite(SpriteRenderer sr, View view)
        {
            var sp = sr.sprite; var tex = sp.m_Texture;
            if (tex == null) return;
            float ppu = Math.Max(1e-4f, sp.m_PPU);
            float x0 = -sp.m_Pivot.x / ppu, y0 = -sp.m_Pivot.y / ppu, x1 = x0 + sp.m_Rect.width / ppu, y1 = y0 + sp.m_Rect.height / ppu;
            if (sr.drawMode != SpriteDrawMode.Simple)
            {
                // sliced / tiled drawn stretched to size (pivot kept)
                var size = sr.size; float px = sp.m_Pivot.x / Math.Max(1, sp.m_Rect.width), py = sp.m_Pivot.y / Math.Max(1, sp.m_Rect.height);
                x0 = -px * size.x; x1 = x0 + size.x; y0 = -py * size.y; y1 = y0 + size.y;
            }
            if (sr.flipX) { (x0, x1) = (-x1, -x0); }
            if (sr.flipY) { (y0, y1) = (-y1, -y0); }
            float tw = Math.Max(1, tex.width), th = Math.Max(1, tex.height);
            float u0 = sp.m_Rect.xMin / tw, u1 = sp.m_Rect.xMax / tw, v0 = sp.m_Rect.yMin / th, v1 = sp.m_Rect.yMax / th;
            if (sr.flipX) (u0, u1) = (u1, u0);
            if (sr.flipY) (v0, v1) = (v1, v0);
            var c = sr.color;
            var mesh = new MeshSnapshot
            {
                pos = new[] { new Vector3(x0, y0, 0), new Vector3(x0, y1, 0), new Vector3(x1, y1, 0), new Vector3(x1, y0, 0) },
                col = new[] { c, c, c, c },
                uv0 = new[] { new Vector4(u0, v0, 0, 0), new Vector4(u0, v1, 0, 0), new Vector4(u1, v1, 0, 0), new Vector4(u1, v0, 0, 0) },
                uv1 = new Vector4[4], nrm = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
            };
            var idx = new[] { 0, 1, 2, 2, 3, 0 };
            mesh.sub.Add(idx);
            var mat = sr.sharedMaterial;
            var sh = new UIDefault(mat, tex) { view = view, model = sr.transform.localToWorldMatrix, local = Matrix4x4.identity };
            sh.SetupFrame();
            DrawCalls++;
            Raster(view, sh, mesh, idx, Matrix4x4.identity, 1, Color.white, false, mat);
        }

        // ------------------------------------------------------------------ ParticleSystemRenderer (camera-facing quads)
        static void DrawParticles(ParticleSystemRenderer pr, Camera cam, View view)
        {
            var ps = pr.GetComponent<ParticleSystem>();
            var list = ps.S.particles;
            if (list.Count == 0) return;
            var mat = pr.sharedMaterial; var tex = mat != null && mat.HasProperty("_MainTex") ? mat.mainTexture : null;
            bool local = ps.S.simulationSpace == ParticleSystemSimulationSpace.Local;
            var M = ps.transform.localToWorldMatrix;
            var right = cam.transform.right; var up = cam.transform.up;
            var mode = pr.renderMode;
            if (mode == ParticleSystemRenderMode.HorizontalBillboard) { right = Vector3.right; up = Vector3.forward; }
            else if (mode == ParticleSystemRenderMode.VerticalBillboard) { up = Vector3.up; right = Vector3.Cross(Vector3.up, cam.transform.forward).normalized; if (right.sqrMagnitude < 1e-8f) right = Vector3.right; right = -right; }
            float maxSize = pr.maxParticleSize, minSize = pr.minParticleSize;
            int n = list.Count;
            var pos = new Vector3[n * 4]; var col = new Color[n * 4]; var uv = new Vector4[n * 4]; var nrm = new Vector3[n * 4]; var idx = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                var p = list[i];
                var c = local ? M.MultiplyPoint3x4(p.position) : p.position;
                var size = ps.CurrentSize3D(p);
                // maxParticleSize / minParticleSize: fractions of the viewport height at the particle's distance
                float viewH = cam.orthographic ? 2 * cam.orthographicSize : 2 * Mathf.Max(0.01f, Vector3.Dot(c - cam.transform.position, cam.transform.forward)) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float s = Mathf.Max(size.x, size.y);
                if (maxSize > 0 && s > maxSize * viewH) size *= maxSize * viewH / s;
                if (minSize > 0 && s < minSize * viewH && s > 0) size *= minSize * viewH / s;
                float rot = -p.rotation * Mathf.Deg2Rad, cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
                var rx = (right * cs + up * sn) * (size.x * 0.5f); var uy = (-right * sn + up * cs) * (size.y * 0.5f);
                Color pc = ps.CurrentColor(p);
                int v = i * 4;
                pos[v] = c - rx - uy; pos[v + 1] = c - rx + uy; pos[v + 2] = c + rx + uy; pos[v + 3] = c + rx - uy;
                uv[v] = new Vector4(0, 0, 0, 0); uv[v + 1] = new Vector4(0, 1, 0, 0); uv[v + 2] = new Vector4(1, 1, 0, 0); uv[v + 3] = new Vector4(1, 0, 0, 0);
                for (int k = 0; k < 4; k++) { col[v + k] = pc; nrm[v + k] = -cam.transform.forward; }
                int o = i * 6; idx[o] = v; idx[o + 1] = v + 1; idx[o + 2] = v + 2; idx[o + 3] = v + 2; idx[o + 4] = v + 3; idx[o + 5] = v;
            }
            var mesh = new MeshSnapshot { pos = pos, col = col, uv0 = uv, uv1 = new Vector4[n * 4], nrm = nrm };
            mesh.sub.Add(idx);
            var sh = new UIDefault(mat, tex) { view = view, model = Matrix4x4.identity, local = Matrix4x4.identity };
            sh.SetupFrame();
            DrawCalls++;
            Raster(view, sh, mesh, idx, Matrix4x4.identity, 1, Color.white, false, mat);
        }

        // ------------------------------------------------------------------ TilemapRenderer (every tile's sprite, Sprites/Default)
        static void DrawTilemap(UnityEngine.Tilemaps.TilemapRenderer tr, View view)
        {
            var map = tr.GetComponent<UnityEngine.Tilemaps.Tilemap>();
            var tint = map.color;
            var mat = tr.sharedMaterial;
            foreach (var (pos, cell) in tr.TilesInOrder(map))
            {
                var sp = cell.sprite; var tex = sp?.m_Texture;
                if (tex == null) continue;
                float ppu = Math.Max(1e-4f, sp.m_PPU);
                // the sprite's pivot sits on the tile anchor (cell centre by default)
                float x0 = -sp.m_Pivot.x / ppu, y0 = -sp.m_Pivot.y / ppu, x1 = x0 + sp.m_Rect.width / ppu, y1 = y0 + sp.m_Rect.height / ppu;
                float tw = Math.Max(1, tex.width), th = Math.Max(1, tex.height);
                float u0 = sp.m_Rect.xMin / tw, u1 = sp.m_Rect.xMax / tw, v0 = sp.m_Rect.yMin / th, v1 = sp.m_Rect.yMax / th;
                var c = tint * cell.color;
                var mesh = new MeshSnapshot
                {
                    pos = new[] { new Vector3(x0, y0, 0), new Vector3(x0, y1, 0), new Vector3(x1, y1, 0), new Vector3(x1, y0, 0) },
                    col = new[] { c, c, c, c },
                    uv0 = new[] { new Vector4(u0, v0, 0, 0), new Vector4(u0, v1, 0, 0), new Vector4(u1, v1, 0, 0), new Vector4(u1, v0, 0, 0) },
                    uv1 = new Vector4[4], nrm = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
                };
                var idx = new[] { 0, 1, 2, 2, 3, 0 };
                mesh.sub.Add(idx);
                var sh = new UIDefault(mat, tex) { view = view, model = map.TileMatrix(pos, cell), local = Matrix4x4.identity };
                sh.SetupFrame();
                DrawCalls++;
                Raster(view, sh, mesh, idx, Matrix4x4.identity, 1, Color.white, false, mat);
            }
        }

        // ------------------------------------------------------------------ one CanvasRenderer
        static readonly Dictionary<Shader, string> s_ShaderNames = new();
        public static string ShaderName(Shader s)
        {
            if (s == null) return "UI/Default";
            if (s_ShaderNames.TryGetValue(s, out var n)) return n;
            n = s.m_ShaderName;
            if (string.IsNullOrEmpty(n) && s.m_Asset?.Path is string p && p.EndsWith(".shader"))
            {
                try
                {
                    var line = File.ReadLines(AssetIndex.Abs(p)).FirstOrDefault(l => l.TrimStart().TrimStart('﻿').StartsWith("Shader "));
                    if (line != null) { int a = line.IndexOf('"'), b = line.IndexOf('"', a + 1); n = line.Substring(a + 1, b - a - 1); }
                }
                catch { }
            }
            if (string.IsNullOrEmpty(n)) n = s.name;
            return s_ShaderNames[s] = n;
        }

        static readonly HashSet<string> s_Warned = new();

        static void DrawRenderer(Item it, Matrix4x4 model, View view, bool gammaVC)
        {
            var cr = it.cr; var mesh = cr.m_Mesh;
            if (mesh.pos.Length == 0) return;
            float inherited = cr.GetAlpha() * it.alpha;
            Color crc = cr.GetColor();
            if (inherited <= 0) return;
            int nmat = Math.Max(1, cr.materialCount);
            for (int sm = 0; sm < mesh.sub.Count && sm < nmat; sm++)
            {
                var idx = mesh.sub[sm];
                if (idx.Length < 3) continue;
                var mat = cr.GetMaterial(sm) ?? Canvas.GetDefaultCanvasMaterial();
                string sname = ShaderName(mat.shader);
                var tex = sm == 0 && cr.m_Texture != null ? cr.m_Texture : (mat.HasProperty("_MainTex") ? mat.mainTexture : null) ?? cr.m_Texture;
                Shading sh = IsTmpSdf(sname)
                    ? new TmpSdfMobile(mat, tex)
                    : new UIDefault(mat, tex);
                if (sh is UIDefault && sname != "UI/Default" && sname != "Sprites/Default" && s_Warned.Add(sname))
                    Debug.LogWarning($"[Render] shader '{sname}' drawn as UI/Default");
                sh.view = view; sh.model = model; sh.local = it.local;
                sh.clip = cr.hasRectClipping; sh.clipRect = cr.m_ClipRect; sh.softness = cr.clippingSoftness;
                sh.SetupFrame();
                DrawCalls++;
                Raster(view, sh, mesh, idx, it.local, inherited, crc, gammaVC, mat);
            }
        }

        internal static bool IsTmpSdf(string shaderName) => shaderName.StartsWith("TextMeshPro/Mobile/Distance Field") || shaderName.StartsWith("TextMeshPro/Distance Field");

        /// <summary>World-space TextMeshPro: the mesh drawn with the SDF text shader in the 3D pass (ZTest LEqual against the scene,
        /// ZWrite Off, Cull Off). TMP already writes linear vertex colours for non-UI text in a linear project.</summary>
        internal static void DrawWorldText(Mesh mesh, int sub, Material mat, Matrix4x4 M, View view)
        {
            var snap = MeshSnapshot.Of(mesh);
            if (snap.pos.Length == 0 || sub >= snap.sub.Count || snap.sub[sub].Length < 3) return;
            var tex = mat.HasProperty("_MainTex") ? mat.mainTexture : null;
            var sh = new TmpSdfMobile(mat, tex) { view = view, model = M, local = Matrix4x4.identity };
            sh.SetupFrame();
            DrawCalls++;
            Raster(view, sh, snap, snap.sub[sub], Matrix4x4.identity, 1, Color.white, true, mat, depthTest: true);
        }

        // ------------------------------------------------------------------ rasterizer
        static void Raster(View view, Shading sh, MeshSnapshot mesh, int[] idx, Matrix4x4 local, float inherited, Color crc, bool gammaVC, Material mat, bool depthTest = false)
        {
            int n = mesh.pos.Length, nv = sh.Varyings;
            var clipPos = new Vector4[n];
            var vary = new float[n * nv];
            var tmp = new float[nv];
            var mvp = view.VP * sh.model;
            for (int i = 0; i < n; i++)
            {
                var p = local.MultiplyPoint3x4(mesh.pos[i]);          // canvas space (object space of the batch)
                var c = mesh.col != null && i < mesh.col.Length ? mesh.col[i] : Color.white;
                c = new Color(c.r * crc.r, c.g * crc.g, c.b * crc.b, c.a * crc.a * inherited);
                if (!gammaVC)
                {
                    c = new Color(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), c.a);
                    if (QuantizeVertexColors) c = (Color)(Color32)c;
                }
                var nn = mesh.nrm != null && i < mesh.nrm.Length ? local.MultiplyVector(mesh.nrm[i]) : new Vector3(0, 0, -1);
                var vin = new VertexIn
                {
                    pos = p, color = c, uv0 = mesh.uv0 != null && i < mesh.uv0.Length ? mesh.uv0[i] : default,
                    uv1 = mesh.uv1 != null && i < mesh.uv1.Length ? mesh.uv1[i] : default, normal = nn,
                };
                clipPos[i] = mvp * new Vector4(p.x, p.y, p.z, 1);
                sh.Vertex(vin, clipPos[i], tmp);
                Array.Copy(tmp, 0, vary, i * nv, nv);
            }

            var st = new Stencil(mat);
            int W = view.W, H = view.H;
            int ns = view.target.m_MS;
            var colors = new[] { view.target.SampleColor(0), ns > 1 ? view.target.SampleColor(1) : null };
            var stens = new[] { view.target.SampleStencil(0), ns > 1 ? view.target.SampleStencil(1) : null };
            var depths = new[] { view.target.SampleDepth(0), ns > 1 ? view.target.SampleDepth(1) : null };
            bool srgb = view.target.m_SRGB;
            int cm = mat != null && mat.HasProperty("_ColorMask") ? (int)mat.GetFloat("_ColorMask") : 15;
            // triangle setup once; then the pixels, in bands of rows: every band walks all triangles in order, so each pixel still
            // sees the same writes in the same order (blending, stencil) while bands run in parallel
            var tris = new List<UiTri>(idx.Length / 3);
            long work = 0;
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                int i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
                if (i0 >= n || i1 >= n || i2 >= n) continue;
                var c0 = clipPos[i0]; var c1 = clipPos[i1]; var c2 = clipPos[i2];
                if (c0.w <= 1e-6f || c1.w <= 1e-6f || c2.w <= 1e-6f) continue;
                var T = new UiTri();
                T.sx0 = (c0.x / c0.w * 0.5f + 0.5f) * W; T.sy0 = (c0.y / c0.w * 0.5f + 0.5f) * H; T.iw0 = 1 / c0.w;
                T.sx1 = (c1.x / c1.w * 0.5f + 0.5f) * W; T.sy1 = (c1.y / c1.w * 0.5f + 0.5f) * H; T.iw1 = 1 / c1.w;
                T.sx2 = (c2.x / c2.w * 0.5f + 0.5f) * W; T.sy2 = (c2.y / c2.w * 0.5f + 0.5f) * H; T.iw2 = 1 / c2.w;
                T.z0 = c0.z / c0.w * .5f + .5f; T.z1 = c1.z / c1.w * .5f + .5f; T.z2 = c2.z / c2.w * .5f + .5f;
                float area = (T.sx1 - T.sx0) * (T.sy2 - T.sy0) - (T.sx2 - T.sx0) * (T.sy1 - T.sy0);
                if (MathF.Abs(area) < 1e-8f) continue;
                T.minX = Math.Max(0, (int)MathF.Floor(MathF.Min(T.sx0, MathF.Min(T.sx1, T.sx2)))); T.maxX = Math.Min(W - 1, (int)MathF.Ceiling(MathF.Max(T.sx0, MathF.Max(T.sx1, T.sx2))));
                T.minY = Math.Max(0, (int)MathF.Floor(MathF.Min(T.sy0, MathF.Min(T.sy1, T.sy2)))); T.maxY = Math.Min(H - 1, (int)MathF.Ceiling(MathF.Max(T.sy0, MathF.Max(T.sy1, T.sy2))));
                if (T.minX > T.maxX || T.minY > T.maxY) continue;
                T.ia = 1 / area;
                // top-left rule
                bool TopLeft(float ax, float ay, float bx, float by) => area > 0 ? (ay == by && bx < ax) || by < ay : (ay == by && bx > ax) || by > ay;
                T.tl0 = TopLeft(T.sx1, T.sy1, T.sx2, T.sy2); T.tl1 = TopLeft(T.sx2, T.sy2, T.sx0, T.sy0); T.tl2 = TopLeft(T.sx0, T.sy0, T.sx1, T.sy1);
                T.o0 = i0 * nv; T.o1 = i1 * nv; T.o2 = i2 * nv;
                tris.Add(T);
                work += (long)(T.maxX - T.minX + 1) * (T.maxY - T.minY + 1);
            }
            if (tris.Count == 0) return;
            int yLo = int.MaxValue, yHi = int.MinValue;
            foreach (var T in tris) { yLo = Math.Min(yLo, T.minY); yHi = Math.Max(yHi, T.maxY); }

            void Band(int y0, int y1)
            {
                var vv = new float[nv];
                int pixels = 0;
                foreach (var T in tris)
                {
                    int ya = Math.Max(T.minY, y0), yb = Math.Min(T.maxY, y1);
                    for (int y = ya; y <= yb; y++)
                    {
                        float py = y + 0.5f;
                        for (int x = T.minX; x <= T.maxX; x++)
                        {
                            float px = x + 0.5f;
                            float w0 = ((T.sx2 - T.sx1) * (py - T.sy1) - (T.sy2 - T.sy1) * (px - T.sx1)) * T.ia;
                            float w1 = ((T.sx0 - T.sx2) * (py - T.sy2) - (T.sy0 - T.sy2) * (px - T.sx2)) * T.ia;
                            float w2 = 1 - w0 - w1;
                            int mask;
                            if (ns == 1)
                            {
                                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                                if ((w0 == 0 && !T.tl0) || (w1 == 0 && !T.tl1) || (w2 == 0 && !T.tl2)) continue;
                                mask = 1;
                            }
                            else if ((mask = Msaa.Coverage(T.sx0, T.sy0, T.sx1, T.sy1, T.sx2, T.sy2, T.ia, T.tl0, T.tl1, T.tl2, x, y, ns)) == 0) continue;
                            int pi = y * W + x;
                            int pass = 0;
                            for (int smp = 0; smp < ns; smp++)
                            {
                                if ((mask & (1 << smp)) == 0) continue;
                                if (depthTest)
                                {
                                    float z = ns == 1 ? w0 * T.z0 + w1 * T.z1 + w2 * T.z2 : Msaa.DepthAt(T.sx0, T.sy0, T.sx1, T.sy1, T.sx2, T.sy2, T.ia, T.z0, T.z1, T.z2, x, y, smp, ns);
                                    if (z > depths[smp][pi] || z < 0) continue;
                                }
                                if (st.test && !st.Pass(stens[smp][pi])) continue;
                                pass |= 1 << smp;
                            }
                            if (pass == 0) continue;
                            // perspective-correct interpolation (at the pixel centre, also for partly covered MSAA pixels)
                            float p0 = w0 * T.iw0, p1 = w1 * T.iw1, p2 = w2 * T.iw2, ps = 1 / (p0 + p1 + p2);
                            p0 *= ps; p1 *= ps; p2 *= ps;
                            for (int k = 0; k < nv; k++) vv[k] = vary[T.o0 + k] * p0 + vary[T.o1 + k] * p1 + vary[T.o2 + k] * p2;
                            if (!sh.Fragment(vv, out float fr, out float fg, out float fb, out float fa)) continue;
                            if (cm != 0) pixels++;
                            float ia2 = 1 - fa;
                            for (int smp = 0; smp < ns; smp++)
                            {
                                if ((pass & (1 << smp)) == 0) continue;
                                var sten = stens[smp]; var color = colors[smp];
                                if (st.write) sten[pi] = st.Apply(sten[pi]);
                                if (cm == 0) continue;
                                int o = pi * 4;
                                if ((cm & 8) != 0) color[o] = Blend(color[o], fr, ia2, srgb);
                                if ((cm & 4) != 0) color[o + 1] = Blend(color[o + 1], fg, ia2, srgb);
                                if ((cm & 2) != 0) color[o + 2] = Blend(color[o + 2], fb, ia2, srgb);
                                if ((cm & 1) != 0) color[o + 3] = B(fa + color[o + 3] / 255f * ia2);
                            }
                        }
                    }
                }
                System.Threading.Interlocked.Add(ref Pixels, pixels);
            }
            int rows = yHi - yLo + 1, bands = Math.Min(rows / 8, Environment.ProcessorCount * 4);
            if (work < ParallelPixels || bands < 2) { Band(yLo, yHi); return; }
            System.Threading.Tasks.Parallel.For(0, bands, b => Band(yLo + rows * b / bands, yLo + rows * (b + 1) / bands - 1));
        }

        /// <summary>Draws covering fewer pixels than this (sum of triangle bounding boxes) are rasterized on one thread.</summary>
        public static long ParallelPixels = 32768;

        struct UiTri
        {
            public float sx0, sx1, sx2, sy0, sy1, sy2, iw0, iw1, iw2, ia, z0, z1, z2;
            public int minX, maxX, minY, maxY, o0, o1, o2;
            public bool tl0, tl1, tl2;
        }

        static byte Blend(byte dst, float src, float oneMinusSrcA, bool srgb)
        {
            if (srgb) return Enc(src + TextureData.SrgbToLinear[dst] * oneMinusSrcA);
            return B(src + dst / 255f * oneMinusSrcA);
        }
        /// <summary>Skybox clear: every pixel gets the sky's radiance along its view ray (through the far plane), sun disk included.</summary>
        static void DrawSky(Camera cam, RenderTexture target, int W, int H, Sky sky)
        {
            var V = Matrix4x4.Scale(new Vector3(1, 1, -1)) * cam.transform.worldToLocalMatrix;
            var inv = (cam.projectionMatrix * V).inverse;
            var camPos = cam.transform.position;
            var c = target.m_SurfColor; bool srgb = target.m_SRGB;
            bool ortho = cam.orthographic; var fwd = cam.transform.forward;
            System.Threading.Tasks.Parallel.For(0, H, y =>
            {
                float ny = (y + .5f) / H * 2 - 1;
                for (int x = 0; x < W; x++)
                {
                    float nx = (x + .5f) / W * 2 - 1;
                    var p = inv * new Vector4(nx, ny, 1, 1);
                    var d = (new Vector3(p.x, p.y, p.z) / p.w - camPos).normalized;
                    if (ortho) d = fwd;   // the sky sphere seen through an orthographic projection: the forward direction
                    var col = sky.Radiance(d, true);
                    int o = (y * W + x) * 4;
                    if (srgb) { c[o] = Enc(col.r); c[o + 1] = Enc(col.g); c[o + 2] = Enc(col.b); }
                    else { c[o] = B(col.r); c[o + 1] = B(col.g); c[o + 2] = B(col.b); }
                    c[o + 3] = 255;
                }
            });
        }

        static byte Enc(float lin) => TextureData.LinearToSrgbByte(lin);
        static byte B(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
        static float Lin(this float g) => Mathf.GammaToLinearSpace(g);

        // ------------------------------------------------------------------ stencil
        struct Stencil
        {
            public bool test, write;
            int refv, comp, op, rmask, wmask;
            public Stencil(Material m)
            {
                refv = (int)G(m, "_Stencil", 0); comp = (int)G(m, "_StencilComp", 8); op = (int)G(m, "_StencilOp", 0);
                rmask = (int)G(m, "_StencilReadMask", 255); wmask = (int)G(m, "_StencilWriteMask", 255);
                test = comp != 8 && comp != 0; write = op != 0 && wmask != 0;
            }
            static float G(Material m, string n, float d) => m != null && m.HasProperty(n) ? m.GetFloat(n) : d;
            public bool Pass(byte s)
            {
                int a = refv & rmask, b = s & rmask;
                return comp switch { 1 => false, 2 => a < b, 3 => a == b, 4 => a <= b, 5 => a > b, 6 => a != b, 7 => a >= b, _ => true };
            }
            public byte Apply(byte s)
            {
                int v = op switch { 1 => 0, 2 => refv, 3 => Math.Min(255, s + 1), 4 => Math.Max(0, s - 1), 5 => ~s & 255, 6 => (s + 1) & 255, 7 => (s - 1) & 255, _ => s };
                return (byte)((s & ~wmask) | (v & wmask));
            }
        }

        // ------------------------------------------------------------------ shaders
        internal struct VertexIn { public Vector3 pos, normal; public Color color; public Vector4 uv0, uv1; }

        internal abstract class Shading
        {
            internal View view; public Matrix4x4 model, local; public bool clip; public Rect clipRect; public Vector2 softness;
            protected TexSampler tex;
            protected static Color MatColor(Material m, string n, Color d) => m != null && m.HasProperty(n) ? m.GetColor(n).linear : d;
            protected static float F(Material m, string n, float d) => m != null && m.HasProperty(n) ? m.GetFloat(n) : d;
            protected Vector4 ClipRect4 => clip ? new Vector4(clipRect.xMin, clipRect.yMin, clipRect.xMax, clipRect.yMax) : new Vector4(-32767, -32767, 32767, 32767);
            public abstract int Varyings { get; }
            public virtual void SetupFrame() { }
            public abstract void Vertex(in VertexIn v, Vector4 clipPos, float[] o);
            public abstract bool Fragment(float[] v, out float r, out float g, out float b, out float a);
            /// <summary>float2 pixelSize = w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy))</summary>
            protected Vector2 PixelSize(float w, float sx = 1, float sy = 1)
            {
                var P = view.P;
                float px = MathF.Abs(sx * (P.m00 * view.W + P.m01 * view.H)), py = MathF.Abs(sy * (P.m10 * view.W + P.m11 * view.H));
                return new Vector2(w / px, w / py);
            }
            protected static float Sat(float x) => x < 0 ? 0 : x > 1 ? 1 : x;
        }

        /// <summary>UI/Default (2022.3 builtin source).</summary>
        sealed class UIDefault : Shading
        {
            readonly Color tint; readonly Vector4 add; readonly bool alphaClip;
            Vector4 cr;
            public UIDefault(Material m, Texture t)
            {
                tint = MatColor(m, "_Color", Color.white);
                tex = TextureData.Sampler(t ?? Texture2D.whiteTexture);
                bool alpha8 = t is Texture2D t2 && t2.format == TextureFormat.Alpha8;
                add = alpha8 ? new Vector4(1, 1, 1, 0) : Vector4.zero;
                alphaClip = m != null && (m.IsKeywordEnabled("UNITY_UI_ALPHACLIP") || F(m, "_UseUIAlphaClip", 0) > 0.5f);
            }
            public override int Varyings => 10;
            public override void SetupFrame() { cr = ClipRect4; }
            public override void Vertex(in VertexIn v, Vector4 cp, float[] o)
            {
                var ps = PixelSize(cp.w);
                var c = new Vector4(Math.Clamp(cr.x, -2e10f, 2e10f), Math.Clamp(cr.y, -2e10f, 2e10f), Math.Clamp(cr.z, -2e10f, 2e10f), Math.Clamp(cr.w, -2e10f, 2e10f));
                o[0] = v.color.r * tint.r; o[1] = v.color.g * tint.g; o[2] = v.color.b * tint.b; o[3] = v.color.a * tint.a;
                o[4] = v.uv0.x; o[5] = v.uv0.y;
                o[6] = v.pos.x * 2 - c.x - c.z; o[7] = v.pos.y * 2 - c.y - c.w;
                o[8] = 0.25f / (0.25f * softness.x + MathF.Abs(ps.x)); o[9] = 0.25f / (0.25f * softness.y + MathF.Abs(ps.y));
            }
            public override bool Fragment(float[] v, out float r, out float g, out float b, out float a)
            {
                float ca = MathF.Round(v[3] * 255f) / 255f;
                tex.Sample(v[4], v[5], out float tr, out float tg, out float tb, out float ta);
                r = v[0] * (tr + add.x); g = v[1] * (tg + add.y); b = v[2] * (tb + add.z); a = ca * (ta + add.w);
                if (clip)
                {
                    float mx = Sat((cr.z - cr.x - MathF.Abs(v[6])) * v[8]), my = Sat((cr.w - cr.y - MathF.Abs(v[7])) * v[9]);
                    a *= mx * my;
                }
                if (alphaClip && a - 0.001f < 0) { r = g = b = 0; return false; }
                r *= a; g *= a; b *= a;
                return true;
            }
        }

        /// <summary>TextMeshPro/Mobile/Distance Field (TMP_SDF-Mobile.shader in the project).</summary>
        sealed class TmpSdfMobile : Shading
        {
            readonly Color face, outlineC, underlayC;
            readonly float faceDilate, outlineW, outlineSoft, ulX, ulY, ulDilate, ulSoft, wN, wB, srA, srC, texW, texH, grad, scX, scY, persp, sharp, vox, voy, mskX, mskY;
            readonly bool outlineOn, underlayOn, underlayInner, alphaClip;
            Vector4 cr;
            public TmpSdfMobile(Material m, Texture t)
            {
                tex = TextureData.Sampler(t ?? Texture2D.whiteTexture);
                face = MatColor(m, "_FaceColor", Color.white); outlineC = MatColor(m, "_OutlineColor", Color.black); underlayC = MatColor(m, "_UnderlayColor", new Color(0, 0, 0, .5f));
                faceDilate = F(m, "_FaceDilate", 0); outlineW = F(m, "_OutlineWidth", 0); outlineSoft = F(m, "_OutlineSoftness", 0);
                ulX = F(m, "_UnderlayOffsetX", 0); ulY = F(m, "_UnderlayOffsetY", 0); ulDilate = F(m, "_UnderlayDilate", 0); ulSoft = F(m, "_UnderlaySoftness", 0);
                wN = F(m, "_WeightNormal", 0); wB = F(m, "_WeightBold", .5f); srA = F(m, "_ScaleRatioA", 1); srC = F(m, "_ScaleRatioC", 1);
                texW = F(m, "_TextureWidth", 512); texH = F(m, "_TextureHeight", 512); grad = F(m, "_GradientScale", 5);
                scX = F(m, "_ScaleX", 1); scY = F(m, "_ScaleY", 1); persp = F(m, "_PerspectiveFilter", .875f); sharp = F(m, "_Sharpness", 0);
                vox = F(m, "_VertexOffsetX", 0); voy = F(m, "_VertexOffsetY", 0); mskX = F(m, "_MaskSoftnessX", 0); mskY = F(m, "_MaskSoftnessY", 0);
                outlineOn = m != null && m.IsKeywordEnabled("OUTLINE_ON");
                underlayOn = m != null && m.IsKeywordEnabled("UNDERLAY_ON");
                underlayInner = m != null && m.IsKeywordEnabled("UNDERLAY_INNER");
                alphaClip = m != null && m.IsKeywordEnabled("UNITY_UI_ALPHACLIP");
            }
            public override int Varyings => 26;
            public override void SetupFrame() { cr = ClipRect4; }
            public override void Vertex(in VertexIn v, Vector4 vPos, float[] o)
            {
                float bold = v.uv1.y <= 0 ? 1 : 0;   // step(texcoord1.y, 0)
                var vert = new Vector3(v.pos.x + vox, v.pos.y + voy, v.pos.z);
                if (vox != 0 || voy != 0) vPos = view.VP * model * new Vector4(vert.x, vert.y, vert.z, 1);
                var pixelSize = PixelSize(vPos.w, scX, scY);
                float scale = 1f / MathF.Sqrt(pixelSize.x * pixelSize.x + pixelSize.y * pixelSize.y);
                scale *= MathF.Abs(v.uv1.y) * grad * (sharp + 1);
                if (view.P.m33 == 0)
                {
                    // perspective: lerp(abs(scale) * (1 - _PerspectiveFilter), scale, abs(dot(worldNormal, normalize(WorldSpaceViewDir(vert)))))
                    var inv = model.inverse;
                    var wn = new Vector3(v.normal.x * inv.m00 + v.normal.y * inv.m10 + v.normal.z * inv.m20,
                                         v.normal.x * inv.m01 + v.normal.y * inv.m11 + v.normal.z * inv.m21,
                                         v.normal.x * inv.m02 + v.normal.y * inv.m12 + v.normal.z * inv.m22).normalized;
                    var wp = model.MultiplyPoint3x4(vert);
                    var vd = (view.camPos - wp).normalized;
                    float k = MathF.Abs(Vector3.Dot(wn, vd));
                    scale = Mathf.Lerp(MathF.Abs(scale) * (1 - persp), scale, k);
                }
                float weight = Mathf.Lerp(wN, wB, bold) / 4f;
                weight = (weight + faceDilate) * srA * 0.5f;
                float layerScale = scale;
                scale /= 1 + (outlineSoft * srA * scale);
                float bias = (0.5f - weight) * scale - 0.5f;
                float outline = outlineW * srA * 0.5f * scale;
                float opacity = underlayOn || underlayInner ? 1f : v.color.a;
                var fc = new Color(v.color.r * face.r, v.color.g * face.g, v.color.b * face.b, opacity * face.a);
                fc.r *= fc.a; fc.g *= fc.a; fc.b *= fc.a;
                var oc = outlineC; oc.a *= opacity; oc.r *= oc.a; oc.g *= oc.a; oc.b *= oc.a;
                oc = Color.Lerp(fc, oc, MathF.Sqrt(MathF.Min(1, outline * 2)));
                float layerBias = 0; Vector2 layerOffset = default;
                if (underlayOn || underlayInner)
                {
                    layerScale /= 1 + ((ulSoft * srC) * layerScale);
                    layerBias = (.5f - weight) * layerScale - .5f - ((ulDilate * srC) * .5f * layerScale);
                    layerOffset = new Vector2(-(ulX * srC) * grad / texW, -(ulY * srC) * grad / texH);
                }
                var c = new Vector4(Math.Clamp(cr.x, -2e10f, 2e10f), Math.Clamp(cr.y, -2e10f, 2e10f), Math.Clamp(cr.z, -2e10f, 2e10f), Math.Clamp(cr.w, -2e10f, 2e10f));
                o[0] = fc.r; o[1] = fc.g; o[2] = fc.b; o[3] = fc.a;
                o[4] = oc.r; o[5] = oc.g; o[6] = oc.b; o[7] = oc.a;
                o[8] = v.uv0.x; o[9] = v.uv0.y; o[10] = 0; o[11] = 0;
                o[12] = scale; o[13] = bias - outline; o[14] = bias + outline; o[15] = bias;
                o[16] = vert.x * 2 - c.x - c.z; o[17] = vert.y * 2 - c.y - c.w;
                o[18] = 0.25f / (0.25f * mskX + pixelSize.x); o[19] = 0.25f / (0.25f * mskY + pixelSize.y);
                o[20] = v.uv0.x + layerOffset.x; o[21] = v.uv0.y + layerOffset.y; o[22] = v.color.a; o[23] = 0;
                o[24] = layerScale; o[25] = layerBias;
            }
            public override bool Fragment(float[] v, out float r, out float g, out float b, out float a)
            {
                float d = tex.SampleA(v[8], v[9]) * v[12];
                float k = Sat(d - v[15]);
                r = v[0] * k; g = v[1] * k; b = v[2] * k; a = v[3] * k;
                if (outlineOn)
                {
                    float t = Sat(d - v[14]);
                    r = v[4] + (v[0] - v[4]) * t; g = v[5] + (v[1] - v[5]) * t; b = v[6] + (v[2] - v[6]) * t; a = v[7] + (v[3] - v[7]) * t;
                    float s = Sat(d - v[13]); r *= s; g *= s; b *= s; a *= s;
                }
                if (underlayOn)
                {
                    float d2 = tex.SampleA(v[20], v[21]) * v[24];
                    float u = Sat(d2 - v[25]) * (1 - a);
                    r += underlayC.r * underlayC.a * u; g += underlayC.g * underlayC.a * u; b += underlayC.b * underlayC.a * u; a += underlayC.a * u;
                }
                if (underlayInner)
                {
                    float sd = Sat(d - v[14]);
                    float d2 = tex.SampleA(v[20], v[21]) * v[24];
                    float u = (1 - Sat(d2 - v[25])) * sd * (1 - a);
                    r += underlayC.r * underlayC.a * u; g += underlayC.g * underlayC.a * u; b += underlayC.b * underlayC.a * u; a += underlayC.a * u;
                }
                if (clip)
                {
                    float mx = Sat((cr.z - cr.x - MathF.Abs(v[16])) * v[18]), my = Sat((cr.w - cr.y - MathF.Abs(v[17])) * v[19]);
                    float m = mx * my; r *= m; g *= m; b *= m; a *= m;
                }
                if (underlayOn || underlayInner) { r *= v[22]; g *= v[22]; b *= v[22]; a *= v[22]; }
                if (alphaClip && a - 0.001f < 0) return false;
                return true;
            }
        }
    }
}
