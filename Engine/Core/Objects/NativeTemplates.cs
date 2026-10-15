using System;
using System.Collections.Generic;
using MiniUnity.Yaml;
using UnityEngine;

namespace MiniUnity
{
    /// <summary>Default serialized bodies (Unity 2022.3 layouts) for engine classes created from code.</summary>
    public static class NativeTemplates
    {
        static readonly Dictionary<Type, YMap> cache = new();

        const string Curve1to0 = @"
    serializedVersion: 2
    m_Curve:
    - serializedVersion: 3
      time: 0
      value: 1
      inSlope: 0
      outSlope: 0
      tangentMode: 0
      weightedMode: 0
      inWeight: 0.33333334
      outWeight: 0.33333334
    - serializedVersion: 3
      time: 1
      value: 0
      inSlope: 0
      outSlope: 0
      tangentMode: 0
      weightedMode: 0
      inWeight: 0.33333334
      outWeight: 0.33333334
    m_PreInfinity: 2
    m_PostInfinity: 2
    m_RotationOrder: 4";
        static string Curve1(string v) => $@"
    serializedVersion: 2
    m_Curve:
    - serializedVersion: 3
      time: 0
      value: {v}
      inSlope: 0
      outSlope: 0
      tangentMode: 0
      weightedMode: 0
      inWeight: 0.33333334
      outWeight: 0.33333334
    m_PreInfinity: 2
    m_PostInfinity: 2
    m_RotationOrder: 4";

