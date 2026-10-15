// Native 3D components and scene settings used by the 3D builders (video 12, Bit3D): Light, MeshFilter, MeshRenderer,
// RenderSettings (stored per scene, written into the scene's RenderSettings document), GameObject.CreatePrimitive.
using System;
using System.Linq;
using MiniUnity;
using MiniUnity.Yaml;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public sealed partial class Light
    {
        public Light() { }
        public LightType type { get => (LightType)NInt("m_Type", 2); set => NSetI("m_Type", (int)value); }
        public Color color { get => NColor("m_Color"); set => NSetColor("m_Color", value); }
        public float intensity { get => NFloat("m_Intensity", 1); set => NSetF("m_Intensity", value); }
        public float range { get => NFloat("m_Range", 10); set => NSetF("m_Range", value); }
        public float spotAngle { get => NFloat("m_SpotAngle", 30); set => NSetF("m_SpotAngle", value); }
        public float innerSpotAngle { get => NFloat("m_InnerSpotAngle", 21.80208f); set => NSetF("m_InnerSpotAngle", value); }
        public LightShadows shadows { get => (LightShadows)NInt("m_Shadows.m_Type"); set => NSetI("m_Shadows.m_Type", (int)value); }
        public float shadowStrength { get => NFloat("m_Shadows.m_Strength", 1); set => NSetF("m_Shadows.m_Strength", value); }
        public float shadowBias { get => NFloat("m_Shadows.m_Bias", .05f); set => NSetF("m_Shadows.m_Bias", value); }
        public float shadowNormalBias { get => NFloat("m_Shadows.m_NormalBias", .4f); set => NSetF("m_Shadows.m_NormalBias", value); }
        public float shadowNearPlane { get => NFloat("m_Shadows.m_NearPlane", .2f); set => NSetF("m_Shadows.m_NearPlane", value); }
        public float bounceIntensity { get => NFloat("m_BounceIntensity", 1); set => NSetF("m_BounceIntensity", value); }
        public int cullingMask { get => (int)(uint)NLong("m_CullingMask.m_Bits", 0xFFFFFFFF); set => NSetI("m_CullingMask.m_Bits", (uint)value); }
        public LightRenderMode renderMode { get => (LightRenderMode)NInt("m_RenderMode"); set => NSetI("m_RenderMode", (int)value); }
        public float colorTemperature { get => NFloat("m_ColorTemperature", 6570); set => NSetF("m_ColorTemperature", value); }
        public bool useColorTemperature { get => NBool("m_UseColorTemperature"); set => NSetB("m_UseColorTemperature", value); }
    }

    public partial class Renderer
    {
        public bool enabled { get => NBool("m_Enabled", true); set => NSetB("m_Enabled", value); }
        public ShadowCastingMode shadowCastingMode { get => (ShadowCastingMode)NInt("m_CastShadows", 1); set => NSetI("m_CastShadows", (int)value); }
        public bool receiveShadows { get => NBool("m_ReceiveShadows", true); set => NSetB("m_ReceiveShadows", value); }
        public Material[] sharedMaterials
        {
            get => NGet("m_Materials") is YSeq s ? s.Items.Select(i => i is YRef r ? (r.Raw ? AssetLoader.ResolveExternal(r) : r.Target) as Material : null).ToArray() : Array.Empty<Material>();
            set { var s = new YSeq(); foreach (var m in value ?? Array.Empty<Material>()) s.Items.Add(new YRef(m)); NSet("m_Materials", s); }
        }
        public Material sharedMaterial { get => sharedMaterials.FirstOrDefault(); set => sharedMaterials = new[] { value }; }
        public Material material { get => sharedMaterial; set => sharedMaterial = value; }
        public Material[] materials { get => sharedMaterials; set => sharedMaterials = value; }
        public int sortingOrder { get => NInt("m_SortingOrder"); set => NSetI("m_SortingOrder", value); }
        public Bounds bounds
        {
            get
            {
                var mf = GetComponent<MeshFilter>();
                var m = mf != null ? mf.sharedMesh : null;
                var lb = m != null ? m.bounds : new Bounds(Vector3.zero, Vector3.one);
                var M = transform.localToWorldMatrix;
                var c = M.MultiplyPoint3x4(lb.center); var e = lb.extents;
                var ax = new Vector3(Mathf.Abs(M.m00) * e.x + Mathf.Abs(M.m01) * e.y + Mathf.Abs(M.m02) * e.z,
                                     Mathf.Abs(M.m10) * e.x + Mathf.Abs(M.m11) * e.y + Mathf.Abs(M.m12) * e.z,
                                     Mathf.Abs(M.m20) * e.x + Mathf.Abs(M.m21) * e.y + Mathf.Abs(M.m22) * e.z);
                return new Bounds(c, ax * 2);
            }
        }
    }

    public sealed partial class MeshRenderer { public MeshRenderer() { } }

    public sealed partial class MeshFilter
    {
        public MeshFilter() { }
        public Mesh sharedMesh { get => NRef<Mesh>("m_Mesh"); set => NSetRef("m_Mesh", value); }
        public Mesh mesh { get => sharedMesh; set => sharedMesh = value; }
    }

    public sealed partial class RenderSettings
    {
        static YMap S => SceneData.Active?.RenderSettings ?? SceneData.DefaultRenderSettings();
        static void Set(string k, YNode v) { if (SceneData.Active != null) { SceneData.Active.RenderSettings[k] = v; SceneData.Active.IsDirty = true; } }
        static float F(string k) => S[k] is YScalar s ? s.F : 0;
        static Color C(string k) => Serializer.ReadColor(S[k]);
        public static bool fog { get => F("m_Fog") != 0; set => Set("m_Fog", YScalar.Of(value)); }
        public static FogMode fogMode { get => (FogMode)(int)F("m_FogMode"); set => Set("m_FogMode", YScalar.Of((int)value)); }
        public static Color fogColor { get => C("m_FogColor"); set => Set("m_FogColor", Serializer.Col(value)); }
        public static float fogDensity { get => F("m_FogDensity"); set => Set("m_FogDensity", YScalar.Of(value)); }
        public static float fogStartDistance { get => F("m_LinearFogStart"); set => Set("m_LinearFogStart", YScalar.Of(value)); }
        public static float fogEndDistance { get => F("m_LinearFogEnd"); set => Set("m_LinearFogEnd", YScalar.Of(value)); }
        public static AmbientMode ambientMode { get => (AmbientMode)(int)F("m_AmbientMode"); set => Set("m_AmbientMode", YScalar.Of((int)value)); }
        public static Color ambientSkyColor { get => C("m_AmbientSkyColor"); set => Set("m_AmbientSkyColor", Serializer.Col(value)); }
        public static Color ambientEquatorColor { get => C("m_AmbientEquatorColor"); set => Set("m_AmbientEquatorColor", Serializer.Col(value)); }
        public static Color ambientGroundColor { get => C("m_AmbientGroundColor"); set => Set("m_AmbientGroundColor", Serializer.Col(value)); }
        public static Color ambientLight { get => ambientSkyColor; set => ambientSkyColor = value; }
        public static float ambientIntensity { get => F("m_AmbientIntensity"); set => Set("m_AmbientIntensity", YScalar.Of(value)); }
        public static Color subtractiveShadowColor { get => C("m_SubtractiveShadowColor"); set => Set("m_SubtractiveShadowColor", Serializer.Col(value)); }
        public static Material skybox
        {
            get => S["m_SkyboxMaterial"] is YRef r ? (r.Raw ? AssetLoader.ResolveExternal(r) : r.Target) as Material : null;
            set => Set("m_SkyboxMaterial", value == null ? new YRef(0, null, 0) : new YRef(value));
        }
        public static Light sun { get => S["m_Sun"] is YRef r ? r.Target as Light : null; set => Set("m_Sun", value == null ? new YRef(0, null, 0) : new YRef(value)); }
        public static float reflectionIntensity { get => F("m_ReflectionIntensity"); set => Set("m_ReflectionIntensity", YScalar.Of(value)); }
    }

    public partial class GameObject
    {
        /// <summary>Like Unity: GameObject with MeshFilter (built-in mesh), MeshRenderer (Default-Material) and a matching collider.</summary>
        public static GameObject CreatePrimitive(PrimitiveType type)
        {
            var go = new GameObject(type.ToString());
            long meshId = type switch
            {
                PrimitiveType.Sphere => 10207, PrimitiveType.Capsule => 10208, PrimitiveType.Cylinder => 10206,
                PrimitiveType.Cube => 10202, PrimitiveType.Plane => 10209, _ => 10210,
            };
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = BuiltinResources.PrimitiveMesh(type, meshId);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BuiltinResources.DefaultMaterial;
            switch (type)
            {
                case PrimitiveType.Sphere: go.AddComponent<SphereCollider>(); break;
                case PrimitiveType.Capsule: case PrimitiveType.Cylinder: go.AddComponent<CapsuleCollider>(); break;
                case PrimitiveType.Cube: go.AddComponent<BoxCollider>(); break;
                default: go.AddComponent<MeshCollider>(); break;
            }
            return go;
        }
    }
}
