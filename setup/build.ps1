# Build WatchMe (self-contained) and compile WatchMe-Setup.exe with Inno Setup 6.
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$publish = Join-Path $root "publish"
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $publish, $dist | Out-Null

Write-Host "Publishing WatchMe (self-contained win-x64)..."
dotnet publish (Join-Path $root "src\Watchout.Desktop\Watchout.Desktop.csproj") `
    -c $Configuration -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$iscc = $null
foreach ($candidate in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"))
{
    if (Test-Path $candidate) { $iscc = $candidate; break }
}
if (-not $iscc)
{
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc)
{
    throw "Inno Setup 6 not found. Install it from https://jrsoftware.org/isinfo.php or: winget install JRSoftware.InnoSetup"
}

Write-Host "Compiling installer with $iscc ..."
& $iscc (Join-Path $root "setup\WatchMe.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

$setup = Join-Path $dist "WatchMe-Setup.exe"
if (-not (Test-Path $setup)) { throw "Installer was not created: $setup" }
Write-Host "Installer ready: $setup"
