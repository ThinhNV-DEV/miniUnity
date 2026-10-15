using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity;
using MiniUnity.Yaml;
using UnityEngine.Playables;

namespace UnityEngine
{
    public sealed partial class Canvas
    {
        public Canvas() { }
        public RenderMode renderMode { get => (RenderMode)NInt("m_RenderMode"); set => NSetI("m_RenderMode", (int)value); }
        public Camera worldCamera { get => NRef<Camera>("m_Camera"); set => NSetRef("m_Camera", value); }
        public float planeDistance { get => NFloat("m_PlaneDistance", 100); set => NSetF("m_PlaneDistance", value); }
        public bool pixelPerfect { get => NBool("m_PixelPerfect"); set => NSetB("m_PixelPerfect", value); }
        public bool overridePixelPerfect { get => NBool("m_OverridePixelPerfect"); set => NSetB("m_OverridePixelPerfect", value); }
        public bool overrideSorting { get => NBool("m_OverrideSorting"); set => NSetB("m_OverrideSorting", value); }
        public int sortingOrder { get => NInt("m_SortingOrder"); set => NSetI("m_SortingOrder", value); }
        public int targetDisplay { get => NInt("m_TargetDisplay"); set => NSetI("m_TargetDisplay", value); }
        public int sortingLayerID { get => NInt("m_SortingLayerID"); set => NSetI("m_SortingLayerID", value); }
        public string sortingLayerName { get => "Default"; set { } }
        public int cachedSortingLayerValue => 0;
        public bool vertexColorAlwaysGammaSpace { get => NBool("m_VertexColorAlwaysGammaSpace"); set => NSetB("m_VertexColorAlwaysGammaSpace", value); }
        public AdditionalCanvasShaderChannels additionalShaderChannels { get => (AdditionalCanvasShaderChannels)NInt("m_AdditionalShaderChannelsFlag"); set => NSetI("m_AdditionalShaderChannelsFlag", (int)value); }
        public StandaloneRenderResize updateRectTransformForStandalone { get => (StandaloneRenderResize)NInt("m_UpdateRectTransformForStandalone"); set => NSetI("m_UpdateRectTransformForStandalone", (int)value); }
        public float normalizedSortingGridSize { get => NFloat("m_SortingBucketNormalizedSize"); set => NSetF("m_SortingBucketNormalizedSize", value); }
        public int sortingGridNormalizedSize { get => (int)normalizedSortingGridSize; set => normalizedSortingGridSize = value; }
        internal float m_ScaleFactor = 1, m_RefPPU = 100;
        public float scaleFactor { get => m_ScaleFactor; set => m_ScaleFactor = value; }
        public float referencePixelsPerUnit { get => m_RefPPU; set => m_RefPPU = value; }
        public bool isRootCanvas { get { for (var t = transform.m_Parent; t != null; t = t.m_Parent) if (t.gameObject.GetComponent<Canvas>() is Canvas c && c.enabled) return false; return true; } }
        public Canvas rootCanvas { get { Canvas r = this; for (var t = transform.m_Parent; t != null; t = t.m_Parent) if (t.gameObject.GetComponent<Canvas>() is Canvas c && c.enabled) r = c; return r; } }
        public Rect pixelRect { get { var px = MiniUnity.Screen.PixelSize(this); return new Rect(0, 0, px.x, px.y); } }
        public int renderOrder => 0;
        public Vector2 renderingDisplaySize => MiniUnity.Screen.PixelSize(this);
        public static Material GetDefaultCanvasMaterial() => BuiltinResources.DefaultUIMaterial;
        public static Material GetDefaultCanvasTextMaterial() => BuiltinResources.DefaultUIMaterial;
        public static Material GetETC1SupportedCanvasMaterial() => BuiltinResources.DefaultUIMaterial;
        public static void ForceUpdateCanvases() { s_willRender?.Invoke(); }
        static WillRenderCanvases s_willRender, s_preWillRender;
        public static event WillRenderCanvases preWillRenderCanvases { add => s_preWillRender += value; remove => s_preWillRender -= value; }
        public static event WillRenderCanvases willRenderCanvases { add => s_willRender += value; remove => s_willRender -= value; }
    }

