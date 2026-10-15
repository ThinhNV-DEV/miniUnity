#!/usr/bin/env bash
# MiniUnity setup: fetch inputs, regenerate everything derived from Unity binaries, build.
# Nothing produced here is committed (see .gitignore). Re-run after updating Unity/packages.
#
#   ./setup.sh                    full setup
#   ./setup.sh --download-unity   Linux (container, CI): download the Unity Editor subset MiniUnity reads (~4 GB stream, ~1.6 GB kept)
#                                 into .cache/unity/<version>/Editor when no installed Editor of the project's version is found
#   ./setup.sh --check            report what is installed / missing / stale, change nothing
#   UNITY_EDITOR=/path/to/Unity/Editor  (folder containing Data/Managed) — auto-detected if unset
#   UNITY_PROJECT=..                     — the Unity project to build (default: parent folder)
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/env.sh"
cd "$MU_HOME"
mkdir -p "$MU_CACHE"
step() { echo; echo "== $*"; }
DOWNLOAD_UNITY=0; CHECK=0
for a in "$@"; do case "$a" in --download-unity) DOWNLOAD_UNITY=1;; --check) CHECK=1;; *) echo "unknown option $a"; exit 2;; esac; done

VERSION=$(sed -n 's/^m_EditorVersion: //p' "$UNITY_PROJECT/ProjectSettings/ProjectVersion.txt")
REVISION=$(sed -n 's/^m_EditorVersionWithRevision: .*(\(.*\))/\1/p' "$UNITY_PROJECT/ProjectSettings/ProjectVersion.txt")
find_editor() {
  [ -n "${UNITY_EDITOR:-}" ] && return 0
  for c in "$MU_CACHE/unity/$VERSION/Editor" "$HOME/Unity/Hub/Editor/$VERSION/Editor" "/opt/unity/Editor" "/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents" \
           "/c/Program Files/Unity/Hub/Editor/$VERSION/Editor" "/mnt/c/Program Files/Unity/Hub/Editor/$VERSION/Editor"; do
    [ -d "$c/Data/Managed/UnityEngine" ] && { UNITY_EDITOR="$c"; return 0; }
    [ -d "$c/Managed/UnityEngine" ] && { UNITY_EDITOR="$c"; return 0; }   # macOS .app/Contents layout
  done
  return 1
}

if [ "$CHECK" = 1 ]; then
  ok() { printf "  %-34s %s\n" "$1" "$2"; }
  echo "MiniUnity setup check (project $UNITY_PROJECT, Unity $VERSION)"
  if command -v dotnet >/dev/null && dotnet --list-sdks | grep -q '^8\.'; then ok ".NET 8 SDK" "$(dotnet --version)"; else ok ".NET 8 SDK" "MISSING (setup.sh installs it)"; fi
  if find_editor; then ok "Unity Editor" "$UNITY_EDITOR"; else ok "Unity Editor" "MISSING ($VERSION; install it, set UNITY_EDITOR, or setup.sh --download-unity)"; fi
  for p in com.unity.timeline com.unity.textmeshpro com.unity.cinemachine com.unity.ugui litmotion; do
    [ -d "$MU_CACHE/packages/$p" ] && ok "package $p" "$(cat "$MU_CACHE/packages/$p/.version" 2>/dev/null || echo present)" || ok "package $p" "MISSING"
  done
  for d in Engine/Generated Engine/Decompiled; do [ -d "$d" ] && ok "$d" "$(find "$d" -name '*.cs' | wc -l) files" || ok "$d" "MISSING"; done
  if [ ! -f "$MU_DLL" ]; then ok "engine build" "MISSING"; elif s=$(mu_stale) && [ -n "$s" ]; then ok "engine build" "STALE ($s changed)"; else ok "engine build" "current"; fi
  for t in python3 ffmpeg git; do command -v $t >/dev/null && ok "$t" "$(command -v $t)" || ok "$t" "missing$([ $t = ffmpeg ] && echo ' (record, Ogg / MP3)')"; done
  [ -d "$UNITY_PROJECT/Library/PackageCache" ] && ok "Library/PackageCache" "$(ls "$UNITY_PROJECT/Library/PackageCache" | wc -l) packages" || ok "Library/PackageCache" "missing (ua pkg.resolve --fetch; needed by compile / test)"
  exit 0
fi

# ---------------------------------------------------------------- 1. .NET 8 SDK
step ".NET SDK"
if ! (command -v dotnet >/dev/null && dotnet --list-sdks | grep -q '^8\.'); then
  echo "installing .NET 8 SDK into $MU_CACHE/dotnet"
  curl -sSL https://dot.net/v1/dotnet-install.sh -o "$MU_CACHE/dotnet-install.sh"
  bash "$MU_CACHE/dotnet-install.sh" --channel 8.0 --install-dir "$MU_CACHE/dotnet"
  source "$MU_HOME/env.sh"
fi
dotnet --version

