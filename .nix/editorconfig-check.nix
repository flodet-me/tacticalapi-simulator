{ pkgs }:
{
  type = "app";
  meta.description = "Checks every tracked file against .editorconfig (editorconfig-checker), every *.nix file against nixfmt, every *.md against markdownlint-cli2 and every *.json against dprint; see docs/CI.md";
  program = "${pkgs.writeShellScriptBin "editorconfig-check" ''
    set -euo pipefail

    echo "Checking .nix formatting (nixfmt --check)..."
    nix_files=$(git ls-files '*.nix')
    if [ -n "$nix_files" ]; then
      ${pkgs.nixfmt}/bin/nixfmt --check $nix_files
    fi

    echo "Checking *.json formatting (dprint check)..."
    ${pkgs.dprint}/bin/dprint check

    echo "Checking *.md structure (markdownlint-cli2)..."
    ${pkgs.markdownlint-cli2}/bin/markdownlint-cli2

    echo "Checking all tracked files against .editorconfig (editorconfig-checker)..."
    ${pkgs.editorconfig-checker}/bin/editorconfig-checker

    echo "All files conform."
  ''}/bin/editorconfig-check";
}
