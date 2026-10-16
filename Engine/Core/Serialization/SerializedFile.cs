// Reader for Unity's binary SerializedFile (the format of Library/Artifacts import results, built-in resources such as
// Editor/Data/Resources/unity_builtin_extra, asset bundles' inner files and player data), versions 17–22, with type trees.
// Every object is turned into the same YAML node tree a text asset of that type gives (MiniUnity's loaders read it unchanged):
// fields in type-tree order, PPtr → {fileID, guid, type} through the file's externals table, maps with string keys → `- key: value`,
// byte vectors / TypelessData → hex strings (as Unity's YAML writes m_IndexBuffer or _typelessdata).
// Layout follows the documented structure of the format (header, metadata: types with their type-tree blobs, objects,
// script types, externals, ref types); constant type / field names come from Unity's common string table.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MiniUnity.Yaml;

namespace MiniUnity.Serialization
{
    public sealed class SerializedFile
    {
        public sealed class Node
        {
            public string Type, Name; public int ByteSize, Level, MetaFlag; public byte TypeFlags;
            public readonly List<Node> Children = new();
            public bool Align => (MetaFlag & 0x4000) != 0;
            public bool IsArray => (TypeFlags & 1) != 0;
        }
        public sealed class SType { public int ClassId; public bool Stripped; public short ScriptIndex; public Node Root; }
        public sealed class ObjectInfo { public long PathId; public long Start; public uint Size; public int TypeIndex; public int ClassId; }
        public sealed class External { public string Guid; public int Type; public string Path; }

        public int Version; public string UnityVersion; public int Platform; public bool BigEndian, TypeTree;
        public readonly List<SType> Types = new();
        public readonly List<ObjectInfo> Objects = new();
        public readonly List<External> Externals = new();
        byte[] data; long dataOffset; string m_Path;   // m_Path set: only the metadata is in data so far

        public static bool IsSerializedFile(string path)
        {
            try
            {
                using var f = File.OpenRead(path);
                if (f.Length < 48) return false;
                var h = new byte[48]; f.ReadExactly(h);
                uint ver = BE32(h, 8);
                if (ver >= 22) return h[16] <= 1 && BE64(h, 24) == (ulong)f.Length;
                return ver is >= 9 and < 22 && BE32(h, 4) == f.Length;
            }
            catch (IOException) { return false; }
        }

        static uint BE32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
        static ulong BE64(byte[] b, int o) => (ulong)BE32(b, o) << 32 | BE32(b, o + 4);

        public static SerializedFile Open(string path) => new(File.ReadAllBytes(path)) { };

        /// <summary>Header and metadata only (types, object table, externals); object data is read from the file when needed.</summary>
        public static SerializedFile OpenMeta(string path)
        {
            using var f = File.OpenRead(path);
            var h = new byte[48]; f.ReadExactly(h);
            uint ver = BE32(h, 8);
            long metaEnd = ver >= 22 ? 48 + BE32(h, 20) : 20 + BE32(h, 0);
            var bytes = new byte[Math.Min(f.Length, metaEnd + 64)];
            f.Position = 0; f.ReadExactly(bytes);
            return new SerializedFile(bytes, path);
        }

        void EnsureData() { if (m_Path != null) { data = File.ReadAllBytes(m_Path); m_Path = null; } }

        public SerializedFile(byte[] bytes, string path = null)
        {
            data = bytes; m_Path = path;
            var r = new Reader(bytes) { Big = true };
            r.Pos = 8; Version = (int)r.U32();
            if (Version < 17) throw new NotSupportedException($"SerializedFile version {Version} (only 17–22 are read)");
            if (Version >= 22) { r.Pos = 16; BigEndian = r.U8() != 0; r.Pos = 20; r.U32(); r.U64(); dataOffset = (long)r.U64(); r.U64(); }
            else { r.Pos = 12; dataOffset = r.U32(); BigEndian = r.U8() != 0; r.Pos = 20; }
            r.Big = BigEndian;
            UnityVersion = r.CStr(); Platform = r.I32(); TypeTree = r.U8() != 0;
            int nTypes = r.I32();
            for (int i = 0; i < nTypes; i++) Types.Add(ReadType(r, false));
            int nObj = r.I32();
            for (int i = 0; i < nObj; i++)
            {
                r.Align4();
                var o = new ObjectInfo { PathId = r.I64() };
                o.Start = Version >= 22 ? r.I64() : r.U32();
                o.Size = r.U32(); o.TypeIndex = r.I32();
                o.ClassId = o.TypeIndex >= 0 && o.TypeIndex < Types.Count ? Types[o.TypeIndex].ClassId : -1;
                Objects.Add(o);
            }
            int nScripts = r.I32();
            for (int i = 0; i < nScripts; i++) { r.I32(); r.Align4(); r.I64(); }
            int nExt = r.I32();
            for (int i = 0; i < nExt; i++)
            {
                r.CStr();
                var g = r.Bytes(16);
                var sb = new StringBuilder(32);
                foreach (var b in g) { sb.Append("0123456789abcdef"[b & 15]); sb.Append("0123456789abcdef"[b >> 4]); }
                Externals.Add(new External { Guid = sb.ToString(), Type = r.I32(), Path = r.CStr() });
            }
        }

