{ pkgs }:
{
  type = "app";
  meta.description = "Runs .github/workflows/dotnet.yml locally via act (needs Docker); see docs/CI.md";
  program = "${pkgs.writeShellScriptBin "ci-local" ''
    set -euo pipefail

    if ! command -v docker >/dev/null 2>&1; then
      echo "Docker is required to run CI locally (act runs each job in a container)." >&2
      exit 1
    fi

    echo "Running .github/workflows/dotnet.yml locally via act..."
    echo "(runner image pinned in .actrc; pass extra act flags after --, e.g. 'nix run .#ci-local -- -j build-and-test')"
    exec ${pkgs.act}/bin/act "$@"
  ''}/bin/ci-local";
}
