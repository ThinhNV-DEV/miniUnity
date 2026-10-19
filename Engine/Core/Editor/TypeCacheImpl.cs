// UnityEditor.TypeCache over the loaded assemblies (engine, packages, project), and the StageUtility queries runtime code
// makes under UNITY_EDITOR (PostProcessing's volume filter): MiniUnity has no prefab stage, so every object is in the main stage.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityEditor
{
    public static partial class TypeCache
    {
        static Type[] s_Types; static int s_AsmCount = -1;
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        static Type[] Types(string assemblyName = null)
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            if (s_Types == null || s_AsmCount != asms.Length)
            {
                var l = new List<Type>();
                foreach (var a in asms)
                {
                    if (a.IsDynamic) continue;
                    try { l.AddRange(a.GetTypes()); }
                    catch (ReflectionTypeLoadException e) { l.AddRange(e.Types.Where(t => t != null)); }
                    catch { }
                }
                s_Types = l.ToArray(); s_AsmCount = asms.Length;
            }
            return assemblyName == null ? s_Types : s_Types.Where(t => t.Assembly.GetName().Name == assemblyName).ToArray();
        }

        static bool Derives(Type t, Type parent)
        {
            if (t == parent) return false;
            if (parent.IsGenericTypeDefinition)
            {
                if (parent.IsInterface) return t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == parent);
                for (var b = t.BaseType; b != null; b = b.BaseType) if (b.IsGenericType && b.GetGenericTypeDefinition() == parent) return true;
                return false;
            }
            return parent.IsAssignableFrom(t);
        }

        public static TypeCollection GetTypesDerivedFrom<T>() => GetTypesDerivedFrom(typeof(T));
        public static TypeCollection GetTypesDerivedFrom<T>(string assemblyName) => GetTypesDerivedFrom(typeof(T), assemblyName);
        public static TypeCollection GetTypesDerivedFrom(Type parentType) => GetTypesDerivedFrom(parentType, null);
        public static TypeCollection GetTypesDerivedFrom(Type parentType, string assemblyName) => new(Types(assemblyName).Where(t => Derives(t, parentType)).ToArray());

        public static TypeCollection GetTypesWithAttribute<T>() where T : Attribute => GetTypesWithAttribute(typeof(T));
        public static TypeCollection GetTypesWithAttribute<T>(string assemblyName) where T : Attribute => GetTypesWithAttribute(typeof(T), assemblyName);
        public static TypeCollection GetTypesWithAttribute(Type attrType) => GetTypesWithAttribute(attrType, null);
        public static TypeCollection GetTypesWithAttribute(Type attrType, string assemblyName) => new(Types(assemblyName).Where(t => Has(t, attrType)).ToArray());

        public static MethodCollection GetMethodsWithAttribute<T>() where T : Attribute => GetMethodsWithAttribute(typeof(T));
        public static MethodCollection GetMethodsWithAttribute<T>(string assemblyName) where T : Attribute => GetMethodsWithAttribute(typeof(T), assemblyName);
        public static MethodCollection GetMethodsWithAttribute(Type attrType) => GetMethodsWithAttribute(attrType, null);
        public static MethodCollection GetMethodsWithAttribute(Type attrType, string assemblyName) => new(Types(assemblyName).SelectMany(t => Safe(() => t.GetMethods(All))).Where(m => Has(m, attrType)).ToArray());

        public static FieldInfoCollection GetFieldsWithAttribute<T>() where T : Attribute => GetFieldsWithAttribute(typeof(T));
        public static FieldInfoCollection GetFieldsWithAttribute<T>(string assemblyName) where T : Attribute => GetFieldsWithAttribute(typeof(T), assemblyName);
        public static FieldInfoCollection GetFieldsWithAttribute(Type attrType) => GetFieldsWithAttribute(attrType, null);
        public static FieldInfoCollection GetFieldsWithAttribute(Type attrType, string assemblyName) => new(Types(assemblyName).SelectMany(t => Safe(() => t.GetFields(All))).Where(f => Has(f, attrType)).ToArray());

        static bool Has(MemberInfo m, Type attr) { try { return m.IsDefined(attr, true); } catch { return false; } }
        static T[] Safe<T>(Func<T[]> f) { try { return f(); } catch { return Array.Empty<T>(); } }

        public struct TypeCollection : IList<Type>, IList
        {
            readonly Type[] items;
            internal TypeCollection(Type[] a) { items = a; }
            Type[] A => items ?? Array.Empty<Type>();
            public int Count => A.Length;
            public bool IsReadOnly => true;
            public bool IsFixedSize => true;
            public bool IsSynchronized => true;
            public object SyncRoot => null;
            public Type this[int index] { get => A[index]; set => throw new NotSupportedException(); }
            object IList.this[int index] { get => A[index]; set => throw new NotSupportedException(); }
            public bool Contains(Type item) => Array.IndexOf(A, item) >= 0;
            public bool Contains(object item) => item is Type t && Contains(t);
            public int IndexOf(Type item) => Array.IndexOf(A, item);
            public int IndexOf(object item) => item is Type t ? IndexOf(t) : -1;
            public void CopyTo(Type[] array, int arrayIndex) => A.CopyTo(array, arrayIndex);
            public void CopyTo(Array array, int arrayIndex) => A.CopyTo(array, arrayIndex);
            public Enumerator GetEnumerator() => new(A);
            IEnumerator<Type> IEnumerable<Type>.GetEnumerator() => ((IEnumerable<Type>)A).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => A.GetEnumerator();
            void IList<Type>.Insert(int index, Type item) => throw new NotSupportedException();
            void IList<Type>.RemoveAt(int index) => throw new NotSupportedException();
            void ICollection<Type>.Add(Type item) => throw new NotSupportedException();
            void ICollection<Type>.Clear() => throw new NotSupportedException();
            bool ICollection<Type>.Remove(Type item) => throw new NotSupportedException();
            int IList.Add(object value) => throw new NotSupportedException();
            void IList.Clear() => throw new NotSupportedException();
            void IList.Insert(int index, object value) => throw new NotSupportedException();
            void IList.Remove(object value) => throw new NotSupportedException();
            void IList.RemoveAt(int index) => throw new NotSupportedException();
            public struct Enumerator : IEnumerator<Type>
            {
                readonly Type[] a; int i;
                internal Enumerator(Type[] a) { this.a = a; i = -1; }
                public bool MoveNext() => ++i < a.Length;
                public Type Current => a[i];
                object IEnumerator.Current => Current;
                void IEnumerator.Reset() => i = -1;
                public void Dispose() { }
            }
        }

        public struct MethodCollection : IList<MethodInfo>, IList
        {
            readonly MethodInfo[] items;
            internal MethodCollection(MethodInfo[] a) { items = a; }
            MethodInfo[] A => items ?? Array.Empty<MethodInfo>();
            public int Count => A.Length;
            public bool IsReadOnly => true;
            public bool IsFixedSize => true;
            public bool IsSynchronized => true;
            public object SyncRoot => null;
            public MethodInfo this[int index] { get => A[index]; set => throw new NotSupportedException(); }
            object IList.this[int index] { get => A[index]; set => throw new NotSupportedException(); }
            public bool Contains(MethodInfo item) => Array.IndexOf(A, item) >= 0;
            public bool Contains(object item) => item is MethodInfo t && Contains(t);
            public int IndexOf(MethodInfo item) => Array.IndexOf(A, item);
            public int IndexOf(object item) => item is MethodInfo t ? IndexOf(t) : -1;
            public void CopyTo(MethodInfo[] array, int arrayIndex) => A.CopyTo(array, arrayIndex);
            public void CopyTo(Array array, int arrayIndex) => A.CopyTo(array, arrayIndex);
            public Enumerator GetEnumerator() => new(A);
            IEnumerator<MethodInfo> IEnumerable<MethodInfo>.GetEnumerator() => ((IEnumerable<MethodInfo>)A).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => A.GetEnumerator();
            void IList<MethodInfo>.Insert(int index, MethodInfo item) => throw new NotSupportedException();
            void IList<MethodInfo>.RemoveAt(int index) => throw new NotSupportedException();
            void ICollection<MethodInfo>.Add(MethodInfo item) => throw new NotSupportedException();
            void ICollection<MethodInfo>.Clear() => throw new NotSupportedException();
            bool ICollection<MethodInfo>.Remove(MethodInfo item) => throw new NotSupportedException();
            int IList.Add(object value) => throw new NotSupportedException();
            void IList.Clear() => throw new NotSupportedException();
            void IList.Insert(int index, object value) => throw new NotSupportedException();
            void IList.Remove(object value) => throw new NotSupportedException();
            void IList.RemoveAt(int index) => throw new NotSupportedException();
            public struct Enumerator : IEnumerator<MethodInfo>
            {
                readonly MethodInfo[] a; int i;
                internal Enumerator(MethodInfo[] a) { this.a = a; i = -1; }
                public bool MoveNext() => ++i < a.Length;
                public MethodInfo Current => a[i];
                object IEnumerator.Current => Current;
                void IEnumerator.Reset() => i = -1;
                public void Dispose() { }
            }
        }

        public struct FieldInfoCollection : IList<FieldInfo>, IList
        {
            readonly FieldInfo[] items;
            internal FieldInfoCollection(FieldInfo[] a) { items = a; }
            FieldInfo[] A => items ?? Array.Empty<FieldInfo>();
            public int Count => A.Length;
            public bool IsReadOnly => true;
            public bool IsFixedSize => true;
            public bool IsSynchronized => true;
            public object SyncRoot => null;
            public FieldInfo this[int index] { get => A[index]; set => throw new NotSupportedException(); }
            object IList.this[int index] { get => A[index]; set => throw new NotSupportedException(); }
            public bool Contains(FieldInfo item) => Array.IndexOf(A, item) >= 0;
            public bool Contains(object item) => item is FieldInfo t && Contains(t);
            public int IndexOf(FieldInfo item) => Array.IndexOf(A, item);
            public int IndexOf(object item) => item is FieldInfo t ? IndexOf(t) : -1;
            public void CopyTo(FieldInfo[] array, int arrayIndex) => A.CopyTo(array, arrayIndex);
            public void CopyTo(Array array, int arrayIndex) => A.CopyTo(array, arrayIndex);
            public Enumerator GetEnumerator() => new(A);
            IEnumerator<FieldInfo> IEnumerable<FieldInfo>.GetEnumerator() => ((IEnumerable<FieldInfo>)A).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => A.GetEnumerator();
            void IList<FieldInfo>.Insert(int index, FieldInfo item) => throw new NotSupportedException();
            void IList<FieldInfo>.RemoveAt(int index) => throw new NotSupportedException();
            void ICollection<FieldInfo>.Add(FieldInfo item) => throw new NotSupportedException();
            void ICollection<FieldInfo>.Clear() => throw new NotSupportedException();
            bool ICollection<FieldInfo>.Remove(FieldInfo item) => throw new NotSupportedException();
            int IList.Add(object value) => throw new NotSupportedException();
            void IList.Clear() => throw new NotSupportedException();
            void IList.Insert(int index, object value) => throw new NotSupportedException();
            void IList.Remove(object value) => throw new NotSupportedException();
            void IList.RemoveAt(int index) => throw new NotSupportedException();
            public struct Enumerator : IEnumerator<FieldInfo>
            {
                readonly FieldInfo[] a; int i;
                internal Enumerator(FieldInfo[] a) { this.a = a; i = -1; }
                public bool MoveNext() => ++i < a.Length;
                public FieldInfo Current => a[i];
                object IEnumerator.Current => Current;
                void IEnumerator.Reset() => i = -1;
                public void Dispose() { }
            }
        }
    }
}

namespace UnityEditor.SceneManagement
{
    public static partial class StageUtility
    {
        public static bool IsGameObjectRenderedByCamera(UnityEngine.GameObject gameObject, UnityEngine.Camera camera) => true;
        public static bool IsGameObjectRenderedByCameraAndPartOfEditableScene(UnityEngine.GameObject gameObject, UnityEngine.Camera camera) => true;
    }
}
