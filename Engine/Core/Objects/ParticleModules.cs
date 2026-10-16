// ParticleSystem modules beyond main / emission: Shape (where particles start and which way they go), Color / Size / Rotation /
// Velocity over lifetime, read from the serialized modules and settable from script; and the ParticleSystemRenderer settings the
// software renderer uses (UIRenderer.DrawParticles: billboards in the transparent queue with sprites, sorted by sorting layer /
// order, clamped to maxParticleSize).
using System;
using MiniUnity.Yaml;

namespace UnityEngine
{
    public sealed partial class ParticleSystem
    {
        internal sealed class Modules
        {
            // shape
            public bool shapeEnabled = true;
            public ParticleSystemShapeType shapeType = ParticleSystemShapeType.Cone;
            public float radius = 1, radiusThickness = 1, angle = 25, arc = 360, length = 5, donutRadius = 0.2f;
            public float randomDirectionAmount, sphericalDirectionAmount, randomPositionAmount;
            public bool alignToDirection;
            public Vector3 position, rotation, scale = Vector3.one, boxThickness;
            // extra shape settings kept for scripts (not used by the emitter)
            public ParticleSystemShapeMultiModeValue radiusMode, arcMode, meshSpawnMode; public float radiusSpread, arcSpread, meshSpawnSpread, meshScale = 1, normalOffset;
            public MinMaxCurve radiusSpeed = 1f, arcSpeed = 1f, meshSpawnSpeed = 1f;
            public ParticleSystemMeshShapeType meshShapeType; public Mesh mesh; public MeshRenderer meshRenderer; public SkinnedMeshRenderer skinnedMeshRenderer;
            public Sprite sprite; public SpriteRenderer spriteRenderer; public bool useMeshMaterialIndex, useMeshColors = true; public int meshMaterialIndex;
            public Texture2D texture; public ParticleSystemShapeTextureChannel textureClipChannel = ParticleSystemShapeTextureChannel.Alpha; public float textureClipThreshold;
            public bool textureColorAffectsParticles = true, textureAlphaAffectsParticles = true, textureBilinearFiltering; public int textureUVChannel;
            // over lifetime
            public bool colorEnabled; public MinMaxGradient color = Color.white;
            public bool sizeEnabled, sizeSeparate; public MinMaxCurve size = 1f, sizeY = 1f, sizeZ = 1f;
            public bool rotationEnabled, rotationSeparate; public MinMaxCurve rotX = 0f, rotY = 0f, rotZ = 0.7853982f;
            public bool velocityEnabled; public MinMaxCurve velX = 0f, velY = 0f, velZ = 0f, speedModifier = 1f; public ParticleSystemSimulationSpace velSpace = ParticleSystemSimulationSpace.Local;
        }

        Modules m_Modules;
        internal Modules M => m_Modules ??= LoadModules();

