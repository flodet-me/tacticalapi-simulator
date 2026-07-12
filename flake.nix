{
  description = "A Nix Flake for .NET Development";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, utils }:
    utils.lib.eachDefaultSystem (system:
      let
        pkgs = import nixpkgs { inherit system; };

        # Define the specific .NET SDK version you need
        # Options: dotnetCorePackages.sdk_6_0, sdk_7_0, sdk_8_0, etc.
        dotnet-sdk = pkgs.dotnet-sdk_10;
      in
      {
        devShells.default = import ./.nix/shell.nix { inherit pkgs dotnet-sdk; };

        apps = {
            format = import ./.nix/format.nix { inherit pkgs dotnet-sdk; };
            ci-local = import ./.nix/ci-local.nix { inherit pkgs; };
        };
      }
    );
}
