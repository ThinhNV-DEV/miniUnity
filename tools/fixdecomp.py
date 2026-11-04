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
        if st.startswith('using ') and any(x in st for x in ['Unity.IL2CPP','UnityEngine.Bindings','UnityEngine.Scripting','UnityEngineInternal','UnityEngine.Internal']): continue
        if st.startswith('[') and st.endswith(']') and not re.match(r'\[(StructLayout|FieldOffset|Serializable|Obsolete|Flags|DefaultMember|AttributeUsage|SerializeField|NonSerialized|EditorBrowsable)\b', st):
            continue
        ln=re.sub(r'\[DefaultValue\([^\]]*\)\]\s*','',ln)
        m=re.match(r'^(\s*)(.*?)\bextern\s+(.*\));\s*$',ln)
        if m:
            ind,pre,rest=m.groups()
            name=re.search(r'(\w+)\s*\(',rest).group(1)
            args=argnames(rest); params=rest[rest.index("(")+1:rest.rindex(")")]
            if not re.search(r'\bstatic\b',pre):
                # instance extern (Unity 6 binds struct methods directly, 2022.3 went through static X_Injected(ref this)):
                # the redirect takes the instance as its first parameter
                tn=f[:-3]; isstruct=re.search(r'\bstruct\s+'+tn+r'\b',s) is not None
                args=[('in this' if isstruct else 'this')]+args
                params=(f'in {tn} _unity_self' if isstruct else f'{tn} _unity_self')+(', '+params if params.strip() else '')
            out.append(f'{ind}{pre}{rest} => MiniUnity.Native.{f[:-3]}_{name}({", ".join(args)});')
            ret=rest[:rest.index(name)].strip()
            natives.append(f'        public static {ret} {f[:-3]}_{name}({params}) => throw new System.NotImplementedException("{f[:-3]}.{name}");')
            continue
        # Unity 6 passes `ref this` to _Injected helpers from readonly members (CS1605 in C#): same reference, without the check
        ln=ln.replace('(ref this,','(ref System.Runtime.CompilerServices.Unsafe.AsRef(in this),').replace('(ref this)','(ref System.Runtime.CompilerServices.Unsafe.AsRef(in this))')
        # strip [DefaultValue(..)] inline param attributes
        ln=re.sub(r'\[DefaultValue\([^\]]*\)\]\s*','',ln)
        out.append(ln)
    s='\n'.join(out)
    s=re.sub(r'^namespace ([\w.]+);', r'namespace \1 {', s, count=1, flags=re.M)+'\n}\n'
    open(os.path.join(dst,f),'w').write(s)


if len(sys.argv)>3:
    open(sys.argv[3],'w').write('using UnityEngine;\nnamespace MiniUnity {\n    public static partial class Native {\n'+'\n'.join(natives)+'\n    }\n}\n')
