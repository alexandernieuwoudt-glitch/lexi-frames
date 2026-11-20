# Builds a self-contained, single-file release of Lexi Frames and zips it.
# Output: release\LexiFrames-v<version>-win-x64.zip
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

[xml]$proj = Get-Content (Join-Path $root 'LexiFrames.csproj')
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { $version = '0.0.0' }

$out   = Join-Path $root 'release'
$stage = Join-Path $out "LexiFrames-v$version-win-x64"
$zip   = "$stage.zip"
Remove-Item $stage, $zip -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none `
  -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Copy-Item (Join-Path $root 'LICENSE') $stage
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $stage
@"
Lexi Frames v$version
=====================

1. Run LexiFrames.exe (it asks for administrator rights; needed for FPS and sensors).
2. Optional, for CPU temperature: install the free PawnIO driver
     winget install namazso.PawnIO
3. Right-click the green L tray icon for Options. Ctrl+Shift+O shows/hides the overlay.

If Windows shows "Windows protected your PC", click More info -> Run anyway
(the app is open source but not code-signed).
"@ | Set-Content (Join-Path $stage 'README.txt') -Encoding utf8

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Release ready: $zip"
Get-ChildItem $stage | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
