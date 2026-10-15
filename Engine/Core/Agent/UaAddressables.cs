// Addressables (com.unity.addressables 1.x) without the Editor: settings, groups, entries, labels, schemas, and a duplicate-dependency analysis.
// Works on the YAML assets under Assets/AddressableAssetsData (the layout the Addressables window writes).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MiniUnity.Yaml;

namespace MiniUnity.Agent
{
    public static partial class UaCommands
    {
        // script GUIDs of com.unity.addressables 1.x (stable across versions; read from Library/PackageCache when present)
        static readonly Dictionary<string, string> AddrScripts = new()
        {
            ["AddressableAssetSettings"] = "468a46d0ae32c3544b7d98094e6448a9", ["AddressableAssetGroup"] = "bbb281ee3bf0b054c82ac2347e9e782c",
            ["BundledAssetGroupSchema"] = "e5d17a21594effb4e9591490b009e7aa", ["ContentUpdateGroupSchema"] = "5834b5087d578d24c926ce20cd31e6d6",
            ["PlayerDataGroupSchema"] = "b1487f5d688e4f94f828f879d599dbdc", ["AddressableAssetSettingsDefaultObject"] = "3a189bb168d8d90478a09ea08c2f3d72",
            ["AddressableAssetGroupTemplate"] = "1a3c5d64ac83548c09dd1678b9f6f1cd", ["BuildScriptFastMode"] = "88d21199f5d473f4db36845f2318f180",
            ["BuildScriptVirtualMode"] = "bb0e4994b34add1409fd8ccaf4a82de5", ["BuildScriptPackedPlayMode"] = "ad8c280d42ee0ed41a27db23b43dd2bf",
            ["BuildScriptPackedMode"] = "3e2e0ffa088c91d41a086d0b8cb16bdc",
        };
        static string AddrScript(string cls)
        {
            var a = AssetDb.All.FirstOrDefault(x => x.Path.Contains("com.unity.addressables") && x.Path.EndsWith("/" + cls + ".cs"));
            return a?.Guid ?? AddrScripts[cls];
        }

        sealed class AddrCtx
        {
            public UFile Settings;
            public YMap S => Settings.Docs[0].Body;
            public string Folder;
            public List<(UFile file, YMap body)> Groups = new();
        }

        static string ScriptGuid(YMap body) => (body?["m_Script"] as YRef)?.Guid;

        static AddrCtx Addr(bool required = true)
        {
            string settingsPath = null;
            var def = AssetDb.All.FirstOrDefault(x => x.Path.EndsWith("/DefaultObject.asset") && x.Path.StartsWith("Assets/"));
            if (def != null && UFile.IsYaml(Ua.Abs(def.Path)))
            {
                var g = Serializer.Str(UFile.Load(def.Path).Docs[0].Body["m_AddressableAssetSettingsGuid"]);
                settingsPath = AssetDb.ByGuid(g)?.Path;
            }
            if (settingsPath == null)
            {
                var sg = AddrScript("AddressableAssetSettings");
                settingsPath = AssetDb.All.Where(x => x.Path.StartsWith("Assets/") && x.Path.EndsWith(".asset"))
                    .FirstOrDefault(x => File.ReadLines(Ua.Abs(x.Path)).Take(20).Any(l => l.Contains(sg)))?.Path;
            }
            if (settingsPath == null) { if (required) throw new UaError("no Addressables settings in the project (addr.init creates them)"); return null; }
            var c = new AddrCtx { Settings = UFile.Load(settingsPath), Folder = Path.GetDirectoryName(settingsPath)!.Replace('\\', '/') };
            foreach (var r in (c.S["m_GroupAssets"] as YSeq)?.Items.OfType<YRef>() ?? Enumerable.Empty<YRef>())
            {
                var ga = AssetDb.ByGuid(r.Guid);
                if (ga == null) continue;
                var gf = UFile.Load(ga.Path);
                c.Groups.Add((gf, gf.Docs[0].Body));
            }
            return c;
        }

