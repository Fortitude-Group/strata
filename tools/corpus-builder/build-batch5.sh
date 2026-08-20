#!/usr/bin/env bash
# Batch 5: the libraries a firmware engineer actually meets. sqlite (amalgamation), mbedTLS (TLS),
# expat (XML). Appends optimised builds to /out/corpus_opt (gcc) and /out/holdout_opt (clang, stripped).
set -uo pipefail
C=/out/corpus_opt; H=/out/holdout_opt; mkdir -p "$C" "$H"
apt-get update -qq >/dev/null; apt-get install -y -qq clang git binutils >/dev/null
clone() { git clone -q --depth 1 --branch "$2" "$1" "$3" 2>/dev/null && echo "cloned $3" || echo "CLONE-FAIL $1 $2"; }
emit() { # <lib> <ver> <dir> <cflags> <src...>
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

# sqlite: single amalgamation file
clone https://github.com/azadkuh/sqlite-amalgamation 3.46.0 sqlite && emit sqlite 3.46.0 sqlite "" sqlite3.c

# mbedTLS: library/*.c with the bundled default config in include/
if clone https://github.com/Mbed-TLS/mbedtls v3.5.0 mbedtls; then
  ( cd mbedtls; SRCS=$(ls library/*.c | tr '\n' ' ')
    for cc in gcc clang; do for opt in O2 O3 Os; do
      out="$C/mbedtls-3.5.0-$cc-$opt.so"; [ "$cc" = clang ] && out="$H/mbedtls-3.5.0-$cc-$opt.so"
      if $cc -shared -fPIC "-$opt" -Iinclude $SRCS -o "$out" 2>/dev/null; then [ "$cc" = clang ] && strip -s "$out" 2>/dev/null; echo "ok mbedtls-$cc-$opt"; else echo "FAIL mbedtls-$cc-$opt"; fi
    done; done )
fi

# expat: needs an expat_config.h; synthesise a minimal one, then compile lib/*.c
if clone https://github.com/libexpat/libexpat R_2_6_2 expat; then
  ( cd expat/expat 2>/dev/null || cd expat
    cat > lib/expat_config.h <<'CFG'
#define HAVE_MEMMOVE 1
#define XML_CONTEXT_BYTES 1024
#define XML_DTD 1
#define XML_NS 1
#define XML_GE 1
#define BYTEORDER 1234
#define HAVE_ARC4RANDOM_BUF 0
#define HAVE_GETRANDOM 0
CFG
    for cc in gcc clang; do for opt in O2 O3 Os; do
      out="$C/expat-2.6.2-$cc-$opt.so"; [ "$cc" = clang ] && out="$H/expat-2.6.2-$cc-$opt.so"
      if $cc -shared -fPIC "-$opt" -DHAVE_EXPAT_CONFIG_H=1 -Ilib lib/xmlparse.c lib/xmltok.c lib/xmlrole.c -o "$out" 2>/dev/null; then [ "$cc" = clang ] && strip -s "$out" 2>/dev/null; echo "ok expat-$cc-$opt"; else echo "FAIL expat-$cc-$opt"; fi
    done; done )
fi

echo "=== corpus_opt libs ==="; ls -1 "$C" | sed 's/-[0-9r].*//' | sort | uniq -c
echo "corpus_opt files: $(ls -1 "$C" | wc -l)  holdout_opt files: $(ls -1 "$H" | wc -l)"
