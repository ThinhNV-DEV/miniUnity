# Sourced by the MiniUnity scripts. All paths are relative to this folder.
MU_HOME="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export MU_HOME
# Unity project (folder that contains Assets/ and Packages/); default: the repo this folder lives in
export UNITY_PROJECT="$(cd "${UNITY_PROJECT:-$MU_HOME/..}" && pwd)"
export MU_CACHE="$MU_HOME/.cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
# prefer a local .NET 8 SDK installed by setup.sh, otherwise the one on PATH
if [ -x "$MU_CACHE/dotnet/dotnet" ]; then export DOTNET_ROOT="$MU_CACHE/dotnet"; export PATH="$MU_CACHE/dotnet:$PATH"; fi
# package script folders whose .cs.meta GUIDs are needed for m_Script references
export MINIUNITY_SCRIPTDIRS="$MU_CACHE/packages/com.unity.timeline/Runtime:$MU_CACHE/packages/com.unity.textmeshpro/Scripts/Runtime:$MU_CACHE/packages/com.unity.ugui/Runtime:$MU_CACHE/packages/com.unity.cinemachine/Runtime"
MU_DLL="$MU_HOME/Engine/bin/Release/net8.0/MiniUnity.dll"
mu() { dotnet "$MU_DLL" "$@"; }
# Prints the first engine source newer than the build (empty when the build is current or missing). Video / game sources the
# csproj also compiles are not checked: the `ua` / `mcp` commands do not depend on them.
mu_stale() { [ -f "$MU_DLL" ] && find "$MU_HOME/Engine/Core" "$MU_HOME/Engine/Shims" "$MU_HOME/Engine/MiniUnity.csproj" "$MU_HOME/config" \
               -newer "$MU_DLL" \( -name '*.cs' -o -name '*.csproj' -o -name '*.cfg' -o -name '*.txt' \) -print -quit 2>/dev/null; }
# Build MiniUnity when there is no build, the engine sources changed since the last one, or NOCOMPILE=0; MU_NOREBUILD=1 never
# rebuilds an existing build. Build output goes to stderr so that stdout stays JSON.
mu_ensure_build() {
  local why=""
  if [ ! -f "$MU_DLL" ]; then why="no build yet"
  elif [ "${NOCOMPILE:-}" = 0 ]; then why="NOCOMPILE=0"
  elif [ "${MU_NOREBUILD:-0}" != 1 ] && [ -n "$(mu_stale)" ]; then why="$(mu_stale) changed"
  fi
  [ -z "$why" ] && return 0
  echo "[MiniUnity] building ($why)…" >&2
  "$MU_HOME/scripts/build_engine.sh" >&2
}
