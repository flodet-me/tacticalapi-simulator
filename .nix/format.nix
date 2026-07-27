{ pkgs, dotnet-sdk }:
{
  type = "app";
  meta.description = "Applies formatting fixes across the whole repo: dotnet format for *.cs, nixfmt for *.nix, and charset/line-ending/trailing-whitespace/final-newline fixes for every other tracked file. Not --verify-no-changes; that's what CI's formatting steps and `nix run .#editorconfig-check` run instead, see docs/CI.md";
  program = "${pkgs.writeShellScriptBin "format-all" ''
    set -euo pipefail

    git="${pkgs.git}/bin/git"
    sed="${pkgs.gnused}/bin/sed"

    echo "Formatting C# (dotnet format)..."
    ${dotnet-sdk}/bin/dotnet format .

    echo "Formatting *.nix (nixfmt)..."
    nix_files=$("$git" ls-files '*.nix')
    if [ -n "$nix_files" ]; then
      ${pkgs.nixfmt}/bin/nixfmt $nix_files
    fi

    echo "Fixing charset/line-endings/trailing-whitespace/final-newline on every other tracked text file..."
    # Same 4 checks .editorconfig-checker.json leaves enabled (Indentation/IndentSize
    # are off - see docs/CI.md for why); an empty-pattern "$git" grep -Il lists
    # tracked files that are text, the same way editorconfig-checker skips binaries.
    "$git" grep -Il "" -- . | while IFS= read -r f; do
      [ -f "$f" ] || continue
      if [ "$(od -An -tx1 -N3 < "$f" | tr -d ' \n')" = "efbbbf" ]; then
        tail -c +4 "$f" > "$f.bom-tmp" && mv "$f.bom-tmp" "$f"
      fi
      "$sed" -i -e 's/\r$//' -e 's/[ \t]*$//' "$f"
      if [ -s "$f" ] && [ -n "$(tail -c1 "$f")" ]; then
        printf '\n' >> "$f"
      fi
    done

    echo "Done. Run 'dotnet format --verify-no-changes' and 'nix run .#editorconfig-check' to verify."
  ''}/bin/format-all";
}
