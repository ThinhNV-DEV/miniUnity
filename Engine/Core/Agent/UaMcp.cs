// `mu mcp <project>`: Model Context Protocol server (stdio, newline-delimited JSON-RPC 2.0) exposing every `ua` command as a tool.
// Commands that only read / edit project files (scenes, prefabs, assets, settings, Addressables, undo) run inside the server
// process, with every cache cleared before the call, so they are seen fresh and skip the .NET start-up of a child process.
// compile / test / play, package commands and anything that loads the project's compiled types run as `mu ua <project> …` in a
// child process: every compile / play / test starts from freshly built assemblies (a .NET process cannot unload what it loaded)
// and can be killed. MINIUNITY_MCP_INPROCESS=0 runs everything in child processes.
// Tool calls run one at a time on a worker thread while the reader keeps reading stdin, so `notifications/cancelled` can kill a
// running child; every call also has a wall-clock timeout (`timeoutSeconds`, else MINIUNITY_MCP_TIMEOUT, else a per-command default).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace MiniUnity.Agent
{
    public static class UaMcp
    {
        const string Protocol = "2025-06-18";
        static string root;
        static StreamWriter stdout;
        static readonly object sendLock = new();
        static readonly BlockingCollection<JsonObject> calls = new();
        // the tool call being run (request id as JSON text) and its child process, for notifications/cancelled
        static readonly object runLock = new();
        static string runningId;
        static Process runningProc;
        static readonly HashSet<string> cancelled = new();
        static readonly HashSet<string> pending = new();   // queued or running tool calls: only these can be cancelled

        public static int Serve(string[] args)
        {
            if (args.Length < 1) { Console.Error.WriteLine("usage: mu mcp <unity-project>"); return 2; }
            root = Path.GetFullPath(args[0]);
            if (!Directory.Exists(Path.Combine(root, "Assets"))) { Console.Error.WriteLine("not a Unity project (no Assets folder): " + root); return 2; }
            stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            Console.SetOut(Console.Error);   // stray Console.Write from in-process commands must not corrupt the protocol stream
            AssetIndex.Root = root;
            Directory.SetCurrentDirectory(root);
            var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            var worker = new Thread(() =>
            {
                foreach (var m in calls.GetConsumingEnumerable())
                {
                    var keys = Ids(m);
                    var r = Handle(m);
                    lock (runLock) foreach (var k in keys) pending.Remove(k);
                    if (r != null) Send(r);
                }
            }) { IsBackground = true, Name = "mcp-tools" };
            worker.Start();
            string line;
            while ((line = stdin.ReadLine()) != null)
            {
                if (line.Trim().Length == 0) continue;
                JsonNode msg;
                try { msg = JsonNode.Parse(line); }
                catch (Exception e) { Send(Error(null, -32700, "parse error: " + e.Message)); continue; }
                if (msg is JsonArray batch)
                {
                    // JSON-RPC batch: cancellations act at once; the rest is answered together, on the worker when it holds a tool call
                    var rest = new JsonArray();
                    foreach (var m in batch)
                        if (m is JsonObject o && IsCancel(o)) Cancel(o); else rest.Add(m?.DeepClone());
                    var wrapped = new JsonObject { ["__batch"] = rest };
                    if (rest.Any(m => m is JsonObject t && t["method"] is JsonValue tv && tv.TryGetValue<string>(out var tm) && tm == "tools/call")) Enqueue(wrapped);
                    else if (Handle(wrapped) is JsonNode outs) Send(outs);
                    continue;
                }
                if (msg is JsonObject mo && IsCancel(mo)) { Cancel(mo); continue; }
                if (msg is JsonObject call && call["method"] is JsonValue mv && mv.TryGetValue<string>(out var meth) && meth == "tools/call") { Enqueue(call); continue; }
                if (Handle(msg as JsonObject) is JsonObject res) Send(res);
            }
            calls.CompleteAdding();
            worker.Join();
            return 0;
        }

        static void Send(JsonNode n) { lock (sendLock) stdout.WriteLine(n.ToJsonString()); }

        static List<string> Ids(JsonObject m) =>
            m["__batch"] is JsonArray b ? b.Select(x => (x as JsonObject)?["id"]?.ToJsonString()).Where(k => k != null).ToList()
            : m["id"] is JsonNode id ? new List<string> { id.ToJsonString() } : new List<string>();

        static void Enqueue(JsonObject m)
        {
            lock (runLock) foreach (var k in Ids(m)) pending.Add(k);
            calls.Add(m);
        }

        static bool IsCancel(JsonObject m) => m["method"] is JsonValue v && v.TryGetValue<string>(out var s) && s == "notifications/cancelled";

        /// <summary>notifications/cancelled {requestId}: for a queued or running tool call, kill its child if it is running and drop its
        /// response; ids of other requests are ignored.</summary>
        static void Cancel(JsonObject m)
        {
            var key = m["params"]?["requestId"]?.ToJsonString();
            if (key == null) return;
            lock (runLock)
            {
                if (!pending.Contains(key)) return;   // unknown or already answered: ignore (MCP)
                cancelled.Add(key);
                if (runningId == key && runningProc != null) Kill(runningProc);
            }
        }

        static void Kill(Process p)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }   // already exited
            catch (System.ComponentModel.Win32Exception) { }
        }

        static JsonNode Handle(JsonObject m)
        {
            if (m == null) return Error(null, -32600, "invalid request");
            if (m["__batch"] is JsonArray batch)
            {
                var outs = new JsonArray();
                foreach (var x in batch)
                {
                    if (Handle(x as JsonObject) is JsonNode r) outs.Add(r);
                }
                return outs.Count > 0 ? outs : null;
            }
            var id = m["id"]?.DeepClone();
            var method = m["method"]?.GetValue<string>();
            bool notification = !m.ContainsKey("id");
            try
            {
                JsonNode result = method switch
                {
                    "initialize" => Initialize(m["params"] as JsonObject),
                    "ping" => new JsonObject(),
                    "tools/list" => new JsonObject { ["tools"] = Tools() },
                    "tools/call" => Call(m["params"] as JsonObject, id?.ToJsonString()),
                    "resources/list" => new JsonObject { ["resources"] = new JsonArray() },
                    "prompts/list" => new JsonObject { ["prompts"] = new JsonArray() },
                    _ when method != null && method.StartsWith("notifications/") => null,
                    _ => throw new McpError(-32601, "method not found: " + method),
                };
                if (notification || result == null || Dropped(id)) return null;
                return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
            }
            catch (McpError e) { return notification || Dropped(id) ? null : Error(id, e.Code, e.Message); }
            catch (Exception e) { return notification || Dropped(id) ? null : Error(id, -32603, e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>A cancelled request gets no response (MCP cancellation).</summary>
        static bool Dropped(JsonNode id) { lock (runLock) return id != null && cancelled.Remove(id.ToJsonString()); }

        sealed class McpError : Exception { public int Code; public McpError(int code, string msg) : base(msg) { Code = code; } }

        static JsonObject Error(JsonNode id, int code, string message) =>
            new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

        static JsonObject Initialize(JsonObject p)
        {
            var asked = p?["protocolVersion"]?.GetValue<string>();
            return new JsonObject
            {
                ["protocolVersion"] = asked ?? Protocol,
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                ["serverInfo"] = new JsonObject { ["name"] = "miniunity", ["version"] = "1.0" },
                ["instructions"] =
                    "Unity project operations without the Unity Editor (project: " + root + "). Every tool takes positional `args` (strings) and " +
                    "`options` (an object: {\"name\": \"x\"} → --name x, {\"basic\": true} → --basic; objects/arrays are passed as JSON). " +
                    "Results are JSON with ok/error and `written` files. Paths are project-relative (Assets/…); GameObjects are addressed by " +
                    "hierarchy path (\"Parent/Child\", \"Child[1]\" for duplicates, or \"#fileID\"). Any write tool accepts {\"dry-run\": true}. " +
                    "Typical loop: compile → test / play → read diagnostics (file:line) → edit → repeat.",
            };
        }

        static string ToolName(string cmd) => cmd.Replace('.', '_');

        static JsonArray Tools()
        {
            var tools = new JsonArray();
            foreach (var (name, (_, usage)) in UaCommands.All)
            {
                var schema = UaSchema.InputSchema(usage, !ReadOnly(name));
                ((JsonObject)schema["properties"])["timeoutSeconds"] = TimeoutSchema();
                if (name == "play") ((JsonObject)schema["properties"])["images"] = new JsonObject { ["type"] = "boolean", ["description"] = "return --screenshot PNGs as image content (default true)" };
                var u = UaSchema.Parse(usage);
                tools.Add(new JsonObject
                {
                    ["name"] = ToolName(name),
                    ["title"] = "ua " + name,
                    ["description"] = (u.Description.Length > 0 ? u.Description + ". " : "") + "Usage: ua " + name + (u.Syntax.Length > 0 ? " " + u.Syntax : ""),
                    ["inputSchema"] = schema,
                    ["annotations"] = new JsonObject { ["readOnlyHint"] = ReadOnly(name) },
                });
            }
            tools.Add(new JsonObject
            {
                ["name"] = "batch",
                ["description"] = "Run several ua commands in one process, in order: commands = [[\"go.create\", \"Assets/Main.unity\", \"Player\"], [\"comp.add\", …]]. Options of a command are inline (\"--name\", \"x\"); dryRun / stopOnError apply to the whole batch.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["commands"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } } },
                        ["dryRun"] = new JsonObject { ["type"] = "boolean", ["description"] = "run every command with --dry-run (nothing is written)" },
                        ["stopOnError"] = new JsonObject { ["type"] = "boolean", ["description"] = "skip the remaining commands after the first failure" },
                        ["timeoutSeconds"] = TimeoutSchema(),
                    },
                    ["required"] = new JsonArray("commands"),
                },
            });
            return tools;
        }

        static JsonObject TimeoutSchema() => new() { ["type"] = "integer", ["minimum"] = 1, ["description"] = "wall-clock limit; the process is killed after it (default 900 for compile/test/play/pkg.resolve/pkg.add/batch, 120 otherwise)" };

        /// <summary>Commands that change no project asset (compile / test / play write only Library/MiniUnity build outputs).</summary>
        static bool ReadOnly(string cmd)
        {
            var verb = cmd[(cmd.LastIndexOf('.') + 1)..];
            // `play` is not read-only: --screenshot / --audio-out write files and the game may save PlayerPrefs
            return cmd is "compile" or "test" or "log.analyze" or "addr.analyze"
                || verb is "list" or "get" or "find" or "info" or "refs" or "deps" or "missing" or "tree" or "of" or "path" or "overrides" or "entries" or "search" or "assemblies";
        }

        static JsonObject Call(JsonObject p, string requestId)
        {
            var name = p?["name"]?.GetValue<string>() ?? throw new McpError(-32602, "missing tool name");
            var input = p["arguments"] as JsonObject ?? new JsonObject();
            var argv = new List<string>();
            string tmp = null;
            string cmdName = name;
            if (name == "batch")
            {
                tmp = Path.Combine(Path.GetTempPath(), "mu-mcp-" + Guid.NewGuid().ToString("N") + ".jsonl");
                var cmds = input["commands"] as JsonArray ?? throw new McpError(-32602, "batch: commands must be an array of arrays");
                File.WriteAllLines(tmp, cmds.Select(c => c!.ToJsonString()));
                argv.Add("batch"); argv.Add(tmp);
                if (input["dryRun"] is JsonValue dv && dv.TryGetValue<bool>(out var dr) && dr) argv.Add("--dry-run");
                if (input["stopOnError"] is JsonValue sv && sv.TryGetValue<bool>(out var se) && se) argv.Add("--stop-on-error");
            }
            else
            {
                var cmd = UaCommands.All.Keys.FirstOrDefault(k => ToolName(k) == name || k == name) ?? throw new McpError(-32602, "unknown tool: " + name);
                argv.Add(cmd); cmdName = cmd;
                if (input["args"] is JsonArray pos) foreach (var x in pos) argv.Add(Str(x));
                if (input["options"] is JsonObject opts)
                    foreach (var (k, v) in opts)
                    {
                        if (v is JsonValue bv && bv.TryGetValue<bool>(out var b)) { if (b) argv.Add("--" + k); continue; }
                        if (v == null) continue;
                        argv.Add("--" + k); argv.Add(Str(v));
                    }
            }
            try
            {
                int timeout = TimeoutSeconds(cmdName, input["timeoutSeconds"]);
                var (code, stdout, stderr, why) = InProcess(cmdName) ? RunHere(argv, timeout) : RunChild(argv, requestId, timeout);
                JsonNode parsed = null;
                if (why == null) try { parsed = JsonNode.Parse(stdout); } catch (JsonException) { }
                if (why != null) parsed = new JsonObject { ["ok"] = false, ["command"] = cmdName, ["error"] = why };
                bool ok = (parsed?["ok"] is JsonValue okv && okv.TryGetValue<bool>(out var okb)) ? okb : code == 0;
                var text = parsed != null ? parsed.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
                                          : (stdout + (stderr.Length > 0 ? "\n[stderr]\n" + stderr : ""));
                var content = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text });
                if (input["images"] is not JsonValue iv || !iv.TryGetValue<bool>(out var wantImages) || wantImages) AttachImages(parsed, content);
                var res = new JsonObject { ["content"] = content, ["isError"] = !ok };
                if (parsed is JsonObject po) res["structuredContent"] = po.DeepClone();
                return res;
            }
            finally { if (tmp != null) try { File.Delete(tmp); } catch (IOException) { } }
        }

        const int MaxImages = 4, MaxImageBytes = 4 << 20;

        /// <summary>`screenshots` written by the command (play --screenshot) go back as MCP image content, so the client sees the
        /// frames without reading files. Paths are project-relative or absolute; at most 4 PNGs of up to 4 MB each.</summary>
        static void AttachImages(JsonNode parsed, JsonArray content)
        {
            if (parsed?["screenshots"] is not JsonArray shots) return;
            int n = 0;
            foreach (var s in shots)
            {
                if (n >= MaxImages) break;
                var path = Str(s);
                var abs = Path.IsPathRooted(path) ? path : Path.Combine(root, path);
                try
                {
                    var fi = new FileInfo(abs);
                    if (!fi.Exists || fi.Length > MaxImageBytes || !abs.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                    content.Add(new JsonObject { ["type"] = "image", ["mimeType"] = "image/png", ["data"] = Convert.ToBase64String(File.ReadAllBytes(abs)) });
                    n++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        static string Str(JsonNode x) => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : x?.ToJsonString() ?? "";

        // ------------------------------------------------------------------ in-process commands
        static bool inProcessBroken;   // a timed-out in-process call may still be running: later calls go to child processes

        /// <summary>Commands that only read / write project files through the YAML layer (no compiled project types, no network).</summary>
        internal static bool InProcess(string cmd)
        {
            if (inProcessBroken || Environment.GetEnvironmentVariable("MINIUNITY_MCP_INPROCESS") == "0") return false;
            if (cmd is "comp.add" or "so.create" or "addr.init") return false;   // default fields / script GUIDs from compiled types and packages
            return cmd.StartsWith("asset.") || cmd.StartsWith("guid.") || cmd.StartsWith("meta.") || cmd.StartsWith("obj.") || cmd.StartsWith("settings.")
                || cmd.StartsWith("tags.") || cmd.StartsWith("buildscenes.") || cmd.StartsWith("scene.") || cmd.StartsWith("go.") || cmd.StartsWith("comp.")
                || cmd.StartsWith("prefab.") || cmd.StartsWith("addr.") || cmd.StartsWith("undo") || cmd is "ref.replace" or "log.analyze";
        }

        static (int code, string stdout, string stderr, string why) RunHere(List<string> argv, int timeoutSeconds)
        {
            AssetDb.Reset(); SceneGraph.ClearCache(); ScriptRegistry.Reset();   // RefIndex stays: it re-checks every file's mtime
            var work = System.Threading.Tasks.Task.Run(() => UaCommands.Run(argv.ToArray()));
            if (!work.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                inProcessBroken = true;
                return (1, "", "", $"timed out after {timeoutSeconds} s (in-process; later calls run in child processes)");
            }
            var res = work.Result;
            return (res["ok"]?.GetValue<bool>() == true ? 0 : 1, res.ToJsonString(), "", null);
        }

        /// <summary>Wall-clock limit for one tool call: the call's `timeoutSeconds`, else MINIUNITY_MCP_TIMEOUT, else 900 s for
        /// commands that compile or download (compile / test / play / pkg.resolve / batch) and 120 s for the rest.</summary>
        internal static int TimeoutSeconds(string cmd, JsonNode asked)
        {
            if (asked is JsonValue v)
            {
                if (v.TryGetValue<double>(out var d) && d >= 1 && d <= int.MaxValue / 1000) return (int)Math.Ceiling(d);
                if (v.TryGetValue<string>(out var str) && int.TryParse(str, out var ns) && ns > 0 && ns <= int.MaxValue / 1000) return ns;
            }
            if (int.TryParse(Environment.GetEnvironmentVariable("MINIUNITY_MCP_TIMEOUT"), out var n) && n > 0 && n <= int.MaxValue / 1000) return n;
            return cmd is "compile" or "test" or "play" or "pkg.resolve" or "pkg.add" or "batch" ? 900 : 120;
        }

        /// <summary>Run `ua` in a child process. `why` is set (and the process tree killed) on timeout or cancellation.</summary>
        static (int code, string stdout, string stderr, string why) RunChild(List<string> argv, string requestId, int timeoutSeconds)
        {
            var host = Environment.ProcessPath!;
            var psi = new ProcessStartInfo(host) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false, StandardOutputEncoding = Encoding.UTF8 };
            if (Path.GetFileNameWithoutExtension(host) == "dotnet") psi.ArgumentList.Add(typeof(UaMcp).Assembly.Location);
            psi.ArgumentList.Add("ua"); psi.ArgumentList.Add(root);
            foreach (var x in argv) psi.ArgumentList.Add(x);
            using var proc = Process.Start(psi)!;
            lock (runLock)
            {
                runningId = requestId; runningProc = proc;
                if (requestId != null && cancelled.Contains(requestId)) Kill(proc);   // cancelled while queued
            }
            try
            {
                proc.StandardInput.Close();
                var err = proc.StandardError.ReadToEndAsync();
                var outp = proc.StandardOutput.ReadToEndAsync();
                string why = null;
                if (!proc.WaitForExit(checked(timeoutSeconds * 1000)))
                {
                    Kill(proc);
                    why = $"timed out after {timeoutSeconds} s (process killed; pass a larger `timeoutSeconds` or set MINIUNITY_MCP_TIMEOUT)";
                }
                proc.WaitForExit();
                lock (runLock) if (why == null && requestId != null && cancelled.Contains(requestId)) why = "cancelled by the client (process killed)";
                var e = err.Result;
                if (e.Length > 8000) e = e[..8000] + "…";
                return (proc.ExitCode, outp.Result, e, why);
            }
            finally { lock (runLock) { runningId = null; runningProc = null; } }
        }
    }
}
