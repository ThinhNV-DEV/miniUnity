using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MiniUnity.Yaml;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MiniUnity
{
    public static class FileIds
    {
        static readonly System.Random rng = new(12345);
        public static long Random64()
        {
            var b = new byte[8]; rng.NextBytes(b);
            long v = BitConverter.ToInt64(b, 0);
            return v == 0 || v == long.MinValue ? 1 : v;
        }
    }

    /// <summary>Writes native-YAML assets (.asset/.playable/.mat) from live objects.</summary>
    public static class AssetWriter
    {
        public static string Write(string path, IEnumerable<Object> objects)
        {
            var objs = objects.Where(o => o != null && !o.m_Destroyed).Distinct().OrderBy(o => o.m_Asset.FileID).ToList();
            var w = new YamlWriter();
            var guid = AssetIndex.GuidOf(path);
            w.RefResolver = o =>
            {
                if (o.m_Asset == null) return (0, null, 0);
                if (o.m_Asset.Path == path) return (o.m_Asset.FileID, null, 0);
                return (o.m_Asset.FileID, o.m_Asset.Guid, o.m_Asset.RefType);
            };
            w.WriteHeader();
            foreach (var o in objs)
            {
                var (id, name) = Serializer.ClassOf(o);
                var body = o is MissingScriptObject ms && ms.RawBody != null ? ms.RawBody : Serializer.Serialize(o);
                w.WriteDoc(id, o.m_Asset.FileID, name, body);
            }
            return w.ToString();
        }
    }

    /// <summary>Writes a .unity scene: plain objects, PrefabInstances (with property modifications) and stripped stand-ins. In prefab
    /// mode it writes a .prefab asset the same way: nested prefab instances stay PrefabInstances, and a root that is itself a
    /// prefab instance makes a prefab variant.</summary>
    public sealed class SceneWriter
    {
        readonly SceneData scene;
        readonly GameObject prefabRoot;                     // prefab mode
        readonly Dictionary<Object, long> presetIds;        // prefab mode: ids to keep (objects loaded from the asset being overwritten)
        readonly Dictionary<Object, long> ids = new();
        readonly List<Object> plain = new();               // fully written objects
        readonly Dictionary<PrefabInstanceData, long> instanceIds = new();
        readonly Dictionary<Object, (PrefabInstanceData inst, long id)> stripped = new();
        readonly Dictionary<Object, PrefabInstanceData> owner = new(); // instance object → its prefab instance
        long next = 100000;
        long NewId()
        {
            if (prefabRoot != null) { long r; do r = Math.Abs(FileIds.Random64()); while (r == 0 || usedIds.Contains(r)); usedIds.Add(r); return r; }   // Unity: random 64-bit ids in prefabs
            next += 1 + (next % 7); return next;
        }
        readonly HashSet<long> usedIds = new();

        public SceneWriter(SceneData s) { scene = s; }
        public SceneWriter(GameObject root, Dictionary<Object, long> keepIds) { prefabRoot = root; presetIds = keepIds; if (keepIds != null) foreach (var v in keepIds.Values) usedIds.Add(v); }

        public string Write()
        {
            // 1. classify hierarchy
            if (prefabRoot != null) Visit(prefabRoot);
            else foreach (var root in scene.Roots) Visit(root);
            // 2. ids for plain objects
            foreach (var o in plain.ToList()) if (!ids.ContainsKey(o)) ids[o] = presetIds != null && presetIds.TryGetValue(o, out var keep) ? keep : NewId();
            var w = new YamlWriter();
            w.RefResolver = Resolve;
            // serialize plain objects first (may discover extra scene objects / stripped refs)
            var docs = new List<(long id, int cls, string name, YMap body, bool stripped)>();
            for (int i = 0; i < plain.Count; i++)
            {
                var o = plain[i];
                var (cls, name) = Serializer.ClassOf(o);
                var body = Serializer.Serialize(o);
                // make sure referenced loose objects (scene-owned materials etc.) get written
                Discover(body);
                docs.Add((ids[o], cls, name, body, false));
            }
            // prefab instances
            foreach (var inst in instanceIds.Keys.ToList())
            {
                var body = PrefabInstanceBody(inst);
                docs.Add((instanceIds[inst], 1001, "PrefabInstance", body, false));
            }
            // stripped (after everything has been resolved once: resolving happens at text emission, so pre-resolve)
            var preW = new YamlWriter { RefResolver = Resolve };
            foreach (var d in docs) preW.WriteDoc(d.cls, d.id, d.name, d.body);
            foreach (var kv in stripped.ToList())
            {
                var o = kv.Key;
                var (cls, name) = Serializer.ClassOf(o);
                var src = o.m_CorrespondingSource;
                var b = new YMap();
                b.Add("m_CorrespondingSourceObject", new YRef(src.m_Asset.FileID, src.m_Asset.Guid, 3));
                b.Add("m_PrefabInstance", new YRef(instanceIds[kv.Value.inst], null, 0));
                b.Add("m_PrefabAsset", new YRef(0, null, 0));
                if (o is MonoBehaviour)
                {
                    b.Add("m_GameObject", new YRef(0, null, 0)); b.Add("m_Enabled", "1"); b.Add("m_EditorHideFlags", "0");
                    b.Add("m_Script", ScriptRegistry.ScriptRef(o.GetType())); b.Add("m_Name", ""); b.Add("m_EditorClassIdentifier", "");
                }
                docs.Add((kv.Value.id, cls, name, b, true));
            }
            w.WriteHeader();
            if (prefabRoot != null)
            {
                foreach (var d in docs.OrderBy(d => d.id)) w.WriteDoc(d.cls, d.id, d.name, d.body, d.stripped);
                return w.ToString();
            }
            int rs = SceneSettingsText.IndexOf("--- !u!104 &2"), re = SceneSettingsText.IndexOf("--- !u!157 &3");
            w.Raw(SceneSettingsText.Substring(0, rs));
            w.WriteDoc(104, 2, "RenderSettings", scene.RenderSettings ?? SceneData.DefaultRenderSettings());
            w.Raw(SceneSettingsText.Substring(re));
            foreach (var d in docs.OrderBy(d => d.id)) w.WriteDoc(d.cls, d.id, d.name, d.body, d.stripped);
            // SceneRoots
            var roots = new YSeq();
            foreach (var r in scene.Roots)
            {
                if (r.m_PrefabInstance != null && instanceIds.TryGetValue(r.m_PrefabInstance, out var pid)) roots.Items.Add(new YRef(pid, null, 0));
                else roots.Items.Add(new YRef(ids[r.m_Transform], null, 0));
            }
            w.WriteDoc(1660057539, 9223372036854775807, "SceneRoots", new YMap().Add("m_ObjectHideFlags", "0").Add("m_Roots", roots));
            return w.ToString();
        }

        void Visit(GameObject go)
        {
            var inst = InstanceOf(go);
            if (inst != null)
            {
                if (go.m_PrefabInstance != null && !instanceIds.ContainsKey(go.m_PrefabInstance)) instanceIds[go.m_PrefabInstance] = NewId();
                owner[go] = inst;
                foreach (var c in go.m_Components)
                {
                    if (c.m_CorrespondingSource != null) owner[c] = inst;
                    else plain.Add(c); // added component
                }
            }
            else
            {
                plain.Add(go);
                foreach (var c in go.m_Components) if (!c.m_Destroyed) plain.Add(c);
            }
            foreach (var ch in go.m_Transform.m_Children) Visit(ch.gameObject);
        }

        /// <summary>The prefab instance a GameObject belongs to (as a corresponding object), or null.</summary>
        static PrefabInstanceData InstanceOf(GameObject go)
        {
            if (go.m_CorrespondingSource == null) return null;
            for (var t = go.m_Transform; t != null; t = t.m_Parent)
                if (t.gameObject.m_PrefabInstance != null) return t.gameObject.m_PrefabInstance;
            return null;
        }

        void Discover(YNode n)
        {
            switch (n)
            {
                case YRef r when !r.Raw && r.Target != null && !r.Target.m_Destroyed:
                    var o = r.Target;
                    if (o.m_Asset != null || ids.ContainsKey(o) || owner.ContainsKey(o)) return;
                    if (o is GameObject || o is Component) return; // outside scene → null
                    ids[o] = NewId(); plain.Add(o);
                    break;
                case YMap m: foreach (var e in m.Entries) Discover(e.Value); break;
                case YSeq s: foreach (var i in s.Items) Discover(i); break;
            }
        }

        (long, string, int) Resolve(Object o)
        {
            if (o == null || o.m_Destroyed) return (0, null, 0);
            if (ids.TryGetValue(o, out var id)) return (id, null, 0);
            if (owner.TryGetValue(o, out var inst))
            {
                // Unity's id for a stripped stand-in: (PrefabInstance id ^ source object id) & 0x7FFFFFFFFFFFFFFF
                if (!stripped.TryGetValue(o, out var s)) { s = (inst, MiniUnity.Agent.SceneGraph.Combine(instanceIds[inst], o.m_CorrespondingSource.m_Asset.FileID)); stripped[o] = s; }
                return (s.id, null, 0);
            }
            if (o.m_Asset != null) return (o.m_Asset.FileID, o.m_Asset.Guid, o.m_Asset.RefType);
            return (0, null, 0);
        }

        // ------------------------------------------------------------------ prefab instance
        YMap PrefabInstanceBody(PrefabInstanceData inst)
        {
            var root = inst.InstanceRoot;
            var m = new YMap();
            m.Add("m_ObjectHideFlags", "0");
            m.Add("serializedVersion", "2");
            var mod = new YMap();
            mod.Add("serializedVersion", "3");
            var parent = root.m_Transform.m_Parent;
            mod.Add("m_TransformParent", new YRef(parent));
            var mods = new YSeq();
            // Unity orders modifications by target fileID, then property path length, then ordinal path
            foreach (var (target, path, value) in PrefabDiff.Modifications(inst).OrderBy(m => m.target.m_Asset.FileID).ThenBy(m => m.path.Length).ThenBy(m => m.path, StringComparer.Ordinal))
            {
                var e = new YMap();
                e.Add("target", new YRef(target.m_Asset.FileID, target.m_Asset.Guid, 3));
                e.Add("propertyPath", path);
                if (value is YRef r) { e.Add("value", ""); e.Add("objectReference", r); }
                else { e.Add("value", value); e.Add("objectReference", new YRef(0, null, 0)); }
                mods.Items.Add(e);
            }
            mod.Add("m_Modifications", mods);
            var removedC = new YSeq(); var removedG = new YSeq();
            foreach (var src in PrefabDiff.Removed(inst))
                (src is GameObject ? removedG : removedC).Items.Add(new YRef(src.m_Asset.FileID, src.m_Asset.Guid, 3));
            mod.Add("m_RemovedComponents", removedC);
            mod.Add("m_RemovedGameObjects", removedG);
            var addedG = new YSeq(); var addedC = new YSeq();
            foreach (var o in inst.Objects)
            {
                if (o is Transform t && !t.m_Destroyed)
                    foreach (var ch in t.m_Children)
                        if (ch.m_CorrespondingSource == null || ch.gameObject.m_PrefabInstance != null && ch.gameObject.m_PrefabInstance != inst)
                            addedG.Items.Add(new YMap().Add("targetCorrespondingSourceObject", SrcRef(t)).Add("insertIndex", "-1").Add("addedObject", new YRef(ch)));
                if (o is GameObject g && !g.m_Destroyed)
                    foreach (var c in g.m_Components)
                        if (c.m_CorrespondingSource == null)
                            addedC.Items.Add(new YMap().Add("targetCorrespondingSourceObject", SrcRef(g)).Add("insertIndex", "-1").Add("addedObject", new YRef(c)));
            }
            mod.Add("m_AddedGameObjects", addedG);
            mod.Add("m_AddedComponents", addedC);
            m.Add("m_Modification", mod);
            m.Add("m_SourcePrefab", new YRef(100100000, inst.PrefabGuid, 3));
            return m;
        }
        static YRef SrcRef(Object o) => new YRef(o.m_CorrespondingSource.m_Asset.FileID, o.m_CorrespondingSource.m_Asset.Guid, 3);

        /// <summary>Body of the default RenderSettings document (dedented), parsed per scene into SceneData.RenderSettings.</summary>
        public static string RenderSettingsBody
        {
            get
            {
                int a = SceneSettingsText.IndexOf("RenderSettings:\n") + "RenderSettings:\n".Length, b = SceneSettingsText.IndexOf("--- !u!157 &3");
                return string.Join("\n", SceneSettingsText.Substring(a, b - a).Split('\n').Select(l => l.StartsWith("  ") ? l.Substring(2) : l));
            }
        }
        public const string SceneSettingsText = @"--- !u!29 &1
OcclusionCullingSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_OcclusionBakeSettings:
    smallestOccluder: 5
    smallestHole: 0.25
    backfaceThreshold: 100
  m_SceneGUID: 00000000000000000000000000000000
  m_OcclusionCullingData: {fileID: 0}
--- !u!104 &2
RenderSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 9
  m_Fog: 0
  m_FogColor: {r: 0.5, g: 0.5, b: 0.5, a: 1}
  m_FogMode: 3
  m_FogDensity: 0.01
  m_LinearFogStart: 0
  m_LinearFogEnd: 300
  m_AmbientSkyColor: {r: 0.212, g: 0.227, b: 0.259, a: 1}
  m_AmbientEquatorColor: {r: 0.114, g: 0.125, b: 0.133, a: 1}
  m_AmbientGroundColor: {r: 0.047, g: 0.043, b: 0.035, a: 1}
  m_AmbientIntensity: 1
  m_AmbientMode: 0
  m_SubtractiveShadowColor: {r: 0.42, g: 0.478, b: 0.627, a: 1}
  m_SkyboxMaterial: {fileID: 10304, guid: 0000000000000000f000000000000000, type: 0}
  m_HaloStrength: 0.5
  m_FlareStrength: 1
  m_FlareFadeSpeed: 3
  m_HaloTexture: {fileID: 0}
  m_SpotCookie: {fileID: 10001, guid: 0000000000000000e000000000000000, type: 0}
  m_DefaultReflectionMode: 0
  m_DefaultReflectionResolution: 128
  m_ReflectionBounces: 1
  m_ReflectionIntensity: 1
  m_CustomReflection: {fileID: 0}
  m_Sun: {fileID: 0}
  m_UseRadianceAmbientProbe: 0
--- !u!157 &3
LightmapSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 12
  m_GIWorkflowMode: 1
  m_GISettings:
    serializedVersion: 2
    m_BounceScale: 1
    m_IndirectOutputScale: 1
    m_AlbedoBoost: 1
    m_EnvironmentLightingMode: 0
    m_EnableBakedLightmaps: 1
    m_EnableRealtimeLightmaps: 0
  m_LightmapEditorSettings:
    serializedVersion: 12
    m_Resolution: 2
    m_BakeResolution: 40
    m_AtlasSize: 1024
    m_AO: 0
    m_AOMaxDistance: 1
    m_CompAOExponent: 1
    m_CompAOExponentDirect: 0
    m_ExtractAmbientOcclusion: 0
    m_Padding: 2
    m_LightmapParameters: {fileID: 0}
    m_LightmapsBakeMode: 1
    m_TextureCompression: 1
    m_FinalGather: 0
    m_FinalGatherFiltering: 1
    m_FinalGatherRayCount: 256
    m_ReflectionCompression: 2
    m_MixedBakeMode: 2
    m_BakeBackend: 1
    m_PVRSampling: 1
    m_PVRDirectSampleCount: 32
    m_PVRSampleCount: 512
    m_PVRBounces: 2
    m_PVREnvironmentSampleCount: 256
    m_PVREnvironmentReferencePointCount: 2048
    m_PVRFilteringMode: 1
    m_PVRDenoiserTypeDirect: 1
    m_PVRDenoiserTypeIndirect: 1
    m_PVRDenoiserTypeAO: 1
    m_PVRFilterTypeDirect: 0
    m_PVRFilterTypeIndirect: 0
    m_PVRFilterTypeAO: 0
    m_PVREnvironmentMIS: 1
    m_PVRCulling: 1
    m_PVRFilteringGaussRadiusDirect: 1
    m_PVRFilteringGaussRadiusIndirect: 5
    m_PVRFilteringGaussRadiusAO: 2
    m_PVRFilteringAtrousPositionSigmaDirect: 0.5
    m_PVRFilteringAtrousPositionSigmaIndirect: 2
    m_PVRFilteringAtrousPositionSigmaAO: 1
    m_ExportTrainingData: 0
    m_TrainingDataDestination: TrainingData
    m_LightProbeSampleCountMultiplier: 4
  m_LightingDataAsset: {fileID: 0}
  m_LightingSettings: {fileID: 0}
--- !u!196 &4
NavMeshSettings:
  serializedVersion: 2
  m_ObjectHideFlags: 0
  m_BuildSettings:
    serializedVersion: 3
    agentTypeID: 0
    agentRadius: 0.5
    agentHeight: 2
    agentSlope: 45
    agentClimb: 0.4
    ledgeDropHeight: 0
    maxJumpAcrossDistance: 0
    minRegionArea: 2
    manualCellSize: 0
    cellSize: 0.16666667
    manualTileSize: 0
    tileSize: 256
    buildHeightMesh: 0
    maxJobWorkers: 0
    preserveTilesOutsideBounds: 0
    debug:
      m_Flags: 0
  m_NavMeshData: {fileID: 0}
";
    }

    /// <summary>Property-path diff between prefab-instance objects and their source objects.</summary>
    public static class PrefabDiff
    {
        static readonly HashSet<string> skipKeys = new() { "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject", "m_Children", "m_Father", "m_Component" };
        static readonly string[] rootRectDefaults = { "m_Pivot.x", "m_Pivot.y", "m_AnchorMax.x", "m_AnchorMax.y", "m_AnchorMin.x", "m_AnchorMin.y", "m_SizeDelta.x", "m_SizeDelta.y", "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.w", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_AnchoredPosition.x", "m_AnchoredPosition.y", "m_LocalEulerAnglesHint.x", "m_LocalEulerAnglesHint.y", "m_LocalEulerAnglesHint.z" };
        static readonly string[] rootTransformDefaults = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.w", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalEulerAnglesHint.x", "m_LocalEulerAnglesHint.y", "m_LocalEulerAnglesHint.z" };

        public static IEnumerable<Object> Removed(PrefabInstanceData inst)
        {
            var have = new HashSet<Object>(inst.Objects.Where(o => !o.m_Destroyed).Select(o => o.m_CorrespondingSource));
            var asset = AssetLoader.Load(AssetIndex.PathOf(inst.PrefabGuid));
            if (asset == null) yield break;
            foreach (var o in asset.Objects.Values)
                if ((o is Component || o is GameObject) && !have.Contains(o))
                {
                    // only report top-most removed (component, or GameObject whose parent still exists)
                    if (o is Component c && !have.Contains(c.m_GameObject)) continue;
                    if (o is GameObject g && g.m_Transform.m_Parent != null && !have.Contains(g.m_Transform.m_Parent.gameObject)) continue;
                    yield return o;
                }
        }

        public static List<(Object target, string path, YNode value)> Modifications(PrefabInstanceData inst)
        {
            var result = new List<(Object, string, YNode)>();
            var root = inst.InstanceRoot;
            Object Canon(Object o)
            {
                if (o == null || o.m_Destroyed) return null;
                if (o.m_CorrespondingSource != null && inst.Objects.Contains(o)) return o.m_CorrespondingSource;
                return o;
            }
            foreach (var o in inst.Objects.OrderBy(x => x.m_CorrespondingSource.m_Asset.FileID))
            {
                if (o.m_Destroyed) continue;
                var src = o.m_CorrespondingSource;
                var a = Flatten(Serializer.Serialize(o));
                var b = Flatten(Serializer.Serialize(src)).ToDictionary(x => x.path, x => x.node);
                var forced = new HashSet<string>();
                if (o == root) forced.Add("m_Name");
                if (o == root.m_Transform) foreach (var p in (o is RectTransform ? rootRectDefaults : rootTransformDefaults)) forced.Add(p);
                var entries = new List<(string path, YNode node)>();
                bool tmp = o is TMPro.TMP_Text;
                foreach (var (path, node) in a)
                {
                    // fields TMP derives while generating the mesh (rendering) – Unity records no override for them
                    if (tmp && (path == "m_TextStyleHashCode" || path.StartsWith("m_fontColor32"))) continue;
                    b.TryGetValue(path, out var sv);
                    if (!forced.Contains(path) && Same(node, sv, Canon)) continue;
                    entries.Add((path, node));
                }
                // forced defaults first in Unity's order
                foreach (var p in forced.OrderBy(p => Array.IndexOf(o is RectTransform ? rootRectDefaults : rootTransformDefaults, p)))
                {
                    var e = entries.FirstOrDefault(x => x.path == p);
                    if (e.path != null) result.Add((src, e.path, e.node is YRef rr ? rr : e.node));
                }
                foreach (var e in entries) if (!forced.Contains(e.path)) result.Add((src, e.path, e.node));
            }
            return result;
        }

        static bool Same(YNode a, YNode b, Func<Object, Object> canon)
        {
            if (b == null) return false;
            if (a is YScalar sa && b is YScalar sb) return sa.Value == sb.Value || NumEq(sa.Value, sb.Value);
            if (a is YRef ra && b is YRef rb)
            {
                if (ra.Raw || rb.Raw)
                {
                    var ka = Key(ra, canon); var kb = Key(rb, x => x);
                    return ka == kb;
                }
                // a reference to a destroyed object ("Missing") still differs from the asset's null: Unity keeps the override
                if (ra.Target != null && ra.Target.m_Destroyed && rb.Target == null) return false;
                return ReferenceEquals(canon(ra.Target), rb.Target == null || rb.Target.m_Destroyed ? null : rb.Target);
            }
            return false;
        }
        static string Key(YRef r, Func<Object, Object> canon)
        {
            if (r.Raw) return r.FileID == 0 ? "null" : $"{r.Guid}:{r.FileID}";
            var t = canon(r.Target);
            if (t == null) return "null";
            return t.m_Asset != null ? $"{t.m_Asset.Guid}:{t.m_Asset.FileID}" : "live:" + t.m_InstanceID;
        }
        static bool NumEq(string a, string b)
        {
            if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return x == y;
            return false;
        }

        public static List<(string path, YNode node)> Flatten(YMap body)
        {
            var list = new List<(string, YNode)>();
            foreach (var e in body.Entries)
            {
                if (skipKeys.Contains(e.Key)) continue;
                Rec(e.Key, e.Value, list);
            }
            return list;
        }
        static void Rec(string prefix, YNode n, List<(string, YNode)> list)
        {
            switch (n)
            {
                case YScalar s: list.Add((prefix, s)); break;
                case YRef r: list.Add((prefix, r)); break;
                case YMap m: foreach (var e in m.Entries) Rec(prefix + "." + e.Key, e.Value, list); break;
                case YSeq q:
                    list.Add((prefix + ".Array.size", YScalar.Of(q.Items.Count)));
                    // Unity emits array modifications column-major for struct/class elements (all data[i].a, then all data[i].b),
                    // row-major for simple elements (scalars, refs, flow-style vectors/colors)
                    if (q.Items.Count > 0 && q.Items.All(it => it is YMap m && !m.Flow))
                    {
                        var keys = ((YMap)q.Items[0]).Entries.Select(e => e.Key).ToList();
                        foreach (var k in keys)
                            for (int i = 0; i < q.Items.Count; i++)
                                Rec($"{prefix}.Array.data[{i}].{k}", ((YMap)q.Items[i])[k], list);
                    }
                    else
                        for (int i = 0; i < q.Items.Count; i++) Rec($"{prefix}.Array.data[{i}]", q.Items[i], list);
                    break;
            }
        }
    }
}