        static (UFile file, YMap body) Group(AddrCtx c, string name) =>
            c.Groups.FirstOrDefault(g => string.Equals(Serializer.Str(g.body["m_GroupName"]), name, StringComparison.OrdinalIgnoreCase)) is var g && g.file != null
                ? g : throw new UaError("no Addressables group " + name);

        static IEnumerable<(UFile file, YMap group, YMap entry)> Entries(AddrCtx c) =>
            c.Groups.SelectMany(g => ((g.body["m_SerializeEntries"] as YSeq)?.Items.OfType<YMap>() ?? Enumerable.Empty<YMap>()).Select(e => (g.file, g.body, e)));

        static JsonObject EntryJson(YMap group, YMap e)
        {
            var guid = Serializer.Str(e["m_GUID"]);
            return new JsonObject
            {
                ["group"] = Serializer.Str(group["m_GroupName"]), ["guid"] = guid, ["asset"] = AssetDb.ByGuid(guid)?.Path, ["address"] = Serializer.Str(e["m_Address"]),
                ["labels"] = Arr((e["m_SerializedLabels"] as YSeq)?.Items.Select(Serializer.Str) ?? Enumerable.Empty<string>(), J),
                ["missing"] = AssetDb.ByGuid(guid) == null,
            };
        }

        static YMap MonoBody(string scriptGuid, string name) => new YMap()
            .Add("m_ObjectHideFlags", "0").Add("m_CorrespondingSourceObject", new YRef(0, null, 0)).Add("m_PrefabInstance", new YRef(0, null, 0))
            .Add("m_PrefabAsset", new YRef(0, null, 0)).Add("m_GameObject", new YRef(0, null, 0)).Add("m_Enabled", "1").Add("m_EditorHideFlags", "0")
            .Add("m_Script", new YRef(11500000, scriptGuid, 3)).Add("m_Name", name).Add("m_EditorClassIdentifier", "");

        static string NewAsset(string rel, YMap body)
        {
            var f = UFile.New(rel);
            f.Add(114, "MonoBehaviour", body, 11400000);
            EnsureFolder(Path.GetDirectoryName(rel)!.Replace('\\', '/'));
            f.Save();
            return WriteMeta(rel);
        }

        static string ProfileVar(AddrCtx c, string name)
        {
            var names = ((c.S["m_ProfileSettings"] as YMap)?["m_ProfileEntryNames"] as YSeq)?.Items.OfType<YMap>();
            return names?.FirstOrDefault(n => Serializer.Str(n["m_Name"]) == name) is YMap m ? Serializer.Str(m["m_Id"]) : "";
        }

