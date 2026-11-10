# Builds the desktop version into desktop\out\Klavyra:
#   Klavyra.exe          launcher with its window (self-contained .NET)
#   api\                 Klavyra.Api.exe (self-contained) + wwwroot with the compiled web app
#   worker\              Python worker code + pyproject.toml/uv.lock (the environment is created on the user's PC)
#   tools\               ffmpeg.exe and uv.exe, copied from this machine
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
foreach ($name in 'ffmpeg', 'uv') {
    $found = Get-Command "$name.exe" -ErrorAction SilentlyContinue
    if ($found) { Copy-Item $found.Source $tools }
    else { Write-Warning "$name.exe was not found on this PC; the build will not include it." }
}

$size = (Get-ChildItem $Output -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("`nDone: {0} ({1:N0} MB)" -f $Output, $size) -ForegroundColor Green
