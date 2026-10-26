#!/usr/bin/env python3
import re,sys,os
natives=[]
def split_params(rest):
    ps=rest[rest.index('(')+1:rest.rindex(')')].strip()
    if not ps: return []
    return [x.strip() for x in ps.split(',')]
def argnames(rest):
    r=[]
    for p in split_params(rest):
        toks=p.split()
        mod=toks[0] if toks[0] in ('ref','out','in') else ''
        r.append((mod+' ' if mod else '')+toks[-1])
    return r
src,dst=sys.argv[1],sys.argv[2]
for f in sorted(os.listdir(src)):
    if not f.endswith('.cs'): continue
    s=open(os.path.join(src,f)).read()
    out=[]
    lines=s.split('\n')
    i=0
    for ln in lines:
        st=ln.strip()
        # Unity 6 bindings marshal strings and arrays through UnityEngine.Bindings types (Engine/Core/Versioned/6000/Bindings.cs):
        # the namespace stays when the file uses one of them, it only held attributes before
        if st == 'using UnityEngine.Bindings;' and re.search(r'\b(ManagedSpanWrapper|StringMarshaller|OutStringMarshaller|BlittableArrayWrapper|OutArray)\b', s): out.append(ln); continue
        if st.startswith('using ') and any(x in st for x in ['Unity.IL2CPP','UnityEngine.Bindings','UnityEngine.Scripting','UnityEngineInternal','UnityEngine.Internal']): continue
        if st.startswith('[') and st.endswith(']') and not re.match(r'\[(StructLayout|FieldOffset|Serializable|Obsolete|Flags|DefaultMember|AttributeUsage|SerializeField|NonSerialized|EditorBrowsable)\b', st):
            continue
        ln=re.sub(r'\[DefaultValue\([^\]]*\)\]\s*','',ln)
        m=re.match(r'^(\s*)(.*?)\bextern\s+(.*\));\s*$',ln)
        if m:
            ind,pre,rest=m.groups()
            name=re.search(r'(\w+)\s*\(',rest).group(1)
            args=argnames(rest); params=rest[rest.index("(")+1:rest.rindex(")")]
            # Unity 6 binds some struct methods as instance externs (`private readonly extern bool Internal_Contains(in Vector3)`):
            # the native side gets the struct as its first argument
            if not re.search(r'\bstatic\b',pre):
                args=['this']+args; params=f'{f[:-3]} _unity_self'+(', '+params if params.strip() else '')
            out.append(f'{ind}{pre}{rest} => MiniUnity.Native.{f[:-3]}_{name}({", ".join(args)});')
            ret=rest[:rest.index(name)].strip()
            natives.append(f'        public static {ret} {f[:-3]}_{name}({params}) => throw new System.NotImplementedException("{f[:-3]}.{name}");')
            continue
        # a readonly member passing itself by ref to an extern (Unity 6: `GetLossyScale_Injected(ref this, …)` in a readonly
        # getter) is legal IL and not legal C#; Unsafe.AsRef gives the same reference without the readonly check
        ln=re.sub(r'\bref this\b','ref System.Runtime.CompilerServices.Unsafe.AsRef(in this)',ln)
        # strip [DefaultValue(..)] inline param attributes
        ln=re.sub(r'\[DefaultValue\([^\]]*\)\]\s*','',ln)
        out.append(ln)
    s='\n'.join(out)
    s=re.sub(r'^namespace ([\w.]+);', r'namespace \1 {', s, count=1, flags=re.M)+'\n}\n'
    open(os.path.join(dst,f),'w').write(s)


if len(sys.argv)>3:
    open(sys.argv[3],'w').write('using UnityEngine;\nnamespace MiniUnity {\n    public static partial class Native {\n'+'\n'.join(natives)+'\n    }\n}\n')
