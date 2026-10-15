// NativeArray<T> on native memory (UnsafeUtility.Malloc) for `ua play` / `ua test`. Jobs run synchronously, so the job-safety system
// (AtomicSafetyHandle, DisposeSentinel) has nothing to enforce: its checks pass; out-of-range indices and use after Dispose still throw.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using SUnsafe = System.Runtime.CompilerServices.Unsafe;

namespace Unity.Collections
{
    [NativeContainer, NativeContainerSupportsMinMaxWriteRestriction, NativeContainerSupportsDeallocateOnJobCompletion, NativeContainerSupportsDeferredConvertListToArray]
    [DebuggerDisplay("Length = {Length}")]
    public unsafe struct NativeArray<T> : IDisposable, IEnumerable<T>, IEquatable<NativeArray<T>> where T : struct
    {
        [NativeDisableUnsafePtrRestriction] internal void* m_Buffer;
        internal int m_Length;
        internal int m_MinIndex;
        internal int m_MaxIndex;
        internal AtomicSafetyHandle m_Safety;
        internal Allocator m_AllocatorLabel;

        public NativeArray(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        {
            Allocate(length, allocator, out this);
            if ((options & NativeArrayOptions.ClearMemory) != 0) UnsafeUtility.MemClear(m_Buffer, (long)Length * UnsafeUtility.SizeOf<T>());
        }
        public NativeArray(T[] array, Allocator allocator)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            Allocate(array.Length, allocator, out this);
            Copy(array, this);
        }
        public NativeArray(NativeArray<T> array, Allocator allocator)
        {
            Allocate(array.Length, allocator, out this);
            Copy(array, 0, this, 0, array.Length);
        }

        static void Allocate(int length, Allocator allocator, out NativeArray<T> array)
        {
            if (allocator <= Allocator.None) throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", nameof(allocator));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must be >= 0");
            IsUnmanagedAndThrow();
            long size = (long)UnsafeUtility.SizeOf<T>() * length;
            array = default;
            array.m_Buffer = UnsafeUtility.Malloc(size, UnsafeUtility.AlignOf<int>(), allocator);
            array.m_Length = length;
            array.m_AllocatorLabel = allocator;
            array.m_MinIndex = 0;
            array.m_MaxIndex = length - 1;
        }

        internal static void InitNestedNativeContainer(AtomicSafetyHandle handle) { }
        internal static void IsUnmanagedAndThrow()
        {
            if (!UnsafeUtility.IsUnmanaged<T>()) throw new InvalidOperationException($"{typeof(T)} used in NativeArray<{typeof(T)}> must be unmanaged (contain no managed types).");
        }

        public int Length => m_Length;
        public bool IsCreated => m_Buffer != null;

        public T this[int index]
        {
            get { Check(index); return UnsafeUtility.ReadArrayElement<T>(m_Buffer, index); }
            [WriteAccessRequired] set { Check(index); UnsafeUtility.WriteArrayElement(m_Buffer, index, value); }
        }

        void Check(int index)
        {
            if (m_Buffer == null) throw new ObjectDisposedException("The NativeArray has been deallocated, it is not allowed to access it");
            if ((uint)index >= (uint)m_Length) throw new IndexOutOfRangeException($"Index {index} is out of range of '{m_Length}' Length.");
        }

        [WriteAccessRequired]
        public void Dispose()
        {
            if (m_Buffer == null) return;
            if (m_AllocatorLabel == Allocator.Invalid) throw new InvalidOperationException("The NativeArray can not be Disposed because it was not allocated with a valid allocator.");
            if (m_AllocatorLabel > Allocator.None) UnsafeUtility.Free(m_Buffer, m_AllocatorLabel);
            m_Buffer = null; m_Length = 0;
        }
        public JobHandle Dispose(JobHandle inputDeps) { Dispose(); return default; }

        [WriteAccessRequired] public void CopyFrom(T[] array) => Copy(array, this);
        [WriteAccessRequired] public void CopyFrom(NativeArray<T> array) => Copy(array, this);
        public void CopyTo(T[] array) => Copy(this, array);
        public void CopyTo(NativeArray<T> array) => Copy(this, array);
        public T[] ToArray() { var a = new T[Length]; Copy(this, a, Length); return a; }

