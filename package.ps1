# package.ps1 - Builds and packages iceman-gui into a self-contained release zip
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "dist"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "   iceman-gui Self-Contained Release Packaging Script   " -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

$root = $PSScriptRoot
$proj = Join-Path $root "iceman-gui.csproj"
$publishDir = Join-Path $root "$OutputDir\$Runtime"
$zipPath = Join-Path $root "$OutputDir\iceman-gui-windows-$Runtime.zip"
$sumsPath = Join-Path $root "$OutputDir\SHA256SUMS.txt"

# 1. Clean previous build outputs
Write-Host "[1/5] Cleaning old publish directory: $publishDir..." -ForegroundColor Yellow
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

# 2. Publish self-contained
Write-Host "[2/5] Publishing self-contained binaries ($Configuration, $Runtime)..." -ForegroundColor Yellow
dotnet publish $proj -c $Configuration -r $Runtime --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# 3. Copy documentation and licenses
Write-Host "[3/5] Copying documentation and licenses..." -ForegroundColor Yellow
Copy-Item -Path (Join-Path $root "README.md"), (Join-Path $root "LICENSE") -Destination $publishDir -ErrorAction SilentlyContinue

# 4. Rigorous verification
Write-Host "[4/5] Verifying packaged components..." -ForegroundColor Yellow
$requiredFiles = @(
    (Join-Path $publishDir "iceman-gui.exe"),
    (Join-Path $publishDir "client\proxmark3.exe"),
    (Join-Path $publishDir "client\libs\Qt6Core.dll"),
    (Join-Path $publishDir "client\setup.bat"),
    (Join-Path $publishDir "pm3.bat")
)

foreach ($f in $requiredFiles) {
    if (-not (Test-Path $f)) {
        throw "Verification FAILED: Missing required file '$f'"
    }
}
Write-Host "  -> All core executables and bundled Proxmark3 files verified successfully!" -ForegroundColor Green

# 5. Compress to ZIP
Write-Host "[5/5] Compressing to $zipPath..." -ForegroundColor Yellow
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash
"$hash  $(Split-Path $zipPath -Leaf)" | Out-File -Encoding ascii $sumsPath

$zipSizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
Write-Host "`n[SUCCESS] Package generated successfully!" -ForegroundColor Green
Write-Host "  File:   $zipPath ($zipSizeMb MB)" -ForegroundColor White
Write-Host "  SHA256: $hash" -ForegroundColor White
