#!/usr/bin/env bash
set -euo pipefail # TEMP for CI/CD

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== npm install ==="
if [[ -f package-lock.json ]]; then
  npm ci
else
  npm install
fi

echo "=== npm run lint ==="
npm run lint

echo "=== npm run check-types ==="
npm run check-types

echo "=== npm run compile ==="
npm run compile

SERVER_PROJECT="server/UdonSharpLsp.Server.csproj"
DOTNET_CONFIGURATION="${DOTNET_CONFIGURATION:-Release}"
DEFAULT_RUNTIME_IDENTIFIERS=(win-x64 linux-x64 linux-arm64 osx-x64 osx-arm64)

if [[ -n "${DOTNET_RIDS:-}" ]]; then
  RUNTIME_IDENTIFIERS=()
  for RUNTIME_ID in ${DOTNET_RIDS}; do
    RUNTIME_IDENTIFIERS+=("$RUNTIME_ID")
  done
else
  RUNTIME_IDENTIFIERS=("${DEFAULT_RUNTIME_IDENTIFIERS[@]}")
fi

if [[ ${#RUNTIME_IDENTIFIERS[@]} -eq 0 ]]; then
  echo "No runtime identifiers configured. Set DOTNET_RIDS to at least one value." >&2
  exit 1
fi

echo "=== dotnet clean/restore/build (server) ==="
dotnet clean "$SERVER_PROJECT" -c "$DOTNET_CONFIGURATION"
dotnet restore "$SERVER_PROJECT"
dotnet build "$SERVER_PROJECT" -c "$DOTNET_CONFIGURATION"

for RID in "${RUNTIME_IDENTIFIERS[@]}"; do
  echo "=== dotnet publish (server) [${RID}] ==="
  dotnet publish "$SERVER_PROJECT" -c "$DOTNET_CONFIGURATION" -r "$RID" --self-contained

  echo "=== Copy publish output to resources/server/${RID} ==="
  SRC="server/bin/${DOTNET_CONFIGURATION}/net8.0/${RID}/publish"
  DEST="resources/server/${RID}"
  rm -rf "$DEST"
  mkdir -p "$DEST"
  cp -Rf "${SRC}/." "$DEST/"
done

echo "SUCCESS"
