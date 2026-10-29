# Publishes the app (self-contained, no .NET needed on the target PC) and builds the installer.
# Requires: .NET SDK and Inno Setup 6 (https://jrsoftware.org/isdl.php, free).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publish = Join-Path $root 'publish'

Write-Host '1/2  Publishing app...' -ForegroundColor Cyan
dotnet publish (Join-Path $root 'PhotoCapture.Host\PhotoCapture.Host.csproj') `
    -c Release -r win-x64 --self-contained true -p:DebugType=none -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$candidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    Write-Warning 'Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php and run this script again.'
    Write-Host "The app itself is ready in: $publish (run SSDPhotoCapture.exe)"
    exit 1
}

Write-Host '2/2  Building installer...' -ForegroundColor Cyan
& $iscc (Join-Path $root 'installer\SSDPhotoCapture.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }
Write-Host "Done: $(Join-Path $root 'installer\output')" -ForegroundColor Green
