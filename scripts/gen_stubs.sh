#!/usr/bin/env bash
# Engine/Generated/*.g.cs: compilable "throw NotImplementedException" stubs for the Unity API, read from the Editor's DLL metadata.
# Members hand-implemented in Engine/Core are skipped automatically (tools/handmembers.py); config/stubs.manual.cfg adds rules.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/../env.sh"
DATA="$1"; M="$DATA/Managed/UnityEngine"; MB="$DATA/MonoBleedingEdge/lib/mono/4.7.1-api"
cd "$MU_HOME"
CFG="$MU_CACHE/stubs.cfg"
{
  cat config/stubs.manual.cfg; echo
  python3 tools/handmembers.py Engine/Core | grep -v -F -f <(grep ": \*$" config/stubs.manual.cfg | sed "s/: \*$/:/") | while read -r l; do
    echo "$l"; t="${l%%:*}"; case "$l" in *".ctor"*) echo "!noctor $t";; esac
  done
  # types replaced by decompiled real implementations
  cat config/math_types.txt config/managed_types.txt | awk '{t=$2; sub(/`.*/,"",t); print "-"t}'
} > "$CFG"
MODS=""
while read -r m; do [ -n "$m" ] && MODS="$MODS $M/UnityEngine.$m.dll"; done < config/engine_modules.txt
while read -r m; do [ -n "$m" ] && MODS="$MODS $M/$m.dll"; done < config/editor_modules.txt
rm -rf Engine/Generated; mkdir -p Engine/Generated
STUB_ALL_INTERNAL=1 dotnet "$MU_CACHE/tools/stubgen/stubgen.dll" Engine/Generated "$CFG" \
  "$MB/mscorlib.dll" "$MB/System.dll" "$MB/System.Core.dll" "$MB/System.Xml.dll" "$MB/Facades/netstandard.dll" -- $MODS 2>&1 | grep -v WARN
