{ pkgs }:
{
  type = "app";
  program = "${pkgs.writeShellScriptBin "ci-local" ''
    set -euo pipefail

    if ! command -v docker >/dev/null 2>&1; then
      echo "Docker is required to run CI locally (act runs each job in a container)." >&2
      exit 1
    fi

    echo "Running .github/workflows/dotnet.yml locally via act..."
    echo "(runner image pinned in .actrc; pass extra act flags after --, e.g. 'nix run .#ci-local -- -j format-check')"
    exec ${pkgs.act}/bin/act "$@"
  ''}/bin/ci-local";
}
