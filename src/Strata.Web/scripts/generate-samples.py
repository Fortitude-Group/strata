"""
One-off generator for the three bundled Strata web-demo sample binaries.
Not part of the build — run manually to (re)produce the committed .dat files under
src/Strata.Web/wwwroot/samples/. Kept here (outside wwwroot, so it is never served as a static
asset) for provenance/reproducibility.

Produces minimal-but-valid ELF64 little-endian x86-64 blobs with appended ASCII
library-signature strings that Strata's seed corpus (Strata.Corpus.SeedCorpus) recognises via
the string/constant evidence signal, plus one unrecognised "vendor" string per sample so the
scan reports an unidentified region (honesty parity, FR-015).
"""
import os
import struct

OUTPUT_DIR = os.path.join(os.path.dirname(__file__), "..", "wwwroot", "samples")


def elf64_header(e_type: int = 2, e_machine: int = 0x3E, e_entry: int = 0x1000) -> bytes:
    ident = bytes([0x7F, 0x45, 0x4C, 0x46, 2, 1, 1, 0]) + bytes(8)  # EI_CLASS=2 (64-bit), EI_DATA=1 (LE)
    rest = struct.pack(
        "<HHIQQQIHHHHHH",
        e_type,        # e_type
        e_machine,     # e_machine (0x3E = EM_X86_64)
        1,             # e_version
        e_entry,       # e_entry
        0,             # e_phoff
        0,             # e_shoff
        0,             # e_flags
        64,            # e_ehsize
        0,             # e_phentsize
        0,             # e_phnum
        0,             # e_shentsize
        0,             # e_shnum
        0,             # e_shstrndx
    )
    header = ident + rest
    assert len(header) == 64
    return header


def strings_blob(*values: str) -> bytes:
    # NUL-separated ASCII runs: StringConstantExtractor splits on any non-printable byte,
    # so NUL terminators are enough to keep each literal a distinct extracted string.
    return b"\x00".join(v.encode("ascii") for v in values) + b"\x00"


ZLIB_DEFLATE = "deflate 1.2.11 Copyright 1995-2017 Jean-loup Gailly and Mark Adler "
ZLIB_INFLATE = "inflate 1.2.11 Copyright 1995-2017 Mark Adler "
ZLIB_NEED_DICT = "need dictionary"
OPENSSL = "OpenSSL 1.1.1k  25 Mar 2021"
LIBPNG = "libpng version 1.6.37 - April 14, 2019"
SQLITE_HEADER = "SQLite format 3"
SQLITE_VERSION = "3.39.4"

samples = {
    # "router-firmware extract" (FR-024 sample set): full-confidence zlib match (all three
    # seed signatures) plus an unmatched vendor string so an unidentified region is reported.
    "router-firmware.dat": strings_blob(
        ZLIB_DEFLATE, ZLIB_INFLATE, ZLIB_NEED_DICT,
        "RTR-9500 Bootloader Build 2024.03 FactoryImage",
    ),
    # "stripped static multi-library hello-world": three libraries at different confidence
    # levels (openssl/libpng full, sqlite partial — only the header magic, not the version
    # string) to show the confidence gradient honestly, plus one unmatched string.
    "static-multilib.dat": strings_blob(
        OPENSSL, LIBPNG, SQLITE_HEADER,
        "Acme Static Multilib Demo Build",
    ),
    # "vendor-style DLL": a low-distinctiveness partial zlib match alongside a full-confidence
    # openssl match, plus one unmatched vendor string.
    "vendor.dat": strings_blob(
        ZLIB_NEED_DICT, OPENSSL,
        "Vendor Proprietary Module X100",
    ),
}

os.makedirs(OUTPUT_DIR, exist_ok=True)
for filename, blob in samples.items():
    data = elf64_header() + blob
    out_path = os.path.join(OUTPUT_DIR, filename)
    with open(out_path, "wb") as f:
        f.write(data)
    print(f"{out_path}: {len(data)} bytes")
