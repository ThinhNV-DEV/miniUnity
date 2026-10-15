// Scene / prefab editing commands for agents: Hierarchy, GameObjects, Transforms, components, serialized fields,
// prefab instances (instantiate, overrides, revert), prefab creation, ScriptableObjects.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MiniUnity.Yaml;

namespace MiniUnity.Agent
{
    public static partial class UaCommands
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static YRef Local(long id) => new YRef(id, null, 0);
        static long Fid(YNode n) => (n as YRef)?.FileID ?? 0;

        /// <summary>Resolver for {"$ref": "go:Path"} values inside file f (GameObject, or one of its components).</summary>
        public static Values.LocalResolver LocalResolver(UFile f) => (target, component) =>
        {
            var g = SceneGraph.Build(f);
            var n = g.Need(target);
            if (component == null) return Local(n.Go);
            var c = g.Comp(n, component) ?? throw new UaError($"{target}: no component {component}");
            return Local(c.Id);
        };

        static JsonObject NodeJson(GNode n, bool comps, int depth, int maxDepth)
        {
            var go = n.GoBody();
            var o = new JsonObject { ["name"] = n.Name, ["path"] = n.Path, ["id"] = n.Go };
            if (Serializer.Str(go["m_IsActive"]) == "0") o["active"] = false;
            if (n.Virtual) { o["prefab"] = AssetDb.ByGuid(n.SrcGuid)?.Path ?? n.SrcGuid; if (n.IsInstanceRoot) o["instance"] = n.Instance; else o["inInstance"] = true; }
            if (comps) o["components"] = Arr(n.Comps, c => J(c.Type));
            if (n.Children.Count > 0)
            {
                if (depth < maxDepth) o["children"] = Arr(n.Children, c => NodeJson(c, comps, depth + 1, maxDepth));
                else o["childCount"] = n.Children.Count;
            }
            return o;
        }

        static JsonObject CompJson(GComp c, string path = null, bool full = true)
        {
            var body = c.Effective();
            var o = new JsonObject { ["type"] = c.Type, ["id"] = c.Id, ["class"] = c.ClassId };
            if (c.Virtual) o["fromPrefab"] = true;
            if (path != null) { o["path"] = path; o["value"] = Values.ToJson(Values.Get(body, path)); return o; }
            if (full)
            {
                var fields = new JsonObject();
                foreach (var e in body.Entries)
                {
                    if (e.Key is "m_ObjectHideFlags" or "m_CorrespondingSourceObject" or "m_PrefabInstance" or "m_PrefabAsset" or "m_GameObject" or "m_EditorHideFlags" or "m_EditorClassIdentifier") continue;
                    fields[e.Key] = Values.ToJson(e.Value);
                }
                o["fields"] = fields;
            }
            return o;
        }

        static (UFile f, SceneGraph g) Open(UaArgs a)
        {
            var f = UFile.Load(a.P(0, "file"));
            SceneGraph.ClearCache();
            return (f, SceneGraph.Build(f));
        }