        public Enumerator GetEnumerator() => new Enumerator(ref this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(ref this);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Equals(NativeArray<T> other) => m_Buffer == other.m_Buffer && m_Length == other.m_Length;
        public override bool Equals(object obj) => obj is NativeArray<T> other && Equals(other);
        public override int GetHashCode() => ((int)(long)m_Buffer * 397) ^ m_Length;
        public static bool operator ==(NativeArray<T> left, NativeArray<T> right) => left.Equals(right);
        public static bool operator !=(NativeArray<T> left, NativeArray<T> right) => !left.Equals(right);

        static void CheckCopy(int srcLength, int srcIndex, int dstLength, int dstIndex, int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "length must be equal or greater than zero.");
            if (srcIndex < 0 || srcIndex > srcLength || (srcIndex == srcLength && srcLength > 0)) throw new ArgumentOutOfRangeException(nameof(srcIndex), "srcIndex is outside the range of valid indexes for the source NativeArray.");
            if (dstIndex < 0 || dstIndex > dstLength || (dstIndex == dstLength && dstLength > 0)) throw new ArgumentOutOfRangeException(nameof(dstIndex), "dstIndex is outside the range of valid indexes for the destination NativeArray.");
            if (srcIndex + length > srcLength) throw new ArgumentException("length is greater than the number of elements from srcIndex to the end of the source NativeArray.", nameof(length));
            if (dstIndex + length > dstLength) throw new ArgumentException("length is greater than the number of elements from dstIndex to the end of the destination NativeArray.", nameof(length));
        }
        static void CheckSameLength(int a, int b) { if (a != b) throw new ArgumentException("source and destination length must be the same"); }

