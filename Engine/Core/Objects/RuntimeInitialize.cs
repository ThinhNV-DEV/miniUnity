using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MiniUnity
{
    /// <summary>[RuntimeInitializeOnLoadMethod] as Unity calls it on entering play mode: SubsystemRegistration, AfterAssembliesLoaded
    /// and BeforeSplashScreen before the first scene loads, BeforeSceneLoad once its objects exist and before Awake, AfterSceneLoad
    /// (the default) after Awake and OnEnable, before the first Start. UniTask installs its player loop this way, the Input System
    /// initialises itself, and games set their frame rate or create their singletons.</summary>
    public static class RuntimeInitialize
    {
        public static int Run(IEnumerable<Assembly> assemblies, params RuntimeInitializeLoadType[] types)
        {
            var all = Methods(assemblies);
            int n = 0;
            foreach (var type in types)
                foreach (var (m, lt) in all)
                {
                    if (lt != type) continue;
                    n++;
                    try { m.Invoke(null, null); }
                    catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            return n;
        }

        static readonly HashSet<Assembly> s_EditorInitialized = new();

        /// <summary>[InitializeOnLoad] static constructors, then [InitializeOnLoadMethod] methods: what the Editor runs after every
        /// domain reload, so before any play mode starts. Code compiled for the Editor initialises itself this way rather than through
        /// [RuntimeInitializeOnLoadMethod] - the Input System sets up its manager in InputSystemEditorInitializer and returns early
        /// from its runtime initialisers under UNITY_EDITOR - and MiniUnity compiles a project for the Editor. Once per assembly per
        /// process, as a domain load is.</summary>
        public static int RunEditorInitializeOnLoad(IEnumerable<Assembly> assemblies)
        {
            var fresh = assemblies.Distinct().Where(s_EditorInitialized.Add).ToList();
            int n = 0;
            foreach (var asm in fresh)
                foreach (var t in Types(asm))
                {
                    if (t.ContainsGenericParameters || !HasAttribute(t.GetCustomAttributesData, "UnityEditor.InitializeOnLoadAttribute")) continue;
                    n++;
                    try { System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle); }
                    catch (TypeInitializationException e) { Debug.LogException(e.InnerException ?? e); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            foreach (var asm in fresh)
                foreach (var t in Types(asm))
                {
                    if (t.ContainsGenericParameters) continue;
                    MethodInfo[] ms;
                    try { ms = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
                    catch (Exception) { continue; }
                    foreach (var m in ms)
                    {
                        if (m.IsGenericMethodDefinition || !HasAttribute(m.GetCustomAttributesData, "UnityEditor.InitializeOnLoadMethodAttribute")) continue;
                        try { if (m.GetParameters().Length != 0) continue; } catch (Exception) { continue; }
                        n++;
                        try { m.Invoke(null, null); }
                        catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
                        catch (Exception e) { Debug.LogException(e); }
                    }
                }
            return n;
        }

        static Type[] Types(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
            catch (Exception) { return Array.Empty<Type>(); }
        }

        static bool HasAttribute(Func<IList<CustomAttributeData>> data, string fullName)
        {
            try { return data().Any(a => a.AttributeType.FullName == fullName); }
            catch (Exception) { return false; }
        }

        /// <summary>Static parameterless methods carrying the attribute, in assembly then declaration order. The attribute is read as
        /// metadata (by name) so that it is found whichever reference assembly the project was compiled against.</summary>
        public static List<(MethodInfo method, RuntimeInitializeLoadType type)> Methods(IEnumerable<Assembly> assemblies)
        {
            var res = new List<(MethodInfo, RuntimeInitializeLoadType)>();
            foreach (var asm in assemblies.Distinct())
            {
                foreach (var t in Types(asm))
                {
                    if (t.ContainsGenericParameters) continue;
                    MethodInfo[] ms;
                    try { ms = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
                    catch (Exception) { continue; }
                    foreach (var m in ms)
                    {
                        if (m.IsGenericMethodDefinition) continue;
                        IList<CustomAttributeData> attrs;
                        try { if (m.GetParameters().Length != 0) continue; attrs = m.GetCustomAttributesData(); }
                        catch (Exception) { continue; }
                        foreach (var cad in attrs)
                        {
                            if (cad.AttributeType.FullName != "UnityEngine.RuntimeInitializeOnLoadMethodAttribute") continue;
                            var lt = cad.ConstructorArguments.Count > 0 ? (RuntimeInitializeLoadType)Convert.ToInt32(cad.ConstructorArguments[0].Value)
                                                                         : RuntimeInitializeLoadType.AfterSceneLoad;
                            res.Add((m, lt));
                        }
                    }
                }
            }
            return res;
        }
    }
}
