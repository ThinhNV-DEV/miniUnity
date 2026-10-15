using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MiniUnity.Yaml
{
    /// <summary>Emits Unity-style YAML (2-space indent, sequences at key indent, flow refs, libyaml-ish scalar folding at 80 cols).</summary>
    public sealed class YamlWriter
    {
        readonly StringBuilder sb = new();
        /// <summary>Resolve a live object reference to (fileID, guid, type). guid null → local.</summary>
        public Func<UnityEngine.Object, (long fileID, string guid, int type)> RefResolver;

        public static string Header => "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n";

        public void WriteHeader() => sb.Append(Header);
        public void Raw(string text) => sb.Append(text);

        public void WriteDoc(int classId, long fileId, string typeName, YMap body, bool stripped = false)
        {
            sb.Append("--- !u!").Append(classId.ToString(CultureInfo.InvariantCulture)).Append(" &").Append(fileId.ToString(CultureInfo.InvariantCulture));
            if (stripped) sb.Append(" stripped");
            sb.Append('\n').Append(typeName).Append(":\n");
            WriteMapBody(body, 2);
        }

        /// <summary>Top-level map without a document header (.meta files, package manifests in YAML form).</summary>
        public void WriteMap(YMap m) => WriteMapBody(m, 0);
        /// <summary>A plain YAML map (e.g. a .meta file) without document headers.</summary>
        public static string Dump(YMap m) { var w = new YamlWriter(); w.WriteMap(m); return w.ToString(); }

        public override string ToString() => sb.ToString();

        void WriteMapBody(YMap m, int indent)
        {
            foreach (var e in m.Entries) WriteEntry(e.Key, e.Value, indent, false);
        }

        void Ind(int n) => sb.Append(' ', n);

        void WriteEntry(string key, YNode v, int indent, bool afterDash)
        {
            if (!afterDash) Ind(indent);
            string k = QuoteKey(key);
            sb.Append(k).Append(':');
            int col = indent + k.Length + 1;
            switch (v)
            {
                case null: sb.Append(" {fileID: 0}\n"); break;
                case YScalar s:
                    sb.Append(' ');
                    WriteScalar(s.Value, col + 1, indent + 2);
                    sb.Append('\n');
                    break;
                case YRef r:
                    sb.Append(' ').Append((r.Wrapped ? Wrap(RefText(r), col + 1, indent + 2) : RefText(r))).Append('\n');
                    break;
                case YSeq q:
                    if (q.Items.Count == 0) { sb.Append(" []\n"); break; }
                    sb.Append('\n');
                    WriteSeqBody(q, indent);
                    break;
                case YMap mm:
                    if (mm.Flow || IsFlowCandidate(mm)) { sb.Append(' ').Append((mm.Wrapped ? Wrap(FlowText(mm), col + 1, indent + 2) : FlowText(mm))).Append('\n'); break; }
                    if (mm.Entries.Count == 0) { sb.Append(" {}\n"); break; }
                    sb.Append('\n');
                    WriteMapBody(mm, indent + 2);
                    break;
            }
        }

        void WriteSeqBody(YSeq q, int indent)
        {
            foreach (var item in q.Items)
            {
                Ind(indent); sb.Append("- ");
                switch (item)
                {
                    case YScalar s: WriteScalar(s.Value, indent + 2, indent + 2); sb.Append('\n'); break;
                    case YRef r: sb.Append((r.Wrapped ? Wrap(RefText(r), indent + 2, indent + 2) : RefText(r))).Append('\n'); break;
                    case YMap m:
                        if (m.Flow || IsFlowCandidate(m)) { sb.Append((m.Wrapped ? Wrap(FlowText(m), indent + 2, indent + 2) : FlowText(m))).Append('\n'); break; }
                        if (m.Entries.Count == 0) { sb.Append("{}\n"); break; }
                        bool first = true;
                        foreach (var e in m.Entries) { WriteEntry(e.Key, e.Value, indent + 2, first); first = false; }
                        break;
                    case YSeq sq:
                        if (sq.Items.Count == 0) { sb.Append("[]\n"); break; }
                        // nested sequence: Unity rarely writes these; emit flow
                        sb.Append('\n'); WriteSeqBody(sq, indent + 2); break;
                }
            }
        }

        static bool IsFlowCandidate(YMap m) => false;

        string FlowText(YMap m)
        {
            var t = new StringBuilder("{");
            bool first = true;
            foreach (var e in m.Entries)
            {
                if (!first) t.Append(", ");
                first = false;
                t.Append(e.Key).Append(": ");
                switch (e.Value)
                {
                    case YScalar s: t.Append(FlowScalar(s.Value)); break;
                    case YRef r: t.Append(RefText(r)); break;
                    case YMap mm: t.Append(FlowText(mm)); break;
                    case YSeq q: t.Append("[]"); break;
                }
            }
            return t.Append('}').ToString();
        }

        static string FlowScalar(string s)
        {
            if (s.Length == 0) return "";
            var st = Analyze(s, true);
            if (st == Style.Plain) return s;
            if (st == Style.Single) return "'" + s.Replace("'", "''") + "'";
            return DoubleQuote(s, -1, 0);
        }

        public string RefText(YRef r)
        {
            long fid; string guid; int type;
            if (r.Raw) { fid = r.FileID; guid = r.Guid; type = r.Type; }
            else if (r.IsNull) { fid = 0; guid = null; type = 0; }
            else (fid, guid, type) = RefResolver != null ? RefResolver(r.Target) : (0, null, 0);
            if (fid == 0 && guid == null) return "{fileID: 0}";
            if (guid == null) return "{fileID: " + fid.ToString(CultureInfo.InvariantCulture) + "}";
            return "{fileID: " + fid.ToString(CultureInfo.InvariantCulture) + ", guid: " + guid + ", type: " + type.ToString(CultureInfo.InvariantCulture) + "}";
        }

        static string QuoteKey(string k) => k;

        /// <summary>
        /// libyaml flow-mapping wrap: after each ", " separator the emitter starts a new line (at contIndent) when the column is already
        /// past the best width – e.g. "m_Texture: {fileID: 2800000, guid: …,\n    type: 3}". Quoted scalars are never split.
        /// </summary>
        static string Wrap(string flow, int column, int contIndent)
        {
            if (column + flow.Length <= BestWidth + 1) return flow;
            var o = new StringBuilder();
            int col = column; bool sq = false, dq = false;
            for (int i = 0; i < flow.Length; i++)
            {
                char c = flow[i];
                if (dq) { o.Append(c); col++; if (c == '\\' && i + 1 < flow.Length) { o.Append(flow[++i]); col++; } else if (c == '"') dq = false; continue; }
                if (sq) { o.Append(c); col++; if (c == '\'') sq = false; continue; }
                if (c == '"') dq = true; else if (c == '\'') sq = true;
                if (c == ',' && i + 1 < flow.Length && flow[i + 1] == ' ')
                {
                    o.Append(','); col++;
                    if (col > BestWidth) { o.Append('\n').Append(' ', contIndent); col = contIndent; }
                    else { o.Append(' '); col++; }
                    i++;
                    continue;
                }
                o.Append(c); col++;
            }
            return o.ToString();
        }

        // ---------------- scalars ----------------
        const int BestWidth = 80;

        void WriteScalar(string s, int column, int contIndent)
        {
            if (s.Length == 0) { sb.Length -= 1; sb.Append(' '); return; } // "key: " (Unity keeps trailing space)
            var st = Analyze(s, false);
            if (st == Style.Plain) { sb.Append(Fold(s, column, contIndent, 0)); return; }
            if (st == Style.Single) { sb.Append(SingleQuote(s, column, contIndent)); return; }
            sb.Append(DoubleQuote(s, column, contIndent));
        }

        enum Style { Plain, Single, Double }

        /// <summary>libyaml yaml_emitter_analyze_scalar + style selection (Unity never uses block scalars).</summary>
        static Style Analyze(string s, bool flow)
        {
            bool leadingSpace = false, leadingBreak = false, trailingSpace = false, trailingBreak = false, breakSpace = false, spaceBreak = false;
            bool prevSpace = false, prevBreak = false, lineBreaks = false, special = false, flowInd = false, blockInd = false;
            if (s.StartsWith("---") || s.StartsWith("...")) { flowInd = blockInd = true; }
            bool precededByWs = true;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool followedByWs = i + 1 >= s.Length || s[i + 1] == ' ' || s[i + 1] == '\t' || s[i + 1] == '\n' || s[i + 1] == '\r';
                if (i == 0)
                {
                    if ("#,[]{}&*!|>'\"%@`".IndexOf(c) >= 0) { flowInd = blockInd = true; }
                    if (c == '?' || c == ':') { flowInd = true; if (followedByWs) blockInd = true; }
                    if (c == '-' && followedByWs) { flowInd = blockInd = true; }
                }
                else
                {
                    if (",?[]{}".IndexOf(c) >= 0) flowInd = true;
                    if (c == ':') { flowInd = true; if (followedByWs) blockInd = true; }
                    if (c == '#' && precededByWs) { flowInd = blockInd = true; }
                }
                bool printable = c == '\n' || (c >= 0x20 && c <= 0x7E);
                if (!printable) special = true;
                bool isBreak = c == '\n' || c == '\r';
                if (isBreak) lineBreaks = true;
                if (c == ' ')
                {
                    if (i == 0) leadingSpace = true;
                    if (i == s.Length - 1) trailingSpace = true;
                    if (prevBreak) breakSpace = true;
                    prevSpace = true; prevBreak = false;
                }
                else if (isBreak)
                {
                    if (i == 0) leadingBreak = true;
                    if (i == s.Length - 1) trailingBreak = true;
                    if (prevSpace) spaceBreak = true;
                    prevBreak = true; prevSpace = false;
                }
                else { prevSpace = prevBreak = false; }
                precededByWs = c == ' ' || c == '\t' || isBreak;
            }
            bool flowPlain = true, blockPlain = true, single = true;
            if (leadingSpace || leadingBreak || trailingSpace || trailingBreak) flowPlain = blockPlain = false;
            if (breakSpace) flowPlain = blockPlain = single = false;
            if (spaceBreak || special) flowPlain = blockPlain = single = false;
            if (lineBreaks) flowPlain = blockPlain = false;
            if (flowInd) flowPlain = false;
            if (blockInd) blockPlain = false;
            if (flow ? flowPlain : blockPlain) return Style.Plain;
            return single ? Style.Single : Style.Double;
        }

        /// <summary>libyaml-style folding at spaces when past the best width.</summary>
        static string Fold(string s, int column, int contIndent, int dummy)
        {
            var o = new StringBuilder();
            int col = column;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ' ' && col >= BestWidth && i != 0 && i != s.Length - 1 && s[i + 1] != ' ')
                {
                    o.Append('\n').Append(' ', contIndent);
                    col = contIndent;
                    continue;
                }
                o.Append(c); col++;
            }
            return o.ToString();
        }

        static string SingleQuote(string s, int column, int contIndent)
        {
            var o = new StringBuilder("'");
            int col = column + 1;
            bool breaks = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ' ')
                {
                    if (col >= BestWidth && i != 0 && i != s.Length - 1 && s[i + 1] != ' ')
                    {
                        o.Append('\n').Append(' ', contIndent); col = contIndent; continue;
                    }
                    o.Append(c); col++; continue;
                }
                if (c == '\n')
                {
                    if (!breaks) { o.Append('\n'); col++; }
                    o.Append('\n');
                    col += 1; // Unity quirk: content line breaks do not reset the folding column
                    breaks = true;
                    continue;
                }
                if (breaks) { o.Append(' ', contIndent); col += contIndent; breaks = false; }
                if (c == '\'') { o.Append("''"); col += 2; continue; }
                o.Append(c); col++;
            }
            return o.Append('\'').ToString();
        }

        static string DoubleQuote(string s, int column, int contIndent)
        {
            var o = new StringBuilder("\"");
            int col = column + 1;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                string esc = null;
                if (c == '"') esc = "\\\"";
                else if (c == '\\') esc = "\\\\";
                else if (c == '\n') esc = "\\n";
                else if (c == '\t') esc = "\\t";
                else if (c == '\r') esc = "\\r";
                else if (c == '\0') esc = "\\0";
                else if (c < 0x20) esc = "\\x" + ((int)c).ToString("X2");
                else if (c > 0x7E)
                {
                    if (char.IsHighSurrogate(c) && i + 1 < s.Length)
                    {
                        int cp = char.ConvertToUtf32(c, s[i + 1]); i++;
                        esc = "\\U" + cp.ToString("X8");
                    }
                    else if (c == '\u0085') esc = "\\N";
                    else if (c == '\u00A0') esc = "\\_";
                    else if (c == '\u2028') esc = "\\L";
                    else if (c == '\u2029') esc = "\\P";
                    else if (c <= 0xFF) esc = "\\x" + ((int)c).ToString("X2");
                    else esc = "\\u" + ((int)c).ToString("X4");
                }
                if (esc != null) { o.Append(esc); col += esc.Length; continue; }
                if (c == ' ' && column >= 0 && col >= BestWidth && i != 0 && i != s.Length - 1 && s[i + 1] != ' ')
                {
                    o.Append('\n').Append(' ', contIndent); col = contIndent; continue;
                }
                o.Append(c); col++;
            }
            return o.Append('"').ToString();
        }
    }
}
