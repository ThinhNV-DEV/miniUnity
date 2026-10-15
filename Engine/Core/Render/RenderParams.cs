// RenderParams (the settings struct of Graphics.RenderMesh* / RenderPrimitives*) and the immediate-mode instanced draw calls.
// The software renderer draws scene renderers through Camera.Render(); instanced draws submitted from script are drawn by the
// cameras of the frame they were submitted in (WorldRenderer, see GraphicsDraw.cs), with per-instance block arrays.
using UnityEngine.Rendering;

namespace UnityEngine
{
    public partial struct RenderParams
    {
        /// <summary>Defaults as documented for Unity's RenderParams(Material).</summary>
        public RenderParams(Material mat)
        {
            layer = 0;
            renderingLayerMask = 1;   // GraphicsSettings.defaultRenderingLayerMask (Unity's default "Light Layer default")
            rendererPriority = 0;
            worldBounds = new Bounds(Vector3.zero, Vector3.zero);
            camera = null;
            motionVectorMode = MotionVectorGenerationMode.Camera;
            reflectionProbeUsage = ReflectionProbeUsage.Off;
            material = mat;
            matProps = null;
            shadowCastingMode = ShadowCastingMode.Off;
            receiveShadows = false;
            lightProbeUsage = LightProbeUsage.Off;
            lightProbeProxyVolume = null;
        }
        public int layer { get; set; }
        public uint renderingLayerMask { get; set; }
        public int rendererPriority { get; set; }
        public Bounds worldBounds { get; set; }
        public Camera camera { get; set; }
        public MotionVectorGenerationMode motionVectorMode { get; set; }
        public ReflectionProbeUsage reflectionProbeUsage { get; set; }
        public Material material { get; set; }
        public MaterialPropertyBlock matProps { get; set; }
        public ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
        public LightProbeUsage lightProbeUsage { get; set; }
        public LightProbeProxyVolume lightProbeProxyVolume { get; set; }
    }

    public partial class Graphics
    {
        public static void RenderMeshInstanced<T>(in RenderParams rparams, Mesh mesh, int submeshIndex, T[] instanceData, int instanceCount = -1, int startInstance = 0) where T : unmanaged
        {
            CheckInstanced(rparams, mesh, instanceData?.Length ?? -1, instanceCount, startInstance);
            SubmitInstances(rparams, mesh, submeshIndex, instanceData, instanceCount == -1 ? instanceData.Length : instanceCount, startInstance);
        }
        public static void RenderMeshInstanced<T>(in RenderParams rparams, Mesh mesh, int submeshIndex, System.Collections.Generic.List<T> instanceData, int instanceCount = -1, int startInstance = 0) where T : unmanaged
        {
            CheckInstanced(rparams, mesh, instanceData?.Count ?? -1, instanceCount, startInstance);
            SubmitInstances(rparams, mesh, submeshIndex, instanceData.ToArray(), instanceCount == -1 ? instanceData.Count : instanceCount, startInstance);
        }
        public static void RenderMeshInstanced<T>(RenderParams rparams, Mesh mesh, int submeshIndex, Unity.Collections.NativeArray<T> instanceData, int instanceCount = -1, int startInstance = 0) where T : unmanaged
        {
            CheckInstanced(rparams, mesh, instanceData.IsCreated ? instanceData.Length : -1, instanceCount, startInstance);
            SubmitInstances(rparams, mesh, submeshIndex, instanceData.ToArray(), instanceCount == -1 ? instanceData.Length : instanceCount, startInstance);
        }

        /// <summary>One draw per instance; the instance data starts with the object-to-world matrix (Matrix4x4 itself, or a struct whose
        /// first field is objectToWorld, as Unity requires). Block arrays are indexed by the instance's position in the call.</summary>
        static void SubmitInstances<T>(in RenderParams rparams, Mesh mesh, int submeshIndex, T[] data, int count, int start) where T : unmanaged
        {
            if (System.Runtime.CompilerServices.Unsafe.SizeOf<T>() < 64) throw new System.ArgumentException("The instance data must start with the object-to-world Matrix4x4.");
            MaterialPropertyBlock block = null;
            if (rparams.matProps != null && !rparams.matProps.isEmpty) { block = new MaterialPropertyBlock(); block.CopyFrom(rparams.matProps); }
            for (int i = 0; i < count; i++)
            {
                var item = data[start + i];
                var m = System.Runtime.CompilerServices.Unsafe.As<T, Matrix4x4>(ref item);
                SubmitDraw(mesh, submeshIndex, rparams.material, null, m, rparams.layer, rparams.camera, rparams.shadowCastingMode, rparams.receiveShadows, i, block);
            }
        }

        /// <summary>Argument checks Unity makes before submitting the batch (null material / mesh / data, range).</summary>
        static void CheckInstanced(in RenderParams rparams, Mesh mesh, int length, int instanceCount, int startInstance)
        {
            if (rparams.material == null) throw new System.ArgumentNullException("rparams.material");
            if (mesh == null) throw new System.ArgumentNullException(nameof(mesh));
            if (length < 0) throw new System.ArgumentNullException("instanceData");
            int count = instanceCount == -1 ? length : instanceCount;
            if (startInstance < 0 || count < 0 || startInstance + count > length)
                throw new System.ArgumentOutOfRangeException(nameof(instanceCount), "instanceCount and startInstance must be within the instanceData range");
        }
    }
}
