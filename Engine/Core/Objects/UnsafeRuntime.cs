// Low-level APIs that project code (Unity.Collections, LitMotion, UniTask…) calls at runtime under `ua play` / `ua test`:
// UnsafeUtility on .NET native memory, and the legacy Input manager as "no device attached" plus keys/buttons scripted by the agent.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SUnsafe = System.Runtime.CompilerServices.Unsafe;

namespace Unity.Collections.LowLevel.Unsafe
{
    public static unsafe partial class UnsafeUtility
    {
        static NativeLeakDetectionMode leakMode = NativeLeakDetectionMode.Disabled;

        public static void* Malloc(long size, int alignment, Allocator allocator) =>
            allocator == Allocator.None ? throw new ArgumentException("Allocator.None is not a valid allocator") : NativeMemory.AlignedAlloc((nuint)Math.Max(size, 1), (nuint)Math.Max(alignment, 1));
        public static void Free(void* memory, Allocator allocator) { if (memory != null && allocator != Allocator.None) NativeMemory.AlignedFree(memory); }
        public static void* MallocTracked(long size, int alignment, Allocator allocator, int callstacksToSkip) => Malloc(size, alignment, allocator);
        public static void FreeTracked(void* memory, Allocator allocator) => Free(memory, allocator);
        public static bool IsValidAllocator(Allocator allocator) => allocator > Allocator.None;

        public static void MemMove(void* destination, void* source, long size) => Buffer.MemoryCopy(source, destination, size, size);
        public static int MemCmp(void* ptr1, void* ptr2, long size) => new ReadOnlySpan<byte>(ptr1, (int)size).SequenceCompareTo(new ReadOnlySpan<byte>(ptr2, (int)size));
        public static void MemCpyReplicate(void* destination, void* source, int size, int count)
        {
            for (int i = 0; i < count; i++) Buffer.MemoryCopy(source, (byte*)destination + (long)i * size, size, size);
        }
        public static void MemCpyStride(void* destination, int destinationStride, void* source, int sourceStride, int elementSize, int count)
        {
            for (int i = 0; i < count; i++) Buffer.MemoryCopy((byte*)source + (long)i * sourceStride, (byte*)destination + (long)i * destinationStride, elementSize, elementSize);
        }