        SType ReadType(Reader r, bool refType)
        {
            var t = new SType { ClassId = r.I32(), Stripped = r.U8() != 0, ScriptIndex = r.I16() };
            if ((refType && t.ScriptIndex >= 0) || t.ClassId < 0 || t.ClassId == 114) r.Bytes(16);   // script id
            r.Bytes(16);   // old type hash
            if (TypeTree)
            {
                t.Root = ReadTree(r);
                if (Version >= 21)
                {
                    if (refType) { r.CStr(); r.CStr(); r.CStr(); }
                    else { int n = r.I32(); for (int i = 0; i < n; i++) r.I32(); }
                }
            }
            return t;
        }

        static Node ReadTree(Reader r)
        {
            int count = r.I32(), strSize = r.I32();
            var raw = new (ushort ver, byte level, byte flags, uint typeOff, uint nameOff, int size, int index, int meta)[count];
            for (int i = 0; i < count; i++)
            {
                raw[i] = (r.U16(), r.U8(), r.U8(), r.U32(), r.U32(), r.I32(), r.I32(), r.I32());
                r.U64();   // ref type hash (v19+)
            }
            var strings = r.Bytes(strSize);
            string Str(uint off)
            {
                if ((off & 0x80000000) != 0) return CommonStrings.TryGetValue((int)(off & 0x7fffffff), out var c) ? c : "?" + (off & 0x7fffffff);
                int e = (int)off; while (e < strings.Length && strings[e] != 0) e++;
                return Encoding.UTF8.GetString(strings, (int)off, e - (int)off);
            }
            var stack = new Stack<Node>(); Node root = null;
            foreach (var n in raw)
            {
                var node = new Node { Type = Str(n.typeOff), Name = Str(n.nameOff), ByteSize = n.size, Level = n.level, MetaFlag = n.meta, TypeFlags = n.flags };
                while (stack.Count > 0 && stack.Peek().Level >= node.Level) stack.Pop();
                if (stack.Count == 0) root = node; else stack.Peek().Children.Add(node);
                stack.Push(node);
            }
            return root;
        }

        // ------------------------------------------------------------------ objects
        public SType TypeOf(ObjectInfo o) => o.TypeIndex >= 0 && o.TypeIndex < Types.Count ? Types[o.TypeIndex] : null;

        /// <summary>The object's fields as the YAML body of a text asset (null when the file has no type tree for it).</summary>
        public YMap Read(ObjectInfo o)
        {
            var t = TypeOf(o);
            if (t?.Root == null) return null;
            EnsureData();
            var r = new Reader(data) { Big = BigEndian, Pos = (int)(dataOffset + o.Start) };
            var map = new YMap();
            Fields(r, t.Root, map);
            return map;
        }

        /// <summary>The object's m_Name without reading the rest (first string field), or null.</summary>
        public string NameOf(ObjectInfo o)
        {
            var t = TypeOf(o);
            if (t?.Root == null) return null;
            EnsureData();
            var r = new Reader(data) { Big = BigEndian, Pos = (int)(dataOffset + o.Start) };
            foreach (var c in t.Root.Children)
            {
                if (c.Name == "m_Name" && c.Type == "string") return (ReadNode(r, c) as YScalar)?.Value;
                if (c.ByteSize < 0 || c.Type == "string") return null;   // variable-size field before the name
                ReadNode(r, c);
            }
            return null;
        }

        public string TypeName(ObjectInfo o) => TypeOf(o)?.Root?.Type ?? ("Class" + o.ClassId);

