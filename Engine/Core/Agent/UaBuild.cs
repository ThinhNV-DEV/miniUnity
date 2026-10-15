// Agent commands: compile the project's C# like Unity does (diagnostics as JSON), list the assembly layout.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;

namespace MiniUnity.Agent
{
    public static partial class UaCommands
    {
        static partial void RegisterBuild()
        {
            Reg("compile.assemblies", "compile.assemblies [--platform android|ios|standalone] [--all]   assembly layout (asmdef / predefined), references, defines", a =>
            {
                var m = ProjectCompiler.Discover(a.O("platform"));
                var list = m.Assemblies.Where(x => a.Has("all") || !x.Excluded);
                return new JsonObject
                {
                    ["defines"] = Arr(m.Defines.OrderBy(x => x), J), ["plugins"] = Arr(m.PluginDlls.Select(Ua.Rel), J), ["warnings"] = Arr(m.Warnings, J),
                    ["assemblies"] = Arr(list, x => new JsonObject
                    {
                        ["name"] = x.Name, ["asmdef"] = x.AsmdefPath != null ? Ua.Rel(x.AsmdefPath) : null, ["package"] = x.Package, ["editor"] = x.Editor, ["test"] = x.IsTest,
                        ["files"] = x.Files.Count, ["references"] = Arr(x.Refs.Select(r => r.Name), J), ["excluded"] = x.Excluded ? x.ExcludedWhy : null,
                    }),
                };
            });
            Reg("compile", "compile [--platform p] [--assembly name[,name]] [--warnings] [--no-emit] [--max n]   compile like Unity; errors (and warnings) with file:line", a =>
            {
                var m = ProjectCompiler.Discover(a.O("platform"));
                var only = a.O("assembly")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
                var r = ProjectCompiler.Build(m, only, !a.Has("no-emit"));
                bool warn = a.Has("warnings");
                int max = a.I("max", 200);
                var diags = r.Order.SelectMany(x => x.Diagnostics.Where(d => warn || d.Severity == DiagnosticSeverity.Error).Select(d => ProjectCompiler.DiagJson(d, x.Name))
                    .Concat(warn ? x.CachedDiagnostics.Select(c => (JsonObject)c.DeepClone()) : Enumerable.Empty<JsonObject>())).ToList();
                // project assemblies first (package warnings are noise for most tasks)
                diags = diags.OrderBy(d => d["file"]?.GetValue<string>()?.StartsWith("Assets/") == true ? 0 : 1).ToList();
                return new JsonObject
                {
                    ["success"] = r.Errors == 0, ["errors"] = r.Errors, ["warnings"] = r.Warnings, ["seconds"] = Math.Round(r.Seconds, 1),
                    ["cached"] = r.Order.Count(x => x.Cached),
                    ["assemblies"] = Arr(r.Order, x => new JsonObject { ["name"] = x.Name, ["files"] = x.Files.Count, ["ok"] = !x.Failed, ["cached"] = x.Cached,
                        ["errors"] = x.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error), ["skippedBecause"] = x.Skipped, ["output"] = x.Failed ? null : Ua.Rel(x.OutputPath) }),
                    ["diagnostics"] = Arr(diags.Take(max), d => d), ["truncated"] = diags.Count > max, ["projectWarnings"] = Arr(m.Warnings, J),
                };
            });
        }
    }
}
