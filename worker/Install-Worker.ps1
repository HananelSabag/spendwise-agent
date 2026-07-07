# ============================================================================
#  Install-Worker.ps1 - one-time setup for the SpendWise Worker
#  Creates a "SpendWise Worker" shortcut on your Desktop and Start Menu that
#  points at the self-contained C# SpendWiseWorker.exe (real logo, no console
#  flash). Builds the exe first if it doesn't exist yet. Run once:
#     powershell -ExecutionPolicy Bypass -File worker\Install-Worker.ps1
# ============================================================================

$ErrorActionPreference = 'Stop'
$WorkerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Exe = Join-Path $WorkerDir 'SpendWiseWorker.exe'
$BuildScript = Join-Path $WorkerDir 'Build-Exe.ps1'

if (-not (Test-Path $Exe)) {
  Write-Host "SpendWiseWorker.exe not found - building it now..." -ForegroundColor Gray
  & $BuildScript
}
if (-not (Test-Path $Exe)) { Write-Host "Build failed - see errors above." -ForegroundColor Red; exit 1 }

$WScriptShell = New-Object -ComObject WScript.Shell
$Fso = New-Object -ComObject Scripting.FileSystemObject

function New-Shortcut($path) {
  $sc = $WScriptShell.CreateShortcut($path)
  $sc.TargetPath = $Exe
  $sc.WorkingDirectory = $WorkerDir
  $sc.IconLocation = $Exe   # the exe's own embedded icon - our real logo
  $sc.Description = 'SpendWise Worker - bank sync agent'
  $sc.Save()
}

# WScript.Shell's CreateShortcut/Save marshals paths through COM in a way
# that mangles non-ASCII folder names - e.g. a OneDrive-redirected Desktop
# folder named in a non-English OS display language - and .Save() throws
# FileNotFoundException even though the folder genuinely exists. The fix:
# resolve to the 8.3 short path (always pure ASCII) for the SHORTCUT'S
# destination only; the .lnk still lands in and opens from the real,
# correctly-named folder - short and long paths point at the same place.
function Get-SafePath([string]$longPath) {
  try { return $Fso.GetFolder($longPath).ShortPath } catch { return $longPath }
}

$desktop = Get-SafePath ([Environment]::GetFolderPath('Desktop'))
$startMenuLong = Join-Path ([Environment]::GetFolderPath('Programs')) 'SpendWise'
if (-not (Test-Path $startMenuLong)) { New-Item -ItemType Directory -Path $startMenuLong -Force | Out-Null }
$startMenu = Get-SafePath $startMenuLong

New-Shortcut (Join-Path $desktop 'SpendWise Worker.lnk')
New-Shortcut (Join-Path $startMenu 'SpendWise Worker.lnk')

# Windows caches shortcut icons aggressively - clear the icon cache so the
# new logo shows up immediately instead of after a reboot.
try {
  Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
  Remove-Item "$env:LOCALAPPDATA\IconCache.db" -Force -ErrorAction SilentlyContinue
  Start-Process explorer.exe
} catch { }

$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Exe).FileVersion
Write-Host "OK - 'SpendWise Worker' (build $fileVersion) added to your Desktop and Start Menu." -ForegroundColor Green
Write-Host "Double-click it to open the worker. Tick 'Launch on Windows startup' inside to run it automatically after every reboot." -ForegroundColor Gray
Write-Host ""
Write-Host "Changed the worker script or logo? Rebuild with:" -ForegroundColor Gray
Write-Host "  powershell -ExecutionPolicy Bypass -File worker\Build-Exe.ps1" -ForegroundColor DarkGray
