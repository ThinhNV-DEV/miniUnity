#!/usr/bin/env bash
# Reference check for YAML files: duplicate fileIDs, dangling local refs, unknown GUIDs.   ./validate.sh Assets/VideoRecreate/Scenes/20_UICanvas.unity ...
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"
args=(); for f in "$@"; do [[ "$f" = /* ]] && args+=("$f") || args+=("$UNITY_PROJECT/$f"); done
mu validate "$UNITY_PROJECT" "${args[@]}"
