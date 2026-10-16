// UnityEngine.Random: Unity's xorshift128 generator (seeding with 1812433253 steps, 23-bit floats inclusive of 1, int ranges by
// modulo, float ranges as min·t + (1 − t)·max) and the shapes built on it. Unity seeds from the clock at startup; MiniUnity seeds
// with MU_RANDOM_SEED (default 0) so runs are reproducible. The exact sequence is an oracle-test item (tests/oracle).
using System;

namespace UnityEngine
{
    public partial class Random
    {
        static uint s_X, s_Y, s_Z, s_W;
        static bool s_Seeded;

        static void Seed(uint seed)
        {
            s_X = seed; s_Y = s_X * 1812433253u + 1; s_Z = s_Y * 1812433253u + 1; s_W = s_Z * 1812433253u + 1;
            s_Seeded = true;
        }

        static void Ensure()
        {
            if (s_Seeded) return;
            int.TryParse(Environment.GetEnvironmentVariable("MU_RANDOM_SEED"), out int seed);
            Seed((uint)seed);
        }

        internal static uint Next()
        {
            Ensure();
            uint t = s_X ^ (s_X << 11);
            s_X = s_Y; s_Y = s_Z; s_Z = s_W;
            return s_W = s_W ^ (s_W >> 19) ^ (t ^ (t >> 8));
        }

        static float NextFloat() => (Next() & 0x007FFFFFu) * (1.0f / 8388607.0f);
        static float NextSigned() => NextFloat() * 2f - 1f;

        public static void InitState(int seed) => Seed((uint)seed);
        public static int seed { get { Ensure(); return (int)s_X; } set => Seed((uint)value); }

        public static State state
        {
            get { Ensure(); return new State { s0 = (int)s_X, s1 = (int)s_Y, s2 = (int)s_Z, s3 = (int)s_W }; }
            set { s_X = (uint)value.s0; s_Y = (uint)value.s1; s_Z = (uint)value.s2; s_W = (uint)value.s3; s_Seeded = true; }
        }

        public static float value => NextFloat();

        public static float Range(float minInclusive, float maxInclusive) { float t = NextFloat(); return minInclusive * t + (1f - t) * maxInclusive; }
        public static int Range(int minInclusive, int maxExclusive)
        {
            if (minInclusive < maxExclusive) return (int)(minInclusive + Next() % (uint)(maxExclusive - minInclusive));
            if (minInclusive > maxExclusive) return (int)(minInclusive - Next() % (uint)(minInclusive - maxExclusive));
            return minInclusive;
        }
        public static float RandomRange(float min, float max) => Range(min, max);
        public static int RandomRange(int min, int max) => Range(min, max);

        static Vector3 UnitVector()
        {
            float z = NextSigned(), a = NextFloat() * 2f * Mathf.PI, r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z);
        }

        public static Vector3 onUnitSphere => UnitVector();
        public static Vector3 insideUnitSphere { get { var v = UnitVector(); return v * Mathf.Pow(NextFloat(), 1f / 3f); } }
        public static Vector2 insideUnitCircle
        {
            get { float a = NextFloat() * 2f * Mathf.PI, r = Mathf.Sqrt(NextFloat()); return new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a)); }
        }

        public static Quaternion rotation
        {
            get { var q = new Quaternion(NextSigned(), NextSigned(), NextSigned(), NextSigned()); return q.normalized; }
        }

        /// <summary>Uniform over SO(3) (Shoemake).</summary>
        public static Quaternion rotationUniform
        {
            get
            {
                float u1 = NextFloat(), u2 = NextFloat() * 2f * Mathf.PI, u3 = NextFloat() * 2f * Mathf.PI;
                float a = Mathf.Sqrt(1f - u1), b = Mathf.Sqrt(u1);
                return new Quaternion(a * Mathf.Sin(u2), a * Mathf.Cos(u2), b * Mathf.Sin(u3), b * Mathf.Cos(u3));
            }
        }

        public static Color ColorHSV() => ColorHSV(0f, 1f, 0f, 1f, 0f, 1f, 1f, 1f);
        public static Color ColorHSV(float hueMin, float hueMax) => ColorHSV(hueMin, hueMax, 0f, 1f, 0f, 1f, 1f, 1f);
        public static Color ColorHSV(float hueMin, float hueMax, float saturationMin, float saturationMax) => ColorHSV(hueMin, hueMax, saturationMin, saturationMax, 0f, 1f, 1f, 1f);
        public static Color ColorHSV(float hueMin, float hueMax, float saturationMin, float saturationMax, float valueMin, float valueMax) => ColorHSV(hueMin, hueMax, saturationMin, saturationMax, valueMin, valueMax, 1f, 1f);
        public static Color ColorHSV(float hueMin, float hueMax, float saturationMin, float saturationMax, float valueMin, float valueMax, float alphaMin, float alphaMax)
        {
            float h = Mathf.Lerp(hueMin, hueMax, value), s = Mathf.Lerp(saturationMin, saturationMax, value), v = Mathf.Lerp(valueMin, valueMax, value);
            var c = Color.HSVToRGB(h, s, v, true);
            c.a = Mathf.Lerp(alphaMin, alphaMax, value);
            return c;
        }


        public partial struct State
        {
            [SerializeField] internal int s0, s1, s2, s3;
        }
    }
}
