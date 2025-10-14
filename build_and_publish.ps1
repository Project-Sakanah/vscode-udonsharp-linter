param(
  [switch]$UseNpmCi # TEMP for CI/CD
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

Write-Host "=== npm install ==="
if ( (Test-Path "package-lock.json") -and $UseNpmCi ) {
  npm ci
} else {
  npm install
}

Write-Host "=== npm run lint ==="
npm run lint

Write-Host "=== npm run check-types ==="
npm run check-types

Write-Host "=== npm run compile ==="
npm run compile

Write-Host "=== dotnet clean/restore/build/publish (server) ==="
Push-Location "server"
dotnet clean
dotnet restore
dotnet build
dotnet publish -c Release -r win-x64 --self-contained
Pop-Location

Write-Host "=== Copy publish output to resources/server/win-x64 ==="
$Src  = Join-Path $Root "server/bin/Release/net8.0/win-x64/publish"
$Dest = Join-Path $Root "resources/server/win-x64"
New-Item -ItemType Directory -Force -Path $Dest | Out-Null
Copy-Item (Join-Path $Src "*") $Dest -Recurse -Force

Write-Host "SUCCESS"
