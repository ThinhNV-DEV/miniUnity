// Running project code without Unity: the DLLs compiled by `ua compile` (against the real Unity reference assemblies) are loaded
// into this process; their references to UnityEngine.*Module / UnityEditor.* resolve to generated facade assemblies that forward
// every type MiniUnity implements (or stubs) to MiniUnity's engine. Scenes are flattened (prefab instances baked with their overrides)
// and deserialized into live objects; `Play` then drives the player loop. NUnit tests run through a small reflection runner.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json.Nodes;
using MiniUnity.Yaml;

namespace MiniUnity.Agent
{
    public static class RuntimeHost
    {
        static readonly Dictionary<string, string> facades = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> projectDlls = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Assembly> loaded = new(StringComparer.OrdinalIgnoreCase);
        static bool hooked;
        public static readonly List<string> Notes = new();

        static string FacadeDir => Ua.Abs("Library/MiniUnity/Facades");

        /// <summary>Facade "UnityEngine.CoreModule" etc.: a type forwarder (ExportedType) for every top-level type of the real module that
        /// MiniUnity defines, internal ones included (packages reach engine internals through [InternalsVisibleTo]; nested types follow their parent).</summary>
        public static void EnsureFacades()
        {
            if (facades.Count > 0) return;
            var data = UnityEditorInstall.Data ?? throw new UaError("Unity Editor not found (UNITY_EDITOR)");
            var engine = typeof(UnityEngine.Object).Assembly;
            var engineStamp = "v3|" + File.GetLastWriteTimeUtc(engine.Location).Ticks;
            Directory.CreateDirectory(FacadeDir);
            var modules = Directory.EnumerateFiles(Path.Combine(data, "Managed/UnityEngine"), "*.dll")
                .Where(f => Path.GetFileName(f).StartsWith("UnityEngine.") || Path.GetFileName(f).StartsWith("UnityEditor.")).ToList();
            var all = new Dictionary<string, List<(string ns, string name)>>();   // facade name → forwarded types
            foreach (var dll in modules)
            {
                var name = Path.GetFileNameWithoutExtension(dll);
                var list = all[name] = new();
                using var s = File.OpenRead(dll); using var pe = new PEReader(s);
                var md = pe.GetMetadataReader();
                foreach (var h in md.TypeDefinitions)
                {
                    var td = md.GetTypeDefinition(h);
                    if (!td.GetDeclaringType().IsNil) continue;
                    var ns = md.GetString(td.Namespace); var n = md.GetString(td.Name);
                    if (n == "<Module>" || n.StartsWith("<")) continue;
                    var full = ns.Length > 0 ? ns + "." + n : n;
                    if (engine.GetType(full) == null) continue;
                    list.Add((ns, n));
                    // nested types need their own forwarders (implementation = the parent's exported type)
                    void Nested(TypeDefinition parent, string parentName)
                    {
                        foreach (var nh in parent.GetNestedTypes())
                        {
                            var nt = md.GetTypeDefinition(nh); var nn = md.GetString(nt.Name);
                            if (nn.StartsWith("<") || engine.GetType(parentName + "+" + nn) == null) continue;
                            list.Add((ns, parentName.Substring(ns.Length > 0 ? ns.Length + 1 : 0) + "+" + nn));
                            Nested(nt, parentName + "+" + nn);
                        }
                    }
                    Nested(td, full);
                }
            }
            // UnityEngine.dll / UnityEditor.dll (old-style references from precompiled plugins): union of their modules
            all["UnityEngine"] = all.Where(k => k.Key.StartsWith("UnityEngine.")).SelectMany(k => k.Value).Distinct().ToList();
            all["UnityEditor"] = all.Where(k => k.Key.StartsWith("UnityEditor.")).SelectMany(k => k.Value).Distinct().ToList();
            var target = engine.GetName();
            foreach (var (name, types) in all)
            {
                var path = Path.Combine(FacadeDir, name + ".dll");
                var stampPath = path + ".stamp";
                if (!(File.Exists(path) && File.Exists(stampPath) && File.ReadAllText(stampPath) == engineStamp))
                {
                    File.WriteAllBytes(path, Forwarder(name, types.Distinct(), target));
                    File.WriteAllText(stampPath, engineStamp);
                }
                facades[name] = path;
            }
        }