        /// <summary>New group asset (+ Bundled / ContentUpdate schemas for "packed").</summary>
        static string CreateGroup(AddrCtx c, string name, bool packed)
        {
            var file = $"{c.Folder}/AssetGroups/{name}.asset";
            if (File.Exists(Ua.Abs(file))) throw new UaError("group asset exists: " + file);
            var groupGuid = Guid.NewGuid().ToString("N");
            var settingsRef = new YRef(11400000, AssetDb.ByPath(c.Settings.Rel).Guid, 2);
            var schemas = new YSeq();
            var body = MonoBody(AddrScript("AddressableAssetGroup"), name)
                .Add("m_GroupName", name).Add("m_GUID", groupGuid).Add("m_SerializeEntries", new YSeq()).Add("m_ReadOnly", "0")
                .Add("m_Settings", settingsRef).Add("m_SchemaSet", new YMap().Add("m_Schemas", schemas));
            var guid = NewAsset(file, body);
            if (packed)
            {
                var groupRef = new YRef(11400000, guid, 2);
                var bundled = MonoBody(AddrScript("BundledAssetGroupSchema"), name + "_BundledAssetGroupSchema")
                    .Add("m_Group", groupRef).Add("m_InternalBundleIdMode", "1").Add("m_Compression", "1").Add("m_IncludeAddressInCatalog", "1")
                    .Add("m_IncludeGUIDInCatalog", "1").Add("m_IncludeLabelsInCatalog", "1").Add("m_InternalIdNamingMode", "0").Add("m_CacheClearBehavior", "0")
                    .Add("m_IncludeInBuild", "1").Add("m_ForceUniqueProvider", "0").Add("m_UseAssetBundleCache", "1").Add("m_UseAssetBundleCrc", "1")
                    .Add("m_UseAssetBundleCrcForCachedBundles", "1").Add("m_UseUWRForLocalBundles", "0").Add("m_Timeout", "0").Add("m_ChunkedTransfer", "0")
                    .Add("m_RedirectLimit", "-1").Add("m_RetryCount", "0").Add("m_BuildPath", new YMap().Add("m_Id", ProfileVar(c, "Local.BuildPath")))
                    .Add("m_LoadPath", new YMap().Add("m_Id", ProfileVar(c, "Local.LoadPath"))).Add("m_BundleMode", "0")
                    .Add("m_BundleNaming", "0").Add("m_AssetLoadMode", "0");
                var sg = NewAsset($"{c.Folder}/AssetGroups/Schemas/{name}_BundledAssetGroupSchema.asset", bundled);
                var cu = MonoBody(AddrScript("ContentUpdateGroupSchema"), name + "_ContentUpdateGroupSchema").Add("m_Group", groupRef).Add("m_StaticContent", "0");
                var cg = NewAsset($"{c.Folder}/AssetGroups/Schemas/{name}_ContentUpdateGroupSchema.asset", cu);
                schemas.Items.Add(new YRef(11400000, sg, 2)); schemas.Items.Add(new YRef(11400000, cg, 2));
                var gf = UFile.Load(file); gf.Docs[0].Body["m_SchemaSet"] = new YMap().Add("m_Schemas", schemas); gf.Dirty = true; gf.Save();
            }
            var list = c.S["m_GroupAssets"] as YSeq ?? new YSeq();
            c.S["m_GroupAssets"] = list;
            list.Items.Add(new YRef(11400000, guid, 2));
            c.Settings.Dirty = true; c.Settings.Save();
            return guid;
        }