# ---------------------------------------------------------------- 2. Unity Editor managed DLLs (API surface)
step "Unity Editor"
if ! find_editor && [ "$DOWNLOAD_UNITY" = 1 ]; then
  [ "$(uname -s)" = Linux ] || { echo "--download-unity downloads the Linux Editor; install Unity $VERSION with Unity Hub instead"; exit 1; }
  [ -n "$REVISION" ] || { echo "no revision in ProjectVersion.txt (m_EditorVersionWithRevision)"; exit 1; }
  dest="$MU_CACHE/unity/$VERSION"; rm -rf "$dest"; mkdir -p "$dest"
  url="https://download.unity3d.com/download_unity/$REVISION/LinuxEditorInstaller/Unity-$VERSION.tar.xz"
  echo "downloading the Editor subset from $url (no license needed: only managed DLLs, reference assemblies and built-in packages are read)"
  curl -sSfL "$url" | tar -xJ -C "$dest" --wildcards 'Editor/Data/Managed/*' 'Editor/Data/MonoBleedingEdge/lib/mono/4.7.1-api/*' 'Editor/Data/NetStandard/*' \
      'Editor/Data/Resources/PackageManager/BuiltInPackages/*' 'Editor/Data/Resources/unity_builtin_extra' 'Editor/Data/UnityReferenceAssemblies/*'
  [ -d "$dest/Editor/Data/Managed/UnityEngine" ] || { echo "download incomplete"; exit 1; }
  UNITY_EDITOR="$dest/Editor"
fi
find_editor || { echo "Unity $VERSION Editor not found. Install it (Unity Hub), set UNITY_EDITOR=<.../Editor>, or on Linux run ./setup.sh --download-unity."; exit 1; }
DATA="$UNITY_EDITOR/Data"; [ -d "$DATA" ] || DATA="$UNITY_EDITOR"
MANAGED="$DATA/Managed/UnityEngine"
echo "using $UNITY_EDITOR (project wants $VERSION)"

# ---------------------------------------------------------------- 3. package sources (versions from packages-lock.json)
step "package sources"
PKG="$MU_CACHE/packages"; mkdir -p "$PKG"
lockver() { python3 -c "import json,sys;print(json.load(open('$UNITY_PROJECT/Packages/packages-lock.json'))['dependencies'][sys.argv[1]]['version'])" "$1"; }
for p in com.unity.timeline com.unity.textmeshpro com.unity.cinemachine; do
  v=$(lockver $p)
  if [ "$(cat "$PKG/$p/.version" 2>/dev/null)" != "$v" ]; then
    echo "  $p@$v"
    rm -rf "$PKG/$p"; mkdir -p "$PKG/$p"
    tgz="$MU_CACHE/$p-$v.tgz"
    curl -sSfL "https://download.packages.unity.com/$p/-/$p-$v.tgz" -o "$tgz"
    # integrity: the registry publishes the tarball's SHA-1
    want=$(curl -sSfL "https://download.packages.unity.com/$p" | python3 -c "import json,sys;print(json.load(sys.stdin)['versions'][sys.argv[1]]['dist']['shasum'])" "$v")
    got=$(sha1sum "$tgz" | cut -d' ' -f1)
    [ "$want" = "$got" ] || { echo "checksum mismatch for $p@$v: registry $want, downloaded $got"; rm -f "$tgz"; exit 1; }
    tar -xzf "$tgz" -C "$PKG/$p" --strip-components=1; rm -f "$tgz"
    echo "$v" > "$PKG/$p/.version"
  fi
done
# uGUI is a built-in package: take it from the Editor
rm -rf "$PKG/com.unity.ugui"; cp -r "$DATA/Resources/PackageManager/BuiltInPackages/com.unity.ugui" "$PKG/com.unity.ugui"
# LitMotion (git package): only Ease / EaseUtility are compiled, at the locked commit
LMHASH=$(python3 -c "import json;print(json.load(open('$UNITY_PROJECT/Packages/packages-lock.json'))['dependencies']['com.annulusgames.lit-motion']['hash'])")
mkdir -p "$PKG/litmotion"
for f in Ease.cs EaseUtility.cs; do
  curl -sSLf "https://raw.githubusercontent.com/annulusgames/LitMotion/$LMHASH/src/LitMotion/Assets/LitMotion/Runtime/$f" -o "$PKG/litmotion/$f"
done

# ---------------------------------------------------------------- 4. tools
step "tools"
dotnet build tools/stubgen/stubgen.csproj -c Release -o "$MU_CACHE/tools/stubgen" -v q -nologo
dotnet build tools/monofloat/monofloat.csproj -c Release -o "$MU_CACHE/tools/monofloat" -v q -nologo
[ -x "$MU_CACHE/tools/ilspy/ilspycmd" ] || dotnet tool install --tool-path "$MU_CACHE/tools/ilspy" ilspycmd --version 8.2.0.7535

# ---------------------------------------------------------------- 5. API stubs from the Editor DLLs
step "stubs (Engine/Generated)"
"$MU_HOME/scripts/gen_stubs.sh" "$DATA"

# ---------------------------------------------------------------- 6. pure-managed engine code decompiled from the Editor DLLs
step "decompile (Engine/Decompiled)"
"$MU_HOME/scripts/decompile.sh" "$MANAGED"

# ---------------------------------------------------------------- 7. Mono float semantics pass + build
step "monofloat + build"
"$MU_HOME/scripts/build_engine.sh"
echo; echo "MiniUnity ready. Try: ./ua.sh help   (a project host such as Tools/MiniUnityVideo builds on top: its ./build.sh)"
