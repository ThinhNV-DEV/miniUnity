using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.TestTools;

// The built-in render pipeline as code sees it (GraphicsSettings.currentRenderPipeline == null, the shader modes of
// GraphicsSettings.asset) and PostProcessing v2 running in play mode: its UNITY_EDITOR paths (TypeCache, StageUtility) work,
// the layer takes the legacy (built-in pipeline) path and volumes blend into the layer's settings.
public class PostProcessingTests
{
    [Test] public void BuiltinPipelineIsActive()
    {
        Assert.IsNull(GraphicsSettings.currentRenderPipeline);
        Assert.IsNull(GraphicsSettings.defaultRenderPipeline);
        Assert.IsNull(QualitySettings.renderPipeline);
        Assert.IsFalse(RuntimeUtilities.scriptableRenderPipelineActive);
        Assert.AreEqual(BuiltinShaderMode.UseBuiltin, GraphicsSettings.GetShaderMode(BuiltinShaderType.DeferredShading));
        Assert.Greater(QualitySettings.names.Length, 0);
        Assert.AreEqual(QualitySettings.names.Length, QualitySettings.names.Distinct().Count());
    }

    [Test] public void TypeCacheFindsTheEffects()
    {
        var types = TypeCache.GetTypesDerivedFrom<PostProcessEffectSettings>();
        CollectionAssert.Contains(types.ToList(), typeof(Bloom));
        CollectionAssert.Contains(types.ToList(), typeof(Vignette));
        Assert.IsFalse(types.Contains(typeof(PostProcessEffectSettings)), "derived types only");
        Assert.IsTrue(TypeCache.GetTypesWithAttribute<UnityEngine.Rendering.PostProcessing.PostProcessAttribute>().Contains(typeof(Bloom)));
        Assert.IsTrue(RuntimeUtilities.GetAllTypesDerivedFrom<PostProcessEffectSettings>().Contains(typeof(Bloom)));
    }

    [UnityTest] public IEnumerator VolumesBlendIntoTheLayerInPlayMode()
    {
        var cam = new GameObject("Cam", typeof(Camera));
        cam.GetComponent<Camera>().targetTexture = new RenderTexture(32, 32, 24);
        var layer = cam.AddComponent<PostProcessLayer>();
        // a layer made from script needs its resources (shaders, textures), as the package documents
        var resources = AssetDatabase.LoadAssetAtPath<PostProcessResources>("Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset");
        Assert.IsNotNull(resources);
        Assert.IsNotNull(resources.shaders.uber, "shader references resolve");
        layer.Init(resources);
        layer.volumeLayer = ~0;
        layer.volumeTrigger = cam.transform;

        var profile = ScriptableObject.CreateInstance<PostProcessProfile>();
        var bloom = profile.AddSettings<Bloom>();
        bloom.enabled.Override(true);
        bloom.intensity.Override(7f);
        var vol = new GameObject("Volume").AddComponent<PostProcessVolume>();
        vol.isGlobal = true; vol.priority = 1; vol.sharedProfile = profile;

        var logs = new System.Collections.Generic.List<string>();
        Application.LogCallback h = (m, st, t) => { if (t != LogType.Log) logs.Add(t + ": " + m + " @ " + st.Split('\n').FirstOrDefault()); };
        Application.logMessageReceived += h;
        yield return null; yield return null;   // the layer's OnPreCull (every frame in play) blends the volumes
        var applied = layer.GetSettings<Bloom>();
        Application.logMessageReceived -= h;
        Assert.IsEmpty(logs, string.Join("\n", logs));
        Assert.IsTrue(applied.enabled.value);
        Assert.AreEqual(7f, applied.intensity.value, 1e-4);
        Assert.AreSame(vol, PostProcessManager.instance.GetHighestPriorityVolume(layer));
        var active = new System.Collections.Generic.List<PostProcessVolume>();
        PostProcessManager.instance.GetActiveVolumes(layer, active);
        CollectionAssert.AreEqual(new[] { vol }, active);
        cam.GetComponent<Camera>().Render();   // the legacy path's command buffers are recorded without errors
        yield return null;
    }
}
