// C# Job System under `ua play` / `ua test`: jobs run synchronously on the main thread when scheduled (as with ScheduleMode.Run),
// so JobHandle.Complete has nothing to wait for. Burst is never used: Burst-compiled code runs as the managed C# it was written in.
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;

namespace Unity.Jobs.LowLevel.Unsafe
{
    public static unsafe partial class JobsUtility
    {
        public partial struct JobScheduleParameters
        {
            public JobHandle Dependency;
            public int ScheduleMode;
            public IntPtr ReflectionData;
            public IntPtr JobDataPtr;
            public JobScheduleParameters(void* i_jobData, IntPtr i_reflectionData, JobHandle i_dependency, ScheduleMode i_scheduleMode)
            {
                Dependency = i_dependency; ScheduleMode = (int)i_scheduleMode; ReflectionData = i_reflectionData; JobDataPtr = (IntPtr)i_jobData;
            }
        }

        sealed class Reflection { public Type JobType; public Delegate Execute; }
        static readonly List<Reflection> reflection = new();
        static bool executing, debugger, compiler;
        static int workers;

        static IntPtr Register(Type userJobType, object fn)
        {
            reflection.Add(new Reflection { JobType = userJobType, Execute = fn as Delegate });
            return (IntPtr)reflection.Count;
        }
        public static IntPtr CreateJobReflectionData(Type type, JobType jobType, object managedJobFunction0, object managedJobFunction1 = null, object managedJobFunction2 = null) => Register(type, managedJobFunction0);
        public static IntPtr CreateJobReflectionData(Type type, object managedJobFunction0, object managedJobFunction1 = null, object managedJobFunction2 = null) => Register(type, managedJobFunction0);
        public static IntPtr CreateJobReflectionData(Type wrapperJobType, Type userJobType, JobType jobType, object managedJobFunction0) => Register(wrapperJobType, managedJobFunction0);
        public static IntPtr CreateJobReflectionData(Type wrapperJobType, Type userJobType, object managedJobFunction0) => Register(wrapperJobType, managedJobFunction0);

