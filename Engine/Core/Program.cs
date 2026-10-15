using System;
using System.IO;
using System.Linq;
using MiniUnity;

public static class Program
{

    /// <summary>Make <paramref name="project"/> the Unity project of this process (asset index root + working directory).</summary>
    public static void Open(string project)
    {
        AssetIndex.Root = Path.GetFullPath(project);
        Directory.SetCurrentDirectory(AssetIndex.Root);
        AddScriptDirs();
    }

    public static void AddScriptDirs()
    {
        var deps = Environment.GetEnvironmentVariable("MINIUNITY_SCRIPTDIRS");
        if (deps != null) ScriptRegistry.ExtraScriptDirs.AddRange(deps.Split(':', StringSplitOptions.RemoveEmptyEntries));
    }
    /// <summary>Command-line verbs: name, usage, handler (args[0] is the verb). A project's own verbs (video builds, recording…)
    /// live in its host (e.g. Tools/MiniUnityVideo in the VideoRecreate repo).</summary>
    static readonly (string name, string usage, Func<string[], int> run)[] Verbs =
    {
        ("ua", "ua <project> <command> …   agent commands (JSON), see AGENT.md", VerbUa),
        ("mcp", "mcp <project>   MCP server (stdio) exposing the ua commands", VerbMcp),
        ("validate", "validate <project> <file>…   duplicate fileIDs, dangling local refs, unknown GUIDs", VerbValidate),
        ("cmp", "cmp <project> <oracleScene> <oracleTimeline> <ourScene> <ourTimeline>   compare with Unity-made files", VerbCmp),
        ("rt", "rt <in.yaml> <out.yaml>   YAML round trip (parse + re-emit)", VerbRt),
        ("assetrt", "assetrt <project> <asset> <out>   asset load + re-serialize round trip", VerbAssetrt),
        ("fonttest", "fonttest <project> <font.asset>   FontEngine vs the glyphs Unity stored (dev)", VerbFonttest),
        ("hash128selftest", "hash128selftest   SpookyHash vectors (dev)", VerbHash128selftest),
        ("hash128test", "hash128test …   Hash128 string encodings (dev)", VerbHash128test),
        ("hashbrute", "hashbrute …   search Hash128 inputs (dev)", VerbHashbrute),
    };

    static int Main(string[] args) => Run(args);

