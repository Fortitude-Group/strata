#!/usr/bin/env bash
# Batch 7: more libraries plus retries of the ones that failed a build flag in batch 6.
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

clone https://github.com/kmackay/micro-ecc v1.0 microecc && ( cd microecc; CF="-I."; SRCS="uECC.c"; mk microecc 1.0 )
clone https://github.com/LiamBindle/MQTT-C v1.1.6 mqttc && ( cd mqttc; CF="-Iinclude"; SRCS="src/mqtt.c src/mqtt_pal.c"; mk mqttc 1.1.6 )
clone https://github.com/ccxvii/mujs 1.3.4 mujs && ( cd mujs; CF="-I."; SRCS="$(ls one.c 2>/dev/null || ls *.c | grep -vE 'main.c|pp.c|ftoa.c|utftest.c'| tr '\n' ' ')"; mk mujs 1.3.4 )
clone https://github.com/BearSSL/BearSSL v0.6 bearssl && ( cd bearssl; CF="-Iinc -Isrc"; SRCS="$(find src -name '*.c' | tr '\n' ' ')"; mk bearssl 0.6 )
clone https://github.com/richgel999/miniz 3.0.2 miniz && ( cd miniz; CF="-I."; SRCS="$(ls miniz.c 2>/dev/null || echo miniz.c)"; mk miniz 3.0.2 )
clone https://github.com/troydhanson/uthash v2.3.0 uthash && ( cd uthash; printf '#include "uthash.h"\n#include "utlist.h"\n#include "utarray.h"\nint _uthash_tu(void){return 0;}\n' > tu.c; CF="-Isrc"; SRCS="tu.c"; mk uthash 2.3.0 )
clone https://github.com/rgamble/libcsv master libcsv && ( cd libcsv; CF="-I."; SRCS="libcsv.c"; mk libcsv 3.0.3 )
clone https://github.com/antirez/sds master sds && ( cd sds; CF="-I."; SRCS="sds.c"; mk sds 2.0.0 )
clone https://github.com/cktan/tomlc99 master tomlc99 && ( cd tomlc99; CF="-I."; SRCS="toml.c"; mk tomlc99 1.0 )

# duktape: the git tag has no amalgamation; fetch the release dist tarball which ships src/duktape.c
if curl -fsSL --max-time 90 https://github.com/svaarala/duktape/releases/download/v2.7.0/duktape-2.7.0.tar.xz -o /tmp/dt.tar.xz 2>/dev/null; then
  ( cd /tmp; tar xf dt.tar.xz; cd duktape-2.7.0; CF="-Isrc"; SRCS="src/duktape.c"; mk duktape 2.7.0 )
else echo "duktape download FAILED"; fi

# retries from batch 6
clone https://github.com/lua/lua v5.4.6 lua2 && ( cd lua2; CF="-DLUA_USE_POSIX"; SRCS="$(ls *.c | grep -vE '^lua\.c$|^luac\.c$' | tr '\n' ' ')"; mk lua 5.4.6 )
clone https://github.com/tinycthread/tinycthread master tinycthread && ( cd tinycthread; CF="-Isource"; SRCS="source/tinycthread.c"; mk tinycthread 1.2 )
clone https://github.com/jedisct1/libhydrogen master libhydrogen && ( cd libhydrogen; CF="-I."; SRCS="hydrogen.c"; mk libhydrogen 1.0.4 )

echo "=== corpus_opt distinct libs ==="; ls -1 "$C" | sed -E 's/-[0-9r].*//' | sort -u | tr '\n' ' '; echo
echo "distinct: $(ls -1 "$C" | sed -E 's/-[0-9r].*//' | sort -u | wc -l)  files: $(ls -1 "$C" | wc -l)"
