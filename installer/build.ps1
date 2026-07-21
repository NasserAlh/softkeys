# softkeys installer build (NF-08) — one command, repeatable:
#   powershell -ExecutionPolicy Bypass -File installer\build.ps1
# Publishes the single-file exe (baseline §7 / CR-14), injects the csproj
# version into the Inno script (D-17), compiles softkeys-setup.exe, and prints
# the SHA-256 of both artifacts for the README and release notes.
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $repo 'softkeys.csproj'

# D-17: the csproj <Version> is the single source of truth
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No <Version> in softkeys.csproj' }

# User-local SDK (CR-05); fall back to PATH if present
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

Write-Host "Publishing softkeys $version..."
& $dotnet publish $csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
$exe = Join-Path $repo 'bin\Release\net8.0-windows\win-x64\publish\softkeys.exe'
if (-not (Test-Path $exe)) { throw "publish output missing: $exe" }

# Inno Setup 6 compiler — user-scope winget install first, then machine-wide
$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'ISCC.exe not found — install via: winget install -e --id JRSoftware.InnoSetup --scope user' }

Write-Host "Compiling installer ($iscc)..."
& $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'softkeys.iss') | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }
$setup = Join-Path $PSScriptRoot 'output\softkeys-setup.exe'

Write-Host ''
Write-Host "softkeys.exe        SHA-256: $((Get-FileHash $exe -Algorithm SHA256).Hash.ToLower())"
Write-Host "softkeys-setup.exe  SHA-256: $((Get-FileHash $setup -Algorithm SHA256).Hash.ToLower())"
Write-Host ''
Write-Host "artifacts:"
Write-Host "  $exe"
Write-Host "  $setup"
