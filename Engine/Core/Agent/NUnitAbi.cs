// Unity's nunit.framework (3.5, "unity-custom") and NUnit's portable 3.5 build, which MiniUnity loads because Unity's build needs
// remoting CallContext, differ in one abstract member that every constraint implements:
//     Unity:    public abstract ConstraintResult Constraint.ApplyTo(object actual)
//     portable: public abstract ConstraintResult Constraint.ApplyTo<TActual>(TActual actual)
// A Constraint compiled against Unity's build (UnityEngine.TestTools.Constraints.AllocatingGCMemoryConstraint, project constraints)
// overrides only the first, so loading it against the portable build fails with TypeLoadException. The portable build is patched
// once (cached next to it) so that both members exist and forward to each other:
//     ApplyTo<TActual>(TActual a) => ApplyTo((object)a)        (virtual instead of abstract: Unity-built constraints)
//     ApplyTo(object a)          => ApplyTo<object>(a)          (new virtual: overridden by Unity-built constraints)
// NUnit's own constraints override the generic member, Unity-built ones the object member; each call path reaches the override.
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MiniUnity.Agent
{
    public static class NUnitAbi
    {
        const string PatchedName = "nunit.framework.unity-abi-1.dll";   // bump the suffix when the patch changes

        /// <summary>Path of the patched copy of the portable NUnit at <paramref name="portable"/> (created on first use).</summary>
        public static string Patched(string portable)
        {
            var output = Path.Combine(Path.GetDirectoryName(portable)!, PatchedName);
            if (File.Exists(output)) return output;
            using var asm = AssemblyDefinition.ReadAssembly(portable, new ReaderParameters { InMemory = true });
            var mod = asm.MainModule;
            var constraint = mod.GetType("NUnit.Framework.Constraints.Constraint") ?? throw new InvalidOperationException("NUnit Constraint type not found");
            if (constraint.Methods.Any(m => m.Name == "ApplyTo" && !m.HasGenericParameters && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.MetadataType == MetadataType.Object))
                throw new InvalidOperationException("NUnit build already has Constraint.ApplyTo(object): not the expected portable 3.5 build");
            var generic = constraint.Methods.Single(m => m.Name == "ApplyTo" && m.IsAbstract && m.GenericParameters.Count == 1
                                                         && m.Parameters.Count == 1 && m.Parameters[0].ParameterType is GenericParameter);

            var byObject = new MethodDefinition("ApplyTo", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot, generic.ReturnType);
            byObject.Parameters.Add(new ParameterDefinition("actual", ParameterAttributes.None, mod.TypeSystem.Object));
            var asObject = new GenericInstanceMethod(generic);
            asObject.GenericArguments.Add(mod.TypeSystem.Object);
            var il = byObject.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, asObject); il.Emit(OpCodes.Ret);
            constraint.Methods.Add(byObject);

            generic.Attributes &= ~MethodAttributes.Abstract;
            var g = generic.Body.GetILProcessor();
            g.Emit(OpCodes.Ldarg_0); g.Emit(OpCodes.Ldarg_1); g.Emit(OpCodes.Box, generic.GenericParameters[0]); g.Emit(OpCodes.Callvirt, byObject); g.Emit(OpCodes.Ret);

            var tmp = output + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            asm.Write(tmp);
            File.Move(tmp, output, overwrite: true);
            return output;
        }
    }
}
