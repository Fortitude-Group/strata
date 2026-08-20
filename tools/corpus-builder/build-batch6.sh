#!/usr/bin/env bash
# Batch 6: a large push toward the full set. Each library has its own compile recipe. Failures are
# tolerated and reported. Optimised builds only; gcc -> corpus_opt, clang (stripped) -> holdout_opt.
set -uo pipefail
C=/out/corpus_opt; H=/out/holdout_opt; mkdir -p "$C" "$H"
apt-get update -qq >/dev/null; apt-get install -y -qq clang git binutils curl >/dev/null
clone() { git clone -q --depth 1 --branch "$2" "$1" "$3" 2>/dev/null && echo "cloned $3" || echo "CLONE-FAIL $1 $2"; }
mk() { # mk <lib> <ver> then reads SRCS + CF from env; compiles in current dir
  local lib="$1" ver="$2"
  for cc in gcc clang; do for opt in O2 O3 Os; do
    out="$C/$lib-$ver-$cc-$opt.so"; [ "$cc" = clang ] && out="$H/$lib-$ver-$cc-$opt.so"
    if $cc -shared -fPIC "-$opt" $CF $SRCS -o "$out" 2>/dev/null; then
      [ "$cc" = clang ] && strip -s "$out" 2>/dev/null; echo "ok   $lib-$ver-$cc-$opt"
    else echo "FAIL $lib-$ver-$cc-$opt"; fi
  done; done
}
cd /tmp

clone https://github.com/civetweb/civetweb v1.16 civetweb   && ( cd civetweb; CF="-Iinclude -Isrc -DUSE_STACK_SIZE=102400"; SRCS="src/civetweb.c"; mk civetweb 1.16 )
clone https://github.com/wren-lang/wren 0.4.0 wren          && ( cd wren; CF="-Isrc/include -Isrc/vm -Isrc/optional"; SRCS="src/vm/*.c src/optional/*.c"; mk wren 0.4.0 )
clone https://github.com/hoedown/hoedown 3.0.7 hoedown      && ( cd hoedown; CF="-Isrc"; SRCS="src/*.c"; mk hoedown 3.0.7 )
clone https://github.com/libtom/libtommath v1.2.1 libtommath && ( cd libtommath; CF=""; SRCS="*.c"; mk libtommath 1.2.1 )
clone https://github.com/libtom/libtomcrypt v1.18.2 libtomcrypt && ( cd libtomcrypt; CF="-Isrc/headers -DLTC_SOURCE -DLTC_NO_ASM"; SRCS="$(find src -name '*.c' | tr '\n' ' ')"; mk libtomcrypt 1.18.2 )
clone https://github.com/BLAKE3-team/BLAKE3 1.5.0 blake3    && ( cd blake3; CF="-Ic -DBLAKE3_NO_SSE2 -DBLAKE3_NO_SSE41 -DBLAKE3_NO_AVX2 -DBLAKE3_NO_AVX512"; SRCS="c/blake3.c c/blake3_dispatch.c c/blake3_portable.c"; mk blake3 1.5.0 )
clone https://github.com/Cyan4973/xxHash v0.8.2 xxhash      && ( cd xxhash; CF=""; SRCS="xxhash.c"; mk xxhash 0.8.2 )
clone https://github.com/nanopb/nanopb 0.4.8 nanopb         && ( cd nanopb; CF="-I."; SRCS="pb_common.c pb_encode.c pb_decode.c"; mk nanopb 0.4.8 )
clone https://github.com/jibsen/parg v1.0.2 parg            && ( cd parg; CF="-I."; SRCS="parg.c"; mk parg 1.0.2 )
clone https://github.com/argtable/argtable3 v3.2.2.f25c624 argtable3 || clone https://github.com/argtable/argtable3 v3.2.1 argtable3
[ -d argtable3 ] && ( cd argtable3; CF="-Isrc"; SRCS="$(ls src/*.c | grep -v getopt | tr '\n' ' ')"; mk argtable3 3.2.2 )
clone https://github.com/likle/cwalk v1.2.9 cwalk           && ( cd cwalk; CF="-Iinclude"; SRCS="src/cwalk.c"; mk cwalk 1.2.9 )
clone https://github.com/likle/cargs v1.1.0 cargs           && ( cd cargs; CF="-Iinclude"; SRCS="src/cargs.c"; mk cargs 1.1.0 )
clone https://github.com/aklomp/base64 v0.5.2 base64        && ( cd base64; CF="-Iinclude -Ilib"; SRCS="lib/lib.c lib/codec_choose.c lib/tables/tables.c lib/arch/generic/codec.c"; mk base64 0.5.2 )
clone https://github.com/IlyaGrebnov/libsais v2.7.1 libsais && ( cd libsais; CF="-Iinclude -Isrc"; SRCS="src/libsais.c"; mk libsais 2.7.1 )
clone https://github.com/ludocode/mpack v1.1.1 mpack        && ( cd mpack; CF="-Isrc -Isrc/mpack"; SRCS="src/mpack/*.c"; mk mpack 1.1.1 )
clone https://github.com/jedisct1/libhydrogen 1.0.4 libhydrogen || clone https://github.com/jedisct1/libhydrogen master libhydrogen
[ -d libhydrogen ] && ( cd libhydrogen; CF="-I."; SRCS="hydrogen.c"; mk libhydrogen 1.0.4 )
clone https://github.com/tinycthread/tinycthread v1.2 tinycthread || clone https://github.com/tinycthread/tinycthread master tinycthread
[ -d tinycthread ] && ( cd tinycthread; CF="-Isource"; SRCS="source/tinycthread.c"; mk tinycthread 1.2 )

