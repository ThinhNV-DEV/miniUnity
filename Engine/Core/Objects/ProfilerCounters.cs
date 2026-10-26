// A profiler counter's value lives in memory the runtime owns, and ProfilerCounterValue<T> (com.unity.profiling.core, used by
// Addressables' ProfilerRuntime) reads and writes it through the pointer it gets back. Nothing samples it here, but the memory has
// to exist: the no-op stub returned null, so the first `counter.Value = 0` failed Addressables' static initialisation.
using System;
using System.Runtime.InteropServices;
using Unity.Profiling.LowLevel;

namespace Unity.Profiling.LowLevel.Unsafe
{
    public static unsafe partial class ProfilerUnsafeUtility
    {
        public static void* CreateCounterValue(out IntPtr counterPtr, string name, ushort categoryId, MarkerFlags flags, byte dataType, byte dataUnit,
            int dataSize, ProfilerCounterOptions counterOptions)
        {
            counterPtr = IntPtr.Zero;
            return CounterMemory(dataSize);
        }

        public static void* CreateCounterValue(out IntPtr counterPtr, char* name, int nameLen, ushort categoryId, MarkerFlags flags, byte dataType,
            byte dataUnit, int dataSize, ProfilerCounterOptions counterOptions)
        {
            counterPtr = IntPtr.Zero;
            return CounterMemory(dataSize);
        }

        /// <summary>Zeroed memory for one counter, kept for the life of the process (as a counter is).</summary>
        static void* CounterMemory(int size)
        {
            size = Math.Max(size, 16);
            var p = (void*)Marshal.AllocHGlobal(size);
            new Span<byte>(p, size).Clear();
            return p;
        }
    }
}
