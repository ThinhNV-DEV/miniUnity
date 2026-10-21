using System.Collections.Generic;
using System;
using System.IO;
using MiniUnity.Yaml;
using UnityEngine;

namespace MiniUnity
{
    /// <summary>Creates the objects an importer would produce for non-YAML assets (identity + the few properties code reads).</summary>
    public static class Importers
    {
        public static void Import(LoadedAsset la, string ext)
        {
            var meta = AssetIndex.ReadMeta(la.Path);
            void Add(long fid, UnityEngine.Object o, bool main = false)
            {
                o.m_Asset = new AssetIdentity { Path = la.Path, Guid = la.Guid, FileID = fid, RefType = 3 };
                if (string.IsNullOrEmpty(o.m_Name)) o.m_Name = Path.GetFileNameWithoutExtension(la.Path);
                la.Objects[fid] = o; la.Order.Add(fid);
                if (main || la.Main == null) la.Main = o;
            }
            switch (ext)
            {
                case ".png": case ".jpg": case ".jpeg": case ".tga": case ".psd": case ".exr":
                {
                    var ti = meta["TextureImporter"] as YMap;
                    var (w, h) = ImageSize(AssetIndex.Abs(la.Path));
                    var tex = new Texture2D { m_W = w, m_H = h, m_SourceFile = AssetIndex.Abs(la.Path), m_Linear = Serializer.Int(ti?["sRGBTexture"], 1) == 0 };
                    tex.filterMode = (FilterMode)Serializer.Int(ti?["textureSettings"] is YMap ts ? ts["filterMode"] : null, 1);
                    tex.wrapMode = (TextureWrapMode)Serializer.Int(ti?["textureSettings"] is YMap ts2 ? ts2["wrapU"] : null, 0);
                    Add(2800000, tex, true);
                    int texType = Serializer.Int(ti?["textureType"], 0);
                    int spriteMode = Serializer.Int(ti?["spriteMode"], 0);
                    if (texType == 8 && spriteMode == 1)
                    {
                        var b = ti?["spriteBorder"] as YMap;
                        var pv = Serializer.ReadV2(ti?["spritePivot"]);
                        var sp = new Sprite
                        {
                            m_Texture = tex, m_Rect = new Rect(0, 0, w, h), m_Pivot = new Vector2(pv.x * w, pv.y * h),
                            m_Border = new Vector4(Serializer.F(b?["x"]), Serializer.F(b?["y"]), Serializer.F(b?["z"]), Serializer.F(b?["w"])),
                            m_PPU = ti?["spritePixelsToUnits"] is YScalar s ? s.F : 100,
                        };
                        Add(21300000, sp);
                        la.Main = tex;
                    }
                    else if (spriteMode == 2 && ti?["spriteSheet"] is YMap sheet && sheet["sprites"] is YSeq sprites)
                    {
                        // Multiple: one Sprite per spriteSheet rect (fileID = internalID, or the internalIDToNameTable / legacy 21300000 + 2·i ids)
                        var byName = new Dictionary<string, long>();
                        if (ti["internalIDToNameTable"] is YSeq tbl)
                            foreach (var e in tbl.Items)
                                if (e is YMap em && em["first"] is YMap f && f.Entries.Count > 0 && f.Entries[0].Value is YScalar idv) byName[Serializer.Str(em["second"])] = idv.L;
                        if (ti["fileIDToRecycleName"] is YMap rec)
                            foreach (var e in rec.Entries) if (long.TryParse(e.Key, out var fidr)) byName[(e.Value as YScalar)?.Value ?? ""] = fidr;
                        float ppu = ti["spritePixelsToUnits"] is YScalar ps ? ps.F : 100;
                        int i = 0;
                        foreach (var it in sprites.Items)
                        {
                            if (it is not YMap sm) { i++; continue; }
                            var name = Serializer.Str(sm["name"]);
                            var r = sm["rect"] as YMap;
                            var rect = new Rect(Serializer.F(r?["x"]), Serializer.F(r?["y"]), Serializer.F(r?["width"]), Serializer.F(r?["height"]));
                            int align = Serializer.Int(sm["alignment"], 0);
                            var pn = align switch
                            {
                                1 => new Vector2(0, 1), 2 => new Vector2(0.5f, 1), 3 => new Vector2(1, 1), 4 => new Vector2(0, 0.5f), 5 => new Vector2(1, 0.5f),
                                6 => new Vector2(0, 0), 7 => new Vector2(0.5f, 0), 8 => new Vector2(1, 0), 9 => Serializer.ReadV2(sm["pivot"]), _ => new Vector2(0.5f, 0.5f),
                            };
                            var bb = sm["border"] as YMap;
                            long id = sm["internalID"] is YScalar iid && iid.L != 0 ? iid.L : byName.TryGetValue(name, out var nid) ? nid : 21300000 + 2 * i;
                            var spr = new Sprite
                            {
                                m_Texture = tex, m_Rect = rect, m_Pivot = new Vector2(pn.x * rect.width, pn.y * rect.height),
                                m_Border = new Vector4(Serializer.F(bb?["x"]), Serializer.F(bb?["y"]), Serializer.F(bb?["z"]), Serializer.F(bb?["w"])), m_PPU = ppu,
                            };
                            spr.m_Name = name;
                            Add(id, spr);
                            i++;
                        }
                        la.Main = tex;
                    }
                    break;
                }
                case ".ttf": case ".otf":
                {
                    // TrueTypeFontImporter: dynamic font, default size 16 (the legacy Text renders it with LegacyFont.cs)
                    var font = new Font(Path.GetFileNameWithoutExtension(la.Path));
                    if (meta?["TrueTypeFontImporter"] is YMap fi) font.m_FontSize = Math.Max(1, Serializer.Int(fi["fontSize"], 16));
                    Add(12800000, font, true);
                    break;
                }
                case ".wav": case ".mp3": case ".ogg": case ".aif": case ".aiff":
                {
                    var clip = new AudioClip();
                    var file = AssetIndex.Abs(la.Path);
                    clip.m_SourceFile = file;
                    if (ext == ".wav") ReadWav(file, clip);
                    else if ((ext == ".ogg" && MiniUnity.Audio.AudioDecode.OggInfo(file, out int och, out int orate, out long ofr)) || (ext == ".mp3" && MiniUnity.Audio.AudioDecode.Mp3Info(file, out och, out orate, out ofr)))
                    { clip.m_Channels = och; clip.m_Frequency = orate; clip.m_Samples = (int)ofr; clip.m_Length = ofr / (float)orate; }
                    Add(8300000, clip, true);
                    break;
                }
                case ".shader":
                    Add(4800000, new Shader(), true);
                    break;
                case ".txt": case ".json": case ".md": case ".bytes": case ".csv": case ".xml": case ".html":
                {
                    var ta = new TextAsset(File.ReadAllText(AssetIndex.Abs(la.Path)));
                    Add(4900000, ta, true);
                    break;
                }
                case ".cs":
                    break;
                default:
                    // models and other assets only Unity imports: its import result in Library/Artifacts, when there is one
                    if (ImportArtifacts.DocsFor(la.Path) is { Count: > 0 } docs) { AssetLoader.LoadDocsInto(la, docs); break; }
                    // no import result: read the model file itself
                    if (ext == ".fbx" && FbxImporter.Import(la, meta, (fid, o, main) => Add(fid, o, main))) break;
                    if (ext == ".inputactions" && ImportInputActions(la, (fid, o, main) => Add(fid, o, main))) break;
                    if (AssetLoader.Verbose) Console.Error.WriteLine("[import] unsupported " + la.Path);
                    break;
            }
        }

