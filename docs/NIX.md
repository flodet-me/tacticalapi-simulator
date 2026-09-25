# Nix dev environment

`flake.nix` gives a reproducible dev shell plus a few helper apps. None of it is required — the plain `dotnet` CLI ([README](../README.md)) works fine — it just saves pinning versions yourself.

**Prerequisite:** [Nix](https://nixos.org/download/) with flakes enabled (`experimental-features = nix-command flakes` in `nix.conf`, or `--extra-experimental-features "nix-command flakes"` per command).

## Entering the shell

```bash
nix develop      # or: direnv allow, once — .envrc is just `use flake`
```

With [direnv](https://direnv.net/) the shell loads on `cd` in and unloads on `cd` out; `.direnv/` is gitignored.

What `.nix/shell.nix` puts on `PATH`:

| Tool | For |
| --- | --- |
| `dotnet-sdk_10` + `icu`/`openssl`/`zlib` | .NET and its native deps (also wired into `LD_LIBRARY_PATH`) |
| `act` | running CI locally ([CI](CI.md)) |
| `gh` | GitHub CLI — PRs, issues, workflow runs |
| `grpcurl` | poking the gRPC surface ([README](../README.md)) |
| `tshark` | inspecting gRPC / gRPC-Web wire traffic |
| `jq`, `yq-go`, `python3` | poking at `.github/workflows/dotnet.yml` and running its embedded coverage-gate / license-check snippets outside a full `act` run |
| `markdownlint-cli2` | markdown structure checks ([CI](CI.md)) |
| `dprint` | JSON formatting ([CI](CI.md)) |
| `jdk` | nothing above needs it — SonarLint's C#/.NET analyzer runs on the JVM, so the IDE plugin needs a JDK on `PATH` to analyze at all |

The hook also adds `~/.dotnet/tools`, for anything installed with `dotnet tool install --global`.

## Apps

| Command | Does |
| --- | --- |
| `nix run .#format` | All formatting fixes in one shot: `dotnet format` for `*.cs`, `nixfmt` for `*.nix`, `dprint` for `*.json`, `markdownlint-cli2 --fix` for `*.md`, charset/EOL/trailing-whitespace/final-newline for every other tracked file. Applies, does not verify. |
| `nix run .#editorconfig-check` | The read-only counterpart: `nixfmt --check`, `dprint check`, `markdownlint-cli2` and `editorconfig-checker`, i.e. CI's formatting steps 2–5 ([CI](CI.md)). Step 1 is plain `dotnet format --verify-no-changes`. |
| `nix run .#ci-local` | Runs `.github/workflows/dotnet.yml` via act (needs Docker). Extra `act` flags go after `--`; caveats in [CI](CI.md). |
| `nix fmt` | This repo's own `*.nix` files only (`nixfmt`, the flake's `formatter`) — unrelated to the C# solution. |
