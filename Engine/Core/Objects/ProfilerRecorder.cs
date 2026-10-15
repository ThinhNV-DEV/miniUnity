// UnityEngine.Profiling.Recorder without a profiler: it measures the interval between `enabled = true` and `enabled = false`.
// "GC.Alloc" reports the managed bytes allocated in that interval as sample blocks: 0 when nothing was allocated, otherwise at
// least 1 (.NET counts bytes, not allocation calls, so the block count is a lower bound). This is what the Unity Test
// Framework's AllocatingGCMemoryConstraint (Is.Not.AllocatingGCMemory()) and Profiler-based allocation checks rely on.
// Other samplers report elapsed time and one block per interval.
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace UnityEngine.Profiling
{
    public sealed partial class Recorder
    {
        static readonly Dictionary<string, Recorder> byName = new(StringComparer.Ordinal);
        string m_Name;
        bool m_Enabled, m_AllThreads = true, m_MeasuringAllThreads;
        long m_StartBytes, m_StartTicks, m_Bytes, m_Ns;
        int m_Blocks;

        /// <summary>The recorder of a sampler; created enabled, like Unity's.</summary>
        public static Recorder Get(string samplerName)
        {
            if (samplerName == null) throw new ArgumentNullException(nameof(samplerName));
            lock (byName)
            {
                if (byName.TryGetValue(samplerName, out var r)) return r;
                r = new Recorder { m_Name = samplerName };
                byName[samplerName] = r;
                r.enabled = true;
                return r;
            }
        }

        bool IsGcAlloc => m_Name == "GC.Alloc";
        static long AllocatedBytes(bool allThreads) => allThreads ? GC.GetTotalAllocatedBytes(true) : GC.GetAllocatedBytesForCurrentThread();

        public void FilterToCurrentThread() => m_AllThreads = false;
        public void CollectFromAllThreads() => m_AllThreads = true;
        public bool isValid => m_Name != null;

        public bool enabled
        {
            get => m_Enabled;
            set
            {
                if (value == m_Enabled) return;
                if (value)
                {
                    m_MeasuringAllThreads = m_AllThreads;
                    m_StartTicks = Stopwatch.GetTimestamp();
                    m_StartBytes = IsGcAlloc ? AllocatedBytes(m_MeasuringAllThreads) : 0;
                    m_Enabled = true;
                }
                else
                {
                    // read the counters before anything else can allocate
                    long bytes = IsGcAlloc ? AllocatedBytes(m_MeasuringAllThreads) - m_StartBytes : 0;
                    long ticks = Stopwatch.GetTimestamp() - m_StartTicks;
                    m_Enabled = false;
                    m_Bytes = Math.Max(0, bytes);
                    m_Ns = (long)(ticks * (1e9 / Stopwatch.Frequency));
                    m_Blocks = IsGcAlloc ? (m_Bytes > 0 ? 1 : 0) : 1;
                }
            }
        }

        public long elapsedNanoseconds => m_Ns;
        public long gpuElapsedNanoseconds => 0;
        public int sampleBlockCount => m_Blocks;
        public int gpuSampleBlockCount => 0;
    }
}
