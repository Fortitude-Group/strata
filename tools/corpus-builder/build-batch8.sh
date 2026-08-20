#!/usr/bin/env bash
# Batch 8: cross the 50-library line. Small single-file libraries plus a miniz download.
set -uo pipefail
C=/out/corpus_opt; H=/out/holdout_opt; mkdir -p "$C" "$H"
apt-get update -qq >/dev/null; apt-get install -y -qq clang git binutils curl unzip >/dev/null
clone() { git clone -q --depth 1 --branch "$2" "$1" "$3" 2>/dev/null && echo "cloned $3" || echo "CLONE-FAIL $1 $2"; }
mk() { local lib="$1" ver="$2"
  for cc in gcc clang; do for opt in O2 O3 Os; do
    out="$C/$lib-$ver-$cc-$opt.so"; [ "$cc" = clang ] && out="$H/$lib-$ver-$cc-$opt.so"
    if $cc -shared -fPIC "-$opt" $CF $SRCS -o "$out" 2>/dev/null; then
      [ "$cc" = clang ] && strip -s "$out" 2>/dev/null; echo "ok   $lib-$ver-$cc-$opt"
    else echo "FAIL $lib-$ver-$cc-$opt"; fi
  done; done
}
cd /tmp

clone https://github.com/cesanta/frozen master frozen && ( cd frozen; CF="-I."; SRCS="frozen.c"; mk frozen 1.7 )
clone https://github.com/cesanta/slre master slre && ( cd slre; CF="-I."; SRCS="slre.c"; mk slre 1.0 )
clone https://github.com/rxi/log.c master logc && ( cd logc; CF="-Isrc -DLOG_USE_COLOR"; SRCS="src/log.c"; mk logc 0.1 )
clone https://github.com/codeplea/tinyexpr master tinyexpr && ( cd tinyexpr; CF="-I."; SRCS="tinyexpr.c"; mk tinyexpr 1.0 )
clone https://github.com/gpakosz/whereami master whereami && ( cd whereami; CF="-Isrc"; SRCS="src/whereami.c"; mk whereami 1.0 )
clone https://github.com/lvandeve/lodepng master lodepng && ( cd lodepng; CF="-I."; SRCS="lodepng.cpp"; cp lodepng.cpp lodepng.c 2>/dev/null; SRCS="lodepng.c"; mk lodepng 1.0 )

# jsmn: header-only; make an impl translation unit
clone https://github.com/zserge/jsmn master jsmn && ( cd jsmn; printf '#define JSMN_STATIC\n#include "jsmn.h"\nint _jsmn_tu(void){return 0;}\n' > tu.c; CF="-I."; SRCS="tu.c"; mk jsmn 1.1 )

# miniz: download the release amalgamation zip
if curl -fsSL --max-time 90 https://github.com/richgel999/miniz/releases/download/3.0.2/miniz-3.0.2.zip -o /tmp/mz.zip 2>/dev/null; then
  ( mkdir -p /tmp/mzx; cd /tmp/mzx; unzip -q /tmp/mz.zip; CF="-I."; SRCS="miniz.c"; mk miniz 3.0.2 )
else echo "miniz download FAILED"; fi

echo "=== corpus_opt distinct libs ==="; ls -1 "$C" | sed -E 's/-[0-9r].*//' | sort -u | tr '\n' ' '; echo
echo "distinct: $(ls -1 "$C" | sed -E 's/-[0-9r].*//' | sort -u | wc -l)  files: $(ls -1 "$C" | wc -l)"