        static void RegisterScene()
        {
            Reg("scene.list", "scene.list   every .unity in the project (+ build index)", a =>
            {
                var build = new List<string>();
                if (File.Exists(Ua.Abs("ProjectSettings/EditorBuildSettings.asset")))
                    build = (UFile.Load("ProjectSettings/EditorBuildSettings.asset").Docs[0].Body["m_Scenes"] as YSeq)?.Items.OfType<YMap>().Select(m => Serializer.Str(m["path"])).ToList() ?? build;
                return new JsonObject
                {
                    ["scenes"] = Arr(AssetDb.All.Where(x => x.Path.EndsWith(".unity")).OrderBy(x => x.Path),
                        x => new JsonObject { ["path"] = x.Path, ["guid"] = x.Guid, ["buildIndex"] = build.IndexOf(x.Path) })
                };
            });
            Reg("scene.new", "scene.new <path.unity> [--basic]   empty scene (--basic: Main Camera + Directional Light)", a =>
            {
                var rel = Ua.Rel(a.P(0, "path"));
                if (File.Exists(Ua.Abs(rel))) throw new UaError("exists: " + rel);
                var f = UFile.New(rel);
                f.Docs.AddRange(YamlReader.ReadDocs(YamlWriter.Header + SceneWriter.SceneSettingsText));
                var roots = new YSeq();
                f.Docs.Add(new YDoc { ClassId = 1660057539, FileId = 9223372036854775807, TypeName = "SceneRoots", Body = new YMap().Add("m_ObjectHideFlags", "0").Add("m_Roots", roots) });
                var reload = UFile.New(rel); reload.Docs = f.Docs;
                EnsureFolder(Path.GetDirectoryName(rel).Replace('\\', '/'));
                Ua.Write(Ua.Abs(rel), f.Text());
                WriteMeta(rel);
                if (a.Has("basic"))
                {
                    var g = UFile.Load(rel);
                    var cam = CreateGo(g, "Main Camera", null, false, new[] { "Camera", "AudioListener" });
                    SetGoField(g, cam, "m_TagString", "MainCamera");
                    SetTr(g, cam, "m_LocalPosition", new YMap { Flow = true }.Add("x", "0").Add("y", "1").Add("z", "-10"));
                    var light = CreateGo(g, "Directional Light", null, false, new[] { "Light" });
                    SetTr(g, light, "m_LocalRotation", new YMap { Flow = true }.Add("x", "0.40821788").Add("y", "-0.23456968").Add("z", "0.10938163").Add("w", "0.8754261"));
                    SetTr(g, light, "m_LocalPosition", new YMap { Flow = true }.Add("x", "0").Add("y", "3").Add("z", "0"));
                    g.Save();
                }
                return new JsonObject { ["path"] = rel };
            });
            Reg("go.tree", "go.tree <file> [--root path] [--depth n] [--components]   Hierarchy of a scene or prefab", a =>
            {
                var (f, g) = Open(a);
                int depth = a.I("depth", 64);
                var roots = a.O("root") is string r ? new List<GNode> { g.Need(r) } : g.Roots;
                return new JsonObject { ["file"] = f.Rel, ["count"] = g.All().Count(), ["roots"] = Arr(roots, n => NodeJson(n, a.Has("components"), 0, depth)) };
            });
            Reg("go.find", "go.find <file> [--name glob] [--component Type] [--tag t] [--layer n|Name] [--active 0|1] [--where 'Comp.path>1; Comp2.path==x']   search the Hierarchy; --where compares serialized values (== != > >= < <= ~=, null for an unassigned reference)", a =>
            {
                var (f, g) = Open(a);
                var rx = a.O("name") is string nm ? Ua.Glob(nm) : null;
                var q = g.All().Where(n => rx == null || rx.IsMatch(n.Name));
                if (a.O("component") is string ct) q = q.Where(n => g.Comp(n, ct) != null);
                if (a.O("tag") is string tag) q = q.Where(n => Serializer.Str(n.GoBody()["m_TagString"]) == tag);
                if (a.O("layer") is string ly) { int li = LayerIndex(ly); q = q.Where(n => Serializer.Int(n.GoBody()["m_Layer"], 0) == li); }
                if (a.O("active") is string act) { bool want = act is "1" or "true"; q = q.Where(n => (Serializer.Str(n.GoBody()["m_IsActive"]) != "0") == want); }
                if (a.O("where") is string wh) { var pred = Where(wh); q = q.Where(n => pred(g, n)); }
                return new JsonObject { ["file"] = f.Rel, ["matches"] = Arr(q, n => new JsonObject { ["path"] = n.Path, ["id"] = n.Go, ["components"] = Arr(n.Comps, c => J(c.Type)) }) };
            });
            Reg("go.get", "go.get <file> <go>   GameObject, Transform and component list", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                var go = n.GoBody();
                var o = NodeJson(n, true, 0, 1);
                o["layer"] = Values.ToJson(go["m_Layer"]); o["tag"] = Serializer.Str(go["m_TagString"]);
                o["active"] = Serializer.Str(go["m_IsActive"]) != "0"; o["static"] = Values.ToJson(go["m_StaticEditorFlags"]);
                var tr = n.Comps.FirstOrDefault(c => c.ClassId is 4 or 224);
                if (tr != null) o["transform"] = CompJson(tr);
                o["components"] = Arr(n.Comps, c => new JsonObject { ["type"] = c.Type, ["id"] = c.Id, ["enabled"] = Serializer.Str(c.Effective()["m_Enabled"]) != "0" });
                return o;
            });
            Reg("go.create", "go.create <file> <name> [--parent go] [--rect] [--components A,B] [--position '{\"x\":0,\"y\":1,\"z\":0}']", a =>
            {
                var f = UFile.Load(a.P(0, "file"));
                var comps = a.O("components")?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                var id = CreateGo(f, a.P(1, "name"), a.O("parent"), a.Has("rect"), comps);
                if (a.O("position") is string pos) SetTr(f, id, a.Has("rect") ? "m_AnchoredPosition" : "m_LocalPosition", Values.FromJson(Values.Parse(pos), null, null));
                f.Save();
                var g = SceneGraph.Build(f);
                return NodeJson(g.ByGo[id], true, 0, 0);
            });
            Reg("go.delete", "go.delete <file> <go>   GameObject + children (+ nested prefab instances); objects inside a prefab instance become removed-overrides", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                var path = n.Path;
                DeleteNode(f, g, n);
                f.Save();
                return new JsonObject { ["deleted"] = path };
            });
            Reg("go.rename", "go.rename <file> <go> <newName>", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                SetGoProp(f, n, "m_Name", new YScalar(a.P(2, "newName")));
                f.Save();
                return new JsonObject { ["path"] = SceneGraph.Build(f).ByGo[n.Go].Path };
            });
            Reg("go.set", "go.set <file> <go> [--active 0|1] [--layer n|Name] [--tag t] [--static n]", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                if (a.O("active") is string ac) SetGoProp(f, n, "m_IsActive", new YScalar(ac is "1" or "true" ? "1" : "0"));
                if (a.O("layer") is string ly) SetGoProp(f, n, "m_Layer", new YScalar(LayerIndex(ly).ToString(Inv)));
                if (a.O("tag") is string tg) SetGoProp(f, n, "m_TagString", new YScalar(tg));
                if (a.O("static") is string st) SetGoProp(f, n, "m_StaticEditorFlags", new YScalar(st == "true" ? "4294967295" : st));
                f.Save();
                return NodeJson(SceneGraph.Build(f).ByGo[n.Go], true, 0, 0);
            });
            Reg("go.move", "go.move <file> <go> [--parent go | --root] [--index n]   reparent / reorder (keeps local values)", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                if (n.Virtual && !n.IsInstanceRoot) throw new UaError("objects inside a prefab instance cannot be moved (unpack the instance first)");
                GNode parent = a.Has("root") ? null : a.O("parent") is string pp ? g.Need(pp) : n.Parent;
                for (var p = parent; p != null; p = p.Parent) if (p == n) throw new UaError("cannot parent an object under itself");
                Detach(f, g, n);
                Attach(f, parent, n, a.I("index", -1));
                f.Save();
                return new JsonObject { ["path"] = SceneGraph.Build(f).ByGo[n.Go].Path };
            });
            Reg("go.duplicate", "go.duplicate <file> <go> [--name n]   copy a GameObject subtree (real objects; prefab instances are copied as instances)", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                if (n.Virtual && !n.IsInstanceRoot) throw new UaError("objects inside a prefab instance cannot be duplicated");
                var map = new Dictionary<long, long>();
                var ids = SubtreeDocs(f, g, n).ToList();
                foreach (var id in ids) map[id] = f.NewId();
                foreach (var id in ids)
                {
                    var d = f.Need(id);
                    var body = (YMap)d.Body.Clone();
                    RemapLocal(body, map);
                    f.Add(d.ClassId, d.TypeName, body, map[id], d.Stripped, f.InsertIndex());
                }
                long newGo = n.IsInstanceRoot ? map[n.Tr] : map[n.Go];
                var copy = SceneGraph.Build(f);
                var cn = n.IsInstanceRoot ? copy.ByTr[map[n.Tr]] : copy.ByGo[map[n.Go]];
                cn.Parent = null;
                Attach(f, n.Parent, cn, -1);
                if (a.O("name") is string nn) SetGoProp(f, SceneGraph.Build(f).ByGo[cn.Go], "m_Name", new YScalar(nn));
                f.Save();
                return new JsonObject { ["path"] = SceneGraph.Build(f).ByGo[cn.Go].Path, ["id"] = cn.Go };
            });

            // ---- components
            Reg("comp.list", "comp.list <file> <go>", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                return new JsonObject { ["go"] = n.Path, ["components"] = Arr(n.Comps, c => CompJson(c, null, false)) };
            });
            Reg("comp.get", "comp.get <file> <go> <Component> [propertyPath]   serialized fields (prefab overrides applied)", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                var c = g.Comp(n, a.P(2, "Component")) ?? throw new UaError($"{n.Path}: no component {a.P(2)}");
                return CompJson(c, a.P(3));
            });
            Reg("comp.set", "comp.set <file> <go> <Component> <propertyPath> <json>   (inside a prefab instance: written as an override)", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                var c = g.Comp(n, a.P(2, "Component")) ?? throw new UaError($"{n.Path}: no component {a.P(2)}");
                SetField(f, g, c, a.P(3, "propertyPath"), Values.Parse(a.P(4, "json")));
                f.Save();
                SceneGraph.ClearCache();
                var g2 = SceneGraph.Build(f);
                return CompJson(g2.CompById[c.Id], a.P(3));
            });
            Reg("comp.add", "comp.add <file> <go> <Type|ScriptName|Assets/…/X.cs|guid> [--set '{\"field\":value}']", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                var id = AddComponent(f, g, n, a.P(2, "Type"));
                if (a.O("set") is string set)
                {
                    SceneGraph.ClearCache();
                    var g2 = SceneGraph.Build(f);
                    foreach (var kv in JsonNode.Parse(set).AsObject()) SetField(f, g2, g2.CompById[id], kv.Key, kv.Value?.DeepClone());
                }
                f.Save();
                var g3 = SceneGraph.Build(f);
                return CompJson(g3.CompById[id]);
            });
            Reg("comp.remove", "comp.remove <file> <go> <Component>", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                var c = g.Comp(n, a.P(2, "Component")) ?? throw new UaError($"{n.Path}: no component {a.P(2)}");
                if (c.ClassId is 4 or 224) throw new UaError("Transform cannot be removed");
                RemoveComponent(f, g, c);
                f.Save();
                return new JsonObject { ["removed"] = c.Type, ["go"] = n.Path };
            });

            // ---- prefabs
            Reg("prefab.instantiate", "prefab.instantiate <file> <prefab> [--parent go] [--name n] [--position json]", a =>
            {
                var f = UFile.Load(a.P(0, "file"));
                var g = SceneGraph.Build(f);
                var pf = AssetDb.Need(a.P(1, "prefab"));
                var parent = a.O("parent") is string pp ? g.Need(pp) : null;
                var (inst, tr) = Instantiate(f, g, pf, parent);
                SceneGraph.ClearCache();
                var g2 = SceneGraph.Build(f);
                var node = g2.ByTr[tr];
                if (a.O("name") is string nm) SetGoProp(f, node, "m_Name", new YScalar(nm));
                if (a.O("position") is string pos)
                {
                    var trc = node.Comps.First(c => c.ClassId is 4 or 224);
                    SetField(f, g2, trc, trc.ClassId == 224 ? "m_AnchoredPosition" : "m_LocalPosition", Values.Parse(pos));
                }
                f.Save();
                return new JsonObject { ["instance"] = inst, ["path"] = SceneGraph.Build(f).ByTr[tr].Path };
            });
            Reg("prefab.overrides", "prefab.overrides <file> <instanceRoot>   modifications, added / removed components and objects", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                if (n.Instance == 0) throw new UaError(n.Path + " is not part of a prefab instance");
                var inst = f.Need(n.Instance);
                var mod = (YMap)inst.Body["m_Modification"];
                var src = inst.Body["m_SourcePrefab"] as YRef;
                var mods = (mod["m_Modifications"] as YSeq)?.Items.OfType<YMap>() ?? Enumerable.Empty<YMap>();
                var srcGraph = SceneGraph.Build(UFile.Load(AssetDb.ByGuid(src.Guid).Path));
                string Where(long srcId) => srcGraph.CompById.TryGetValue(srcId, out var sc) ? sc.Owner.Path + "::" + sc.Type : srcGraph.ByGo.TryGetValue(srcId, out var sn) ? sn.Path : "#" + srcId;
                return new JsonObject
                {
                    ["prefab"] = AssetDb.ByGuid(src.Guid)?.Path, ["instance"] = n.Instance,
                    ["modifications"] = Arr(mods, m => new JsonObject
                    {
                        ["target"] = Where(Fid(m["target"])), ["propertyPath"] = Serializer.Str(m["propertyPath"]),
                        ["value"] = Values.ToJson(m["value"]), ["objectReference"] = (m["objectReference"] as YRef)?.FileID != 0 ? Values.ToJson(m["objectReference"]) : null,
                    }),
                    ["addedComponents"] = Values.ToJson(mod["m_AddedComponents"]), ["addedGameObjects"] = Values.ToJson(mod["m_AddedGameObjects"]),
                    ["removedComponents"] = Values.ToJson(mod["m_RemovedComponents"]), ["removedGameObjects"] = Values.ToJson(mod["m_RemovedGameObjects"]),
                };
            });
            Reg("prefab.revert", "prefab.revert <file> <go> [--component C] [--path propertyPath]   drop overrides (all for the instance when only the root is given)", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                if (n.Instance == 0) throw new UaError(n.Path + " is not part of a prefab instance");
                var inst = f.Need(n.Instance);
                var mods = (YSeq)((YMap)inst.Body["m_Modification"])["m_Modifications"];
                var srcGuid = (inst.Body["m_SourcePrefab"] as YRef).Guid;
                HashSet<long> targets;
                if (a.O("component") is string cs) { var c = g.Comp(n, cs) ?? throw new UaError("no component " + cs); targets = new() { c.SrcId }; }
                else if (n.IsInstanceRoot && !a.Has("path")) targets = null;
                else targets = new HashSet<long>(n.Comps.Select(c => c.SrcId).Append(n.SrcGo));
                var path = a.O("path");
                int before = mods.Items.Count;
                mods.Items.RemoveAll(it => it is YMap m && (targets == null || targets.Contains(Fid(m["target"]))) && (path == null || Serializer.Str(m["propertyPath"]).StartsWith(path))
                    && !(targets == null && IsKeptRootOverride(m, n)));
                f.Dirty = true; f.Save();
                return new JsonObject { ["reverted"] = before - mods.Items.Count };
            });
            Reg("prefab.create", "prefab.create <file> <go> <Assets/…/X.prefab> [--no-link]   save a scene subtree as a prefab and (unless --no-link) replace it with an instance", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                if (n.Virtual) throw new UaError("select a real (non prefab-instance) GameObject");
                var dst = Ua.Rel(a.P(2, "prefab"));
                if (File.Exists(Ua.Abs(dst))) throw new UaError("exists: " + dst);
                var ids = SubtreeDocs(f, g, n).ToList();
                if (ids.Any(id => f.Get(id).ClassId == 1001)) throw new UaError("subtree contains prefab instances – nested prefab creation is not supported yet");
                var pf = UFile.New(dst);
                foreach (var id in ids)
                {
                    var d = f.Need(id);
                    var body = (YMap)d.Body.Clone();
                    if (id == n.Tr) body["m_Father"] = Local(0);
                    // references to scene objects outside the subtree cannot live in a prefab
                    ClearOutside(body, new HashSet<long>(ids));
                    pf.Add(d.ClassId, d.TypeName, body, id);
                }
                pf.Docs = pf.Docs.OrderBy(d => d.ClassId == 1 && d.FileId == n.Go ? 0 : 1).ToList();
                EnsureFolder(Path.GetDirectoryName(dst).Replace('\\', '/'));
                Ua.Write(Ua.Abs(dst), pf.Text());
                var guid = WriteMeta(dst);
                var res = new JsonObject { ["prefab"] = dst, ["guid"] = guid };
                if (!a.Has("no-link"))
                {
                    var parent = n.Parent;
                    int index = (parent?.Children ?? g.Roots).IndexOf(n);
                    DeleteNode(f, g, n);
                    SceneGraph.ClearCache();
                    var g2 = SceneGraph.Build(f);
                    var (inst, tr) = Instantiate(f, g2, AssetDb.ByPath(dst), parent == null ? null : g2.ByGo[parent.Go], index);
                    f.Save();
                    res["instance"] = inst;
                }
                return res;
            });
            Reg("prefab.unpack", "prefab.unpack <file> <instanceRoot>   replace a prefab instance by plain objects (outermost level, overrides baked)", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                if (!n.IsInstanceRoot) throw new UaError(n.Path + " is not a prefab instance root");
                var parent = n.Parent; int index = (parent?.Children ?? g.Roots).IndexOf(n);
                var created = Unpack(f, g, n);
                SceneGraph.ClearCache();
                var g2 = SceneGraph.Build(f);
                var root = g2.ByGo[created];
                root.Parent = null;
                Attach(f, parent == null ? null : g2.ByGo[parent.Go], root, index);
                f.Save();
                return new JsonObject { ["path"] = SceneGraph.Build(f).ByGo[created].Path };
            });

            // ---- ScriptableObjects
            Reg("so.create", "so.create <ScriptName|Assets/…/X.cs> <Assets/…/Y.asset> [--set '{\"field\":value}']", a =>
            {
                var (guid, type) = ScriptOf(a.P(0, "script"));
                var dst = Ua.Rel(a.P(1, "asset"));
                if (File.Exists(Ua.Abs(dst))) throw new UaError("exists: " + dst);
                var f = UFile.New(dst);
                var body = new YMap();
                body.Add("m_ObjectHideFlags", "0").Add("m_CorrespondingSourceObject", Local(0)).Add("m_PrefabInstance", Local(0)).Add("m_PrefabAsset", Local(0));
                body.Add("m_GameObject", Local(0)).Add("m_Enabled", "1").Add("m_EditorHideFlags", "0").Add("m_Script", new YRef(11500000, guid, 3));
                body.Add("m_Name", Path.GetFileNameWithoutExtension(dst)).Add("m_EditorClassIdentifier", "");
                foreach (var e in DefaultFields(type, scriptable: true)) body.Entries.Add(e);
                f.Add(114, "MonoBehaviour", body, 11400000);
                if (a.O("set") is string set) foreach (var kv in JsonNode.Parse(set).AsObject()) Values.Set(body, kv.Key, kv.Value?.DeepClone(), null);
                EnsureFolder(Path.GetDirectoryName(dst).Replace('\\', '/'));
                f.Save();
                var g = WriteMeta(dst);
                return new JsonObject { ["asset"] = dst, ["guid"] = g, ["script"] = AssetDb.ByGuid(guid)?.Path, ["fields"] = Values.ToJson(body) };
            });
        }

        static bool IsKeptRootOverride(YMap m, GNode root)
        {
            // reverting "everything" keeps the root placement + name, like Unity's Revert All
            var p = Serializer.Str(m["propertyPath"]);
            return Fid(m["target"]) == root.SrcTr && (p.StartsWith("m_LocalPosition") || p.StartsWith("m_LocalRotation") || p.StartsWith("m_AnchoredPosition") || p.StartsWith("m_SizeDelta")
                || p.StartsWith("m_AnchorM") || p.StartsWith("m_Pivot") || p.StartsWith("m_LocalEulerAnglesHint") || p == "m_RootOrder")
                || (Fid(m["target"]) == root.SrcGo && p == "m_Name");
        }

        static int LayerIndex(string ly)
        {
            if (int.TryParse(ly, out var i)) return i;
            var layers = (UFile.Load("ProjectSettings/TagManager.asset").Docs[0].Body["layers"] as YSeq)?.Items.Select(Serializer.Str).ToList();
            int idx = layers?.IndexOf(ly) ?? -1;
            return idx >= 0 ? idx : throw new UaError("unknown layer " + ly);
        }

        // ------------------------------------------------------------------ building blocks
        static YMap GoBody(string name, int layer) => new YMap()
            .Add("m_ObjectHideFlags", "0").Add("m_CorrespondingSourceObject", Local(0)).Add("m_PrefabInstance", Local(0)).Add("m_PrefabAsset", Local(0))
            .Add("serializedVersion", "6").Add("m_Component", new YSeq()).Add("m_Layer", layer.ToString(Inv)).Add("m_Name", name)
            .Add("m_TagString", "Untagged").Add("m_Icon", Local(0)).Add("m_NavMeshLayer", "0").Add("m_StaticEditorFlags", "0").Add("m_IsActive", "1");

        /// <summary>New GameObject (+Transform/RectTransform + components) under parentPath (null = scene root).</summary>
        static long CreateGo(UFile f, string name, string parentPath, bool rect, IEnumerable<string> comps)
        {
            var g = SceneGraph.Build(f);
            var parent = parentPath != null ? g.Need(parentPath) : null;
            bool parentRect = parent != null && parent.Comps.Any(c => c.ClassId == 224);
            rect |= parentRect;
            int layer = parent != null ? Serializer.Int(parent.GoBody()["m_Layer"], 0) : 0;
            var go = f.Add(1, "GameObject", GoBody(name, layer), 0, false, f.InsertIndex());
            var trBody = ClassIds.NewComponent(rect ? 224 : 4, go.FileId);
            var tr = f.Add(rect ? 224 : 4, rect ? "RectTransform" : "Transform", trBody, 0, false, f.InsertIndex());
            ((YSeq)go.Body["m_Component"]).Items.Add(new YMap().Add("component", Local(tr.FileId)));
            var node = new GNode { Go = go.FileId, Tr = tr.FileId, Name = name, GoBody = () => go.Body };
            if (parent == null && !f.Docs.Any(d => d.ClassId == 1660057539) && f.Rel.EndsWith(".prefab"))
                throw new UaError("a prefab has a single root: give --parent");
            Attach(f, parent, node, -1);
            SceneGraph.ClearCache();
            foreach (var c in comps) AddComponent(f, SceneGraph.Build(f), SceneGraph.Build(f).ByGo[go.FileId], c.Trim());
            return go.FileId;
        }

        static void SetGoField(UFile f, long goId, string key, string value) { f.Need(goId).Body[key] = new YScalar(value); f.Dirty = true; }
        static void SetTr(UFile f, long goId, string key, YNode value)
        {
            var tr = f.Docs.First(d => d.ClassId is 4 or 224 && Fid(d.Body["m_GameObject"]) == goId);
            tr.Body[key] = value; f.Dirty = true;
        }

        /// <summary>Parent transform id for children of node (stripped stand-in for objects inside an instance; created on demand).</summary>
        static long ParentTr(UFile f, GNode parent)
        {
            if (!parent.Virtual) return parent.Tr;
            EnsureStripped(f, parent.Tr, parent.Comps.First(c => c.ClassId is 4 or 224).ClassId, parent.SrcTr, parent.SrcGuid, parent.Instance);
            return parent.Tr;
        }

        static YDoc EnsureStripped(UFile f, long id, int classId, long srcId, string guid, long inst)
        {
            var d = f.Get(id);
            if (d != null) return d;
            var body = new YMap().Add("m_CorrespondingSourceObject", new YRef(srcId, guid, 3)).Add("m_PrefabInstance", Local(inst)).Add("m_PrefabAsset", Local(0));
            return f.Add(classId, ClassIds.Name(classId), body, id, true, f.InsertIndex());
        }

        /// <summary>Hook node (real GameObject or instance root) under parent at index (-1 = last).</summary>
        static void Attach(UFile f, GNode parent, GNode n, int index)
        {
            long nodeTr = n.Tr;
            if (n.IsInstanceRoot)
            {
                var mod = (YMap)f.Need(n.Instance).Body["m_Modification"];
                mod["m_TransformParent"] = Local(parent == null ? 0 : ParentTr(f, parent));
            }
            else
            {
                f.Need(nodeTr).Body["m_Father"] = Local(parent == null ? 0 : ParentTr(f, parent));
            }
            if (parent == null)
            {
                var sr = f.Docs.FirstOrDefault(d => d.ClassId == 1660057539);
                if (sr != null)
                {
                    var roots = sr.Body["m_Roots"] as YSeq ?? new YSeq();
                    sr.Body["m_Roots"] = roots;
                    if (index < 0 || index > roots.Items.Count) roots.Items.Add(Local(nodeTr)); else roots.Items.Insert(index, Local(nodeTr));
                }
            }
            else if (parent.Virtual)
            {
                // child added inside a prefab instance → m_AddedGameObjects
                var inst = f.Need(parent.Instance);
                var mod = (YMap)inst.Body["m_Modification"];
                var added = mod["m_AddedGameObjects"] as YSeq ?? new YSeq();
                mod["m_AddedGameObjects"] = added;
                added.Items.Add(new YMap().Add("targetCorrespondingSourceObject", new YRef(parent.SrcTr, parent.SrcGuid, 3)).Add("insertIndex", index.ToString(Inv)).Add("addedObject", Local(nodeTr)));
            }
            else
            {
                var kids = f.Need(parent.Tr).Body["m_Children"] as YSeq ?? new YSeq();
                f.Need(parent.Tr).Body["m_Children"] = kids;
                if (index < 0 || index > kids.Items.Count) kids.Items.Add(Local(nodeTr)); else kids.Items.Insert(index, Local(nodeTr));
            }
            f.Dirty = true;
        }

        static void Detach(UFile f, SceneGraph g, GNode n)
        {
            long tr = n.Tr;
            var sr = f.Docs.FirstOrDefault(d => d.ClassId == 1660057539);
            (sr?.Body["m_Roots"] as YSeq)?.Items.RemoveAll(r => Fid(r) == tr);
            if (n.Parent != null && !n.Parent.Virtual) (f.Need(n.Parent.Tr).Body["m_Children"] as YSeq)?.Items.RemoveAll(r => Fid(r) == tr);
            foreach (var inst in f.OfClass(1001))
                ((inst.Body["m_Modification"] as YMap)?["m_AddedGameObjects"] as YSeq)?.Items.RemoveAll(it => it is YMap m && Fid(m["addedObject"]) == tr);
            f.Dirty = true;
        }

        /// <summary>All document ids that make up node's subtree in this file (instances: PrefabInstance + its stripped docs + added objects).</summary>
        static IEnumerable<long> SubtreeDocs(UFile f, SceneGraph g, GNode n)
        {
            if (n.IsInstanceRoot)
            {
                yield return n.Instance;
                foreach (var d in f.Docs.Where(d => d.Stripped && Fid(d.Body["m_PrefabInstance"]) == n.Instance)) yield return d.FileId;
                foreach (var d in f.Docs.Where(d => !d.Stripped && d.Body?["m_GameObject"] is YRef r && f.Get(r.FileID)?.Stripped == true && Fid(f.Get(r.FileID).Body["m_PrefabInstance"]) == n.Instance))
                    yield return d.FileId;   // added components
            }
            else if (!n.Virtual)
            {
                yield return n.Go;
                foreach (var c in n.Comps) yield return c.Id;
            }
            foreach (var k in n.Children)
                if (!k.Virtual || k.IsInstanceRoot) foreach (var id in SubtreeDocs(f, g, k)) yield return id;
                else foreach (var kk in k.Children.Where(x => !x.Virtual || x.IsInstanceRoot)) foreach (var id in SubtreeDocs(f, g, kk)) yield return id;
        }

        static void DeleteNode(UFile f, SceneGraph g, GNode n)
        {
            if (n.Virtual && !n.IsInstanceRoot)
            {
                // removed GameObject override (Unity 2022.2+)
                var mod = (YMap)f.Need(n.Instance).Body["m_Modification"];
                var rem = mod["m_RemovedGameObjects"] as YSeq ?? new YSeq();
                mod["m_RemovedGameObjects"] = rem;
                rem.Items.Add(new YRef(n.SrcGo, n.SrcGuid, 3));
                f.Dirty = true;
                return;
            }
            var ids = SubtreeDocs(f, g, n).ToList();
            Detach(f, g, n);
            foreach (var id in ids) f.Remove(id);
            // dangling references to deleted objects → null
            var gone = new HashSet<long>(ids);
            foreach (var d in f.Docs) if (d.Body != null) NullRefs(d.Body, gone);
        }

        static void NullRefs(YNode n, HashSet<long> gone)
        {
            switch (n)
            {
                case YMap m:
                    for (int i = 0; i < m.Entries.Count; i++)
                        if (m.Entries[i].Value is YRef r && r.Raw && r.Guid == null && gone.Contains(r.FileID)) m.Entries[i] = new(m.Entries[i].Key, Local(0));
                        else NullRefs(m.Entries[i].Value, gone);
                    break;
                case YSeq q:
                    q.Items.RemoveAll(it => it is YMap im && im.Entries.Count == 1 && im.Entries[0].Value is YRef rr && rr.Guid == null && gone.Contains(rr.FileID));
                    for (int i = 0; i < q.Items.Count; i++)
                        if (q.Items[i] is YRef r && r.Raw && r.Guid == null && gone.Contains(r.FileID)) q.Items[i] = Local(0);
                        else NullRefs(q.Items[i], gone);
                    break;
            }
        }

        static void RemapLocal(YNode n, Dictionary<long, long> map)
        {
            switch (n)
            {
                case YMap m:
                    for (int i = 0; i < m.Entries.Count; i++)
                        if (m.Entries[i].Value is YRef r && r.Raw && r.Guid == null && map.TryGetValue(r.FileID, out var nid)) m.Entries[i] = new(m.Entries[i].Key, Local(nid));
                        else RemapLocal(m.Entries[i].Value, map);
                    break;
                case YSeq q:
                    for (int i = 0; i < q.Items.Count; i++)
                        if (q.Items[i] is YRef r && r.Raw && r.Guid == null && map.TryGetValue(r.FileID, out var nid)) q.Items[i] = Local(nid);
                        else RemapLocal(q.Items[i], map);
                    break;
            }
        }

        static void ClearOutside(YNode n, HashSet<long> inside)
        {
            switch (n)
            {
                case YMap m:
                    for (int i = 0; i < m.Entries.Count; i++)
                        if (m.Entries[i].Value is YRef r && r.Raw && r.Guid == null && r.FileID != 0 && !inside.Contains(r.FileID)) m.Entries[i] = new(m.Entries[i].Key, Local(0));
                        else ClearOutside(m.Entries[i].Value, inside);
                    break;
                case YSeq q:
                    for (int i = 0; i < q.Items.Count; i++)
                        if (q.Items[i] is YRef r && r.Raw && r.Guid == null && r.FileID != 0 && !inside.Contains(r.FileID)) q.Items[i] = Local(0);
                        else ClearOutside(q.Items[i], inside);
                    break;
            }
        }

        /// <summary>Set a GameObject property; on an object inside a prefab instance it becomes a modification.</summary>
        static void SetGoProp(UFile f, GNode n, string key, YNode value)
        {
            if (!n.Virtual) { f.Need(n.Go).Body[key] = value; f.Dirty = true; return; }
            AddMod(f, n.Instance, n.SrcGo, n.SrcGuid, key, value);
        }

        static void SetField(UFile f, SceneGraph g, GComp c, string path, JsonNode value)
        {
            var local = LocalResolver(f);
            if (!c.Virtual)
            {
                Values.Set(c.Doc.Body, path, value, local);
                f.Dirty = true;
                return;
            }
            // prefab instance override: convert to leaf property modifications
            var cur = Values.Get(c.Effective(), path);
            var node = Values.FromJson(value, cur, local);
            foreach (var (p, leaf) in Values.Flatten(node, path)) AddMod(f, c.Owner.Instance, c.SrcId, c.Owner.SrcGuid, p, leaf);
        }

        static void AddMod(UFile f, long inst, long srcId, string guid, string path, YNode leaf)
        {
            var mod = (YMap)f.Need(inst).Body["m_Modification"];
            var mods = mod["m_Modifications"] as YSeq ?? new YSeq();
            mod["m_Modifications"] = mods;
            mods.Items.RemoveAll(it => it is YMap m && Fid(m["target"]) == srcId && Serializer.Str(m["propertyPath"]) == path);
            var entry = new YMap().Add("target", new YRef(srcId, guid, 3)).Add("propertyPath", path);
            if (leaf is YRef r) entry.Add("value", "").Add("objectReference", r.Clone());
            else entry.Add("value", Serializer.Str(leaf)).Add("objectReference", Local(0));
            mods.Items.Add(entry);
            f.Dirty = true;
        }

        // ------------------------------------------------------------------ components
        public static (string guid, Type type) ScriptOf(string spec)
        {
            AssetInfo a = null;
            if (spec.EndsWith(".cs") || spec.Contains('/') || System.Text.RegularExpressions.Regex.IsMatch(spec, "^(guid:)?[0-9a-fA-F]{32}$")) a = AssetDb.Resolve(spec);
            else
            {
                var cands = AssetDb.All.Where(x => x.Path.EndsWith("/" + spec + ".cs")).ToList();
                if (cands.Count > 1) throw new UaError($"several scripts named {spec}: {string.Join(", ", cands.Select(c => c.Path))} – pass the path");
                a = cands.FirstOrDefault();
            }
            if (a != null) return (a.Guid, ScriptRegistry.TypeOf(a.Guid) ?? CompiledProjectType(a));
            // package script known to the engine (TMPro.TextMeshProUGUI, UnityEngine.UI.Image, …)
            var t = TypeCache.AllTypes.FirstOrDefault(x => (x.Name == spec || x.FullName == spec) && (typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(x) || typeof(UnityEngine.ScriptableObject).IsAssignableFrom(x)));
            var g = t != null ? ScriptRegistry.GuidOf(t) : null;
            if (g != null) return (g, t);
            throw new UaError("script not found: " + spec);
        }

        /// <summary>The compiled type of a project script that the engine does not know: compile the project (incremental, like
        /// `ua compile`) and load its runtime assemblies, as `play` does. Null when the project does not compile or no Unity Editor
        /// is installed (the component is then added without default field values; Unity fills them in on import).</summary>
        static Type CompiledProjectType(AssetInfo script)
        {
            if (!script.Path.EndsWith(".cs", StringComparison.Ordinal) || UnityEditorInstall.Data == null) return null;
            try
            {
                var (_, build, _) = RuntimeHost.CompileAndLoad(new UaArgs(Array.Empty<string>()), x => !x.Editor);
                ScriptRegistry.Reset();
                var t = ScriptRegistry.TypeOf(script.Guid);
                if (t == null && build.Errors > 0) Serializer.Warnings.Add($"{script.Path}: the project has compile errors, so the script's default field values are unknown (ua compile)");
                return t;
            }
            catch (UaError e) { Serializer.Warnings.Add($"{script.Path}: default field values unknown ({e.Message})"); return null; }
        }

        /// <summary>Default serialized fields of a script class (from the compiled type; empty when the type is unknown – Unity fills them in).</summary>
        static IEnumerable<KeyValuePair<string, YNode>> DefaultFields(Type t, bool scriptable)
        {
            if (t == null) yield break;
            YMap body = null;
            try
            {
                UnityEngine.Object o;
                if (scriptable) o = UnityEngine.ScriptableObject.CreateInstance(t);
                else { var go = new UnityEngine.GameObject("__ua_tmp"); o = go.AddComponent(t); }
                body = Serializer.Serialize(o);
            }
            catch (Exception e) { Console.Error.WriteLine($"[ua] defaults for {t.Name}: {e.Message}"); }
            if (body == null) yield break;
            bool after = false;
            foreach (var e in body.Entries)
            {
                if (after) yield return new(e.Key, e.Value);
                if (e.Key == "m_EditorClassIdentifier") after = true;
            }
        }

        static long AddComponent(UFile f, SceneGraph g, GNode n, string spec)
        {
            long goId = n.Go;
            if (n.Virtual) EnsureStripped(f, n.Go, 1, n.SrcGo, n.SrcGuid, n.Instance);
            int? cls = ClassIds.Id(spec);
            YMap body; int classId; string typeName;
            if (cls != null && cls != 114)
            {
                classId = cls.Value; typeName = ClassIds.Name(classId);
                if (!ClassIds.IsComponent(classId)) throw new UaError(spec + " is not a component");
                if (classId is 4 or 224) throw new UaError("a GameObject already has a Transform");
                body = ClassIds.NewComponent(classId, goId);
            }
            else
            {
                var (guid, type) = ScriptOf(spec);
                classId = 114; typeName = "MonoBehaviour";
                body = ClassIds.NewComponent(114, goId);
                body.Add("m_EditorHideFlags", "0").Add("m_Script", new YRef(11500000, guid, 3)).Add("m_Name", "").Add("m_EditorClassIdentifier", "");
                foreach (var e in DefaultFields(type, scriptable: false)) body.Entries.Add(e);
                // [RequireComponent]
                if (type != null)
                    foreach (UnityEngine.RequireComponent rc in type.GetCustomAttributes(typeof(UnityEngine.RequireComponent), true))
                        foreach (var req in new[] { rc.m_Type0, rc.m_Type1, rc.m_Type2 }.Where(x => x != null))
                            if (!n.Comps.Any(c => c.Type == req.Name || (req == typeof(UnityEngine.Transform)))) AddComponent(f, SceneGraph.Build(f), SceneGraph.Build(f).ByGo[goId], req.Name);
            }
            var d = f.Add(classId, typeName, body, 0, false, f.InsertIndex());
            if (n.Virtual)
            {
                var mod = (YMap)f.Need(n.Instance).Body["m_Modification"];
                var added = mod["m_AddedComponents"] as YSeq ?? new YSeq();
                mod["m_AddedComponents"] = added;
                added.Items.Add(new YMap().Add("targetCorrespondingSourceObject", new YRef(n.SrcGo, n.SrcGuid, 3)).Add("insertIndex", "-1").Add("addedObject", Local(d.FileId)));
            }
            else ((YSeq)f.Need(goId).Body["m_Component"]).Items.Add(new YMap().Add("component", Local(d.FileId)));
            f.Dirty = true;
            return d.FileId;
        }

        static void RemoveComponent(UFile f, SceneGraph g, GComp c)
        {
            if (c.Virtual)
            {
                var mod = (YMap)f.Need(c.Owner.Instance).Body["m_Modification"];
                var rem = mod["m_RemovedComponents"] as YSeq ?? new YSeq();
                mod["m_RemovedComponents"] = rem;
                rem.Items.Add(new YRef(c.SrcId, c.Owner.SrcGuid, 3));
                f.Dirty = true;
                return;
            }
            var goDoc = f.Get(Fid(c.Doc.Body["m_GameObject"]));
            if (goDoc != null && !goDoc.Stripped) (goDoc.Body["m_Component"] as YSeq)?.Items.RemoveAll(it => it is YMap m && Fid(m["component"]) == c.Id);
            foreach (var inst in f.OfClass(1001))
                ((inst.Body["m_Modification"] as YMap)?["m_AddedComponents"] as YSeq)?.Items.RemoveAll(it => it is YMap m && Fid(m["addedObject"]) == c.Id);
            f.Remove(c.Id);
            foreach (var d in f.Docs) if (d.Body != null) NullRefs(d.Body, new HashSet<long> { c.Id });
        }

        // ------------------------------------------------------------------ prefab instances
        static (long inst, long tr) Instantiate(UFile f, SceneGraph g, AssetInfo pf, GNode parent, int index = -1)
        {
            if (!pf.Path.EndsWith(".prefab") && AssetDb.ImporterOf(pf) != "ModelImporter") throw new UaError(pf.Path + " is not a prefab or model");
            SceneGraph.ClearCache();
            var src = SceneGraph.Build(UFile.Load(pf.Path));
            var root = src.Roots.FirstOrDefault() ?? throw new UaError(pf.Path + ": empty prefab");
            long inst = f.NewId();
            var rootTrComp = root.Comps.First(c => c.ClassId is 4 or 224);
            var mods = new YSeq();
            void M(long target, string path, string value) => mods.Items.Add(new YMap().Add("target", new YRef(target, pf.Guid, 3)).Add("propertyPath", path).Add("value", value).Add("objectReference", Local(0)));
            var rb = rootTrComp.Effective();
            foreach (var key in new[] { "m_LocalPosition", "m_LocalRotation", "m_LocalEulerAnglesHint" })
                if (rb[key] is YMap v) foreach (var e in v.Entries) M(root.Tr, key + "." + e.Key, Serializer.Str(e.Value));
            if (rootTrComp.ClassId == 224)
                foreach (var key in new[] { "m_AnchoredPosition", "m_SizeDelta", "m_AnchorMin", "m_AnchorMax", "m_Pivot" })
                    if (rb[key] is YMap v) foreach (var e in v.Entries) M(root.Tr, key + "." + e.Key, Serializer.Str(e.Value));
            M(root.Go, "m_Name", root.Name);
            var modification = new YMap().Add("serializedVersion", "3").Add("m_TransformParent", Local(parent == null ? 0 : ParentTr(f, parent)))
                .Add("m_Modifications", mods).Add("m_RemovedComponents", new YSeq()).Add("m_RemovedGameObjects", new YSeq())
                .Add("m_AddedGameObjects", new YSeq()).Add("m_AddedComponents", new YSeq());
            var body = new YMap().Add("m_ObjectHideFlags", "0").Add("serializedVersion", "2").Add("m_Modification", modification)
                .Add("m_SourcePrefab", new YRef(100100000, pf.Guid, 3));
            f.Add(1001, "PrefabInstance", body, inst, false, f.InsertIndex());
            long trId = SceneGraph.Combine(inst, root.Tr);
            EnsureStripped(f, trId, rootTrComp.ClassId, root.Tr, pf.Guid, inst);
            var node = new GNode { IsInstanceRoot = true, Virtual = true, Instance = inst, Tr = trId, Go = SceneGraph.Combine(inst, root.Go) };
            if (parent != null && parent.Virtual)
            {
                // nested instance under an object of another instance: hook via the stripped parent + m_AddedGameObjects
            }
            Attach(f, parent, node, index);
            return (inst, trId);
        }

        /// <summary>Bake an instance into plain objects (outermost level): every virtual object/component becomes a real document.</summary>
        static long Unpack(UFile f, SceneGraph g, GNode root)
        {
            var map = new Dictionary<long, long>();     // virtual id → new id
            var nodes = new List<GNode>();
            void Collect(GNode n) { if (n.Virtual && n.Instance == root.Instance) { nodes.Add(n); map[n.Go] = f.NewId(); foreach (var c in n.Comps) map[c.Id] = c.Virtual ? f.NewId() : c.Id; foreach (var k in n.Children) Collect(k); } }
            Collect(root);
            var addedChildren = new List<GNode>();
            foreach (var n in nodes)
            {
                var go = (YMap)n.GoBody().Clone();
                go["m_CorrespondingSourceObject"] = Local(0); go["m_PrefabInstance"] = Local(0); go["m_PrefabAsset"] = Local(0);
                var comps = new YSeq();
                foreach (var c in n.Comps) comps.Items.Add(new YMap().Add("component", Local(map[c.Id])));
                go["m_Component"] = comps;
                f.Add(1, "GameObject", go, map[n.Go], false, f.InsertIndex());
                foreach (var c in n.Comps)
                {
                    if (!c.Virtual) { var real = f.Need(c.Id); real.Body["m_GameObject"] = Local(map[n.Go]); continue; }
                    var b = (YMap)c.Effective().Clone();
                    b["m_CorrespondingSourceObject"] = Local(0); b["m_PrefabInstance"] = Local(0); b["m_PrefabAsset"] = Local(0);
                    b["m_GameObject"] = Local(map[n.Go]);
                    if (c.ClassId is 4 or 224)
                    {
                        var kids = new YSeq();
                        foreach (var k in n.Children) kids.Items.Add(Local(k.Virtual && k.Instance == root.Instance ? map[k.Comps.First(x => x.ClassId is 4 or 224).Id] : k.Tr));
                        b["m_Children"] = kids;
                        b["m_Father"] = Local(n == root ? 0 : map[n.Parent.Comps.First(x => x.ClassId is 4 or 224).Id]);
                    }
                    f.Add(c.ClassId, ClassIds.Name(c.ClassId), b, map[c.Id], false, f.InsertIndex());
                }
                foreach (var k in n.Children.Where(k => !(k.Virtual && k.Instance == root.Instance))) addedChildren.Add(k);
            }
            // remap references inside the new objects from virtual ids to the new ids
            var remap = map.Where(kv => kv.Key != kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var id in map.Values.Distinct()) RemapLocal(f.Need(id).Body, remap);
            // added children (real objects parented to stripped stand-ins) → re-parent to the new transforms
            foreach (var d in f.Docs.Where(d => !d.Stripped && d.ClassId is 4 or 224))
                if (remap.TryGetValue(Fid(d.Body["m_Father"]), out var nf)) d.Body["m_Father"] = Local(nf);
            Detach(f, g, root);
            foreach (var d in f.Docs.Where(d => d.FileId == root.Instance || (d.Stripped && Fid(d.Body["m_PrefabInstance"]) == root.Instance)).Select(d => d.FileId).ToList()) f.Remove(d);
            foreach (var d in f.Docs) if (d.Body != null) RemapLocal(d.Body, remap);
            return map[root.Go];
        }
    }
}
