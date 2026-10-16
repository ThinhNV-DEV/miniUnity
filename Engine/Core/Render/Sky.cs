// Skybox materials evaluated in C#, for the camera clear (CameraClearFlags.Skybox), the ambient probe of AmbientMode.Skybox and
// the default reflection (Standard's specular environment when there is no reflection probe).
//   Skybox/Procedural: port of Unity's built-in shader (Skybox-Procedural.shader: two-sample atmospheric scattering, ground
//     below the horizon, high-quality / simple / no sun disk) with the main directional light as the sun.
//   Skybox/6 Sided: _FrontTex (+Z) _BackTex (-Z) _LeftTex (+X) _RightTex (-X) _UpTex _DownTex × _Tint × 2 (colour space
//     double) × _Exposure, _Rotation about Y.
//   Skybox/Panoramic: latitude-longitude _MainTex, same tint / exposure / rotation.
// Radiance without the sun disk is tabulated once per material state (256×128 directions, bilinear); the disk is added per
// pixel. Reflections use six GGX-prefiltered levels (Unity's UNITY_SPECCUBE_LOD_STEPS) built from that table.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace MiniUnity
{
    public sealed class Sky
    {
        enum Kind { Procedural, SixSided, Panoramic }
        readonly Kind kind;

        // procedural
        float sunSize = .04f, convergence = 5, thickness = 1, exposure = 1.3f; int sunDisk = 2;
        Vector3 skyTint = new(.5f, .5f, .5f), groundColor = new(.369f, .349f, .341f);   // linear
        Vector3 sunDir; Vector3 sunCol;   // _WorldSpaceLightPos0, _LightColor0 (linear)
        // textured
        readonly TexSampler[] faces = new TexSampler[6]; TexSampler pano; Vector3 tint = new(.5f, .5f, .5f); float rotation;

        const int TW = 256, TH = 128;
        Vector3[] table;            // radiance without sun disk, lat-long
        Vector3[][] levels;         // prefiltered reflection levels 1..6 (64×32)
        const int LW = 64, LH = 32;

        static Sky s_Last; static string s_LastKey;

        /// <summary>The sky of a skybox material lit by the main light (null when its shader is not one of the above).</summary>
        public static Sky For(Material m, Light sun)
        {
            if (m == null) return null;
            var name = UIRenderer.ShaderName(m.shader) ?? "";
            Kind k;
            if (name == "Skybox/Procedural") k = Kind.Procedural;
            else if (name == "Skybox/6 Sided") k = Kind.SixSided;
            else if (name == "Skybox/Panoramic") k = Kind.Panoramic;
            else return null;
            Vector3 L = Vector3.zero, C = Vector3.zero;
            if (sun != null)
            {
                L = -(sun.transform.rotation * Vector3.forward).normalized;
                var c = sun.color * sun.intensity;
                C = new Vector3(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b));
            }
            var key = Key(m, k, L, C);
            if (key == s_LastKey && s_Last != null) return s_Last;
            var s = new Sky(m, k, L, C);
            s_Last = s; s_LastKey = key;
            return s;
        }

        static string Key(Material m, Kind k, Vector3 L, Vector3 C)
        {
            var sb = new StringBuilder();
            sb.Append(m.GetInstanceID()).Append('|').Append(k).Append('|');
            void F(float v) => sb.Append(v.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            foreach (var p in new[] { "_SunSize", "_SunSizeConvergence", "_AtmosphereThickness", "_Exposure", "_SunDisk", "_Rotation" }) F(m.HasProperty(p) ? m.GetFloat(p) : -1);
            foreach (var p in new[] { "_SkyTint", "_GroundColor", "_Tint" }) { var c = m.HasProperty(p) ? m.GetColor(p) : Color.clear; F(c.r); F(c.g); F(c.b); }
            foreach (var p in new[] { "_FrontTex", "_BackTex", "_LeftTex", "_RightTex", "_UpTex", "_DownTex", "_MainTex" }) sb.Append(m.HasProperty(p) && m.GetTexture(p) is Texture t ? t.GetInstanceID() : 0).Append(',');
            F(L.x); F(L.y); F(L.z); F(C.x); F(C.y); F(C.z);
            return sb.ToString();
        }

        Sky(Material m, Kind k, Vector3 L, Vector3 C)
        {
            kind = k; sunDir = L; sunCol = C;
            float Fl(string n, float d) => m.HasProperty(n) ? m.GetFloat(n) : d;
            Vector3 Col(string n, Vector3 d) { if (!m.HasProperty(n)) return d; var c = m.GetColor(n); return new Vector3(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b)); }
            exposure = Fl("_Exposure", k == Kind.Procedural ? 1.3f : 1);
            if (k == Kind.Procedural)
            {
                sunSize = Fl("_SunSize", .04f); convergence = Fl("_SunSizeConvergence", 5); thickness = Fl("_AtmosphereThickness", 1);
                sunDisk = (int)Fl("_SunDisk", 2);   // shader default High Quality; Default-Skybox uses Simple (1)
                // material colours reach the shader linearized (linear colour space); the defaults are given linear
                skyTint = Col("_SkyTint", new Vector3(.214f, .214f, .214f)); groundColor = Col("_GroundColor", new Vector3(.112f, .1f, .095f));
            }
            else
            {
                tint = Col("_Tint", new Vector3(.214f, .214f, .214f)); rotation = Fl("_Rotation", 0);
                TexSampler S(string n) => m.HasProperty(n) && m.GetTexture(n) is Texture t ? TextureData.Sampler(t) : null;
                if (k == Kind.SixSided) { faces[0] = S("_FrontTex"); faces[1] = S("_BackTex"); faces[2] = S("_LeftTex"); faces[3] = S("_RightTex"); faces[4] = S("_UpTex"); faces[5] = S("_DownTex"); }
                else pano = S("_MainTex");
            }
            BuildTable();
        }

        // ---------------------------------------------------------------- radiance
        /// <summary>Linear radiance seen in world direction d (unit), with or without the sun disk.</summary>
        public Color Radiance(Vector3 d, bool withSunDisk)
        {
            var c = Lookup(table, TW, TH, d);
            if (withSunDisk && kind == Kind.Procedural && sunDisk != 0 && d.y >= 0) c += SunDisk(d);
            return new Color(c.x, c.y, c.z);
        }

        static Vector3 Lookup(Vector3[] t, int w, int h, Vector3 d)
        {
            float u = (MathF.Atan2(d.x, d.z) / (2 * MathF.PI) + .5f) * w - .5f;
            float v = (MathF.Acos(Math.Clamp(d.y, -1, 1)) / MathF.PI) * h - .5f;
            int x0 = (int)MathF.Floor(u), y0 = (int)MathF.Floor(v);
            float fx = u - x0, fy = v - y0;
            int xa = ((x0 % w) + w) % w, xb = (xa + 1) % w, ya = Math.Clamp(y0, 0, h - 1), yb = Math.Clamp(y0 + 1, 0, h - 1);
            return t[ya * w + xa] * ((1 - fx) * (1 - fy)) + t[ya * w + xb] * (fx * (1 - fy)) + t[yb * w + xa] * ((1 - fx) * fy) + t[yb * w + xb] * (fx * fy);
        }

        static Vector3 Dir(int x, int y, int w, int h)
        {
            float ph = ((x + .5f) / w - .5f) * 2 * MathF.PI, th = (y + .5f) / h * MathF.PI;
            float st = MathF.Sin(th);
            return new Vector3(st * MathF.Sin(ph), MathF.Cos(th), st * MathF.Cos(ph));
        }

        void BuildTable()
        {
            var t = new Vector3[TW * TH];
            Parallel.For(0, TH, y => { for (int x = 0; x < TW; x++) t[y * TW + x] = Evaluate(Dir(x, y, TW, TH)); });
            table = t;
        }

        Vector3 Evaluate(Vector3 d) => kind switch
        {
            Kind.Procedural => Procedural(d),
            Kind.SixSided => SixSided(d),
            _ => Panoramic(d),
        };

        // ---------------------------------------------------------------- Skybox/Procedural
        const float OuterRadius = 1.025f, OuterRadius2 = OuterRadius * OuterRadius, InnerRadius = 1, InnerRadius2 = 1, CameraHeight = .0001f;
        const float Mie = .001f, SunBrightness = 20, MaxScatter = 50, HDSundiskIntensityFactor = 15, SimpleSundiskIntensityFactor = 27;
        const float SunScale = 400 * SunBrightness, KmESun = Mie * SunBrightness, Km4PI = Mie * 4 * MathF.PI;
        const float Scale = 1 / (OuterRadius - 1), ScaleDepth = .25f, ScaleOverScaleDepth = (1 / (OuterRadius - 1)) / .25f, Samples = 2;
        const float MieG = -.99f, MieG2 = .9801f, SkyGroundThreshold = .02f;

        static float ScaleF(float inCos) { float x = 1 - inCos; return .25f * MathF.Exp(-.00287f + x * (.459f + x * (3.83f + x * (-6.8f + x * 5.25f)))); }
        static Vector3 Exp(Vector3 v) => new(MathF.Exp(v.x), MathF.Exp(v.y), MathF.Exp(v.z));
        static Vector3 Mul(Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
        static Vector3 Sat(Vector3 v) => new(Math.Clamp(v.x, 0, 1), Math.Clamp(v.y, 0, 1), Math.Clamp(v.z, 0, 1));

        Vector3 InvWavelength()
        {
            var tintGamma = new Vector3(MathF.Pow(skyTint.x, 1 / 2.2f), MathF.Pow(skyTint.y, 1 / 2.2f), MathF.Pow(skyTint.z, 1 / 2.2f));
            var lo = new Vector3(.65f, .57f, .475f) - new Vector3(.15f, .15f, .15f); var hi = new Vector3(.65f, .57f, .475f) + new Vector3(.15f, .15f, .15f);
            var w = lo + Mul(hi - lo, Vector3.one - tintGamma);
            return new Vector3(1 / MathF.Pow(w.x, 4), 1 / MathF.Pow(w.y, 4), 1 / MathF.Pow(w.z, 4));
        }

        /// <summary>Scattering of the vertex stage: sky colour, ground colour, sun colour (cIn / cOut), then the fragment's blend.</summary>
        void Scatter(Vector3 eyeRay, out Vector3 skyColor, out Vector3 groundCol, out Vector3 sunColor)
        {
            float rayleigh = Mathf.Lerp(0, .0025f, MathF.Pow(thickness, 2.5f));
            float KrESun = rayleigh * SunBrightness, Kr4PI = rayleigh * 4 * MathF.PI;
            var inv = InvWavelength();
            var cameraPos = new Vector3(0, InnerRadius + CameraHeight, 0);
            Vector3 cIn, cOut;
            var ext = inv * Kr4PI + new Vector3(Km4PI, Km4PI, Km4PI);
            if (eyeRay.y >= 0)
            {
                float far = MathF.Sqrt(OuterRadius2 + InnerRadius2 * eyeRay.y * eyeRay.y - InnerRadius2) - InnerRadius * eyeRay.y;
                float height = InnerRadius + CameraHeight;
                float depth = MathF.Exp(ScaleOverScaleDepth * -CameraHeight);
                float startAngle = Vector3.Dot(eyeRay, cameraPos) / height;
                float startOffset = depth * ScaleF(startAngle);
                float sampleLength = far / Samples, scaledLength = sampleLength * Scale;
                var sampleRay = eyeRay * sampleLength;
                var samplePoint = cameraPos + sampleRay * .5f;
                var front = Vector3.zero;
                for (int i = 0; i < (int)Samples; i++)
                {
                    float h = samplePoint.magnitude;
                    float dpt = MathF.Exp(ScaleOverScaleDepth * (InnerRadius - h));
                    float lightAngle = Vector3.Dot(sunDir, samplePoint) / h;
                    float cameraAngle = Vector3.Dot(eyeRay, samplePoint) / h;
                    float scatter = startOffset + dpt * (ScaleF(lightAngle) - ScaleF(cameraAngle));
                    var att = Exp(-Math.Clamp(scatter, 0, MaxScatter) * ext);
                    front += att * (dpt * scaledLength);
                    samplePoint += sampleRay;
                }
                cIn = front * KrESun; cIn = Mul(cIn, inv);
                cOut = front * KmESun;
            }
            else
            {
                float far = -CameraHeight / MathF.Min(-.001f, eyeRay.y);
                var pos = cameraPos + far * eyeRay;
                float depth = MathF.Exp(-CameraHeight * (1 / ScaleDepth));
                float cameraAngle = Vector3.Dot(-eyeRay, pos), lightAngle = Vector3.Dot(sunDir, pos);
                float cameraScale = ScaleF(cameraAngle), lightScale = ScaleF(lightAngle);
                float cameraOffset = depth * cameraScale, temp = lightScale + cameraScale;
                float sampleLength = far, scaledLength = sampleLength * Scale;
                var samplePoint = cameraPos + eyeRay * sampleLength * .5f;
                float h = samplePoint.magnitude;
                float dpt = MathF.Exp(ScaleOverScaleDepth * (InnerRadius - h));
                float scatter = dpt * temp - cameraOffset;
                var att = Exp(-Math.Clamp(scatter, 0, MaxScatter) * ext);
                var front = att * (dpt * scaledLength);
                cIn = Mul(front, inv * KrESun + new Vector3(KmESun, KmESun, KmESun));
                cOut = Sat(att);
            }
            groundCol = exposure * (cIn + Mul(groundColor, cOut));
            float eyeCos = Vector3.Dot(sunDir, eyeRay);   // getRayleighPhase(light, -(-eyeRay))
            skyColor = exposure * (cIn * (.75f + .75f * eyeCos * eyeCos));
            float lightColorIntensity = Math.Clamp(sunCol.magnitude, .25f, 1);
            sunColor = sunDisk == 2 ? HDSundiskIntensityFactor * Mul(Sat(cOut), sunCol) / lightColorIntensity
                                    : SimpleSundiskIntensityFactor * Mul(Sat(cOut * SunScale), sunCol) / lightColorIntensity;
        }

        Vector3 Procedural(Vector3 eyeRay)
        {
            Scatter(eyeRay, out var sky, out var ground, out _);
            float y = -eyeRay.y / SkyGroundThreshold;   // the fragment's ray is -eyeRay
            return Vector3.Lerp(sky, ground, Math.Clamp(y, 0, 1));
        }

        Vector3 SunDisk(Vector3 eyeRay)
        {
            if (sunCol == Vector3.zero) return Vector3.zero;
            float y = -eyeRay.y / SkyGroundThreshold;
            if (y >= 0) return Vector3.zero;
            Scatter(eyeRay, out _, out _, out var sunColor);
            float att;
            if (sunDisk == 1)
            {
                float dist = (sunDir - eyeRay).magnitude;
                float spot = 1 - Mathf.SmoothStep(0, 1, Math.Clamp(dist / Mathf.Max(sunSize, 1e-6f), 0, 1));
                att = spot * spot;
            }
            else
            {
                float focused = MathF.Pow(Math.Clamp(Vector3.Dot(sunDir, eyeRay), 0, 1), convergence);
                float eyeCos = -focused, eyeCos2 = focused * focused;
                float temp = 1 + MieG2 - 2 * MieG * eyeCos;
                temp = MathF.Pow(temp, MathF.Pow(sunSize, .65f) * 10);
                temp = MathF.Max(temp, 1e-4f);
                att = 1.5f * ((1 - MieG2) / (2 + MieG2)) * (1 + eyeCos2) / temp;
            }
            return sunColor * att;
        }

        // ---------------------------------------------------------------- textured skyboxes
        const float ColorSpaceDouble = 4.59479380f;   // unity_ColorSpaceDouble in linear colour space

        Vector3 Rotate(Vector3 d)
        {
            if (rotation == 0) return d;
            float a = rotation * Mathf.Deg2Rad, s = MathF.Sin(a), c = MathF.Cos(a);
            return new Vector3(c * d.x - s * d.z, d.y, s * d.x + c * d.z);   // RotateAroundYInDegrees
        }

        Vector3 Tinted(TexSampler t, float u, float v)
        {
            if (t == null) return Vector3.zero;
            t.Sample(u, v, out float r, out float g, out float b, out _);
            return Mul(new Vector3(r, g, b), tint) * (ColorSpaceDouble * exposure);
        }

        Vector3 SixSided(Vector3 d)
        {
            d = Rotate(d);
            float ax = MathF.Abs(d.x), ay = MathF.Abs(d.y), az = MathF.Abs(d.z);
            // each face as seen from the inside, texture up = world up (up / down faces: top edge towards +Z)
            if (az >= ax && az >= ay)
                return d.z > 0 ? Tinted(faces[0], .5f + .5f * d.x / az, .5f + .5f * d.y / az) : Tinted(faces[1], .5f - .5f * d.x / az, .5f + .5f * d.y / az);
            if (ax >= ay)
                return d.x > 0 ? Tinted(faces[2], .5f - .5f * d.z / ax, .5f + .5f * d.y / ax) : Tinted(faces[3], .5f + .5f * d.z / ax, .5f + .5f * d.y / ax);
            return d.y > 0 ? Tinted(faces[4], .5f + .5f * d.x / ay, .5f + .5f * d.z / ay) : Tinted(faces[5], .5f + .5f * d.x / ay, .5f - .5f * d.z / ay);
        }

        Vector3 Panoramic(Vector3 d)
        {
            d = Rotate(d);
            float u = .5f + MathF.Atan2(d.x, d.z) / (2 * MathF.PI);
            float v = .5f + MathF.Asin(Math.Clamp(d.y, -1, 1)) / MathF.PI;
            return Tinted(pano, u, v);
        }

        // ---------------------------------------------------------------- reflections
        /// <summary>Specular environment for reflection direction r at a perceptual roughness (Standard: mip = r(1.7 - 0.7r)·6).</summary>
        public Color Specular(Vector3 r, float perceptualRoughness)
        {
            if (levels == null) BuildLevels();
            float mip = perceptualRoughness * (1.7f - .7f * perceptualRoughness) * 6;
            int m0 = Math.Clamp((int)MathF.Floor(mip), 0, 6), m1 = Math.Min(6, m0 + 1);
            float f = Math.Clamp(mip - m0, 0, 1);
            Vector3 L(int m) => m == 0 ? Lookup(table, TW, TH, r) + (kind == Kind.Procedural && sunDisk != 0 && r.y >= 0 ? SunDisk(r) : Vector3.zero) : Lookup(levels[m - 1], LW, LH, r);
            var c = f <= 0 ? L(m0) : Vector3.Lerp(L(m0), L(m1), f);
            return new Color(c.x, c.y, c.z);
        }

        void BuildLevels()
        {
            // source: the radiance table at 128×64 with each texel's solid angle
            const int SW = 128, SH = 64;
            var src = new Vector3[SW * SH]; var dir = new Vector3[SW * SH]; var dw = new float[SW * SH];
            for (int y = 0; y < SH; y++)
                for (int x = 0; x < SW; x++)
                {
                    var d = Dir(x, y, SW, SH);
                    dir[y * SW + x] = d; src[y * SW + x] = Lookup(table, TW, TH, d);
                    float th = (y + .5f) / SH * MathF.PI;
                    dw[y * SW + x] = MathF.Sin(th) * (MathF.PI / SH) * (2 * MathF.PI / SW);
                }
            var lv = new Vector3[6][];
            for (int m = 1; m <= 6; m++)
            {
                float pr = (1.7f - MathF.Sqrt(MathF.Max(0, 2.89f - 2.8f * m / 6f))) / 1.4f;
                float a = MathF.Max(pr * pr, 1e-3f), a2 = a * a;
                var o = new Vector3[LW * LH];
                Parallel.For(0, LH, y =>
                {
                    for (int x = 0; x < LW; x++)
                    {
                        var n = Dir(x, y, LW, LH);
                        Vector3 sum = Vector3.zero; float wsum = 0;
                        for (int i = 0; i < src.Length; i++)
                        {
                            float nl = Vector3.Dot(n, dir[i]);
                            if (nl <= 0) continue;
                            // GGX lobe around n (n = v = r), split-sum prefilter weighted by n·l
                            float hn = MathF.Sqrt((1 + nl) * .5f);
                            float dd = hn * hn * (a2 - 1) + 1;
                            float w = a2 / (MathF.PI * dd * dd) * nl * dw[i];
                            sum += src[i] * w; wsum += w;
                        }
                        o[y * LW + x] = wsum > 0 ? sum / wsum : Vector3.zero;
                    }
                });
                lv[m - 1] = o;
            }
            levels = lv;
        }
    }
}
