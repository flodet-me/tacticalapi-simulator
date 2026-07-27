{ pkgs }:
{
  type = "app";
  meta.description = "Checks every tracked file against .editorconfig (editorconfig-checker) and every *.nix file against nixfmt; see docs/CI.md";
  program = "${pkgs.writeShellScriptBin "editorconfig-check" ''
    set -euo pipefail

    echo "Checking .nix formatting (nixfmt --check)..."
    nix_files=$(git ls-files '*.nix')
    if [ -n "$nix_files" ]; then
      ${pkgs.nixfmt}/bin/nixfmt --check $nix_files
    fi

    echo "Checking all tracked files against .editorconfig (editorconfig-checker)..."
    ${pkgs.editorconfig-checker}/bin/editorconfig-checker

    echo "All files conform to .editorconfig."
  ''}/bin/editorconfig-check";
}
