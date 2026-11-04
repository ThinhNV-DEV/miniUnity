#!/usr/bin/env bash
# Moved to Tools/MiniUnityVideo/build.sh (the VideoRecreate host of MiniUnity); kept so existing commands and docs keep working.
# Will be removed when MiniUnity becomes a submodule.
exec "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/../Tools/MiniUnityVideo/build.sh" "$@"