        YNode ReadNode(Reader r, Node n)
        {
            YNode v;
            if (n.Type == "string" && n.Children.Count > 0 && n.Children[0].IsArray)
            {
                int len = r.I32(); v = new YScalar(Encoding.UTF8.GetString(r.Bytes(len)));
                if (n.Align || n.Children[0].Align) r.Align4();
                return v;
            }
            if (n.Type == "TypelessData")
            {
                int len = r.I32(); v = new YScalar(Convert.ToHexString(r.Bytes(len)).ToLowerInvariant());
                if (n.Align) r.Align4();
                return v;
            }
            if (n.Type.StartsWith("PPtr<", StringComparison.Ordinal) && n.Children.Count == 2)
            {
                int file = r.I32(); long path = r.I64();
                if (n.Align) r.Align4();
                if (file == 0) return new YRef(path, null, 0);
                var ext = file - 1 < Externals.Count ? Externals[file - 1] : null;
                return new YRef(path, ext?.Guid, ext?.Type ?? 0);
            }
            if (n.Children.Count == 0)
            {
                v = Primitive(r, n);
                if (n.Align) r.Align4();
                return v;
            }
            var arrayNode = n.IsArray ? n : n.Children.Count == 1 && n.Children[0].IsArray ? n.Children[0] : null;
            if (arrayNode != null)
            {
                int count = r.I32();
                var elem = arrayNode.Children[1];
                if (elem.Children.Count == 0 && elem.Type is "UInt8" or "SInt8" or "char")
                {
                    v = new YScalar(Convert.ToHexString(r.Bytes(count)).ToLowerInvariant());
                }
                else
                {
                    var seq = new YSeq();
                    for (int i = 0; i < count; i++)
                    {
                        var item = ReadNode(r, elem);
                        // map / pair<string, T>: Unity's YAML writes `- key: value`
                        if (elem.Type == "pair" && item is YMap pm && pm["first"] is YScalar k && elem.Children[0].Type == "string") item = new YMap().Add(k.Value, pm["second"]);
                        seq.Items.Add(item);
                    }
                    if (count == 0) seq.Flow = true;
                    v = seq;
                }
                if (arrayNode.Align || n.Align) r.Align4();
                return v;
            }
            var map = new YMap();
            Fields(r, n, map);
            if (n.Align) r.Align4();
            return map;
        }

        /// <summary>Children of a struct node into a map; TypelessData is written the YAML way: its byte count under its own name
        /// (m_DataSize, image data) and the bytes as hex under _typelessdata.</summary>
        void Fields(Reader r, Node n, YMap map)
        {
            foreach (var c in n.Children)
            {
                if (c.Type == "TypelessData")
                {
                    int len = r.I32();
                    map.Add(c.Name, YScalar.Of(len));
                    map.Add("_typelessdata", new YScalar(Convert.ToHexString(r.Bytes(len)).ToLowerInvariant()));
                    if (c.Align) r.Align4();
                    continue;
                }
                map.Add(c.Name, ReadNode(r, c));
            }
        }

        /// <summary>Every object as a YAML document (class id, fileID = path id, type name, body), ready for AssetLoader.</summary>
        public List<YDoc> ToDocs()
        {
            var docs = new List<YDoc>();
            foreach (var o in Objects)
                if (Read(o) is YMap body) docs.Add(new YDoc { ClassId = o.ClassId, FileId = o.PathId, TypeName = TypeName(o), Body = body });
            return docs;
        }