    public sealed partial class CanvasGroup
    {
        public CanvasGroup() { }
        public float alpha { get => NFloat("m_Alpha", 1); set => NSetF("m_Alpha", Mathf.Clamp01(value)); }
        public bool interactable { get => NBool("m_Interactable", true); set => NSetB("m_Interactable", value); }
        public bool blocksRaycasts { get => NBool("m_BlocksRaycasts", true); set => NSetB("m_BlocksRaycasts", value); }
        public bool ignoreParentGroups { get => NBool("m_IgnoreParentGroups"); set => NSetB("m_IgnoreParentGroups", value); }
        public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera) => blocksRaycasts;
    }

    public sealed partial class CanvasRenderer
    {
        public CanvasRenderer() { }
        public bool cullTransparentMesh { get => NBool("m_CullTransparentMesh", true); set => NSetB("m_CullTransparentMesh", value); }
        // runtime-only render state (not serialized) – what Unity's native canvas keeps per renderer
        Color m_Color = Color.white; float m_Alpha = 1; bool m_Cull; int m_MaterialCount, m_PopMaterialCount;
        readonly Dictionary<int, Material> m_Materials = new();
        readonly Dictionary<int, Material> m_PopMaterials = new();
        internal Texture m_Texture, m_AlphaTexture;
        internal bool m_RectClipping; internal Rect m_ClipRect;
        internal MiniUnity.MeshSnapshot m_Mesh;
        public bool cull { get => m_Cull; set => m_Cull = value; }
        public bool hasMoved => false;
        public bool hasPopInstruction { get; set; }
        public bool hasRectClipping => m_RectClipping;
        public int absoluteDepth => 0;
        public int relativeDepth => 0;
        public int materialCount { get => m_MaterialCount; set => m_MaterialCount = value; }
        public int popMaterialCount { get => m_PopMaterialCount; set => m_PopMaterialCount = value; }
        public Vector2 clippingSoftness { get; set; }
        public void SetColor(Color color) => m_Color = color;
        public Color GetColor() => m_Color;
        public void SetAlpha(float alpha) => m_Alpha = alpha;
        public float GetAlpha() => m_Alpha;
        public float GetInheritedAlpha() => m_Alpha * MiniUnity.UIRenderer.GroupAlpha(transform);
        public void EnableRectClipping(Rect rect) { m_RectClipping = true; m_ClipRect = rect; }
        public void DisableRectClipping() { m_RectClipping = false; }
        public void SetMaterial(Material material, int index) { m_Materials[index] = material; if (index >= m_MaterialCount) m_MaterialCount = index + 1; }
        public void SetMaterial(Material material, Texture texture) { m_MaterialCount = 1; m_Materials[0] = material; m_Texture = texture; }
        public Material GetMaterial(int index) => m_Materials.TryGetValue(index, out var m) ? m : null;
        public Material GetMaterial() => GetMaterial(0);
        public void SetPopMaterial(Material material, int index) { m_PopMaterials[index] = material; }
        public Material GetPopMaterial(int index) => m_PopMaterials.TryGetValue(index, out var m) ? m : null;
        public void SetTexture(Texture texture) { m_Texture = texture; }
        public void SetAlphaTexture(Texture texture) { m_AlphaTexture = texture; }
        public void SetMesh(Mesh mesh) { m_Mesh = mesh != null ? MiniUnity.MeshSnapshot.Of(mesh) : null; }
        public void Clear() { m_Mesh = null; }
        public Vector4 GetMeshBounds() => m_Mesh?.Bounds ?? default;
        public void SetVertices(List<UIVertex> vertices) => m_Mesh = MiniUnity.MeshSnapshot.OfQuads(vertices, vertices.Count);
        public void SetVertices(UIVertex[] vertices, int size) => m_Mesh = MiniUnity.MeshSnapshot.OfQuads(vertices, size);
        public static void SplitUIVertexStreams(List<UIVertex> verts, List<Vector3> positions, List<Color32> colors, List<Vector4> uv0S, List<Vector4> uv1S, List<Vector4> uv2S, List<Vector4> uv3S, List<Vector3> normals, List<Vector4> tangents, List<int> indices)
        {
            positions.Clear(); colors.Clear(); uv0S.Clear(); uv1S.Clear(); uv2S.Clear(); uv3S.Clear(); normals.Clear(); tangents.Clear(); indices.Clear();
            foreach (var v in verts) { positions.Add(v.position); colors.Add(v.color); uv0S.Add(v.uv0); uv1S.Add(v.uv1); uv2S.Add(v.uv2); uv3S.Add(v.uv3); normals.Add(v.normal); tangents.Add(v.tangent); }
            for (int i = 0; i < verts.Count; i++) indices.Add(i);
        }
        public static void CreateUIVertexStream(List<UIVertex> verts, List<Vector3> positions, List<Color32> colors, List<Vector4> uv0S, List<Vector4> uv1S, List<Vector4> uv2S, List<Vector4> uv3S, List<Vector3> normals, List<Vector4> tangents, List<int> indices)
        {
            verts.Clear();
            foreach (var i in indices)
                verts.Add(new UIVertex { position = positions[i], color = colors[i], uv0 = uv0S[i], uv1 = uv1S[i], uv2 = i < uv2S.Count ? uv2S[i] : default, uv3 = i < uv3S.Count ? uv3S[i] : default, normal = normals[i], tangent = tangents[i] });
        }
        public static void AddUIVertexStream(List<UIVertex> verts, List<Vector3> positions, List<Color32> colors, List<Vector4> uv0S, List<Vector4> uv1S, List<Vector4> uv2S, List<Vector4> uv3S, List<Vector3> normals, List<Vector4> tangents)
        {
            for (int i = 0; i < verts.Count; i++) { var v = verts[i]; positions.Add(v.position); colors.Add(v.color); uv0S.Add(v.uv0); uv1S.Add(v.uv1); uv2S.Add(v.uv2); uv3S.Add(v.uv3); normals.Add(v.normal); tangents.Add(v.tangent); }
        }
    }

    public partial class Camera
    {
        public Camera() { }
        public bool orthographic { get => NBool("orthographic"); set => NSetB("orthographic", value); }
        public float orthographicSize { get => NFloat("orthographic size", 5); set => NSetF("orthographic size", value); }
        public float fieldOfView { get => NFloat("field of view", 60); set => NSetF("field of view", value); }
        public float nearClipPlane { get => NFloat("near clip plane", 0.3f); set => NSetF("near clip plane", value); }
        public float farClipPlane { get => NFloat("far clip plane", 1000); set => NSetF("far clip plane", value); }
        public CameraClearFlags clearFlags { get => (CameraClearFlags)NInt("m_ClearFlags", 1); set => NSetI("m_ClearFlags", (int)value); }
        public Color backgroundColor { get => NColor("m_BackGroundColor"); set => NSetColor("m_BackGroundColor", value); }
        public int cullingMask { get => (int)(uint)NLong("m_CullingMask.m_Bits", 0xFFFFFFFF); set => NSetI("m_CullingMask.m_Bits", (uint)value); }
        public float depth { get => NFloat("m_Depth"); set => NSetF("m_Depth", value); }
        public bool usePhysicalProperties { get => NInt("m_projectionMatrixMode", 1) == 2; set => NSetI("m_projectionMatrixMode", value ? 2 : 1); }
        public Vector2 sensorSize { get => Serializer.ReadV2(NGet("m_SensorSize")); set => NSetVec2("m_SensorSize", value); }
        public Vector2 lensShift { get => Serializer.ReadV2(NGet("m_LensShift")); set => NSetVec2("m_LensShift", value); }
        public Camera.GateFitMode gateFit { get => (Camera.GateFitMode)NInt("m_GateFitMode", 2); set => NSetI("m_GateFitMode", (int)value); }
        public float focalLength { get => NFloat("m_FocalLength", 50); set => NSetF("m_FocalLength", value); }
        public float focusDistance { get => NFloat("m_FocusDistance", 10); set => NSetF("m_FocusDistance", value); }
        public int iso { get => NInt("m_Iso", 200); set => NSetI("m_Iso", value); }
        public float shutterSpeed { get => NFloat("m_ShutterSpeed", .005f); set => NSetF("m_ShutterSpeed", value); }
        public float aperture { get => NFloat("m_Aperture", 16); set => NSetF("m_Aperture", value); }
        public int bladeCount { get => NInt("m_BladeCount", 5); set => NSetI("m_BladeCount", value); }
        public Vector2 curvature { get => Serializer.ReadV2(NGet("m_Curvature")); set => NSetVec2("m_Curvature", value); }
        public float barrelClipping { get => NFloat("m_BarrelClipping", .25f); set => NSetF("m_BarrelClipping", value); }
        public float anamorphism { get => NFloat("m_Anamorphism"); set => NSetF("m_Anamorphism", value); }
        public CameraType cameraType { get => CameraType.Game; set { } }
        public Vector3 velocity => Vector3.zero;
        public bool allowHDR { get => NBool("m_HDR", true); set => NSetB("m_HDR", value); }
        public bool allowMSAA { get => NBool("m_AllowMSAA", true); set => NSetB("m_AllowMSAA", value); }
        public int targetDisplay { get => NInt("m_TargetDisplay"); set => NSetI("m_TargetDisplay", value); }
        public RenderTexture targetTexture { get => NRef<RenderTexture>("m_TargetTexture"); set => NSetRef("m_TargetTexture", value); }
        public Rect rect { get { var n = NGet("m_NormalizedViewPortRect") as YMap; return new Rect(Serializer.F(n?["x"]), Serializer.F(n?["y"]), Serializer.F(n?["width"]), Serializer.F(n?["height"])); } set { } }
        public int pixelWidth => targetTexture != null ? targetTexture.width : (int)MiniUnity.Screen.Width;
        public int pixelHeight => targetTexture != null ? targetTexture.height : (int)MiniUnity.Screen.Height;
        public float aspect { get => pixelWidth / (float)pixelHeight; set { } }
        public static Camera main => ObjectRegistry.SceneObjects(typeof(Camera), false).Cast<Camera>().FirstOrDefault(c => c.gameObject.CompareTag("MainCamera"));
        public static Camera current => null;
        public static int allCamerasCount => ObjectRegistry.SceneObjects(typeof(Camera), false).Count();
        public static Camera[] allCameras => ObjectRegistry.SceneObjects(typeof(Camera), false).Cast<Camera>().ToArray();
        /// <summary>View matrix like Unity's: camera space looks down −Z (OpenGL convention).</summary>
        public Matrix4x4 worldToCameraMatrix
        {
            get => Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse;
            set { }
        }
        public Matrix4x4 cameraToWorldMatrix => worldToCameraMatrix.inverse;
        public Matrix4x4 projectionMatrix
        {
            get
            {
                if (orthographic) { float h = orthographicSize, w = h * aspect; return Matrix4x4.Ortho(-w, w, -h, h, nearClipPlane, farClipPlane); }
                return Matrix4x4.Perspective(fieldOfView, aspect, nearClipPlane, farClipPlane);
            }
            set { }
        }
        // ---- screen / viewport / world conversions (full-screen viewport; z = distance along the camera's forward axis)
        float Depth(Vector3 world) => Vector3.Dot(world - transform.position, transform.forward);
        public Vector3 WorldToViewportPoint(Vector3 p)
        {
            var v = projectionMatrix.MultiplyPoint(worldToCameraMatrix.MultiplyPoint(p));
            return new Vector3((v.x + 1) * 0.5f, (v.y + 1) * 0.5f, Depth(p));
        }
        public Vector3 WorldToScreenPoint(Vector3 p) => ViewportToScreenPoint(WorldToViewportPoint(p));
        public Vector3 ViewportToScreenPoint(Vector3 v) => new Vector3(v.x * pixelWidth, v.y * pixelHeight, v.z);
        public Vector3 ScreenToViewportPoint(Vector3 s) => new Vector3(s.x / pixelWidth, s.y / pixelHeight, s.z);
        public Vector3 ViewportToWorldPoint(Vector3 v)
        {
            float x = v.x * 2 - 1, y = v.y * 2 - 1, z = v.z;
            Vector3 local;
            if (orthographic) local = new Vector3(x * orthographicSize * aspect, y * orthographicSize, z);
            else
            {
                float t = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
                local = new Vector3(x * t * aspect * z, y * t * z, z);
            }
            return transform.position + transform.rotation * local;
        }
        public Vector3 ScreenToWorldPoint(Vector3 s) => ViewportToWorldPoint(ScreenToViewportPoint(s));
        public Ray ViewportPointToRay(Vector3 v)
        {
            var o = ViewportToWorldPoint(new Vector3(v.x, v.y, nearClipPlane));
            var d = orthographic ? transform.forward : (ViewportToWorldPoint(new Vector3(v.x, v.y, nearClipPlane + 1)) - o).normalized;
            return new Ray(o, d);
        }
        public Ray ScreenPointToRay(Vector3 s) => ViewportPointToRay(ScreenToViewportPoint(s));
        /// <summary>Software render of this camera (UI canvases; see MiniUnity.UIRenderer) into targetTexture.</summary>
        public void Render() => MiniUnity.UIRenderer.RenderCamera(this);
    }

}

