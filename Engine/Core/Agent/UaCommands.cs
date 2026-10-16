// "mu ua <project> <command> …" – Unity project operations for agents, without the Unity Editor. Output: one JSON object on stdout.
// Assets / GUIDs / references, ProjectSettings, scenes + Hierarchy + GameObjects + components + serialized fields, prefabs and
// prefab instances (overrides), ScriptableObjects. Edits are made on the YAML documents and written back losslessly.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MiniUnity.Yaml;

namespace MiniUnity.Agent
{
    public sealed class UaArgs
    {
        public readonly List<string> Pos = new();
        public readonly Dictionary<string, string> Opt = new(StringComparer.OrdinalIgnoreCase);
        public UaArgs(IEnumerable<string> args)
        {
            var a = args.ToList();
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].StartsWith("--") && a[i].Length > 2)
                {
                    var k = a[i][2..];
                    int eq = k.IndexOf('=');
                    if (eq > 0) { Opt[k[..eq]] = k[(eq + 1)..]; continue; }
                    if (i + 1 < a.Count && !a[i + 1].StartsWith("--")) Opt[k] = a[++i]; else Opt[k] = "true";
                }
                else Pos.Add(a[i]);
            }
        }
        public string P(int i, string name) => i < Pos.Count ? Pos[i] : throw new UaError($"missing argument <{name}>");
        public string P(int i) => i < Pos.Count ? Pos[i] : null;
        public string O(string k, string d = null) => Opt.TryGetValue(k, out var v) ? v : d;
        public bool Has(string k) => Opt.ContainsKey(k);
        public int I(string k, int d)
        {
            if (!Opt.TryGetValue(k, out var v)) return d;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : throw new UaError($"--{k}: expected an integer, got '{v}'");
        }
    }

    public static partial class UaCommands
    {
        public delegate JsonNode Cmd(UaArgs a);
        public static readonly SortedDictionary<string, (Cmd run, string usage)> All = new(StringComparer.Ordinal);
        static void Reg(string name, string usage, Cmd c) => All[name] = (c, usage);

        static UaCommands()
        {
            RegisterAssets();
            RegisterScene();
            RegisterQuery();
            RegisterBench();
            RegisterExtra();
        }
        static partial void RegisterExtra();

        public static int Entry(string[] args)
        {
            // ua <project> <command> args… | ua <project> batch <file.jsonl>  (each line: ["command", "arg", …])
            if (args.Length < 2 || args[1] is "help" or "--help")
            {
                Console.WriteLine(JsonSerializer.Serialize(All.ToDictionary(k => k.Key, k => k.Value.usage), new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            AssetIndex.Root = Path.GetFullPath(args[0]);
            if (!Directory.Exists(AssetIndex.Root)) { Print(new JsonObject { ["ok"] = false, ["error"] = "project folder not found: " + AssetIndex.Root }); return 1; }
            Directory.SetCurrentDirectory(AssetIndex.Root);
            var rest = args.Skip(1).ToArray();
            if (rest[0] == "batch") return Batch(new UaArgs(rest.Skip(1)));
            var res = Run(rest);
            Print(res);
            return res["ok"]?.GetValue<bool>() == true ? 0 : 1;
        }

        /// <summary>batch <file.jsonl> [--dry-run] [--stop-on-error]: one command per line (["go.create", "Assets/Main.unity", "Player"]).
        /// Always prints one JSON object; a malformed line becomes a failed result with its line number. --dry-run applies to every line.</summary>
        static int Batch(UaArgs ba)
        {
            var results = new JsonArray();
            int bad = 0, skipped = 0;
            bool stop = ba.Has("stop-on-error"), dry = ba.Has("dry-run");
            string[] lines;
            try
            {
                var file = ba.P(0, "file.jsonl");
                if (!File.Exists(file)) throw new UaError("batch file not found: " + file);
                lines = File.ReadAllLines(file);
            }
            catch (Exception e) when (e is UaError or IOException or UnauthorizedAccessException)
            {
                Print(new JsonObject { ["ok"] = false, ["command"] = "batch", ["error"] = e.Message });
                return 1;
            }
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                if (stop && bad > 0) { skipped++; continue; }
                string[] arr;
                try
                {
                    arr = (JsonNode.Parse(line) as JsonArray ?? throw new UaError("expected a JSON array of strings"))
                        .Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : x?.ToJsonString() ?? "null").ToArray();
                    if (arr.Length == 0) throw new UaError("empty command");
                }
                catch (Exception e) when (e is JsonException or UaError or InvalidOperationException)
                {
                    results.Add(new JsonObject { ["ok"] = false, ["command"] = null, ["line"] = i + 1, ["error"] = $"line {i + 1}: {e.Message}" }); bad++;
                    continue;
                }
                if (arr[0] == "batch") { results.Add(new JsonObject { ["ok"] = false, ["command"] = "batch", ["line"] = i + 1, ["error"] = $"line {i + 1}: nested batch is not supported" }); bad++; continue; }
                if (dry && !arr.Contains("--dry-run")) arr = arr.Append("--dry-run").ToArray();
                var r = Run(arr);
                r["line"] = i + 1;
                if (r["ok"]?.GetValue<bool>() != true) bad++;
                results.Add(r);
            }
            var o = new JsonObject { ["ok"] = bad == 0, ["failed"] = bad, ["results"] = results };
            if (dry) o["dryRun"] = true;
            if (skipped > 0) o["skipped"] = skipped;
            Print(o);
            return bad == 0 ? 0 : 1;
        }

        public static JsonObject Run(string[] cmdArgs)
        {
            var name = cmdArgs[0];
            var a = new UaArgs(cmdArgs.Skip(1));
            Ua.DryRun = a.Has("dry-run");
            Ua.ShowDiff = a.Has("diff");
            Ua.Written.Clear();
            Ua.Diffs.Clear();
            bool journaled = !name.StartsWith("undo", StringComparison.Ordinal);
            if (journaled) UaJournal.Begin(name, cmdArgs.Skip(1)); else UaJournal.Abandon();
            Serializer.Warnings.Clear();
            Serializer.Strict = a.Has("strict");
            // UnityWebRequest from project code may reach these hosts (default: loopback only; MU_NETWORK=1 allows all)
            if (a.O("allow-network") is string hosts) Environment.SetEnvironmentVariable("MU_ALLOW_NETWORK", hosts);
            try
            {
                if (!All.TryGetValue(name, out var c)) throw new UaError($"unknown command '{name}' (ua help lists them)");
                var r = c.run(a);
                var o = r as JsonObject ?? new JsonObject { ["result"] = r };
                var res = new JsonObject { ["ok"] = true, ["command"] = name };
                foreach (var kv in o.ToList()) { o.Remove(kv.Key); res[kv.Key] = kv.Value; }
                if (Ua.Written.Count > 0) res["written"] = new JsonArray(Ua.Written.Distinct().Select(w => (JsonNode)w).ToArray());
                if (Ua.DryRun) res["dryRun"] = true;
                if (Serializer.Warnings.Count > 0) res["warnings"] = new JsonArray(Serializer.Warnings.Select(w => (JsonNode)w).ToArray());
                if ((Ua.DryRun || Ua.ShowDiff) && Ua.Diffs.Count > 0) res["diff"] = Ua.Diffs.DeepClone();
                if (journaled && UaJournal.Commit(true) is int id) res["undo"] = id;
                RefIndex.Save();
                return res;
            }
            catch (UaError e) { var r = new JsonObject { ["ok"] = false, ["command"] = name, ["error"] = e.Message }; return Partial(r, journaled); }
            catch (Exception e) { var r = new JsonObject { ["ok"] = false, ["command"] = name, ["error"] = e.GetType().Name + ": " + e.Message, ["stack"] = e.StackTrace }; return Partial(r, journaled); }
        }

        /// <summary>A command that failed after changing files keeps its journal, so `undo` can restore them.</summary>
        static JsonObject Partial(JsonObject r, bool journaled)
        {
            if (journaled && UaJournal.Commit(false) is int id) { r["undo"] = id; r["written"] = new JsonArray(Ua.Written.Distinct().Select(w => (JsonNode)w).ToArray()); }
            return r;
        }

        static void Print(JsonNode n) => Console.WriteLine(n.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

        static JsonArray Arr<T>(IEnumerable<T> xs, Func<T, JsonNode> f) => new(xs.Select(f).ToArray());
        static JsonNode J(string s) => s == null ? null : JsonValue.Create(s);

        // =====================================================================================================================
        // Assets, GUIDs, references, meta, settings
        // =====================================================================================================================
        static void RegisterAssets()
        {
            Reg("asset.find", "asset.find [--name glob] [--type ext|Importer|ClassName] [--in folder] [--label l] [--limit n]", a =>
            {
                var name = a.O("name"); var type = a.O("type"); var under = a.O("in") is string u ? Ua.Rel(u) : null; var label = a.O("label");
                var rx = name == null ? null : Ua.Glob(name);
                var q = AssetDb.All.Where(x => !x.Folder);
                if (under != null) q = q.Where(x => x.Path.StartsWith(under + "/"));
                if (rx != null) q = q.Where(x => rx.IsMatch(Path.GetFileName(x.Path)) || rx.IsMatch(Path.GetFileNameWithoutExtension(x.Path)));
                if (type != null) q = q.Where(x => MatchesType(x, type));
                if (label != null) q = q.Where(x => Labels(x).Contains(label, StringComparer.OrdinalIgnoreCase));
                var list = q.OrderBy(x => x.Path, StringComparer.Ordinal).ToList();
                int lim = a.I("limit", 500);
                return new JsonObject { ["count"] = list.Count, ["assets"] = Arr(list.Take(lim), x => new JsonObject { ["path"] = x.Path, ["guid"] = x.Guid, ["importer"] = AssetDb.ImporterOf(x) }) };
            });
            Reg("asset.info", "asset.info <path|guid>", a =>
            {
                var x = AssetDb.Need(a.P(0, "asset"));
                var o = new JsonObject { ["path"] = x.Path, ["guid"] = x.Guid, ["folder"] = x.Folder, ["importer"] = AssetDb.ImporterOf(x), ["labels"] = Arr(Labels(x), J) };
                if (!x.Folder)
                {
                    var abs = Ua.Abs(x.Path);
                    o["bytes"] = new FileInfo(abs).Length;
                    try { var r = AssetDb.MainRef(x); o["mainObject"] = new JsonObject { ["fileID"] = r.FileID, ["guid"] = r.Guid, ["type"] = r.Type }; } catch { }
                    if (UFile.IsYaml(abs))
                    {
                        var f = UFile.Load(x.Path);
                        o["objects"] = Arr(f.Docs.GroupBy(d => d.TypeName ?? ClassIds.Name(d.ClassId)), g => new JsonObject { ["type"] = g.Key, ["count"] = g.Count() });
                        o["dependencies"] = Arr(Deps(x.Path), d => d);
                    }
                }
                return o;
            });
            Reg("asset.refs", "asset.refs <path|guid> [--in folder]   files that reference the asset", a =>
            {
                var x = AssetDb.Resolve(a.P(0, "asset"));
                string guid = x?.Guid ?? (Regex.IsMatch(a.P(0), "^(guid:)?[0-9a-fA-F]{32}$") ? a.P(0).Replace("guid:", "") : throw new UaError("asset not found: " + a.P(0)));
                var refs = AssetDb.ReferencesTo(guid, a.O("in"));
                return new JsonObject { ["guid"] = guid, ["path"] = x?.Path, ["count"] = refs.Count, ["referencedBy"] = Arr(refs, r => new JsonObject { ["file"] = r.file, ["occurrences"] = r.count }) };
            });
            Reg("asset.deps", "asset.deps <path|guid> [--recursive]   assets this asset references", a =>
            {
                var x = AssetDb.Need(a.P(0, "asset"));
                var seen = new HashSet<string>(); var queue = new Queue<string>(); queue.Enqueue(x.Path);
                var outList = new JsonArray();
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    foreach (var d in Deps(cur))
                    {
                        var key = d["guid"].GetValue<string>();
                        if (!seen.Add(key)) continue;
                        outList.Add(d);
                        if (a.Has("recursive") && d["path"] is JsonValue pv && pv.GetValue<string>() is string p && UFile.IsYaml(Ua.Abs(p))) queue.Enqueue(p);
                    }
                }
                return new JsonObject { ["path"] = x.Path, ["count"] = outList.Count, ["dependencies"] = outList };
            });
            Reg("asset.missing", "asset.missing [--in folder]   references to GUIDs that do not exist + missing scripts", a =>
            {
                var res = new JsonArray();
                foreach (var f in AssetDb.ReferenceFiles(a.O("in")).Where(f => !f.EndsWith(".meta")))
                {
                    var missing = AssetDb.GuidsIn(f).Distinct().Where(g => AssetDb.ByGuid(g) == null && !AssetDb.IsBuiltin(g) && !ScriptRegistry.KnownGuid(g)).ToList();
                    if (missing.Count > 0) res.Add(new JsonObject { ["file"] = f, ["missingGuids"] = Arr(missing, J) });
                }
                return new JsonObject { ["count"] = res.Count, ["files"] = res };
            });
            Reg("asset.move", "asset.move <src> <dst>   move / rename with its .meta (GUID kept, references stay valid)", a =>
            {
                var x = AssetDb.Need(a.P(0, "src"));
                var dst = Ua.Rel(a.P(1, "dst"));
                if (File.Exists(Ua.Abs(dst)) || Directory.Exists(Ua.Abs(dst))) throw new UaError("destination exists: " + dst);
                EnsureFolder(Path.GetDirectoryName(dst).Replace('\\', '/'));
                Ua.Move(Ua.Abs(x.Path), Ua.Abs(dst));
                Ua.Move(Ua.Abs(x.Path) + ".meta", Ua.Abs(dst) + ".meta");
                Ua.Written.Add(dst);
                AssetDb.Reset();
                return new JsonObject { ["from"] = x.Path, ["to"] = dst, ["guid"] = x.Guid };
            });
            Reg("asset.copy", "asset.copy <src> <dst>   duplicate with a new GUID", a =>
            {
                var x = AssetDb.Need(a.P(0, "src"));
                if (x.Folder) throw new UaError("asset.copy copies files only");
                var dst = Ua.Rel(a.P(1, "dst"));
                if (File.Exists(Ua.Abs(dst))) throw new UaError("destination exists: " + dst);
                EnsureFolder(Path.GetDirectoryName(dst).Replace('\\', '/'));
                var guid = Guid.NewGuid().ToString("N");
                Ua.WriteBytes(Ua.Abs(dst), File.ReadAllBytes(Ua.Abs(x.Path)));
                var meta = File.ReadAllText(Ua.Abs(x.Path) + ".meta");
                Ua.Write(Ua.Abs(dst) + ".meta", Regex.Replace(meta, @"^guid: [0-9a-f]{32}", "guid: " + guid, RegexOptions.Multiline));
                AssetDb.Register(dst, guid);
                return new JsonObject { ["from"] = x.Path, ["to"] = dst, ["guid"] = guid };
            });
            Reg("asset.delete", "asset.delete <path|guid> [--force]   refuses while other assets reference it unless --force", a =>
            {
                var x = AssetDb.Need(a.P(0, "asset"));
                var refs = x.Folder ? new List<(string file, int count)>() : AssetDb.ReferencesTo(x.Guid);
                if (refs.Count > 0 && !a.Has("force"))
                    throw new UaError($"{x.Path} is referenced by {refs.Count} file(s): {string.Join(", ", refs.Take(8).Select(r => r.file))} – use --force");
                Ua.Delete(Ua.Abs(x.Path));
                Ua.Delete(Ua.Abs(x.Path) + ".meta");
                Ua.Written.Add(x.Path);
                AssetDb.Reset();
                return new JsonObject { ["deleted"] = x.Path, ["guid"] = x.Guid, ["danglingReferences"] = Arr(refs, r => J(r.file)) };
            });
            Reg("asset.mkdir", "asset.mkdir <folder>", a => new JsonObject { ["folder"] = EnsureFolder(Ua.Rel(a.P(0, "folder"))) });
            Reg("asset.import", "asset.import <path|folder>   create missing .meta files (default importer settings by file type)", a =>
            {
                var p = Ua.Rel(a.P(0, "path"));
                var created = new JsonArray();
                IEnumerable<string> items = Directory.Exists(Ua.Abs(p))
                    ? new[] { p }.Concat(Directory.EnumerateFileSystemEntries(Ua.Abs(p), "*", SearchOption.AllDirectories).Select(Ua.Rel))
                    : new[] { p };
                foreach (var it in items)
                {
                    if (it.EndsWith(".meta") || Path.GetFileName(it).StartsWith(".") || Path.GetFileName(it).EndsWith("~")) continue;
                    if (it is "Assets" or "Packages" || it.StartsWith("Packages/") && it.Count(ch => ch == '/') == 1 && !File.Exists(Ua.Abs(it))) continue;   // roots and package folders have no .meta
                    if (!File.Exists(Ua.Abs(it)) && !Directory.Exists(Ua.Abs(it))) throw new UaError("not found: " + it);
                    if (File.Exists(Ua.Abs(it) + ".meta")) continue;
                    EnsureFolder(Path.GetDirectoryName(it).Replace('\\', '/'));
                    var guid = WriteMeta(it);
                    created.Add(new JsonObject { ["path"] = it, ["guid"] = guid, ["importer"] = AssetDb.ImporterOf(AssetDb.ByPath(it)) });
                }
                return new JsonObject { ["created"] = created };
            });
            Reg("asset.labels", "asset.labels <asset> [--add a,b] [--remove c]", a =>
            {
                var x = AssetDb.Need(a.P(0, "asset"));
                var labels = Labels(x).ToList();
                if (a.O("add") is string add) foreach (var l in add.Split(',', StringSplitOptions.RemoveEmptyEntries)) if (!labels.Contains(l)) labels.Add(l);
                if (a.O("remove") is string rem) labels.RemoveAll(l => rem.Split(',').Contains(l));
                if (a.Has("add") || a.Has("remove"))
                {
                    var meta = AssetDb.Meta(x);
                    var seq = new YSeq(); foreach (var l in labels) seq.Items.Add(new YScalar(l));
                    if (labels.Count == 0) meta.Entries.RemoveAll(e => e.Key == "labels");
                    else if (meta.Has("labels")) meta["labels"] = seq;
                    else meta.Entries.Insert(Math.Max(0, meta.Entries.FindIndex(e => e.Key.EndsWith("Importer"))), new("labels", seq));
                    SaveMeta(x, meta);
                }
                return new JsonObject { ["path"] = x.Path, ["labels"] = Arr(labels, J) };
            });
            Reg("meta.get", "meta.get <asset> [propertyPath]   importer settings, e.g. TextureImporter.maxTextureSize", a =>
            {
                var x = AssetDb.Need(a.P(0, "asset"));
                return new JsonObject { ["path"] = x.Path, ["value"] = Values.ToJson(Values.Get(AssetDb.Meta(x), a.P(1) ?? "")) };
            });
            Reg("meta.set", "meta.set <asset> <propertyPath> <json>", a =>
            {
                var x = AssetDb.Need(a.P(0, "asset"));
                var meta = AssetDb.Meta(x);
                var v = Values.Set(meta, a.P(1, "propertyPath"), Values.Parse(a.P(2, "json")), null);
                SaveMeta(x, meta);
                return new JsonObject { ["path"] = x.Path, ["value"] = Values.ToJson(v) };
            });
            Reg("guid.of", "guid.of <path>", a => { var x = AssetDb.Need(a.P(0, "path")); return new JsonObject { ["path"] = x.Path, ["guid"] = x.Guid }; });
            Reg("guid.path", "guid.path <guid>", a =>
            {
                var g = a.P(0, "guid").Replace("guid:", "");
                var x = AssetDb.ByGuid(g);
                return new JsonObject { ["guid"] = g, ["path"] = x?.Path, ["builtin"] = AssetDb.IsBuiltin(g), ["exists"] = x != null };
            });
            Reg("guid.regenerate", "guid.regenerate <asset> [--update-refs]   give the asset a new GUID (and rewrite references)", a =>
            {
                var x = AssetDb.Need(a.P(0, "asset"));
                var ng = Guid.NewGuid().ToString("N");
                var refs = a.Has("update-refs") ? AssetDb.ReferencesTo(x.Guid) : new List<(string, int)>();
                foreach (var (file, _) in refs) Ua.Write(Ua.Abs(file), File.ReadAllText(Ua.Abs(file)).Replace(x.Guid, ng));
                var meta = File.ReadAllText(Ua.Abs(x.Path) + ".meta");
                Ua.Write(Ua.Abs(x.Path) + ".meta", meta.Replace("guid: " + x.Guid, "guid: " + ng));
                AssetDb.Reset();
                return new JsonObject { ["path"] = x.Path, ["old"] = x.Guid, ["new"] = ng, ["updatedFiles"] = refs.Count };
            });
            Reg("ref.replace", "ref.replace <fromAsset> <toAsset> [--in folder]   point every reference of one asset at another", a =>
            {
                var from = AssetDb.Need(a.P(0, "from")); var to = AssetDb.Need(a.P(1, "to"));
                var refs = AssetDb.ReferencesTo(from.Guid, a.O("in"));
                foreach (var (file, _) in refs) if (!file.EndsWith(".meta")) Ua.Write(Ua.Abs(file), File.ReadAllText(Ua.Abs(file)).Replace(from.Guid, to.Guid));
                return new JsonObject { ["from"] = from.Path, ["to"] = to.Path, ["files"] = Arr(refs, r => J(r.file)) };
            });

            // ---- raw object access (any YAML asset: .asset, .mat, ProjectSettings, scenes by #id)
            Reg("obj.list", "obj.list <file>   documents of a YAML file", a =>
            {
                var f = UFile.Load(a.P(0, "file"));
                return new JsonObject { ["file"] = f.Rel, ["objects"] = Arr(f.Docs, d => new JsonObject { ["id"] = d.FileId, ["class"] = d.ClassId, ["type"] = d.TypeName, ["stripped"] = d.Stripped, ["name"] = J(Serializer.Str(d.Body?["m_Name"])) }) };
            });
            Reg("obj.get", "obj.get <file> [#id] [propertyPath]   (first document when #id is omitted)", a =>
            {
                var f = UFile.Load(a.P(0, "file"));
                var (d, path) = DocArg(f, a, 1);
                return new JsonObject { ["file"] = f.Rel, ["id"] = d.FileId, ["type"] = d.TypeName, ["value"] = Values.ToJson(Values.Get(d.Body, path ?? "")) };
            });
            Reg("obj.set", "obj.set <file> [#id] <propertyPath> <json>", a =>
            {
                var f = UFile.Load(a.P(0, "file"));
                var (d, rest) = DocArg(f, a, 1, 2);
                var path = rest ?? throw new UaError("missing <propertyPath>");
                var json = a.Pos.Last();
                var v = Values.Set(d.Body, path, Values.Parse(json), LocalResolver(f));
                f.Dirty = true; f.Save();
                return new JsonObject { ["file"] = f.Rel, ["id"] = d.FileId, ["value"] = Values.ToJson(v) };
            });
            Reg("settings.list", "settings.list   ProjectSettings/*.asset", a => new JsonObject
            {
                ["settings"] = Arr(Directory.EnumerateFiles(Ua.Abs("ProjectSettings"), "*.asset").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x), J)
            });
            Reg("settings.get", "settings.get <Name> [propertyPath]   e.g. settings.get TagManager tags", a =>
            {
                var f = UFile.Load(SettingsFile(a.P(0, "Name")));
                return new JsonObject { ["file"] = f.Rel, ["value"] = Values.ToJson(Values.Get(f.Docs[0].Body, a.P(1) ?? "")) };
            });
            Reg("settings.set", "settings.set <Name> <propertyPath> <json>   e.g. settings.set PlayerSettings productName '\"Sand Loop\"'", a =>
            {
                var f = UFile.Load(SettingsFile(a.P(0, "Name")));
                var v = Values.Set(f.Docs[0].Body, a.P(1, "propertyPath"), Values.Parse(a.P(2, "json")), null);
                f.Dirty = true; f.Save();
                return new JsonObject { ["file"] = f.Rel, ["value"] = Values.ToJson(v) };
            });
            Reg("tags.add", "tags.add <tag> | --layer <name> [--index n] | --sorting-layer <name>", a =>
            {
                var f = UFile.Load("ProjectSettings/TagManager.asset");
                var b = f.Docs[0].Body;
                var o = new JsonObject();
                if (a.O("layer") is string layer)
                {
                    var layers = (YSeq)b["layers"];
                    int idx = a.I("index", -1);
                    if (idx < 0) idx = layers.Items.FindIndex(8, it => Serializer.Str(it).Length == 0);
                    if (idx < 8 || idx >= layers.Items.Count) throw new UaError("no free user layer (8–31)");
                    layers.Items[idx] = new YScalar(layer); o["layer"] = layer; o["index"] = idx;
                }
                else if (a.O("sorting-layer") is string sl)
                {
                    var sls = (YSeq)b["m_SortingLayers"];
                    var id = (uint)new Random().Next();
                    sls.Items.Add(new YMap().Add("name", sl).Add("uniqueID", id.ToString(CultureInfo.InvariantCulture)).Add("locked", "0"));
                    o["sortingLayer"] = sl; o["uniqueID"] = id;
                }
                else
                {
                    var tag = a.P(0, "tag");
                    var tags = b["tags"] as YSeq ?? new YSeq();
                    if (!tags.Items.Any(t => Serializer.Str(t) == tag)) tags.Items.Add(new YScalar(tag));
                    b["tags"] = tags; o["tag"] = tag;
                }
                f.Dirty = true; f.Save();
                return o;
            });
            Reg("buildscenes.get", "buildscenes.get   EditorBuildSettings scene list", a =>
            {
                var f = UFile.Load("ProjectSettings/EditorBuildSettings.asset");
                var sc = (f.Docs[0].Body["m_Scenes"] as YSeq)?.Items.OfType<YMap>() ?? Enumerable.Empty<YMap>();
                return new JsonObject { ["scenes"] = Arr(sc, s => new JsonObject { ["path"] = Serializer.Str(s["path"]), ["enabled"] = Serializer.Str(s["enabled"]) == "1", ["guid"] = Serializer.Str(s["guid"]) }) };
            });
            Reg("buildscenes.set", "buildscenes.set <scene.unity>... [--disable]   replace the build scene list (order kept)", a =>
            {
                var f = UFile.Load("ProjectSettings/EditorBuildSettings.asset");
                var seq = new YSeq();
                foreach (var p in a.Pos)
                {
                    var x = AssetDb.Need(p);
                    seq.Items.Add(new YMap().Add("enabled", a.Has("disable") ? "0" : "1").Add("path", x.Path).Add("guid", x.Guid));
                }
                f.Docs[0].Body["m_Scenes"] = seq; f.Dirty = true; f.Save();
                return new JsonObject { ["count"] = seq.Items.Count };
            });
        }

        static string SettingsFile(string name)
        {
            var rel = name.Contains('/') ? name : "ProjectSettings/" + (name.EndsWith(".asset") ? name : name + ".asset");
            if (File.Exists(Ua.Abs(rel))) return rel;
            // by document type (PlayerSettings lives in ProjectSettings.asset, PhysicsManager in DynamicsManager.asset, …)
            foreach (var f in Directory.EnumerateFiles(Ua.Abs("ProjectSettings"), "*.asset"))
            {
                if (!UFile.IsYaml(f)) continue;
                foreach (var l in File.ReadLines(f).Skip(2).Take(2))
                    if (l.TrimEnd(':') == name) return Ua.Rel(f);
            }
            throw new UaError("no settings file for " + name);
        }

        static (YDoc doc, string path) DocArg(UFile f, UaArgs a, int i, int maxPos = 1)
        {
            var p = a.P(i);
            if (p != null && p.StartsWith("#")) return (f.Need(long.Parse(p[1..], CultureInfo.InvariantCulture)), a.P(i + 1) is string s && (a.Pos.Count > i + 2 || maxPos == 1) ? s : null);
            if (f.Docs.Count == 0) throw new UaError(f.Rel + ": empty file");
            return (f.Docs[0], p != null && (a.Pos.Count > i + 1 || maxPos == 1) ? p : null);
        }

        static bool MatchesType(AssetInfo x, string type)
        {
            var ext = Path.GetExtension(x.Path).TrimStart('.');
            if (string.Equals(ext, type.TrimStart('.'), StringComparison.OrdinalIgnoreCase)) return true;
            var imp = AssetDb.ImporterOf(x);
            if (imp != null && (string.Equals(imp, type, StringComparison.OrdinalIgnoreCase) || string.Equals(imp, type + "Importer", StringComparison.OrdinalIgnoreCase))) return true;
            switch (type.ToLowerInvariant())
            {
                case "scene": return ext == "unity";
                case "prefab": return ext == "prefab";
                case "script": case "monoscript": return ext == "cs";
                case "material": return ext == "mat";
                case "texture": case "texture2d": return imp == "TextureImporter";
                case "sprite": return imp == "TextureImporter" && Serializer.Int((AssetDb.Meta(x)["TextureImporter"] as YMap)?["textureType"], 0) == 8;
                case "audioclip": case "audio": return imp == "AudioImporter";
                case "model": return imp == "ModelImporter";
                case "shader": return ext == "shader" || ext == "shadergraph";
                case "animationclip": return ext == "anim";
                case "animatorcontroller": return ext == "controller";
                case "scriptableobject":
                case "asset": return ext == "asset";
            }
            // ScriptableObject class: first document's script name
            if (ext == "asset" && UFile.IsYaml(Ua.Abs(x.Path)))
            {
                try
                {
                    using var r = new StreamReader(Ua.Abs(x.Path));
                    for (int i = 0; i < 30 && r.ReadLine() is string l; i++)
                    {
                        var m = Regex.Match(l, @"m_Script: \{fileID: \d+, guid: ([0-9a-f]{32})");
                        if (m.Success) return AssetDb.ByGuid(m.Groups[1].Value) is AssetInfo s && Path.GetFileNameWithoutExtension(s.Path).Equals(type, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch { }
            }
            return false;
        }

        static IEnumerable<string> Labels(AssetInfo x) => (AssetDb.Meta(x)["labels"] as YSeq)?.Items.Select(Serializer.Str) ?? Enumerable.Empty<string>();

        static IEnumerable<JsonObject> Deps(string rel)
        {
            var self = AssetDb.ByPath(rel)?.Guid;
            foreach (var g in AssetDb.GuidsIn(rel).Distinct())
            {
                if (g == self) continue;
                var d = AssetDb.ByGuid(g);
                yield return new JsonObject { ["guid"] = g, ["path"] = d?.Path, ["builtin"] = AssetDb.IsBuiltin(g), ["missing"] = d == null && !AssetDb.IsBuiltin(g) && !ScriptRegistry.KnownGuid(g) };
            }
        }

        static void SaveMeta(AssetInfo x, YMap meta)
        {
            // .meta files: plain YAML map (fileFormatVersion first, no %YAML header)
            var w = new YamlWriter();
            w.WriteMap(meta);
            Ua.Write(Ua.Abs(x.Path) + ".meta", w.ToString());
            x.Meta = meta;
        }

        public static string EnsureFolder(string rel)
        {
            rel = Ua.Rel(rel);
            if (string.IsNullOrEmpty(rel) || rel == "Assets" || rel == "Packages") return rel;
            var parent = Path.GetDirectoryName(rel)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            if (!Directory.Exists(Ua.Abs(rel)))
            {
                Ua.EnsureDir(Ua.Abs(rel));
                Ua.Written.Add(rel);
            }
            if (!File.Exists(Ua.Abs(rel) + ".meta") && rel.StartsWith("Assets/")) WriteMeta(rel, folder: true);
            return rel;
        }

        /// <summary>Write a default .meta for a new asset (importer chosen by extension, like Unity's first import).</summary>
        public static string WriteMeta(string rel, string guid = null, bool folder = false)
        {
            guid ??= Guid.NewGuid().ToString("N");
            var ext = Path.GetExtension(rel).ToLowerInvariant();
            string body;
            if (folder || Directory.Exists(Ua.Abs(rel))) body = "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
            else body = ext switch
            {
                ".cs" => "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
                ".png" or ".jpg" or ".jpeg" or ".tga" or ".psd" or ".tif" or ".tiff" or ".bmp" or ".gif" or ".exr" or ".hdr" => TextureMeta(),
                ".wav" or ".mp3" or ".ogg" or ".aif" or ".aiff" => "AudioImporter:\n  externalObjects: {}\n  serializedVersion: 7\n  defaultSettings:\n    serializedVersion: 2\n    loadType: 0\n    sampleRateSetting: 0\n    sampleRateOverride: 44100\n    compressionFormat: 1\n    quality: 1\n    conversionMode: 0\n    preloadAudioData: 0\n  platformSettingOverrides: {}\n  forceToMono: 0\n  normalize: 1\n  loadInBackground: 0\n  ambisonic: 0\n  3D: 1\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
                ".txt" or ".json" or ".xml" or ".csv" or ".bytes" or ".html" or ".htm" or ".yaml" or ".md" => "TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
                ".ttf" or ".otf" => "TrueTypeFontImporter:\n  externalObjects: {}\n  serializedVersion: 4\n  fontSize: 16\n  forceTextureCase: -2\n  characterSpacing: 0\n  characterPadding: 1\n  includeFontData: 1\n  fontNames:\n  - \n  fallbackFontReferences: []\n  customCharacters: \n  fontRenderingMode: 0\n  ascentCalculationMode: 1\n  useLegacyBoundsCalculation: 0\n  shouldRoundAdvanceValue: 1\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
                ".shader" or ".cginc" or ".hlsl" => "ShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n  nonModifiableTextures: []\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
                ".prefab" => "PrefabImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
                ".asmdef" => "AssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
                ".fbx" or ".obj" or ".blend" or ".dae" => "ModelImporter:\n  serializedVersion: 22200\n  internalIDToNameTable: []\n  externalObjects: {}\n  materials:\n    materialImportMode: 2\n  animations:\n    importAnimation: 1\n  meshes:\n    globalScale: 1\n  importAnimation: 1\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
                ".mat" => "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
                ".anim" => "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 7400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
                ".controller" => "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 9100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
                ".asset" or ".playable" or ".mixer" or ".spriteatlas" or ".physicMaterial" or ".overrideController" or ".mask" =>
                    "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: " + MainId(rel) + "\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
                _ => "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
            };
            Ua.Write(Ua.Abs(rel) + ".meta", $"fileFormatVersion: 2\nguid: {guid}\n" + body);
            AssetDb.Register(rel, guid);
            return guid;
        }

        static string MainId(string rel)
        {
            try { if (UFile.IsYaml(Ua.Abs(rel))) return YamlReader.ReadFile(Ua.Abs(rel)).First().FileId.ToString(CultureInfo.InvariantCulture); } catch { }
            return "11400000";
        }

        static string TextureMeta() =>
            "TextureImporter:\n  internalIDToNameTable: []\n  externalObjects: {}\n  serializedVersion: 12\n  mipmaps:\n    mipMapMode: 0\n    enableMipMap: 1\n    sRGBTexture: 1\n    linearTexture: 0\n    fadeOut: 0\n    borderMipMap: 0\n    mipMapsPreserveCoverage: 0\n    alphaTestReferenceValue: 0.5\n    mipMapFadeDistanceStart: 1\n    mipMapFadeDistanceEnd: 3\n  bumpmap:\n    convertToNormalMap: 0\n    externalNormalMap: 0\n    heightScale: 0.25\n    normalMapFilter: 0\n    flipGreenChannel: 0\n  isReadable: 0\n  streamingMipmaps: 0\n  streamingMipmapsPriority: 0\n  vTOnly: 0\n  ignoreMipmapLimit: 0\n  grayScaleToAlpha: 0\n  generateCubemap: 6\n  cubemapConvolution: 0\n  seamlessCubemap: 0\n  textureFormat: 1\n  maxTextureSize: 2048\n  textureSettings:\n    serializedVersion: 2\n    filterMode: 1\n    aniso: 1\n    mipBias: 0\n    wrapU: 0\n    wrapV: 0\n    wrapW: 0\n  nPOTScale: 1\n  lightmap: 0\n  compressionQuality: 50\n  spriteMode: 0\n  spriteExtrude: 1\n  spriteMeshType: 1\n  alignment: 0\n  spritePivot: {x: 0.5, y: 0.5}\n  spritePixelsToUnits: 100\n  spriteBorder: {x: 0, y: 0, z: 0, w: 0}\n  spriteGenerateFallbackPhysicsShape: 1\n  alphaUsage: 1\n  alphaIsTransparency: 0\n  spriteTessellationDetail: -1\n  textureType: 0\n  textureShape: 1\n  singleChannelComponent: 0\n  flipbookRows: 1\n  flipbookColumns: 1\n  maxTextureSizeSet: 0\n  compressionQualitySet: 0\n  textureFormatSet: 0\n  ignorePngGamma: 0\n  applyGammaDecoding: 0\n  swizzle: 50462976\n  cookieLightType: 0\n  platformSettings:\n  - serializedVersion: 3\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 2048\n    resizeAlgorithm: 0\n    textureFormat: -1\n    textureCompression: 1\n    compressionQuality: 50\n    crunchedCompression: 0\n    allowsAlphaSplitting: 0\n    overridden: 0\n    ignorePlatformSupport: 0\n    androidETC2FallbackOverride: 0\n    forceMaximumCompressionQuality_BC6H_BC7: 0\n  spriteSheet:\n    serializedVersion: 2\n    sprites: []\n    outline: []\n    physicsShape: []\n    bones: []\n    spriteID: \n    internalID: 0\n    vertices: []\n    indices: \n    edges: []\n    weights: []\n    secondaryTextures: []\n    nameFileIdTable: {}\n  mipmapLimitGroupName: \n  pSDRemoveMatte: 0\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
    }
}