        static YNode Primitive(Reader r, Node n) => n.Type switch
        {
            "bool" => YScalar.Of(r.U8() != 0),
            "SInt8" => YScalar.Of((int)(sbyte)r.U8()),
            "UInt8" or "char" => YScalar.Of((int)r.U8()),
            "SInt16" or "short" => YScalar.Of((int)r.I16()),
            "UInt16" or "unsigned short" => YScalar.Of((int)r.U16()),
            "SInt32" or "int" or "Type*" => YScalar.Of(r.I32()),
            "UInt32" or "unsigned int" => YScalar.Of((long)r.U32()),
            "SInt64" or "long long" or "FileSize" => YScalar.Of(r.I64()),
            "UInt64" or "unsigned long long" => new YScalar(r.U64().ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "float" => YScalar.Of(r.F32()),
            "double" => YScalar.Of(r.F64()),
            _ => Skip(r, n),
        };

        static YNode Skip(Reader r, Node n) { if (n.ByteSize > 0) r.Pos += n.ByteSize; return new YScalar(""); }

        // ------------------------------------------------------------------ reader
        sealed class Reader
        {
            readonly byte[] b; public int Pos; public bool Big;
            public Reader(byte[] bytes) { b = bytes; }
            public byte U8() => b[Pos++];
            public ushort U16() { var v = Big ? (ushort)(b[Pos] << 8 | b[Pos + 1]) : BitConverter.ToUInt16(b, Pos); Pos += 2; return v; }
            public short I16() => (short)U16();
            public uint U32() { uint v = Big ? (uint)(b[Pos] << 24 | b[Pos + 1] << 16 | b[Pos + 2] << 8 | b[Pos + 3]) : BitConverter.ToUInt32(b, Pos); Pos += 4; return v; }
            public int I32() => (int)U32();
            public ulong U64() { ulong v = Big ? (ulong)U32() << 32 | U32() : BitConverter.ToUInt64(b, Pos); if (!Big) Pos += 8; return v; }
            public long I64() => (long)U64();
            public float F32() { uint u = U32(); return BitConverter.Int32BitsToSingle((int)u); }
            public double F64() => BitConverter.Int64BitsToDouble(I64());
            public byte[] Bytes(int n) { var r = new byte[n]; Buffer.BlockCopy(b, Pos, r, 0, n); Pos += n; return r; }
            public string CStr() { int s = Pos; while (b[Pos] != 0) Pos++; var str = Encoding.UTF8.GetString(b, s, Pos - s); Pos++; return str; }
            public void Align4() => Pos = (Pos + 3) & ~3;
        }

        // Unity's common string table (type and field names stored once in the engine; a type tree refers to them by offset)
        static readonly Dictionary<int, string> CommonStrings = BuildCommon();
        static Dictionary<int, string> BuildCommon()
        {
            const string all = "AABB\0AnimationClip\0AnimationCurve\0AnimationState\0Array\0Base\0BitField\0bitset\0bool\0char\0ColorRGBA\0Component\0data\0deque\0double\0dynamic_array\0FastPropertyName\0first\0float\0Font\0GameObject\0Generic Mono\0GradientNEW\0GUID\0GUIStyle\0int\0list\0long long\0map\0Matrix4x4f\0MdFour\0MonoBehaviour\0MonoScript\0m_ByteSize\0m_Curve\0m_EditorClassIdentifier\0m_EditorHideFlags\0m_Enabled\0m_ExtensionPtr\0m_GameObject\0m_Index\0m_IsArray\0m_IsStatic\0m_MetaFlag\0m_Name\0m_ObjectHideFlags\0m_PrefabInternal\0m_PrefabParentObject\0m_Script\0m_StaticEditorFlags\0m_Type\0m_Version\0Object\0pair\0PPtr<Component>\0PPtr<GameObject>\0PPtr<Material>\0PPtr<MonoBehaviour>\0PPtr<MonoScript>\0PPtr<Object>\0PPtr<Prefab>\0PPtr<Sprite>\0PPtr<TextAsset>\0PPtr<Texture>\0PPtr<Texture2D>\0PPtr<Transform>\0Prefab\0Quaternionf\0Rectf\0RectInt\0RectOffset\0second\0set\0short\0size\0SInt16\0SInt32\0SInt64\0SInt8\0staticvector\0string\0TextAsset\0TextMesh\0Texture\0Texture2D\0Transform\0TypelessData\0UInt16\0UInt32\0UInt64\0UInt8\0unsigned int\0unsigned long long\0unsigned short\0vector\0Vector2f\0Vector3f\0Vector4f\0m_ScriptingClassIdentifier\0Gradient\0Type*\0int2_storage\0int3_storage\0BoundsInt\0m_CorrespondingSourceObject\0m_PrefabInstance\0m_PrefabAsset\0FileSize\0Hash128\0RenderingLayerMask\0";
            var d = new Dictionary<int, string>();
            int start = 0;
            for (int i = 0; i < all.Length; i++) if (all[i] == '\0') { d[start] = all[start..i]; start = i + 1; }
            return d;
        }

        /// <summary>Self-check of the common string offsets against values the format is known to use.</summary>
        internal static bool CommonTableOk() => CommonStrings.TryGetValue(49, out var a) && a == "Array" && CommonStrings.TryGetValue(222, out var i) && i == "int"
            && CommonStrings.TryGetValue(427, out var n) && n == "m_Name" && CommonStrings.TryGetValue(840, out var s) && s == "string" && CommonStrings.TryGetValue(981, out var v) && v == "vector";
    }
}
