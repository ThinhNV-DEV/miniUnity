#!/usr/bin/env bash
# Unity project operations for agents without the Unity Editor (JSON in / out). See AGENT.md.
#   ./ua.sh help                         list commands
#   ./ua.sh go.tree Assets/Scenes/Main.unity --depth 2
#   UNITY_PROJECT=/path/to/project ./ua.sh asset.find --type prefab
#   MiniUnity is rebuilt first when its engine sources changed since the last build; NOCOMPILE=0 forces a rebuild,
#   MU_NOREBUILD=1 always reuses the existing build
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"
cd "$MU_HOME"
mu_ensure_build || { echo '{"ok": false, "error": "MiniUnity build failed (compiler errors on stderr)"}'; exit 1; }
[ -n "$UNITY_PROJECT" ] || [ "${1:-help}" = help ] || { echo '{"ok": false, "error": "no Unity project: MiniUnity is not inside one; set UNITY_PROJECT=/path/to/project"}'; exit 2; }
mu ua "${UNITY_PROJECT:-$MU_HOME}" "$@"
