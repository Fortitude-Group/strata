#!/usr/bin/env bash
# Batch 4: mongoose (network), libdeflate + heatshrink (more compression), lua retry. Appends
# optimized builds to /out/corpus_opt (gcc) and /out/holdout_opt (clang, stripped).
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

clone https://github.com/cesanta/mongoose 7.14 mongoose && build mongoose 7.14 mongoose "" mongoose.c

if clone https://github.com/ebiggers/libdeflate v1.19 libdeflate; then
  ( cd libdeflate; SRCS=$(find lib -name '*.c' | tr '\n' ' ')
    for cc in gcc clang; do for opt in O2 O3 Os; do
      out="$C/libdeflate-1.19-$cc-$opt.so"; [ "$cc" = clang ] && out="$H/libdeflate-1.19-$cc-$opt.so"
      if $cc -shared -fPIC "-$opt" -I. -Ilib $SRCS -o "$out" 2>/dev/null; then [ "$cc" = clang ] && strip -s "$out" 2>/dev/null; echo "ok libdeflate-$cc-$opt"; else echo "FAIL libdeflate-$cc-$opt"; fi
    done; done )
fi

clone https://github.com/atomicobject/heatshrink v0.4.1 heatshrink && build heatshrink 0.4.1 heatshrink "-I." heatshrink_encoder.c heatshrink_decoder.c

# lua: core library only (exclude the two CLI entry points). Report the real error if it fails.
if clone https://github.com/lua/lua v5.4.6 lua; then
  ( cd lua; SRCS=$(ls *.c | grep -vE '^lua\.c$|^luac\.c$' | tr '\n' ' ')
    gcc -shared -fPIC -O2 $SRCS -o /tmp/lua-probe.so 2>/tmp/lua-err.txt && echo "lua probe ok" || { echo "lua probe FAILED:"; head -3 /tmp/lua-err.txt; }
    for cc in gcc clang; do for opt in O2 O3 Os; do
      out="$C/lua-5.4.6-$cc-$opt.so"; [ "$cc" = clang ] && out="$H/lua-5.4.6-$cc-$opt.so"
      if $cc -shared -fPIC "-$opt" $SRCS -o "$out" 2>/dev/null; then [ "$cc" = clang ] && strip -s "$out" 2>/dev/null; echo "ok lua-$cc-$opt"; else echo "FAIL lua-$cc-$opt"; fi
    done; done )
fi

echo "=== corpus_opt libs ==="; ls -1 "$C" | sed 's/-[0-9r].*//' | sort | uniq -c
echo "corpus_opt files: $(ls -1 "$C" | wc -l)  holdout_opt files: $(ls -1 "$H" | wc -l)"
