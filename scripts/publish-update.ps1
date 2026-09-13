# Publish a cloud update: zip + latest.json (+ GitHub Release if gh is logged in)
# Usage: powershell -File scripts/publish-update.ps1 [-Version 0.3.1] [-Changelog "Fixes"]
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
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
if (Test-Path $zip) { Remove-Item $zip -Force }

Write-Host "Publishing $Version..."
dotnet publish $csproj -c Release -r win-x64 --self-contained true -o $outDir -p:Version=$Version -p:InformationalVersion=$Version
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

@'
DeGoogle Kit for Windows
========================
1. Unzip this folder anywhere you like.
2. Double-click DeGoogleKit.exe. You do not need to install .NET.
3. If Windows SmartScreen says it is an unrecognized app, choose More info → Run anyway. Builds are not code-signed yet.

Download later builds from:
https://github.com/OpolyCorporation/degoogle-kit/releases/latest
'@ | Set-Content -Path (Join-Path $outDir "README.txt") -Encoding UTF8

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
