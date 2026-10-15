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
                    Add(12800000, new Font(Path.GetFileNameWithoutExtension(la.Path)), true);
                    break;
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
                    if (AssetLoader.Verbose) Console.Error.WriteLine("[import] unsupported " + la.Path);
                    break;
            }
        }

        public static (int, int) ImageSize(string path)
        {
            try
            {
                using var f = File.OpenRead(path);
                var hdr = new byte[24];
                if (f.Read(hdr, 0, 24) == 24 && hdr[0] == 0x89 && hdr[1] == 'P')
                    return ((hdr[16] << 24) | (hdr[17] << 16) | (hdr[18] << 8) | hdr[19], (hdr[20] << 24) | (hdr[21] << 16) | (hdr[22] << 8) | hdr[23]);
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