        Modules LoadModules()
        {
            var m = new Modules();
            if (m_Native == null) return m;
            if (NGet("ShapeModule") is YMap)
            {
                m.shapeEnabled = NBool("ShapeModule.enabled", true);
                m.shapeType = (ParticleSystemShapeType)NInt("ShapeModule.type", 4);
                m.angle = NFloat("ShapeModule.angle", 25); m.length = NFloat("ShapeModule.length", 5);
                m.radiusThickness = NFloat("ShapeModule.radiusThickness", 1); m.donutRadius = NFloat("ShapeModule.donutRadius", 0.2f);
                m.radius = NFloat("ShapeModule.radius.value", 1); m.arc = NFloat("ShapeModule.arc.value", 360);
                m.position = NVec3("ShapeModule.m_Position"); m.rotation = NVec3("ShapeModule.m_Rotation");
                m.scale = NGet("ShapeModule.m_Scale") != null ? NVec3("ShapeModule.m_Scale") : Vector3.one;
                m.boxThickness = NVec3("ShapeModule.boxThickness");
                m.alignToDirection = NBool("ShapeModule.alignToDirection");
                m.randomDirectionAmount = NFloat("ShapeModule.randomDirectionAmount"); m.sphericalDirectionAmount = NFloat("ShapeModule.sphericalDirectionAmount");
                m.randomPositionAmount = NFloat("ShapeModule.randomPositionAmount");
            }
            m.colorEnabled = NBool("ColorModule.enabled"); if (m.colorEnabled) m.color = GradientOf(NGet("ColorModule.gradient"));
            m.sizeEnabled = NBool("SizeModule.enabled"); m.sizeSeparate = NBool("SizeModule.separateAxes");
            if (m.sizeEnabled) { m.size = CurveOf(NGet("SizeModule.curve"), 1); m.sizeY = CurveOf(NGet("SizeModule.y"), 1); m.sizeZ = CurveOf(NGet("SizeModule.z"), 1); }
            m.rotationEnabled = NBool("RotationModule.enabled"); m.rotationSeparate = NBool("RotationModule.separateAxes");
            if (m.rotationEnabled) { m.rotX = CurveOf(NGet("RotationModule.x"), 0); m.rotY = CurveOf(NGet("RotationModule.y"), 0); m.rotZ = CurveOf(NGet("RotationModule.curve"), 0.7853982f); }
            m.velocityEnabled = NBool("VelocityModule.enabled");
            if (m.velocityEnabled)
            {
                m.velX = CurveOf(NGet("VelocityModule.x"), 0); m.velY = CurveOf(NGet("VelocityModule.y"), 0); m.velZ = CurveOf(NGet("VelocityModule.z"), 0);
                m.speedModifier = CurveOf(NGet("VelocityModule.speedModifier"), 1);
                m.velSpace = NBool("VelocityModule.inWorldSpace") ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            }
            return m;
        }

        public ShapeModule shape => new ShapeModule(this);
        public ColorOverLifetimeModule colorOverLifetime => new ColorOverLifetimeModule(this);
        public SizeOverLifetimeModule sizeOverLifetime => new SizeOverLifetimeModule(this);
        public RotationOverLifetimeModule rotationOverLifetime => new RotationOverLifetimeModule(this);
        public VelocityOverLifetimeModule velocityOverLifetime => new VelocityOverLifetimeModule(this);

        static float Rand(State s) => s.Random01();

        static Vector3 RandomUnit(State s)
        {
            float z = Rand(s) * 2 - 1, a = Rand(s) * 2 * Mathf.PI, r = Mathf.Sqrt(Math.Max(0, 1 - z * z));
            return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z);
        }

