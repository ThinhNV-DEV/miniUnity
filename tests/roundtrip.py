#!/usr/bin/env python3
"""Lossless YAML check: every Unity YAML file tracked by git is parsed and re-emitted (`mu rt`) and must come back byte for byte.
    python3 MiniUnity/tests/roundtrip.py [path-prefix]"""
import os, subprocess, sys, tempfile

MU = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.dirname(MU)
DLL = os.path.join(MU, "Engine/bin/Release/net8.0/MiniUnity.dll")
DOTNET = os.path.join(MU, ".cache/dotnet/dotnet") if os.path.exists(os.path.join(MU, ".cache/dotnet/dotnet")) else "dotnet"

files = [f for f in subprocess.run(["git", "ls-files", "-z"], cwd=REPO, capture_output=True, text=True).stdout.split("\0")
         if f and (len(sys.argv) < 2 or f.startswith(sys.argv[1]))]
yaml = []
for f in files:
    p = os.path.join(REPO, f)
    try:
        with open(p, "rb") as fh:
            if fh.read(5) == b"%YAML": yaml.append(f)
    except OSError: pass
out = os.path.join(tempfile.mkdtemp(prefix="mu_rt_"), "out.yaml")
bad = []
for f in yaml:
    r = subprocess.run([DOTNET, DLL, "rt", os.path.join(REPO, f), out], capture_output=True, text=True)
    if r.returncode != 0 or open(os.path.join(REPO, f), "rb").read() != open(out, "rb").read(): bad.append(f)
for f in bad[:20]: print("  DIFF", f)
print(f"{'PASS' if not bad else 'FAILED'}: {len(yaml) - len(bad)}/{len(yaml)} YAML files re-emitted byte for byte")
sys.exit(1 if bad else 0)
