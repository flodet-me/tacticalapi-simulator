{ pkgs, dotnet-sdk }:

pkgs.mkShell {
          buildInputs = [
            dotnet-sdk
            pkgs.icu # Required for .NET globalization
            pkgs.openssl
            pkgs.zlib
            pkgs.act # Run .github/workflows/dotnet.yml locally (needs Docker); see docs/CI.md
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
