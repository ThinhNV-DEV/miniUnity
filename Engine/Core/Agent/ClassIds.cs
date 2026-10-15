// Unity native class ids (YAML "--- !u!<id>") ⇄ type names, and default bodies for components the agent can add.
// Bodies are minimal: Unity fills every missing serialized field with its default when it loads the file.
using System;
using System.Collections.Generic;
using System.Linq;
using MiniUnity.Yaml;

namespace MiniUnity.Agent
{
    public static class ClassIds
    {
        public static readonly Dictionary<int, string> Names = new()
        {
            [1] = "GameObject", [4] = "Transform", [5] = "TimeManager", [11] = "AudioManager", [13] = "InputManager", [19] = "Physics2DSettings",
            [20] = "Camera", [21] = "Material", [23] = "MeshRenderer", [28] = "Texture2D", [29] = "OcclusionCullingSettings", [30] = "GraphicsSettings",
            [33] = "MeshFilter", [43] = "Mesh", [45] = "Skybox", [47] = "QualitySettings", [48] = "Shader", [49] = "TextAsset", [50] = "Rigidbody2D",
            [54] = "Rigidbody", [55] = "PhysicsManager", [58] = "CircleCollider2D", [59] = "HingeJoint", [60] = "PolygonCollider2D", [61] = "BoxCollider2D",
            [62] = "PhysicsMaterial2D", [64] = "MeshCollider", [65] = "BoxCollider", [66] = "CompositeCollider2D", [68] = "EdgeCollider2D",
            [70] = "CapsuleCollider2D", [72] = "ComputeShader", [74] = "AnimationClip", [75] = "ConstantForce", [78] = "TagManager", [81] = "AudioListener",
            [82] = "AudioSource", [83] = "AudioClip", [84] = "RenderTexture", [89] = "Cubemap", [90] = "Avatar", [91] = "AnimatorController",
            [95] = "Animator", [96] = "TrailRenderer", [102] = "TextMesh", [104] = "RenderSettings", [108] = "Light", [111] = "Animation",
            [114] = "MonoBehaviour", [115] = "MonoScript", [117] = "Texture3D", [119] = "Projector", [120] = "LineRenderer", [123] = "LensFlare",
            [124] = "FlareLayer", [126] = "NavMeshProjectSettings", [128] = "Font", [129] = "PlayerSettings", [134] = "PhysicMaterial",
            [135] = "SphereCollider", [136] = "CapsuleCollider", [137] = "SkinnedMeshRenderer", [138] = "FixedJoint", [141] = "BuildSettings",
            [143] = "CharacterController", [144] = "CharacterJoint", [145] = "SpringJoint", [146] = "WheelCollider", [153] = "ConfigurableJoint",
            [154] = "TerrainCollider", [156] = "TerrainData", [157] = "LightmapSettings", [159] = "EditorSettings", [162] = "EditorUserSettings",
            [164] = "AudioReverbFilter", [165] = "AudioHighPassFilter", [166] = "AudioChorusFilter", [167] = "AudioReverbZone", [168] = "AudioEchoFilter",
            [169] = "AudioLowPassFilter", [170] = "AudioDistortionFilter", [182] = "WindZone", [183] = "Cloth", [187] = "Texture2DArray",
            [191] = "OffMeshLink", [192] = "OcclusionArea", [195] = "NavMeshAgent", [196] = "NavMeshSettings", [198] = "ParticleSystem",
            [199] = "ParticleSystemRenderer", [200] = "ShaderVariantCollection", [205] = "LODGroup", [206] = "BlendTree", [208] = "NavMeshObstacle",
            [210] = "SortingGroup", [212] = "SpriteRenderer", [213] = "Sprite", [215] = "ReflectionProbe", [218] = "Terrain", [220] = "LightProbeGroup",
            [221] = "AnimatorOverrideController", [222] = "CanvasRenderer", [223] = "Canvas", [224] = "RectTransform", [225] = "CanvasGroup",
            [231] = "SpringJoint2D", [232] = "DistanceJoint2D", [233] = "HingeJoint2D", [234] = "SliderJoint2D", [235] = "WheelJoint2D",
            [238] = "NavMeshData", [240] = "AudioMixer", [241] = "AudioMixerController", [243] = "AudioMixerGroupController",
            [244] = "AudioMixerEffectController", [245] = "AudioMixerSnapshotController", [247] = "ConstantForce2D", [249] = "AreaEffector2D",
            [250] = "PointEffector2D", [251] = "PlatformEffector2D", [252] = "SurfaceEffector2D", [253] = "BuoyancyEffector2D", [254] = "RelativeJoint2D",
            [255] = "FixedJoint2D", [256] = "FrictionJoint2D", [257] = "TargetJoint2D", [258] = "LightProbes", [259] = "LightProbeProxyVolume",
            [272] = "AudioMixerSnapshot", [273] = "AudioMixerGroup", [300] = "RuntimeInitializeOnLoadManager", [310] = "UnityConnectSettings",
            [319] = "AvatarMask", [320] = "PlayableDirector", [328] = "VideoPlayer", [329] = "VideoClip", [330] = "ParticleSystemForceField",
            [331] = "SpriteMask", [1001] = "PrefabInstance", [1101] = "AnimatorStateTransition", [1102] = "AnimatorState",
            [1107] = "AnimatorStateMachine", [1109] = "AnimatorTransition", [1113] = "LightmapParameters", [1120] = "LightingDataAsset",
            [19719996] = "TilemapCollider2D", [156049354] = "Grid", [181963792] = "Preset", [483693784] = "TilemapRenderer",
            [612988286] = "SpriteAtlasAsset", [687078895] = "SpriteAtlas", [850595691] = "LightingSettings", [937362698] = "VFXManager",
            [1386491679] = "PresetManager", [1660057539] = "SceneRoots", [1742807556] = "GridLayout", [1839735485] = "Tilemap",
            [1953259897] = "TerrainLayer", [2083052967] = "VisualEffect", [895512359] = "AimConstraint", [1183024399] = "LookAtConstraint",
            [1773428102] = "ParentConstraint", [1818360608] = "PositionConstraint", [1818360609] = "RotationConstraint", [1818360610] = "ScaleConstraint",
        };

