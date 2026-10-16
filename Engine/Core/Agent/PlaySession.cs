// `ua play <scene> --interactive [--serve port]`: the play session stays open and is driven by JSON commands, one per line on
// stdin; every reply is one stdout line starting with "@mu " (anything else on stdout is the game's own output).
//   {"cmd":"step","frames":n} | {"cmd":"step","seconds":s}      advance the player loop
//   {"cmd":"input","spec":"Space,mouse0@2-4,…"}                --input syntax, frames counted from the next frame ("@1"); an item
//                                                               without frames is a one-frame tap
//   {"cmd":"hold","key":"left shift"} / {"cmd":"release",…}     keys / mouse buttons held across steps
//   {"cmd":"mouse","x":400,"y":300}                             mouse position (pixels, origin bottom-left like Input.mousePosition)
//   {"cmd":"screenshot","file":"shot.png"} | {"cmd":"screenshot","inline":true}   render every camera (PNG file or base64)
//   {"cmd":"watch","paths":"Player/Transform.position,…"}      values, like --watch
//   {"cmd":"state"}                                             frame, time, logs and exceptions since the last state
//   {"cmd":"call","path":"Obj/Comp.Method"}                     invoke a method without arguments
//   {"cmd":"quit"}                                              end the session (the normal play summary is printed after it)
// --serve starts a small viewer at http://127.0.0.1:port/ : it shows the frames as the game runs and sends the keyboard and mouse
// of the page to the session, so a person can play the game inside MiniUnity.
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MiniUnity.Agent
{
    public static class PlaySession
    {
        sealed class Request { public JsonObject cmd; public TaskCompletionSource<JsonObject> reply; }

        public static void Run(Func<byte[]> grab, Func<string, JsonNode> watch, Action<string> call, int port, int width, int height)
        {
            var queue = new BlockingCollection<Request>();
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            var reader = new Thread(() =>
            {
                string line;
                var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
                while ((line = stdin.ReadLine()) != null)
                {
                    if (line.Trim().Length == 0) continue;
                    JsonObject cmd;
                    try { cmd = JsonNode.Parse(line) as JsonObject; } catch (JsonException e) { cmd = new JsonObject { ["cmd"] = "error", ["error"] = e.Message }; }
                    var r = new Request { cmd = cmd ?? new JsonObject(), reply = new TaskCompletionSource<JsonObject>() };
                    queue.Add(r);
                    var res = r.reply.Task.Result;
                    lock (stdout) stdout.WriteLine("@mu " + res.ToJsonString());
                    if (res["quit"] is JsonValue) break;
                }
                queue.Add(new Request { cmd = new JsonObject { ["cmd"] = "quit" }, reply = new TaskCompletionSource<JsonObject>() });
            }) { IsBackground = true, Name = "play-session-stdin" };
            reader.Start();

            HttpListener http = null;
            if (port > 0)
            {
                http = new HttpListener();
                http.Prefixes.Add($"http://127.0.0.1:{port}/");
                http.Start();
                new Thread(() => Serve(http, queue, width, height)) { IsBackground = true, Name = "play-session-http" }.Start();
            }
            int logMark = 0, exMark = 0;
            lock (stdout) stdout.WriteLine("@mu " + new JsonObject { ["ready"] = true, ["frame"] = Play.Frame, ["size"] = $"{width}x{height}", ["viewer"] = port > 0 ? $"http://127.0.0.1:{port}/" : null }.ToJsonString());
            try
            {
                foreach (var r in queue.GetConsumingEnumerable())
                {
                    JsonObject res;
                    try { res = Handle(r.cmd, grab, watch, call, ref logMark, ref exMark); }
                    catch (Exception e) { res = new JsonObject { ["ok"] = false, ["error"] = e.GetType().Name + ": " + e.Message }; }
                    r.reply.TrySetResult(res);
                    if (res["quit"] is JsonValue) break;
                }
            }
            finally { try { http?.Stop(); } catch (ObjectDisposedException) { } }
        }

        static string S(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : o[k]?.ToJsonString();
        static double D(JsonObject o, string k, double d) => o[k] is JsonValue v && (v.TryGetValue<double>(out var x) || double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)) ? x : d;

        static JsonObject Handle(JsonObject c, Func<byte[]> grab, Func<string, JsonNode> watch, Action<string> call, ref int logMark, ref int exMark)
        {
            var cmd = S(c, "cmd") ?? "";
            var res = new JsonObject { ["ok"] = true, ["cmd"] = cmd };
            switch (cmd)
            {
                case "step":
                {
                    int n = c["seconds"] != null ? (int)Math.Ceiling(D(c, "seconds", 0) / Play.FrameDelta) : (int)D(c, "frames", 1);
                    for (int i = 0; i < Math.Max(0, n); i++) Play.Step();
                    break;
                }
                case "input": Play.Input.Parse(S(c, "spec") ?? "", Play.Frame, tap: true); break;
                case "hold": Play.Input.Hold(S(c, "key") ?? "", Play.Frame + 1); break;
                case "release": Play.Input.Release(S(c, "key") ?? "", Play.Frame + 1); break;
                case "mouse": Play.Input.MousePosition = new Vector3((float)D(c, "x", 0), (float)D(c, "y", 0), 0); break;
                case "frame":   // viewer: advance, then render
                {
                    int n = (int)D(c, "advance", 1);
                    for (int i = 0; i < Math.Max(0, n); i++) Play.Step();
                    res["png"] = Convert.ToBase64String(grab());
                    break;
                }
                case "screenshot":
                {
                    var png = grab();
                    if (S(c, "file") is string f && f.Length > 0) { File.WriteAllBytes(Ua.Abs(f), png); res["file"] = f; }
                    else res["png"] = Convert.ToBase64String(png);
                    break;
                }
                case "watch":
                    res["watch"] = new JsonObject((S(c, "paths") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => new System.Collections.Generic.KeyValuePair<string, JsonNode>(p.Trim(), watch(p.Trim()))));
                    break;
                case "state":
                {
                    var logs = Play.Log.Skip(logMark).Take(200).Select(l => (JsonNode)new JsonObject { ["type"] = l.Type, ["message"] = l.Message, ["frame"] = l.Frame }).ToArray();
                    logMark = Play.Log.Count;
                    var ex = Play.Exceptions.Skip(exMark).Select(e => (JsonNode)new JsonObject { ["type"] = e.ex.GetType().Name, ["message"] = e.ex.Message, ["where"] = e.where }).ToArray();
                    exMark = Play.Exceptions.Count;
                    res["logs"] = new JsonArray(logs); res["exceptions"] = new JsonArray(ex);
                    break;
                }
                case "call": call(S(c, "path") ?? ""); break;
                case "quit": res["quit"] = true; break;
                case "error": res["ok"] = false; res["error"] = S(c, "error"); break;
                default: res["ok"] = false; res["error"] = $"unknown command '{cmd}' (step, input, hold, release, mouse, screenshot, watch, state, call, quit)"; break;
            }
            res["frame"] = Play.Frame; res["time"] = Math.Round(Play.Time, 4);
            return res;
        }

        // ------------------------------------------------------------------ viewer
        static void Serve(HttpListener http, BlockingCollection<Request> queue, int width, int height)
        {
            while (http.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = http.GetContext(); } catch (Exception) { return; }
                try
                {
                    var path = ctx.Request.Url!.AbsolutePath;
                    byte[] body; string type;
                    if (path == "/" || path == "/index.html") { body = Encoding.UTF8.GetBytes(Viewer.Replace("{W}", width.ToString()).Replace("{H}", height.ToString())); type = "text/html; charset=utf-8"; }
                    else if (path == "/frame")
                    {
                        int adv = int.TryParse(ctx.Request.QueryString["advance"], out var a) ? a : 1;
                        var res = Ask(queue, new JsonObject { ["cmd"] = "frame", ["advance"] = adv });
                        body = res["png"] is JsonValue pv && pv.TryGetValue<string>(out var b64) ? Convert.FromBase64String(b64) : Array.Empty<byte>();
                        type = "image/png";
                    }
                    else if (path == "/cmd" && ctx.Request.HttpMethod == "POST")
                    {
                        using var rd = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                        var cmd = JsonNode.Parse(rd.ReadToEnd()) as JsonObject ?? new JsonObject();
                        body = Encoding.UTF8.GetBytes(Ask(queue, cmd).ToJsonString()); type = "application/json";
                    }
                    else { ctx.Response.StatusCode = 404; body = Encoding.UTF8.GetBytes("not found"); type = "text/plain"; }
                    ctx.Response.ContentType = type;
                    ctx.Response.AddHeader("Cache-Control", "no-store");
                    ctx.Response.OutputStream.Write(body, 0, body.Length);
                }
                catch (Exception e) { try { ctx.Response.StatusCode = 500; var b = Encoding.UTF8.GetBytes(e.Message); ctx.Response.OutputStream.Write(b, 0, b.Length); } catch { } }
                finally { try { ctx.Response.Close(); } catch { } }
            }
        }

        static JsonObject Ask(BlockingCollection<Request> queue, JsonObject cmd)
        {
            var r = new Request { cmd = cmd, reply = new TaskCompletionSource<JsonObject>() };
            try { queue.Add(r); } catch (InvalidOperationException) { return new JsonObject { ["ok"] = false, ["error"] = "session ended" }; }
            return r.reply.Task.Wait(TimeSpan.FromSeconds(60)) ? r.reply.Task.Result : new JsonObject { ["ok"] = false, ["error"] = "timeout" };
        }

        const string Viewer = """
<!doctype html><html><head><meta charset="utf-8"><title>MiniUnity play</title>
<style>body{margin:0;background:#111;color:#ccc;font:13px sans-serif;display:flex;flex-direction:column;align-items:center}
img{max-width:100vw;max-height:90vh;image-rendering:auto;outline:none;cursor:crosshair}#bar{padding:6px}</style></head>
<body><div id="bar">MiniUnity · click the frame to focus · keys and mouse go to the game · <span id="st"></span>
<label><input type="checkbox" id="run" checked> run</label></div><img id="v" tabindex="0" width="{W}" height="{H}">
<script>
const v=document.getElementById('v'),st=document.getElementById('st'),run=document.getElementById('run');
const W={W},H={H};
function post(c){return fetch('/cmd',{method:'POST',body:JSON.stringify(c)}).then(r=>r.json()).then(j=>{st.textContent='frame '+j.frame;return j});}
async function loop(){for(;;){if(!run.checked){await new Promise(r=>setTimeout(r,100));continue;}
 const r=await fetch('/frame?advance=2');const b=await r.blob();const u=URL.createObjectURL(b);
 await new Promise(ok=>{v.onload=()=>{URL.revokeObjectURL(u);ok()};v.src=u;});}}
const keymap={' ':'space','ArrowUp':'up arrow','ArrowDown':'down arrow','ArrowLeft':'left arrow','ArrowRight':'right arrow',
 'Shift':'left shift','Control':'left ctrl','Alt':'left alt','Enter':'return','Escape':'escape','Backspace':'backspace','Tab':'tab'};
function key(e){return keymap[e.key]||(e.key.length==1?e.key.toLowerCase():null);}
v.addEventListener('keydown',e=>{const k=key(e);if(k&&!e.repeat){post({cmd:'hold',key:k});}e.preventDefault();});
v.addEventListener('keyup',e=>{const k=key(e);if(k)post({cmd:'release',key:k});e.preventDefault();});
function pos(e){const r=v.getBoundingClientRect();return{cmd:'mouse',x:(e.clientX-r.left)/r.width*W,y:H-(e.clientY-r.top)/r.height*H};}
v.addEventListener('mousemove',e=>post(pos(e)));
v.addEventListener('mousedown',e=>{v.focus();post(pos(e)).then(()=>post({cmd:'hold',key:'mouse'+e.button}));e.preventDefault();});
v.addEventListener('mouseup',e=>{post({cmd:'release',key:'mouse'+e.button});});
v.addEventListener('contextmenu',e=>e.preventDefault());
loop();
</script></body></html>
""";
    }
}
