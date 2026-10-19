// The render pipeline and built-in shader settings code reads to choose its path (PostProcessing v2's
// RuntimeUtilities.scriptableRenderPipelineActive, isDeferredShadingActive…): GraphicsSettings.asset m_CustomRenderPipeline and
// the m_Deferred / m_DepthNormals… shader modes, QualitySettings.asset's level list (customRenderPipeline per level,
// m_CurrentQuality). A project on the built-in pipeline gets currentRenderPipeline == null, as in Unity.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniUnity;
using MiniUnity.Yaml;
using UnityEngine.Rendering;

namespace MiniUnity
{
    public static class PipelineSettings
    {
        static string s_Root;
        static YMap s_Graphics;
        static List<YMap> s_Levels;
        static int s_Current;
        internal static RenderPipelineAsset s_Default; internal static bool s_DefaultSet;
        internal static RenderPipelineAsset s_Quality; internal static bool s_QualitySet;
        internal static int? s_Level;
        internal static readonly Dictionary<BuiltinShaderType, BuiltinShaderMode> s_Modes = new();

        public static void Reset() { s_Root = null; s_Graphics = null; s_Levels = null; s_DefaultSet = s_QualitySet = false; s_Default = s_Quality = null; s_Level = null; s_Modes.Clear(); }

        static void Load()
        {
            if (s_Root == AssetIndex.Root && s_Levels != null) return;
            Reset();
            s_Root = AssetIndex.Root; s_Levels = new List<YMap>();
            s_Graphics = Doc("ProjectSettings/GraphicsSettings.asset");
            var q = Doc("ProjectSettings/QualitySettings.asset");
            if (q != null)
            {
                s_Current = Serializer.Int(q["m_CurrentQuality"], 0);
                if (q["m_QualitySettings"] is YSeq seq) s_Levels.AddRange(seq.Items.OfType<YMap>());
            }
            if (int.TryParse(Environment.GetEnvironmentVariable("MU_QUALITY_LEVEL"), out int forced)) s_Current = forced;
        }

        static YMap Doc(string rel)
        {
            try { return s_Root != null && File.Exists(Path.Combine(s_Root, rel)) ? MiniUnity.Agent.UFile.Load(rel).Docs.FirstOrDefault()?.Body : null; }
            catch (Exception) { return null; }
        }

        static RenderPipelineAsset Asset(YNode n) =>
            n is YRef r && r.FileID != 0 && !string.IsNullOrEmpty(r.Guid) ? AssetLoader.ResolveGuid(r.Guid, r.FileID, typeof(RenderPipelineAsset)) as RenderPipelineAsset : null;

        public static RenderPipelineAsset Default { get { Load(); if (!s_DefaultSet) { s_Default = Asset(s_Graphics?["m_CustomRenderPipeline"]); s_DefaultSet = true; } return s_Default; } }
        public static int Level { get { Load(); return s_Level ?? Math.Clamp(s_Current, 0, Math.Max(0, s_Levels.Count - 1)); } }
        public static int LevelCount { get { Load(); return s_Levels.Count; } }
        public static string LevelName(int i) { Load(); return i >= 0 && i < s_Levels.Count ? Serializer.Str(s_Levels[i]["name"]) : ""; }
        public static RenderPipelineAsset LevelPipeline(int i) { Load(); return i >= 0 && i < s_Levels.Count ? Asset(s_Levels[i]["customRenderPipeline"]) : null; }
        public static RenderPipelineAsset Quality { get { Load(); if (!s_QualitySet) { s_Quality = LevelPipeline(Level); s_QualitySet = true; } return s_Quality; } }

        static readonly Dictionary<BuiltinShaderType, string> Keys = new()
        {
            [BuiltinShaderType.DeferredShading] = "m_Deferred", [BuiltinShaderType.DeferredReflections] = "m_DeferredReflections",
            [BuiltinShaderType.LegacyDeferredLighting] = "m_LegacyDeferred", [BuiltinShaderType.ScreenSpaceShadows] = "m_ScreenSpaceShadows",
            [BuiltinShaderType.DepthNormals] = "m_DepthNormals", [BuiltinShaderType.MotionVectors] = "m_MotionVectors",
            [BuiltinShaderType.LightHalo] = "m_LightHalo", [BuiltinShaderType.LensFlare] = "m_LensFlare",
        };
        public static BuiltinShaderMode Mode(BuiltinShaderType t)
        {
            Load();
            if (s_Modes.TryGetValue(t, out var m)) return m;
            return Keys.TryGetValue(t, out var k) && s_Graphics?[k] is YMap e ? (BuiltinShaderMode)Serializer.Int(e["m_Mode"], 1) : BuiltinShaderMode.UseBuiltin;
        }
    }
}

namespace UnityEngine.Rendering
{
    public partial class GraphicsSettings
    {
        public static RenderPipelineAsset defaultRenderPipeline { get => PipelineSettings.Default; set { PipelineSettings.s_Default = value; PipelineSettings.s_DefaultSet = true; } }
        public static RenderPipelineAsset renderPipelineAsset { get => defaultRenderPipeline; set => defaultRenderPipeline = value; }
        /// <summary>The quality level's pipeline when it sets one, else the default (null: built-in render pipeline).</summary>
        public static RenderPipelineAsset currentRenderPipeline => QualitySettings.renderPipeline ?? defaultRenderPipeline;
        public static RenderPipelineAsset[] allConfiguredRenderPipelines =>
            new[] { defaultRenderPipeline }.Concat(Enumerable.Range(0, PipelineSettings.LevelCount).Select(PipelineSettings.LevelPipeline)).Where(a => a != null).Distinct().ToArray();
        public static BuiltinShaderMode GetShaderMode(BuiltinShaderType type) => PipelineSettings.Mode(type);
        public static void SetShaderMode(BuiltinShaderType type, BuiltinShaderMode mode) => PipelineSettings.s_Modes[type] = mode;
    }
}

namespace UnityEngine
{
    public partial class QualitySettings
    {
        public static RenderPipelineAsset renderPipeline { get => PipelineSettings.Quality; set { PipelineSettings.s_Quality = value; PipelineSettings.s_QualitySet = true; } }
        public static RenderPipelineAsset GetRenderPipelineAssetAt(int index)
        {
            if (index < 0 || index >= PipelineSettings.LevelCount) throw new IndexOutOfRangeException($"{nameof(index)} is out of range [0..{PipelineSettings.LevelCount}[");
            return PipelineSettings.LevelPipeline(index);
        }
        public static int GetQualityLevel() => PipelineSettings.Level;
        public static void SetQualityLevel(int index) => SetQualityLevel(index, true);
        public static void SetQualityLevel(int index, bool applyExpensiveChanges)
        {
            if (index < 0 || index >= PipelineSettings.LevelCount) return;
            PipelineSettings.s_Level = index; PipelineSettings.s_QualitySet = false;
        }
        public static string[] names => Enumerable.Range(0, PipelineSettings.LevelCount).Select(PipelineSettings.LevelName).ToArray();
    }
}
