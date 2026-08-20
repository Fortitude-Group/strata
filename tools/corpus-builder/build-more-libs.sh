#!/usr/bin/env bash
# Adds more real libraries to the optimized corpus/holdout sets (appends to /out/corpus_opt and
# /out/holdout_opt). Optimized builds only (-O2/-O3/-Os); gcc -> corpus, clang (stripped) -> holdout.
set -uo pipefail
C=/out/corpus_opt; H=/out/holdout_opt; mkdir -p "$C" "$H"
apt-get update -qq >/dev/null; apt-get install -y -qq clang git binutils >/dev/null

clone() { git clone -q --depth 1 --branch "$2" "$1" "$3" 2>/dev/null && echo "cloned $3" || echo "CLONE-FAIL $1 $2"; }

# build <lib> <ver> <dir> <extra-cflags> <src...>
build() {
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
clone https://github.com/kgabis/parson v1.5.3 parson && build parson 1.5.3 parson "" parson.c
clone https://github.com/benhoyt/inih r58 inih && build inih r58 inih "" ini.c
clone https://github.com/JuliaStrings/utf8proc v2.9.0 utf8proc && build utf8proc 2.9.0 utf8proc "" utf8proc.c
clone https://github.com/antirez/linenoise 1.0 linenoise && build linenoise 1.0 linenoise "" linenoise.c
clone https://github.com/cktan/tomlc99 v1.0 tomlc99 && build tomlc99 1.0 tomlc99 "" toml.c

# zstd retry: disable assembly, add include paths, compile the three core subtrees.
for v in 1.5.5 1.5.6; do
  if clone https://github.com/facebook/zstd "v$v" "zstd-$v"; then
    ( cd "zstd-$v"
      SRCS=$(ls lib/common/*.c lib/compress/*.c lib/decompress/*.c 2>/dev/null)
      for cc in gcc clang; do for opt in O2 O3 Os; do
        out="$C/zstd-$v-$cc-$opt.so"; [ "$cc" = clang ] && out="$H/zstd-$v-$cc-$opt.so"
        if $cc -shared -fPIC "-$opt" -DZSTD_DISABLE_ASM=1 -Ilib -Ilib/common $SRCS -o "$out" 2>/dev/null; then
          [ "$cc" = clang ] && strip -s "$out" 2>/dev/null; echo "ok   zstd-$v-$cc-$opt"
        else echo "FAIL zstd-$v-$cc-$opt"; fi
      done; done )
  fi
done

echo "=== corpus_opt libs ==="; ls -1 "$C" | sed 's/-[0-9r].*//' | sort | uniq -c
echo "corpus_opt files: $(ls -1 "$C" | wc -l)  holdout_opt files: $(ls -1 "$H" | wc -l)"