        static readonly Dictionary<Type, int> sizes = new();
        static readonly Dictionary<Type, bool> managed = new();
        public static int SizeOf<T>() where T : struct => SUnsafe.SizeOf<T>();
        public static int SizeOf(Type type)
        {
            lock (sizes)
                if (!sizes.TryGetValue(type, out var s))
                    sizes[type] = s = (int)typeof(SUnsafe).GetMethod(nameof(SUnsafe.SizeOf))!.MakeGenericMethod(type).Invoke(null, null)!;
            return sizes[type];
        }
        public static int AlignOf<T>() where T : struct { int s = SUnsafe.SizeOf<T>(); return s >= 8 ? 8 : s >= 4 ? 4 : s >= 2 ? 2 : 1; }
        static bool ContainsRefs(Type type)
        {
            lock (managed)
                if (!managed.TryGetValue(type, out var r))
                    managed[type] = r = (bool)typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.IsReferenceOrContainsReferences))!.MakeGenericMethod(type).Invoke(null, null)!;
            return managed[type];
        }
        public static bool IsUnmanaged<T>() => !RuntimeHelpers.IsReferenceOrContainsReferences<T>();
        public static bool IsUnmanaged(Type type) => type.IsValueType && !ContainsRefs(type);
        public static bool IsBlittable<T>() where T : struct => IsBlittable(typeof(T));
        public static bool IsBlittable(Type type) => type.IsValueType && !ContainsRefs(type) && type != typeof(bool) && type != typeof(char);
        public static bool IsValidNativeContainerElementType<T>() => IsUnmanaged<T>();
        public static bool IsValidNativeContainerElementType(Type type) => IsUnmanaged(type);
        public static bool IsNativeContainerType<T>() => typeof(T).IsDefined(typeof(NativeContainerAttribute), false);
        internal static int GetScriptingTypeFlags(Type type) => (IsUnmanaged(type) ? 1 : 0) | (type.IsDefined(typeof(NativeContainerAttribute), false) ? 2 : 0);
        internal static bool IsArrayBlittable(Array arr) => IsBlittable(arr.GetType().GetElementType()!);
        internal static bool IsGenericListBlittable<T>() where T : struct => IsBlittable<T>();
        internal static string GetReasonForArrayNonBlittable(Array arr) => GetReasonForTypeNonBlittable(arr.GetType().GetElementType()!);
        internal static string GetReasonForGenericListNonBlittable<T>() where T : struct => GetReasonForTypeNonBlittable(typeof(T));
        internal static string GetReasonForTypeNonBlittable(Type t) => $"{t} is not blittable (it is a reference type or contains references)";
        internal static string GetReasonForValueTypeNonBlittable<T>() where T : struct => GetReasonForTypeNonBlittable(typeof(T));
        internal static void LogError(string msg, string filename, int linenumber) => UnityEngine.Debug.LogError($"{filename}({linenumber}): {msg}");

        public static int GetFieldOffset(System.Reflection.FieldInfo field) => (int)Marshal.OffsetOf(field.DeclaringType!, field.Name);
        public static void* PinGCObjectAndGetAddress(object target, out ulong gcHandle)
        {
            if (target is Array arr) return PinGCArrayAndGetDataAddress(arr, out gcHandle);
            var h = GCHandle.Alloc(target, GCHandleType.Pinned);
            gcHandle = (ulong)(long)GCHandle.ToIntPtr(h);
            return (void*)h.AddrOfPinnedObject();
        }
        public static void* PinGCArrayAndGetDataAddress(Array target, out ulong gcHandle)
        {
            var h = GCHandle.Alloc(target, GCHandleType.Pinned);
            gcHandle = (ulong)(long)GCHandle.ToIntPtr(h);
            return (void*)Marshal.UnsafeAddrOfPinnedArrayElement(target, 0);
        }
        public static void ReleaseGCObject(ulong gcHandle) => GCHandle.FromIntPtr((IntPtr)(long)gcHandle).Free();
        public static void CopyObjectAddressToPtr(object target, void* dstPtr) => *(IntPtr*)dstPtr = *(IntPtr*)SUnsafe.AsPointer(ref target);

        public static int CheckForLeaks() => 0;
        public static int ForgiveLeaks() => 0;
        public static NativeLeakDetectionMode GetLeakDetectionMode() => leakMode;
        public static void SetLeakDetectionMode(NativeLeakDetectionMode value) => leakMode = value;
        internal static int LeakRecord(IntPtr handle, LeakCategory category, int callstacksToSkip) => 0;
        internal static int LeakErase(IntPtr handle, LeakCategory category) => 0;

        public static void CopyPtrToStructure<T>(void* ptr, out T output) where T : struct => output = SUnsafe.Read<T>(ptr);
        public static void CopyStructureToPtr<T>(ref T input, void* ptr) where T : struct => SUnsafe.Write(ptr, input);
        public static T ReadArrayElement<T>(void* source, int index) => SUnsafe.Read<T>((byte*)source + (long)index * SUnsafe.SizeOf<T>());
        public static T ReadArrayElementWithStride<T>(void* source, int index, int stride) => SUnsafe.Read<T>((byte*)source + (long)index * stride);
        public static void WriteArrayElement<T>(void* destination, int index, T value) => SUnsafe.Write((byte*)destination + (long)index * SUnsafe.SizeOf<T>(), value);
        public static void WriteArrayElementWithStride<T>(void* destination, int index, int stride, T value) => SUnsafe.Write((byte*)destination + (long)index * stride, value);
        public static void* AddressOf<T>(ref T output) where T : struct => SUnsafe.AsPointer(ref output);
        public static ref T As<U, T>(ref U from) => ref SUnsafe.As<U, T>(ref from);
        public static ref T AsRef<T>(void* ptr) where T : struct => ref SUnsafe.AsRef<T>(ptr);
        public static ref T ArrayElementAsRef<T>(void* ptr, int index) where T : struct => ref SUnsafe.AsRef<T>((byte*)ptr + (long)index * SUnsafe.SizeOf<T>());
        public static int EnumToInt<T>(T enumValue) where T : struct, IConvertible => SUnsafe.SizeOf<T>() == 4 ? SUnsafe.As<T, int>(ref enumValue) : Convert.ToInt32(enumValue);
        public static bool EnumEquals<T>(T lhs, T rhs) where T : struct, IConvertible => EqualityComparer<T>.Default.Equals(lhs, rhs);
    }
}

namespace UnityEngine
{
}


