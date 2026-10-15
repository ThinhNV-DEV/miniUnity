#!/usr/bin/env bash
# Engine/Decompiled: real managed implementations of small engine types (math structs, pools, Playable bases, TextCore
# glyph structs), decompiled locally from the Editor DLLs with ilspycmd. Native (extern) calls are redirected to
# MiniUnity.Native (hand-written, Engine/Core/NativeMath.cs) / MiniUnity.NativeM (generated throwing stubs).
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/../env.sh"
M="$1"
cd "$MU_HOME"
export DOTNET_ROLL_FORWARD=Major
ILSPY="$MU_CACHE/tools/ilspy/ilspycmd"
rm -rf Engine/Decompiled; mkdir -p Engine/Decompiled/Math Engine/Decompiled/Managed
decomp() { # list outDir nativeClass
  local tmp="$MU_CACHE/decomp_raw/$(basename "$2")"; rm -rf "$tmp"; mkdir -p "$tmp"
  while read -r mod t; do
    [ -z "$mod" ] && continue
    n=$(echo "${t##*.}" | tr '`' '_')
    "$ILSPY" -r "$M" -t "$t" "$M/UnityEngine.$mod.dll" 2> "$tmp/$n.err" | grep -v "You are not using the latest\|^Latest version is" > "$tmp/$n.cs" || true
    # a failed decompile used to leave an empty file and a confusing build error later: stop here with ilspycmd's message
    if ! grep -qE "(class|struct|interface|enum) " "$tmp/$n.cs"; then
      echo "decompile failed: $t from UnityEngine.$mod.dll" >&2; sed 's/^/  /' "$tmp/$n.err" | grep -v "latest version" >&2 || true; exit 1
    fi
  done < "$1"
  python3 tools/fixdecomp.py "$tmp" "$2" "$3"
}
# math: redirects go to the hand-written MiniUnity.Native (NativeMath.cs); the generated list is only a reference
decomp config/math_types.txt Engine/Decompiled/Math "$MU_CACHE/NativeMath.reference.cs"
sed -i 's/^\tprivate int m_TangentMode;/\tinternal int m_TangentMode;/' Engine/Decompiled/Math/Keyframe.cs
# other managed types: redirects to generated throwing stubs
decomp config/managed_types.txt Engine/Decompiled/Managed Engine/Decompiled/Managed/_NativeRedirects.cs
sed -i 's/public static partial class Native {/public static partial class NativeM {/' Engine/Decompiled/Managed/_NativeRedirects.cs
sed -i 's/MiniUnity\.Native\./MiniUnity.NativeM./g' Engine/Decompiled/Managed/*.cs
echo "decompiled $(ls Engine/Decompiled/Math | wc -l) math + $(ls Engine/Decompiled/Managed | wc -l) managed files"
