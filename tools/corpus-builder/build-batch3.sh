#!/usr/bin/env bash
# Batch 3: adds larger/more-diverse libraries and THREE more compression libraries (brotli, zopfli,
# fastlz) as targeted counter-examples for the bzip2/lz4/zstd confusion. Appends optimized builds to
# /out/corpus_opt (gcc) and /out/holdout_opt (clang, stripped).
set -uo pipefail
C=/out/corpus_opt; H=/out/holdout_opt; mkdir -p "$C" "$H"
apt-get update -qq >/dev/null; apt-get install -y -qq clang git binutils >/dev/null
clone() { git clone -q --depth 1 --branch "$2" "$1" "$3" 2>/dev/null && echo "cloned $3" || echo "CLONE-FAIL $1 $2"; }
build() { # <lib> <ver> <dir> <cflags> <src...>
  local lib="$1" ver="$2" dir="$3" cflags="$4"; shift 4
  ( cd "$dir" 2>/dev/null || { echo "no dir $dir"; return; }
    for cc in gcc clang; do for opt in O2 O3 Os; do
      out="$C/$lib-$ver-$cc-$opt.so"; [ "$cc" = clang ] && out="$H/$lib-$ver-$cc-$opt.so"
      if $cc -shared -fPIC "-$opt" $cflags "$@" -o "$out" 2>/dev/null; then
        [ "$cc" = clang ] && strip -s "$out" 2>/dev/null; echo "ok   $lib-$ver-$cc-$opt"
      else echo "FAIL $lib-$ver-$cc-$opt"; fi
    done; done )
}
cd /tmp

# lua — all core .c except the two CLI mains
if clone https://github.com/lua/lua v5.4.6 lua; then
  ( cd lua; SRCS=$(ls *.c | grep -vE '^lua\.c$|^luac\.c$' | tr '\n' ' ')
    for cc in gcc clang; do for opt in O2 O3 Os; do
      out="$C/lua-5.4.6-$cc-$opt.so"; [ "$cc" = clang ] && out="$H/lua-5.4.6-$cc-$opt.so"
      if $cc -shared -fPIC "-$opt" $SRCS -o "$out" 2>/dev/null; then [ "$cc" = clang ] && strip -s "$out" 2>/dev/null; echo "ok lua-$cc-$opt"; else echo "FAIL lua-$cc-$opt"; fi
    done; done )
fi

# brotli — common/dec/enc, include dir
if clone https://github.com/google/brotli v1.1.0 brotli; then
  ( cd brotli; SRCS=$(ls c/common/*.c c/dec/*.c c/enc/*.c 2>/dev/null | tr '\n' ' ')
    for cc in gcc clang; do for opt in O2 O3 Os; do
      out="$C/brotli-1.1.0-$cc-$opt.so"; [ "$cc" = clang ] && out="$H/brotli-1.1.0-$cc-$opt.so"
      if $cc -shared -fPIC "-$opt" -Ic/include $SRCS -o "$out" 2>/dev/null; then [ "$cc" = clang ] && strip -s "$out" 2>/dev/null; echo "ok brotli-$cc-$opt"; else echo "FAIL brotli-$cc-$opt"; fi
    done; done )
fi

clone https://github.com/google/zopfli zopfli-1.0.3 zopfli && build zopfli 1.0.3 zopfli "" src/zopfli/blocksplitter.c src/zopfli/cache.c src/zopfli/deflate.c src/zopfli/gzip_container.c src/zopfli/hash.c src/zopfli/katajainen.c src/zopfli/lz77.c src/zopfli/squeeze.c src/zopfli/tree.c src/zopfli/util.c src/zopfli/zlib_container.c src/zopfli/zopfli_lib.c
clone https://github.com/ariya/FastLZ 0.5.0 fastlz && build fastlz 0.5.0 fastlz "" fastlz.c
clone https://github.com/nodejs/http-parser v2.9.4 http_parser && build http_parser 2.9.4 http_parser "" http_parser.c
clone https://github.com/mity/md4c release-0.5.2 md4c && build md4c 0.5.2 md4c "-Isrc" src/md4c.c src/md4c-html.c src/entity.c
clone https://github.com/ibireme/yyjson 0.10.0 yyjson && build yyjson 0.10.0 yyjson "-Isrc" src/yyjson.c
clone https://github.com/LoupVaillant/Monocypher 4.0.2 monocypher && build monocypher 4.0.2 monocypher "-Isrc" src/monocypher.c src/optional/monocypher-ed25519.c

echo "=== corpus_opt libs ==="; ls -1 "$C" | sed 's/-[0-9r].*//' | sort | uniq -c
echo "corpus_opt files: $(ls -1 "$C" | wc -l)  holdout_opt files: $(ls -1 "$H" | wc -l)"