namespace UnityEngine.Playables
{
    public partial class PlayableDirector
    {
        public PlayableDirector() { }
        public PlayableAsset playableAsset { get => NRef<PlayableAsset>("m_PlayableAsset"); set => NSetRef("m_PlayableAsset", value); }
        public bool playOnAwake { get => NBool("m_InitialState", true); set => NSetB("m_InitialState", value); }
        public DirectorWrapMode extrapolationMode { get => (DirectorWrapMode)NInt("m_WrapMode", 2); set => NSetI("m_WrapMode", (int)value); }
        public DirectorUpdateMode timeUpdateMode { get => (DirectorUpdateMode)NInt("m_DirectorUpdateMode", 1); set => NSetI("m_DirectorUpdateMode", (int)value); }
        public double initialTime { get => NGet("m_InitialTime") is YScalar s ? s.D : 0; set => NSetD("m_InitialTime", value); }
        double m_Time;
        public double time { get => m_Time; set => m_Time = value; }
        public double duration => playableAsset != null ? playableAsset.duration : 0;
        internal bool m_Playing;
        public PlayState state => m_Playing ? PlayState.Playing : PlayState.Paused;
        PlayableGraph m_Graph; Playable m_Root; PlayableAsset m_GraphAsset;
        public PlayableGraph playableGraph => m_Graph;