        static readonly Dictionary<Type, string> texts = new()
        {
            [typeof(Camera)] = @"
serializedVersion: 2
m_ClearFlags: 1
m_BackGroundColor: {r: 0.19215687, g: 0.3019608, b: 0.4745098, a: 0}
m_projectionMatrixMode: 1
m_GateFitMode: 2
m_FOVAxisMode: 0
m_Iso: 200
m_ShutterSpeed: 0.005
m_Aperture: 16
m_FocusDistance: 10
m_FocalLength: 50
m_BladeCount: 5
m_Curvature: {x: 2, y: 11}
m_BarrelClipping: 0.25
m_Anamorphism: 0
m_SensorSize: {x: 36, y: 24}
m_LensShift: {x: 0, y: 0}
m_NormalizedViewPortRect:
  serializedVersion: 2
  x: 0
  y: 0
  width: 1
  height: 1
near clip plane: 0.3
far clip plane: 1000
field of view: 60
orthographic: 0
orthographic size: 5
m_Depth: 0
m_CullingMask:
  serializedVersion: 2
  m_Bits: 4294967295
m_RenderingPath: -1
m_TargetTexture: {fileID: 0}
m_TargetDisplay: 0
m_TargetEye: 3
m_HDR: 1
m_AllowMSAA: 1
m_AllowDynamicResolution: 0
m_ForceIntoRT: 0
m_OcclusionCulling: 1
m_StereoConvergence: 10
m_StereoSeparation: 0.022
",
            [typeof(AudioListener)] = "\n",
            [typeof(AudioSource)] = @"
serializedVersion: 4
OutputAudioMixerGroup: {fileID: 0}
m_audioClip: {fileID: 0}
m_PlayOnAwake: 1
m_Volume: 1
m_Pitch: 1
Loop: 0
Mute: 0
Spatialize: 0
SpatializePostEffects: 0
Priority: 128
DopplerLevel: 1
MinDistance: 1
MaxDistance: 500
Pan2D: 0
rolloffMode: 0
BypassEffects: 0
BypassListenerEffects: 0
BypassReverbZones: 0
rolloffCustomCurve:" + Curve1to0.Replace("\n    ", "\n  ") + @"
panLevelCustomCurve:" + Curve1("0").Replace("\n    ", "\n  ") + @"
spreadCustomCurve:" + Curve1("0").Replace("\n    ", "\n  ") + @"
reverbZoneMixCustomCurve:" + Curve1("1").Replace("\n    ", "\n  ") + "\n",
            [typeof(Canvas)] = @"
serializedVersion: 3
m_RenderMode: 0
m_Camera: {fileID: 0}
m_PlaneDistance: 100
m_PixelPerfect: 0
m_ReceivesEvents: 1
m_OverrideSorting: 0
m_OverridePixelPerfect: 0
m_SortingBucketNormalizedSize: 0
m_VertexColorAlwaysGammaSpace: 0
m_AdditionalShaderChannelsFlag: 0
m_UpdateRectTransformForStandalone: 0
m_SortingLayerID: 0
m_SortingOrder: 0
m_TargetDisplay: 0
",
            [typeof(CanvasRenderer)] = "\nm_CullTransparentMesh: 1\n",
            [typeof(CanvasGroup)] = "\nm_Alpha: 1\nm_Interactable: 1\nm_BlocksRaycasts: 1\nm_IgnoreParentGroups: 0\n",
            [typeof(UnityEngine.Playables.PlayableDirector)] = @"
serializedVersion: 3
m_PlayableAsset: {fileID: 0}
m_InitialState: 1
m_WrapMode: 2
m_DirectorUpdateMode: 1
m_InitialTime: 0
m_SceneBindings: []
m_ExposedReferences:
  m_References: []
",
            [typeof(Light)] = @"
serializedVersion: 10
m_Type: 2
m_Shape: 0
m_Color: {r: 1, g: 0.95686275, b: 0.8392157, a: 1}
m_Intensity: 1
m_Range: 10
m_SpotAngle: 30
m_InnerSpotAngle: 21.80208
m_CookieSize: 10
m_Shadows:
  m_Type: 0
  m_Resolution: -1
  m_CustomResolution: -1
  m_Strength: 1
  m_Bias: 0.05
  m_NormalBias: 0.4
  m_NearPlane: 0.2
  m_CullingMatrixOverride:
    e00: 1
    e01: 0
    e02: 0
    e03: 0
    e10: 0
    e11: 1
    e12: 0
    e13: 0
    e20: 0
    e21: 0
    e22: 1
    e23: 0
    e30: 0
    e31: 0
    e32: 0
    e33: 1
  m_UseCullingMatrixOverride: 0
m_Cookie: {fileID: 0}
m_DrawHalo: 0
m_Flare: {fileID: 0}
m_RenderMode: 0
m_CullingMask:
  serializedVersion: 2
  m_Bits: 4294967295
m_RenderingLayerMask: 1
m_Lightmapping: 4
m_LightShadowCasterMode: 0
m_AreaSize: {x: 1, y: 1}
m_BounceIntensity: 1
m_ColorTemperature: 6570
m_UseColorTemperature: 0
m_BoundingSphereOverride: {x: 0, y: 0, z: 0, w: 0}
m_UseBoundingSphereOverride: 0
m_UseViewFrustumForShadowCasterCull: 1
m_ShadowRadius: 0
m_ShadowAngle: 0
",
            [typeof(MeshFilter)] = "\nm_Mesh: {fileID: 0}\n",
            [typeof(MeshRenderer)] = @"
m_Enabled: 1
m_CastShadows: 1
m_ReceiveShadows: 1
m_DynamicOccludee: 1
m_StaticShadowCaster: 0
m_MotionVectors: 1
m_LightProbeUsage: 1
m_ReflectionProbeUsage: 1
m_RayTracingMode: 2
m_RayTraceProcedural: 0
m_RenderingLayerMask: 1
m_RendererPriority: 0
m_Materials: []
m_StaticBatchInfo:
  firstSubMesh: 0
  subMeshCount: 0
m_StaticBatchRoot: {fileID: 0}
m_ProbeAnchor: {fileID: 0}
m_LightProbeVolumeOverride: {fileID: 0}
m_ScaleInLightmap: 1
m_ReceiveGI: 1
m_PreserveUVs: 1
m_IgnoreNormalsForChartDetection: 0
m_ImportantGI: 0
m_StitchLightmapSeams: 1
m_SelectedEditorRenderState: 3
m_MinimumChartSize: 4
m_AutoUVMaxDistance: 0.5
m_AutoUVMaxAngle: 89
m_LightmapParameters: {fileID: 0}
m_SortingLayerID: 0
m_SortingLayer: 0
m_SortingOrder: 0
m_AdditionalVertexStreams: {fileID: 0}
",
            [typeof(Material)] = @"
serializedVersion: 8
m_Name: 
m_Shader: {fileID: 0}
m_Parent: {fileID: 0}
m_ModifiedSerializedProperties: 0
m_ValidKeywords: []
m_InvalidKeywords: []
m_LightmapFlags: 4
m_EnableInstancingVariants: 0
m_DoubleSidedGI: 0
m_CustomRenderQueue: -1
stringTagMap: {}
disabledShaderPasses: []
m_LockedProperties:
m_SavedProperties:
  serializedVersion: 3
  m_TexEnvs: []
  m_Ints: []
  m_Floats: []
  m_Colors: []
m_BuildTextureStacks: []
",
        };

        public static YMap For(Type t)
        {
            if (cache.TryGetValue(t, out var m)) return m;
            m = null;
            for (var c = t; c != null && m == null; c = c.BaseType)
                if (texts.TryGetValue(c, out var txt)) m = YamlReader.ParseText(txt);
            if (m == null && IsNativeEngineType(t)) m = new YMap();
            cache[t] = m;
            return m;
        }

        static bool IsNativeEngineType(Type t)
        {
            if (typeof(MonoBehaviour).IsAssignableFrom(t) || typeof(ScriptableObject).IsAssignableFrom(t)) return false;
            if (t == typeof(GameObject) || typeof(Transform).IsAssignableFrom(t)) return false;
            return typeof(UnityEngine.Object).IsAssignableFrom(t);
        }
    }
}
