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

echo "=== dotnet clean/restore/build/publish (server) ==="
pushd "server" >/dev/null
dotnet clean
dotnet restore
dotnet build
dotnet publish -c Release -r win-x64 --self-contained
popd >/dev/null

echo "=== Copy publish output to resources/server/win-x64 ==="
SRC="server/bin/Release/net8.0/win-x64/publish"
DEST="resources/server/win-x64"
mkdir -p "$DEST"
cp -Rf "${SRC}/." "$DEST/"

echo "SUCCESS"
