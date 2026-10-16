#!/usr/bin/env python3
"""Generate Engine/Core/Editor/ImguiControls.cs: every overload of the IMGUI controls MiniUnity runs headless (GUI, GUILayout,
EditorGUI, EditorGUILayout), from the signatures in Engine/Generated (the stubs of the installed Unity version). Each overload maps
its parameters by name onto MiniUnity.Imgui (label / value / options), so a new overload in another Unity version is picked up
by re-running:   python3 tools/imgui_gen.py   (then scripts/gen_stubs.sh drops the stubs these replace)."""
import os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__)); MU = os.path.dirname(HERE)
GEN = os.path.join(MU, "Engine/Generated")
OUT = os.environ.get("IMGUI_OUT") or os.path.join(MU, "Engine/Core/Editor/ImguiControls.cs")

CLASSES = [("UnityEngine", "GUI", "UnityEngine.IMGUIModule.g.cs"), ("UnityEngine", "GUILayout", "UnityEngine.IMGUIModule.g.cs"),
           ("UnityEditor", "EditorGUI", "UnityEditor.CoreModule.g.cs"), ("UnityEditor", "EditorGUILayout", "UnityEditor.CoreModule.g.cs")]

# member → kind
FIELD = {"TextField", "DelayedTextField", "TextArea", "PasswordField", "IntField", "DelayedIntField", "LongField", "FloatField", "DelayedFloatField",
         "DoubleField", "DelayedDoubleField", "Toggle", "ToggleLeft", "Foldout", "BeginFoldoutHeaderGroup", "BeginToggleGroup", "ColorField",
         "Vector2Field", "Vector3Field", "Vector4Field", "Vector2IntField", "Vector3IntField", "TagField", "LayerField", "Slider", "IntSlider",
         "HorizontalSlider", "VerticalSlider", "EnumPopup", "EnumFlagsField", "Popup", "IntPopup", "MaskField", "Toolbar", "SelectionGrid", "ObjectField",
         "CurveField", "GradientField", "RectField", "BoundsField"}
BUTTON = {"Button", "RepeatButton", "LinkButton", "DropdownButton"}
LABEL = {"Label", "LabelField", "HelpBox", "Box", "SelectableLabel", "PrefixLabel", "DropShadowLabel"}
NOOP = {"Space", "FlexibleSpace", "Separator", "BeginHorizontal", "EndHorizontal", "BeginVertical", "EndVertical", "EndScrollView", "EndFoldoutHeaderGroup",
        "EndToggleGroup", "BeginArea", "EndArea", "DrawRect", "DrawTexture", "DrawPreviewTexture", "DrawTextureTransparent", "DrawTextureAlpha",
        "BeginScrollView", "GetControlRect", "BeginGroup", "EndGroup", "FocusControl", "SetNextControlName", "BeginChangeCheck", "EndChangeCheck",
        "BeginDisabledGroup", "EndDisabledGroup", "ProgressBar", "Width", "Height", "MinWidth", "MaxWidth", "MinHeight", "MaxHeight", "ExpandWidth",
        "ExpandHeight", "PropertyField", "GetPropertyHeight", "FocusTextInControl", "IndentedRect", "BeginProperty", "EndProperty"}
ALL = FIELD | BUTTON | LABEL | NOOP

VALUE_NAMES = ["value", "selectedIndex", "selectedValue", "selected", "enumValue", "obj", "foldout", "toggle", "text", "password", "tag", "layer",
               "color", "mask", "selected", "curve", "gradient", "property"]
LABEL_NAMES = ["label", "content", "title", "text", "image"]


