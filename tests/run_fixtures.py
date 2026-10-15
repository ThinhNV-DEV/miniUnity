#!/usr/bin/env python3
"""Runtime fixture tests (tests/fixtures/*Tests.cs: physics 3D/2D, joints, wheels, articulations, Animator, humanoid IK, audio,
mixer, input, particles, profiler) in a throw-away Unity project, through `ua test`.
    python3 MiniUnity/tests/run_fixtures.py [--keep] [--filter text]
Needs a MiniUnity build, a Unity Editor (reference assemblies) and the repo's Library/PackageCache (test framework + NUnit; the
RoboHero sample of com.unity.2d.pixel-perfect for one Animator test). A fixture whose header has `// Run with: ua test --filter X
--input "…"` is run again on its own with that input (it is skipped in the first pass, which has no scripted input)."""
import json, os, re, shutil, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
MU = os.path.dirname(HERE)
REPO = os.path.dirname(MU)
FIX = os.path.join(HERE, "fixtures")
DLL = os.path.join(MU, "Engine/bin/Release/net8.0/MiniUnity.dll")
DOTNET = os.path.join(MU, ".cache/dotnet/dotnet") if os.path.exists(os.path.join(MU, ".cache/dotnet/dotnet")) else "dotnet"
PKGS = ["com.unity.test-framework@1.1.33", "com.unity.ext.nunit@1.0.6"]


def make_project():
    proj = tempfile.mkdtemp(prefix="mu_fixtures_")
    shutil.copytree(os.path.join(REPO, "ProjectSettings"), os.path.join(proj, "ProjectSettings"))
    for pk in PKGS:
        src = os.path.join(REPO, "Library/PackageCache", pk)
        if not os.path.isdir(src):
            sys.exit(f"missing {src}: open the project in Unity once or run MiniUnity/ua.sh pkg.resolve --fetch")
        shutil.copytree(src, os.path.join(proj, "Library/PackageCache", pk))
    os.makedirs(os.path.join(proj, "Packages"))
    json.dump({"dependencies": {"com.unity.test-framework": "1.1.33"}}, open(os.path.join(proj, "Packages/manifest.json"), "w"))
    json.dump({"dependencies": {
        "com.unity.test-framework": {"version": "1.1.33", "depth": 0, "source": "registry", "dependencies": {"com.unity.ext.nunit": "1.0.6"}, "url": "https://packages.unity.com"},
        "com.unity.ext.nunit": {"version": "1.0.6", "depth": 1, "source": "registry", "dependencies": {}, "url": "https://packages.unity.com"}}},
        open(os.path.join(proj, "Packages/packages-lock.json"), "w"))
    tests = os.path.join(proj, "Assets/PlayTests")
    os.makedirs(tests)
    editor_tests = os.path.join(proj, "Assets/EditorTests")
    os.makedirs(editor_tests)
    for f in os.listdir(FIX):
        # *EditorTests.cs use UnityEditor (AssetDatabase, PrefabUtility): an Editor-only test assembly, as in a Unity project
        if f.endswith(".cs"): shutil.copy2(os.path.join(FIX, f), editor_tests if f.endswith("EditorTests.cs") else tests)
    json.dump({"name": "Fixtures", "references": ["UnityEngine.TestRunner", "UnityEditor.TestRunner"], "optionalUnityReferences": ["TestAssemblies"]},
              open(os.path.join(tests, "Fixtures.asmdef"), "w"))
    json.dump({"name": "Fixtures.Editor", "references": ["UnityEngine.TestRunner", "UnityEditor.TestRunner"], "includePlatforms": ["Editor"], "optionalUnityReferences": ["TestAssemblies"]},
              open(os.path.join(editor_tests, "Fixtures.Editor.asmdef"), "w"))
    for script in ("animassets.py", "audioassets.py"):
        subprocess.run([sys.executable, os.path.join(FIX, script), proj], check=True, capture_output=True)
    robo = next((os.path.join(d, "RoboHero") for d, dirs, _ in os.walk(os.path.join(REPO, "Library/PackageCache")) if "RoboHero" in dirs and "pixel-perfect" in d), None)
    if robo:
        for sub in ("Animations", "Textures"):
            shutil.copytree(os.path.join(robo, sub), os.path.join(proj, "Assets/RoboHero", sub))
    return proj, robo is not None


def ua_test(proj, *args):
    p = subprocess.run([DOTNET, DLL, "ua", proj, "test", *args], capture_output=True, text=True)
    try: return json.loads(p.stdout)
    except json.JSONDecodeError: sys.exit("ua test did not return JSON:\n" + p.stdout[-2000:] + p.stderr[-2000:])


def main():
    keep = "--keep" in sys.argv
    flt = sys.argv[sys.argv.index("--filter") + 1] if "--filter" in sys.argv else None
    proj, robo = make_project()
    r = ua_test(proj, *(["--filter", flt] if flt else []))
    if not r.get("ok"): sys.exit(f"ua test failed: {r.get('error')}")
    if r.get("compileErrors"): sys.exit(f"compile errors in {r['compileErrors']}")
    if not r.get("tests"): sys.exit("no tests ran (is a Unity Editor available for the reference assemblies?)")
    tests = {t["name"]: t for t in r.get("tests", [])}
    # fixtures that need scripted input: run them again with the spec from their header
    for f in sorted(os.listdir(FIX)):
        m = re.search(r'Run with: ua test --filter (\S+) --input "([^"]+)"', open(os.path.join(FIX, f)).read()) if f.endswith(".cs") else None
        if m and (flt is None or flt in m.group(1)):
            for t in ua_test(proj, "--filter", m.group(1), "--input", m.group(2)).get("tests", []): tests[t["name"]] = t
    failed = [t for t in tests.values() if t["result"] == "Failed"]
    skipped = [t for t in tests.values() if t["result"] not in ("Passed", "Failed")]
    for t in failed: print(f"  FAIL {t['name']}: {str(t.get('message', ''))[:300]}")
    for t in skipped: print(f"  skip {t['name']}: {str(t.get('message', ''))[:120]}")
    if not robo: print("  note: RoboHero sample not found (com.unity.2d.pixel-perfect in Library/PackageCache); its Animator test fails")
    print(f"{'PASS' if not failed else 'FAILED'}: {len(tests) - len(failed) - len(skipped)} passed, {len(failed)} failed, {len(skipped)} skipped · {proj}")
    if not keep and not failed: shutil.rmtree(proj)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
