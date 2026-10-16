// `ua serialized.dump`: read a binary SerializedFile (Library/Artifacts import results, built-in resources, player data) with
// MiniUnity's reader and list its objects, or print chosen objects as the YAML a text asset of that type would hold.
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MiniUnity.Serialization;
using MiniUnity.Yaml;

namespace MiniUnity.Agent
{
    public static partial class UaCommands
    {
        static void RegisterSerialized()
        {
            Reg("serialized.dump", "serialized.dump <file> [--type Material] [--name text] [--id pathID] [--yaml] [--max n]   objects of a binary Unity SerializedFile (type, pathID, name); --id / --yaml print their fields as YAML", a =>
            {
                var path = a.P(0, "file");
                if (!File.Exists(path)) path = Ua.Abs(path);
                if (!File.Exists(path)) throw new UaError("file not found: " + a.P(0));
                if (!SerializedFile.IsSerializedFile(path)) throw new UaError("not a Unity SerializedFile: " + a.P(0));
                var f = SerializedFile.Open(path);
                string type = a.O("type"), name = a.O("name");
                long? id = a.O("id") is string ids ? long.Parse(ids, System.Globalization.CultureInfo.InvariantCulture) : null;
                int max = a.I("max", 200);
                bool yaml = a.Has("yaml") || id != null;
                var sel = f.Objects.Where(o => (id == null || o.PathId == id) && (type == null || string.Equals(f.TypeName(o), type, StringComparison.OrdinalIgnoreCase)));
                if (name != null) sel = sel.Where(o => (f.NameOf(o) ?? "").Contains(name, StringComparison.OrdinalIgnoreCase));
                var list = sel.Take(max).ToList();
                var res = new JsonObject
                {
                    ["file"] = a.P(0), ["version"] = f.Version, ["unity"] = f.UnityVersion, ["platform"] = f.Platform, ["typeTree"] = f.TypeTree,
                    ["objectCount"] = f.Objects.Count,
                    ["types"] = new JsonObject(f.Objects.GroupBy(o => f.TypeName(o)).OrderByDescending(g => g.Count()).Take(40).Select(g => new System.Collections.Generic.KeyValuePair<string, JsonNode>(g.Key, g.Count()))),
                    ["externals"] = Arr(f.Externals, e => new JsonObject { ["guid"] = e.Guid, ["type"] = e.Type, ["path"] = e.Path }),
                    ["objects"] = Arr(list, o =>
                    {
                        var j = new JsonObject { ["pathId"] = o.PathId, ["type"] = f.TypeName(o), ["classId"] = o.ClassId, ["size"] = o.Size };
                        if (f.NameOf(o) is string n) j["name"] = n;
                        if (yaml && f.Read(o) is YMap body) j["yaml"] = YamlWriter.Dump(body);
                        return j;
                    }),
                };
                return res;
            });
        }
    }
}
