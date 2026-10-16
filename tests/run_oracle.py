#!/usr/bin/env python3
"""Oracle tests: measurements made in the real Unity Editor and in MiniUnity (tests/oracle/*.cs, Oracle.Report(key, value, tol)).
    python3 MiniUnity/tests/run_oracle.py                       run in MiniUnity, compare with tests/oracle/expected.json
    python3 MiniUnity/tests/run_oracle.py --make-unity-project DIR
        writes a Unity 2022.3 project with the oracle tests; open it in Unity and run all PlayMode tests (Window ▸ General ▸
        Test Runner), or:  Unity -batchmode -projectPath DIR -runTests -testPlatform PlayMode -testResults DIR/results.xml
    python3 MiniUnity/tests/run_oracle.py --import DIR          DIR/OracleResults.json (written by the tests) → expected.json
Exit 1 when a value is outside its tolerance. Keys Unity has not measured yet are listed as "no reference"."""
import json, os, shutil, subprocess, sys, tempfile
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import run_fixtures as rf

ORACLE = os.path.join(rf.HERE, "oracle")
EXPECTED = os.path.join(ORACLE, "expected.json")


def write_tests(proj):
    d = os.path.join(proj, "Assets/Oracle"); os.makedirs(d, exist_ok=True)
    for f in os.listdir(ORACLE):
        if f.endswith(".cs"): shutil.copy2(os.path.join(ORACLE, f), d)
    json.dump({"name": "Oracle", "references": ["UnityEngine.TestRunner", "UnityEditor.TestRunner"], "optionalUnityReferences": ["TestAssemblies"]},
              open(os.path.join(d, "Oracle.asmdef"), "w"))


def make_unity_project(dst):
    os.makedirs(dst, exist_ok=True)
    shutil.copytree(os.path.join(rf.REPO, "ProjectSettings"), os.path.join(dst, "ProjectSettings"), dirs_exist_ok=True)
    os.makedirs(os.path.join(dst, "Packages"), exist_ok=True)
    json.dump({"dependencies": {"com.unity.test-framework": "1.1.33", "com.unity.modules.physics": "1.0.0", "com.unity.modules.physics2d": "1.0.0",
                                "com.unity.modules.animation": "1.0.0", "com.unity.modules.audio": "1.0.0"}},
              open(os.path.join(dst, "Packages/manifest.json"), "w"), indent=2)
    write_tests(dst)
    print(f"Unity project written to {dst}. Open it with Unity {open(os.path.join(rf.REPO, 'ProjectSettings/ProjectVersion.txt')).read().split()[1]},"
          f" run every PlayMode test, then: python3 {os.path.relpath(__file__)} --import {dst}")


def import_results(src):
    r = json.load(open(os.path.join(src, "OracleResults.json")))
    if "MiniUnity" in r.get("engine", ""): sys.exit("these results come from MiniUnity, not Unity")
    json.dump(r, open(EXPECTED, "w"), indent=2, sort_keys=True)
    print(f"{len(r['values'])} reference values from {r['engine']} → {os.path.relpath(EXPECTED)}")


def run():
    proj = tempfile.mkdtemp(prefix="mu_oracle_")
    shutil.copytree(os.path.join(rf.REPO, "ProjectSettings"), os.path.join(proj, "ProjectSettings"))
    for pk in rf.PKGS: shutil.copytree(os.path.join(rf.REPO, "Library/PackageCache", pk), os.path.join(proj, "Library/PackageCache", pk))
    os.makedirs(os.path.join(proj, "Packages"))
    json.dump({"dependencies": {"com.unity.test-framework": "1.1.33"}}, open(os.path.join(proj, "Packages/manifest.json"), "w"))
    json.dump({"dependencies": {
        "com.unity.test-framework": {"version": "1.1.33", "depth": 0, "source": "registry", "dependencies": {"com.unity.ext.nunit": "1.0.6"}, "url": "https://packages.unity.com"},
        "com.unity.ext.nunit": {"version": "1.0.6", "depth": 1, "source": "registry", "dependencies": {}, "url": "https://packages.unity.com"}}},
        open(os.path.join(proj, "Packages/packages-lock.json"), "w"))
    write_tests(proj)
    r = rf.ua_test(proj)
    if r.get("compileErrors"): sys.exit(f"compile errors: {r['compileErrors']}")
    for t in r.get("tests", []):
        if t["result"] != "Passed": print(f"  test {t['name']}: {t['result']} {str(t.get('message', ''))[:200]}")
    ours = json.load(open(os.path.join(proj, "OracleResults.json")))["values"]
    ref = json.load(open(EXPECTED)) if os.path.exists(EXPECTED) else {"engine": None, "values": {}}
    bad, missing = 0, 0
    print(f"reference: {ref.get('engine') or 'none yet (run --make-unity-project, then --import)'}")
    for k in sorted(set(ours) | set(ref["values"])):
        o, u = ours.get(k), ref["values"].get(k)
        if u is None: missing += 1; print(f"  ?    {k}: MiniUnity {o['value']:.6g} (no reference)"); continue
        if o is None: bad += 1; print(f"  FAIL {k}: not measured by MiniUnity"); continue
        tol = max(u["tolerance"], o["tolerance"]); diff = abs(o["value"] - u["value"])
        ok = diff <= tol + 1e-9
        bad += not ok
        print(f"  {'ok  ' if ok else 'FAIL'} {k}: Unity {u['value']:.6g} · MiniUnity {o['value']:.6g} · |Δ| {diff:.3g} (tol {tol:g})")
    print(f"{'PASS' if not bad else 'FAILED'}: {len(ours) - bad - missing} within tolerance, {bad} outside, {missing} without a Unity reference · {proj}")
    if not bad: shutil.rmtree(proj)
    return 1 if bad else 0


if __name__ == "__main__":
    a = sys.argv[1:]
    if "--make-unity-project" in a: make_unity_project(a[a.index("--make-unity-project") + 1])
    elif "--import" in a: import_results(a[a.index("--import") + 1])
    else: sys.exit(run())
