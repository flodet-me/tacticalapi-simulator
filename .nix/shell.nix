{ pkgs, dotnet-sdk }:

pkgs.mkShell {
  buildInputs = [
    dotnet-sdk
    pkgs.icu # Required for .NET globalization
    pkgs.openssl
    pkgs.zlib
    pkgs.act # Run .github/workflows/dotnet.yml locally (needs Docker); see docs/CI.md
    pkgs.grpcurl # Poke the running host's reflection-enabled gRPC endpoint; see README.md
    pkgs.tshark # Inspect gRPC/gRPC-Web wire traffic (HTTP/2 h2c, HTTP/1.1) between adapters and Host
    pkgs.jq # Query JSON - API responses, appsettings.json, CycloneDX SBOM output
    pkgs.yq-go # Query/edit YAML - .github/workflows/dotnet.yml, action.yml files
    pkgs.python3 # Run the inline scripts dotnet.yml uses for the coverage gate and license allow-list checks
    pkgs.nixfmt # Formats/checks *.nix files (also the flake's `formatter`, and what `nix run .#editorconfig-check` calls); see docs/CI.md
    pkgs.editorconfig-checker # Checks every tracked file against .editorconfig (charset/EOL/trailing-whitespace/final-newline); see docs/CI.md
  ];

  # Set environment variables for .NET
  shellHook = ''
    export DOTNET_ROOT="${dotnet-sdk}";

    # Add local dotnet tools to PATH
    export PATH="$PATH:$HOME/.dotnet/tools"

    echo "🚀 .NET Development Shell Active"
    dotnet --version
  '';

  # Libraries that .NET often needs to link against
  LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath [
    pkgs.stdenv.cc.cc
    pkgs.openssl
    pkgs.zlib
    pkgs.icu
  ];
}
