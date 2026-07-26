# syntax=docker/dockerfile:1
#
# Build from the REPO ROOT: docker build -f src/TacticalApi.Simulator.Host/Dockerfile .
# (the build context has to be the repo root - this project needs
# Directory.Build.props/Directory.Packages.props/global.json and the whole
# src/ tree for its project references, not just its own directory.)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG VERSION=0.1.0
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY external/tacticalapi/rheinmetall external/tacticalapi/rheinmetall
COPY src/ src/

RUN dotnet publish src/TacticalApi.Simulator.Host --configuration Release \
    -p:Version=${VERSION} \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG VERSION=0.1.0
LABEL org.opencontainers.image.title="TacticalAPI Simulator" \
      org.opencontainers.image.description="Simulator for the Rheinmetall TacticalAPI gRPC interface" \
      org.opencontainers.image.version="${VERSION}"

WORKDIR /app
COPY --from=build /app/publish .

# gRPC-Web (HTTP/1.1) and native gRPC (h2c) endpoints, see appsettings.json.
EXPOSE 4268 5100

ENTRYPOINT ["dotnet", "TacticalApi.Simulator.Host.dll"]
