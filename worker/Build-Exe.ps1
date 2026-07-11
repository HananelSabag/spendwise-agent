# ============================================================================
# Build-Exe.ps1 - publishes the real C# SpendWise Worker desktop app.
#
# No IDE needed:
#   powershell -ExecutionPolicy Bypass -File worker\Build-Exe.ps1
#
# Output:
#   worker\SpendWiseWorker.exe
# ============================================================================

$ErrorActionPreference = 'Stop'

$WorkerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $WorkerDir 'desktop\SpendWiseWorker.Desktop.csproj'
$PublishDir = Join-Path $WorkerDir 'desktop\bin\Release\net8.0-windows\win-x64\publish'
$OutExe = Join-Path $WorkerDir 'SpendWiseWorker.exe'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  Write-Error 'dotnet SDK not found. Install the .NET Desktop SDK, then run this again.'
  exit 1
}

if (-not (Test-Path $Project)) {
  Write-Error "Project not found: $Project"
  exit 1
}

$now = Get-Date
$version = '{0}.{1}.{2}.{3}' -f $now.ToString('yy'), $now.Month, $now.Day, ($now.Hour * 100 + $now.Minute)
Write-Output "Build version: $version"

dotnet publish $Project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:Version=$version `
  -p:AssemblyVersion=$version `
  -p:FileVersion=$version `
  -p:InformationalVersion=$version

if ($LASTEXITCODE -ne 0) {
  Write-Error "dotnet publish failed (exit $LASTEXITCODE)"
  exit 1
}

$publishedExe = Join-Path $PublishDir 'SpendWiseWorker.exe'
if (-not (Test-Path $publishedExe)) {
  Write-Error "Published exe not found: $publishedExe"
  exit 1
}

Copy-Item -LiteralPath $publishedExe -Destination $OutExe -Force
Copy-Item -LiteralPath (Join-Path $PublishDir 'spendwise.ico') -Destination (Join-Path $WorkerDir 'spendwise.ico') -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $PublishDir 'logo-source.png') -Destination (Join-Path $WorkerDir 'logo-source.png') -Force -ErrorAction SilentlyContinue

Write-Output ''
Write-Output "Built: $OutExe"
Write-Output "Version: $version"
Write-Output "Size: $((Get-Item $OutExe).Length) bytes"