        static byte[] Forwarder(string name, IEnumerable<(string ns, string name)> types, AssemblyName target)
        {
            var mb = new MetadataBuilder();
            mb.AddModule(0, mb.GetOrAddString(name + ".dll"), mb.GetOrAddGuid(Guid.NewGuid()), default, default);
            mb.AddAssembly(mb.GetOrAddString(name), new Version(0, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.Sha1);
            var asmRef = mb.AddAssemblyReference(mb.GetOrAddString(target.Name!), target.Version ?? new Version(0, 0, 0, 0), default, default, 0, default);
            mb.AddTypeDefinition(default, default, mb.GetOrAddString("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            // "Outer+Inner" entries come after their parent (discovery order); nested rows use the parent's row as implementation
            var rows = new Dictionary<string, ExportedTypeHandle>();
            foreach (var (ns, n) in types)
            {
                int plus = n.LastIndexOf('+');
                if (plus < 0)
                    rows[ns + "|" + n] = mb.AddExportedType((TypeAttributes)0x00200000 /* Forwarder */, ns.Length > 0 ? mb.GetOrAddString(ns) : default, mb.GetOrAddString(n), asmRef, 0);
                else if (rows.TryGetValue(ns + "|" + n[..plus], out var parent))
                    rows[ns + "|" + n] = mb.AddExportedType(TypeAttributes.NestedPublic, default, mb.GetOrAddString(n[(plus + 1)..]), parent, 0);
            }
            var peb = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(mb), new BlobBuilder());
            var blob = new BlobBuilder();
            peb.Serialize(blob);
            return blob.ToArray();
        }

        /// <summary>Unity's nunit.framework (3.5, Mono build) uses remoting CallContext, which .NET does not have; the same NUnit
        /// version's portable build (NuGet nunit 3.5.0, lib/dotnet) is loaded instead. Downloaded once into the NuGet packages folder.</summary>
        public static string PortableNUnit()
        {
            var home = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget/packages");
            var dll = Path.Combine(home, "nunit/3.5.0/lib/dotnet/nunit.framework.dll");
            if (File.Exists(dll)) return WithUnityAbi(dll);
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                var bytes = http.GetByteArrayAsync("https://api.nuget.org/v3-flatcontainer/nunit/3.5.0/nunit.3.5.0.nupkg").GetAwaiter().GetResult();
                using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
                var e = zip.GetEntry("lib/dotnet/nunit.framework.dll") ?? throw new Exception("lib/dotnet/nunit.framework.dll not in package");
                Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
                using (var src = e.Open()) using (var dst = File.Create(dll)) src.CopyTo(dst);
                return WithUnityAbi(dll);
            }
            catch (Exception ex) { Notes.Add("portable NUnit unavailable (" + ex.Message + "): NUnit asserts may fail with TypeLoadException CallContext"); return null; }
        }

        /// <summary>The portable build patched to Unity's Constraint.ApplyTo(object) ABI (NUnitAbi); unpatched if patching fails.</summary>
        static string WithUnityAbi(string portable)
        {
            try { return NUnitAbi.Patched(portable); }
            catch (Exception ex) { Notes.Add("NUnit ABI patch failed (" + ex.Message + "): constraints built against Unity's NUnit may throw TypeLoadException"); return portable; }
        }

        /// <summary>Load the compiled project assemblies (all, or the given ones and their references) into this process.</summary>
        public static List<Assembly> LoadProject(ProjectCompiler.ProjectModel m, IEnumerable<UAssembly> assemblies)
        {
            EnsureFacades();
            Environment.SetEnvironmentVariable("UNITY_BURST_DISABLE_COMPILATION", "1");   // Burst jobs / function pointers run as managed code
            foreach (var a in m.Assemblies.Where(x => x.OutputPath != null && File.Exists(x.OutputPath))) projectDlls[a.Name] = a.OutputPath;
            foreach (var p in m.PluginDlls.Concat(m.ExplicitPlugins.Select(e => e.path))) projectDlls.TryAdd(Path.GetFileNameWithoutExtension(p), p);
            if (!hooked)
            {
                hooked = true;
                AssemblyLoadContext.Default.Resolving += (ctx, name) =>
                {
                    if (loaded.TryGetValue(name.Name!, out var have)) return have;
                    string path = null;
                    if (facades.TryGetValue(name.Name!, out var f)) path = f;
                    else if (name.Name == "nunit.framework" && PortableNUnit() is string nu) path = nu;
                    else if (projectDlls.TryGetValue(name.Name!, out var p)) path = p;
                    if (path == null) return null;
                    var asm = ctx.LoadFromAssemblyPath(path);
                    loaded[name.Name!] = asm;
                    return asm;
                };
            }
            var res = new List<Assembly>();
            foreach (var a in assemblies)
            {
                if (a.OutputPath == null || !File.Exists(a.OutputPath)) continue;
                if (!loaded.TryGetValue(a.Name, out var asm))
                {
                    var pdb = Path.ChangeExtension(a.OutputPath, ".pdb");
                    using var dll = new MemoryStream(File.ReadAllBytes(a.OutputPath));
                    using var sym = File.Exists(pdb) ? new MemoryStream(File.ReadAllBytes(pdb)) : null;
                    asm = AssemblyLoadContext.Default.LoadFromStream(dll, sym);
                    loaded[a.Name] = asm;
                }
                res.Add(asm);
                if (!TypeCache.ProjectAssemblies.Contains(asm)) TypeCache.AddProjectAssembly(asm);
            }
            ScriptRegistry.Reset();
            return res;
        }

        /// <summary>Compile (incremental) and load everything the given assembly names need (all project assemblies when null).</summary>
        public static (ProjectCompiler.ProjectModel model, ProjectCompiler.BuildResult build, List<Assembly> asms) CompileAndLoad(UaArgs a, Func<UAssembly, bool> pick)
        {
            var m = ProjectCompiler.Discover(a.O("platform"));
            var build = ProjectCompiler.Build(m, null, true);
            var targets = build.Order.Where(x => !x.Failed && pick(x)).ToList();
            var asms = LoadProject(m, build.Order.Where(x => !x.Failed));
            return (m, build, asms.Where(asm => targets.Any(t => t.Name == asm.GetName().Name)).ToList());
        }

        // ------------------------------------------------------------------ scenes
        /// <summary>A prefab instance of a flattened file: its root and every object that comes from the source prefab, with the id the
        /// object has in that prefab (as the prefab itself is seen flattened), so the loaded objects can be connected again.</summary>
        public sealed class FlatInstance { public long RootGo; public string SrcGuid; public long SrcRootGo; public readonly List<(long id, long srcId)> Objects = new(); }

        /// <summary>Scene/prefab file → self-contained documents: every prefab instance baked into plain objects (ids as seen in this file).</summary>
        public static List<YDoc> Flatten(UFile f, List<FlatInstance> instances = null)
        {
            SceneGraph.ClearCache();
            var g = SceneGraph.Build(f);
            if (instances != null)
            {
                var byInst = new Dictionary<long, FlatInstance>();
                foreach (var n in g.All().Where(n => n.Virtual))
                {
                    if (!byInst.TryGetValue(n.Instance, out var fi)) byInst[n.Instance] = fi = new FlatInstance { SrcGuid = n.SrcGuid };
                    if (n.IsInstanceRoot) { fi.RootGo = n.Go; fi.SrcRootGo = n.SrcGo; }
                    fi.Objects.Add((n.Go, n.SrcGo));
                    foreach (var c in n.Comps.Where(c => c.Virtual)) fi.Objects.Add((c.Id, c.SrcId));
                }
                instances.AddRange(byInst.Values.Where(i => i.RootGo != 0));
            }
            var docs = new List<YDoc>();
            var addedChildren = new Dictionary<long, List<long>>();   // virtual transform → real children added in this file
            foreach (var d in f.Docs)
            {
                if (d.Stripped || d.ClassId == 1001) continue;
                docs.Add(g.StrippedAlias.Count > 0 ? new YDoc { ClassId = d.ClassId, FileId = d.FileId, TypeName = d.TypeName, Body = (YMap)d.Body.Clone() } : d);
                if (d.ClassId is 4 or 224 && d.Body["m_Father"] is YRef fr && g.ByTr.TryGetValue(g.Alias(fr.FileID), out var p) && p.Virtual)
                    (addedChildren.TryGetValue(g.Alias(fr.FileID), out var l) ? l : addedChildren[g.Alias(fr.FileID)] = new()).Add(d.FileId);
            }
            foreach (var n in g.All().Where(n => n.Virtual))
            {
                var go = (YMap)n.GoBody().Clone();
                var comps = new YSeq();
                foreach (var c in n.Comps) comps.Items.Add(new YMap().Add("component", new YRef(c.Id, null, 0)));
                go["m_Component"] = comps;
                docs.Add(new YDoc { ClassId = 1, FileId = n.Go, TypeName = "GameObject", Body = go });
                foreach (var c in n.Comps.Where(c => c.Virtual))
                {
                    var b = (YMap)c.Effective().Clone();
                    b["m_GameObject"] = new YRef(n.Go, null, 0);
                    if (c.ClassId is 4 or 224)
                    {
                        if (n.IsInstanceRoot) b["m_Father"] = new YRef(n.Parent?.Tr ?? 0, null, 0);
                        var kids = new YSeq();
                        foreach (var k in n.Children) kids.Items.Add(new YRef(k.Tr, null, 0));
                        b["m_Children"] = kids;
                    }
                    docs.Add(new YDoc { ClassId = c.ClassId, FileId = c.Id, TypeName = ClassIds.Name(c.ClassId), Body = b });
                }
            }
            // references to stripped stand-ins that Unity numbered without the (instance ^ source) rule
            if (g.StrippedAlias.Count > 0)
            {
                void Fix(YNode n)
                {
                    switch (n)
                    {
                        case YRef r when r.Raw && string.IsNullOrEmpty(r.Guid): r.FileID = g.Alias(r.FileID); break;
                        case YMap m: foreach (var e in m.Entries) Fix(e.Value); break;
                        case YSeq s: foreach (var i in s.Items) Fix(i); break;
                    }
                }
                foreach (var d in docs) Fix(d.Body);
            }
            return docs;
        }

        /// <summary>Connect the objects of flattened prefab instances to their prefab again (m_CorrespondingSource, the instance root's
        /// PrefabInstanceData), as Unity keeps them: PrefabUtility answers for them and saving writes PrefabInstances back.</summary>
        public static void Reconnect(LoadedAsset la, List<FlatInstance> instances)
        {
            foreach (var fi in instances)
            {
                if (!la.Objects.TryGetValue(fi.RootGo, out var ro) || ro is not UnityEngine.GameObject root) continue;
                var asset = AssetLoader.ResolveGuid(fi.SrcGuid, fi.SrcRootGo, typeof(UnityEngine.GameObject)) as UnityEngine.GameObject;
                if (asset == null) continue;
                var inst = new PrefabInstanceData { PrefabAsset = asset, PrefabGuid = fi.SrcGuid, InstanceRoot = root };
                foreach (var (id, srcId) in fi.Objects)
                {
                    if (!la.Objects.TryGetValue(id, out var o)) continue;
                    var src = AssetLoader.ResolveGuid(fi.SrcGuid, srcId, null);
                    if (src == null) continue;
                    o.m_CorrespondingSource = src; inst.Objects.Add(o);
                }
                root.m_PrefabInstance = inst;
            }
        }

        /// <summary>Load a scene into a new active SceneData (live objects, scene-owned: no asset identity).</summary>
        public static SceneData LoadScene(string rel, SceneData into = null)
        {
            var f = UFile.Load(rel);
            var instances = new List<FlatInstance>();
            var docs = Flatten(f, instances);
            var la = AssetLoader.LoadDocs(rel + "#play", AssetIndex.GuidOf(rel) ?? "0", docs);
            Reconnect(la, instances);
            var scene = into ?? new SceneData { Path = rel, Name = Path.GetFileNameWithoutExtension(rel) };
            scene.Loaded = true;
            SceneData.Active = scene;
            var gos = la.Objects.Values.OfType<UnityEngine.GameObject>().ToList();
            foreach (var o in la.Objects.Values) { o.m_Asset = null; if (o is UnityEngine.GameObject go) { go.m_InPrefabAsset = false; go.m_Scene = scene; } }
            // roots in SceneRoots order when present
            var order = (f.Docs.FirstOrDefault(d => d.ClassId == 1660057539)?.Body["m_Roots"] as YSeq)?.Items.OfType<YRef>().Select(r => r.FileID).ToList() ?? new List<long>();
            var roots = gos.Where(go => go.m_Transform != null && go.m_Transform.m_Parent == null).ToList();
            long TrId(UnityEngine.GameObject go) => la.Objects.FirstOrDefault(kv => ReferenceEquals(kv.Value, go.m_Transform)).Key;
            foreach (var r in roots.OrderBy(go => { int i = order.IndexOf(TrId(go)); return i < 0 ? int.MaxValue : i; })) scene.AddRoot(r);
            return scene;
        }
    }
}
