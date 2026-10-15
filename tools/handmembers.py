# Extract member names declared in hand-written partial classes → stub-skip config lines.
import re,sys,os,glob
root=sys.argv[1]
out={}
for f in glob.glob(root+'/**/*.cs',recursive=True):
    src=open(f,encoding='utf-8').read()
    ns=None
    # crude: track namespace blocks
    for m in re.finditer(r'namespace\s+([\w.]+)\s*\{',src):
        pass
    # find "partial class|struct NAME" with brace matching
    for m in re.finditer(r'\bpartial\s+(?:class|struct)\s+(\w+)',src):
        name=m.group(1)
        # namespace = last namespace decl before m
        nss=[x for x in re.finditer(r'namespace\s+([\w.]+)\s*\{',src) if x.start()<m.start()]
        nsn=nss[-1].group(1) if nss else ''
        i=src.index('{',m.end())
        depth=0; j=i
        while True:
            c=src[j]
            if c=='{': depth+=1
            elif c=='}':
                depth-=1
                if depth==0: break
            j+=1
        body=src[i+1:j]
        names=set()
        # only depth-1 lines
        d=0; line=''
        for ch in body:
            if ch=='{':
                if d==0: line+='{'
                d+=1; continue
            if ch=='}':
                d-=1; continue
            if d==0: line+=ch
        for decl in re.split(r'[;\n]',line):
            decl=decl.strip()
            if not re.match(r'(public|internal|protected|private|static)',decl): continue
            mm=re.search(r'(?:operator\s*\S+|(\w+)\s*(?:<[^>]*>)?\s*(?:\(|\{|=>|=|$))',decl)
            if decl.find('operator')>=0:
                if 'implicit' in decl or 'explicit' in decl: names.add('op_Implicit'); names.add('op_Explicit')
                else:
                    ops={'==':'op_Equality','!=':'op_Inequality','<=':'op_LessThanOrEqual','>=':'op_GreaterThanOrEqual','<':'op_LessThan','>':'op_GreaterThan',
                         '+':'op_Addition','-':'op_Subtraction','*':'op_Multiply','/':'op_Division','%':'op_Modulus','!':'op_LogicalNot','~':'op_OnesComplement',
                         '&':'op_BitwiseAnd','|':'op_BitwiseOr','^':'op_ExclusiveOr','true':'op_True','false':'op_False','++':'op_Increment','--':'op_Decrement'}
                    om=re.search(r'operator\s*(==|!=|<=|>=|\+\+|--|true|false|[<>+\-*/%!~&|^])',decl)
                    if om: names.add(ops[om.group(1)])
                continue
            if mm and mm.group(1):
                n=mm.group(1)
                if n==name: n='.ctor'
                if n in ('get','set','add','remove','new','this'): continue
                names.add(n)
            if re.search(r'\bthis\s*\[',decl): names.add('this[]')
        key=(nsn+'.'+name) if nsn else name
        out.setdefault(key,set()).update(names)
for k,v in sorted(out.items()):
    print(k+': '+', '.join(sorted(v)))
