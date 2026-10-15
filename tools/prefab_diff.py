#!/usr/bin/env python3
"""So hai file .prefab (Unity YAML) sau khi chuẩn hoá fileID theo đường dẫn hierarchy.
Dùng: prefab_diff.py <oracle.prefab> <ours.prefab>   → in số object, số field khác, vài dòng khác đầu tiên.
fileID cục bộ do Unity / MiniUnity sinh ngẫu nhiên nên không so trực tiếp được; script đổi mỗi fileID thành
"<class>@<đường dẫn GameObject>#<thứ tự>" rồi so từng document."""
import re, sys
from collections import defaultdict

HDR = re.compile(r"^--- !u!(\d+) &(-?\d+)", re.M)


def docs(path):
    s = open(path, encoding="utf-8").read()
    out = []
    ms = list(HDR.finditer(s))
    for i, m in enumerate(ms):
        body = s[m.end(): ms[i + 1].start() if i + 1 < len(ms) else len(s)]
        out.append((int(m.group(1)), m.group(2), body))
    return out


def canon(path):
    ds = docs(path)
    by = {fid: (cls, body) for cls, fid, body in ds}
    go_name, go_of, father = {}, {}, {}
    for cls, fid, body in ds:
        if cls == 1:
            m = re.search(r"m_Name: (.*)", body)
            go_name[fid] = m.group(1).strip() if m else "?"
        m = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
        if m:
            go_of[fid] = m.group(1)
        if cls in (4, 224):
            m = re.search(r"m_Father: \{fileID: (-?\d+)\}", body)
            father[fid] = m.group(1) if m else "0"
    tr_of_go = {g: t for t, g in go_of.items() if by[t][0] in (4, 224)}

    def gpath(go):
        parts, t = [], tr_of_go.get(go)
        seen = 0
        while t and t != "0" and seen < 200:
            parts.append(go_name.get(go_of.get(t, ""), "?"))
            t = father.get(t, "0")
            seen += 1
        return "/".join(reversed(parts))

    label, count = {}, defaultdict(int)
    for cls, fid, body in ds:
        owner = fid if cls == 1 else go_of.get(fid)
        base = f"{cls}@{gpath(owner) if owner else '?'}"
        count[base] += 1
        label[fid] = f"{base}#{count[base]}"
    res = {}
    for cls, fid, body in ds:
        b = re.sub(r"fileID: (-?\d+)", lambda m: "fileID: " + label.get(m.group(1), m.group(1)), body)
        res[label[fid]] = b
    return res


def main():
    a, b = canon(sys.argv[1]), canon(sys.argv[2])
    ka, kb = set(a), set(b)
    same = sum(1 for k in ka & kb if a[k] == b[k])
    print(f"objects: oracle {len(a)}, ours {len(b)}, common {len(ka & kb)}, identical {same}")
    for k in sorted(ka - kb)[:5]:
        print("  only oracle:", k)
    for k in sorted(kb - ka)[:5]:
        print("  only ours:  ", k)
    shown = 0
    for k in sorted(ka & kb):
        if a[k] != b[k] and shown < 6:
            la, lb = a[k].splitlines(), b[k].splitlines()
            diff = [(x, y) for x, y in zip(la, lb) if x != y][:2]
            print(f"  differs: {k}: {diff}")
            shown += 1


if __name__ == "__main__":
    main()
