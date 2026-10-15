// Scene queries and small edits the agent commands were missing: go.find filters (--where on serialized values, --layer,
// --active), go.refs (who references a GameObject), comp.move (component order), tags.remove.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MiniUnity.Yaml;

namespace MiniUnity.Agent
{
    public static partial class UaCommands
    {
        /// <summary>"Rigidbody2D.m_Mass>1", "PlayerController.speed==5", "Image.m_Sprite==null", "TextMeshProUGUI.m_text~=Score".
        /// Operators: == != > >= < <= ~= (contains, case-insensitive). Several conditions: separated by ';' (all must hold).</summary>
        static readonly Regex whereRx = new(@"^\s*([^.\s]+?)\.(\S+?)\s*(==|!=|>=|<=|~=|>|<)\s*(.*?)\s*$", RegexOptions.CultureInvariant);

        internal static Func<SceneGraph, GNode, bool> Where(string expr)
        {
            var conds = expr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(c =>
            {
                var m = whereRx.Match(c);
                if (!m.Success) throw new UaError($"--where: expected Component.propertyPath<op>value (op: == != > >= < <= ~=), got '{c}'");
                return (comp: m.Groups[1].Value, path: m.Groups[2].Value, op: m.Groups[3].Value, value: m.Groups[4].Value.Trim('"', '\''));
            }).ToList();
            return (g, n) => conds.All(c =>
            {
                var comp = g.Comp(n, c.comp);
                if (comp == null) return false;
                YNode v;
                try { v = Values.Get(comp.Effective(), c.path); }
                catch (UaError) { return false; }
                return Compare(v, c.op, c.value);
            });
        }

        static bool Compare(YNode v, string op, string want)
        {
            if (v == null) return op == "!=";
            // references: "null" / "none" is an unassigned reference (fileID 0)
            if (v is YRef r)
            {
                bool isNull = r.IsNull;
                if (want is "null" or "none") return op == "==" ? isNull : op == "!=" && !isNull;
                var j = Values.ToJson(v);
                var s = j?["asset"]?.ToString() ?? j?.ToJsonString() ?? "";
                return op switch { "==" => s == want, "!=" => s != want, "~=" => s.Contains(want, StringComparison.OrdinalIgnoreCase), _ => false };
            }
            var text = v is YScalar sc ? sc.Value : Values.ToJson(v)?.ToJsonString() ?? "";
            bool num = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) & double.TryParse(want, NumberStyles.Float, CultureInfo.InvariantCulture, out var b);
            if (want is "true" or "false" && text is "0" or "1") { num = true; a = text == "1" ? 1 : 0; b = want == "true" ? 1 : 0; }
            return op switch
            {
                "==" => num ? a == b : text == want,
                "!=" => num ? a != b : text != want,
                "~=" => text.Contains(want, StringComparison.OrdinalIgnoreCase),
                ">" => num && a > b, ">=" => num && a >= b, "<" => num && a < b, "<=" => num && a <= b,
                _ => false,
            };
        }

        static void RegisterQuery()
        {
            Reg("undo", "undo [--force]   revert the last write command (journal in Library/MiniUnity/undo); refuses when its files changed since, unless --force", a =>
                UaJournal.UndoLast(a.Has("force")));
            Reg("undo.list", "undo.list   journals that undo can revert, newest first", a => new JsonObject { ["entries"] = UaJournal.List() });
            Reg("go.refs", "go.refs <file> <go>   serialized references in the file to this GameObject or its components (hierarchy links excluded)", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                var targets = new Dictionary<long, string> { [n.Go] = "GameObject" };
                if (n.Tr != 0) targets[n.Tr] = "Transform";
                foreach (var c in n.Comps) targets.TryAdd(c.Id, c.Type);
                var hits = new JsonArray();
                foreach (var other in g.All())
                    foreach (var c in other.Comps)
                        foreach (var (path, leaf) in Values.Flatten(c.Effective(), ""))
                        {
                            if (leaf is not YRef r || !r.Raw || !string.IsNullOrEmpty(r.Guid) || !targets.TryGetValue(r.FileID, out var what)) continue;
                            if (path is "m_GameObject" or "m_Father" || path.StartsWith("m_Children.")) continue;   // hierarchy, not a reference
                            hits.Add(new JsonObject { ["path"] = other.Path, ["component"] = c.Type, ["property"] = path, ["target"] = what });
                        }
                return new JsonObject { ["file"] = f.Rel, ["go"] = n.Path, ["count"] = hits.Count, ["references"] = hits };
            });

            Reg("comp.move", "comp.move <file> <go> <Component> --index n   reorder a component (Transform stays first)", a =>
            {
                var (f, g) = Open(a);
                var n = g.Need(a.P(1, "go"));
                var c = g.Comp(n, a.P(2, "Component")) ?? throw new UaError($"{n.Path}: no component {a.P(2)}");
                if (n.Virtual) throw new UaError("components of objects inside a prefab instance keep the prefab's order (edit the prefab)");
                if (c.ClassId is 4 or 224) throw new UaError("the Transform is always the first component");
                var list = (f.Need(n.Go).Body["m_Component"] as YSeq) ?? throw new UaError($"{n.Path}: no m_Component list");
                int from = list.Items.FindIndex(it => it is YMap m && Fid(m["component"]) == c.Id);
                if (from < 0) throw new UaError($"{n.Path}: {c.Type} is not in m_Component");
                if (!a.Has("index")) throw new UaError("missing --index n (0 is the Transform)");
                int to = Math.Clamp(a.I("index", from), 1, list.Items.Count - 1);
                var item = list.Items[from];
                list.Items.RemoveAt(from);
                list.Items.Insert(to, item);
                f.Dirty = true;
                f.Save();
                return new JsonObject { ["path"] = n.Path, ["components"] = Arr(SceneGraph.Build(f).ByGo[n.Go].Comps, x => J(x.Type)) };
            });

            Reg("tags.remove", "tags.remove <tag> | --layer <name> | --sorting-layer <name>", a =>
            {
                var f = UFile.Load("ProjectSettings/TagManager.asset");
                var b = f.Docs[0].Body;
                var o = new JsonObject();
                if (a.O("layer") is string layer)
                {
                    var layers = (YSeq)b["layers"];
                    int idx = layers.Items.FindIndex(it => Serializer.Str(it) == layer);
                    if (idx < 0) throw new UaError("no layer " + layer);
                    if (idx < 8) throw new UaError($"layer {idx} ({layer}) is a builtin layer");
                    layers.Items[idx] = new YScalar(""); o["layer"] = layer; o["index"] = idx;
                }
                else if (a.O("sorting-layer") is string sl)
                {
                    var sls = (YSeq)b["m_SortingLayers"];
                    int idx = sls.Items.FindIndex(it => it is YMap m && Serializer.Str(m["name"]) == sl);
                    if (idx < 0) throw new UaError("no sorting layer " + sl);
                    if (sl == "Default") throw new UaError("the Default sorting layer cannot be removed");
                    sls.Items.RemoveAt(idx); o["sortingLayer"] = sl;
                }
                else
                {
                    var tag = a.P(0, "tag");
                    var tags = b["tags"] as YSeq;
                    if (tags == null || tags.Items.RemoveAll(t => Serializer.Str(t) == tag) == 0) throw new UaError("no tag " + tag);
                    o["tag"] = tag;
                }
                f.Dirty = true; f.Save();
                return o;
            });
        }
    }
}
