// MCP input schemas generated from the `ua` usage lines, so that a client sees each tool's positional arguments (in order,
// required / optional / repeated) and its --options (flags as booleans) instead of one generic {args, options} shape.
//     "comp.set <file> <go> <Component> <propertyPath> <json>   (inside a prefab instance: written as an override)"
//     "go.set <file> <go> [--active 0|1] [--layer n|Name] [--tag t] [--static n]"
//     "addr.entry.add <asset|folder>... [--group g] [--simplify]   make assets addressable"
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MiniUnity.Agent
{
    public static class UaSchema
    {
        public sealed record Positional(string Name, bool Required, bool Repeated);
        public sealed record Option(string Name, string Value);   // Value null: a flag
        /// <summary>Forms: alternatives at the top level of the syntax ("<name>[@version] | <name> <git-url>"), each with its positionals.</summary>
        public sealed record Usage(string Command, List<List<Positional>> Forms, List<Option> Options, string Syntax, string Description)
        {
            public List<Positional> Args => Forms.OrderByDescending(f => f.Count).First();
        }

        // an option value: quoted, a JSON object, or a run of non-space characters that may contain "a|b" and "[…]" (out.png[@t,…])
        static readonly Regex optRx = new(@"--([A-Za-z0-9][A-Za-z0-9-]*)(?:[ =]((?:'[^']*'|""[^""]*""|\{[^}]*\}|(?:[^\s\[\]]|\[[^\]\s]*\])+)))?", RegexOptions.CultureInvariant);

        public static Usage Parse(string usage)
        {
            var m = Regex.Match(usage, @"^(\S+)\s*(.*?)(?:\s{3,}(.*))?$", RegexOptions.Singleline);
            string cmd = m.Groups[1].Value, syntax = m.Groups[2].Value.Trim(), desc = m.Groups[3].Success ? m.Groups[3].Value.Trim() : "";
            var options = new List<Option>();
            foreach (Match o in optRx.Matches(syntax))
            {
                var v = o.Groups[2].Success ? o.Groups[2].Value : null;
                if (v != null && v.StartsWith("--")) v = null;
                if (options.All(x => x.Name != o.Groups[1].Value)) options.Add(new Option(o.Groups[1].Value, v));
            }
            // positionals: what is left once every --option (and its value) is removed, per top-level alternative
            var rest = optRx.Replace(syntax, " ");
            var forms = new List<List<Positional>>();
            foreach (var form in SplitTopLevel(rest))
            {
                var args = new List<Positional>();
                foreach (Match p in Regex.Matches(form, @"<([^>]+)>(\.\.\.)?|(?<=^|\s)\[([^\[\]\s|]+)\]"))
                {
                    if (p.Groups[1].Success) args.Add(new Positional(p.Groups[1].Value, !InsideBrackets(form, p.Index), p.Groups[2].Success));
                    else if (!p.Groups[3].Value.StartsWith("-")) args.Add(new Positional(p.Groups[3].Value, false, false));
                }
                forms.Add(args);
            }
            return new Usage(cmd, forms, options, syntax, desc);
        }

        /// <summary>Split on "|" outside brackets / angle brackets.</summary>
        static IEnumerable<string> SplitTopLevel(string s)
        {
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c is '[' or '<') depth++;
                else if (c is ']' or '>') depth--;
                else if (c == '|' && depth == 0) { yield return s[start..i]; start = i + 1; }
            }
            yield return s[start..];
        }

        static bool InsideBrackets(string s, int at)
        {
            int depth = 0;
            for (int i = 0; i < at; i++) { if (s[i] == '[') depth++; else if (s[i] == ']') depth--; }
            return depth > 0;
        }

        static readonly (string name, string desc)[] common =
        {
            ("dry-run", "run fully but write nothing (the result still lists `written`)"),
            ("strict", "fail instead of writing defaults for values that cannot be serialized"),
        };

        public static JsonObject InputSchema(string usage, bool writes)
        {
            var u = Parse(usage);
            static string Describe(List<Positional> f) => f.Count == 0 ? "none"
                : string.Join(", ", f.Select((a, i) => $"{i + 1}. {a.Name}{(a.Repeated ? " (one or more)" : "")}{(a.Required ? "" : " (optional)")}"));
            var argDesc = u.Forms.Count == 1 ? (u.Args.Count == 0 ? "none" : "in order: " + Describe(u.Args))
                : string.Join("; or ", u.Forms.Select(f => "[" + Describe(f) + "]").Distinct());
            var opts = new JsonObject();
            foreach (var o in u.Options)
                opts[o.Name] = o.Value == null
                    ? new JsonObject { ["type"] = "boolean", ["description"] = "flag --" + o.Name }
                    : new JsonObject { ["description"] = $"--{o.Name} {o.Value} (string, number, or JSON object / array)" };
            if (writes) foreach (var (n, d) in common) opts[n] ??= new JsonObject { ["type"] = "boolean", ["description"] = d };
            int required = u.Forms.Min(f => f.Count(a => a.Required));
            int max = u.Forms.Any(f => f.Any(a => a.Repeated)) ? -1 : u.Forms.Max(f => f.Count);
            var props = new JsonObject
            {
                ["args"] = new JsonObject
                {
                    ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "positional arguments, " + argDesc,
                },
                ["options"] = new JsonObject { ["type"] = "object", ["properties"] = opts, ["additionalProperties"] = true, ["description"] = "--options of `ua " + u.Command + "`" },
            };
            if (required > 0) ((JsonObject)props["args"])["minItems"] = required;
            if (max >= 0) ((JsonObject)props["args"])["maxItems"] = max;
            var schema = new JsonObject { ["type"] = "object", ["properties"] = props };
            if (required > 0) schema["required"] = new JsonArray("args");
            return schema;
        }
    }
}
