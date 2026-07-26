# syntax=docker/dockerfile:1
#
# Build from the REPO ROOT: docker build -f src/TacticalApi.Simulator.Adapter.OpenSky/Dockerfile .
# (the build context has to be the repo root - this project needs
# Directory.Build.props/Directory.Packages.props/global.json and the whole
# src/ tree for its project references, not just its own directory.)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG VERSION=0.1.0
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY external/tacticalapi/rheinmetall external/tacticalapi/rheinmetall
COPY src/ src/

RUN dotnet publish src/TacticalApi.Simulator.Adapter.OpenSky --configuration Release \
    -p:Version=${VERSION} \
    --output /app/publish

# Plain console app - no ASP.NET Core, no port ever bound - so the smaller
# runtime image is enough, unlike the Host's aspnet one.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
ARG VERSION=0.1.0
LABEL org.opencontainers.image.title="TacticalAPI Simulator Adapter - OpenSky" \
      org.opencontainers.image.description="OpenSky Network flight-tracker adapter for the Rheinmetall TacticalAPI gRPC interface" \
      org.opencontainers.image.version="${VERSION}"

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "TacticalApi.Simulator.Adapter.OpenSky.dll"]
