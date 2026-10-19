#!/usr/bin/env bash
# MCP server (stdio) exposing the `ua` agent commands as tools. Register it with an MCP client, e.g. .mcp.json:
#   { "mcpServers": { "miniunity": { "command": "/path/to/MiniUnity/mcp.sh", "args": ["/path/to/UnityProject"] } } }
# The project argument defaults to $UNITY_PROJECT (the project MiniUnity lives in, if any). Builds MiniUnity first when there is no build or its engine
# sources changed (see env.sh › mu_ensure_build).
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"
[ -n "${1:-$UNITY_PROJECT}" ] || { echo "usage: mcp.sh /path/to/UnityProject (MiniUnity is not inside a project)" >&2; exit 2; }
PROJECT="$(cd "${1:-$UNITY_PROJECT}" && pwd)"
cd "$MU_HOME"
mu_ensure_build
exec dotnet "$MU_DLL" mcp "$PROJECT"
