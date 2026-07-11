{ pkgs, dotnet-sdk }:
{
  type = "app";
  program = "${pkgs.writeShellScriptBin "dotnet-format-all" ''
    echo "Formatting .NET solution/project..."

    # Run dotnet format
    # . : current directory
    # --verify-no-changes: (Optional) use this in CI to fail if code isn't formatted

    ${dotnet-sdk}/bin/dotnet format .

    echo "Done! Code style applied."
  ''}/bin/dotnet-format-all";
}
