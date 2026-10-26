#!/usr/bin/env bash
# Engine/Generated/*.g.cs: compilable "throw NotImplementedException" stubs for the Unity API, read from the Editor's DLL metadata.
# Members hand-implemented in Engine/Core are skipped automatically (tools/handmembers.py); config/stubs.manual.cfg adds rules.
#   gen_stubs.sh <Editor/Data> [unity version]   the version defaults to the one in the Editor's path (…/6000.3.21f1/Editor/Data)
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/../env.sh"
DATA="$1"; M="$DATA/Managed/UnityEngine"; MB="$DATA/MonoBleedingEdge/lib/mono/4.7.1-api"
VERSION="${2:-$(echo "$DATA" | grep -oE '[0-9]{4}\.[0-9]+\.[0-9]+[abfpx][0-9]+' | tail -1)}"
[ -n "$VERSION" ] || { echo "gen_stubs.sh: pass the Unity version as the second argument (none in the path $DATA)" >&2; exit 1; }
cd "$MU_HOME"
# the API level decides which Engine/Core/Versioned folder is the hand-written code (and so which members the stubs leave out)
LEVEL=$(python3 tools/unity_defines.py "$VERSION" level)
CFG="$MU_CACHE/stubs.cfg"
{
  cat config/stubs.manual.cfg; echo
  python3 tools/handmembers.py Engine/Core --level "$LEVEL" | grep -v -F -f <(grep ": \*$" config/stubs.manual.cfg | sed "s/: \*$/:/") | while read -r l; do
    echo "$l"; t="${l%%:*}"; case "$l" in *".ctor"*) echo "!noctor $t";; esac
  done
  # types replaced by decompiled real implementations
  cat config/math_types.txt config/managed_types.txt | awk '{t=$2; sub(/`.*/,"",t); print "-"t}'
} > "$CFG"
MODS=""
# a module this Editor does not have (RenderAs2DModule is Unity 6 only) is skipped
while read -r m; do [ -n "$m" ] && [ -f "$M/UnityEngine.$m.dll" ] && MODS="$MODS $M/UnityEngine.$m.dll"; done < config/engine_modules.txt
while read -r m; do [ -n "$m" ] && MODS="$MODS $M/$m.dll"; done < config/editor_modules.txt
rm -rf Engine/Generated; mkdir -p Engine/Generated
python3 tools/unity_defines.py "$VERSION" props Engine/Generated/UnityVersion.props
STUB_ALL_INTERNAL=1 dotnet "$MU_CACHE/tools/stubgen/stubgen.dll" Engine/Generated "$CFG" \
  "$MB/mscorlib.dll" "$MB/System.dll" "$MB/System.Core.dll" "$MB/System.Xml.dll" "$MB/Facades/netstandard.dll" -- $MODS 2>&1 | grep -v WARN