        YSeq Bindings
        {
            get
            {
                if (NGet("m_SceneBindings") is YSeq s) return s;
                var n = new YSeq(); NSet("m_SceneBindings", n); return n;
            }
        }
        static Object Target(YNode n) => n is YRef r ? (r.Raw ? AssetLoader.ResolveExternal(r) : r.Target) : null;

        public void SetGenericBinding(Object key, Object value)
        {
            foreach (var it in Bindings.Items)
                if (it is YMap m && Target(m["key"]) == key && key != null) { m["value"] = new YRef(value); return; }
            Bindings.Items.Add(new YMap().Add("key", new YRef(key)).Add("value", new YRef(value)));
        }
        public Object GetGenericBinding(Object key)
        {
            foreach (var it in Bindings.Items)
                if (it is YMap m && Target(m["key"]) == key) return Target(m["value"]);
            return null;
        }
        public void ClearGenericBinding(Object key) => Bindings.Items.RemoveAll(it => it is YMap m && Target(m["key"]) == key);
        /// <summary>Edit-mode evaluation like Unity's: (re)build the graph from playableAsset, bind outputs, set the root time, evaluate once.</summary>
        public void Evaluate()
        {
            var asset = playableAsset;
            if (asset == null) return;
            if (!m_Graph.IsValid() || m_GraphAsset != asset) RebuildGraph();
            if (!m_Root.IsValid()) return;
            RebindPlayableGraphOutputs();
            double t = m_Time;
            double dur = duration;
            if (extrapolationMode == DirectorWrapMode.Hold && t > dur) t = dur;
            else if (extrapolationMode == DirectorWrapMode.Loop && dur > 0 && t > dur) t %= dur;
            m_Root.SetTime(t);
            m_Graph.Evaluate();
        }
        public void DeferredEvaluate() => Evaluate();
        /// <summary>Play mode: the director advances every frame (MiniUnity.Play → DirectorSystem) and evaluates its graph.</summary>
        public void Play()
        {
            m_Playing = true;
            if (!m_Graph.IsValid() || m_GraphAsset != playableAsset) RebuildGraph();
            Evaluate();
            played?.Invoke(this);
        }
        public void Play(PlayableAsset asset) { playableAsset = asset; Play(); }
        internal void Play(FrameRate frameRate) => Play();
        public void Play(PlayableAsset asset, DirectorWrapMode mode) { playableAsset = asset; extrapolationMode = mode; Play(); }
        public void Stop()
        {
            bool was = m_Playing;
            m_Playing = false; m_Time = initialTime;
            if (m_Graph.IsValid()) m_Graph.Destroy(); m_Graph = default; m_Root = Playable.Null; m_GraphAsset = null;
            if (was) stopped?.Invoke(this);
        }
        public void Pause() { if (m_Playing) { m_Playing = false; paused?.Invoke(this); } }
        public void Resume() { if (!m_Playing) { m_Playing = true; played?.Invoke(this); } }

