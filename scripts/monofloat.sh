#!/usr/bin/env bash
# Mono float semantics (double intermediates, narrowed on store) for the decompiled math of the engine and, when a host project
# is given, for the project sources it compiles, so results match Unity bit-for-bit.
#   scripts/monofloat.sh                      engine only  → Engine/Transformed + Engine/Transformed.props
#   scripts/monofloat.sh <host.csproj>        + the host's project sources → <host dir>/Transformed + <host dir>/Transformed.props
# The rewriter needs the semantic model of everything that is compiled together, so it reads the engine and host items in one pass.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/../env.sh"
ENGINE="$MU_HOME/Engine"
HOST="${1:-}"
[ -n "$HOST" ] && HOST="$(cd "$(dirname "$HOST")" && pwd)/$(basename "$HOST")"
HOSTDIR="${HOST:+$(dirname "$HOST")}"
# evaluate the item lists without the props of a previous run (they replace sources by their transformed copies)
rm -f "$ENGINE/Transformed.props" ${HOSTDIR:+"$HOSTDIR/Transformed.props"}
dotnet msbuild "$ENGINE/MiniUnity.csproj" -getItem:Compile -getProperty:DefineConstants > "$MU_CACHE/items.json"
if [ -n "$HOST" ]; then dotnet msbuild "$HOST" -getItem:Compile -p:UnityProject="$UNITY_PROJECT/" > "$MU_CACHE/items_host.json"; else rm -f "$MU_CACHE/items_host.json"; fi
python3 - "$MU_CACHE" "$ENGINE" "${HOSTDIR:-}" "$UNITY_PROJECT" <<'PY'
import json,sys,os
cache,eng,hostdir,proj=sys.argv[1],sys.argv[2],sys.argv[3],os.path.realpath(sys.argv[4])
d=json.load(open(cache+'/items.json'))
items=[i['FullPath'] for i in d['Items']['Compile']]
eng_t=[i for i in items if '/Engine/Decompiled/Math/' in i]
host_t=[]
if hostdir:
    h=json.load(open(cache+'/items_host.json'))
    hi=[i['FullPath'] for i in h['Items']['Compile']]
    items+=hi
    host_t=[i for i in hi if os.path.realpath(i).startswith(proj+'/Assets/')]
open(cache+'/mf_all.txt','w').write('\n'.join(items))
open(cache+'/mf_defs.txt','w').write(d['Properties']['DefineConstants'])
open(cache+'/mf_targets.txt','w').write('\n'.join(eng_t+host_t))
def props(dest, tg, root_prop):
    lines=['<Project>','  <ItemGroup>']
    for t in tg:
        rel=os.path.relpath(t,'/')
        lines.append(f'    <Compile Remove="{t}" />')
        lines.append(f'    <Compile Include="{root_prop}Transformed/{rel}" Link="MonoFloat/{os.path.basename(t)}" />')
    lines+=['  </ItemGroup>','</Project>']
    open(dest,'w').write('\n'.join(lines))
props(eng+'/Transformed.props.new', eng_t, '$(MSBuildThisFileDirectory)')
if hostdir: props(hostdir+'/Transformed.props.new', host_t, '$(MSBuildThisFileDirectory)')
open(cache+'/mf_split.json','w').write(json.dumps({'engine':eng_t,'host':host_t}))
PY
rm -rf "$MU_CACHE/mf_out"
dotnet "$MU_CACHE/tools/monofloat/monofloat.dll" "$MU_CACHE/mf_defs.txt" "$MU_CACHE/mf_all.txt" "$MU_CACHE/mf_targets.txt" "$MU_CACHE/mf_out"
python3 - "$MU_CACHE" "$ENGINE" "${HOSTDIR:-}" <<'PY'
import json,sys,os,shutil
cache,eng,hostdir=sys.argv[1],sys.argv[2],sys.argv[3]
split=json.load(open(cache+'/mf_split.json'))
for key,root in (('engine',eng),('host',hostdir)):
    if not root: continue
    dst=os.path.join(root,'Transformed')
    shutil.rmtree(dst,ignore_errors=True)
    for t in split[key]:
        rel=os.path.relpath(t,'/')
        src=os.path.join(cache,'mf_out',rel)
        os.makedirs(os.path.dirname(os.path.join(dst,rel)),exist_ok=True)
        shutil.copy2(src,os.path.join(dst,rel))
PY
mv "$ENGINE/Transformed.props.new" "$ENGINE/Transformed.props"
[ -n "$HOSTDIR" ] && mv "$HOSTDIR/Transformed.props.new" "$HOSTDIR/Transformed.props"
exit 0
