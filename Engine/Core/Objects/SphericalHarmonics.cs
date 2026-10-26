using System;

namespace UnityEngine.Rendering
{
    /// <summary>Third-order spherical harmonics, as Unity stores them: 9 coefficients per colour channel, laid out
    /// [rgb * 9 + coefficient]. Basis and normalisation follow Unity's (and Sloan's "Stupid SH Tricks") real SH.</summary>
    public partial struct SphericalHarmonicsL2
    {
        float shr0, shr1, shr2, shr3, shr4, shr5, shr6, shr7, shr8;
        float shg0, shg1, shg2, shg3, shg4, shg5, shg6, shg7, shg8;
        float shb0, shb1, shb2, shb3, shb4, shb5, shb6, shb7, shb8;

        const float Inv2SqrtPI = 0.28209479177387814347f;
        const float Sqrt3Div2SqrtPI = 0.48860251190291992159f;
        const float Sqrt15Div2SqrtPI = 1.0925484305920790705f;
        const float Sqrt5Div4SqrtPI = 0.31539156525252000603f;
        const float Sqrt15Div4SqrtPI = 0.54627421529603953527f;

        public unsafe float this[int rgb, int coefficient]
        {
            get
            {
                Check(rgb, coefficient);
                fixed (float* p = &shr0) return p[rgb * 9 + coefficient];
            }
            set
            {
                Check(rgb, coefficient);
                fixed (float* p = &shr0) p[rgb * 9 + coefficient] = value;
            }
        }

        static void Check(int rgb, int coefficient)
        {
            if ((uint)rgb > 2 || (uint)coefficient > 8)
                throw new IndexOutOfRangeException("Invalid index!");
        }

        static void Basis(Vector3 d, Span<float> b)
        {
            b[0] = Inv2SqrtPI;
            b[1] = -Sqrt3Div2SqrtPI * d.y;
            b[2] = Sqrt3Div2SqrtPI * d.z;
            b[3] = -Sqrt3Div2SqrtPI * d.x;
            b[4] = Sqrt15Div2SqrtPI * d.x * d.y;
            b[5] = -Sqrt15Div2SqrtPI * d.y * d.z;
            b[6] = Sqrt5Div4SqrtPI * (3f * d.z * d.z - 1f);
            b[7] = -Sqrt15Div2SqrtPI * d.x * d.z;
            b[8] = Sqrt15Div4SqrtPI * (d.x * d.x - d.y * d.y);
        }

        public void Clear() => this = default;

        public void AddAmbientLight(Color color)
        {
            shr0 += color.r;
            shg0 += color.g;
            shb0 += color.b;
        }

        public void AddAmbientLight(in Color color) => AddAmbientLight((Color)color);

        /// <summary>Like Unity: the light is projected onto the basis with twice its intensity.</summary>
        public void AddDirectionalLight(Vector3 direction, Color color, float intensity)
        {
            Span<float> b = stackalloc float[9];
            Basis(direction.normalized, b);
            Color c = color * (2f * intensity);
            for (int i = 0; i < 9; i++)
            {
                this[0, i] += b[i] * c.r;
                this[1, i] += b[i] * c.g;
                this[2, i] += b[i] * c.b;
            }
        }

        public void AddDirectionalLight(in Vector3 direction, in Color color, float intensity) => AddDirectionalLight((Vector3)direction, (Color)color, intensity);

        public void Evaluate(Vector3[] directions, Color[] results)
        {
            if (directions == null) throw new ArgumentNullException(nameof(directions));
            if (results == null) throw new ArgumentNullException(nameof(results));
            int n = Math.Min(directions.Length, results.Length);
            Span<float> b = stackalloc float[9];
            for (int k = 0; k < n; k++)
            {
                Basis(directions[k], b);
                float r = 0, g = 0, bl = 0;
                for (int i = 0; i < 9; i++)
                {
                    r += this[0, i] * b[i];
                    g += this[1, i] * b[i];
                    bl += this[2, i] * b[i];
                }
                results[k] = new Color(r, g, bl, 1f);
            }
        }

        public override int GetHashCode()
        {
            var h = new HashCode();
            for (int c = 0; c < 3; c++)
                for (int i = 0; i < 9; i++)
                    h.Add(this[c, i]);
            return h.ToHashCode();
        }

        public override bool Equals(object other) => other is SphericalHarmonicsL2 sh && Equals(sh);

        public bool Equals(SphericalHarmonicsL2 other)
        {
            for (int c = 0; c < 3; c++)
                for (int i = 0; i < 9; i++)
                    if (this[c, i] != other[c, i]) return false;
            return true;
        }

        public bool Equals(in SphericalHarmonicsL2 other) => Equals((SphericalHarmonicsL2)other);

        public static SphericalHarmonicsL2 operator *(SphericalHarmonicsL2 lhs, float rhs)
        {
            var r = default(SphericalHarmonicsL2);
            for (int c = 0; c < 3; c++)
                for (int i = 0; i < 9; i++)
                    r[c, i] = lhs[c, i] * rhs;
            return r;
        }

        public static SphericalHarmonicsL2 operator *(float lhs, SphericalHarmonicsL2 rhs) => rhs * lhs;

        public static SphericalHarmonicsL2 operator +(SphericalHarmonicsL2 lhs, SphericalHarmonicsL2 rhs)
        {
            var r = default(SphericalHarmonicsL2);
            for (int c = 0; c < 3; c++)
                for (int i = 0; i < 9; i++)
                    r[c, i] = lhs[c, i] + rhs[c, i];
            return r;
        }

        public static bool operator ==(SphericalHarmonicsL2 lhs, SphericalHarmonicsL2 rhs) => lhs.Equals(rhs);
        public static bool operator !=(SphericalHarmonicsL2 lhs, SphericalHarmonicsL2 rhs) => !lhs.Equals(rhs);
    }
}
