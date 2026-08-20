#!/usr/bin/env bash
# Compiles real zlib sources across versions x {gcc,clang} x optimisation levels inside a container,
# splitting output into a corpus set (gcc) and a held-out benchmark set (clang, stripped) built with a
# deliberately different compiler than the corpus (SC-001 / FR-022). Runs entirely in Docker; the host
# needs no toolchain. Output lands in /out/{corpus,holdout} (bind-mounted).
set -euo pipefail

CORPUS=/out/corpus
HOLDOUT=/out/holdout
mkdir -p "$CORPUS" "$HOLDOUT"

apt-get update -qq >/dev/null
apt-get install -y -qq clang git binutils >/dev/null

ZSRC="adler32.c crc32.c deflate.c infback.c inffast.c inflate.c inftrees.c trees.c zutil.c \
      compress.c uncompr.c gzclose.c gzlib.c gzread.c gzwrite.c"

for ver in 1.2.11 1.2.12 1.2.13 1.3.1; do
  cd /tmp
  rm -rf "zlib-$ver"
  git clone -q --depth 1 --branch "v$ver" https://github.com/madler/zlib "zlib-$ver"
  cd "zlib-$ver"

  # Corpus: gcc at -O2/-O3/-Os (symbols kept so corpus function names are meaningful).
  for opt in O2 O3 Os; do
    gcc -shared -fPIC "-$opt" $ZSRC -o "$CORPUS/zlib-$ver-gcc-$opt.so" 2>/dev/null \
      && echo "corpus  zlib-$ver-gcc-$opt" || echo "FAIL corpus zlib-$ver-gcc-$opt"
  done

  # Held-out: clang at -O0, STRIPPED — different compiler AND opt level than the corpus, and the
  # realistic stripped-scan case.
  clang -shared -fPIC -O0 $ZSRC -o "$HOLDOUT/zlib-$ver-clang-O0.so" 2>/dev/null || echo "FAIL holdout clang $ver"
  strip -s "$HOLDOUT/zlib-$ver-clang-O0.so" 2>/dev/null || true
  echo "holdout zlib-$ver-clang-O0 (stripped)"
done

echo "--- corpus set ---"; ls -1 "$CORPUS"
echo "--- holdout set ---"; ls -1 "$HOLDOUT"
