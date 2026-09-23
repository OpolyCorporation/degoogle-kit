# Publish a Windows zip for GitHub Releases.
#
# Packaging choice (Norton / SmartScreen):
#   We ship a self-contained *folder* (not PublishSingleFile). Single-file .NET
#   apps unpack to temp at runtime and look like droppers to many AVs.
#   Optional Authenticode signing when WINDOWS_CERT_PFX / WINDOWS_CERT_PASSWORD
#   secrets are set in CI — that is the real fix for Norton/SmartScreen.
#
# Usage: powershell -File scripts/publish-update.ps1 [-Version 0.4.1] [-Changelog "…"]
param(
    [string]$Version = "",
    [string]$Changelog = "Bug fixes and improvements.",
    [string]$Repo = "OpolyCorporation/degoogle-kit",
    [switch]$SkipGitHub,
    [switch]$SingleFile
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

$useSingle = [bool]$SingleFile
if ($useSingle) {
    Write-Host "Publishing $Version (single-file — more AV false positives; prefer default multi-file)..."
} else {
    Write-Host "Publishing $Version (self-contained folder — better for Norton/SmartScreen)..."
}

$publishArgs = @(
    "publish", $csproj,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-o", $outDir,
    "-p:Version=$Version",
    "-p:InformationalVersion=$Version",
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-p:CopyOutputSymbolsToPublishDirectory=false"
)
if ($useSingle) {
    $publishArgs += @(
        "-p:PublishSingleFile=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true"
    )
} else {
    # Explicit: do not pack into one exe that extracts to %TEMP%
    $publishArgs += @(
        "-p:PublishSingleFile=false",
        "-p:IncludeAllContentForSelfExtract=false"
    )
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Get-ChildItem $outDir -Recurse -Include *.pdb, *.xml, createdump.exe | Remove-Item -Force -ErrorAction SilentlyContinue

function Sign-PublishedBinaries {
    param([string]$Dir)
    $pfxB64 = $env:WINDOWS_CERT_PFX
    $pfxPass = $env:WINDOWS_CERT_PASSWORD
    if ([string]::IsNullOrWhiteSpace($pfxB64)) {
        Write-Host "No WINDOWS_CERT_PFX secret — shipping unsigned (Norton/SmartScreen may warn)."
        return
    }

    $pfxPath = Join-Path $env:TEMP "dgk-code-sign.pfx"
    try {
        [IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($pfxB64))
        $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
        if (-not $signtool) {
            Write-Warning "signtool.exe not found; skipping Authenticode."
            return
        }
        $targets = Get-ChildItem $Dir -Recurse -Include *.exe, *.dll |
            Where-Object { $_.Name -match '^(DeGoogleKit|DeGoogleKit\.)' -or $_.Extension -eq '.exe' }
        # Sign the main exe first, then sibling managed DLLs that ship with the app.
        $exe = Join-Path $Dir "DeGoogleKit.exe"
        $toSign = @()
        if (Test-Path $exe) { $toSign += $exe }
        $toSign += Get-ChildItem $Dir -Filter "*.dll" -File | Where-Object {
            $_.Name -like "DeGoogleKit*" -or $_.Name -like "DeGoogleKit.Licensing*"
        } | ForEach-Object { $_.FullName }

        foreach ($f in ($toSign | Select-Object -Unique)) {
            Write-Host "Signing $f ..."
            & $signtool sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com `
                /f $pfxPath /p $pfxPass $f
            if ($LASTEXITCODE -ne 0) { throw "signtool failed for $f" }
        }
        Write-Host "Authenticode signing complete."
    }
    finally {
        if (Test-Path $pfxPath) { Remove-Item $pfxPath -Force -ErrorAction SilentlyContinue }
    }
}

Sign-PublishedBinaries -Dir $outDir

@'
DeGoogle Kit for Windows
========================
Publisher: Opolyonix Corp (CVR 43410369, Denmark)

1. Unzip this folder somewhere permanent (e.g. Documents\DeGoogleKit).
2. Double-click Setup.cmd (recommended). It clears the Windows "downloaded from
   the internet" mark, copies the app into your user folder, and starts it.
   Or run DeGoogleKit.exe from this folder.
3. On first launch, choose Desktop / Start menu shortcuts if you want them.
4. Scan, plan, Takeout, and GDPR work without paying. Pro is optional.

Antivirus / Norton / SmartScreen
--------------------------------
Builds may be unsigned until we ship a code-signed release. That is a
reputation warning, not a virus. If Norton quarantines DeGoogleKit.exe:

  • Norton → Security → Scans → Quarantine → Restore DeGoogleKit.exe
  • Or allow the folder: %LOCALAPPDATA%\Programs\DeGoogleKit
  • Report a false positive: https://submit.norton.com/

Windows SmartScreen: More info → Run anyway (once).

https://github.com/OpolyCorporation/degoogle-kit/releases/latest
'@ | Set-Content -Path (Join-Path $outDir "README.txt") -Encoding UTF8

@'
@echo off
setlocal
title DeGoogle Kit setup
set "SRC=%~dp0"
set "DEST=%LOCALAPPDATA%\Programs\DeGoogleKit"

echo DeGoogle Kit — installing for this Windows user...
echo Publisher: Opolyonix Corp
echo.

REM Clear Mark-of-the-Web so Explorer/Norton treat files as local after install.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%SRC%' -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue" >nul 2>&1

if not exist "%SRC%DeGoogleKit.exe" (
  echo Could not find DeGoogleKit.exe next to Setup.cmd
  pause
  exit /b 1
)

mkdir "%DEST%" >nul 2>&1
echo Copying files to:
echo   %DEST%
robocopy "%SRC%." "%DEST%" /E /NFL /NDL /NJH /NJS /nc /ns /np >nul
set "RC=%ERRORLEVEL%"
if %RC% GEQ 8 (
  echo Copy failed (robocopy %RC%).
  pause
  exit /b 1
)
if not exist "%DEST%\DeGoogleKit.exe" (
  echo Copy failed.
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%DEST%' -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue" >nul 2>&1

echo.
echo Installed. The app will ask about Desktop / Start menu shortcuts.
echo Scan, plan, Takeout, and GDPR work without Pro.
echo If Norton blocks the app, restore it from Quarantine and allow this folder.
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