        /// <summary>One player-loop frame of a playing director (GameTime / UnscaledGameTime; Manual directors are driven by script).</summary>
        internal void PlayerLoopTick(float scaledDt, float unscaledDt)
        {
            if (!m_Playing) return;
            var mode = timeUpdateMode;
            if (mode == DirectorUpdateMode.Manual) return;
            m_Time += mode == DirectorUpdateMode.UnscaledGameTime ? unscaledDt : scaledDt;
            double dur = duration;
            if (m_Time > dur)
            {
                switch (extrapolationMode)
                {
                    case DirectorWrapMode.None: Evaluate(); Stop(); return;
                    case DirectorWrapMode.Loop: if (dur > 0) m_Time %= dur; break;
                }
            }
            Evaluate();
        }
        public void RebuildGraph()
        {
            if (m_Graph.IsValid()) m_Graph.Destroy();
            m_GraphAsset = playableAsset;
            m_Root = Playable.Null;
            if (m_GraphAsset == null) { m_Graph = default; return; }
            m_Graph = PlayableGraph.Create(name);
            if (MiniUnity.PlayableRuntime.G(m_Graph) is { } g) g.owner = this;
            m_Graph.SetResolver(this);
            m_Root = m_GraphAsset.CreatePlayable(m_Graph, gameObject);
        }
        public void RebindPlayableGraphOutputs()
        {
            if (!m_Graph.IsValid()) return;
            for (int i = 0; i < m_Graph.GetOutputCount(); i++)
            {
                var o = m_Graph.GetOutput(i);
                var key = o.GetReferenceObject();
                if (key == null) continue;
                var target = GetGenericBinding(key);
                if (target != null || o.GetUserData() == null || o.GetUserData() is not PlayableDirector) o.SetUserData(target);
            }
        }
        YSeq ExposedRefs
        {
            get
            {
                if (NGet("m_ExposedReferences.m_References") is YSeq s) return s;
                var n = new YSeq(); NSet("m_ExposedReferences.m_References", n); return n;
            }
        }
        public void ClearReferenceValue(PropertyName id) => ExposedRefs.Items.RemoveAll(it => it is YMap m && (m["first"] as YScalar)?.Value == id.ToString());
        public void SetReferenceValue(PropertyName id, Object value)
        {
            string k = id.ToString();
            foreach (var it in ExposedRefs.Items)
                if (it is YMap m && (m["first"] as YScalar)?.Value == k) { m["second"] = new YRef(value); return; }
            ExposedRefs.Items.Add(new YMap().Add("first", new YScalar(k)).Add("second", new YRef(value)));
        }
        public Object GetReferenceValue(PropertyName id, out bool idValid)
        {
            string k = id.ToString();
            foreach (var it in ExposedRefs.Items)
                if (it is YMap m && (m["first"] as YScalar)?.Value == k) { idValid = true; return Target(m["second"]); }
            idValid = false; return null;
        }
        public event Action<PlayableDirector> played;
        public event Action<PlayableDirector> paused;
        public event Action<PlayableDirector> stopped;
    }
}
