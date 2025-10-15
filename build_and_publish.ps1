param(
  [switch]$UseNpmCi,
  [string[]]$RuntimeIdentifiers = @(
    "win-x64",
    "linux-x64",
    "linux-arm64",
    "osx-x64",
    "osx-arm64"
  ),
  [string]$DotnetConfiguration = "Release"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

if (-not $RuntimeIdentifiers -or $RuntimeIdentifiers.Count -eq 0) {
  throw "No runtime identifiers provided. Supply values with -RuntimeIdentifiers."
}

$RuntimeIdentifiers = $RuntimeIdentifiers | Where-Object { $_ -and $_.Trim() -ne "" }
if ($RuntimeIdentifiers.Count -eq 0) {
  throw "No runtime identifiers provided. Supply values with -RuntimeIdentifiers."
}

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

$ServerProject = Join-Path $Root "server/UdonSharpLsp.Server.csproj"

Write-Host "=== dotnet clean/restore/build (server) ==="
dotnet clean $ServerProject -c $DotnetConfiguration
dotnet restore $ServerProject
dotnet build $ServerProject -c $DotnetConfiguration

foreach ($rid in $RuntimeIdentifiers) {
  Write-Host "=== dotnet publish (server) [$rid] ==="
  dotnet publish $ServerProject -c $DotnetConfiguration -r $rid --self-contained

  Write-Host "=== Copy publish output to resources/server/$rid ==="
  $src  = Join-Path $Root "server/bin/$DotnetConfiguration/net8.0/$rid/publish"
  $dest = Join-Path $Root "resources/server/$rid"

  if (-not (Test-Path $src)) {
    throw "Publish output not found at $src"
  }

  if (Test-Path $dest) {
    Remove-Item $dest -Recurse -Force
  }

  New-Item -ItemType Directory -Force -Path $dest | Out-Null
  Copy-Item (Join-Path $src "*") $dest -Recurse -Force
}

Write-Host "SUCCESS"
