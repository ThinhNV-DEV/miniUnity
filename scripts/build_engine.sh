#!/usr/bin/env bash
# Build the MiniUnity engine (Engine/MiniUnity.csproj: ua, mcp, generic verbs) with the Mono-float pass over its math.
#   MONOFLOAT=0 skips the pass. Project hosts (e.g. Tools/MiniUnityVideo) build the engine as part of their own build.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/../env.sh"
[ -d "$MU_HOME/Engine/Generated" ] || { echo "run ./setup.sh first" >&2; exit 1; }
[ "${MONOFLOAT:-1}" != 0 ] && "$MU_HOME/scripts/monofloat.sh" >/dev/null
out=$(dotnet build "$MU_HOME/Engine/MiniUnity.csproj" -c Release -v q -nologo -p:MonoFloat="${MONOFLOAT:-1}" 2>&1) || { echo "$out" | grep -E " error " | sort -u | head -20 >&2; exit 1; }
