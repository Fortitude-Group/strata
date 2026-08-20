#requires -version 7
<#
.SYNOPSIS
    Generates small, deterministic ELF/PE/Mach-O fixture binaries with known embedded library strings,
    plus a ground-truth.json manifest describing what each fixture should identify as.

.DESCRIPTION
    T022/T102: this documents and automates fixture generation for manual exploration and for anyone
    extending the test suite — it is NOT part of the automated test run (the xUnit projects build their
    own in-memory fixtures via TestBinaries.cs / Strata.Cli.Tests helpers, which is faster and needs no
    disk I/O). Safe to re-run; it always rewrites tests/fixtures/out/.

    Outputs use a .dat extension deliberately: tests/fixtures/*.elf and *.bin are gitignored (see the
    repo .gitignore), and .dat dodges that the same way src/Strata.Web/wwwroot/samples/*.dat does. Files
    under tests/fixtures/out/ are otherwise untracked convenience artifacts, not committed corpus data.

.EXAMPLE
    pwsh tests/fixtures/build-fixtures.ps1
#>
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "out")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}

# ---------------------------------------------------------------------------
# Byte-array builders — mirror the header layouts asserted against in
# tests/Strata.Core.Tests/IngestionTests.cs and tests/Strata.Cli.Tests/TestBinaries.cs.
# ---------------------------------------------------------------------------

function New-ElfWithStrings {
    param([string[]]$Strings)

    $bytes = New-Object byte[] 64
    $bytes[0] = 0x7F; $bytes[1] = [byte][char]'E'; $bytes[2] = [byte][char]'L'; $bytes[3] = [byte][char]'F'
    $bytes[4] = 2     # EI_CLASS = 64-bit
    $bytes[5] = 1     # EI_DATA  = little-endian
    $bytes[6] = 1     # EI_VERSION
    $bytes[16] = 2    # e_type = ET_EXEC
    $bytes[18] = 0x3E # e_machine = EM_X86_64
    $bytes[20] = 1    # e_version
    $bytes[52] = 64   # e_ehsize -- e_shoff (@40) stays 0 -> no section table

    $tail = [System.Collections.Generic.List[byte]]::new()
    foreach ($s in $Strings) {
        $tail.AddRange([System.Text.Encoding]::ASCII.GetBytes($s))
        $tail.Add(0)
    }

    return $bytes + $tail.ToArray()
}

function New-PeWithStrings {
    param([string[]]$Strings)

    $bytes = New-Object byte[] 512
    $bytes[0] = [byte][char]'M'; $bytes[1] = [byte][char]'Z'
    $bytes[0x3C] = 0x80                                    # PE header offset
    $bytes[0x80] = [byte][char]'P'; $bytes[0x81] = [byte][char]'E'   # "PE\0\0"
    $bytes[0x84] = 0x64; $bytes[0x85] = 0x86                # machine = IMAGE_FILE_MACHINE_AMD64
    $bytes[0x94] = 0xE0                                     # SizeOfOptionalHeader

    $tail = [System.Collections.Generic.List[byte]]::new()
    foreach ($s in $Strings) {
        $tail.AddRange([System.Text.Encoding]::ASCII.GetBytes($s))
        $tail.Add(0)
    }

    return $bytes + $tail.ToArray()
}

function New-MachOWithStrings {
    param([string[]]$Strings)

    $bytes = New-Object byte[] 64
    $bytes[0] = 0xCF; $bytes[1] = 0xFA; $bytes[2] = 0xED; $bytes[3] = 0xFE   # 64-bit LE magic on disk
    $bytes[4] = 0x07; $bytes[5] = 0x00; $bytes[6] = 0x00; $bytes[7] = 0x01  # cputype = CPU_TYPE_X86_64 (LE)

    $tail = [System.Collections.Generic.List[byte]]::new()
    foreach ($s in $Strings) {
        $tail.AddRange([System.Text.Encoding]::ASCII.GetBytes($s))
        $tail.Add(0)
    }

    return $bytes + $tail.ToArray()
}

# ---------------------------------------------------------------------------
# Fixture definitions — known-good seed-corpus strings (src/Strata.Corpus/SeedCorpus.cs), so a scan
# against the seed corpus should identify exactly the listed library/version.
# ---------------------------------------------------------------------------

$zlibString    = "deflate 1.2.11 Copyright 1995-2017 Jean-loup Gailly and Mark Adler "
$opensslString = "OpenSSL 1.1.1k  25 Mar 2021"
$libpngString  = "libpng version 1.6.37 - April 14, 2019"
$noiseString   = "vendor-specific blob that matches nothing in the seed corpus"

$fixtures = @(
    @{
        FileName = "elf-zlib.dat"
        Format   = "Elf"
        Builder  = { New-ElfWithStrings @($zlibString, $noiseString) }
        Expected = @(@{ Library = "zlib"; Version = "1.2.11" })
    },
    @{
        FileName = "pe-libpng.dat"
        Format   = "Pe"
        Builder  = { New-PeWithStrings @($libpngString, $noiseString) }
        Expected = @(@{ Library = "libpng"; Version = "1.6.37" })
    },
    @{
        FileName = "macho-openssl.dat"
        Format   = "MachO"
        Builder  = { New-MachOWithStrings @($opensslString, $noiseString) }
        Expected = @(@{ Library = "openssl"; Version = "1.1.1k" })
    },
    @{
        FileName = "elf-multi.dat"
        Format   = "Elf"
        Builder  = { New-ElfWithStrings @($zlibString, $opensslString, $libpngString, $noiseString) }
        Expected = @(
            @{ Library = "zlib"; Version = "1.2.11" }
            @{ Library = "openssl"; Version = "1.1.1k" }
            @{ Library = "libpng"; Version = "1.6.37" }
        )
    }
)

$manifest = @{
    generatedUtc = (Get-Date).ToUniversalTime().ToString("o")
    corpus       = "seed-0.1.0 (src/Strata.Corpus/SeedCorpus.cs)"
    fixtures     = @()
}

foreach ($fixture in $fixtures) {
    $path = Join-Path $OutDir $fixture.FileName
    $bytes = & $fixture.Builder
    [System.IO.File]::WriteAllBytes($path, $bytes)

    $manifest.fixtures += @{
        file             = $fixture.FileName
        format           = $fixture.Format
        sizeBytes        = $bytes.Length
        expectedLibraries = $fixture.Expected
    }

    Write-Host "wrote $($fixture.FileName) ($($bytes.Length) bytes, format=$($fixture.Format))"
}

$manifestPath = Join-Path $OutDir "ground-truth.json"
$manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding utf8
Write-Host "wrote ground-truth.json -> $manifestPath"