        /// <summary>Start offset and direction from the shape module, in the emitter's local space.</summary>
        internal void ShapeSample(out Vector3 pos, out Vector3 dir)
        {
            var s = S; var m = M;
            if (!m.shapeEnabled) { pos = Vector3.zero; dir = Vector3.forward; return; }
            float R = m.radius, inner = R * (1 - Mathf.Clamp01(m.radiusThickness));
            float ArcAngle() => Rand(s) * m.arc * Mathf.Deg2Rad;
            switch (m.shapeType)
            {
                case ParticleSystemShapeType.Sphere:
                case ParticleSystemShapeType.SphereShell:
                case ParticleSystemShapeType.Hemisphere:
                case ParticleSystemShapeType.HemisphereShell:
                {
                    var u = RandomUnit(s);
                    if (m.shapeType is ParticleSystemShapeType.Hemisphere or ParticleSystemShapeType.HemisphereShell) u.z = Math.Abs(u.z);
                    bool shell = m.shapeType is ParticleSystemShapeType.SphereShell or ParticleSystemShapeType.HemisphereShell;
                    float r = shell ? R : Mathf.Lerp(inner, R, Mathf.Pow(Rand(s), 1f / 3f));
                    pos = u * r; dir = u;
                    break;
                }
                case ParticleSystemShapeType.Cone:
                case ParticleSystemShapeType.ConeShell:
                case ParticleSystemShapeType.ConeVolume:
                case ParticleSystemShapeType.ConeVolumeShell:
                {
                    float a = ArcAngle();
                    bool shell = m.shapeType is ParticleSystemShapeType.ConeShell or ParticleSystemShapeType.ConeVolumeShell;
                    float rf = shell ? 1 : Mathf.Lerp(1 - Mathf.Clamp01(m.radiusThickness), 1, Mathf.Sqrt(Rand(s)));
                    var radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                    float sinA = Mathf.Sin(m.angle * Mathf.Deg2Rad), cosA = Mathf.Cos(m.angle * Mathf.Deg2Rad);
                    dir = (radial * (sinA * rf) + new Vector3(0, 0, cosA)).normalized;
                    pos = radial * (R * rf);
                    if (m.shapeType is ParticleSystemShapeType.ConeVolume or ParticleSystemShapeType.ConeVolumeShell)
                    {
                        float along = Rand(s) * m.length;
                        pos += dir * (along / Math.Max(cosA, 1e-4f));
                    }
                    break;
                }
                case ParticleSystemShapeType.Box:
                case ParticleSystemShapeType.BoxShell:
                case ParticleSystemShapeType.BoxEdge:
                    pos = new Vector3(Rand(s) - 0.5f, Rand(s) - 0.5f, Rand(s) - 0.5f);
                    dir = Vector3.forward;
                    break;
                case ParticleSystemShapeType.Circle:
                case ParticleSystemShapeType.CircleEdge:
                {
                    float a = ArcAngle();
                    var radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                    float r = m.shapeType == ParticleSystemShapeType.CircleEdge ? R : Mathf.Lerp(inner, R, Mathf.Sqrt(Rand(s)));
                    pos = radial * r; dir = radial;
                    break;
                }
                case ParticleSystemShapeType.SingleSidedEdge:
                    pos = new Vector3((Rand(s) * 2 - 1) * R, 0, 0); dir = Vector3.up;
                    break;
                case ParticleSystemShapeType.Donut:
                {
                    float a = ArcAngle(), b = Rand(s) * 2 * Mathf.PI;
                    var ring = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                    var tube = ring * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                    pos = ring * R + tube * (m.donutRadius * Mathf.Sqrt(Rand(s))); dir = tube;
                    break;
                }
                case ParticleSystemShapeType.Rectangle:
                    pos = new Vector3(Rand(s) - 0.5f, Rand(s) - 0.5f, 0); dir = Vector3.forward;
                    break;
                default:
                    pos = Vector3.zero; dir = Vector3.forward;
                    break;
            }
            if (m.sphericalDirectionAmount > 0 && pos.sqrMagnitude > 1e-12f) dir = Vector3.Lerp(dir, pos.normalized, m.sphericalDirectionAmount).normalized;
            if (m.randomDirectionAmount > 0) dir = Vector3.Lerp(dir, RandomUnit(s), m.randomDirectionAmount).normalized;
            if (m.randomPositionAmount > 0) pos += RandomUnit(s) * (m.randomPositionAmount * Rand(s));
            var rot = Quaternion.Euler(m.rotation);
            pos = rot * Vector3.Scale(pos, m.scale) + m.position;
            dir = (rot * Vector3.Scale(dir, new Vector3(Math.Sign(m.scale.x == 0 ? 1 : m.scale.x), Math.Sign(m.scale.y == 0 ? 1 : m.scale.y), Math.Sign(m.scale.z == 0 ? 1 : m.scale.z)))).normalized;
            if (dir.sqrMagnitude < 1e-12f) dir = Vector3.forward;
        }

        /// <summary>Normalized age t (0 at birth, 1 at death) and a per-particle random factor for the over-lifetime curves.</summary>
        static float Age(in Particle p) => p.startLifetime > 0 ? Mathf.Clamp01(1 - p.remainingLifetime / p.startLifetime) : 0;
        static float Lerp01(in Particle p, uint salt) { uint h = (p.randomSeed ^ salt) * 2654435761u; return (h >> 8) / 16777215f; }

        internal Color32 CurrentColor(in Particle p)
        {
            var m = M;
            if (!m.colorEnabled) return p.startColor;
            Color c = m.color.Evaluate(Age(p), Lerp01(p, 0x9E37u));
            Color sc = p.startColor;
            return sc * c;
        }

