#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
export DOTNET_CLI_HOME="$(mktemp -d "${TMPDIR:-/tmp}/dameng-local-test.XXXXXX")"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
trap 'rm -rf "$DOTNET_CLI_HOME"' EXIT
compatible_host() {
  [[ -x "$1" ]] && (cd "$repo_root" && "$1" --version >/dev/null 2>&1)
}
if [[ -n "${DOTNET_HOST_PATH:-}" ]]; then
  dotnet_host="$DOTNET_HOST_PATH"
elif command -v dotnet >/dev/null 2>&1 && compatible_host "$(command -v dotnet)"; then
  dotnet_host="$(command -v dotnet)"
else
  dotnet_host="$HOME/.dotnet/dotnet"
fi
if ! compatible_host "$dotnet_host"; then
  printf '%s\n' 'No dotnet host with a compatible SDK found.' >&2
  exit 1
fi
export DOTNET_HOST_PATH="$dotnet_host"

"$dotnet_host" restore "$script_dir/LocalTest.csproj" --locked-mode --disable-parallel -m:1 /nodeReuse:false /p:UseSharedCompilation=false
"$dotnet_host" build "$script_dir/LocalTest.csproj" --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
"$dotnet_host" "$script_dir/bin/Debug/net10.0/LocalTest.dll" "$@"
