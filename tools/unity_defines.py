#!/usr/bin/env python3
"""Version defines Unity sets for an Editor version, and the API level MiniUnity's hand-written code follows.

    unity_defines.py <version> props <out.props>   MSBuild props for the engine build (Engine/Generated/UnityVersion.props)
    unity_defines.py <version> level               the API level: 2022 (Unity 2022.3 and older) or 6000 (Unity 6)

Unity defines UNITY_<major>, UNITY_<major>_<minor>, UNITY_<major>_<minor>_<patch> and every UNITY_<year>_<minor>_OR_NEWER up to
its own release. Unity 6 is the continuation of 2023.3, so it keeps all the year-based ones through 2023.3 and adds
UNITY_6000_<minor>_OR_NEWER. The same rule is in UaCompile.BaseDefines for the project's own assemblies.
"""
import re
import sys

LAST_MINOR = {2017: 4, 2018: 4, 2019: 4, 2020: 3, 2021: 3, 2022: 3, 2023: 3}


def defines(version):
    m = re.match(r"^(\d+)\.(\d+)\.(\d+)", version)
    if not m:
        raise SystemExit(f"not a Unity version: {version}")
    major, minor, patch = (int(x) for x in m.groups())
    out = [f"UNITY_{major}", f"UNITY_{major}_{minor}", f"UNITY_{major}_{minor}_{patch}"]
    out += [f"UNITY_5_{n}_OR_NEWER" for n in (3, 4, 5, 6)]
    for year in sorted(LAST_MINOR):
        if year > major:
            break
        last = minor if year == major else LAST_MINOR[year]
        out += [f"UNITY_{year}_{n}_OR_NEWER" for n in range(1, last + 1)]
    if major >= 6000:
        out += [f"UNITY_6000_{n}_OR_NEWER" for n in range(0, minor + 1)]
    return out


def level(version):
    return "6000" if int(version.split(".")[0]) >= 6000 else "2022"


if __name__ == "__main__":
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    version, what = sys.argv[1], sys.argv[2]
    if what == "level":
        print(level(version))
    elif what == "props":
        with open(sys.argv[3], "w") as f:
            f.write("<Project>\n  <!-- written by scripts/gen_stubs.sh for the Editor the stubs came from -->\n  <PropertyGroup>\n")
            f.write(f"    <MiniUnityUnityVersion>{version}</MiniUnityUnityVersion>\n")
            f.write(f"    <MiniUnityApiLevel>{level(version)}</MiniUnityApiLevel>\n")
            f.write(f"    <MiniUnityVersionDefines>{';'.join(defines(version))}</MiniUnityVersionDefines>\n")
            f.write("  </PropertyGroup>\n</Project>\n")
    else:
        raise SystemExit(__doc__)
