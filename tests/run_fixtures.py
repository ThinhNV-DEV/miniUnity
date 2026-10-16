#!/usr/bin/env python3
"""Runtime fixture tests (tests/fixtures/*Tests.cs: physics 3D/2D, joints, wheels, articulations, Animator, humanoid IK, audio,
mixer, input, particles, profiler) in a throw-away Unity project, through `ua test`.
    python3 MiniUnity/tests/run_fixtures.py [--keep] [--filter text]
Needs a MiniUnity build, a Unity Editor (reference assemblies) and the repo's Library/PackageCache (test framework + NUnit, Timeline; the
RoboHero sample of com.unity.2d.pixel-perfect for one Animator test). A fixture whose header has `// Run with: ua test --filter X
--input "…"` is run again on its own with that input (it is skipped in the first pass, which has no scripted input)."""
import glob, json, os, re, shutil, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
MU = os.path.dirname(HERE)
REPO = os.path.dirname(MU)
FIX = os.path.join(HERE, "fixtures")
DLL = os.path.join(MU, "Engine/bin/Release/net8.0/MiniUnity.dll")
DOTNET = os.path.join(MU, ".cache/dotnet/dotnet") if os.path.exists(os.path.join(MU, ".cache/dotnet/dotnet")) else "dotnet"
PKGS = ["com.unity.test-framework@1.1.33", "com.unity.ext.nunit@1.0.6", "com.unity.timeline@1.7.1"]


def make_project():
    proj = tempfile.mkdtemp(prefix="mu_fixtures_")
    shutil.copytree(os.path.join(REPO, "ProjectSettings"), os.path.join(proj, "ProjectSettings"))
    for pk in PKGS:
        src = os.path.join(REPO, "Library/PackageCache", pk)
        if not os.path.isdir(src):
            sys.exit(f"missing {src}: open the project in Unity once or run MiniUnity/ua.sh pkg.resolve --fetch")
        shutil.copytree(src, os.path.join(proj, "Library/PackageCache", pk))
    os.makedirs(os.path.join(proj, "Packages"))
    json.dump({"dependencies": {"com.unity.test-framework": "1.1.33", "com.unity.timeline": "1.7.1"}}, open(os.path.join(proj, "Packages/manifest.json"), "w"))
    json.dump({"dependencies": {
        "com.unity.test-framework": {"version": "1.1.33", "depth": 0, "source": "registry", "dependencies": {"com.unity.ext.nunit": "1.0.6"}, "url": "https://packages.unity.com"},
        "com.unity.ext.nunit": {"version": "1.0.6", "depth": 1, "source": "registry", "dependencies": {}, "url": "https://packages.unity.com"},
        "com.unity.timeline": {"version": "1.7.1", "depth": 0, "source": "registry", "dependencies": {}, "url": "https://packages.unity.com"}}},
        open(os.path.join(proj, "Packages/packages-lock.json"), "w"))
    tests = os.path.join(proj, "Assets/PlayTests")
    os.makedirs(tests)
    editor_tests = os.path.join(proj, "Assets/EditorTests")
    os.makedirs(editor_tests)
    for f in os.listdir(FIX):
        # *EditorTests.cs use UnityEditor (AssetDatabase, PrefabUtility): an Editor-only test assembly, as in a Unity project
        if f.endswith(".cs"): shutil.copy2(os.path.join(FIX, f), editor_tests if f.endswith("EditorTests.cs") else tests)
    json.dump({"name": "Fixtures", "references": ["UnityEngine.TestRunner", "UnityEditor.TestRunner", "Unity.Timeline"], "optionalUnityReferences": ["TestAssemblies"]},
              open(os.path.join(tests, "Fixtures.asmdef"), "w"))
    json.dump({"name": "Fixtures.Editor", "references": ["UnityEngine.TestRunner", "UnityEditor.TestRunner"], "includePlatforms": ["Editor"], "optionalUnityReferences": ["TestAssemblies"]},
              open(os.path.join(editor_tests, "Fixtures.Editor.asmdef"), "w"))
    # stand-in for TMP's SDF shader (found by name: world-space text draws with MiniUnity's TMP shader)
    sd = os.path.join(proj, "Assets/Shaders"); os.makedirs(sd)
    open(os.path.join(sd, "TmpSdfStub.shader"), "w").write('Shader "TextMeshPro/Mobile/Distance Field" { SubShader { Pass { } } }\n')
    open(os.path.join(sd, "TmpSdfStub.shader.meta"), "w").write("fileFormatVersion: 2\nguid: 5d7a3c1e9b2f4a6c8e0d1f3a5b7c9e01\nShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n  nonModifiableTextures: []\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
    # a "model" whose import result is Unity's own unity_builtin_extra (a real binary SerializedFile, Editor/Data/Resources),
    # linked in as its Library/Artifacts entry; the .meta names some of its objects (ImportArtifactTests)
    extra = next(iter(sorted(glob.glob(os.path.join(MU, ".cache/unity/*/Editor/Data/Resources/unity_builtin_extra")))), None)
    if extra:
        os.makedirs(os.path.join(proj, "Library/Artifacts/5e"), exist_ok=True)
        os.symlink(extra, os.path.join(proj, "Library/Artifacts/5e/5e1f0c3a9b7d4e2f8a6c0b1d3e5f7a90"))
        md = os.path.join(proj, "Assets/Models"); os.makedirs(md)
        open(os.path.join(md, "Builtin.fbx"), "wb").write(b"Kaydara FBX Binary  \x00")
        names = [(21, 10303, "Default-Material"), (21, 10304, "Default-Skybox"), (28, 10904, "UISprite")]
        table = "".join(f"  - first:\n      {c}: {i}\n    second: {n}\n" for c, i, n in names)
        open(os.path.join(md, "Builtin.fbx.meta"), "w").write("fileFormatVersion: 2\nguid: 5e1f0c3a9b7d4e2f8a6c0b1d3e5f7a91\nModelImporter:\n  serializedVersion: 22200\n  internalIDToNameTable:\n" + table)
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