def split_params(s):
    out, depth, cur = [], 0, ""
    for ch in s:
        if ch in "<([": depth += 1
        if ch in ">)]": depth -= 1
        if ch == "," and depth == 0: out.append(cur.strip()); cur = ""
        else: cur += ch
    if cur.strip(): out.append(cur.strip())
    res = []
    for p in out:
        d = None
        if " = " in p: p, d = p.split(" = ", 1)
        toks = p.split()
        mods = [t for t in toks[:-2] if t in ("params", "out", "ref", "in", "this")]
        typ = " ".join(t for t in toks[:-1] if t not in ("params", "out", "ref", "in", "this"))
        res.append({"mods": mods, "type": typ, "name": toks[-1], "default": d, "raw": (" ".join(mods) + " " if mods else "") + typ + " " + toks[-1] + (" = " + d if d else "")})
    return res


def short(t): return t.replace("global::", "")


def label_expr(ps, value):
    for n in LABEL_NAMES:
        for p in ps:
            if p["name"] == n and p is not value:
                t = short(p["type"])
                if t == "string": return p["name"]
                if t.endswith("GUIContent"): return f"MiniUnity.Imgui.Text({p['name']})"
                if t.endswith("Texture"): return f'({p["name"]} != null ? {p["name"]}.name : "")'
    return '""'


def gen_body(cls, name, ret, ps):
    rt = short(ret)
    value = None
    if name in FIELD or name == "PropertyField":
        for n in VALUE_NAMES:
            value = next((p for p in ps if p["name"] == n and "out" not in p["mods"]), None)
            if value: break
    if name in ("Toolbar", "SelectionGrid"): value = next((p for p in ps if p["name"] == "selected"), value)
    lab = label_expr(ps, value)
    kind = f'"{cls}.{name}"'
    outs = [p for p in ps if "out" in p["mods"]]
    pre = "".join(f"{p['name']} = default; " for p in outs)
    if name in BUTTON:
        return f"{pre}return MiniUnity.Imgui.Button({kind}, {lab});"
    if name in LABEL:
        second = next((p for p in ps if p["name"] in ("label2", "message", "text") and p.get("name") != None and short(p["type"]) in ("string", "UnityEngine.GUIContent") and lab != p["name"] and f"MiniUnity.Imgui.Text({p['name']})" != lab), None)
        sec = (second["name"] if short(second["type"]) == "string" else f"MiniUnity.Imgui.Text({second['name']})") if second else '""'
        call = f"MiniUnity.Imgui.Label({kind}, {lab}, {sec});"
        if rt == "void": return pre + call
        if rt == "UnityEngine.Rect": return pre + call + " return MiniUnity.Imgui.NextRect();"
        return pre + call + " return default;"
    if name in FIELD and value is not None:
        vt = short(value["type"])
        if vt.endswith("SerializedProperty"):
            call = f"MiniUnity.Imgui.Property({kind}, {lab}, {value['name']});"
            return pre + call + ("" if rt == "void" else " return default;" if rt != "bool" else " return true;")
        if name == "ObjectField" and rt != "void":
            typ = next((p["name"] for p in ps if p["name"] == "objType"), "typeof(UnityEngine.Object)")
            return f"{pre}return MiniUnity.Imgui.ObjectField({kind}, {lab}, {value['name']}, {typ});"
        opts = next((p for p in ps if p["name"] in ("displayedOptions", "texts", "contents", "images")), None)
        vals = next((p for p in ps if p["name"] == "optionValues"), None)
        lo = next((p for p in ps if p["name"] in ("leftValue", "minValue")), None); hi = next((p for p in ps if p["name"] in ("rightValue", "maxValue")), None)
        if opts is not None and rt in ("int",):
            ov = vals["name"] if vals else "null"
            return f"{pre}return MiniUnity.Imgui.Popup({kind}, {lab}, {value['name']}, MiniUnity.Imgui.Texts({opts['name']}), {ov});"
        if lo is not None and hi is not None and rt in ("float", "int"):
            return f"{pre}return MiniUnity.Imgui.Slider({kind}, {lab}, {value['name']}, {lo['name']}, {hi['name']});"
        if rt == "void": return f"{pre}MiniUnity.Imgui.Field({kind}, {lab}, {value['name']});"
        cast = "" if vt == rt or (rt == "System.Enum" and vt == "System.Enum") else f"({rt})"
        if vt == rt or vt == "System.Enum": return f"{pre}return {cast}MiniUnity.Imgui.Field({kind}, {lab}, {value['name']});"
        return f"{pre}return ({rt})(object)MiniUnity.Imgui.Field<object>({kind}, {lab}, {value['name']});"
    # no-op family (layout, drawing, scroll views)
    if name == "BeginScrollView" and rt.endswith("Vector2"):
        sp = next((p for p in ps if p["name"] == "scrollPosition"), None)
        return f"{pre}return {sp['name'] if sp else 'default'};"
    if name in ("Width", "Height", "MinWidth", "MaxWidth", "MinHeight", "MaxHeight", "ExpandWidth", "ExpandHeight"):
        return f"{pre}return MiniUnity.Imgui.Option();"
    if name == "PropertyField":
        prop = next((p for p in ps if p["name"] == "property"), None)
        return f"{pre}MiniUnity.Imgui.Property({kind}, {lab}, {prop['name'] if prop else 'null'});" + ("" if rt == "void" else " return true;" if rt == "bool" else " return default;")
    if name == "BeginChangeCheck": return pre + "MiniUnity.Imgui.BeginChangeCheck();"
    if name == "EndChangeCheck": return pre + "return MiniUnity.Imgui.EndChangeCheck();"
    if name == "BeginDisabledGroup":
        d = next((p for p in ps if p["name"] == "disabled"), None)
        return pre + f"MiniUnity.Imgui.BeginDisabled({d['name'] if d else 'true'});"
    if name == "EndDisabledGroup": return pre + "MiniUnity.Imgui.EndDisabled();"
    if name == "DrawRect":
        c = next((p for p in ps if p["name"] == "color"), None)
        return pre + f"MiniUnity.Imgui.Label({kind}, \"\", {c['name'] if c else 'default'}.ToString());"
    if name == "GetPropertyHeight": return pre + "return 18;"
    if rt == "void": return pre.rstrip()
    if rt.endswith("Rect"): return pre + "return MiniUnity.Imgui.NextRect();"
    if rt == "bool": return pre + "return false;"
    if rt == "string": return pre + 'return "";'
    return pre + "return default;"


