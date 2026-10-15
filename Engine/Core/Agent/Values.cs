// YAML node ⇄ JSON for the agent tools, Unity property paths ("m_LocalPosition.x", "m_Materials.Array.data[0]", "items[2].name"),
// and reference values ({"$ref": "Assets/x.mat"}, {"$ref": "#1234"}, {"$ref": "go:Canvas/Button", "$component": "Image"}).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MiniUnity.Yaml;

namespace MiniUnity.Agent
{
    public static class Values
    {
        static readonly Regex Num = new(@"^-?(\d+(\.\d+)?([eE][-+]?\d+)?|\.\d+)$", RegexOptions.Compiled);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ YAML → JSON
        public static JsonNode ToJson(YNode n, int depth = 0, int maxItems = 200)
        {
            switch (n)
            {
                case null: return null;
                case YScalar s:
                    if (Num.IsMatch(s.Value) && s.Value.Length < 25)
                    {
                        if (long.TryParse(s.Value, NumberStyles.Integer, Inv, out var l)) return JsonValue.Create(l);
                        if (double.TryParse(s.Value, NumberStyles.Float, Inv, out var dv) && !double.IsInfinity(dv)) return JsonValue.Create(dv);
                    }
                    return JsonValue.Create(s.Value);
                case YRef r:
                {
                    var o = new JsonObject { ["fileID"] = r.Raw ? r.FileID : 0 };
                    if (r.Raw && r.Guid != null)
                    {
                        o["guid"] = r.Guid; o["type"] = r.Type;
                        var a = AssetDb.ByGuid(r.Guid);
                        if (a != null) o["asset"] = a.Path;
                        else if (AssetDb.IsBuiltin(r.Guid)) o["asset"] = "(builtin)";
                        else o["missing"] = true;
                    }
                    return o;
                }
                case YMap m:
                {
                    var o = new JsonObject();
                    foreach (var e in m.Entries) o[e.Key] = ToJson(e.Value, depth + 1, maxItems);
                    return o;
                }
                case YSeq q:
                {
                    var a = new JsonArray();
                    int i = 0;
                    foreach (var it in q.Items)
                    {
                        if (i++ >= maxItems) { a.Add(JsonValue.Create($"… {q.Items.Count - maxItems} more")); break; }
                        a.Add(ToJson(it, depth + 1, maxItems));
                    }
                    return a;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ JSON → YAML
        public delegate YRef LocalResolver(string target, string component);

        public static YNode FromJson(JsonNode j, YNode like, LocalResolver local)
        {
            switch (j)
            {
                case null:
                    return like is YRef || like is null ? new YRef(0, null, 0) : new YScalar("");
                case JsonValue v:
                {
                    if (v.TryGetValue<bool>(out var b)) return new YScalar(b ? "1" : "0");
                    if (v.TryGetValue<string>(out var str))
                    {
                        if (like is YRef && (str.StartsWith("Assets/") || str.StartsWith("Packages/") || str.StartsWith("#") || str.StartsWith("go:") || str.StartsWith("guid:")))
                            return RefFrom(str, null, null, local);
                        return new YScalar(str);
                    }
                    if (v.TryGetValue<long>(out var l)) return new YScalar(l.ToString(Inv));
                    if (v.TryGetValue<double>(out var d))
                    {
                        if (Math.Abs(d) < 1e15 && d == Math.Floor(d)) return new YScalar(((long)d).ToString(Inv));
                        return new YScalar(Fmt.F((float)d));
                    }
                    return new YScalar(v.ToJsonString().Trim('"'));
                }
                case JsonObject o:
                {
                    if (o.ContainsKey("$ref")) return RefFrom(o["$ref"]?.GetValue<string>(), o["$sub"]?.GetValue<string>(), o["$component"]?.GetValue<string>(), local);
                    if (o.ContainsKey("fileID") && o.Count <= 4 && (o.ContainsKey("guid") || o.Count == 1 || like is YRef))
                    {
                        long fid = o["fileID"].GetValue<long>();
                        string guid = o["guid"]?.GetValue<string>();
                        int type = o["type"]?.GetValue<int>() ?? (guid != null ? 2 : 0);
                        return new YRef(fid, guid, guid == null ? 0 : type);
                    }
                    var likeMap = like as YMap;
                    var m = new YMap { Flow = likeMap?.Flow ?? IsSmallVector(o) };
                    // keep the existing key order, then append new keys
                    if (likeMap != null)
                        foreach (var e in likeMap.Entries)
                            m.Add(e.Key, o.ContainsKey(e.Key) ? FromJson(o[e.Key], e.Value, local) : e.Value.Clone());
                    foreach (var kv in o)
                        if (likeMap == null || !likeMap.Has(kv.Key)) m.Add(kv.Key, FromJson(kv.Value, null, local));
                    return m;
                }
                case JsonArray a:
                {
                    var likeSeq = like as YSeq;
                    var proto = likeSeq?.Items.FirstOrDefault();
                    var q = new YSeq();
                    for (int i = 0; i < a.Count; i++)
                        q.Items.Add(FromJson(a[i], likeSeq != null && i < likeSeq.Items.Count ? likeSeq.Items[i] : proto, local));
                    return q;
                }
            }
            throw new UaError("unsupported JSON value");
        }

        static bool IsSmallVector(JsonObject o) =>
            o.Count is >= 2 and <= 4 && o.All(kv => kv.Key.Length == 1 && "xyzwrgba".Contains(kv.Key) && kv.Value is JsonValue);

        public static YRef RefFrom(string target, string sub, string component, LocalResolver local)
        {
            if (string.IsNullOrEmpty(target) || target == "null" || target == "none") return new YRef(0, null, 0);
            if (target.StartsWith("#")) return new YRef(long.Parse(target[1..], Inv), null, 0);
            if (target.StartsWith("go:") || target.StartsWith("/"))
            {
                if (local == null) throw new UaError("local reference needs a scene / prefab context: " + target);
                return local(target.StartsWith("go:") ? target[3..] : target, component);
            }
            var a = AssetDb.Resolve(target) ?? throw new UaError("reference target not found: " + target);
            return AssetDb.MainRef(a, sub);
        }

        // ------------------------------------------------------------------ property paths
        static readonly Regex Index = new(@"^(.*?)\[(\d+)\]$", RegexOptions.Compiled);

        /// <summary>Split "a.b.Array.data[3].c" / "a.b[3].c" into keys and indices.</summary>
        public static List<object> Split(string path)
        {
            var parts = new List<object>();
            var toks = path.Split('.');
            for (int i = 0; i < toks.Length; i++)
            {
                var t = toks[i];
                if (t == "Array" && i + 1 < toks.Length && (toks[i + 1].StartsWith("data[") || toks[i + 1] == "size"))
                {
                    var nx = toks[++i];
                    if (nx == "size") parts.Add("#size");
                    else parts.Add(int.Parse(nx[5..^1], Inv));
                    continue;
                }
                var m = Index.Match(t);
                if (m.Success) { if (m.Groups[1].Value.Length > 0) parts.Add(m.Groups[1].Value); parts.Add(int.Parse(m.Groups[2].Value, Inv)); }
                else parts.Add(t);
            }
            return parts;
        }

        public static YNode Get(YNode root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            YNode cur = root;
            foreach (var p in Split(path))
            {
                switch (p)
                {
                    case "#size": return cur is YSeq sq ? new YScalar(sq.Items.Count.ToString(Inv)) : null;
                    case string k: cur = (cur as YMap)?[k]; break;
                    case int i: cur = cur is YSeq s && i < s.Items.Count ? s.Items[i] : null; break;
                }
                if (cur == null) return null;
            }
            return cur;
        }

        /// <summary>Set the value at path (creating map keys; "Array.size" resizes by cloning the last element).</summary>
        public static YNode Set(YNode root, string path, JsonNode value, LocalResolver local)
        {
            var parts = Split(path);
            YNode cur = root;
            for (int i = 0; i < parts.Count; i++)
            {
                bool last = i == parts.Count - 1;
                var p = parts[i];
                if (p is "#size")
                {
                    if (cur is not YSeq sq) throw new UaError($"{path}: not an array");
                    int n = value.GetValue<int>();
                    while (sq.Items.Count > n) sq.Items.RemoveAt(sq.Items.Count - 1);
                    while (sq.Items.Count < n) sq.Items.Add(sq.Items.Count > 0 ? sq.Items[^1].Clone() : new YScalar(""));
                    return sq;
                }
                if (p is string k)
                {
                    if (cur is not YMap m) throw new UaError($"{path}: '{k}' is not inside a map");
                    if (last) { var nv = FromJson(value, m[k], local); m[k] = nv; return nv; }
                    if (m[k] == null) m[k] = parts[i + 1] is int ? new YSeq() : new YMap();
                    cur = m[k];
                }
                else
                {
                    int idx = (int)p;
                    if (cur is not YSeq s) throw new UaError($"{path}: [{idx}] is not inside an array");
                    while (s.Items.Count <= idx) s.Items.Add(s.Items.Count > 0 ? s.Items[^1].Clone() : new YScalar(""));
                    if (last) { var nv = FromJson(value, s.Items[idx], local); s.Items[idx] = nv; return nv; }
                    cur = s.Items[idx];
                }
            }
            throw new UaError("empty property path");
        }

        /// <summary>Flatten a node to Unity property paths with leaf values (for prefab modifications).</summary>
        public static IEnumerable<(string path, YNode leaf)> Flatten(YNode n, string prefix)
        {
            switch (n)
            {
                case YMap m:
                    foreach (var e in m.Entries)
                        foreach (var x in Flatten(e.Value, prefix.Length == 0 ? e.Key : prefix + "." + e.Key)) yield return x;
                    break;
                case YSeq q:
                    yield return (prefix + ".Array.size", new YScalar(q.Items.Count.ToString(Inv)));
                    for (int i = 0; i < q.Items.Count; i++)
                        foreach (var x in Flatten(q.Items[i], $"{prefix}.Array.data[{i}]")) yield return x;
                    break;
                default: yield return (prefix, n); break;
            }
        }

        public static JsonNode Parse(string s)
        {
            if (s == null) return null;
            try { return JsonNode.Parse(s); }
            catch (JsonException) { return JsonValue.Create(s); }   // bare words → string
        }
    }
}
