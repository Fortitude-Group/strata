# syntax=docker/dockerfile:1
#
# Strata CLI container image (contracts/github-action.md — User Story 4, FR-021).
#
# Build stage publishes a self-contained, single-file `strata` executable for linux-x64 on the
# .NET 10 SDK image. The final stage is a minimal, runtime-free Linux base — it contains only the
# published `strata` executable (which embeds the seed corpus, Strata.Corpus/SeedCorpus.cs, as
# compiled data — no separate corpus file to copy in) plus system CA certs. No .NET or Python
# install is required to run the image (FR-021).
#
# Build (from repo root):
#   docker build -f src/Strata.Cli/Dockerfile -t strata:local .
#
# Run:
#   docker run --rm -v "$PWD":/workspace -w /workspace strata:local scan /workspace/sample.bin

# ---- build stage --------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy just the solution/project files first for better layer caching, then restore.
COPY Directory.Build.props ./
COPY Strata.slnx ./
COPY src/Strata.Cli/Strata.Cli.csproj src/Strata.Cli/
COPY src/Strata.Core/Strata.Core.csproj src/Strata.Core/
COPY src/Strata.Corpus/Strata.Corpus.csproj src/Strata.Corpus/
COPY src/Strata.Sbom/Strata.Sbom.csproj src/Strata.Sbom/
COPY src/Strata.Vuln/Strata.Vuln.csproj src/Strata.Vuln/
RUN dotnet restore src/Strata.Cli/Strata.Cli.csproj -r linux-x64

# Now copy the rest of the source and publish a self-contained single-file executable.
COPY src/Strata.Cli/ src/Strata.Cli/
COPY src/Strata.Core/ src/Strata.Core/
COPY src/Strata.Corpus/ src/Strata.Corpus/
COPY src/Strata.Sbom/ src/Strata.Sbom/
COPY src/Strata.Vuln/ src/Strata.Vuln/

# The published host executable takes the project's AssemblyName (`Strata.Cli`, unset in the
# .csproj — not ours to change here). Rename to the `strata` command name the contract/action
# expect; renaming post-publish (rather than overriding AssemblyName at publish time) avoids
# that global MSBuild property cascading into the P2P-referenced projects' own output names too.
RUN dotnet publish src/Strata.Cli/Strata.Cli.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:IncludeAllContentForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:PublishTrimmed=false \
    -o /publish \
 && mv /publish/Strata.Cli /publish/strata

# ---- runtime stage -------------------------------------------------------
# runtime-deps: no .NET runtime, no SDK, no shell scripting language — just the native OS
# libraries a self-contained single-file .NET app needs from the host (libicu, openssl, zlib).
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime

COPY --from=build /publish/strata /usr/local/bin/strata
RUN chmod +x /usr/local/bin/strata

WORKDIR /workspace
ENTRYPOINT ["strata"]
CMD ["--help"]
