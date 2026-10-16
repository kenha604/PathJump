# Builds a standalone PathJump.exe (no .NET install needed) and zips it with the tutorial.
# Usage: run from anywhere:  pwsh -File publish.ps1
#
# Output is only dist\PathJump.zip. The exe is staged under obj\ and not left in dist\, because
# each PathJump copy keeps its own Data folder: running a build output by mistake starts with an
# empty list, and the next publish would wipe whatever that copy had saved.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$stage = Join-Path $root "obj\publish\PathJump"
$dist = Join-Path $root "dist"
$zip = Join-Path $dist "PathJump.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

dotnet publish (Join-Path $root "PathJump.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Copy-Item (Join-Path $root "README.md") (Join-Path $stage "PATHJUMP_TUTORIAL.md")
New-Item -ItemType Directory -Force $dist | Out-Null
Compress-Archive -Path (Join-Path $stage "PathJump.exe"), (Join-Path $stage "PATHJUMP_TUTORIAL.md") -DestinationPath $zip -Force

"Built: $(Join-Path $stage 'PathJump.exe')"
"Zip:   $zip  ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