        public static void Copy(NativeArray<T> src, NativeArray<T> dst) { CheckSameLength(src.Length, dst.Length); Copy(src, 0, dst, 0, src.Length); }
        public static void Copy(ReadOnly src, NativeArray<T> dst) { CheckSameLength(src.Length, dst.Length); Copy(src, 0, dst, 0, src.Length); }
        public static void Copy(T[] src, NativeArray<T> dst) { CheckSameLength(src.Length, dst.Length); Copy(src, 0, dst, 0, src.Length); }
        public static void Copy(NativeArray<T> src, T[] dst) { CheckSameLength(src.Length, dst.Length); Copy(src, 0, dst, 0, src.Length); }
        public static void Copy(ReadOnly src, T[] dst) { CheckSameLength(src.Length, dst.Length); Copy(src, 0, dst, 0, src.Length); }
        public static void Copy(NativeArray<T> src, NativeArray<T> dst, int length) => Copy(src, 0, dst, 0, length);
        public static void Copy(ReadOnly src, NativeArray<T> dst, int length) => Copy(src, 0, dst, 0, length);
        public static void Copy(T[] src, NativeArray<T> dst, int length) => Copy(src, 0, dst, 0, length);
        public static void Copy(NativeArray<T> src, T[] dst, int length) => Copy(src, 0, dst, 0, length);
        public static void Copy(ReadOnly src, T[] dst, int length) => Copy(src, 0, dst, 0, length);
        public static void Copy(NativeArray<T> src, int srcIndex, NativeArray<T> dst, int dstIndex, int length)
        {
            CheckCopy(src.Length, srcIndex, dst.Length, dstIndex, length);
            int sz = UnsafeUtility.SizeOf<T>();
            UnsafeUtility.MemMove((byte*)dst.m_Buffer + (long)dstIndex * sz, (byte*)src.m_Buffer + (long)srcIndex * sz, (long)length * sz);
        }
        public static void Copy(ReadOnly src, int srcIndex, NativeArray<T> dst, int dstIndex, int length)
        {
            CheckCopy(src.Length, srcIndex, dst.Length, dstIndex, length);
            int sz = UnsafeUtility.SizeOf<T>();
            UnsafeUtility.MemMove((byte*)dst.m_Buffer + (long)dstIndex * sz, (byte*)src.m_Buffer + (long)srcIndex * sz, (long)length * sz);
        }
        public static void Copy(T[] src, int srcIndex, NativeArray<T> dst, int dstIndex, int length)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            CheckCopy(src.Length, srcIndex, dst.Length, dstIndex, length);
            for (int i = 0; i < length; i++) UnsafeUtility.WriteArrayElement(dst.m_Buffer, dstIndex + i, src[srcIndex + i]);
        }
        public static void Copy(NativeArray<T> src, int srcIndex, T[] dst, int dstIndex, int length)
        {
            if (dst == null) throw new ArgumentNullException(nameof(dst));
            CheckCopy(src.Length, srcIndex, dst.Length, dstIndex, length);
            for (int i = 0; i < length; i++) dst[dstIndex + i] = UnsafeUtility.ReadArrayElement<T>(src.m_Buffer, srcIndex + i);
        }
        public static void Copy(ReadOnly src, int srcIndex, T[] dst, int dstIndex, int length)
        {
            if (dst == null) throw new ArgumentNullException(nameof(dst));
            CheckCopy(src.Length, srcIndex, dst.Length, dstIndex, length);
            for (int i = 0; i < length; i++) dst[dstIndex + i] = UnsafeUtility.ReadArrayElement<T>(src.m_Buffer, srcIndex + i);
        }

        public U ReinterpretLoad<U>(int sourceIndex) where U : struct
        {
            long bytes = (long)sourceIndex * UnsafeUtility.SizeOf<U>();
            if (sourceIndex < 0 || bytes + UnsafeUtility.SizeOf<U>() > (long)Length * UnsafeUtility.SizeOf<T>()) throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            return UnsafeUtility.ReadArrayElement<U>(m_Buffer, sourceIndex);
        }
        public void ReinterpretStore<U>(int destIndex, U data) where U : struct
        {
            long bytes = (long)destIndex * UnsafeUtility.SizeOf<U>();
            if (destIndex < 0 || bytes + UnsafeUtility.SizeOf<U>() > (long)Length * UnsafeUtility.SizeOf<T>()) throw new ArgumentOutOfRangeException(nameof(destIndex));
            UnsafeUtility.WriteArrayElement(m_Buffer, destIndex, data);
        }
        public NativeArray<U> Reinterpret<U>() where U : struct
        {
            int t = UnsafeUtility.SizeOf<T>(), u = UnsafeUtility.SizeOf<U>();
            long bytes = (long)Length * t;
            if (bytes % u != 0) throw new InvalidOperationException($"Types {typeof(T)} (array length {Length}) and {typeof(U)} cannot be aliased due to size constraints. The size of the types and lengths involved must line up.");
            return As<U>((int)(bytes / u));
        }
        public NativeArray<U> Reinterpret<U>(int expectedTypeSize) where U : struct
        {
            if (UnsafeUtility.SizeOf<T>() != expectedTypeSize) throw new InvalidOperationException($"Type {typeof(T)} was expected to be {expectedTypeSize} but is {UnsafeUtility.SizeOf<T>()} bytes");
            if (UnsafeUtility.SizeOf<U>() != expectedTypeSize) throw new InvalidOperationException($"Type {typeof(U)} was expected to be {expectedTypeSize} but is {UnsafeUtility.SizeOf<U>()} bytes");
            return As<U>(Length);
        }
        NativeArray<U> As<U>(int length) where U : struct
        {
            var r = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<U>(m_Buffer, length, Allocator.None);
            r.m_Safety = m_Safety;
            return r;
        }
        public NativeArray<T> GetSubArray(int start, int length)
        {
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), $"start {start} must be >= 0");
            if (start + length > Length) throw new ArgumentOutOfRangeException(nameof(length), $"sub array range {start}-{start + length - 1} is outside the range of the native array 0-{Length - 1}");
            var r = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>((byte*)m_Buffer + (long)UnsafeUtility.SizeOf<T>() * start, length, Allocator.None);
            r.m_Safety = m_Safety;
            return r;
        }
        public ReadOnly AsReadOnly() => new ReadOnly(m_Buffer, m_Length, ref m_Safety);

        public struct Enumerator : IEnumerator<T>
        {
            NativeArray<T> m_Array;
            int m_Index;
            T value;
            public Enumerator(ref NativeArray<T> array) { m_Array = array; m_Index = -1; value = default; }
            public void Dispose() { }
            public bool MoveNext()
            {
                m_Index++;
                if (m_Index < m_Array.m_Length) { value = UnsafeUtility.ReadArrayElement<T>(m_Array.m_Buffer, m_Index); return true; }
                value = default; return false;
            }
            public void Reset() => m_Index = -1;
            public T Current => value;
            object IEnumerator.Current => Current;
        }

        [NativeContainer, NativeContainerIsReadOnly]
        [DebuggerDisplay("Length = {Length}")]
        public struct ReadOnly : IEnumerable<T>
        {
            [NativeDisableUnsafePtrRestriction] internal void* m_Buffer;
            internal int m_Length;
            internal AtomicSafetyHandle m_Safety;
            internal ReadOnly(void* buffer, int length, ref AtomicSafetyHandle safety) { m_Buffer = buffer; m_Length = length; m_Safety = safety; }
            public int Length => m_Length;
            public bool IsCreated => m_Buffer != null;
            public T this[int index]
            {
                get
                {
                    if ((uint)index >= (uint)m_Length) throw new IndexOutOfRangeException($"Index {index} is out of range of '{m_Length}' Length.");
                    return UnsafeUtility.ReadArrayElement<T>(m_Buffer, index);
                }
            }
            public void CopyTo(T[] array) => Copy(this, array);
            public void CopyTo(NativeArray<T> array) => Copy(this, array);
            public T[] ToArray() { var a = new T[m_Length]; Copy(this, a, m_Length); return a; }
            public NativeArray<U>.ReadOnly Reinterpret<U>() where U : struct
            {
                long bytes = (long)m_Length * UnsafeUtility.SizeOf<T>();
                if (bytes % UnsafeUtility.SizeOf<U>() != 0) throw new InvalidOperationException($"Types {typeof(T)} (array length {m_Length}) and {typeof(U)} cannot be aliased due to size constraints.");
                return new NativeArray<U>.ReadOnly(m_Buffer, (int)(bytes / UnsafeUtility.SizeOf<U>()), ref m_Safety);
            }
            public Enumerator GetEnumerator() => new Enumerator(in this);
            IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public struct Enumerator : IEnumerator<T>
            {
                readonly ReadOnly m_Array;
                int m_Index;
                T value;
                public Enumerator(in ReadOnly array) { m_Array = array; m_Index = -1; value = default; }
                public void Dispose() { }
                public bool MoveNext()
                {
                    m_Index++;
                    if (m_Index < m_Array.m_Length) { value = UnsafeUtility.ReadArrayElement<T>(m_Array.m_Buffer, m_Index); return true; }
                    value = default; return false;
                }
                public void Reset() => m_Index = -1;
                public T Current => value;
                object IEnumerator.Current => Current;
            }
        }
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    public static unsafe partial class NativeArrayUnsafeUtility
    {
        public static AtomicSafetyHandle GetAtomicSafetyHandle<T>(NativeArray<T> array) where T : struct => array.m_Safety;
        public static void SetAtomicSafetyHandle<T>(ref NativeArray<T> array, AtomicSafetyHandle safety) where T : struct => array.m_Safety = safety;
        public static NativeArray<T> ConvertExistingDataToNativeArray<T>(void* dataPointer, int length, Allocator allocator) where T : struct
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must be >= 0");
            return new NativeArray<T> { m_Buffer = dataPointer, m_Length = length, m_AllocatorLabel = allocator, m_MinIndex = 0, m_MaxIndex = length - 1 };
        }
        public static void* GetUnsafePtr<T>(this NativeArray<T> nativeArray) where T : struct => nativeArray.m_Buffer;
        public static void* GetUnsafeReadOnlyPtr<T>(this NativeArray<T> nativeArray) where T : struct => nativeArray.m_Buffer;
        public static void* GetUnsafeReadOnlyPtr<T>(this NativeArray<T>.ReadOnly nativeArray) where T : struct => nativeArray.m_Buffer;
        public static void* GetUnsafeBufferPointerWithoutChecks<T>(NativeArray<T> nativeArray) where T : struct => nativeArray.m_Buffer;
    }

    /// <summary>Safety handle: every check passes (jobs complete when scheduled, nothing runs concurrently).</summary>
    public struct AtomicSafetyHandle
    {
        internal const int Read = 1;
        internal const int Write = 2;
        internal const int Dispose = 4;
        internal const int TempVersion = 8;
        internal const int VersionIncrement = 16;
        internal const int ReadCheck = -7;
        internal const int WriteCheck = -6;
        internal const int DisposeCheck = -4;
        internal const int ReadWriteDisposeCheck = -8;
        [NativeDisableUnsafePtrRestriction] internal IntPtr versionNode;
        internal int version;
        internal int staticSafetyId;
        static int nextStaticId;

        public static AtomicSafetyHandle Create() => new AtomicSafetyHandle { versionNode = (IntPtr)1 };
        public static AtomicSafetyHandle GetTempUnsafePtrSliceHandle() => new AtomicSafetyHandle { versionNode = (IntPtr)2 };
        public static AtomicSafetyHandle GetTempMemoryHandle() => new AtomicSafetyHandle { versionNode = (IntPtr)3 };
        public static bool IsTempMemoryHandle(AtomicSafetyHandle handle) => handle.versionNode == (IntPtr)3;
        public static void Release(AtomicSafetyHandle handle) { }
        public static bool IsDefaultValue(in AtomicSafetyHandle handle) => handle.versionNode == IntPtr.Zero && handle.version == 0;
        internal static void SetExclusiveWeak(ref AtomicSafetyHandle handle, bool enabled) { }
        internal static bool GetExclusiveWeak(in AtomicSafetyHandle handle) => false;
        public static void PrepareUndisposable(ref AtomicSafetyHandle handle) { }
        public static void UseSecondaryVersion(ref AtomicSafetyHandle handle) { }
        public static void SetAllowSecondaryVersionWriting(AtomicSafetyHandle handle, bool allowWriting) { }
        public static void SetBumpSecondaryVersionOnScheduleWrite(AtomicSafetyHandle handle, bool value) { }
        public static void SetAllowReadOrWriteAccess(AtomicSafetyHandle handle, bool allowReadWriteAccess) { }
        public static bool GetAllowReadOrWriteAccess(AtomicSafetyHandle handle) => true;
        public static void SetNestedContainer(AtomicSafetyHandle handle, bool isNestedContainer) { }
        public static bool GetNestedContainer(AtomicSafetyHandle handle) => false;
        public static void CheckWriteAndBumpSecondaryVersion(AtomicSafetyHandle handle) { }
        public static EnforceJobResult EnforceAllBufferJobsHaveCompleted(AtomicSafetyHandle handle) => EnforceJobResult.AllJobsAlreadySynced;
        public static EnforceJobResult EnforceAllBufferJobsHaveCompletedAndRelease(AtomicSafetyHandle handle) => EnforceJobResult.AllJobsAlreadySynced;
        public static EnforceJobResult EnforceAllBufferJobsHaveCompletedAndDisableReadWrite(AtomicSafetyHandle handle) => EnforceJobResult.AllJobsAlreadySynced;
        internal static void CheckReadAndThrowNoEarlyOut(AtomicSafetyHandle handle) { }
        internal static void CheckWriteAndThrowNoEarlyOut(AtomicSafetyHandle handle) { }
        public static void CheckDeallocateAndThrow(AtomicSafetyHandle handle) { }
        public static void CheckGetSecondaryDataPointerAndThrow(AtomicSafetyHandle handle) { }
        public static int GetReaderArray(AtomicSafetyHandle handle, int maxCount, IntPtr output) => 0;
        public static JobHandle GetWriter(AtomicSafetyHandle handle) => default;
        public static void CheckReadAndThrow(AtomicSafetyHandle handle) { }
        public static void CheckWriteAndThrow(AtomicSafetyHandle handle) { }
        public static void ValidateNonDefaultHandle(in AtomicSafetyHandle handle) { }
        public static bool IsValidNonDefaultHandle(in AtomicSafetyHandle handle) => !IsDefaultValue(handle);
        public static void CheckExistsAndThrow(in AtomicSafetyHandle handle) { }
        public static bool IsHandleValid(in AtomicSafetyHandle handle) => true;
        public static string GetReaderName(AtomicSafetyHandle handle, int readerIndex) => null;
        public static string GetWriterName(AtomicSafetyHandle handle) => null;
        public static unsafe int NewStaticSafetyId(byte* ownerTypeNameBytes, int byteCount) => System.Threading.Interlocked.Increment(ref nextStaticId);
        public static int NewStaticSafetyId<T>() => System.Threading.Interlocked.Increment(ref nextStaticId);
        public static unsafe void SetCustomErrorMessage(int staticSafetyId, AtomicSafetyErrorType errorType, byte* messageBytes, int byteCount) { }
        public static void SetStaticSafetyId(ref AtomicSafetyHandle handle, int staticSafetyId) => handle.staticSafetyId = staticSafetyId;
        internal static void CreateHandle(out AtomicSafetyHandle safety, Allocator allocator) => safety = allocator == Allocator.Temp ? GetTempMemoryHandle() : Create();
        internal static void DisposeHandle(ref AtomicSafetyHandle safety) { }
    }

    /// <summary>Leak detection is not tracked: Create returns a sentinel, Dispose / Clear do nothing.</summary>
    public sealed class DisposeSentinel
    {
        internal DisposeSentinel() { }
        public static void Dispose(ref AtomicSafetyHandle safety, ref DisposeSentinel sentinel) => sentinel = null;
        public static void Create(out AtomicSafetyHandle safety, out DisposeSentinel sentinel, int callSiteStackDepth, Allocator allocator)
        {
            safety = allocator == Allocator.Temp ? AtomicSafetyHandle.GetTempMemoryHandle() : AtomicSafetyHandle.Create();
            sentinel = null;
        }
        public static void Clear(ref DisposeSentinel sentinel) => sentinel = null;
        internal static void Create(out AtomicSafetyHandle safety, out Dummy sentinel, int callSiteStackDepth, Allocator allocator)
        {
            safety = allocator == Allocator.Temp ? AtomicSafetyHandle.GetTempMemoryHandle() : AtomicSafetyHandle.Create();
            sentinel = default;
        }
        internal static void Clear(ref Dummy sentinel) => sentinel = default;
        internal struct Dummy
        {
            public static implicit operator Dummy(Class value) => default;
            public class Class { }
        }
    }
}