# jansson: synthesise the two config headers it needs
if clone https://github.com/akheron/jansson v2.14 jansson; then
  ( cd jansson
    cat > src/jansson_config.h <<'CFG'
#ifndef JANSSON_CONFIG_H
#define JANSSON_CONFIG_H
#define JSON_INLINE inline
#define JSON_INTEGER_IS_LONG_LONG 1
#define JSON_HAVE_LOCALECONV 1
#define JSON_HAVE_ATOMIC_BUILTINS 1
#define JSON_HAVE_SYNC_BUILTINS 1
#define JSON_PARSER_MAX_DEPTH 2048
#endif
CFG
    printf '#define HAVE_STDINT_H 1\n' > src/jansson_private_config.h
    CF="-Isrc -DHAVE_CONFIG_H=0"; SRCS="src/*.c"; mk jansson 2.14 )
fi

# libyaml: synthesise config.h
if clone https://github.com/yaml/libyaml 0.2.5 libyaml; then
  ( cd libyaml
    cat > config.h <<'CFG'
#define YAML_VERSION_MAJOR 0
#define YAML_VERSION_MINOR 2
#define YAML_VERSION_PATCH 5
#define YAML_VERSION_STRING "0.2.5"
CFG
    CF="-I. -Iinclude -DHAVE_CONFIG_H=1"; SRCS="src/*.c"; mk libyaml 0.2.5 )
fi

# lua: core library, capture the real error once
if clone https://github.com/lua/lua v5.4.6 lua; then
  ( cd lua; SRCS="$(ls *.c | grep -vE '^lua\.c$|^luac\.c$' | tr '\n' ' ')"
    gcc -shared -fPIC -O2 $SRCS -o /tmp/lp.so 2>/tmp/luaerr.txt || { echo "lua err:"; head -2 /tmp/luaerr.txt; }
    CF=""; mk lua 5.4.6 )
fi

# sqlite: fetch the amalgamation from sqlite.org
if curl -fsSL --max-time 60 https://www.sqlite.org/2024/sqlite-amalgamation-3460000.zip -o /tmp/sq.zip 2>/dev/null; then
  ( cd /tmp; apt-get install -y -qq unzip >/dev/null; unzip -q sq.zip; cd sqlite-amalgamation-3460000
    CF=""; SRCS="sqlite3.c"; mk sqlite 3.46.0 )
else echo "sqlite download FAILED"; fi

echo "=== corpus_opt libs ==="; ls -1 "$C" | sed 's/-[0-9].*//' | sort | uniq -c
echo "corpus_opt files: $(ls -1 "$C" | wc -l)  holdout_opt files: $(ls -1 "$H" | wc -l)"