        /// <summary>File ID Unity gives an object a ScriptedImporter adds with AddObjectToAsset(identifier, obj): xxHash64 of
        /// "Type:&lt;class&gt;-&gt;&lt;identifier&gt;&lt;n&gt;" (n counts earlier objects with that class and identifier), class
        /// "MonoBehaviour" for ScriptableObjects — the same scheme as model sub-assets.</summary>
        public static long ScriptedImporterId(string className, string identifier, int n = 0)
            => unchecked((long)FbxImporter.XxHash64(System.Text.Encoding.UTF8.GetBytes($"Type:{className}->{identifier}{n}")));

        /// <summary>Input System .inputactions (InputActionImporter): the InputActionAsset ("&lt;root&gt;") and an InputActionReference
        /// per action (identified by the action's id), built by the package's own InputActionAsset.FromJson.</summary>
        static bool ImportInputActions(LoadedAsset la, Action<long, UnityEngine.Object, bool> add)
        {
            var assetType = TypeCache.FindType("UnityEngine.InputSystem.InputActionAsset");
            var refType = TypeCache.FindType("UnityEngine.InputSystem.InputActionReference");
            if (assetType == null || refType == null) return false;
            try
            {
                var asset = (ScriptableObject)assetType.GetMethod("FromJson", new[] { typeof(string) }).Invoke(null, new object[] { File.ReadAllText(AssetIndex.Abs(la.Path)) });
                asset.name = Path.GetFileNameWithoutExtension(la.Path);
                add(ScriptedImporterId("MonoBehaviour", "<root>"), asset, true);
                var create = refType.GetMethod("Create", new[] { TypeCache.FindType("UnityEngine.InputSystem.InputAction") });
                var displayName = refType.GetMethod("GetDisplayName", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public, null, new[] { TypeCache.FindType("UnityEngine.InputSystem.InputAction") }, null);
                foreach (var map in (System.Collections.IEnumerable)assetType.GetProperty("actionMaps").GetValue(asset))
                    foreach (var action in (System.Collections.IEnumerable)map.GetType().GetProperty("actions").GetValue(map))
                    {
                        var r = (ScriptableObject)create.Invoke(null, new[] { action });
                        r.name = displayName?.Invoke(null, new[] { action }) as string ?? r.name;
                        add(ScriptedImporterId("MonoBehaviour", action.GetType().GetProperty("id").GetValue(action).ToString()), r, false);
                    }
                return true;
            }
            catch (Exception e) { Debug.LogWarning($"[import] {la.Path}: {(e.InnerException ?? e).Message}"); return false; }
        }

