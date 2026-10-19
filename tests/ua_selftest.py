#!/usr/bin/env python3
"""Self-test for `mu ua` (agent commands): builds a throw-away Unity project from pieces of this repo, runs a scripted
session of edits, and checks every result plus the integrity of the files written (`mu validate`).
    python3 MiniUnity/tests/ua_selftest.py            (needs a MiniUnity build: MiniUnity/build.sh)
"""
import json, os, shutil, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
MU = os.path.dirname(HERE)
REPO = os.path.dirname(MU)
DATA = os.path.join(HERE, "data")   # ProjectSettings + a few assets: the selftest needs no host project


def pkg_source(name, version):
    """Package sources: setup.sh's .cache/packages, else a host project's Library/PackageCache (None when neither has it)."""
    c = os.path.join(MU, ".cache/packages", name)
    if os.path.isdir(c): return c
    c = os.path.join(REPO, "Library/PackageCache", f"{name}@{version}")
    return c if os.path.isdir(c) else None
DLL = os.path.join(MU, "Engine/bin/Release/net8.0/MiniUnity.dll")
DOTNET = os.path.join(MU, ".cache/dotnet/dotnet") if os.path.exists(os.path.join(MU, ".cache/dotnet/dotnet")) else "dotnet"

fails = 0
def check(cond, msg):
    global fails
    print(("  ok   " if cond else "  FAIL ") + msg)
    if not cond: fails += 1

def copy(rel, proj):
    src, dst = os.path.join(DATA, rel), os.path.join(proj, rel)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    if os.path.isdir(src): shutil.copytree(src, dst)
    else: shutil.copy2(src, dst)
    if os.path.exists(src + ".meta"): shutil.copy2(src + ".meta", dst + ".meta")

