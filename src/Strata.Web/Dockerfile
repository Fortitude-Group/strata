# syntax=docker/dockerfile:1
#
# Strata public web demo container image (contracts/web-demo.md — User Story 6). Blazor Server app;
# no separate corpus file to copy in — it uses the embedded seed corpus (Strata.Corpus.SeedCorpus).
#
# Build (from repo root):
#   docker build -f src/Strata.Web/Dockerfile -t strata-web:local .
#
# Run:
#   docker run --rm -p 8080:8080 strata-web:local
#
# (Prefer `docker compose -f src/Strata.Web/docker-compose.yml up --build` — see that file.)

# ---- build stage --------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy just the solution/project files first for better layer caching, then restore.
COPY Directory.Build.props ./
COPY Strata.slnx ./
COPY src/Strata.Web/Strata.Web.csproj src/Strata.Web/
COPY src/Strata.Core/Strata.Core.csproj src/Strata.Core/
COPY src/Strata.Corpus/Strata.Corpus.csproj src/Strata.Corpus/
COPY src/Strata.Sbom/Strata.Sbom.csproj src/Strata.Sbom/
RUN dotnet restore src/Strata.Web/Strata.Web.csproj -r linux-x64

# Now copy the rest of the source and publish a framework-dependent linux-x64 build.
COPY src/Strata.Web/ src/Strata.Web/
COPY src/Strata.Core/ src/Strata.Core/
COPY src/Strata.Corpus/ src/Strata.Corpus/
COPY src/Strata.Sbom/ src/Strata.Sbom/

RUN dotnet publish src/Strata.Web/Strata.Web.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained false \
    -o /publish

# ---- runtime stage -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Strata.Web.dll"]
