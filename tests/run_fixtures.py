#!/usr/bin/env python3
"""Runtime fixture tests (tests/fixtures/*Tests.cs: physics 3D/2D, joints, wheels, articulations, Animator, humanoid IK, audio,
mixer, input, particles, profiler) in a throw-away Unity project, through `ua test`.
    python3 MiniUnity/tests/run_fixtures.py [--keep] [--filter text]
Needs a MiniUnity build, a Unity Editor (reference assemblies) and the package sources setup.sh fetches into .cache/packages
(test framework + NUnit, Timeline, uGUI, PostProcessing; a host project's Library/PackageCache works too; the
RoboHero sample of com.unity.2d.pixel-perfect for one Animator test). A fixture whose header has `// Run with: ua test --filter X
--input "…"` is run again on its own with that input (it is skipped in the first pass, which has no scripted input)."""
import glob, json, os, re, shutil, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
MU = os.path.dirname(HERE)
REPO = os.path.dirname(MU)
FIX = os.path.join(HERE, "fixtures")
DATA = os.path.join(HERE, "data")
DLL = os.path.join(MU, "Engine/bin/Release/net8.0/MiniUnity.dll")
DOTNET = os.path.join(MU, ".cache/dotnet/dotnet") if os.path.exists(os.path.join(MU, ".cache/dotnet/dotnet")) else "dotnet"
PKGS = [("com.unity.test-framework", "1.1.33"), ("com.unity.ext.nunit", "1.0.6"), ("com.unity.timeline", "1.7.7"), ("com.unity.ugui", "1.0.0"), ("com.unity.postprocessing", "3.4.0")]


def package(name, version):
    """(source folder, version) of a package: setup.sh's .cache/packages (its own version), else a host project's
    Library/PackageCache at the given version."""
    c = os.path.join(MU, ".cache/packages", name)
    if os.path.isdir(c):
        v = open(os.path.join(c, ".version")).read().strip() if os.path.exists(os.path.join(c, ".version")) else version
        return c, v
    c = os.path.join(REPO, "Library/PackageCache", f"{name}@{version}")
    if os.path.isdir(c): return c, version
    sys.exit(f"missing package {name}: run MiniUnity/setup.sh (it fetches the test framework, NUnit, Timeline, uGUI and PostProcessing into .cache/packages)")


def make_project():
    proj = tempfile.mkdtemp(prefix="mu_fixtures_")
    # the fixtures' own ProjectSettings (tests/data): linear colour space, quality "Ultra" (2x MSAA), default tags / layers
    shutil.copytree(os.path.join(DATA, "ProjectSettings"), os.path.join(proj, "ProjectSettings"))
    vers = {}
    for name, version in PKGS:
        src, v = package(name, version)
        vers[name] = v
        shutil.copytree(src, os.path.join(proj, "Library/PackageCache", f"{name}@{v}"))
    os.makedirs(os.path.join(proj, "Packages"))
    tf, nu, tl, ui, pp = vers["com.unity.test-framework"], vers["com.unity.ext.nunit"], vers["com.unity.timeline"], vers["com.unity.ugui"], vers["com.unity.postprocessing"]
    json.dump({"dependencies": {"com.unity.test-framework": tf, "com.unity.timeline": tl, "com.unity.ugui": ui, "com.unity.postprocessing": pp}}, open(os.path.join(proj, "Packages/manifest.json"), "w"))
    json.dump({"dependencies": {
        "com.unity.test-framework": {"version": tf, "depth": 0, "source": "registry", "dependencies": {"com.unity.ext.nunit": nu}, "url": "https://packages.unity.com"},
        "com.unity.ext.nunit": {"version": nu, "depth": 1, "source": "registry", "dependencies": {}, "url": "https://packages.unity.com"},
        "com.unity.timeline": {"version": tl, "depth": 0, "source": "registry", "dependencies": {}, "url": "https://packages.unity.com"},
        "com.unity.ugui": {"version": ui, "depth": 0, "source": "builtin", "dependencies": {}},
        "com.unity.postprocessing": {"version": pp, "depth": 0, "source": "registry", "dependencies": {}, "url": "https://packages.unity.com"}}},
        open(os.path.join(proj, "Packages/packages-lock.json"), "w"))
    tests = os.path.join(proj, "Assets/PlayTests")
    os.makedirs(tests)
    editor_tests = os.path.join(proj, "Assets/EditorTests")
    os.makedirs(editor_tests)
    for f in os.listdir(FIX):
        # *EditorTests.cs use UnityEditor (AssetDatabase, PrefabUtility): an Editor-only test assembly, as in a Unity project
        if f.endswith(".cs") or f.endswith(".cs.meta"):   # a script some test refers to by guid ships its .meta
            shutil.copy2(os.path.join(FIX, f), editor_tests if f.split(".")[0].endswith("EditorTests") else tests)
    json.dump({"name": "Fixtures", "references": ["UnityEngine.TestRunner", "UnityEditor.TestRunner", "Unity.Timeline", "UnityEngine.UI", "Unity.Postprocessing.Runtime"], "optionalUnityReferences": ["TestAssemblies"]},
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
    # models read from the FBX file (FbxImportEditorTests): an ASCII fixture and, when the Editor is there, a binary sample
    shutil.copytree(os.path.join(FIX, "Models"), os.path.join(proj, "Assets/Models"), dirs_exist_ok=True)
    sphere = next(iter(glob.glob(os.path.join(MU, ".cache/unity/*/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.universal/Samples~/URPPackageSamples/Shaders/Lit/Models/UVSphere.fbx"))), None)
    if sphere: shutil.copy2(sphere, os.path.join(proj, "Assets/Models/UVSphere.fbx"))
    # scenes of the fixture Build Settings (tests/data/ProjectSettings/EditorBuildSettings.asset): SceneLoadTests
    shutil.copytree(os.path.join(FIX, "Scenes"), os.path.join(proj, "Assets/Scenes"))
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
    if not robo: print("  note: RoboHero sample not found (com.unity.2d.pixel-perfect in a host project's Library/PackageCache); its Animator test is skipped")
    print(f"{'PASS' if not failed else 'FAILED'}: {len(tests) - len(failed) - len(skipped)} passed, {len(failed)} failed, {len(skipped)} skipped · {proj}")
    if not keep and not failed: shutil.rmtree(proj)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
