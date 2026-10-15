# Builds a standalone PathJump.exe (no .NET install needed) and zips it with the tutorial.
# Usage: run from anywhere:  pwsh -File publish.ps1
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "dist\PathJump"
$zip = Join-Path $root "dist\PathJump.zip"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish (Join-Path $root "PathJump.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Copy-Item (Join-Path $root "README.md") (Join-Path $out "PATHJUMP_TUTORIAL.md")
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip

Get-ChildItem $out | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
"Zip: $zip  ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
