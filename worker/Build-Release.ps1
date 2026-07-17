# Builds the self-contained personal Worker release.
# Output layout stays deliberately simple:
#   SpendWiseWorker.exe   <- the only file users launch
#   START-HERE.md         <- bilingual help
#   app/                  <- private runtime plumbing; users can ignore it

[CmdletBinding()]
param(
  [string]$OutputZip
)

$ErrorActionPreference = 'Stop'
$WorkerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $WorkerDir
$ReleaseDir = Join-Path $RepoRoot 'SpendWise Agent Release'
if (-not $OutputZip) { $OutputZip = Join-Path $RepoRoot 'SpendWiseAgent-ForUsers.zip' }

function Assert-RepoChild([string]$Path) {
  $root = [IO.Path]::GetFullPath($RepoRoot).TrimEnd('\') + '\'
  $full = [IO.Path]::GetFullPath($Path)
  if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing filesystem operation outside repository: $full"
  }
  return $full
}

$ReleaseDir = Assert-RepoChild $ReleaseDir
$OutputZip = Assert-RepoChild $OutputZip

& (Join-Path $WorkerDir 'Build-Exe.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Worker executable build failed.' }

$node = (Get-Command node.exe -ErrorAction Stop).Source
$chrome = Get-ChildItem (Join-Path $env:USERPROFILE '.cache\puppeteer\chrome') `
  -Recurse -Filter chrome.exe -ErrorAction SilentlyContinue |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1
if (-not $chrome) { throw 'Bundled Chromium was not found in the Puppeteer cache. Run npm install first.' }
$chromeRoot = Split-Path -Parent $chrome.FullName

if (Test-Path -LiteralPath $ReleaseDir) { Remove-Item -LiteralPath $ReleaseDir -Recurse -Force }
if (Test-Path -LiteralPath $OutputZip) { Remove-Item -LiteralPath $OutputZip -Force }

$app = Join-Path $ReleaseDir 'app'
New-Item -ItemType Directory -Path $app, (Join-Path $app 'runtime'), (Join-Path $app 'worker'), (Join-Path $app 'chromium') -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $WorkerDir 'SpendWiseWorker.exe') -Destination $ReleaseDir
Copy-Item -LiteralPath (Join-Path $WorkerDir 'spendwise.ico') -Destination $ReleaseDir
Copy-Item -LiteralPath (Join-Path $WorkerDir 'logo-source.png') -Destination $ReleaseDir
Copy-Item -LiteralPath (Join-Path $WorkerDir 'START-HERE.md') -Destination $ReleaseDir
Copy-Item -LiteralPath (Join-Path $WorkerDir 'release.env') -Destination (Join-Path $app '.env')
Copy-Item -LiteralPath (Join-Path $RepoRoot 'package.json') -Destination $app
Copy-Item -LiteralPath (Join-Path $RepoRoot 'package-lock.json') -Destination $app
Copy-Item -LiteralPath (Join-Path $RepoRoot 'src') -Destination $app -Recurse
Copy-Item -LiteralPath (Join-Path $RepoRoot 'patches') -Destination $app -Recurse
Copy-Item -LiteralPath (Join-Path $RepoRoot 'node_modules') -Destination $app -Recurse
Copy-Item -LiteralPath (Join-Path $WorkerDir 'i18n') -Destination (Join-Path $app 'worker') -Recurse
Copy-Item -LiteralPath $node -Destination (Join-Path $app 'runtime\node.exe')
Copy-Item -LiteralPath $chromeRoot -Destination (Join-Path $app 'chromium\chrome-win64') -Recurse

# Never ship local scrape artifacts or Chromium's first-run leftovers.
$runtimeScrapes = Join-Path $app 'node_modules\scraped-data'
if (Test-Path -LiteralPath $runtimeScrapes) { Remove-Item -LiteralPath $runtimeScrapes -Recurse -Force }
foreach ($junk in @('debug.log', 'First Run')) {
  $path = Join-Path $app "chromium\chrome-win64\$junk"
  if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}

$forbidden = Get-ChildItem -LiteralPath $ReleaseDir -Recurse -Force |
  Where-Object {
    $_.Name -in @(
      'agent-private.key', '.agent-device.json', '.agent-state.json',
      '.worker-state.json', 'worker-profile.json', '.chrome-profile',
      'scraped-data', 'RAW_DEBUG'
    ) -or $_.Name -like '*.log'
  }
if ($forbidden) {
  throw "Release contains forbidden runtime/private artifacts: $($forbidden.FullName -join ', ')"
}

$smoke = Start-Process -FilePath (Join-Path $ReleaseDir 'SpendWiseWorker.exe') `
  -ArgumentList '--smoke' -Wait -PassThru -WindowStyle Hidden
if ($smoke.ExitCode -ne 0) { throw "Packaged Worker smoke test failed (exit $($smoke.ExitCode))." }

Compress-Archive -Path (Join-Path $ReleaseDir '*') -DestinationPath $OutputZip -CompressionLevel Optimal
Write-Output "Release folder: $ReleaseDir"
Write-Output "Release ZIP: $OutputZip"
Write-Output "ZIP size: $([math]::Round((Get-Item $OutputZip).Length / 1MB, 1)) MB"
