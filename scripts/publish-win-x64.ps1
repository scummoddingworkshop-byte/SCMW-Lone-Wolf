# Publishes SCMW — Lone Wolf as a self-contained Windows x64 build and zips it, ready to
# attach to a GitHub Release. No .NET install is needed by the player.
#
# Usage:   powershell -ExecutionPolicy Bypass -File scripts\publish-win-x64.ps1
#
# Output:  artifacts\release\SCMW-Lone-Wolf-v<version>-win-x64\        (the folder to run)
#          artifacts\release\SCMW-Lone-Wolf-v<version>-win-x64.zip    (the file to upload)

$ErrorActionPreference = "Stop"

$repo   = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repo "src\SCMW.App\SCMW.App.csproj"

# The version lives in one place; read it from there.
$match = Select-String -Path $csproj -Pattern "<Version>(.+?)</Version>"
if (-not $match) { throw "Could not read <Version> from $csproj" }
$version = $match.Matches[0].Groups[1].Value
$name    = "SCMW-Lone-Wolf-v$version-win-x64"

$out = Join-Path $repo "artifacts\release\$name"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

Write-Host "Publishing $name ..."
dotnet publish (Join-Path $repo "src\SCMW.App") -c Release -r win-x64 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Debug symbols are for developers, not players.
Get-ChildItem $out -Recurse -Filter *.pdb | Remove-Item -Force

$zip = Join-Path $repo "artifacts\release\$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -CompressionLevel Optimal

$mb = "{0:N1}" -f ((Get-Item $zip).Length / 1MB)
Write-Host ""
Write-Host "Done."
Write-Host "  Folder: $out"
Write-Host "  Zip:    $zip ($mb MB)"