        public static (int, int) ImageSize(string path)
        {
            try
            {
                using var f = File.OpenRead(path);
                var hdr = new byte[24];
                if (f.Read(hdr, 0, 24) == 24 && hdr[0] == 0x89 && hdr[1] == 'P')
                    return ((hdr[16] << 24) | (hdr[17] << 16) | (hdr[18] << 8) | hdr[19], (hdr[20] << 24) | (hdr[21] << 16) | (hdr[22] << 8) | hdr[23]);
                f.Close();
                return ImageDecoders.Size(File.ReadAllBytes(path), Path.GetExtension(path).ToLowerInvariant());
            }
            catch { }
            return (0, 0);
        }

        public static void ReadWav(string path, AudioClip clip)
        {
            try
            {
                using var f = File.OpenRead(path);
                using var r = new BinaryReader(f);
                r.ReadBytes(12);
                int channels = 1, rate = 44100, bits = 16; long dataLen = 0;
                while (f.Position < f.Length - 8)
                {
                    var id = new string(r.ReadChars(4)); int len = r.ReadInt32();
                    if (id == "fmt ")
                    {
                        r.ReadInt16(); channels = r.ReadInt16(); rate = r.ReadInt32(); r.ReadInt32(); r.ReadInt16(); bits = r.ReadInt16();
                        if (len > 16) r.ReadBytes(len - 16);
                    }
                    else if (id == "data") { dataLen = len; break; }
                    else r.ReadBytes(len + (len & 1));
                }
                int frames = (int)(dataLen / Math.Max(1, channels * bits / 8));
                clip.m_Channels = channels; clip.m_Frequency = rate; clip.m_Samples = frames;
                clip.m_Length = frames / (float)rate;
            }
            catch (Exception e) { Debug.LogWarning("[import] wav " + path + ": " + e.Message); }
        }
    }
}