        internal Vector3 CurrentSize3D(in Particle p)
        {
            var m = M;
            if (!m.sizeEnabled) return p.startSize3D;
            float t = Age(p), r = Lerp01(p, 0x51EDu);
            var k = m.sizeSeparate ? new Vector3(m.size.Evaluate(t, r), m.sizeY.Evaluate(t, r), m.sizeZ.Evaluate(t, r)) : Vector3.one * m.size.Evaluate(t, r);
            return Vector3.Scale(p.startSize3D, k);
        }

        // ---------------------------------------------------------------- modules (struct views over the system)
        public partial struct ShapeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ShapeModule(ParticleSystem ps) { m_ParticleSystem = ps; }
            Modules M => m_ParticleSystem.M;
            public bool enabled { get => M.shapeEnabled; set => M.shapeEnabled = value; }
            public ParticleSystemShapeType shapeType { get => M.shapeType; set => M.shapeType = value; }
            public float radius { get => M.radius; set => M.radius = Mathf.Max(0, value); }
            public float radiusThickness { get => M.radiusThickness; set => M.radiusThickness = Mathf.Clamp01(value); }
            public float angle { get => M.angle; set => M.angle = Mathf.Clamp(value, 0, 90); }
            public float arc { get => M.arc; set => M.arc = Mathf.Clamp(value, 0, 360); }
            public float length { get => M.length; set => M.length = Mathf.Max(0, value); }
            public float donutRadius { get => M.donutRadius; set => M.donutRadius = Mathf.Max(0, value); }
            public Vector3 position { get => M.position; set => M.position = value; }
            public Vector3 rotation { get => M.rotation; set => M.rotation = value; }
            public Vector3 scale { get => M.scale; set => M.scale = value; }
            public Vector3 box { get => M.scale; set => M.scale = value; }
            public Vector3 boxThickness { get => M.boxThickness; set => M.boxThickness = value; }
            public bool alignToDirection { get => M.alignToDirection; set => M.alignToDirection = value; }
            public float randomDirectionAmount { get => M.randomDirectionAmount; set => M.randomDirectionAmount = Mathf.Clamp01(value); }
            public bool randomDirection { get => M.randomDirectionAmount >= 1; set => M.randomDirectionAmount = value ? 1 : 0; }
            public float sphericalDirectionAmount { get => M.sphericalDirectionAmount; set => M.sphericalDirectionAmount = Mathf.Clamp01(value); }
            public float randomPositionAmount { get => M.randomPositionAmount; set => M.randomPositionAmount = Mathf.Max(0, value); }
            public ParticleSystemShapeMultiModeValue radiusMode { get => M.radiusMode; set => M.radiusMode = value; }
            public float radiusSpread { get => M.radiusSpread; set => M.radiusSpread = value; }
            public MinMaxCurve radiusSpeed { get => M.radiusSpeed; set => M.radiusSpeed = value; }
            public float radiusSpeedMultiplier { get => M.radiusSpeed.Multiplier; set => M.radiusSpeed = M.radiusSpeed.WithMultiplier(value); }
            public ParticleSystemShapeMultiModeValue arcMode { get => M.arcMode; set => M.arcMode = value; }
            public float arcSpread { get => M.arcSpread; set => M.arcSpread = value; }
            public MinMaxCurve arcSpeed { get => M.arcSpeed; set => M.arcSpeed = value; }
            public float arcSpeedMultiplier { get => M.arcSpeed.Multiplier; set => M.arcSpeed = M.arcSpeed.WithMultiplier(value); }
            public ParticleSystemMeshShapeType meshShapeType { get => M.meshShapeType; set => M.meshShapeType = value; }
            public Mesh mesh { get => M.mesh; set => M.mesh = value; }
            public MeshRenderer meshRenderer { get => M.meshRenderer; set => M.meshRenderer = value; }
            public SkinnedMeshRenderer skinnedMeshRenderer { get => M.skinnedMeshRenderer; set => M.skinnedMeshRenderer = value; }
            public Sprite sprite { get => M.sprite; set => M.sprite = value; }
            public SpriteRenderer spriteRenderer { get => M.spriteRenderer; set => M.spriteRenderer = value; }
            public bool useMeshMaterialIndex { get => M.useMeshMaterialIndex; set => M.useMeshMaterialIndex = value; }
            public int meshMaterialIndex { get => M.meshMaterialIndex; set => M.meshMaterialIndex = value; }
            public bool useMeshColors { get => M.useMeshColors; set => M.useMeshColors = value; }
            public float normalOffset { get => M.normalOffset; set => M.normalOffset = value; }
            public float meshScale { get => M.meshScale; set => M.meshScale = value; }
            public ParticleSystemShapeMultiModeValue meshSpawnMode { get => M.meshSpawnMode; set => M.meshSpawnMode = value; }
            public float meshSpawnSpread { get => M.meshSpawnSpread; set => M.meshSpawnSpread = value; }
            public MinMaxCurve meshSpawnSpeed { get => M.meshSpawnSpeed; set => M.meshSpawnSpeed = value; }
            public float meshSpawnSpeedMultiplier { get => M.meshSpawnSpeed.Multiplier; set => M.meshSpawnSpeed = M.meshSpawnSpeed.WithMultiplier(value); }
            public Texture2D texture { get => M.texture; set => M.texture = value; }
            public ParticleSystemShapeTextureChannel textureClipChannel { get => M.textureClipChannel; set => M.textureClipChannel = value; }
            public float textureClipThreshold { get => M.textureClipThreshold; set => M.textureClipThreshold = value; }
            public bool textureColorAffectsParticles { get => M.textureColorAffectsParticles; set => M.textureColorAffectsParticles = value; }
            public bool textureAlphaAffectsParticles { get => M.textureAlphaAffectsParticles; set => M.textureAlphaAffectsParticles = value; }
            public bool textureBilinearFiltering { get => M.textureBilinearFiltering; set => M.textureBilinearFiltering = value; }
            public int textureUVChannel { get => M.textureUVChannel; set => M.textureUVChannel = value; }
        }

        public partial struct ColorOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ColorOverLifetimeModule(ParticleSystem ps) { m_ParticleSystem = ps; }
            public bool enabled { get => m_ParticleSystem.M.colorEnabled; set => m_ParticleSystem.M.colorEnabled = value; }
            public MinMaxGradient color { get => m_ParticleSystem.M.color; set => m_ParticleSystem.M.color = value; }
        }

        public partial struct SizeOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal SizeOverLifetimeModule(ParticleSystem ps) { m_ParticleSystem = ps; }
            Modules M => m_ParticleSystem.M;
            public bool enabled { get => M.sizeEnabled; set => M.sizeEnabled = value; }
            public bool separateAxes { get => M.sizeSeparate; set => M.sizeSeparate = value; }
            public MinMaxCurve size { get => M.size; set => M.size = value; }
            public float sizeMultiplier { get => M.size.Multiplier; set => M.size = M.size.WithMultiplier(value); }
            public MinMaxCurve x { get => M.size; set => M.size = value; }
            public float xMultiplier { get => M.size.Multiplier; set => M.size = M.size.WithMultiplier(value); }
            public MinMaxCurve y { get => M.sizeY; set => M.sizeY = value; }
            public float yMultiplier { get => M.sizeY.Multiplier; set => M.sizeY = M.sizeY.WithMultiplier(value); }
            public MinMaxCurve z { get => M.sizeZ; set => M.sizeZ = value; }
            public float zMultiplier { get => M.sizeZ.Multiplier; set => M.sizeZ = M.sizeZ.WithMultiplier(value); }
        }

        public partial struct RotationOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal RotationOverLifetimeModule(ParticleSystem ps) { m_ParticleSystem = ps; }
            Modules M => m_ParticleSystem.M;
            public bool enabled { get => M.rotationEnabled; set => M.rotationEnabled = value; }
            public bool separateAxes { get => M.rotationSeparate; set => M.rotationSeparate = value; }
            public MinMaxCurve x { get => M.rotX; set => M.rotX = value; }
            public float xMultiplier { get => M.rotX.Multiplier; set => M.rotX = M.rotX.WithMultiplier(value); }
            public MinMaxCurve y { get => M.rotY; set => M.rotY = value; }
            public float yMultiplier { get => M.rotY.Multiplier; set => M.rotY = M.rotY.WithMultiplier(value); }
            public MinMaxCurve z { get => M.rotZ; set => M.rotZ = value; }
            public float zMultiplier { get => M.rotZ.Multiplier; set => M.rotZ = M.rotZ.WithMultiplier(value); }
        }

        public partial struct VelocityOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal VelocityOverLifetimeModule(ParticleSystem ps) { m_ParticleSystem = ps; }
            Modules M => m_ParticleSystem.M;
            public bool enabled { get => M.velocityEnabled; set => M.velocityEnabled = value; }
            public MinMaxCurve x { get => M.velX; set => M.velX = value; }
            public MinMaxCurve y { get => M.velY; set => M.velY = value; }
            public MinMaxCurve z { get => M.velZ; set => M.velZ = value; }
            public float xMultiplier { get => M.velX.Multiplier; set => M.velX = M.velX.WithMultiplier(value); }
            public float yMultiplier { get => M.velY.Multiplier; set => M.velY = M.velY.WithMultiplier(value); }
            public float zMultiplier { get => M.velZ.Multiplier; set => M.velZ = M.velZ.WithMultiplier(value); }
            public MinMaxCurve speedModifier { get => M.speedModifier; set => M.speedModifier = value; }
            public float speedModifierMultiplier { get => M.speedModifier.Multiplier; set => M.speedModifier = M.speedModifier.WithMultiplier(value); }
            public ParticleSystemSimulationSpace space { get => M.velSpace; set => M.velSpace = value; }
            // orbital / radial velocity are kept for scripts but not simulated
            public MinMaxCurve orbitalX { get; set; }
            public MinMaxCurve orbitalY { get; set; }
            public MinMaxCurve orbitalZ { get; set; }
            public float orbitalXMultiplier { get; set; }
            public float orbitalYMultiplier { get; set; }
            public float orbitalZMultiplier { get; set; }
            public MinMaxCurve orbitalOffsetX { get; set; }
            public MinMaxCurve orbitalOffsetY { get; set; }
            public MinMaxCurve orbitalOffsetZ { get; set; }
            public float orbitalOffsetXMultiplier { get; set; }
            public float orbitalOffsetYMultiplier { get; set; }
            public float orbitalOffsetZMultiplier { get; set; }
            public MinMaxCurve radial { get; set; }
            public float radialMultiplier { get; set; }
        }
    }

    public sealed partial class ParticleSystemRenderer
    {
        public ParticleSystemRenderMode renderMode { get => (ParticleSystemRenderMode)NInt("m_RenderMode"); set => NSetI("m_RenderMode", (int)value); }
        public ParticleSystemSortMode sortMode { get => (ParticleSystemSortMode)NInt("m_SortMode"); set => NSetI("m_SortMode", (int)value); }
        public float minParticleSize { get => NFloat("m_MinParticleSize"); set => NSetF("m_MinParticleSize", value); }
        public float maxParticleSize { get => NFloat("m_MaxParticleSize", 0.5f); set => NSetF("m_MaxParticleSize", value); }
        public float sortingFudge { get => NFloat("m_SortingFudge"); set => NSetF("m_SortingFudge", value); }
        public ParticleSystemRenderSpace alignment { get => (ParticleSystemRenderSpace)NInt("m_RenderAlignment"); set => NSetI("m_RenderAlignment", (int)value); }
        public Vector3 pivot { get => NVec3("m_Pivot"); set => NSetVec3("m_Pivot", value); }
        public Vector3 flip { get => NVec3("m_Flip"); set => NSetVec3("m_Flip", value); }
        public float lengthScale { get => NFloat("m_LengthScale", 2); set => NSetF("m_LengthScale", value); }
        public float velocityScale { get => NFloat("m_VelocityScale"); set => NSetF("m_VelocityScale", value); }
        public float cameraVelocityScale { get => NFloat("m_CameraVelocityScale"); set => NSetF("m_CameraVelocityScale", value); }
        public float normalDirection { get => NFloat("m_NormalDirection", 1); set => NSetF("m_NormalDirection", value); }
    }
}