def main():
    out = ["// Generated by tools/imgui_gen.py from the Unity API stubs: every overload of the IMGUI controls MiniUnity runs headless,",
           "// mapped by parameter name onto MiniUnity.Imgui (Editor/Imgui.cs). Do not edit; re-run the script for another Unity version.",
           "#pragma warning disable CS0618, CS0612, CS1998", "using System;", ""]
    for ns, cls, file in CLASSES:
        text = open(os.path.join(GEN, file)).read()
        m = re.search(r"public (?:sealed |static |abstract )*unsafe partial class " + cls + r" \{\n(.*?)\n    \}\n", text, re.S)
        if not m: sys.exit(f"{cls} not found in {file}")
        body = m.group(1)
        methods = []
        for line in body.split("\n"):
            mm = re.match(r"        public static (?!unsafe)(.+?) (\w+)\((.*)\) => throw new global::System.NotImplementedException\(", line)
            if not mm: continue
            ret, name, params = mm.groups()
            if name not in ALL: continue
            ps = split_params(params)
            sig = ", ".join(p["raw"] for p in ps)
            methods.append(f"        public static {ret} {name}({sig}) {{ {gen_body(cls, name, ret, ps)} }}")
        out.append(f"namespace {ns}\n{{\n    public partial class {cls}\n    {{")
        out.extend(methods)
        out.append("    }\n}\n")
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    open(OUT, "w").write("\n".join(out))
    print(f"{OUT}: {sum(1 for l in out if l.startswith('        public static'))} overloads")


if __name__ == "__main__":
    main()
