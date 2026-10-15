using System;
using System.Text;

namespace UnityEngine
{
    public partial struct Hash128
    {
        public Hash128(ulong a, ulong b) { u64_0 = a; u64_1 = b; }

        /// <summary>Hash variants tried while matching Unity's native ComputeHash128FromScriptString (matched against the dialogue hashes Unity stored in a project's Timeline assets).</summary>
        public static int StringMode = 1; // 1 = UTF-8 (verified against the Unity docs vector), 0 = UTF-16
        public static unsafe Hash128 Compute(string data)
        {
            data ??= "";
            ulong h1 = 0, h2 = 0;
            if (StringMode == 0) fixed (char* p = data) SpookyHash.Hash(p, (ulong)(data.Length * 2), &h1, &h2);
            else { var b = Encoding.UTF8.GetBytes(data); fixed (byte* p = b) SpookyHash.Hash(p, (ulong)b.Length, &h1, &h2); }
            return new Hash128(h1, h2);
        }
        public static int ToStringMode = 0;
        public override string ToString()
        {
            var bytes = new byte[16];
            BitConverter.GetBytes(u64_0).CopyTo(bytes, 0); BitConverter.GetBytes(u64_1).CopyTo(bytes, 8);
            var sb = new StringBuilder(32);
            switch (ToStringMode)
            {
                case 0: foreach (var b in bytes) sb.Append(b.ToString("x2")); break;                       // memory order
                case 1: sb.Append(u64_0.ToString("x16")).Append(u64_1.ToString("x16")); break;        // two u64
                case 3: for (int i = 0; i < 4; i++) sb.Append(BitConverter.ToUInt32(bytes, i * 4).ToString("x8")); break;
                case 4: for (int i = 15; i >= 0; i--) sb.Append(bytes[i].ToString("x2")); break;
                case 5: sb.Append(u64_1.ToString("x16")).Append(u64_0.ToString("x16")); break;
                case 2: foreach (var b in bytes) sb.Append("0123456789abcdef"[b & 15]).Append("0123456789abcdef"[b >> 4]); break; // nibble-swapped
            }
            return sb.ToString();
        }
    }
}

namespace UnityEngine
{
    // value semantics + incremental Append (SpookyHash seeded with the current value, as Unity's Hash128.Append)
    public partial struct Hash128
    {
        public bool isValid => u64_0 != 0 || u64_1 != 0;
        public int CompareTo(Hash128 rhs) => u64_0 != rhs.u64_0 ? u64_0.CompareTo(rhs.u64_0) : u64_1.CompareTo(rhs.u64_1);
        public int CompareTo(object obj) => obj is Hash128 h ? CompareTo(h) : 1;
        public override bool Equals(object obj) => obj is Hash128 h && Equals(h);
        public bool Equals(Hash128 obj) => u64_0 == obj.u64_0 && u64_1 == obj.u64_1;
        public override int GetHashCode() => u64_0.GetHashCode() ^ u64_1.GetHashCode();
        public static bool operator ==(Hash128 hash1, Hash128 hash2) => hash1.Equals(hash2);
        public static bool operator !=(Hash128 hash1, Hash128 hash2) => !hash1.Equals(hash2);
        public static bool operator <(Hash128 x, Hash128 y) => x.CompareTo(y) < 0;
        public static bool operator >(Hash128 x, Hash128 y) => x.CompareTo(y) > 0;
        public static Hash128 Parse(string hashString)
        {
            var bytes = new byte[16];
            for (int i = 0; i < 16 && i * 2 + 1 < (hashString?.Length ?? 0); i++) bytes[i] = Convert.ToByte(hashString.Substring(i * 2, 2), 16);
            return new Hash128(BitConverter.ToUInt64(bytes, 0), BitConverter.ToUInt64(bytes, 8));
        }
        public unsafe void Append(void* data, ulong size) { ulong a = u64_0, b = u64_1; SpookyHash.Hash(data, size, &a, &b); u64_0 = a; u64_1 = b; }
        public unsafe void Append(string data) { var b = Encoding.UTF8.GetBytes(data ?? ""); fixed (byte* p = b) Append(p, (ulong)b.Length); }
        public unsafe void Append(int val) => Append(&val, 4);
        public unsafe void Append(float val) => Append(&val, 4);
        public unsafe void Append<T>(ref T val) where T : unmanaged { fixed (T* p = &val) Append(p, (ulong)sizeof(T)); }
        public unsafe void Append<T>(T[] data) where T : struct => Append(data, 0, data.Length);
        public unsafe void Append<T>(T[] data, int start, int count) where T : struct
        {
            var h = System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { Append((byte*)h.AddrOfPinnedObject() + (long)start * System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), (ulong)((long)count * System.Runtime.CompilerServices.Unsafe.SizeOf<T>())); }
            finally { h.Free(); }
        }
        public void Append<T>(System.Collections.Generic.List<T> data) where T : struct => Append(data.ToArray());
        public void Append<T>(System.Collections.Generic.List<T> data, int start, int count) where T : struct => Append(data.ToArray(), start, count);
        public void Append<T>(Unity.Collections.NativeArray<T> data) where T : struct => Append(data.ToArray());
        public void Append<T>(Unity.Collections.NativeArray<T> data, int start, int count) where T : struct => Append(data.ToArray(), start, count);
    }
}
