#!/usr/bin/env bash
# Compiles a multi-library reference corpus from real open-source sources inside a container. Each
# (library, version) is built as a shared object across {gcc,clang} x {-O0,-O2,-O3,-Os}; the gcc set
# becomes the corpus, the clang set (stripped) becomes the held-out benchmark set built with a
# deliberately different compiler (FR-022). Per-library build failures are tolerated and reported.
set -uo pipefail

CORPUS=/out/corpus
HOLDOUT=/out/holdout
mkdir -p "$CORPUS" "$HOLDOUT"
apt-get update -qq >/dev/null
apt-get install -y -qq clang git binutils curl unzip >/dev/null

emit() { # emit <lib> <ver> <srcdir> <compile-args...>
  local lib="$1" ver="$2" dir="$3"; shift 3
  local args=("$@")
  ( cd "$dir" || return 1
    for cc in gcc clang; do
      for opt in O0 O2 O3 Os; do
        out="$CORPUS/$lib-$ver-$cc-$opt.so"
        [ "$cc" = clang ] && out="$HOLDOUT/$lib-$ver-$cc-$opt.so"
        if $cc -shared -fPIC "-$opt" "${args[@]}" -o "$out" 2>/dev/null; then
          [ "$cc" = clang ] && strip -s "$out" 2>/dev/null
          echo "ok   $lib-$ver-$cc-$opt"
        else
          echo "FAIL $lib-$ver-$cc-$opt"
        fi
      done
    done )
}

clone() { git clone -q --depth 1 --branch "$2" "$1" "$3" 2>/dev/null && echo "cloned $3" || echo "CLONE-FAIL $1 $2"; }

cd /tmp

# zlib — plain .c list, no config needed
ZS="adler32.c crc32.c deflate.c infback.c inffast.c inflate.c inftrees.c trees.c zutil.c compress.c uncompr.c gzclose.c gzlib.c gzread.c gzwrite.c"
for v in 1.2.13 1.3.1; do clone https://github.com/madler/zlib "v$v" "zlib-$v" && emit zlib "$v" "zlib-$v" $ZS; done

# bzip2 — 7 .c files, no config
for v in 1.0.8; do clone https://github.com/libarchive/bzip2 "bzip2-$v" "bzip2-$v" && \
  emit bzip2 "$v" "bzip2-$v" blocksort.c huffman.c crctable.c randtable.c compress.c decompress.c bzlib.c; done

# lz4 — lib/*.c, no config
for v in 1.9.4 1.10.0; do clone https://github.com/lz4/lz4 "v$v" "lz4-$v" && \
  emit lz4 "$v" "lz4-$v" lib/lz4.c lib/lz4hc.c lib/lz4frame.c lib/xxhash.c; done

# cJSON — single file
for v in 1.7.15 1.7.18; do clone https://github.com/DaveGamble/cJSON "v$v" "cJSON-$v" && emit cJSON "$v" "cJSON-$v" cJSON.c; done

# zstd — lib subtrees, no config
for v in 1.5.5 1.5.6; do
  if clone https://github.com/facebook/zstd "v$v" "zstd-$v"; then
    ( cd "zstd-$v"
      SRCS=$(ls lib/common/*.c lib/compress/*.c lib/decompress/*.c 2>/dev/null)
      for cc in gcc clang; do for opt in O0 O2 O3 Os; do
        out="$CORPUS/zstd-$v-$cc-$opt.so"; [ "$cc" = clang ] && out="$HOLDOUT/zstd-$v-$cc-$opt.so"
        if $cc -shared -fPIC "-$opt" -Ilib -Ilib/common $SRCS -o "$out" 2>/dev/null; then
          [ "$cc" = clang ] && strip -s "$out" 2>/dev/null; echo "ok   zstd-$v-$cc-$opt"
        else echo "FAIL zstd-$v-$cc-$opt"; fi
      done; done )
  fi
done

# libpng — needs zlib headers + prebuilt pnglibconf.h
for v in 1.6.40; do
  if clone https://github.com/pnggroup/libpng "v$v" "libpng-$v"; then
    ( cd "libpng-$v"
      cp scripts/pnglibconf.h.prebuilt pnglibconf.h 2>/dev/null
      SRCS=$(ls png.c pngerror.c pngget.c pngmem.c pngpread.c pngread.c pngrio.c pngrtran.c pngrutil.c pngset.c pngtrans.c pngwio.c pngwrite.c pngwtran.c pngwutil.c 2>/dev/null)
      for cc in gcc clang; do for opt in O0 O2 O3 Os; do
        out="$CORPUS/libpng-$v-$cc-$opt.so"; [ "$cc" = clang ] && out="$HOLDOUT/libpng-$v-$cc-$opt.so"
        if $cc -shared -fPIC "-$opt" -I/tmp/zlib-1.3.1 $SRCS -o "$out" 2>/dev/null; then
          [ "$cc" = clang ] && strip -s "$out" 2>/dev/null; echo "ok   libpng-$v-$cc-$opt"
        else echo "FAIL libpng-$v-$cc-$opt"; fi
      done; done )
  fi
done

echo "=== CORPUS (gcc) ==="; ls -1 "$CORPUS" | sed 's/-gcc.*//' | sort | uniq -c
echo "=== HOLDOUT (clang) ==="; ls -1 "$HOLDOUT" | sed 's/-clang.*//' | sort | uniq -c
echo "corpus files: $(ls -1 "$CORPUS" | wc -l)  holdout files: $(ls -1 "$HOLDOUT" | wc -l)"
