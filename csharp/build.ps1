<#
    Build the C# ASUS Display Control app into an installable package.

    Usage:
        powershell -ExecutionPolicy Bypass -File csharp\build.ps1                # tiny, framework-dependent
        powershell -ExecutionPolicy Bypass -File csharp\build.ps1 -SelfContained # larger, no .NET needed

    Produces:  csharp\publish\ASUS-Display-Control.exe   (+ bundled cli\windows\dwc\)

    Default is framework-dependent: a few MB total, but the target PC needs the
    .NET 8 Desktop Runtime (https://dotnet.microsoft.com/download/dotnet/8.0).
    -SelfContained bundles the runtime (~145 MB) so no prerequisites are needed.
#>
param(
    [switch]$SelfContained
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $PSScriptRoot

# Resolve dotnet (PATH, or the user-local install used during development).
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCmd) { $dotnet = $dotnetCmd.Source } else { $dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "dotnet SDK not found. Install the .NET 8 SDK." }

# 1. Ensure the ASUS CLI is extracted so it can be bundled.
$cliDir = Join-Path $root "cli\windows\dwc"
if (-not (Test-Path (Join-Path $cliDir "dwc.exe"))) {
    Write-Host "==> Extracting bundled CLI" -ForegroundColor Cyan
    Expand-Archive -Path (Join-Path $root "cli\windows\dwc_win.zip") -DestinationPath (Join-Path $root "cli\windows") -Force
}

if (-not (Test-Path (Join-Path $PSScriptRoot "icon.ico"))) {
    throw "csharp\icon.ico is missing (it is checked into the repo and embedded in the app)."
}

# 2. Publish (one-folder, not single-file: loose DLLs stay memory-mapped/shared -> low RAM).
$outDir = Join-Path $PSScriptRoot "publish"
Remove-Item -Recurse -Force $outDir -ErrorAction SilentlyContinue

$common = @(
    "publish", "AsusDisplayControl.csproj",
    "-c", "Release",
    "-r", "win-x64",
    "-o", $outDir,
    "-p:DebugType=none"
)
if ($SelfContained) {
    Write-Host "==> Publishing (self-contained; no .NET needed on target, ~145 MB)" -ForegroundColor Cyan
    & $dotnet @common --self-contained true
} else {
    Write-Host "==> Publishing (framework-dependent; needs .NET 8 Desktop Runtime, a few MB)" -ForegroundColor Cyan
    & $dotnet @common --self-contained false
}
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$exe = Join-Path $outDir "ASUS-Display-Control.exe"
if (-not (Test-Path $exe)) { throw "Build failed: $exe not found" }

$total = (Get-ChildItem $outDir -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host "`n==> Built: $exe" -ForegroundColor Green
Write-Host ("    Total size: {0} MB ({1} files)" -f [math]::Round($total/1MB,2), (Get-ChildItem $outDir -Recurse -File).Count) -ForegroundColor Green