        /// <summary>Execute(ref T jobData, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex) once over the whole range.</summary>
        static JobHandle Run(ref JobScheduleParameters p, int length)
        {
            int idx = (int)p.ReflectionData - 1;
            if (idx < 0 || idx >= reflection.Count || reflection[idx].Execute == null) throw new InvalidOperationException("job reflection data not initialized");
            var r = reflection[idx];
            var ps = r.Execute.Method.GetParameters();
            var dataType = ps[0].ParameterType.IsByRef ? ps[0].ParameterType.GetElementType()! : ps[0].ParameterType;
            var data = ReadBoxed(dataType, (void*)p.JobDataPtr);
            var ranges = new JobRanges { BatchSize = Math.Max(length, 1), NumJobs = 1, TotalIterationCount = length };
            var args = new object[] { data, IntPtr.Zero, IntPtr.Zero, ranges, 0 };
            bool was = executing; executing = true;
            try { r.Execute.DynamicInvoke(args); }
            catch (TargetInvocationException e) when (e.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
            finally { executing = was; }
            return default;
        }

        static readonly MethodInfo readT = typeof(System.Runtime.CompilerServices.Unsafe).GetMethod(nameof(System.Runtime.CompilerServices.Unsafe.Read), new[] { typeof(void*) })!;
        static object ReadBoxed(Type t, void* ptr) => readT.MakeGenericMethod(t).Invoke(null, new object[] { (IntPtr)ptr })!;

        public static JobHandle Schedule(ref JobScheduleParameters parameters) => Run(ref parameters, 1);
        public static JobHandle ScheduleParallelFor(ref JobScheduleParameters parameters, int arrayLength, int innerloopBatchCount) => Run(ref parameters, arrayLength);
        public static JobHandle ScheduleParallelForDeferArraySize(ref JobScheduleParameters parameters, int innerloopBatchCount, void* listData, void* listDataAtomicSafetyHandle) =>
            throw new NotImplementedException("Unity.Jobs.LowLevel.Unsafe.JobsUtility.ScheduleParallelForDeferArraySize (deferred NativeList jobs)");

        public static void GetJobRange(ref JobRanges ranges, int jobIndex, out int beginIndex, out int endIndex) { beginIndex = 0; endIndex = ranges.TotalIterationCount; }
        public static bool GetWorkStealingRange(ref JobRanges ranges, int jobIndex, out int beginIndex, out int endIndex)
        {
            // one batch covering everything, handed out once per execution
            if (ranges.StartEndIndex != IntPtr.Zero || ranges.TotalIterationCount <= 0) { beginIndex = endIndex = 0; return false; }
            ranges.StartEndIndex = (IntPtr)1;
            beginIndex = 0; endIndex = ranges.TotalIterationCount;
            return true;
        }
        public static void PatchBufferMinMaxRanges(IntPtr bufferRangePatchData, void* jobdata, int startIndex, int rangeSize) { }
        public static void ResetJobWorkerCount() => workers = 0;

        public static bool IsExecutingJob => executing;
        public static bool JobDebuggerEnabled { get => debugger; set => debugger = value; }
        public static bool JobCompilerEnabled { get => compiler; set => compiler = value; }
        public static int JobWorkerMaximumCount => 0;
        public static int JobWorkerCount { get => workers; set => workers = Math.Max(0, value); }
        public static int ThreadIndex => 0;
        public static int ThreadIndexCount => 1;
        internal static bool JobBatchingEnabled => false;
    }
}

namespace Unity.Jobs
{
    public partial struct JobHandle
    {
        public void Complete() { }
        public static void CompleteAll(ref JobHandle job0, ref JobHandle job1) { }
        public static void CompleteAll(ref JobHandle job0, ref JobHandle job1, ref JobHandle job2) { }
        public static void CompleteAll(NativeArray<JobHandle> jobs) { }
        public static void ScheduleBatchedJobs() { }
        public static JobHandle CombineDependencies(JobHandle job0, JobHandle job1) => default;
        public static JobHandle CombineDependencies(JobHandle job0, JobHandle job1, JobHandle job2) => default;
        public static JobHandle CombineDependencies(NativeArray<JobHandle> jobs) => default;
        public static JobHandle CombineDependencies(NativeSlice<JobHandle> jobs) => default;
        internal static unsafe JobHandle CombineDependenciesInternalPtr(void* jobs, int count) => default;
        public static bool CheckFenceIsDependencyOrDidSyncFence(JobHandle jobHandle, JobHandle dependsOn) => true;
        public bool IsCompleted => true;
        public bool Equals(JobHandle other) => jobGroup == other.jobGroup && version == other.version;
        public override bool Equals(object obj) => obj is JobHandle h && Equals(h);
        public static bool operator ==(JobHandle a, JobHandle b) => a.Equals(b);
        public static bool operator !=(JobHandle a, JobHandle b) => !a.Equals(b);
        public override int GetHashCode() => HashCode.Combine(jobGroup, version);
    }
}

namespace Unity.Jobs.LowLevel.Unsafe
{
    public static unsafe partial class JobHandleUnsafeUtility
    {
        public static JobHandle CombineDependencies(JobHandle* jobs, int count) => default;
    }
}

namespace Unity.Burst.LowLevel
{
    /// <summary>Burst's native service (internal to UnityEngine.CoreModule). MiniUnity never Burst-compiles: compile requests resolve to the
    /// managed delegate, shared statics get native memory.</summary>
    internal static unsafe class BurstCompilerService
    {
        public delegate bool ExtractCompilerFlags(Type jobType, out string flags);
        public enum BurstLogType { Info = 0, Warning = 1, Error = 2 }
        static readonly List<Delegate> compiled = new();
        static readonly Dictionary<UnityEngine.Hash128, IntPtr> shared = new();
        static uint mode;