def main():
    proj = tempfile.mkdtemp(prefix="ua_selftest_")
    for rel in ["ProjectSettings", "Packages/manifest.json", "Assets/VideoRecreate/Prefabs/Characters3D", "Assets/VideoRecreate/Art/Generated/Materials",
                "Assets/VideoRecreate/Art/Generated/Meshes", "Assets/VideoRecreate/Scripts/Components/PhysicsStepSim.cs", "Assets/VideoRecreate/Shaders"]:
        copy(rel, proj)
    for folder in ["Assets/VideoRecreate", "Assets/VideoRecreate/Prefabs", "Assets/VideoRecreate/Art", "Assets/VideoRecreate/Art/Generated",
                   "Assets/VideoRecreate/Scripts", "Assets/VideoRecreate/Scripts/Components"]:
        shutil.copy2(os.path.join(DATA, folder + ".meta"), os.path.join(proj, folder + ".meta"))
    env = dict(os.environ, MINIUNITY_SCRIPTDIRS=os.environ.get("MINIUNITY_SCRIPTDIRS", ""))

    def ua(*args, expect_ok=True):
        p = subprocess.run([DOTNET, DLL, "ua", proj, *args], capture_output=True, text=True, env=env)
        try: r = json.loads(p.stdout)
        except Exception: print(p.stdout, p.stderr); raise
        if expect_ok and not r.get("ok"): print("   ", r.get("error"), r.get("stack", "")[:400])
        return r

    def validate(rel):
        p = subprocess.run([DOTNET, DLL, "validate", proj, os.path.join(proj, rel)], capture_output=True, text=True, env=env)
        return p.returncode == 0, (p.stdout + p.stderr).strip()

    S = "Assets/Test/Main.unity"
    print("scene + GameObjects")
    r = ua("scene.new", S, "--basic"); check(r["ok"], "scene.new --basic")
    t = ua("go.tree", S)
    check([n["name"] for n in t["roots"]] == ["Main Camera", "Directional Light"], "roots: Main Camera, Directional Light")
    r = ua("go.create", S, "Player", "--components", "BoxCollider,Rigidbody", "--position", '{"x":1,"y":2,"z":3}')
    check(r["ok"] and r["components"] == ["Transform", "BoxCollider", "Rigidbody"], "go.create Player with BoxCollider + Rigidbody")
    r = ua("comp.get", S, "Player", "Transform", "m_LocalPosition"); check(r["value"] == {"x": 1, "y": 2, "z": 3}, "position set")
    r = ua("comp.set", S, "Player", "Rigidbody", "m_Mass", "5"); check(r["ok"] and r["value"] == 5, "comp.set Rigidbody.m_Mass = 5")
    r = ua("go.create", S, "Child", "--parent", "Player"); check(r.get("path") == "Player/Child", "child under Player")
    r = ua("go.rename", S, "Player/Child", "Weapon"); check(r.get("path") == "Player/Weapon", "rename")
    r = ua("go.move", S, "Player/Weapon", "--root"); check(r.get("path") == "Weapon", "move to root")
    r = ua("go.move", S, "Weapon", "--parent", "Player"); check(r.get("path") == "Player/Weapon", "move back under Player")
    r = ua("go.set", S, "Player", "--tag", "Player", "--layer", "2"); check(r["ok"], "go.set tag / layer")
    r = ua("go.duplicate", S, "Player", "--name", "Player2"); check(r.get("path") == "Player2", "duplicate subtree")
    t = ua("go.tree", S); p2 = [n for n in t["roots"] if n["name"] == "Player2"][0]
    check(p2["children"][0]["name"] == "Weapon", "duplicate keeps children")
    r = ua("go.delete", S, "Player2"); check(r["ok"], "delete subtree")

    print("MonoBehaviour + references")
    r = ua("comp.add", S, "Player", "PhysicsStepSim")
    check(r["ok"] and r["type"] == "PhysicsStepSim" and r["fields"].get("fixedStep") == 0.02, "comp.add script with default fields from the compiled type")
    r = ua("comp.set", S, "Player", "PhysicsStepSim", "ball", '{"$ref":"go:Player/Weapon","$component":"Transform"}')
    check(r["ok"] and r["value"]["fileID"] != 0, "object reference to another object's Transform")
    r = ua("comp.set", S, "Player", "PhysicsStepSim", "segments", '[{"start":1,"speed":4},{"start":2}]')
    check(r["ok"] and len(r["value"]) == 2, "list of serializable classes")
    r = ua("comp.remove", S, "Player", "BoxCollider"); check(r["ok"], "comp.remove")
    r = ua("go.find", S, "--where", "Rigidbody.m_Mass>4; PhysicsStepSim.fixedStep==0.02"); check([m["path"] for m in r.get("matches", [])] == ["Player"], "go.find --where on serialized values")
    r = ua("go.find", S, "--where", "PhysicsStepSim.ball==null"); check(r["ok"] and r["matches"] == [], "go.find --where reference == null")
    r = ua("go.refs", S, "Player/Weapon"); check(r.get("count") == 1 and r["references"][0]["property"] == "ball" and r["references"][0]["target"] == "Transform", "go.refs finds the field that references the object")
    r = ua("comp.move", S, "Player", "PhysicsStepSim", "--index", "1"); check(r.get("components") == ["Transform", "PhysicsStepSim", "Rigidbody"], "comp.move reorders components")
    r = ua("comp.move", S, "Player", "Transform", "--index", "2", expect_ok=False); check(not r["ok"], "comp.move refuses to move the Transform")
    r = ua("go.find", S, "--name", "#bad", expect_ok=False); r2 = ua("go.get", S, "#notanid", expect_ok=False)
    check(not r2["ok"] and "bad object id" in r2["error"] and "stack" not in r2, "bad #id → clear error")

    print("prefab instances")
    P = "Assets/VideoRecreate/Prefabs/Characters3D/Bit3D.prefab"
    r = ua("prefab.instantiate", S, P, "--name", "Bit", "--position", '{"x":0,"y":0,"z":5}'); check(r.get("path") == "Bit", "instantiate prefab")
    r = ua("go.get", S, "Bit"); check(r.get("prefab") == P and r["transform"]["fields"]["m_LocalPosition"]["z"] == 5, "instance root reads overrides")
    r = ua("comp.set", S, "Bit/Body", "MeshRenderer", "m_Enabled", "0"); check(r["ok"] and r["value"] == 0, "override inside instance")
    r = ua("prefab.overrides", S, "Bit"); check(any(m["propertyPath"] == "m_Enabled" for m in r["modifications"]), "override listed")
    r = ua("comp.add", S, "Bit/Body", "BoxCollider"); check(r["ok"], "added component on instance object")
    r = ua("go.create", S, "Hat", "--parent", "Bit/Body"); check(r.get("path") == "Bit/Body/Hat", "added GameObject inside instance")
    r = ua("go.delete", S, "Bit/Belly"); check(r["ok"], "removed GameObject override")
    t = ua("go.tree", S, "--root", "Bit", "--depth", "1"); names = [c["name"] for c in t["roots"][0]["children"]]
    check("Belly" not in names and "Body" in names, "removed object hidden")
    r = ua("prefab.revert", S, "Bit/Body", "--component", "MeshRenderer"); check(r["reverted"] == 1, "revert one override")
    ok, msg = validate(S); check(ok, "scene validates after instance edits " + ("" if ok else msg[-300:]))

    print("prefab creation / unpack")
    r = ua("prefab.create", S, "Player", "Assets/Test/Player.prefab"); check(r["ok"] and r.get("instance"), "prefab.create replaces subtree with instance")
    t = ua("go.tree", "Assets/Test/Player.prefab"); check(t["roots"][0]["name"] == "Player" and t["roots"][0]["children"][0]["name"] == "Weapon", "prefab file holds the subtree")
    r = ua("go.get", S, "Player"); check(r.get("prefab") == "Assets/Test/Player.prefab", "scene object is now an instance")
    r = ua("prefab.unpack", S, "Bit"); check(r["ok"], "unpack instance")
    r = ua("go.get", S, "Bit"); check("prefab" not in r and any(c["type"] == "BoxCollider" for c in ua("comp.list", S, "Bit/Body")["components"]), "unpacked object keeps added component")
    ok, msg = validate(S); check(ok, "scene validates after unpack " + ("" if ok else msg[-300:]))

    print("assets / GUIDs / settings")
    r = ua("asset.move", "Assets/Test/Player.prefab", "Assets/Test/Prefabs/Player.prefab"); check(r["ok"], "asset.move keeps GUID")
    r = ua("asset.refs", "Assets/Test/Prefabs/Player.prefab"); check(any(x["file"] == S for x in r["referencedBy"]), "scene still references moved prefab")
    r = ua("asset.delete", "Assets/Test/Prefabs/Player.prefab", expect_ok=False); check(not r["ok"], "delete refused while referenced")
    r = ua("asset.missing", "--in", "Assets/Test"); check(r["count"] == 0, "no missing references")
    r = ua("asset.find", "--type", "prefab", "--in", "Assets/Test"); check(r["count"] == 1, "asset.find by type")
    r = ua("asset.labels", "Assets/Test/Prefabs/Player.prefab", "--add", "hero,char"); check(r["labels"] == ["hero", "char"], "labels")
    r = ua("asset.find", "--label", "hero"); check(r["count"] == 1, "find by label")
    open(os.path.join(proj, "Assets/Test/notes.txt"), "w").write("hi")
    r = ua("asset.import", "Assets/Test"); check(any(c["path"] == "Assets/Test/notes.txt" and c["importer"] == "TextScriptImporter" for c in r["created"]), "asset.import creates .meta")
    r = ua("meta.set", "Assets/Test/notes.txt", "TextScriptImporter.userData", '"x"'); check(r["ok"], "meta.set importer field")
    r = ua("settings.get", "TagManager", "tags"); check(r["ok"], "settings.get")
    r = ua("tags.add", "Enemy"); r = ua("settings.get", "TagManager", "tags"); check("Enemy" in r["value"], "tags.add")
    r = ua("settings.set", "PlayerSettings", "productName", '"UA Test"'); check(r["ok"] and r["value"] == "UA Test", "settings.set PlayerSettings.productName")
    r = ua("buildscenes.set", S); r = ua("scene.list"); check(any(s["path"] == S and s["buildIndex"] == 0 for s in r["scenes"]), "build scene list")
    r = ua("buildscenes.get"); check([x["path"] for x in r["scenes"]] == [S] and r["scenes"][0]["enabled"], "buildscenes.get")
    P2 = "Assets/Test/Prefabs/Player.prefab"
    r = ua("asset.info", P2); pg = r.get("guid"); check(r["ok"] and r["importer"] == "PrefabImporter" and len(pg or "") == 32, "asset.info")
    r = ua("guid.path", pg); check(r["path"] == P2 and r["exists"], "guid.path")
    r = ua("asset.deps", S); check(any(d.get("path") == P2 or d == P2 for d in r["dependencies"]), "asset.deps lists the prefab used by the scene")
    r = ua("asset.copy", P2, "Assets/Test/Prefabs/PlayerCopy.prefab"); cg = r.get("guid"); check(r["ok"] and cg and cg != pg, "asset.copy gives the copy a new GUID")
    r = ua("ref.replace", P2, "Assets/Test/Prefabs/PlayerCopy.prefab", "--in", "Assets/Test"); check(S in r["files"], "ref.replace points the scene at the copy")
    r = ua("asset.refs", "Assets/Test/Prefabs/PlayerCopy.prefab"); check(any(x["file"] == S for x in r["referencedBy"]), "the copy is now referenced")
    ua("ref.replace", "Assets/Test/Prefabs/PlayerCopy.prefab", P2, "--in", "Assets/Test")
    r = ua("guid.regenerate", "Assets/Test/Prefabs/PlayerCopy.prefab", "--update-refs"); check(r["ok"] and r["old"] == cg and r["new"] != cg, "guid.regenerate")
    r = ua("asset.delete", "Assets/Test/Prefabs/PlayerCopy.prefab"); check(r["ok"], "asset.delete of an unreferenced copy")
    r = ua("meta.get", "Assets/Test/notes.txt", "TextScriptImporter.userData"); check(r["value"] == "x", "meta.get reads the value meta.set wrote")
    r = ua("settings.list"); check(any("TagManager" in json.dumps(x) for x in r["settings"]), "settings.list")

    print("ScriptableObject")
    open(os.path.join(proj, "Assets/Test/GameConfig.cs"), "w").write("using UnityEngine;\npublic class GameConfig : ScriptableObject { public int lives = 3; }\n")
    ua("asset.import", "Assets/Test/GameConfig.cs")
    r = ua("so.create", "GameConfig", "Assets/Test/Config.asset", "--set", '{"lives": 5}'); check(r["ok"] and r["fields"]["lives"] == 5, "so.create with fields")
    r = ua("obj.get", "Assets/Test/Config.asset", "lives"); check(r["value"] == 5, "obj.get on ScriptableObject")
    r = ua("obj.set", "Assets/Test/Config.asset", "lives", "7"); check(r["value"] == 7, "obj.set on ScriptableObject")
    r = ua("obj.list", "Assets/Test/Config.asset"); check(len(r["objects"]) == 1 and r["objects"][0]["type"] == "MonoBehaviour", "obj.list")

    print("packages")
    r = ua("pkg.list"); check(r["ok"] and any(x["name"] == "com.unity.addressables" for x in r["packages"]), "pkg.list reads manifest")
    if os.environ.get("UA_OFFLINE") != "1":
        r = ua("pkg.add", "com.unity.splines@2.5.2"); check(r["ok"] and r["version"] == "2.5.2", "pkg.add registry package (validated against the registry)")
        man = json.load(open(os.path.join(proj, "Packages/manifest.json")))
        keys = list(man["dependencies"].keys()); check(keys == sorted(keys) and "com.unity.splines" in keys, "manifest stays sorted")
        r = ua("pkg.deps", "com.unity.splines"); check(r["ok"] and r["name"] == "com.unity.splines", "pkg.deps tree")
        r = ua("pkg.remove", "com.unity.splines"); check(r["ok"], "pkg.remove")
        r = ua("pkg.add", "com.unity.nope@9.9.9", expect_ok=False); check(not r["ok"], "unknown package rejected")
        r = ua("pkg.info", "com.unity.addressables"); check(r["ok"] and r["name"] == "com.unity.addressables" and r.get("requested"), "pkg.info")
        r = ua("pkg.search", "com.unity.splines"); check(any(x["name"] == "com.unity.splines" and x["source"] == "registry" for x in r["results"]), "pkg.search: exact name on the registry")
        r = ua("pkg.search", "addressables"); check(any(x["name"] == "com.unity.addressables" for x in r["results"]), "pkg.search: text in known packages")

        print("Addressables (fetch package sources → real script GUIDs)")
        p2 = tempfile.mkdtemp(prefix="ua_addr_")
        os.makedirs(os.path.join(p2, "Packages")); os.makedirs(os.path.join(p2, "Assets/Art"))
        shutil.copytree(os.path.join(DATA, "ProjectSettings"), os.path.join(p2, "ProjectSettings"))
        json.dump({"dependencies": {"com.unity.addressables": "1.22.3"}}, open(os.path.join(p2, "Packages/manifest.json"), "w"))
        def ua2(*args, expect_ok=True):
            q = subprocess.run([DOTNET, DLL, "ua", p2, *args], capture_output=True, text=True, env=env)
            res = json.loads(q.stdout)
            if expect_ok and not res.get("ok"): print("   ", res.get("error"))
            return res
        r = ua2("pkg.resolve", "--fetch", "--write-lock")
        check(r["ok"] and any(x["name"] == "com.unity.scriptablebuildpipeline" for x in r["packages"]) and len(r["fetched"]) >= 2, "resolve + fetch transitive dependencies")
        check(os.path.exists(os.path.join(p2, "Packages/packages-lock.json")), "packages-lock.json written")
        for n in ["a.mat", "b.mat", "shared.mat"]:
            open(os.path.join(p2, "Assets/Art", n), "w").write("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &2100000\nMaterial:\n  m_Name: x\n")
        ua2("asset.import", "Assets/Art")
        sh = ua2("guid.of", "Assets/Art/shared.mat")["guid"]
        for n in ["a.mat", "b.mat"]:
            open(os.path.join(p2, "Assets/Art", n), "a").write(f"  m_Parent: {{fileID: 2100000, guid: {sh}, type: 2}}\n")
        r = ua2("addr.init"); check(r["ok"], "addr.init")
        r = ua2("addr.info"); check(r["initialized"] and r["groups"][0]["name"] == "Default Local Group" and r["groups"][0]["default"], "Default Local Group is default")
        st = ua2("obj.get", "Assets/AddressableAssetsData/AddressableAssetSettings.asset", "m_Script")
        lib_guid = open(next(os.path.join(dp, f) + ".meta" for dp, _, fs in os.walk(os.path.join(p2, "Library/PackageCache")) for f in fs if f == "AddressableAssetSettings.cs")).read().split("guid: ")[1][:32]
        check(st["value"]["guid"] == lib_guid, "settings use the package's real script GUID")
        r = ua2("addr.group.create", "Characters"); check(r["ok"], "addr.group.create")
        r = ua2("addr.entry.add", "Assets/Art/a.mat", "--group", "Characters", "--labels", "hero"); check(r["ok"] and r["added"][0]["address"] == "Assets/Art/a.mat", "entry add with label")
        r = ua2("addr.entry.add", "Assets/Art/b.mat", "--simplify"); check(r["added"][0]["group"] == "Default Local Group" and r["added"][0]["address"] == "b", "entry add to default group, simplified address")
        r = ua2("addr.entry.set", "Assets/Art/b.mat", "--add-labels", "ui", "--group", "Characters"); check(r["group"] == "Characters" and r["labels"] == ["ui"], "entry move + labels")
        r = ua2("addr.entry.set", "Assets/Art/b.mat", "--group", "Default Local Group"); check(r["ok"], "move back")
        r = ua2("addr.analyze"); check([d["asset"] for d in r["duplicatedDependencies"]] == ["Assets/Art/shared.mat"], "analyze finds the material shared by two groups")
        r = ua2("addr.schema.set", "Characters", "BundledAssetGroupSchema", "m_Compression", "2"); check(r["value"] == 2, "schema setting")
        r = ua2("addr.label.remove", "hero"); check(r["entriesChanged"] == 1, "label removal strips entries")
        r = ua2("addr.entries", "--group", "Characters"); check(r["count"] == 1 and r["entries"][0]["labels"] == [], "entries listing")
        r = ua2("addr.label.add", "boss"); check("boss" in r["labels"], "addr.label.add")
        r = ua2("addr.schema.get", "Characters", "BundledAssetGroupSchema", "m_Compression"); check(r["value"] == 2, "addr.schema.get")
        r = ua2("addr.group.rename", "Characters", "Heroes"); check(r["ok"] and ua2("addr.info")["groups"][1]["name"] == "Heroes", "addr.group.rename")
        r = ua2("addr.group.default", "Heroes"); check(next(g for g in ua2("addr.info")["groups"] if g["name"] == "Heroes")["default"], "addr.group.default")
        ua2("addr.group.default", "Default Local Group")
        r = ua2("addr.entry.remove", "Assets/Art/a.mat"); check(r["removed"] == 1, "addr.entry.remove")
        r = ua2("addr.group.remove", "Heroes"); check(r["ok"] and all(g["name"] != "Heroes" for g in ua2("addr.info")["groups"]), "addr.group.remove")
        shutil.rmtree(p2)

        version = open(os.path.join(DATA, "ProjectSettings/ProjectVersion.txt")).read().split("m_EditorVersion: ")[1].split()[0]
        editor = os.environ.get("UNITY_EDITOR") or next((c for c in [os.path.join(MU, ".cache/unity", version, "Editor"), os.path.expanduser(f"~/Unity/Hub/Editor/{version}/Editor"), "/opt/unity/Editor"]
                                                         if os.path.isdir(os.path.join(c, "Data/Managed"))), "/opt/unity/Editor")
        if os.path.isdir(os.path.join(editor, "Data/Managed")):
            print("Input System package (native backend fed by --input)")
            p4 = tempfile.mkdtemp(prefix="ua_input_")
            shutil.copytree(os.path.join(DATA, "ProjectSettings"), os.path.join(p4, "ProjectSettings"))
            ps = os.path.join(p4, "ProjectSettings/ProjectSettings.asset")
            open(ps, "w").write(open(ps).read().replace("activeInputHandler: 0", "activeInputHandler: 2").replace("activeInputHandler: 1", "activeInputHandler: 2"))
            os.makedirs(os.path.join(p4, "Packages")); os.makedirs(os.path.join(p4, "Assets/Scripts"))
            json.dump({"dependencies": {"com.unity.inputsystem": "1.7.0"}}, open(os.path.join(p4, "Packages/manifest.json"), "w"))
            open(os.path.join(p4, "Assets/Scripts/Probe.cs"), "w").write("""using UnityEngine;
using UnityEngine.InputSystem;
public class Probe : MonoBehaviour
{
    public int presses, jumps; public Vector2 move, mouse; public bool left; public int touches;
    public Vector2 stick, jstick; public int south; public float trigger; public bool hasPad, dpadUp, jtrigger;
    InputAction _jump, _move;
    void Awake()
    {
        _jump = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space"); _jump.performed += _ => jumps++;
        _move = new InputAction("Move", InputActionType.Value);
        _move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
    }
    void OnEnable() { _jump.Enable(); _move.Enable(); }
    void OnDisable() { _jump.Disable(); _move.Disable(); }
    void Update()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame) presses++;
        move = _move.ReadValue<Vector2>(); mouse = Mouse.current.position.ReadValue(); left = Mouse.current.leftButton.isPressed;
        if (Touchscreen.current.primaryTouch.press.wasPressedThisFrame) touches++;
        var pad = Gamepad.current; hasPad = pad != null;
        if (pad != null)
        {
            if (pad.leftStick.ReadValue().sqrMagnitude > 0) stick = pad.leftStick.ReadValue();
            if (pad.buttonSouth.wasPressedThisFrame) south++;
            trigger = Mathf.Max(trigger, pad.rightTrigger.ReadValue()); dpadUp |= pad.dpad.up.isPressed;
        }
        var joy = Joystick.current;
        if (joy != null) { if (joy.stick.ReadValue().sqrMagnitude > 0) jstick = joy.stick.ReadValue(); jtrigger |= joy.trigger.isPressed; }
    }
}
""")
            def ua4(*args, expect_ok=True):
                q = subprocess.run([DOTNET, DLL, "ua", p4, *args], capture_output=True, text=True, env=env)
                res = json.loads(q.stdout[q.stdout.index("{"):])
                if expect_ok and not res.get("ok"): print("   ", res.get("error"))
                return res
            r = ua4("pkg.resolve", "--fetch"); check(r["ok"], "fetch com.unity.inputsystem")
            ua4("asset.import", "Assets/Scripts/Probe.cs"); ua4("scene.new", "Assets/Main.unity", "--basic")
            ua4("go.create", "Assets/Main.unity", "P"); ua4("comp.add", "Assets/Main.unity", "P", "Probe")
            r = ua4("play", "Assets/Main.unity", "--frames", "42", "--input", "Space@10-20,w@30-50,d@35-50,mouse=(400,300)@5,mouse0@40-45,touch0=(100,200)@25-28",
                    "--watch", "P/Probe.presses,P/Probe.jumps,P/Probe.move,P/Probe.mouse,P/Probe.left,P/Probe.touches,P/Probe.hasPad")
            wv = r.get("watch", {})
            check(r["ok"] and r["errors"] == 0 and not r["exceptions"], "Input System initializes in play mode (no errors)")
            check(wv.get("P/Probe.presses") == 1 and wv.get("P/Probe.jumps") == 1, "Keyboard.current + InputAction from scripted keys")
            check(wv.get("P/Probe.move") == "(0.71, 0.71)", "2DVector composite (normalized)")
            check(wv.get("P/Probe.mouse") == "(400.00, 300.00)" and wv.get("P/Probe.left") is True, "Mouse position + button")
            check(wv.get("P/Probe.touches") == 1, "Touchscreen primary touch")
            check(wv.get("P/Probe.hasPad") is False, "no gamepad unless the scripted input uses one")
            r = ua4("play", "Assets/Main.unity", "--frames", "30", "--input", "pad.leftStick=(1,0)@5-10,pad.a@12-14,pad.rightTrigger=0.75@15-16,pad.dpad=(0,1)@18,joy.stick=(0,-1)@20-22,joy.trigger@24",
                    "--watch", "P/Probe.hasPad,P/Probe.stick,P/Probe.south,P/Probe.trigger,P/Probe.dpadUp,P/Probe.jstick,P/Probe.jtrigger")
            wv = r.get("watch", {})
            check(r["ok"] and r["errors"] == 0 and wv.get("P/Probe.hasPad") is True and wv.get("P/Probe.stick") == "(1.00, 0.00)" and wv.get("P/Probe.south") == 1,
                  "Gamepad.current from pad.* input: stick, button south (alias a)")
            check(abs(wv.get("P/Probe.trigger", 0) - 0.75) < 0.01 and wv.get("P/Probe.dpadUp") is True, "Gamepad trigger and dpad")
            check(wv.get("P/Probe.jstick") == "(0.00, -1.00)" and wv.get("P/Probe.jtrigger") is True, "Joystick.current from joy.* input")
            check(not ua4("play", "Assets/Main.unity", "--frames", "2", "--input", "pad.nope@1", expect_ok=False).get("ok"), "unknown gamepad control is an error")
            shutil.rmtree(p4)

    version = open(os.path.join(DATA, "ProjectSettings/ProjectVersion.txt")).read().split("m_EditorVersion: ")[1].split()[0]
    editor = os.environ.get("UNITY_EDITOR") or next((c for c in [os.path.join(MU, ".cache/unity", version, "Editor"), os.path.expanduser(f"~/Unity/Hub/Editor/{version}/Editor"), "/opt/unity/Editor"]
                                                     if os.path.isdir(os.path.join(c, "Data/Managed"))), "/opt/unity/Editor")
    cache_pkgs = ["com.unity.test-framework@1.1.33", "com.unity.ext.nunit@1.0.6"]
    if os.path.isdir(os.path.join(editor, "Data/Managed")) and not all(pkg_source(*pk.split("@")) for pk in cache_pkgs):
        check(False, "compile/test/play section needs the test framework sources (run setup.sh: it fetches them into .cache/packages)")
    elif os.path.isdir(os.path.join(editor, "Data/Managed")):
        print("compile, tests, play, logs (Unity Editor reference assemblies)")
        p3 = tempfile.mkdtemp(prefix="ua_rt_")
        shutil.copytree(os.path.join(DATA, "ProjectSettings"), os.path.join(p3, "ProjectSettings"))
        for pk in cache_pkgs:
            shutil.copytree(pkg_source(*pk.split("@")), os.path.join(p3, "Library/PackageCache", pk))
        def w(rel, text):
            os.makedirs(os.path.dirname(os.path.join(p3, rel)), exist_ok=True); open(os.path.join(p3, rel), "w").write(text)
        w("Packages/manifest.json", '{"dependencies": {"com.unity.test-framework": "1.1.33"}}')
        w("Packages/packages-lock.json", '{"dependencies": {"com.unity.test-framework": {"version": "1.1.33", "depth": 0, "source": "registry", "dependencies": {"com.unity.ext.nunit": "1.0.6"}, "url": "https://packages.unity.com"}, "com.unity.ext.nunit": {"version": "1.0.6", "depth": 1, "source": "registry", "dependencies": {}, "url": "https://packages.unity.com"}}}')
        w("Assets/Scripts/Game.asmdef", '{"name": "Game"}')
        w("Assets/Scripts/Spinner.cs", """using System.Collections;
using UnityEngine;
public class Spinner : MonoBehaviour
{
    public float speed = 90f;
    public int updates;
    public bool failOnFrame3;
    public Transform target;
    void Awake() { Debug.Log("Awake " + name); }
    IEnumerator Start() { yield return new WaitForSeconds(0.5f); Debug.LogWarning("half second"); }
    void Update()
    {
        updates++;
        transform.Rotate(0, speed * Time.deltaTime, 0);
        if (Input.GetKeyDown(KeyCode.Space)) Debug.Log("jump at " + Time.frameCount);
        if (failOnFrame3 && Time.frameCount == 3) target.position = Vector3.one;
    }
}
public static class MathUtil { public static int Add(int a, int b) => a + b; }
""")
        w("Assets/Tests/Tests.asmdef", '{"name": "Tests", "references": ["Game", "UnityEngine.TestRunner", "UnityEditor.TestRunner"], "includePlatforms": ["Editor"], "overrideReferences": true, "precompiledReferences": ["nunit.framework.dll"], "defineConstraints": ["UNITY_INCLUDE_TESTS"], "autoReferenced": false}')
        w("Assets/Tests/EditTests.cs", """using NUnit.Framework;
public class EditTests
{
    [Test] public void Adds() => Assert.AreEqual(4, MathUtil.Add(2, 2));
    [Test] public void Fails() => Assert.AreEqual(5, MathUtil.Add(2, 2), "math is broken");
    [TestCase(1, 2, 3)] [TestCase(2, 2, 5)] public void Cases(int a, int b, int c) => Assert.That(MathUtil.Add(a, b), Is.EqualTo(c));
    [Test, Ignore("later")] public void Skipped() { }
}
""")
        w("Assets/Tests/JobTests.cs", """using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
public class JobTests
{
    struct Fill : IJobParallelFor { public NativeArray<int> a; public void Execute(int i) => a[i] = i * 2; }
    struct Sum : IJob { [ReadOnly] public NativeArray<int> a; public NativeArray<int> total; public void Execute() { int s = 0; for (int i = 0; i < a.Length; i++) s += a[i]; total[0] = s; } }
    [Test] public void ParallelForThenJob()
    {
        var a = new NativeArray<int>(100, Allocator.TempJob); var t = new NativeArray<int>(1, Allocator.TempJob);
        new Sum { a = a, total = t }.Schedule(new Fill { a = a }.Schedule(100, 16)).Complete();
        Assert.AreEqual(198, a[99]); Assert.AreEqual(9900, t[0]);
        a.Dispose(); t.Dispose();
        Assert.Throws<System.ObjectDisposedException>(() => { var x = a[0]; });
    }
}
""")
        w("Assets/PlayTests/PlayTests.asmdef", '{"name": "PlayTests", "references": ["Game", "UnityEngine.TestRunner"], "overrideReferences": true, "precompiledReferences": ["nunit.framework.dll"], "defineConstraints": ["UNITY_INCLUDE_TESTS"], "autoReferenced": false, "optionalUnityReferences": ["TestAssemblies"]}')
        w("Assets/PlayTests/PlayTests.cs", """using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
public class PlayTests
{
    [UnityTest] public IEnumerator SpinnerRotates() { var g = new GameObject("s"); var s = g.AddComponent<Spinner>(); yield return null; yield return null; Assert.Greater(s.updates, 0); }
    [UnityTest] public IEnumerator WaitSeconds() { float t0 = Time.time; yield return new WaitForSeconds(1f); Assert.GreaterOrEqual(Time.time - t0, 1f); }
    [UnityTest] public IEnumerator UnexpectedError() { Debug.LogError("boom"); yield return null; }
    [UnityTest] public IEnumerator ExpectedError() { LogAssert.Expect(LogType.Error, "boom"); Debug.LogError("boom"); yield return null; }
}
""")
        w("Assets/Broken/Broken.asmdef", '{"name": "Broken", "references": ["Game"]}')
        w("Assets/Broken/Broken.cs", "public class Broken { int X() => MathUtil.Add(1); }\n")
        def ua3(*args):
            q = subprocess.run([DOTNET, DLL, "ua", p3, *args], capture_output=True, text=True, env=env)
            return json.loads(q.stdout)
        r = ua3("compile")
        errs = [d for d in r["diagnostics"] if d["severity"] == "error"]
        check(r["ok"] and not r["success"] and len(errs) == 1 and errs[0]["file"] == "Assets/Broken/Broken.cs" and errs[0]["id"] == "CS7036", "compile reports CS7036 in Broken.cs with file/line")
        os.remove(os.path.join(p3, "Assets/Broken/Broken.cs")); os.remove(os.path.join(p3, "Assets/Broken/Broken.asmdef"))
        r = ua3("compile"); check(r["success"] and r["errors"] == 0, "compile clean after removing the broken assembly")
        r = ua3("compile.assemblies"); names = [x["name"] for x in r["assemblies"]]
        check("Game" in names and "UnityEngine.TestRunner" in names, "compile.assemblies (project asmdef + package assemblies)")
        r = ua3("test")
        res = {t["name"]: t for t in r.get("tests", [])}
        check(r["ok"] and r["total"] == 10 and r["passed"] == 6 and r["failed"] == 3 and r["skipped"] == 1, "test: 6 passed, 3 failed, 1 skipped")
        check(res.get("JobTests.ParallelForThenJob", {}).get("result") == "Passed", "jobs + NativeArray (IJobParallelFor → IJob dependency)")
        check(res.get("EditTests.Fails", {}).get("line") == 5 and "math is broken" in res["EditTests.Fails"].get("message", ""), "failed assert → message + file:line")
        check(res.get("PlayTests.SpinnerRotates", {}).get("mode") == "PlayMode" and res["PlayTests.SpinnerRotates"]["result"] == "Passed", "[UnityTest] drives the player loop (PlayMode)")
        check(res.get("PlayTests.UnexpectedError", {}).get("result") == "Failed" and res.get("PlayTests.ExpectedError", {}).get("result") == "Passed", "LogAssert: unexpected error fails, expected passes")
        r = ua3("test", "--mode", "editmode", "--filter", "Cases"); check(r["total"] == 2, "test --mode/--filter")
        ua3("asset.import", "Assets")
        check(not os.path.exists(os.path.join(p3, "Assets.meta")), "asset.import writes no Assets.meta")
        ua3("scene.new", "Assets/Main.unity", "--basic"); ua3("go.create", "Assets/Main.unity", "Wheel")
        r = ua3("comp.add", "Assets/Main.unity", "Wheel", "Spinner"); check(r["ok"], "comp.add project script")
        ua3("comp.set", "Assets/Main.unity", "Wheel", "Spinner", "failOnFrame3", "1")
        r = ua3("play", "Assets/Main.unity", "--frames", "60", "--input", "Space@10", "--watch", "Wheel/Spinner.updates")
        ex = r.get("exceptions", [])
        w("play.json", json.dumps({"exceptions": [{"type": "System.NotImplementedException", "file": "Assets/Scripts/Gap.cs", "line": 3, "count": 2,
                                               "miniunity": "API not implemented by MiniUnity (works in Unity): UnityEngine.Terrain.activeTerrain"}]}))
        check(r["ok"] and r["watch"]["Wheel/Spinner.updates"] == 60, "play: Update ran 60 frames")
        check(len(ex) == 1 and ex[0]["type"] == "UnityEngine.UnassignedReferenceException" and ex[0]["file"] == "Assets/Scripts/Spinner.cs" and ex[0]["line"] == 16
              and ex[0]["message"].startswith("The variable target of Spinner has not been assigned."), "play: exception with project file:line (unassigned field: Unity's fake null)")
        msgs = [l["message"] for l in r.get("logs", [])]
        check("Awake Wheel" in msgs and "half second" in msgs and "jump at 10" in msgs, "play: Awake, coroutine WaitForSeconds, scripted input")
        w("Assets/Editor/HelloWindow.cs", """using UnityEditor;
using UnityEngine;
public class HelloWindow : EditorWindow
{
    enum Mode { Slow, Fast }
    [SerializeField] int count = 2;
    [SerializeField] string who = "x";
    bool fancy; Mode mode;
    [MenuItem("Tools/Hello Window")] static void Open() => GetWindow<HelloWindow>("Hello");
    [MenuItem("Tools/Say Hi")] static void Hi() => Debug.Log("hi from menu");
    void OnGUI()
    {
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        count = EditorGUILayout.IntField("Count", count);
        who = EditorGUILayout.TextField("Who", who);
        mode = (Mode)EditorGUILayout.EnumPopup("Mode", mode);
        using (new EditorGUI.DisabledScope(count <= 0))
            if (GUILayout.Button("Go")) Debug.Log($"go {who} {count} {mode}");
        using (var c = new EditorGUI.ChangeCheckScope())
        {
            fancy = EditorGUILayout.Toggle("Fancy", fancy);
            if (c.changed) Debug.Log("fancy " + fancy);
        }
    }
}
""")
        r = ua3("menu", "Tools/Say Hi")
        check(r["ok"] and any(l["message"] == "hi from menu" for l in r["logs"]), "menu: runs a [MenuItem] of the project")
        r = ua3("window", "Tools/Hello Window", "--set", "Count=3;Who=bob;Mode=Fast;Fancy=true", "--click", "Go")
        msgs = [l["message"] for l in r.get("logs", [])]
        ctl = {c["label"]: c for c in r.get("controls", [])}
        check(r["ok"] and r["title"] == "Hello" and "go bob 3 Fast" in msgs and "fancy True" in msgs and ctl["Count"]["value"] == "3" and ctl["Mode"]["value"] == "Fast",
              "window: OnGUI headless, --set fields (int, text, enum, toggle + ChangeCheckScope), --click a button")
        r = ua3("window", "HelloWindow", "--set", "Count=0", "--click", "Go")
        check(not r["ok"] and "no enabled button 'Go'" in r["error"], "window: a button inside a DisabledScope cannot be clicked")
        w("Assets/Scripts/Gap.cs", "public static class Gap\n{\n    public static object T() => UnityEngine.Terrain.activeTerrain;\n}\n")
        r = ua3("apigap", "--from", os.path.join(p3, "play.json"))
        stub = {x["member"]: x for x in r.get("stub", [])}
        check(r["ok"] and "UnityEngine.Terrain.activeTerrain" in stub and stub["UnityEngine.Terrain.activeTerrain"]["sites"] == ["Assets/Scripts/Gap.cs:3"]
              and r["summary"]["missing"] == 0, "apigap: a stub member the project calls, with its file:line")
        check(stub.get("UnityEngine.Terrain.activeTerrain", {}).get("hitAtRuntime") == 2 and r["runtime"][0]["count"] == 2, "apigap --from: exceptions hit at run time")
        w("Editor.log", "Assets/A.cs(3,5): error CS0103: The name 'x' does not exist in the current context\nNullReferenceException: Object reference not set to an instance of an object\nA.Update () (at Assets/A.cs:9)\n\nNullReferenceException: Object reference not set to an instance of an object\nA.Update () (at Assets/A.cs:9)\n")
        r = ua3("log.analyze", "Editor.log")
        check(r["compileErrors"][0]["id"] == "CS0103" and r["exceptions"][0]["count"] == 2 and r["exceptions"][0]["line"] == 9, "log.analyze groups compile errors and exceptions")

        # interactive play through MCP: a click on a cube reaches its OnMouseDown
        w("Assets/Scripts/ClickMe.cs", """using UnityEngine;
public class ClickMe : MonoBehaviour
{
    void OnMouseDown() => Debug.Log("clicked " + name);
    void OnMouseUpAsButton() => Debug.Log("button " + name);
}
""")
        ua3("asset.import", "Assets/Scripts/ClickMe.cs")
        r1 = ua3("scene.new", "Assets/Click.unity")
        r2 = ua3("go.create", "Assets/Click.unity", "Cam", "--components", "Camera", "--position", json.dumps({"x": 0, "y": 0, "z": -10}))
        r3 = ua3("go.create", "Assets/Click.unity", "Box", "--components", "BoxCollider,ClickMe")
        check(r1["ok"] and r2["ok"] and r3["ok"], "click scene built" + ("" if r1["ok"] and r2["ok"] and r3["ok"] else f" ({r1.get('error')} / {r2.get('error')} / {r3.get('error')})"))
        mp3 = subprocess.Popen([DOTNET, DLL, "mcp", p3], stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, env=env)
        def rpc3(i, name, args):
            mp3.stdin.write(json.dumps({"jsonrpc": "2.0", "id": i, "method": "tools/call", "params": {"name": name, "arguments": args}}) + "\n"); mp3.stdin.flush()
            return json.loads(mp3.stdout.readline())["result"]
        mp3.stdin.write(json.dumps({"jsonrpc": "2.0", "id": 0, "method": "initialize", "params": {"protocolVersion": "2025-06-18", "capabilities": {}}}) + "\n"); mp3.stdin.flush(); mp3.stdout.readline()
        r = rpc3(1, "play_start", {"scene": "Assets/Click.unity", "options": {"size": "320x180"}})
        check(not r["isError"] and '"ready": true' in r["content"][0]["text"], "MCP play_start opens an interactive session")
        ri = rpc3(2, "play_input", {"spec": "mouse=(160,90),mouse0@1-2", "step": 4})
        r = rpc3(3, "play_state", {"watch": "Box/Transform.position"})
        st = json.loads(r["content"][0]["text"])
        msgs = [l["message"] for l in st.get("logs", [])]
        check("clicked Box" in msgs and "button Box" in msgs, "play_input mouse0 over a collider → OnMouseDown / OnMouseUpAsButton" + ("" if "clicked Box" in msgs else f" (input: {ri['content'][0]['text'][:400]} state: {json.dumps(st)[:400]})"))
        r = rpc3(4, "play_screenshot", {})
        check(any(c.get("type") == "image" for c in r["content"]), "play_screenshot returns the frame as an image")
        r = rpc3(5, "play_stop", {})
        check(not r["isError"], "play_stop ends the session")
        mp3.stdin.close(); mp3.wait()
        shutil.rmtree(p3)

    print("binary SerializedFile (Unity's unity_builtin_extra)")
    import glob as _glob
    extra = next(iter(sorted(_glob.glob(os.path.join(MU, ".cache/unity/*/Editor/Data/Resources/unity_builtin_extra")))), None)
    if extra:
        r = ua("serialized.dump", extra, "--id", "10304")
        o = (r.get("objects") or [{}])[0]
        check(r["ok"] and r["version"] == 22 and r["typeTree"] and o.get("name") == "Default-Skybox" and "_Exposure: 1.3" in o.get("yaml", "")
              and "m_Shader: {fileID: 106}" in o.get("yaml", ""), "serialized.dump reads a Unity binary file: Default-Skybox as YAML")
        r = ua("serialized.dump", extra, "--type", "Material")
        check(r["ok"] and {"Default-Material", "Sprites-Default"} <= {x.get("name") for x in r["objects"]}, "serialized.dump --type lists objects by type")
    else: print("  skip (no Unity Editor data in MiniUnity/.cache)")

    print("robustness: bad options, writes outside the project, batch")
    r = ua("asset.find", "--limit", "abc", expect_ok=False)
    check(not r["ok"] and r["error"] == "--limit: expected an integer, got 'abc'" and "stack" not in r, "bad integer option → clear error, no stack")
    evil = proj + "_evil"
    r = ua("scene.new", os.path.join(evil, "X.unity"), expect_ok=False)
    check(not r["ok"] and "refuses" in r["error"] and not os.path.exists(evil), "write into a sibling folder whose name starts with the project's is refused")
    check(not [f for d, _, fs in os.walk(os.path.join(proj, "Assets")) for f in fs if f.endswith(".tmp")], "atomic writes leave no temp files")
    bf = os.path.join(proj, "edits.jsonl")
    with open(bf, "w") as f: f.write('["go.create", "%s", "FromBatch"]\nnot json\n["go.delete", "%s", "Nope"]\n' % (S, S))
    before = open(os.path.join(proj, S)).read()
    r = ua("batch", bf, "--dry-run", expect_ok=False)
    check(not r["ok"] and r["failed"] == 2 and r["results"][1]["line"] == 2 and r["results"][0]["ok"] and r["results"][0].get("dryRun"), "batch: malformed line is a failed result with its line number; --dry-run reaches every command")
    check(open(os.path.join(proj, S)).read() == before, "batch --dry-run writes nothing")
    r = ua("batch", bf, "--stop-on-error", expect_ok=False)
    check(r["skipped"] == 1 and ua("go.find", S, "--name", "frombatch")["matches"][0]["path"] == "FromBatch", "batch --stop-on-error skips the rest; go.find --name is case-insensitive")
    ua("go.delete", S, "FromBatch")

    print("diff + undo")
    before = open(os.path.join(proj, S)).read()
    r = ua("go.create", S, "Undoable", "--dry-run")
    d = r.get("diff", [{}])[0]
    check(r["ok"] and d.get("path") == S and d.get("change") == "modified" and "+  m_Name: Undoable" in d.get("diff", ""), "--dry-run returns a unified diff")
    r = ua("go.create", S, "Undoable"); check(isinstance(r.get("undo"), int), "write commands report their undo journal id")
    r = ua("undo.list"); check(r["entries"][0]["command"].startswith("go.create") and r["entries"][0]["id"] == ua("undo.list")["entries"][0]["id"], "undo.list")
    r = ua("undo"); check(r["ok"] and open(os.path.join(proj, S)).read() == before, "undo restores the file byte for byte")
    r = ua("asset.mkdir", "Assets/UndoDir"); r = ua("undo"); check(r["ok"] and not os.path.exists(os.path.join(proj, "Assets/UndoDir")), "undo removes a created folder")
    ua("go.create", S, "Undoable2")
    with open(os.path.join(proj, S), "a") as fh: fh.write("")
    os.utime(os.path.join(proj, S))
    with open(os.path.join(proj, S), "a") as fh: fh.write("\n")
    r = ua("undo", expect_ok=False); check(not r["ok"] and "changed" in r["error"], "undo refuses when the file changed afterwards")
    r = ua("undo", "--force"); check(r["ok"] and open(os.path.join(proj, S)).read() == before, "undo --force restores anyway")

    r = ua("tags.add", "SelfTestTag"); r2 = ua("tags.remove", "SelfTestTag"); r3 = ua("tags.remove", "SelfTestTag", expect_ok=False)
    check(r["ok"] and r2["ok"] and not r3["ok"], "tags.remove (and an unknown tag is an error)")

    print("MCP server (stdio JSON-RPC)")
    mp = subprocess.Popen([DOTNET, DLL, "mcp", proj], stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, env=env)
    def rpc(i, method, params=None):
        mp.stdin.write(json.dumps({"jsonrpc": "2.0", "id": i, "method": method, **({"params": params} if params is not None else {})}) + "\n"); mp.stdin.flush()
        return json.loads(mp.stdout.readline())
    r = rpc(1, "initialize", {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "selftest", "version": "0"}})
    check(r["result"]["serverInfo"]["name"] == "miniunity" and "tools" in r["result"]["capabilities"], "initialize")
    mp.stdin.write(json.dumps({"jsonrpc": "2.0", "method": "notifications/initialized"}) + "\n"); mp.stdin.flush()
    tools = {t["name"]: t for t in rpc(2, "tools/list")["result"]["tools"]}
    check("go_tree" in tools and "comp_set" in tools and "batch" in tools and tools["go_tree"]["annotations"]["readOnlyHint"] and not tools["go_create"]["annotations"]["readOnlyHint"], "tools/list with read-only hints")
    r = rpc(3, "tools/call", {"name": "go_create", "arguments": {"args": [S, "FromMcp"], "options": {"position": {"x": 1, "y": 0, "z": 0}}}})
    check(not r["result"]["isError"] and r["result"]["structuredContent"]["path"] == "FromMcp", "tools/call go_create (object option passed as JSON)")
    r = rpc(4, "tools/call", {"name": "go_get", "arguments": {"args": [S, "FromMcp"]}})
    check(r["result"]["structuredContent"]["transform"]["fields"]["m_LocalPosition"]["x"] == 1, "tools/call sees the previous call's edit")
    r = rpc(5, "tools/call", {"name": "go_delete", "arguments": {"args": [S, "Nope"]}})
    check(r["result"]["isError"] and "error" in r["result"]["structuredContent"], "failing command → isError")
    ua("go.rename", S, "FromMcp", "RenamedOutside")                     # another process edits the scene between two calls
    with open(os.path.join(proj, "Assets/Test/Outside.txt"), "w") as fh: fh.write("x")
    ua("asset.import", "Assets/Test/Outside.txt")
    r = rpc(60, "tools/call", {"name": "go_get", "arguments": {"args": [S, "RenamedOutside"]}})
    r2 = rpc(61, "tools/call", {"name": "asset_find", "arguments": {"options": {"name": "Outside.txt"}}})
    check(not r["result"]["isError"] and r2["result"]["structuredContent"]["count"] == 1, "in-process tools see edits made by other processes between calls")
    rpc(62, "tools/call", {"name": "go_rename", "arguments": {"args": [S, "RenamedOutside", "FromMcp"]}})
    rpc(6, "tools/call", {"name": "go_delete", "arguments": {"args": [S, "FromMcp"]}})
    check(not tools["play"]["annotations"]["readOnlyHint"] and "timeoutSeconds" in tools["go_tree"]["inputSchema"]["properties"], "play is not read-only; tools take timeoutSeconds")
    cs = tools["comp_set"]["inputSchema"]; gs = tools["go_set"]["inputSchema"]["properties"]["options"]["properties"]
    check(cs["properties"]["args"]["minItems"] == 5 and "4. propertyPath" in cs["properties"]["args"]["description"] and cs["required"] == ["args"]
          and gs["active"]["description"].startswith("--active 0|1") and tools["go_move"]["inputSchema"]["properties"]["options"]["properties"]["root"]["type"] == "boolean",
          "per-command schemas from the usage lines (positionals in order, options, flags)")
    r = rpc(7, "tools/call", {"name": "batch", "arguments": {"commands": [["go.create", S, "McpBatch"]], "dryRun": True}})
    check(not r["result"]["isError"] and r["result"]["structuredContent"]["dryRun"] and not ua("go.find", S, "--name", "McpBatch")["matches"], "batch tool with dryRun writes nothing")
    mp.stdin.close(); mp.wait()

    print("bench + determinism (replay)")
    small = ["bench", "--only", "physics3d,physics2d,raycast,animation,render3d", "--bodies", "60", "--frames", "60", "--render-frames", "1", "--size", "320x180"]
    b1, b2 = ua(*small), ua(*small)
    check(b1.get("ok") and all(b1[k]["msMean"] > 0 for k in ("physics3d", "physics2d", "raycast", "animation", "render3d")), "bench runs every workload")
    check(all(b1[k]["digest"] == b2[k]["digest"] for k in ("physics3d", "physics2d", "raycast", "animation", "render3d")) and b1["raycast"]["hits"] == b2["raycast"]["hits"],
          "replay: a second run gives bitwise identical poses, hits and pixels")
    check(not ua("bench", "--only", "nope", expect_ok=False).get("ok"), "bench rejects an unknown workload")

    print("round trip")
    p = subprocess.run([DOTNET, DLL, "rt", os.path.join(proj, S), os.path.join(proj, "rt.unity")], capture_output=True, text=True)
    check(open(os.path.join(proj, S)).read() == open(os.path.join(proj, "rt.unity")).read(), "edited scene re-reads and re-writes identically")

    print(f"\n{'PASS' if fails == 0 else 'FAILED'} ({fails} failures) · project: {proj}")
    if fails == 0: shutil.rmtree(proj)
    return 1 if fails else 0

if __name__ == "__main__":
    sys.exit(main())