        static readonly Dictionary<string, int> byName = Names.ToDictionary(k => k.Value, k => k.Key, StringComparer.OrdinalIgnoreCase);

        public static string Name(int id) => Names.TryGetValue(id, out var n) ? n : "Class" + id;
        public static int? Id(string name) => byName.TryGetValue(name, out var i) ? i : null;

        /// <summary>Class ids that are components (live on a GameObject).</summary>
        static readonly HashSet<int> nonComponents = new() { 1, 21, 28, 43, 48, 49, 74, 83, 84, 89, 90, 91, 104, 115, 117, 128, 134, 156, 157, 196, 206, 213, 221, 238, 1001, 1660057539, 29, 62 };
        public static bool IsComponent(int id) => !nonComponents.Contains(id) && !(id >= 5 && id <= 19) && id != 30 && id != 47 && id != 55 && id != 78 && id != 129 && id != 141;

        /// <summary>Behaviours (have m_Enabled).</summary>
        static readonly HashSet<int> behaviours = new() { 20, 45, 81, 82, 95, 102, 108, 111, 114, 119, 123, 124, 164, 165, 166, 167, 168, 169, 170, 195, 208, 210, 215, 220, 223, 225, 247, 249, 250, 251, 252, 253, 259, 320, 328, 331, 1742807556, 156049354, 895512359, 1183024399, 1773428102, 1818360608, 1818360609, 1818360610 };
        public static bool IsBehaviour(int id) => behaviours.Contains(id);

        /// <summary>Default body for a new component of class id on gameObject (fileID goId).</summary>
        public static YMap NewComponent(int id, long goId)
        {
            var m = new YMap();
            m.Add("m_ObjectHideFlags", "0");
            m.Add("m_CorrespondingSourceObject", new YRef(0, null, 0));
            m.Add("m_PrefabInstance", new YRef(0, null, 0));
            m.Add("m_PrefabAsset", new YRef(0, null, 0));
            m.Add("m_GameObject", new YRef(goId, null, 0));
            if (IsBehaviour(id) || id == 23 || id == 212 || id == 137 || id == 199 || id == 120 || id == 96 || id == 483693784 || id == 19719996
                || (id >= 58 && id <= 70) || id == 64 || id == 65 || id == 135 || id == 136 || id == 143 || id == 154 || id == 146)
                m.Add("m_Enabled", "1");
            if (Extra.TryGetValue(id, out var extra))
                foreach (var e in YamlReader.ParseText(extra).Entries) m.Entries.Add(e);
            return m;
        }

