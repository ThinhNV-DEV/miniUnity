#!/usr/bin/env python3
"""Unity's version scripting defines for an Editor version, e.g. 6000.3.21f1 → UNITY_6000_3_OR_NEWER, …, UNITY_2023_3_OR_NEWER, …

Same rules as ProjectCompiler.BaseDefines (Engine/Core/Agent/UaCompile.cs): UNITY_<major>, UNITY_<major>_<minor>,
UNITY_<major>_<minor>_<patch>, UNITY_5_3..5_6_OR_NEWER, every UNITY_<year>_<minor>_OR_NEWER up to the version (Unity 6 still
defines the 2017–2023.3 ones) and, from Unity 6, UNITY_6000_<minor>_OR_NEWER.

  unity_defines.py <version>                 → defines separated by ';'
  unity_defines.py <version> --props <file>  → MSBuild props setting MiniUnityVersionDefines (imported by MiniUnity.Common.props)
"""
import re
import sys


def defines(version):
    m = re.match(r"^(\d+)\.(\d+)\.(\d+)", version)
    if not m:
        raise SystemExit(f"not a Unity version: {version}")
    major, minor, patch = (int(g) for g in m.groups())
    d = [f"UNITY_{major}", f"UNITY_{major}_{minor}", f"UNITY_{major}_{minor}_{patch}"]
    d += [f"UNITY_5_{mi}_OR_NEWER" for mi in (3, 4, 5, 6)]
    for y in range(2017, min(major, 2023) + 1):
        last = minor if y == major else 3 if y >= 2020 else 4
        d += [f"UNITY_{y}_{mi}_OR_NEWER" for mi in range(1, last + 1)]
    for v in range(6000, major + 1):
        # minors of a past Unity 6 major are not known here; 9 covers the releases so far
        last = minor if v == major else 9
        d += [f"UNITY_{v}_{mi}_OR_NEWER" for mi in range(0, last + 1)]
    return d


if __name__ == "__main__":
    d = ";".join(defines(sys.argv[1]))
    if len(sys.argv) > 3 and sys.argv[2] == "--props":
        with open(sys.argv[3], "w") as f:
            f.write("<Project>\n  <!-- written by setup.sh for Unity %s; do not edit -->\n  <PropertyGroup>\n"
                    "    <MiniUnityVersionDefines>%s</MiniUnityVersionDefines>\n  </PropertyGroup>\n</Project>\n" % (sys.argv[1], d))
    else:
        print(d)
