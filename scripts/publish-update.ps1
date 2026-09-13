# Publish a Windows zip: one compressed exe + Setup.cmd (+ GitHub Release if gh is logged in)
# Usage: powershell -File scripts/publish-update.ps1 [-Version 0.3.2] [-Changelog "Fixes"]
param(
    [string]$Version = "",
    [string]$Changelog = "Bug fixes and improvements.",
    [string]$Repo = "OpolyCorporation/degoogle-kit",
    [switch]$SkipGitHub
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

$csproj = Join-Path $root "DeGoogleKit.csproj"
if (-not $Version) {
    [xml]$xml = Get-Content $csproj
    $Version = $xml.Project.PropertyGroup.Version | Select-Object -First 1
}

$outDir = Join-Path $root "artifacts\win-x64"
$zip = Join-Path $root "artifacts\DeGoogleKit-win-x64.zip"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
if (Test-Path $zip) { Remove-Item $zip -Force }

Write-Host "Publishing $Version (single-file, compressed, self-contained)..."
dotnet publish $csproj -c Release -r win-x64 --self-contained true -o $outDir `
    -p:Version=$Version `
    -p:InformationalVersion=$Version `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:CopyOutputSymbolsToPublishDirectory=false
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Get-ChildItem $outDir -Recurse -Include *.pdb, *.xml, createdump.exe | Remove-Item -Force -ErrorAction SilentlyContinue

@'
DeGoogle Kit for Windows
========================
1. Unzip this folder (one app file, not gigabytes).
2. Double-click Setup.cmd to copy it into your user folder and start it
   — or just run DeGoogleKit.exe from this folder.
3. On first launch, choose Desktop / Start menu shortcuts if you want them.
4. Scan, plan, Takeout, and GDPR work without paying. Pro is optional.

If Windows SmartScreen appears: More info → Run anyway.

https://github.com/OpolyCorporation/degoogle-kit/releases/latest
'@ | Set-Content -Path (Join-Path $outDir "README.txt") -Encoding UTF8

@'
@echo off
setlocal
title DeGoogle Kit setup
set "DEST=%LOCALAPPDATA%\Programs\DeGoogleKit"
echo Installing DeGoogle Kit for this Windows user...
mkdir "%DEST%" >nul 2>&1
copy /Y "%~dp0DeGoogleKit.exe" "%DEST%\DeGoogleKit.exe" >nul
if exist "%~dp0README.txt" copy /Y "%~dp0README.txt" "%DEST%\README.txt" >nul
if exist "%~dp0updates" xcopy /E /I /Y "%~dp0updates" "%DEST%\updates\" >nul
if not exist "%DEST%\DeGoogleKit.exe" (
  echo Could not copy DeGoogleKit.exe
  pause
  exit /b 1
)
echo.
echo Installed to:
echo   %DEST%\DeGoogleKit.exe
echo The app will ask if you want Desktop and Start menu shortcuts.
echo Scan, plan, Takeout, and GDPR work without Pro.
echo.
start "" "%DEST%\DeGoogleKit.exe"
endlocal
'@ | Set-Content -Path (Join-Path $outDir "Setup.cmd") -Encoding ASCII

if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($outDir, $zip)

$sha = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$download = "https://github.com/$Repo/releases/download/v$Version/DeGoogleKit-win-x64.zip"
$manifestPath = Join-Path $root "updates\latest.json"
$manifest = @{
    version     = $Version
    releasedAt  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd")
    changelog   = $Changelog
    downloadUrl = $download
    sha256      = $sha
} | ConvertTo-Json
Set-Content -Path $manifestPath -Value $manifest -Encoding UTF8
Copy-Item $manifestPath (Join-Path $root "artifacts\latest.json") -Force

Write-Host "Zip: $zip"
Write-Host ("Size: {0:N1} MB" -f ((Get-Item $zip).Length / 1MB))
Write-Host "SHA256: $sha"
Write-Host "Manifest: $manifestPath"

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $SkipGitHub -and $gh) {
    Write-Host "Creating GitHub release v$Version (upload zip + latest.json)..."
    gh release create "v$Version" $zip (Join-Path $root "artifacts\latest.json") --repo $Repo --title "DeGoogle Kit $Version" --notes $Changelog
} elseif ($SkipGitHub) {
    Write-Host "SkipGitHub: zip and latest.json are in artifacts/."
} else {
    Write-Host "gh not found. Upload artifacts\DeGoogleKit-win-x64.zip and artifacts\latest.json to a GitHub Release named v$Version."
}