        static void RegisterAddressables()
        {
            Reg("addr.info", "addr.info   settings, groups (entries, schemas), labels", a =>
            {
                var c = Addr(false);
                if (c == null) return new JsonObject { ["initialized"] = false };
                var defGuid = Serializer.Str(c.S["m_DefaultGroup"]);
                return new JsonObject
                {
                    ["initialized"] = true, ["settings"] = c.Settings.Rel,
                    ["labels"] = Values.ToJson((c.S["m_LabelTable"] as YMap)?["m_LabelNames"]),
                    ["groups"] = Arr(c.Groups, g => new JsonObject
                    {
                        ["name"] = Serializer.Str(g.body["m_GroupName"]), ["asset"] = g.file.Rel, ["guid"] = Serializer.Str(g.body["m_GUID"]),
                        ["default"] = Serializer.Str(g.body["m_GUID"]) == defGuid, ["entries"] = (g.body["m_SerializeEntries"] as YSeq)?.Items.Count ?? 0,
                        ["schemas"] = Arr((((g.body["m_SchemaSet"] as YMap)?["m_Schemas"]) as YSeq)?.Items.OfType<YRef>() ?? Enumerable.Empty<YRef>(), r => J(AssetDb.ByGuid(r.Guid)?.Path ?? r.Guid)),
                    }),
                };
            });
            Reg("addr.init", "addr.init [--folder Assets/AddressableAssetsData]   create settings + Default Local Group (best effort: open once in Unity to let Addressables finish its own upgrade)", a =>
            {
                if (Addr(false) != null) throw new UaError("Addressables settings already exist");
                var folder = Ua.Rel(a.O("folder", "Assets/AddressableAssetsData"));
                string G() => Guid.NewGuid().ToString("N");
                var vars = new[] { ("BuildTarget", "[UnityEditor.EditorUserBuildSettings.activeBuildTarget]"),
                    ("Local.BuildPath", "[UnityEngine.AddressableAssets.Addressables.BuildPath]/[BuildTarget]"),
                    ("Local.LoadPath", "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/[BuildTarget]"),
                    ("Remote.BuildPath", "ServerData/[BuildTarget]"), ("Remote.LoadPath", "http://[PrivateIpAddress]:[HostingServicePort]") }
                    .Select(v => (id: G(), name: v.Item1, value: v.Item2)).ToList();
                var profileId = G();
                var values = new YSeq(); var names = new YSeq();
                foreach (var v in vars)
                {
                    values.Items.Add(new YMap().Add("m_Id", v.id).Add("m_Value", v.value));
                    names.Items.Add(new YMap().Add("m_Id", v.id).Add("m_Name", v.name).Add("m_InlineUsage", "0"));
                }
                var profiles = new YSeq();
                profiles.Items.Add(new YMap().Add("m_InheritedParent", "").Add("m_Id", profileId).Add("m_ProfileName", "Default").Add("m_Values", values));
                // data builders (play mode scripts / build script)
                var builders = new YSeq();
                foreach (var (cls, nm) in new[] { ("BuildScriptFastMode", "BuildScriptFastMode"), ("BuildScriptVirtualMode", "BuildScriptVirtualMode"),
                                                  ("BuildScriptPackedPlayMode", "BuildScriptPackedPlayMode"), ("BuildScriptPackedMode", "BuildScriptPackedMode") })
                    builders.Items.Add(new YRef(11400000, NewAsset($"{folder}/DataBuilders/{nm}.asset", MonoBody(AddrScript(cls), nm).Add("instanceProviderType", new YMap().Add("m_AssemblyName", "").Add("m_ClassName", "")).Add("sceneProviderType", new YMap().Add("m_AssemblyName", "").Add("m_ClassName", ""))), 2));
                var settings = MonoBody(AddrScript("AddressableAssetSettings"), "AddressableAssetSettings")
                    .Add("m_DefaultGroup", "").Add("m_currentHash", new YMap().Add("serializedVersion", "2").Add("Hash", "00000000000000000000000000000000"))
                    .Add("m_OptimizeCatalogSize", "0").Add("m_BuildRemoteCatalog", "0").Add("m_CatalogRequestsTimeout", "0").Add("m_DisableCatalogUpdateOnStart", "0")
                    .Add("m_InternalIdNamingMode", "0").Add("m_InternalBundleIdMode", "1").Add("m_AssetLoadMode", "0").Add("m_IgnoreUnsupportedFilesInBuild", "0")
                    .Add("m_UniqueBundleIds", "0").Add("m_EnableJsonCatalog", "0").Add("m_NonRecursiveBuilding", "1").Add("m_CCDEnabled", "0")
                    .Add("m_maxConcurrentWebRequests", "3").Add("m_UseUWRForLocalBundles", "0").Add("m_BundleTimeout", "0").Add("m_BundleRetryCount", "0")
                    .Add("m_BundleRedirectLimit", "-1").Add("m_SharedBundleSettings", "0").Add("m_SharedBundleSettingsCustomGroupIndex", "0").Add("m_ContiguousBundles", "1")
                    .Add("m_StripUnityVersionFromBundleBuild", "0").Add("m_DisableVisibleSubAssetRepresentations", "0").Add("m_BuiltInBundleNaming", "0")
                    .Add("mBuiltInBundleCustomNaming", "").Add("m_MonoScriptBundleNaming", "0").Add("m_CheckForContentUpdateRestrictionsOption", "0")
                    .Add("m_MonoScriptBundleCustomNaming", "").Add("m_RemoteCatalogBuildPath", new YMap().Add("m_Id", vars[3].id))
                    .Add("m_RemoteCatalogLoadPath", new YMap().Add("m_Id", vars[4].id)).Add("m_ContentStateBuildPathProfileVariableName", "")
                    .Add("m_CustomContentStateBuildPath", "").Add("m_ContentStateBuildPath", "").Add("m_BuildAddressablesWithPlayerBuild", "0")
                    .Add("m_overridePlayerVersion", "[UnityEditor.PlayerSettings.bundleVersion]").Add("m_GroupAssets", new YSeq())
                    .Add("m_BuildSettings", new YMap().Add("m_LogResourceManagerExceptions", "1").Add("m_BundleBuildPath", "Temp/com.unity.addressables/AssetBundles"))
                    .Add("m_ProfileSettings", new YMap().Add("m_Profiles", profiles).Add("m_ProfileEntryNames", names).Add("m_ProfileVersion", "1"))
                    .Add("m_LabelTable", new YMap().Add("m_LabelNames", new YSeq { Items = { new YScalar("default") } }))
                    .Add("m_SchemaTemplates", new YSeq()).Add("m_GroupTemplateObjects", new YSeq()).Add("m_InitializationObjects", new YSeq())
                    .Add("m_ActivePlayerDataBuilderIndex", "3").Add("m_DataBuilders", builders).Add("m_ActiveProfileId", profileId);
                var settingsGuid = NewAsset($"{folder}/AddressableAssetSettings.asset", settings);
                NewAsset($"{folder}/DefaultObject.asset", MonoBody(AddrScript("AddressableAssetSettingsDefaultObject"), "DefaultObject").Add("m_AddressableAssetSettingsGuid", settingsGuid));
                AssetDb.Reset();
                var c = Addr();
                // "Built In Data" (player data) + "Default Local Group"
                CreateGroup(c, "Default Local Group", true);
                c = Addr();
                c.S["m_DefaultGroup"] = new YScalar(Serializer.Str(Group(c, "Default Local Group").body["m_GUID"]));
                c.Settings.Dirty = true; c.Settings.Save();
                return new JsonObject { ["settings"] = $"{folder}/AddressableAssetSettings.asset", ["defaultGroup"] = "Default Local Group" };
            });
            Reg("addr.group.create", "addr.group.create <name> [--empty]   new group (packed: Bundled + ContentUpdate schemas)", a =>
            {
                var c = Addr();
                if (c.Groups.Any(g => Serializer.Str(g.body["m_GroupName"]) == a.P(0, "name"))) throw new UaError("group exists");
                return new JsonObject { ["group"] = a.P(0), ["guid"] = CreateGroup(c, a.P(0), !a.Has("empty")) };
            });
            Reg("addr.group.remove", "addr.group.remove <name>   delete the group asset + its schemas", a =>
            {
                var c = Addr();
                var g = Group(c, a.P(0, "name"));
                var ga = AssetDb.ByPath(g.file.Rel);
                (c.S["m_GroupAssets"] as YSeq)?.Items.RemoveAll(r => (r as YRef)?.Guid == ga.Guid);
                c.Settings.Dirty = true; c.Settings.Save();
                var schemas = ((g.body["m_SchemaSet"] as YMap)?["m_Schemas"] as YSeq)?.Items.OfType<YRef>().Select(r => AssetDb.ByGuid(r.Guid)).Where(x => x != null).ToList() ?? new();
                foreach (var x in schemas.Append(ga))
                    { Ua.Delete(Ua.Abs(x.Path)); Ua.Delete(Ua.Abs(x.Path) + ".meta"); }
                AssetDb.Reset();
                return new JsonObject { ["removed"] = a.P(0), ["entriesDropped"] = (g.body["m_SerializeEntries"] as YSeq)?.Items.Count ?? 0 };
            });
            Reg("addr.group.rename", "addr.group.rename <name> <newName>", a =>
            {
                var c = Addr();
                var g = Group(c, a.P(0, "name"));
                g.body["m_GroupName"] = new YScalar(a.P(1, "newName")); g.body["m_Name"] = new YScalar(a.P(1));
                g.file.Dirty = true; g.file.Save();
                return new JsonObject { ["group"] = a.P(1), ["asset"] = g.file.Rel };
            });
            Reg("addr.group.default", "addr.group.default <name>", a =>
            {
                var c = Addr();
                var g = Group(c, a.P(0, "name"));
                c.S["m_DefaultGroup"] = new YScalar(Serializer.Str(g.body["m_GUID"])); c.Settings.Dirty = true; c.Settings.Save();
                return new JsonObject { ["default"] = a.P(0) };
            });
            Reg("addr.entries", "addr.entries [--group g] [--label l] [--asset path]", a =>
            {
                var c = Addr();
                var q = Entries(c);
                if (a.O("group") is string gn) q = q.Where(x => string.Equals(Serializer.Str(x.group["m_GroupName"]), gn, StringComparison.OrdinalIgnoreCase));
                if (a.O("label") is string lb) q = q.Where(x => ((x.entry["m_SerializedLabels"] as YSeq)?.Items.Select(Serializer.Str) ?? Enumerable.Empty<string>()).Contains(lb));
                if (a.O("asset") is string ap) { var g = AssetDb.Need(ap).Guid; q = q.Where(x => Serializer.Str(x.entry["m_GUID"]) == g); }
                var list = q.ToList();
                return new JsonObject { ["count"] = list.Count, ["entries"] = Arr(list, x => EntryJson(x.group, x.entry)) };
            });
            Reg("addr.entry.add", "addr.entry.add <asset|folder>... [--group g] [--address a] [--labels l1,l2] [--simplify]   make assets addressable (moves existing entries)", a =>
            {
                var c = Addr();
                var g = a.O("group") is string gn ? Group(c, gn) : c.Groups.First(x => Serializer.Str(x.body["m_GUID"]) == Serializer.Str(c.S["m_DefaultGroup"]));
                var labels = a.O("labels")?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                var known = ((c.S["m_LabelTable"] as YMap)?["m_LabelNames"] as YSeq) ?? new YSeq();
                foreach (var l in labels) if (!known.Items.Any(k => Serializer.Str(k) == l)) known.Items.Add(new YScalar(l));
                var added = new JsonArray();
                foreach (var p in a.Pos)
                {
                    var x = AssetDb.Need(p);
                    // remove from any group first (an asset lives in one group)
                    foreach (var (file, group, entry) in Entries(c).ToList())
                        if (Serializer.Str(entry["m_GUID"]) == x.Guid) { ((YSeq)group["m_SerializeEntries"]).Items.Remove(entry); file.Dirty = true; }
                    var addr = a.O("address") ?? (a.Has("simplify") ? Path.GetFileNameWithoutExtension(x.Path) : x.Path);
                    var lseq = new YSeq(); foreach (var l in labels) lseq.Items.Add(new YScalar(l));
                    var e = new YMap().Add("m_GUID", x.Guid).Add("m_Address", addr).Add("m_ReadOnly", "0").Add("m_SerializedLabels", lseq).Add("FlaggedDuringContentUpdateRestriction", "0");
                    var list = g.body["m_SerializeEntries"] as YSeq ?? new YSeq(); g.body["m_SerializeEntries"] = list;
                    list.Items.Add(e);
                    g.file.Dirty = true;
                    added.Add(EntryJson(g.body, e));
                }
                foreach (var gg in c.Groups) gg.file.Save();
                c.Settings.Dirty = true; c.Settings.Save();
                return new JsonObject { ["added"] = added };
            });
            Reg("addr.entry.remove", "addr.entry.remove <asset>...", a =>
            {
                var c = Addr();
                int n = 0;
                foreach (var p in a.Pos)
                {
                    var g = AssetDb.Resolve(p)?.Guid ?? p;
                    foreach (var (file, group, entry) in Entries(c).ToList())
                        if (Serializer.Str(entry["m_GUID"]) == g) { ((YSeq)group["m_SerializeEntries"]).Items.Remove(entry); file.Dirty = true; n++; }
                }
                foreach (var gg in c.Groups) gg.file.Save();
                return new JsonObject { ["removed"] = n };
            });
            Reg("addr.entry.set", "addr.entry.set <asset> [--address a] [--labels l1,l2 | --add-labels l | --remove-labels l] [--group g]", a =>
            {
                var c = Addr();
                var x = AssetDb.Need(a.P(0, "asset"));
                var hit = Entries(c).FirstOrDefault(e => Serializer.Str(e.entry["m_GUID"]) == x.Guid);
                if (hit.entry == null) throw new UaError(x.Path + " is not addressable");
                var e = hit.entry;
                if (a.O("address") is string ad) e["m_Address"] = new YScalar(ad);
                var labels = ((e["m_SerializedLabels"] as YSeq)?.Items.Select(Serializer.Str) ?? Enumerable.Empty<string>()).ToList();
                if (a.O("labels") is string ls) labels = ls.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (a.O("add-labels") is string al) labels.AddRange(al.Split(',').Where(l => l.Length > 0 && !labels.Contains(l)));
                if (a.O("remove-labels") is string rl) labels.RemoveAll(l => rl.Split(',').Contains(l));
                var known = ((c.S["m_LabelTable"] as YMap)?["m_LabelNames"] as YSeq) ?? new YSeq();
                foreach (var l in labels) if (!known.Items.Any(k => Serializer.Str(k) == l)) { known.Items.Add(new YScalar(l)); c.Settings.Dirty = true; }
                var lseq = new YSeq(); foreach (var l in labels) lseq.Items.Add(new YScalar(l));
                e["m_SerializedLabels"] = lseq;
                hit.file.Dirty = true;
                if (a.O("group") is string gn)
                {
                    var g = Group(c, gn);
                    ((YSeq)hit.group["m_SerializeEntries"]).Items.Remove(e);
                    var list = g.body["m_SerializeEntries"] as YSeq ?? new YSeq(); g.body["m_SerializeEntries"] = list;
                    list.Items.Add(e); g.file.Dirty = true;
                    hit = (g.file, g.body, e);
                }
                foreach (var gg in c.Groups) gg.file.Save();
                c.Settings.Save();
                return EntryJson(hit.group, e);
            });
            Reg("addr.label.add", "addr.label.add <label>", a =>
            {
                var c = Addr();
                var known = (c.S["m_LabelTable"] as YMap)?["m_LabelNames"] as YSeq ?? throw new UaError("settings have no label table");
                if (!known.Items.Any(k => Serializer.Str(k) == a.P(0, "label"))) known.Items.Add(new YScalar(a.P(0)));
                c.Settings.Dirty = true; c.Settings.Save();
                return new JsonObject { ["labels"] = Values.ToJson(known) };
            });
            Reg("addr.label.remove", "addr.label.remove <label>   (also strips it from entries)", a =>
            {
                var c = Addr();
                var l = a.P(0, "label");
                var known = (c.S["m_LabelTable"] as YMap)?["m_LabelNames"] as YSeq;
                known?.Items.RemoveAll(k => Serializer.Str(k) == l);
                int stripped = 0;
                foreach (var (file, group, entry) in Entries(c))
                    if (entry["m_SerializedLabels"] is YSeq s && s.Items.RemoveAll(k => Serializer.Str(k) == l) > 0) { file.Dirty = true; stripped++; }
                foreach (var gg in c.Groups) gg.file.Save();
                c.Settings.Dirty = true; c.Settings.Save();
                return new JsonObject { ["removed"] = l, ["entriesChanged"] = stripped };
            });
            Reg("addr.schema.get", "addr.schema.get <group> <SchemaType> [propertyPath]   e.g. BundledAssetGroupSchema m_Compression", a =>
            {
                var c = Addr();
                var f = SchemaFile(Group(c, a.P(0, "group")).body, a.P(1, "SchemaType"));
                return new JsonObject { ["schema"] = f.Rel, ["value"] = Values.ToJson(Values.Get(f.Docs[0].Body, a.P(2) ?? "")) };
            });
            Reg("addr.schema.set", "addr.schema.set <group> <SchemaType> <propertyPath> <json>", a =>
            {
                var c = Addr();
                var f = SchemaFile(Group(c, a.P(0, "group")).body, a.P(1, "SchemaType"));
                var v = Values.Set(f.Docs[0].Body, a.P(2, "propertyPath"), Values.Parse(a.P(3, "json")), null);
                f.Dirty = true; f.Save();
                return new JsonObject { ["schema"] = f.Rel, ["value"] = Values.ToJson(v) };
            });
            Reg("addr.analyze", "addr.analyze   duplicated implicit dependencies, missing entries, scenes that are addressable and in Build Settings", a =>
            {
                var c = Addr();
                var entries = Entries(c).ToList();
                var explicitGuids = new HashSet<string>(entries.Select(e => Serializer.Str(e.entry["m_GUID"])));
                var pulledBy = new Dictionary<string, HashSet<string>>();
                var memo = new Dictionary<string, HashSet<string>>();
                HashSet<string> Closure(string guid, HashSet<string> stack)
                {
                    if (memo.TryGetValue(guid, out var m)) return m;
                    var res = new HashSet<string>();
                    var x = AssetDb.ByGuid(guid);
                    if (x == null || x.Folder || !UFile.IsYaml(Ua.Abs(x.Path)) || !stack.Add(guid)) return res;
                    foreach (var d in AssetDb.GuidsIn(x.Path).Distinct())
                    {
                        if (d == guid || AssetDb.IsBuiltin(d) || AssetDb.ByGuid(d) == null) continue;
                        if (AssetDb.ByGuid(d).Path.EndsWith(".cs")) continue;   // scripts are not bundled
                        res.Add(d);
                        if (!explicitGuids.Contains(d)) res.UnionWith(Closure(d, stack));
                    }
                    stack.Remove(guid);
                    return memo[guid] = res;
                }
                foreach (var (file, group, entry) in entries)
                {
                    var gname = Serializer.Str(group["m_GroupName"]);
                    foreach (var d in Closure(Serializer.Str(entry["m_GUID"]), new HashSet<string>()))
                    {
                        if (explicitGuids.Contains(d)) continue;
                        if (!pulledBy.TryGetValue(d, out var set)) pulledBy[d] = set = new HashSet<string>();
                        set.Add(gname);
                    }
                }
                var dups = pulledBy.Where(kv => kv.Value.Count > 1).OrderBy(kv => AssetDb.ByGuid(kv.Key)?.Path).ToList();
                var buildScenes = File.Exists(Ua.Abs("ProjectSettings/EditorBuildSettings.asset"))
                    ? ((UFile.Load("ProjectSettings/EditorBuildSettings.asset").Docs[0].Body["m_Scenes"] as YSeq)?.Items.OfType<YMap>().Select(s => Serializer.Str(s["guid"])).ToHashSet() ?? new HashSet<string>()) : new HashSet<string>();
                return new JsonObject
                {
                    ["entries"] = entries.Count,
                    ["duplicatedDependencies"] = Arr(dups, kv => new JsonObject { ["asset"] = AssetDb.ByGuid(kv.Key)?.Path, ["groups"] = Arr(kv.Value.OrderBy(x => x), J) }),
                    ["missingEntries"] = Arr(entries.Where(e => AssetDb.ByGuid(Serializer.Str(e.entry["m_GUID"])) == null), e => EntryJson(e.group, e.entry)),
                    ["scenesAlsoInBuildSettings"] = Arr(entries.Where(e => buildScenes.Contains(Serializer.Str(e.entry["m_GUID"]))), e => J(AssetDb.ByGuid(Serializer.Str(e.entry["m_GUID"]))?.Path)),
                    ["duplicateAddresses"] = Arr(entries.GroupBy(e => Serializer.Str(e.entry["m_Address"])).Where(g => g.Count() > 1), g => new JsonObject { ["address"] = g.Key, ["count"] = g.Count() }),
                };
            });
        }

        static UFile SchemaFile(YMap group, string type)
        {
            foreach (var r in ((group["m_SchemaSet"] as YMap)?["m_Schemas"] as YSeq)?.Items.OfType<YRef>() ?? Enumerable.Empty<YRef>())
            {
                var x = AssetDb.ByGuid(r.Guid);
                if (x == null) continue;
                var f = UFile.Load(x.Path);
                var sg = ScriptGuid(f.Docs[0].Body);
                if (AssetDb.ByGuid(sg) is AssetInfo s && Path.GetFileNameWithoutExtension(s.Path).Equals(type, StringComparison.OrdinalIgnoreCase)) return f;
                if (AddrScripts.TryGetValue(type, out var known) && known == sg) return f;
            }
            throw new UaError($"group {Serializer.Str(group["m_GroupName"])} has no {type}");
        }
    }
}
