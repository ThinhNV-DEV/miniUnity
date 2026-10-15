// Undo journal and diffs for the `ua` write commands.
// Every change a command makes to the project goes through Ua.Write / WriteBytes / Delete / Move / MakeDir, which record the
// previous state in a journal (Library/MiniUnity/undo/<n>/: manifest.json + backups). `undo` replays the last journal backwards
// (restore, delete what was created, move back) after checking that the files are still as the command left them; `--force`
// skips that check. The last 30 journals are kept. `--dry-run` / `--diff` return a unified diff of every text file written.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MiniUnity.Agent
{
    public static class UaJournal
    {
        const int Keep = 30;
        sealed class Op
        {
            public string Kind;      // write | delete-file | delete-dir | move | mkdir
            public string Path, To;  // project-relative
            public bool Existed;     // write: the file existed before
            public string Backup;    // file name (write / delete-file) or folder name (delete-dir) inside the journal
            public string After;     // state after the command, checked before undoing: sha256 / "absent" / "present"
        }

        static readonly JsonSerializerOptions fields = new() { IncludeFields = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        static List<Op> ops;
        static HashSet<string> backedUp;
        static string dir, command;
        static int seq;

        static string Root => Path.Combine(Ua.Root, "Library/MiniUnity/undo");

        public static void Begin(string cmd, IEnumerable<string> args)
        {
            ops = new List<Op>(); backedUp = new HashSet<string>(StringComparer.Ordinal); dir = null; seq = 0;
            command = string.Join(" ", new[] { cmd }.Concat(args.Select(a => a.Contains(' ') ? "'" + a + "'" : a)));
        }

        public static void Abandon() { ops = null; dir = null; }

        static string Dir()
        {
            if (dir != null) return dir;
            Directory.CreateDirectory(Root);
            int n = Entries().Select(e => e.n).DefaultIfEmpty(0).Max() + 1;
            dir = Path.Combine(Root, n.ToString("D4", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static string Store(byte[] data)
        {
            var name = (seq++).ToString(CultureInfo.InvariantCulture) + ".bak";
            File.WriteAllBytes(Path.Combine(Dir(), name), data);
            return name;
        }

        static bool Active => ops != null && !Ua.DryRun;

        /// <summary>Before a file is written: keep its previous content (once per command) or note that it did not exist.</summary>
        public static void BeforeWrite(string abs)
        {
            if (!Active) return;
            var rel = Ua.Rel(abs);
            if (!backedUp.Add("w:" + rel)) return;
            bool existed = File.Exists(abs);
            ops.Add(new Op { Kind = "write", Path = rel, Existed = existed, Backup = existed ? Store(File.ReadAllBytes(abs)) : null });
        }

        public static void BeforeDelete(string abs)
        {
            if (!Active) return;
            var rel = Ua.Rel(abs);
            if (File.Exists(abs)) ops.Add(new Op { Kind = "delete-file", Path = rel, Backup = Store(File.ReadAllBytes(abs)) });
            else if (Directory.Exists(abs))
            {
                var name = (seq++).ToString(CultureInfo.InvariantCulture) + ".dir";
                CopyTree(abs, Path.Combine(Dir(), name));
                ops.Add(new Op { Kind = "delete-dir", Path = rel, Backup = name });
            }
        }

        public static void Moved(string fromAbs, string toAbs)
        {
            if (Active) ops.Add(new Op { Kind = "move", Path = Ua.Rel(fromAbs), To = Ua.Rel(toAbs) });
        }

        public static void MadeDir(string abs)
        {
            if (Active) ops.Add(new Op { Kind = "mkdir", Path = Ua.Rel(abs) });
        }

        /// <summary>Write the journal of the command that just ran (also after a failure: what it changed can be undone).</summary>
        public static int? Commit(bool ok)
        {
            if (ops == null || ops.Count == 0 || Ua.DryRun) { Abandon(); return null; }
            foreach (var op in ops)
                op.After = op.Kind switch
                {
                    "write" => State(op.Path),
                    "move" => State(op.To),
                    "mkdir" => Directory.Exists(Ua.Abs(op.Path)) ? "present" : "absent",
                    _ => "absent",
                };
            var m = new JsonObject
            {
                ["command"] = command, ["ok"] = ok, ["time"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["ops"] = new JsonArray(ops.Select(o => (JsonNode)JsonSerializer.SerializeToNode(o, fields)).ToArray()),
            };
            var d = Dir();
            File.WriteAllText(Path.Combine(d, "manifest.json"), m.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            int n = int.Parse(Path.GetFileName(d), CultureInfo.InvariantCulture);
            foreach (var (old, _) in Entries().OrderByDescending(e => e.n).Skip(Keep).Select(e => (e.path, e.n)))
                try { Directory.Delete(old, true); } catch (IOException) { }
            Abandon();
            return n;
        }

        static string State(string rel)
        {
            var abs = Ua.Abs(rel);
            if (File.Exists(abs)) return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(abs)));
            return Directory.Exists(abs) ? "present" : "absent";
        }

        static IEnumerable<(int n, string path)> Entries()
        {
            if (!Directory.Exists(Root)) yield break;
            foreach (var d in Directory.GetDirectories(Root))
                if (int.TryParse(Path.GetFileName(d), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && File.Exists(Path.Combine(d, "manifest.json")))
                    yield return (n, d);
        }

        static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
            foreach (var d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
        }

        static JsonObject Manifest(string d) => JsonNode.Parse(File.ReadAllText(Path.Combine(d, "manifest.json"))) as JsonObject;

        public static JsonArray List()
        {
            var res = new JsonArray();
            foreach (var (n, d) in Entries().OrderByDescending(e => e.n))
            {
                var m = Manifest(d);
                res.Add(new JsonObject
                {
                    ["id"] = n, ["command"] = m["command"]?.DeepClone(), ["ok"] = m["ok"]?.DeepClone(), ["time"] = m["time"]?.DeepClone(),
                    ["files"] = new JsonArray(((JsonArray)m["ops"]).Select(o => (JsonNode)(o["Kind"] + " " + o["Path"] + (o["To"] is JsonNode t ? " → " + t : ""))).ToArray()),
                });
            }
            return res;
        }

        /// <summary>Undo the newest journal: check, then replay its operations backwards.</summary>
        public static JsonObject UndoLast(bool force)
        {
            var last = Entries().OrderByDescending(e => e.n).FirstOrDefault();
            if (last.path == null) throw new UaError("nothing to undo (no journal in Library/MiniUnity/undo)");
            var m = Manifest(last.path);
            var list = ((JsonArray)m["ops"]).Select(o => o.Deserialize<Op>(fields)).ToList();
            var conflicts = new List<string>();
            foreach (var op in list)
            {
                var now = op.Kind switch { "write" => State(op.Path), "move" => State(op.To), "mkdir" => Directory.Exists(Ua.Abs(op.Path)) ? "present" : "absent", _ => op.After };
                if (op.Kind is "delete-file" or "delete-dir" && (File.Exists(Ua.Abs(op.Path)) || Directory.Exists(Ua.Abs(op.Path)))) now = "present";
                if (now != op.After) conflicts.Add($"{op.Path}{(op.To != null ? " → " + op.To : "")} changed since `{m["command"]}`");
            }
            if (conflicts.Count > 0 && !force)
                throw new UaError("undo refused, files changed after the command (use --force to restore anyway): " + string.Join("; ", conflicts.Take(8)));
            var restored = new JsonArray();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var op = list[i];
                var abs = Ua.Abs(op.Path);
                SafeIO.Check(abs);
                switch (op.Kind)
                {
                    case "write":
                        if (op.Existed) SafeIO.AtomicWriteBytes(abs, File.ReadAllBytes(Path.Combine(last.path, op.Backup)));
                        else if (File.Exists(abs)) File.Delete(abs);
                        break;
                    case "delete-file":
                        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
                        SafeIO.AtomicWriteBytes(abs, File.ReadAllBytes(Path.Combine(last.path, op.Backup)));
                        break;
                    case "delete-dir":
                        CopyTree(Path.Combine(last.path, op.Backup), abs);
                        break;
                    case "move":
                        var to = Ua.Abs(op.To);
                        SafeIO.Check(to);
                        if (Directory.Exists(to)) Directory.Move(to, abs); else if (File.Exists(to)) File.Move(to, abs);
                        break;
                    case "mkdir":
                        if (Directory.Exists(abs) && !Directory.EnumerateFileSystemEntries(abs).Any()) Directory.Delete(abs);
                        break;
                }
                restored.Add(JsonValue.Create(op.Kind + " " + op.Path));
            }
            Directory.Delete(last.path, true);
            return new JsonObject { ["undone"] = m["command"]?.DeepClone(), ["id"] = last.n, ["restored"] = restored, ["forced"] = conflicts.Count > 0 };
        }
    }

    /// <summary>Line diff in unified format (Myers on the part between the common prefix and suffix).</summary>
    public static class UaDiff
    {
        public const int MaxLines = 400;

        public static string Unified(string path, string before, string after, int context = 3)
        {
            var a = Lines(before); var b = Lines(after);
            var ops = Diff(a, b);
            if (ops.All(o => o.op == ' ')) return "";
            var sb = new StringBuilder();
            sb.Append("--- a/").Append(path).Append('\n').Append("+++ b/").Append(path).Append('\n');
            // positions in a / b for each op
            var pos = new List<(int ai, int bi)>(ops.Count);
            int ia = 0, ib = 0;
            foreach (var (op, _) in ops) { pos.Add((ia, ib)); if (op != '+') ia++; if (op != '-') ib++; }
            int i = 0, lines = 0;
            while (i < ops.Count)
            {
                if (ops[i].op == ' ') { i++; continue; }
                int start = Math.Max(0, i - context), end = i;
                // extend while the next change is within 2*context unchanged lines
                while (true)
                {
                    int j = end + 1;
                    while (j < ops.Count && ops[j].op == ' ' && j - end <= 2 * context) j++;
                    if (j < ops.Count && ops[j].op != ' ' && j - end <= 2 * context) { end = j; continue; }
                    break;
                }
                int stop = Math.Min(ops.Count, end + context + 1);
                int aStart = pos[start].ai, bStart = pos[start].bi;
                int aLen = ops.Skip(start).Take(stop - start).Count(o => o.op != '+'), bLen = ops.Skip(start).Take(stop - start).Count(o => o.op != '-');
                sb.Append("@@ -").Append(aStart + 1).Append(',').Append(aLen).Append(" +").Append(bStart + 1).Append(',').Append(bLen).Append(" @@\n");
                for (int k = start; k < stop; k++)
                {
                    if (++lines > MaxLines) { sb.Append("… (diff truncated)\n"); return sb.ToString(); }
                    sb.Append(ops[k].op).Append(ops[k].line).Append('\n');
                }
                i = stop;
            }
            return sb.ToString();
        }

        static string[] Lines(string s) => s.Length == 0 ? Array.Empty<string>() : s.TrimEnd('\n').Replace("\r\n", "\n").Split('\n');

        public static List<(char op, string line)> Diff(string[] a, string[] b)
        {
            int pre = 0;
            while (pre < a.Length && pre < b.Length && a[pre] == b[pre]) pre++;
            int suf = 0;
            while (suf < a.Length - pre && suf < b.Length - pre && a[a.Length - 1 - suf] == b[b.Length - 1 - suf]) suf++;
            var res = new List<(char, string)>();
            for (int i = 0; i < pre; i++) res.Add((' ', a[i]));
            res.AddRange(Myers(a[pre..(a.Length - suf)], b[pre..(b.Length - suf)]));
            for (int i = a.Length - suf; i < a.Length; i++) res.Add((' ', a[i]));
            return res;
        }

        static List<(char, string)> Myers(string[] a, string[] b)
        {
            int n = a.Length, m = b.Length, max = n + m;
            var res = new List<(char, string)>();
            if (max == 0) return res;
            if (n == 0 || m == 0 || max > 20000)
            {
                foreach (var x in a) res.Add(('-', x));
                foreach (var y in b) res.Add(('+', y));
                return res;
            }
            int off = max;
            var v = new int[2 * max + 2];
            var trace = new List<int[]>();
            int dEnd = -1;
            for (int d = 0; d <= max && dEnd < 0; d++)
            {
                trace.Add((int[])v.Clone());
                for (int k = -d; k <= d; k += 2)
                {
                    int x = k == -d || (k != d && v[off + k - 1] < v[off + k + 1]) ? v[off + k + 1] : v[off + k - 1] + 1;
                    int y = x - k;
                    while (x < n && y < m && a[x] == b[y]) { x++; y++; }
                    v[off + k] = x;
                    if (x >= n && y >= m) { dEnd = d; break; }
                }
            }
            // backtrack
            int cx = n, cy = m;
            var rev = new List<(char, string)>();
            for (int d = dEnd; d > 0; d--)
            {
                var pv = trace[d];
                int k = cx - cy;
                int pk = k == -d || (k != d && pv[off + k - 1] < pv[off + k + 1]) ? k + 1 : k - 1;
                int px = pv[off + pk], py = px - pk;
                while (cx > px && cy > py) { rev.Add((' ', a[cx - 1])); cx--; cy--; }
                if (cx == px) { rev.Add(('+', b[cy - 1])); cy--; } else { rev.Add(('-', a[cx - 1])); cx--; }
            }
            while (cx > 0 && cy > 0) { rev.Add((' ', a[cx - 1])); cx--; cy--; }
            rev.Reverse();
            return rev;
        }
    }
}
