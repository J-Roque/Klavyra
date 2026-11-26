# Builds the desktop version into desktop\out\Klavyra:
#   Klavyra.exe          launcher with its window (self-contained .NET)
#   api\                 Klavyra.Api.exe (self-contained) + wwwroot with the compiled web app
#   worker\              Python worker code + pyproject.toml/uv.lock (the environment is created on the user's PC)
#   tools\               ffmpeg.exe (pinned essentials build, downloaded and checked) and uv.exe (from this PC)
# and then desktop\out\Klavyra-Setup-<version>.exe when Inno Setup 6 is installed.
# Usage (from the repository root):  powershell -ExecutionPolicy Bypass -File desktop\build.ps1
# Messages are ASCII on purpose: Windows PowerShell 5.1 reads scripts without a BOM as ANSI.
param(
    [string]$Output = (Join-Path $PSScriptRoot 'out\Klavyra'),
    [switch]$SkipWeb
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Step([string]$text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Check([string]$what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" } }

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
New-Item -ItemType Directory -Force $Output | Out-Null

if (-not $SkipWeb) {
    Step 'Web app (ng build)'
    Push-Location (Join-Path $root 'web')
    try { npx ng build; Check 'ng build' } finally { Pop-Location }
}
$web = Join-Path $root 'web\dist\web\browser'
if (-not (Test-Path (Join-Path $web 'index.html'))) { throw "No compiled web app in $web" }

Step 'Launcher (Klavyra.exe)'
dotnet publish (Join-Path $root 'src\Klavyra.Desktop') -c Release -r win-x64 --self-contained -o $Output -v quiet
Check 'dotnet publish Klavyra.Desktop'

Step 'API + web app'
$api = Join-Path $Output 'api'
dotnet publish (Join-Path $root 'src\Klavyra.Api') -c Release -r win-x64 --self-contained -o $api -v quiet
Check 'dotnet publish Klavyra.Api'
# Copied into the published output, never into src\Klavyra.Api\wwwroot, so development builds stay as they are.
Copy-Item $web (Join-Path $api 'wwwroot') -Recurse

Step 'Worker code'
$worker = Join-Path $Output 'worker'
New-Item -ItemType Directory -Force $worker | Out-Null
Copy-Item (Join-Path $root 'worker\pyproject.toml'), (Join-Path $root 'worker\uv.lock') $worker
Copy-Item (Join-Path $root 'worker\klavyra_worker') $worker -Recurse
Get-ChildItem $worker -Recurse -Directory -Filter '__pycache__' | Remove-Item -Recurse -Force

Step 'Tools (ffmpeg, uv)'
$tools = Join-Path $Output 'tools'
New-Item -ItemType Directory -Force $tools | Out-Null

# ffmpeg: the pinned "essentials" build (105 MB instead of the 228 MB full build), checked against its SHA256.
$ffmpegVersion = '9.0.2'
$ffmpegSha256 = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
$ffmpegUrl = "https://github.com/GyanD/codexffmpeg/releases/download/$ffmpegVersion/ffmpeg-$ffmpegVersion-essentials_build.zip"
$cache = Join-Path $PSScriptRoot 'cache'
New-Item -ItemType Directory -Force $cache | Out-Null
$zip = Join-Path $cache "ffmpeg-$ffmpegVersion-essentials_build.zip"
if (-not (Test-Path $zip) -or (Get-FileHash $zip -Algorithm SHA256).Hash -ne $ffmpegSha256) {
    Write-Host "Downloading $ffmpegUrl"
    $ProgressPreference = 'SilentlyContinue'  # the progress bar makes Invoke-WebRequest very slow in PowerShell 5.1
    Invoke-WebRequest $ffmpegUrl -OutFile $zip -UseBasicParsing
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash
    if ($hash -ne $ffmpegSha256) { Remove-Item $zip; throw "ffmpeg download has SHA256 $hash, expected $ffmpegSha256" }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -like '*/bin/ffmpeg.exe') { $target = Join-Path $tools 'ffmpeg.exe' }
        elseif ($entry.FullName -match '^[^/]+/LICENSE$') { $target = Join-Path $tools 'ffmpeg-LICENSE.txt' }
        else { continue }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
    }
} finally { $archive.Dispose() }
if (-not (Test-Path (Join-Path $tools 'ffmpeg.exe'))) { throw "ffmpeg.exe not found inside $zip" }

$uv = Get-Command 'uv.exe' -ErrorAction SilentlyContinue
if (-not $uv) { throw 'uv.exe was not found on this PC (https://docs.astral.sh/uv/)' }
Copy-Item $uv.Source $tools

Step 'Licenses'
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $Output 'LICENSE.txt')
Copy-Item (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.txt') $Output

$size = (Get-ChildItem $Output -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("`nDone: {0} ({1:N0} MB)" -f $Output, $size) -ForegroundColor Green

Step 'Installer (Inno Setup)'
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
$iscc = (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if ($iscc) {
    & $iscc /Q "/DAppVersion=$version" "/DSourceDir=$Output" (Join-Path $PSScriptRoot 'klavyra.iss')
    Check 'ISCC'
    Write-Host ("Installer: {0}" -f (Join-Path $PSScriptRoot "out\Klavyra-Setup-$version.exe")) -ForegroundColor Green
} else {
    Write-Warning 'Inno Setup 6 is not installed (winget install JRSoftware.InnoSetup); skipping the installer.'
}
