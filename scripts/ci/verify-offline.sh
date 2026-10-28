#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
cd "$repo_root"
offline_cli_home="$(mktemp -d "${TMPDIR:-/tmp}/dameng-offline.XXXXXX")"
trap 'rm -rf "$offline_cli_home"' EXIT
export DOTNET_CLI_HOME="$offline_cli_home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
unset DAMENG_TEST_CONNECTION_STRING ConnectionStrings__DamengCliFixture

dotnet restore W.EntityFrameworkCore.Dameng.slnx --locked-mode --disable-parallel -m:1 /nodeReuse:false /p:UseSharedCompilation=false
dotnet build W.EntityFrameworkCore.Dameng.slnx --configuration Release --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
scripts/local-test/run.sh test unit --configuration Release
scripts/local-test/run.sh test unit --configuration Release --driver w
scripts/local-test/run.sh test offline-fixtures --configuration Release
scripts/local-test/run.sh test offline-fixtures --configuration Release --driver w
scripts/local-test/run.sh selftest
PYTHONDONTWRITEBYTECODE=1 python3 -B scripts/audit/test_scan_aggregate_queries.py
python3 -I scripts/driver-candidate/verify-package.py --require-lock-reference