        // defaults written by Unity 2022.3 for the most common components (fields after m_GameObject / m_Enabled)
        static readonly Dictionary<int, string> Extra = new()
        {
            [4] = "m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\nm_LocalPosition: {x: 0, y: 0, z: 0}\nm_LocalScale: {x: 1, y: 1, z: 1}\nm_ConstrainProportionsScale: 0\nm_Children: []\nm_Father: {fileID: 0}\nm_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}\n",
            [224] = "m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\nm_LocalPosition: {x: 0, y: 0, z: 0}\nm_LocalScale: {x: 1, y: 1, z: 1}\nm_ConstrainProportionsScale: 0\nm_Children: []\nm_Father: {fileID: 0}\nm_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}\nm_AnchorMin: {x: 0.5, y: 0.5}\nm_AnchorMax: {x: 0.5, y: 0.5}\nm_AnchoredPosition: {x: 0, y: 0}\nm_SizeDelta: {x: 100, y: 100}\nm_Pivot: {x: 0.5, y: 0.5}\n",
            [33] = "m_Mesh: {fileID: 0}\n",
            [23] = "m_CastShadows: 1\nm_ReceiveShadows: 1\nm_DynamicOccludee: 1\nm_StaticShadowCaster: 0\nm_MotionVectors: 1\nm_LightProbeUsage: 1\nm_ReflectionProbeUsage: 1\nm_RayTracingMode: 2\nm_RayTraceProcedural: 0\nm_RenderingLayerMask: 1\nm_RendererPriority: 0\nm_Materials:\n- {fileID: 10303, guid: 0000000000000000f000000000000000, type: 0}\nm_StaticBatchInfo:\n  firstSubMesh: 0\n  subMeshCount: 0\nm_StaticBatchRoot: {fileID: 0}\nm_ProbeAnchor: {fileID: 0}\nm_LightProbeVolumeOverride: {fileID: 0}\nm_ScaleInLightmap: 1\nm_ReceiveGI: 1\nm_PreserveUVs: 0\nm_IgnoreNormalsForChartDetection: 0\nm_ImportantGI: 0\nm_StitchLightmapSeams: 1\nm_SelectedEditorRenderState: 3\nm_MinimumChartSize: 4\nm_AutoUVMaxDistance: 0.5\nm_AutoUVMaxAngle: 89\nm_LightmapParameters: {fileID: 0}\nm_SortingLayerID: 0\nm_SortingLayer: 0\nm_SortingOrder: 0\nm_AdditionalVertexStreams: {fileID: 0}\n",
            [212] = "m_CastShadows: 0\nm_ReceiveShadows: 0\nm_DynamicOccludee: 1\nm_StaticShadowCaster: 0\nm_MotionVectors: 1\nm_LightProbeUsage: 1\nm_ReflectionProbeUsage: 1\nm_RayTracingMode: 0\nm_RayTraceProcedural: 0\nm_RenderingLayerMask: 1\nm_RendererPriority: 0\nm_Materials:\n- {fileID: 10754, guid: 0000000000000000f000000000000000, type: 0}\nm_SortingLayerID: 0\nm_SortingLayer: 0\nm_SortingOrder: 0\nm_Sprite: {fileID: 0}\nm_Color: {r: 1, g: 1, b: 1, a: 1}\nm_FlipX: 0\nm_FlipY: 0\nm_DrawMode: 0\nm_Size: {x: 1, y: 1}\nm_AdaptiveModeThreshold: 0.5\nm_SpriteTileMode: 0\nm_WasSpriteAssigned: 0\nm_MaskInteraction: 0\nm_SpriteSortPoint: 0\n",
            [65] = "m_Material: {fileID: 0}\nm_IncludeLayers:\n  serializedVersion: 2\n  m_Bits: 0\nm_ExcludeLayers:\n  serializedVersion: 2\n  m_Bits: 0\nm_LayerOverridePriority: 0\nm_IsTrigger: 0\nm_ProvidesContacts: 0\nm_Enabled: 1\nserializedVersion: 3\nm_Size: {x: 1, y: 1, z: 1}\nm_Center: {x: 0, y: 0, z: 0}\n",
            [135] = "m_Material: {fileID: 0}\nm_IncludeLayers:\n  serializedVersion: 2\n  m_Bits: 0\nm_ExcludeLayers:\n  serializedVersion: 2\n  m_Bits: 0\nm_LayerOverridePriority: 0\nm_IsTrigger: 0\nm_ProvidesContacts: 0\nm_Enabled: 1\nserializedVersion: 3\nm_Radius: 0.5\nm_Center: {x: 0, y: 0, z: 0}\n",
            [136] = "m_Material: {fileID: 0}\nm_IncludeLayers:\n  serializedVersion: 2\n  m_Bits: 0\nm_ExcludeLayers:\n  serializedVersion: 2\n  m_Bits: 0\nm_LayerOverridePriority: 0\nm_IsTrigger: 0\nm_ProvidesContacts: 0\nm_Enabled: 1\nserializedVersion: 2\nm_Radius: 0.5\nm_Height: 2\nm_Direction: 1\nm_Center: {x: 0, y: 0, z: 0}\n",
            [64] = "m_Material: {fileID: 0}\nm_IncludeLayers:\n  serializedVersion: 2\n  m_Bits: 0\nm_ExcludeLayers:\n  serializedVersion: 2\n  m_Bits: 0\nm_LayerOverridePriority: 0\nm_IsTrigger: 0\nm_ProvidesContacts: 0\nm_Enabled: 1\nserializedVersion: 5\nm_Convex: 0\nm_CookingOptions: 30\nm_Mesh: {fileID: 0}\n",
            [54] = "serializedVersion: 4\nm_Mass: 1\nm_Drag: 0\nm_AngularDrag: 0.05\nm_CenterOfMass: {x: 0, y: 0, z: 0}\nm_InertiaTensor: {x: 1, y: 1, z: 1}\nm_InertiaRotation: {x: 0, y: 0, z: 0, w: 1}\nm_IncludeLayers:\n  serializedVersion: 2\n  m_Bits: 0\nm_ExcludeLayers:\n  serializedVersion: 2\n  m_Bits: 0\nm_ImplicitCom: 1\nm_ImplicitTensor: 1\nm_UseGravity: 1\nm_IsKinematic: 0\nm_Interpolate: 0\nm_Constraints: 0\nm_CollisionDetection: 0\n",
            [50] = "m_BodyType: 0\nm_Simulated: 1\nm_UseFullKinematicContacts: 0\nm_UseAutoMass: 0\nm_Mass: 1\nm_LinearDrag: 0\nm_AngularDrag: 0.05\nm_GravityScale: 1\nm_Material: {fileID: 0}\nm_Interpolate: 0\nm_SleepingMode: 1\nm_CollisionDetection: 0\nm_Constraints: 0\n",
            [61] = "m_Density: 1\nm_Material: {fileID: 0}\nm_IsTrigger: 0\nm_UsedByEffector: 0\nm_UsedByComposite: 0\nm_Offset: {x: 0, y: 0}\nm_Size: {x: 1, y: 1}\nm_EdgeRadius: 0\n",
            [58] = "m_Density: 1\nm_Material: {fileID: 0}\nm_IsTrigger: 0\nm_UsedByEffector: 0\nm_UsedByComposite: 0\nm_Offset: {x: 0, y: 0}\nm_Radius: 0.5\n",
            [70] = "m_Density: 1\nm_Material: {fileID: 0}\nm_IsTrigger: 0\nm_UsedByEffector: 0\nm_UsedByComposite: 0\nm_Offset: {x: 0, y: 0}\nm_Size: {x: 0.5, y: 1}\nm_Direction: 0\n",
            [82] = "serializedVersion: 4\nOutputAudioMixerGroup: {fileID: 0}\nm_audioClip: {fileID: 0}\nm_PlayOnAwake: 1\nm_Volume: 1\nm_Pitch: 1\nLoop: 0\nMute: 0\nSpatialize: 0\nSpatializePostEffects: 0\nPriority: 128\nDopplerLevel: 1\nMinDistance: 1\nMaxDistance: 500\nPan2D: 0\nrolloffMode: 0\nBypassEffects: 0\nBypassListenerEffects: 0\nBypassReverbZones: 0\n",
            [20] = "serializedVersion: 2\nm_ClearFlags: 1\nm_BackGroundColor: {r: 0.19215687, g: 0.3019608, b: 0.4745098, a: 0}\nm_projectionMatrixMode: 1\nm_GateFitMode: 2\nm_FOVAxisMode: 0\nm_SensorSize: {x: 36, y: 24}\nm_LensShift: {x: 0, y: 0}\nm_FocalLength: 50\nm_NormalizedViewPortRect:\n  serializedVersion: 2\n  x: 0\n  y: 0\n  width: 1\n  height: 1\nnear clip plane: 0.3\nfar clip plane: 1000\nfield of view: 60\northographic: 0\northographic size: 5\nm_Depth: -1\nm_CullingMask:\n  serializedVersion: 2\n  m_Bits: 4294967295\nm_RenderingPath: -1\nm_TargetTexture: {fileID: 0}\nm_TargetDisplay: 0\nm_TargetEye: 3\nm_HDR: 1\nm_AllowMSAA: 1\nm_AllowDynamicResolution: 0\nm_ForceIntoRT: 0\nm_OcclusionCulling: 1\nm_StereoConvergence: 10\nm_StereoSeparation: 0.022\n",
            [108] = "serializedVersion: 10\nm_Type: 1\nm_Shape: 0\nm_Color: {r: 1, g: 0.95686275, b: 0.8392157, a: 1}\nm_Intensity: 1\nm_Range: 10\nm_SpotAngle: 30\nm_InnerSpotAngle: 21.80208\nm_CookieSize: 10\nm_Shadows:\n  m_Type: 2\n  m_Resolution: -1\n  m_CustomResolution: -1\n  m_Strength: 1\n  m_Bias: 0.05\n  m_NormalBias: 0.4\n  m_NearPlane: 0.2\n",
            [223] = "serializedVersion: 3\nm_RenderMode: 0\nm_Camera: {fileID: 0}\nm_PlaneDistance: 100\nm_PixelPerfect: 0\nm_ReceivesEvents: 1\nm_OverrideSorting: 0\nm_OverridePixelPerfect: 0\nm_SortingBucketNormalizedSize: 0\nm_VertexColorAlwaysGammaSpace: 0\nm_AdditionalShaderChannelsFlag: 0\nm_UpdateRectTransformForStandalone: 0\nm_SortingLayerID: 0\nm_SortingOrder: 0\nm_TargetDisplay: 0\n",
            [222] = "m_CullTransparentMesh: 1\n",
            [225] = "m_Alpha: 1\nm_Interactable: 1\nm_BlocksRaycasts: 1\nm_IgnoreParentGroups: 0\n",
            [95] = "serializedVersion: 5\nm_Avatar: {fileID: 0}\nm_Controller: {fileID: 0}\nm_CullingMode: 0\nm_UpdateMode: 0\nm_ApplyRootMotion: 0\nm_LinearVelocityBlending: 0\nm_StabilizeFeet: 0\nm_WarningMessage: \nm_HasTransformHierarchy: 1\nm_AllowConstantClipSamplingOptimization: 1\nm_KeepAnimatorStateOnDisable: 0\nm_WriteDefaultValuesOnDisable: 0\n",
            [143] = "m_Material: {fileID: 0}\nm_IncludeLayers:\n  serializedVersion: 2\n  m_Bits: 0\nm_ExcludeLayers:\n  serializedVersion: 2\n  m_Bits: 0\nm_LayerOverridePriority: 0\nm_IsTrigger: 0\nm_ProvidesContacts: 0\nm_Enabled: 1\nserializedVersion: 3\nm_Height: 2\nm_Radius: 0.5\nm_SlopeLimit: 45\nm_StepOffset: 0.3\nm_SkinWidth: 0.08\nm_MinMoveDistance: 0.001\nm_Center: {x: 0, y: 0, z: 0}\n",
            [210] = "m_SortingLayerID: 0\nm_SortingLayer: 0\nm_SortingOrder: 0\nm_SortAtRoot: 0\n",
            [320] = "serializedVersion: 3\nm_PlayableAsset: {fileID: 0}\nm_InitialState: 1\nm_WrapMode: 2\nm_DirectorUpdateMode: 1\nm_InitialTime: 0\nm_SceneBindings: []\nm_ExposedReferences:\n  m_References: []\n",
        };
    }
}
