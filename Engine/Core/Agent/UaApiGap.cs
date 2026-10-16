// Agent command `apigap`: which Unity APIs the project uses that MiniUnity does not implement, ranked by use.
// Static: the IL of the project's compiled assemblies (Library/MiniUnity/ScriptAssemblies, built like `compile`) is scanned with
// Mono.Cecil for every UnityEngine / UnityEditor member it calls, reads or creates; each is looked up in MiniUnity.dll and reported
// as "stub" (body is `throw new NotImplementedException`) or "missing" (no such type / member: MissingMethodException at run time).
// Dynamic: `--from a.json,b.json` (outputs of play / test / record) and `--play scene` add the exceptions marked "miniunity" that
// actually happened, with their counts.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MiniUnity.Agent
{
    public static partial class UaCommands
    {
        static void RegisterApiGap()
        {
            Reg("apigap", "apigap [--all] [--assembly a,b] [--from play.json,test.json] [--play scene.unity [--frames n]] [--max n]   Unity APIs the project uses that MiniUnity has as stubs or lacks, ranked by use (static IL scan; --from / --play add the ones hit at run time); --all includes package assemblies", a =>
            {
                var gap = new ApiGap();
                var m = ProjectCompiler.Discover(a.O("platform"));
                var only = a.O("assembly")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
                var r = ProjectCompiler.Build(m, only, true);
                var scanned = new JsonArray();
                foreach (var asm in r.Order)
                {
                    if (asm.Failed || asm.OutputPath == null || !File.Exists(asm.OutputPath)) continue;
                    if (only == null && !a.Has("all") && (asm.FromPackage || asm.Package != null)) continue;
                    gap.Scan(asm.OutputPath, asm.Name);
                    scanned.Add((JsonNode)JsonValue.Create(asm.Name));
                }
                var runtime = new Dictionary<string, (int count, List<string> sites)>();
                foreach (var f in (a.O("from") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                    ApiGap.Collect(JsonNode.Parse(File.ReadAllText(f)), runtime);
                if (a.O("play") is string scene)
                {
                    var args = new List<string> { scene, "--frames", a.O("frames", "300") };
                    ApiGap.Collect(All["play"].run(new UaArgs(args)), runtime);
                }
                int max = a.I("max", 100);
                JsonNode Rows(IEnumerable<ApiGap.Use> xs) => Arr(xs.OrderByDescending(x => x.Count).ThenBy(x => x.Member).Take(max), x => new JsonObject
                {
                    ["member"] = x.Member, ["uses"] = x.Count, ["module"] = x.Module,
                    ["sites"] = Arr(x.Sites.Take(3), J), ["hitAtRuntime"] = runtime.TryGetValue(x.Member, out var hit) ? JsonValue.Create(hit.count) : null,
                });
                var stubs = gap.Uses.Values.Where(u => u.Kind == "stub").ToList();
                var missing = gap.Uses.Values.Where(u => u.Kind == "missing").ToList();
                return new JsonObject
                {
                    ["assemblies"] = scanned, ["compileErrors"] = r.Errors,
                    ["summary"] = new JsonObject
                    {
                        ["unityMembersUsed"] = gap.Checked, ["stub"] = stubs.Count, ["missing"] = missing.Count,
                        ["stubUses"] = stubs.Sum(x => x.Count), ["missingUses"] = missing.Sum(x => x.Count), ["hitAtRuntime"] = runtime.Count,
                    },
                    ["byType"] = Arr(stubs.Concat(missing).GroupBy(x => x.Type).OrderByDescending(g => g.Sum(x => x.Count)).Take(30),
                        g => new JsonObject { ["type"] = g.Key, ["members"] = g.Count(), ["uses"] = g.Sum(x => x.Count) }),
                    ["stub"] = Rows(stubs), ["missing"] = Rows(missing),
                    ["runtime"] = Arr(runtime.OrderByDescending(kv => kv.Value.count).Take(max), kv => new JsonObject
                    {
                        ["member"] = kv.Key, ["count"] = kv.Value.count, ["sites"] = Arr(kv.Value.sites.Take(3), J),
                    }),
                };
            });
        }
    }

    public sealed class ApiGap
    {
        public sealed class Use { public string Member, Type, Module, Kind; public int Count; public List<string> Sites = new(); }
        public readonly Dictionary<string, Use> Uses = new();
        public int Checked;
        readonly Dictionary<string, TypeDefinition> types = new();
        readonly Dictionary<string, string> verdicts = new();   // member key → "ok" / "stub" / "missing"

        public ApiGap()
        {
            var mu = ModuleDefinition.ReadModule(typeof(UnityEngine.Object).Assembly.Location);
            void Add(TypeDefinition t) { types[t.FullName] = t; foreach (var n in t.NestedTypes) Add(n); }
            foreach (var t in mu.Types) Add(t);
        }

        static bool IsUnity(TypeReference t)
        {
            // the engine modules MiniUnity.dll stands in for (UnityEngine.UI, TestRunner… are packages compiled from source)
            var scope = t.Scope?.Name ?? "";
            return scope is "UnityEngine" or "UnityEditor" || (scope.StartsWith("UnityEngine.") || scope.StartsWith("UnityEditor.")) && scope.EndsWith("Module");
        }

        public void Scan(string dll, string asmName)
        {
            var rp = new ReaderParameters { ReadSymbols = File.Exists(Path.ChangeExtension(dll, ".pdb")), ReadingMode = ReadingMode.Deferred };
            ModuleDefinition mod;
            try { mod = ModuleDefinition.ReadModule(dll, rp); }
            catch { mod = ModuleDefinition.ReadModule(dll); }
            using (mod)
                foreach (var t in mod.GetTypes())
                    foreach (var meth in t.Methods)
                    {
                        if (!meth.HasBody) continue;
                        SequencePoint sp = null;
                        foreach (var ins in meth.Body.Instructions)
                        {
                            var p = meth.DebugInformation?.GetSequencePoint(ins);
                            if (p != null && !p.IsHidden) sp = p;
                            switch (ins.Operand)
                            {
                                case MethodReference mr when IsUnity(mr.DeclaringType): Note(mr, sp, asmName); break;
                                case FieldReference fr when IsUnity(fr.DeclaringType): Note(fr, sp, asmName); break;
                            }
                        }
                    }
        }

        static string Site(SequencePoint sp, string asm) => sp == null ? asm : $"{Ua.Rel(sp.Document.Url)}:{sp.StartLine}";

        static string TypeName(TypeReference t)
        {
            if (t is GenericInstanceType gi) t = gi.ElementType;
            return t.FullName;
        }

        static string Display(MemberReference m)
        {
            var t = TypeName(m.DeclaringType).Replace('/', '.');
            int bt = t.IndexOf('`'); if (bt > 0) t = t[..bt];
            var n = m.Name;
            if (n.StartsWith("get_") || n.StartsWith("set_")) n = n[4..];
            else if (n == ".ctor") n = "ctor";
            return t + "." + n;
        }

        void Note(MemberReference m, SequencePoint sp, string asm)
        {
            var key = m is MethodReference mr ? MethodKey(mr) : TypeName(m.DeclaringType) + "::" + m.Name;
            if (!verdicts.TryGetValue(key, out var v))
            {
                v = m is MethodReference r ? Verdict(r) : FieldVerdict((FieldReference)m);
                verdicts[key] = v; Checked++;
            }
            if (v == "ok") return;
            var display = Display(m);
            if (!Uses.TryGetValue(display, out var u))
                Uses[display] = u = new Use { Member = display, Type = TypeName(m.DeclaringType).Replace('/', '.'), Module = m.DeclaringType.Scope?.Name, Kind = v };
            else if (v == "missing") u.Kind = "missing";
            u.Count++;
            var s = Site(sp, asm);
            if (u.Sites.Count < 10 && !u.Sites.Contains(s)) u.Sites.Add(s);
        }

        static string MethodKey(MethodReference m) => TypeName(m.DeclaringType) + "::" + m.Name + "(" + string.Join(",", m.Parameters.Select(p => Norm(p.ParameterType))) + ")";

        /// <summary>Parameter type for matching across assemblies: generic parameters by position, no assembly qualifiers.</summary>
        static string Norm(TypeReference t) => t switch
        {
            GenericParameter gp => (gp.Type == GenericParameterType.Method ? "!!" : "!") + gp.Position,
            ByReferenceType br => Norm(br.ElementType) + "&",
            ArrayType at => Norm(at.ElementType) + "[]",
            GenericInstanceType gi => TypeName(gi) + "<" + string.Join(",", gi.GenericArguments.Select(Norm)) + ">",
            _ => t.FullName,
        };

        TypeDefinition Find(TypeReference t) => types.TryGetValue(TypeName(t), out var d) ? d : null;

        IEnumerable<TypeDefinition> Chain(TypeDefinition t)
        {
            for (int guard = 0; t != null && guard < 32; guard++)
            {
                yield return t;
                t = t.BaseType == null ? null : types.TryGetValue(TypeName(t.BaseType), out var b) ? b : null;
            }
        }

        string Verdict(MethodReference m)
        {
            var t = Find(m.DeclaringType);
            if (t == null) return "missing";
            var want = m.Parameters.Select(p => Norm(p.ParameterType)).ToList();
            foreach (var c in Chain(t))
            {
                var cands = c.Methods.Where(x => x.Name == m.Name && x.Parameters.Count == want.Count).ToList();
                var hit = cands.FirstOrDefault(x => x.Parameters.Select(p => Norm(p.ParameterType)).SequenceEqual(want)) ?? (cands.Count == 1 ? cands[0] : null);
                if (hit != null) return IsStub(hit) ? "stub" : "ok";
            }
            // interface / object members (ToString, GetHashCode…) resolve outside the Unity type
            return m.Name is "ToString" or "GetHashCode" or "Equals" or "GetType" or "Finalize" ? "ok" : "missing";
        }

        string FieldVerdict(FieldReference f)
        {
            var t = Find(f.DeclaringType);
            if (t == null) return "missing";
            foreach (var c in Chain(t)) if (c.Fields.Any(x => x.Name == f.Name)) return "ok";
            return "missing";
        }

        /// <summary>A generated stub: `throw new NotImplementedException("…")` and nothing else.</summary>
        public static bool IsStub(MethodDefinition m)
        {
            if (!m.HasBody) return false;
            var ins = m.Body.Instructions;
            if (ins.Count > 6) return false;
            return ins.Any(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference c && c.DeclaringType.FullName == "System.NotImplementedException")
                && ins.Any(i => i.OpCode == OpCodes.Throw);
        }

        /// <summary>Exceptions marked "miniunity" anywhere in a play / test / record JSON result.</summary>
        public static void Collect(JsonNode n, Dictionary<string, (int count, List<string> sites)> into)
        {
            switch (n)
            {
                case JsonObject o:
                    if (o["miniunity"]?.GetValue<string>() is string mu)
                    {
                        int colon = mu.IndexOf("): ", StringComparison.Ordinal);
                        var member = (colon >= 0 ? mu[(colon + 3)..] : mu).Trim();
                        int count = o["count"] is JsonValue cv && cv.TryGetValue<int>(out var c) ? c : 1;
                        var site = o["file"] != null ? $"{o["file"]}:{o["line"]}" : o["where"]?.ToString();
                        var cur = into.TryGetValue(member, out var e) ? e : (0, new List<string>());
                        if (site != null && !cur.Item2.Contains(site)) cur.Item2.Add(site);
                        into[member] = (cur.Item1 + count, cur.Item2);
                        return;   // its "inner" is the same exception
                    }
                    foreach (var (_, v) in o) Collect(v, into);
                    break;
                case JsonArray arr:
                    foreach (var v in arr) Collect(v, into);
                    break;
            }
        }
    }
}
