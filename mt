#!/usr/bin/env bash
# mt — 명령줄 도구를 짧게 부른다: ./mt validate a.motion-authoring.json
# 빌드된 것이 없거나 소스가 더 새로우면 먼저 빌드한다(그 말은 stderr로 — stdout은 결과의 JSON뿐이다).
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dll="$root/src/Mt.Cli/bin/Release/net10.0/mt.dll"

if [ ! -f "$dll" ] || [ -n "$(find "$root/src" -name '*.cs' -newer "$dll" -not -path '*/obj/*' -not -path '*/bin/*' -print -quit)" ]; then
  dotnet build "$root/src/Mt.Cli/Mt.Cli.csproj" -c Release --nologo -v quiet 1>&2
fi

exec dotnet "$dll" "$@"
