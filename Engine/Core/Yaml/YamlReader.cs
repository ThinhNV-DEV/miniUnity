using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MiniUnity.Yaml
{
    /// <summary>Parser for the YAML subset Unity writes (.unity/.prefab/.asset/.playable/.meta).</summary>
    public static class YamlReader
    {
        public static List<YDoc> ReadFile(string path) => ReadDocs(File.ReadAllText(path), path);

        // "--- !u!114 &11400000" or "--- !u!1001 &-4216859302048453862 stripped" (file ids may be negative)
        static readonly System.Text.RegularExpressions.Regex header =
            new(@"^--- !u!(\d+) &(-?\d+)( stripped)?[ \t]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>Only a real document header starts a new document: a "--- " line that is not one (inside a block or quoted
        /// scalar) stays part of the current document. A malformed "--- !u!" header fails with the file and line.</summary>
        static bool IsHeader(string l, string source, int lineNo)
        {
            if (!l.StartsWith("--- ")) return false;
            if (header.IsMatch(l)) return true;
            if (l.StartsWith("--- !u!")) throw new FormatException($"{source ?? "<yaml>"}:{lineNo}: malformed Unity document header '{l}' (expected '--- !u!<classID> &<fileID> [stripped]')");
            return false;
        }

        public static List<YDoc> ReadDocs(string text, string source = null)
        {
            var docs = new List<YDoc>();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            int i = 0;
            while (i < lines.Length)
            {
                var l = lines[i];
                if (IsHeader(l, source, i + 1))
                {
                    var d = new YDoc();
                    var hm = header.Match(l);
                    if (!int.TryParse(hm.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out d.ClassId)
                        || !long.TryParse(hm.Groups[2].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out d.FileId))
                        throw new FormatException($"{source ?? "<yaml>"}:{i + 1}: class or file id out of range in '{l}'");
                    d.Stripped = hm.Groups[3].Success;
                    i++;
                    int start = i;
                    while (i < lines.Length && !IsHeader(lines[i], source, i + 1)) i++;
                    var sub = new List<string>();
                    for (int k = start; k < i; k++) sub.Add(lines[k]);
                    var root = Parse(sub) as YMap;
                    if (root != null && root.Entries.Count > 0)
                    {
                        d.TypeName = root.Entries[0].Key;
                        d.Body = root.Entries[0].Value as YMap ?? new YMap();
                    }
                    docs.Add(d);
                    continue;
                }
                i++;
            }
            return docs;
        }

        /// <summary>Parse a plain YAML text (e.g. .meta) into a map.</summary>
        public static YMap ParseText(string text)
        {
            var lines = new List<string>();
            foreach (var l in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (l.StartsWith("%") || l.StartsWith("---")) continue;
                lines.Add(l);
            }
            return Parse(lines) as YMap ?? new YMap();
        }

        sealed class P
        {
            public List<string> L; public int I;
        }

        static YNode Parse(List<string> lines)
        {
            var p = new P { L = lines, I = 0 };
            Skip(p);
            if (p.I >= p.L.Count) return new YMap();
            return ParseBlock(p, Indent(p.L[p.I]));
        }

        static int Indent(string s) { int n = 0; while (n < s.Length && s[n] == ' ') n++; return n; }
        static void Skip(P p) { while (p.I < p.L.Count && (p.L[p.I].Trim().Length == 0 || p.L[p.I].TrimStart().StartsWith("#"))) p.I++; }

        static YNode ParseBlock(P p, int indent)
        {
            Skip(p);
            if (p.I >= p.L.Count) return new YScalar("");
            var line = p.L[p.I];
            int ind = Indent(line);
            if (line.Substring(ind).StartsWith("- ") || line.Substring(ind) == "-") return ParseSeq(p, ind);
            return ParseMap(p, ind);
        }

        static YSeq ParseSeq(P p, int indent)
        {
            var seq = new YSeq();
            while (true)
            {
                Skip(p);
                if (p.I >= p.L.Count) break;
                var line = p.L[p.I];
                int ind = Indent(line);
                if (ind != indent) break;
                var rest = line.Substring(ind);
                if (!(rest.StartsWith("- ") || rest == "-")) break;
                var content = rest.Length > 2 ? rest.Substring(2) : "";
                if (content.Length == 0)
                {
                    p.I++;
                    // "- " followed by a deeper block = nested node; otherwise an empty scalar item (e.g. TagManager layers)
                    int save = p.I;
                    Skip(p);
                    bool deeper = p.I < p.L.Count && Indent(p.L[p.I]) > indent && !p.L[p.I].StartsWith("--- ");
                    p.I = save;
                    seq.Items.Add(deeper ? ParseBlock(p, indent + 2) : new YScalar(""));
                    continue;
                }
                // "- key: value" → mapping item, continuing at indent+2
                if (LooksLikeKey(content, out _))
                {
                    // rewrite the line as a map line at indent+2
                    p.L[p.I] = new string(' ', indent + 2) + content;
                    seq.Items.Add(ParseMap(p, indent + 2));
                    continue;
                }
                p.I++;
                seq.Items.Add(ParseInlineValue(p, content, indent + 2));
            }
            return seq;
        }

        static bool LooksLikeKey(string s, out int colon)
        {
            colon = -1;
            if (s.Length == 0) return false;
            char c = s[0];
            if (c == '{' || c == '[' || c == '"' || c == '\'') return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == ':' && (i + 1 == s.Length || s[i + 1] == ' ')) { colon = i; return true; }
            }
            return false;
        }

        static YMap ParseMap(P p, int indent)
        {
            var map = new YMap();
            while (true)
            {
                Skip(p);
                if (p.I >= p.L.Count) break;
                var line = p.L[p.I];
                int ind = Indent(line);
                if (ind != indent) break;
                var rest = line.Substring(ind);
                if (rest.StartsWith("- ")) break;
                if (!LooksLikeKey(rest, out int colon)) { p.I++; continue; }
                string key = rest.Substring(0, colon);
                if (key.StartsWith("'") && key.EndsWith("'")) key = key.Substring(1, key.Length - 2);
                string val = colon + 1 < rest.Length ? rest.Substring(colon + 2) : "";
                p.I++;
                if (val.Length == 0)
                {
                    // nested block, or sequence at same indent, or empty scalar
                    Skip(p);
                    if (p.I < p.L.Count)
                    {
                        var nl = p.L[p.I]; int ni = Indent(nl);
                        var nr = nl.Substring(ni);
                        if (ni > indent) { map.Add(key, ParseBlock(p, ni)); continue; }
                        if (ni == indent && (nr.StartsWith("- ") || nr == "-")) { map.Add(key, ParseSeq(p, ni)); continue; }
                    }
                    map.Add(key, new YScalar(""));
                    continue;
                }
                map.Add(key, ParseInlineValue(p, val, indent));
            }
            return map;
        }

        /// <summary>Parse a value that starts on the current (already consumed) line; may continue on following lines.</summary>
        static YNode ParseInlineValue(P p, string val, int ownerIndent)
        {
            if (val.StartsWith("{") || val.StartsWith("["))
            {
                // flow; may span lines
                var sb = new StringBuilder(val);
                bool wrapped = false;
                while (!FlowBalanced(sb.ToString()) && p.I < p.L.Count) { sb.Append(' ').Append(p.L[p.I].Trim()); p.I++; wrapped = true; }
                int pos = 0;
                var flowNode = ParseFlow(sb.ToString(), ref pos);
                if (flowNode != null) flowNode.Wrapped = wrapped;
                return flowNode;
            }
            if (val.StartsWith("\""))
            {
                var sb = new StringBuilder(val);
                while (!DoubleClosed(sb.ToString()) && p.I < p.L.Count) { sb.Append('\n').Append(p.L[p.I]); p.I++; }
                return new YScalar(UnescapeDouble(sb.ToString()));
            }
            if (val.StartsWith("'"))
            {
                var sb = new StringBuilder(val);
                while (!SingleClosed(sb.ToString()) && p.I < p.L.Count) { sb.Append('\n').Append(p.L[p.I]); p.I++; }
                return new YScalar(UnescapeSingle(sb.ToString()));
            }
            // plain; continuation lines are more indented than the owner key
            var parts = new List<string> { val.TrimEnd() };
            while (p.I < p.L.Count)
            {
                var nl = p.L[p.I];
                if (nl.Trim().Length == 0) { parts.Add(""); p.I++; continue; }
                if (Indent(nl) <= ownerIndent || nl.StartsWith("--- ")) break;
                parts.Add(nl.Trim()); p.I++;
            }
            while (parts.Count > 1 && parts[^1] == "") parts.RemoveAt(parts.Count - 1);
            return new YScalar(Fold(parts));
        }

        static string Fold(List<string> parts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i == 0) { sb.Append(parts[0]); continue; }
                if (parts[i] == "") { sb.Append('\n'); continue; }
                if (parts[i - 1] != "") sb.Append(' ');
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        static bool FlowBalanced(string s)
        {
            int depth = 0; bool sq = false, dq = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (dq) { if (c == '\\') i++; else if (c == '"') dq = false; continue; }
                if (sq) { if (c == '\'') { if (i + 1 < s.Length && s[i + 1] == '\'') i++; else sq = false; } continue; }
                if (c == '"') dq = true; else if (c == '\'') sq = true;
                else if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
            }
            return depth <= 0;
        }

        static bool DoubleClosed(string s)
        {
            for (int i = 1; i < s.Length; i++)
            {
                if (s[i] == '\\') { i++; continue; }
                if (s[i] == '"') return true;
            }
            return false;
        }
        static bool SingleClosed(string s)
        {
            for (int i = 1; i < s.Length; i++)
            {
                if (s[i] == '\'') { if (i + 1 < s.Length && s[i + 1] == '\'') { i++; continue; } return true; }
            }
            return false;
        }

        static string UnescapeSingle(string s)
        {
            int end = s.LastIndexOf('\'');
            var body = s.Substring(1, end - 1);
            body = FoldQuoted(body, false);
            return body.Replace("''", "'");
        }

        // fold line breaks inside quoted scalars: "a\n   b" → "a b"; blank line → \n
        static string FoldQuoted(string body, bool dbl)
        {
            if (!body.Contains('\n')) return body;
            var lines = body.Split('\n');
            var sb = new StringBuilder(lines[0]);
            bool prevBlank = false;
            for (int i = 1; i < lines.Length; i++)
            {
                string ln = lines[i].TrimStart(' ', '\t');
                if (ln.Length == 0 && i < lines.Length - 1) { sb.Append('\n'); prevBlank = true; continue; }
                if (dbl && EndsWithEscapedBreak(sb)) { sb.Length -= 1; sb.Append(ln); prevBlank = false; continue; }
                if (!prevBlank) sb.Append(' ');
                prevBlank = false;
                sb.Append(ln);
            }
            return sb.ToString();
        }
        static bool EndsWithEscapedBreak(StringBuilder sb)
        {
            if (sb.Length == 0 || sb[^1] != '\\') return false;
            int n = 0; for (int i = sb.Length - 1; i >= 0 && sb[i] == '\\'; i--) n++;
            return n % 2 == 1;
        }

        static string UnescapeDouble(string s)
        {
            // find closing quote
            int end = -1;
            for (int i = 1; i < s.Length; i++) { if (s[i] == '\\') { i++; continue; } if (s[i] == '"') { end = i; break; } }
            var body = s.Substring(1, end - 1);
            body = FoldQuoted(body, true);
            var sb = new StringBuilder();
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c != '\\') { sb.Append(c); continue; }
                char n = body[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '0': sb.Append('\0'); break;
                    case 'a': sb.Append('\a'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'e': sb.Append('\x1b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'v': sb.Append('\v'); break;
                    case ' ': sb.Append(' '); break;
                    case '"': sb.Append('"'); break;
                    case '/': sb.Append('/'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'N': sb.Append('\u0085'); break;
                    case '_': sb.Append('\u00A0'); break;
                    case 'L': sb.Append('\u2028'); break;
                    case 'P': sb.Append('\u2029'); break;
                    case 'x': sb.Append((char)Convert.ToInt32(body.Substring(i + 1, 2), 16)); i += 2; break;
                    case 'u': sb.Append((char)Convert.ToInt32(body.Substring(i + 1, 4), 16)); i += 4; break;
                    case 'U': sb.Append(char.ConvertFromUtf32(Convert.ToInt32(body.Substring(i + 1, 8), 16))); i += 8; break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        static YNode ParseFlow(string s, ref int pos)
        {
            SkipWs(s, ref pos);
            if (s[pos] == '{')
            {
                pos++;
                var m = new YMap { Flow = true };
                while (true)
                {
                    SkipWs(s, ref pos);
                    if (s[pos] == '}') { pos++; break; }
                    int kStart = pos;
                    while (s[pos] != ':') pos++;
                    string key = s.Substring(kStart, pos - kStart).Trim();
                    pos++;
                    var v = ParseFlowValue(s, ref pos);
                    m.Add(key, v);
                    SkipWs(s, ref pos);
                    if (s[pos] == ',') pos++;
                }
                return ToRef(m);
            }
            if (s[pos] == '[')
            {
                pos++;
                var q = new YSeq { Flow = true };
                while (true)
                {
                    SkipWs(s, ref pos);
                    if (s[pos] == ']') { pos++; break; }
                    q.Items.Add(ParseFlowValue(s, ref pos));
                    SkipWs(s, ref pos);
                    if (s[pos] == ',') pos++;
                }
                return q;
            }
            return ParseFlowValue(s, ref pos);
        }

        static YNode ParseFlowValue(string s, ref int pos)
        {
            SkipWs(s, ref pos);
            if (s[pos] == '{' || s[pos] == '[') return ParseFlow(s, ref pos);
            if (s[pos] == '"')
            {
                int st = pos; pos++;
                while (s[pos] != '"') { if (s[pos] == '\\') pos++; pos++; }
                pos++;
                return new YScalar(UnescapeDouble(s.Substring(st, pos - st)));
            }
            if (s[pos] == '\'')
            {
                int st = pos; pos++;
                while (true) { if (s[pos] == '\'') { if (pos + 1 < s.Length && s[pos + 1] == '\'') { pos += 2; continue; } break; } pos++; }
                pos++;
                return new YScalar(UnescapeSingle(s.Substring(st, pos - st)));
            }
            int start = pos;
            while (pos < s.Length && s[pos] != ',' && s[pos] != '}' && s[pos] != ']') pos++;
            return new YScalar(s.Substring(start, pos - start).Trim());
        }

        static void SkipWs(string s, ref int pos) { while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\n')) pos++; }

        /// <summary>{fileID: x, guid: y, type: z} → YRef (raw).</summary>
        static YNode ToRef(YMap m)
        {
            if (m.Entries.Count >= 1 && m.Entries[0].Key == "fileID" && (m.Entries.Count == 1 || m.Entries.Count == 3 && m.Has("guid")))
            {
                long fid = Fmt.ParseL(((YScalar)m["fileID"]).Value);
                string guid = m["guid"] is YScalar g ? g.Value : null;
                int type = m["type"] is YScalar t ? (int)Fmt.ParseL(t.Value) : 0;
                return new YRef(fid, guid, type);
            }
            return m;
        }
    }
}