    /// <summary>Run an engine verb (hosts forward the verbs they do not have).</summary>
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            foreach (var (_, usage, _) in Verbs) Console.WriteLine("mu " + usage);
            return args.Length == 0 ? 2 : 0;
        }
        if (Array.Find(Verbs, v => v.name == args[0]) is { run: not null } verb) return verb.run(args);
        Console.Error.WriteLine($"unknown command {args[0]} (mu help lists them; video verbs such as build / record are in the project's host)");
        return 2;
    }


    static int VerbUa(string[] args)
    {
        AddScriptDirs();
        return MiniUnity.Agent.UaCommands.Entry(args.Skip(1).ToArray());
    }

    static int VerbMcp(string[] args)
    {
        return MiniUnity.Agent.UaMcp.Serve(args.Skip(1).ToArray());
    }

    static int VerbCmp(string[] args)
    {
        // cmp <projectRoot> <oracleScene> <oracleTimeline> <ourSceneRel> <ourTimelineRel>   (oracle .meta next to the oracle files)
        string root = Path.GetFullPath(args[1]);
        AddScriptDirs();
        AssetIndex.Root = root;
        Compare.Timeline(Path.GetFullPath(args[3]), Path.Combine(root, args[5]), Console.Out, out var oi, out var ni);
        Compare.SceneCmp(Path.GetFullPath(args[2]), Path.Combine(root, args[4]), root, root, oi, ni, Console.Out);
        return 0;
    }

    static int VerbValidate(string[] args)
    {
        // validate <projectRoot> <file>...: local refs resolve, external GUIDs exist, unique ids, stripped/prefab sanity
        AssetIndex.Root = Path.GetFullPath(args[1]);
        AddScriptDirs();
        int bad = 0;
        foreach (var f in args.Skip(2))
        {
            var docs = MiniUnity.Yaml.YamlReader.ReadFile(f);
            var ids = new System.Collections.Generic.HashSet<long>();
            int dup = 0, dangling = 0, missingGuid = 0, refs = 0;
            foreach (var d in docs) if (!ids.Add(d.FileId)) dup++;
            var missingSamples = new System.Collections.Generic.List<string>();
            void Walk(MiniUnity.Yaml.YNode n)
            {
                switch (n)
                {
                    case MiniUnity.Yaml.YRef r:
                        if (r.FileID == 0) return;
                        refs++;
                        if (string.IsNullOrEmpty(r.Guid)) { if (!ids.Contains(r.FileID)) { dangling++; if (missingSamples.Count < 3) missingSamples.Add("local " + r.FileID); } }
                        else if (!r.Guid.StartsWith("0000000000000000") && AssetIndex.PathOf(r.Guid) == null && MiniUnity.ScriptRegistry.TypeOf(r.Guid) == null && !MiniUnity.ScriptRegistry.KnownGuid(r.Guid))
                        { missingGuid++; if (missingSamples.Count < 3) missingSamples.Add("guid " + r.Guid); }
                        return;
                    case MiniUnity.Yaml.YMap m: foreach (var e in m.Entries) Walk(e.Value); return;
                    case MiniUnity.Yaml.YSeq q: foreach (var i in q.Items) Walk(i); return;
                }
            }
            foreach (var d in docs) Walk(d.Body);
            bad += dup + dangling + missingGuid;
            Console.WriteLine($"{Path.GetFileName(f)}: docs {docs.Count}, refs {refs}, duplicate ids {dup}, dangling local refs {dangling}, unknown guids {missingGuid} {string.Join("; ", missingSamples)}");
        }
        return bad == 0 ? 0 : 2;
    }

    static int VerbHash128selftest(string[] args)
    {
        var buf = new byte[512]; for (int i = 0; i < 512; i++) buf[i] = (byte)(i + 128);
        unsafe { fixed (byte* pb = buf) for (int len = 0; len < 4; len++) { ulong h1 = 0, h2 = 0; UnityEngine.SpookyHash.Hash(pb, (ulong)len, &h1, &h2); Console.WriteLine($"{len}: {(uint)h1:x8}"); } }
        // Unity docs vector: Append(42), Append(13f), Append("Hello"), Append(int[]{1..5}) → 2d6e582c3fcfb4b8f3c16650a75dc37b
        unsafe
        {
            for (int enc = 0; enc < 2; enc++)
            {
                ulong h1 = 0, h2 = 0;
                void H(byte[] b) { fixed (byte* q = b) { ulong a = h1, c = h2; UnityEngine.SpookyHash.Hash(q, (ulong)b.Length, &a, &c); h1 = a; h2 = c; } }
                H(BitConverter.GetBytes(42)); H(BitConverter.GetBytes(13.0f));
                H(enc == 0 ? System.Text.Encoding.Unicode.GetBytes("Hello") : System.Text.Encoding.UTF8.GetBytes("Hello"));
                var arr = new byte[20]; for (int i = 0; i < 5; i++) BitConverter.GetBytes(i + 1).CopyTo(arr, i * 4); H(arr);
                var hh = new UnityEngine.Hash128(h1, h2);
                for (int tm = 0; tm < 6; tm++) { UnityEngine.Hash128.ToStringMode = tm; Console.WriteLine($"enc{enc} tostring{tm}: {hh}"); }
            }
        }
        return 0;
    }

    static int VerbAssetrt(string[] args)
    {
        // assetrt <projectRoot> <assetRel> <outFile>: load a native-YAML asset through MiniUnity and write it back
        AssetIndex.Root = Path.GetFullPath(args[1]); AddScriptDirs();
        var la = AssetLoader.Load(args[2]);
        File.WriteAllText(args[3], AssetWriter.Write(args[2], la.Order.Select(i => la.Objects[i])));
        return 0;
    }

    static int VerbHashbrute(string[] args)
    {
        // hashbrute <text> <hash> <voice>... : search speed/silence/format space for a Piper signature
        string text = args[1], target = args[2];
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var v in args.Skip(3))
            for (int sp = 50; sp <= 200; sp++)
            {
                foreach (var sep in new[] { ".", "," })
                {
                    string spd = (sp / 100.0).ToString("0.00", inv).Replace(".", sep);
                    var cands = new System.Collections.Generic.List<string> { $"piper|{v}|{spd}" };
                    for (int si = 0; si <= 200; si += 1) cands.Add($"piper|{v}|{spd}|{(si / 100.0).ToString("0.00", inv).Replace(".", sep)}");
                    foreach (var c in cands)
                        if (UnityEngine.Hash128.Compute(text + "|" + c).ToString() == target) { Console.WriteLine("MATCH " + c); return 0; }
                }
            }
        Console.WriteLine("no match");
        return 1;
    }

    static int VerbHash128test(string[] args)
    {
        // hash128test <narration.asset> <signature-for-base-lang> <signature-for-other-langs> : recompute generatedHash
        var docs = MiniUnity.Yaml.YamlReader.ReadFile(args[1]);
        var body = docs[0].Body;
        string baseLang = MiniUnity.Serializer.Str(body["baseLanguage"]);
        var prons = (body["pronunciations"] as MiniUnity.Yaml.YSeq)?.Items.OfType<MiniUnity.Yaml.YMap>().Select(m => (lang: MiniUnity.Serializer.Str(m["lang"]), word: MiniUnity.Serializer.Str(m["word"]), spoken: MiniUnity.Serializer.Str(m["spoken"]))).ToList();
        string Spoken(string text, string lang)
        {
            var rules = prons.Where(p => p.lang == lang && p.word.Length > 0).OrderByDescending(p => p.word.Length).ToList();
            foreach (var p in rules) text = System.Text.RegularExpressions.Regex.Replace(text, @"(?<![\p{L}\p{N}_])" + System.Text.RegularExpressions.Regex.Escape(p.word) + @"(?![\p{L}\p{N}_])", p.spoken);
            return text;
        }
        var entries = new System.Collections.Generic.List<(string lang, string text, string hash)>();
        foreach (var l in ((MiniUnity.Yaml.YSeq)body["lines"]).Items.OfType<MiniUnity.Yaml.YMap>())
        {
            entries.Add((baseLang, MiniUnity.Serializer.Str(l["text"]), MiniUnity.Serializer.Str(l["generatedHash"])));
            if (l["translations"] is MiniUnity.Yaml.YSeq tr) foreach (var t in tr.Items.OfType<MiniUnity.Yaml.YMap>())
                entries.Add((MiniUnity.Serializer.Str(t["lang"]), MiniUnity.Serializer.Str(t["text"]), MiniUnity.Serializer.Str(t["generatedHash"])));
        }
        var sigs = args.Skip(2).ToArray();
        var spokenCache = entries.Select(e => Spoken(e.text, e.lang)).ToArray();
        for (int sm = 0; sm < 2; sm++) for (int tm = 0; tm < 6; tm++)
        {
            UnityEngine.Hash128.StringMode = sm; UnityEngine.Hash128.ToStringMode = tm;
            var byLang = new System.Collections.Generic.Dictionary<string, (int ok, int n, string sig)>();
            for (int ei = 0; ei < entries.Count; ei++)
            {
                var e = entries[ei]; if (e.hash.Length == 0) continue; var spoken = spokenCache[ei];
                bool hit = false; string which = null;
                foreach (var sg0 in sigs) { var sg = sg0.Replace("{lang}", e.lang); var vs = sg.Split('=', 2); if (vs.Length == 2 && vs[0] != e.lang) continue; sg = vs[^1];
                    if (UnityEngine.Hash128.Compute(spoken + "|" + sg).ToString() == e.hash) { hit = true; which = sg; break; } }
                byLang.TryGetValue(e.lang, out var c);
                byLang[e.lang] = (c.ok + (hit ? 1 : 0), c.n + 1, which ?? c.sig);
            }
            Console.WriteLine($"utf{(sm == 0 ? 16 : 8)} tostring{tm}: " + string.Join("  ", byLang.Select(kv => $"{kv.Key} {kv.Value.ok}/{kv.Value.n} [{kv.Value.sig}]")));
        }
        return 0;
    }

    static int VerbRt(string[] args)
    {
        // YAML round-trip test (no object model): parse + re-emit
        var docs = MiniUnity.Yaml.YamlReader.ReadFile(args[1]);
        var w = new MiniUnity.Yaml.YamlWriter();
        w.WriteHeader();
        foreach (var d in docs) w.WriteDoc(d.ClassId, d.FileId, d.TypeName, d.Body, d.Stripped);
        File.WriteAllText(args[2], w.ToString());
        return 0;
    }






    static int VerbFonttest(string[] args)
    {
        // fonttest <root> <fontAsset.asset>: compare MiniUnity's FontEngine with the glyphs Unity put in the asset
        Open(args[1]);
        var fa = UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(args[2]);
        var FE = typeof(UnityEngine.TextCore.LowLevel.FontEngine);
        Console.WriteLine($"load: {UnityEngine.TextCore.LowLevel.FontEngine.LoadFontFace(fa.sourceFontFile, fa.faceInfo.pointSize)} pad {fa.atlasPadding} mode {fa.atlasRenderMode}");
        var atlas = fa.atlasTexture; atlas.EnsurePixels();
        int ok = 0, okRect = 0, n = 0; double num = 0, den = 0, err = 0; int cnt = 0;
        foreach (var g in fa.glyphTable)
        {
            n++;
            UnityEngine.TextCore.LowLevel.FontEngine.TryGetGlyphWithIndexValue(g.index, UnityEngine.TextCore.LowLevel.GlyphLoadFlags.LOAD_DEFAULT, out var mine);
            var a = g.metrics; var b = mine.metrics;
            bool same = Math.Abs(a.width - b.width) < 1e-3 && Math.Abs(a.height - b.height) < 1e-3 && Math.Abs(a.horizontalBearingX - b.horizontalBearingX) < 1e-3 && Math.Abs(a.horizontalBearingY - b.horizontalBearingY) < 1e-3 && Math.Abs(a.horizontalAdvance - b.horizontalAdvance) < 1e-3;
            if (same) ok++; else if (n - ok <= 5) Console.WriteLine($"  glyph {g.index}: unity w{a.width} h{a.height} bx{a.horizontalBearingX} by{a.horizontalBearingY} adv{a.horizontalAdvance} | ours w{b.width} h{b.height} bx{b.horizontalBearingX} by{b.horizontalBearingY} adv{b.horizontalAdvance}");
            if (g.glyphRect.width == mine.glyphRect.width && g.glyphRect.height == mine.glyphRect.height) okRect++;
            // SDF: render ours into a scratch texture at the same rect, compare
            if (g.glyphRect.width == 0) continue;
            var scratch = new UnityEngine.Texture2D(atlas.width, atlas.height, UnityEngine.TextureFormat.Alpha8, false);
            var gg = new UnityEngine.TextCore.Glyph(g.index, g.metrics, g.glyphRect, 1, 0);
            UnityEngine.TextCore.LowLevel.FontEngine.RenderGlyphsToTexture(new System.Collections.Generic.List<UnityEngine.TextCore.Glyph> { gg }, fa.atlasPadding, fa.atlasRenderMode, scratch);
            var r = g.glyphRect; int pad = fa.atlasPadding;
            for (int y = r.y - pad; y < r.y + r.height + pad; y++)
                for (int x = r.x - pad; x < r.x + r.width + pad; x++)
                {
                    float ua = atlas.m_Pixels[y * atlas.width + x].a, ma = scratch.m_Pixels[y * atlas.width + x].a;
                    float dm = (ma - 0.5f), du = (ua - 0.5f);
                    if (ma > 0.02f && ma < 0.98f && ua > 0.02f && ua < 0.98f) { num += du * dm; den += dm * dm; }
                    err += Math.Abs(ua - ma); cnt++;
                }
            if (n > 40) break;
        }
        Console.WriteLine($"metrics identical {ok}/{n}, rect size {okRect}/{n}; SDF mean |Δ| {err / Math.Max(1, cnt) * 255:0.00}/255, best scale factor ×{num / Math.Max(1e-9, den):0.000}");
        return 0;
    }


}
