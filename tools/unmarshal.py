#!/usr/bin/env python3
"""Unity 6 binding glue in decompiled sources → plain MiniUnity.Native redirects.

Unity 6 marshals strings / spans to native code through UnityEngine.Bindings.ManagedSpanWrapper (StringMarshaller,
OutStringMarshaller, `<Name>_Injected(ref ManagedSpanWrapper …)`), where 2022.3 called `extern string Name(…)` directly.
fixdecomp.py redirects the `_Injected` externs, but the glue around them only works with Unity's native side. For every method
whose body uses ManagedSpanWrapper, replace the body with the redirect MiniUnity.Native.<Type>_<Method>(args) — the same name
the 2022.3 decompile produced, so the hand-written implementations in Engine/Core/NativeMath.cs keep working — and drop the
`_Injected` helpers that take a ManagedSpanWrapper.

  unmarshal.py <dir>   rewrites every *.cs in <dir> in place
"""
import glob
import os
import re
import sys

HEADER = re.compile(r"^(?P<indent>\t*)(?P<mods>(?:(?:public|internal|private|protected|static|unsafe|extern)\s+)+)"
                    r"(?P<ret>[\w.<>\[\],]+)\s+(?P<name>\w+)\((?P<params>[^)]*)\)\s*\n(?P=indent)\{", re.M)


def body_end(src, open_brace):
    depth = 0
    for i in range(open_brace, len(src)):
        if src[i] == "{":
            depth += 1
        elif src[i] == "}":
            depth -= 1
            if depth == 0:
                return i
    raise ValueError("unbalanced braces")


def rewrite(src, type_name):
    out, pos = [], 0
    for m in HEADER.finditer(src):
        if m.start() < pos:
            continue
        brace = m.end() - 1
        end = body_end(src, brace)
        if "ManagedSpanWrapper" not in src[brace:end]:
            continue
        args = ", ".join(p.split()[-1] for p in m.group("params").split(",") if p.strip())
        mods = m.group("mods").replace("unsafe ", "")
        call = f"MiniUnity.Native.{type_name}_{m.group('name')}({args})"
        stmt = f"{call};" if m.group("ret") == "void" else f"return {call};"
        out.append(src[pos:m.start()])
        out.append(f"{m.group('indent')}{mods}{m.group('ret')} {m.group('name')}({m.group('params')})\n"
                   f"{m.group('indent')}{{\n{m.group('indent')}\t{stmt}\n{m.group('indent')}}}")
        pos = end + 1
    out.append(src[pos:])
    src = "".join(out)
    # one-line `_Injected(... ManagedSpanWrapper ...) => MiniUnity.Native...;` helpers are now unused
    return re.sub(r"^\t*[^\n]*\b\w+_Injected\([^)]*ManagedSpanWrapper[^)]*\)[^\n]*;\n\n?", "", src, flags=re.M)


if __name__ == "__main__":
    for f in glob.glob(os.path.join(sys.argv[1], "*.cs")):
        src = open(f, encoding="utf-8").read()
        if "ManagedSpanWrapper" not in src:
            continue
        new = rewrite(src, os.path.splitext(os.path.basename(f))[0])
        if new != src:
            open(f, "w", encoding="utf-8").write(new)
