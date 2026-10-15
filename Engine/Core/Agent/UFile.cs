// Lossless editing of Unity YAML files (.unity, .prefab, .asset, .mat, .controller, ProjectSettings/*.asset …) for the agent tools.
// Every document is kept as a YDoc tree; untouched documents are written back byte-for-byte (see README › Cách hoạt động, YAML lossless).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniUnity.Yaml;

namespace MiniUnity.Agent
{
    public sealed class UFile
    {
        public string Rel;                 // project-relative path ("Assets/Scenes/Main.unity")
        public string Abs => Path.Combine(AssetIndex.Root, Rel);
        public List<YDoc> Docs = new();
        readonly Dictionary<long, YDoc> byId = new();
        public bool Dirty;
        static readonly Random rng = new();

        public static bool IsYaml(string abs)
        {
            if (!File.Exists(abs)) return false;
            using var f = File.OpenRead(abs);
            var buf = new byte[5];
            return f.Read(buf, 0, 5) == 5 && System.Text.Encoding.ASCII.GetString(buf) == "%YAML";
        }

        public static UFile Load(string rel)
        {
            rel = Ua.Rel(rel);
            var f = new UFile { Rel = rel };
            if (!IsYaml(f.Abs)) throw new UaError($"not a text-serialized Unity YAML file: {rel}");
            f.Docs = YamlReader.ReadFile(f.Abs);
            foreach (var d in f.Docs) f.byId[d.FileId] = d;
            return f;
        }

        public static UFile New(string rel)
        {
            var f = new UFile { Rel = Ua.Rel(rel), Dirty = true };
            return f;
        }

        public YDoc Get(long id) => byId.TryGetValue(id, out var d) ? d : null;
        public YDoc Need(long id) => Get(id) ?? throw new UaError($"{Rel}: no object &{id}");
        public IEnumerable<YDoc> OfClass(int cls) => Docs.Where(d => d.ClassId == cls);

        /// <summary>Unity-style random 63-bit file id that is unique within this file.</summary>
        public long NewId()
        {
            while (true)
            {
                long id = (long)(rng.NextInt64() & 0x7FFFFFFFFFFFFFFF);
                if (id > 1000000 && !byId.ContainsKey(id)) return id;
            }
        }

        public YDoc Add(int classId, string typeName, YMap body, long id = 0, bool stripped = false, int insertAt = -1)
        {
            var d = new YDoc { ClassId = classId, FileId = id != 0 ? id : NewId(), TypeName = typeName, Body = body, Stripped = stripped };
            if (byId.ContainsKey(d.FileId)) throw new UaError($"{Rel}: duplicate file id {d.FileId}");
            if (insertAt >= 0 && insertAt <= Docs.Count) Docs.Insert(insertAt, d); else Docs.Add(d);
            byId[d.FileId] = d;
            Dirty = true;
            return d;
        }

        public void Remove(long id)
        {
            if (!byId.TryGetValue(id, out var d)) return;
            Docs.Remove(d); byId.Remove(id); Dirty = true;
        }

        /// <summary>Insert a document before SceneRoots (Unity keeps SceneRoots last in scene files).</summary>
        public int InsertIndex() { int i = Docs.FindIndex(d => d.ClassId == 1660057539); return i < 0 ? Docs.Count : i; }

        public string Text()
        {
            var w = new YamlWriter();
            w.WriteHeader();
            foreach (var d in Docs) w.WriteDoc(d.ClassId, d.FileId, d.TypeName, d.Body, d.Stripped);
            return w.ToString();
        }

        public void Save()
        {
            if (!Dirty) return;
            Ua.Write(Abs, Text());
            Dirty = false;
        }
    }

    public sealed class UaError : Exception { public UaError(string m) : base(m) { } }
}