        public static bool IsInitialized => true;
        public static void Initialize(string folderRuntime, ExtractCompilerFlags extractCompilerFlags) { }
        internal static string InitializeInternal(string path, ExtractCompilerFlags extractCompilerFlags) => null;
        public static string GetDisassembly(MethodInfo m, string compilerOptions) => "(MiniUnity: Burst is not available; methods run as managed code)";
        public static int CompileAsyncDelegateMethod(object delegateMethod, string compilerOptions) { compiled.Add((Delegate)delegateMethod); return compiled.Count; }
        public static void* GetAsyncCompiledAsyncDelegateMethod(int userID)
        {
            var d = compiled[userID - 1];
            System.Runtime.InteropServices.GCHandle.Alloc(d);
            return (void*)System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(d);
        }
        public static void* GetOrCreateSharedMemory(ref UnityEngine.Hash128 key, uint size_of, uint alignment)
        {
            lock (shared)
            {
                if (!shared.TryGetValue(key, out var p))
                {
                    p = (IntPtr)System.Runtime.InteropServices.NativeMemory.AlignedAlloc(Math.Max(size_of, 1u), Math.Max(alignment, 1u));
                    System.Runtime.InteropServices.NativeMemory.Clear((void*)p, Math.Max(size_of, 1u));
                    shared[key] = p;
                }
                return (void*)p;
            }
        }
        public static string GetMethodSignature(MethodInfo method) => method.ToString();
        public static void SetCurrentExecutionMode(uint environment) => mode = environment;
        public static uint GetCurrentExecutionMode() => mode;
        public static void Log(void* userData, BurstLogType logType, byte* message, byte* filename, int lineNumber) => RuntimeLog(userData, logType, message, filename, lineNumber);
        public static void RuntimeLog(void* userData, BurstLogType logType, byte* message, byte* filename, int lineNumber)
        {
            var msg = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((IntPtr)message);
            if (logType == BurstLogType.Error) UnityEngine.Debug.LogError(msg); else if (logType == BurstLogType.Warning) UnityEngine.Debug.LogWarning(msg); else UnityEngine.Debug.Log(msg);
        }
        public static bool LoadBurstLibrary(string fullPathToLibBurstGenerated) => false;
    }
}

namespace Unity.Jobs
{
    // Built-in job types: Schedule executes immediately (main thread, whole range), the handle is already complete.
    public static partial class IJobExtensions
    {
        public static void EarlyJobInit<T>() where T : struct, IJob { }
        public static JobHandle Schedule<T>(this T jobData, JobHandle dependsOn = default) where T : struct, IJob { jobData.Execute(); return default; }
        public static void Run<T>(this T jobData) where T : struct, IJob => jobData.Execute();
        public static JobHandle ScheduleByRef<T>(this ref T jobData, JobHandle dependsOn = default) where T : struct, IJob { jobData.Execute(); return default; }
        public static void RunByRef<T>(this ref T jobData) where T : struct, IJob => jobData.Execute();
    }

    public static partial class IJobParallelForExtensions
    {
        static void Each<T>(ref T job, int n) where T : struct, IJobParallelFor { for (int i = 0; i < n; i++) job.Execute(i); }
        public static void EarlyJobInit<T>() where T : struct, IJobParallelFor { }
        public static JobHandle Schedule<T>(this T jobData, int arrayLength, int innerloopBatchCount, JobHandle dependsOn = default) where T : struct, IJobParallelFor { Each(ref jobData, arrayLength); return default; }
        public static void Run<T>(this T jobData, int arrayLength) where T : struct, IJobParallelFor => Each(ref jobData, arrayLength);
        public static JobHandle ScheduleByRef<T>(this ref T jobData, int arrayLength, int innerloopBatchCount, JobHandle dependsOn = default) where T : struct, IJobParallelFor { Each(ref jobData, arrayLength); return default; }
        public static void RunByRef<T>(this ref T jobData, int arrayLength) where T : struct, IJobParallelFor => Each(ref jobData, arrayLength);
    }

    public static partial class IJobForExtensions
    {
        static void Each<T>(ref T job, int n) where T : struct, IJobFor { for (int i = 0; i < n; i++) job.Execute(i); }
        public static void EarlyJobInit<T>() where T : struct, IJobFor { }
        public static JobHandle Schedule<T>(this T jobData, int arrayLength, JobHandle dependency) where T : struct, IJobFor { Each(ref jobData, arrayLength); return default; }
        public static JobHandle ScheduleParallel<T>(this T jobData, int arrayLength, int innerloopBatchCount, JobHandle dependency) where T : struct, IJobFor { Each(ref jobData, arrayLength); return default; }
        public static void Run<T>(this T jobData, int arrayLength) where T : struct, IJobFor => Each(ref jobData, arrayLength);
        public static JobHandle ScheduleByRef<T>(this ref T jobData, int arrayLength, JobHandle dependency) where T : struct, IJobFor { Each(ref jobData, arrayLength); return default; }
        public static JobHandle ScheduleParallelByRef<T>(this ref T jobData, int arrayLength, int innerloopBatchCount, JobHandle dependency) where T : struct, IJobFor { Each(ref jobData, arrayLength); return default; }
        public static void RunByRef<T>(this ref T jobData, int arrayLength) where T : struct, IJobFor => Each(ref jobData, arrayLength);
    }
}
