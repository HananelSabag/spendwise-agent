# ============================================================================
#  Install-Worker.ps1 — one-time setup for the SpendWise Worker
#  Creates a clickable "SpendWise Worker" shortcut on your Desktop and Start
#  Menu that launches the tray app silently. Run once:
#     powershell -ExecutionPolicy Bypass -File worker\Install-Worker.ps1
# ============================================================================

$ErrorActionPreference = 'Stop'
$WorkerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Vbs = Join-Path $WorkerDir 'SpendWise-Worker.vbs'

if (-not (Test-Path $Vbs)) { Write-Host "Launcher not found: $Vbs" -ForegroundColor Red; exit 1 }

$WScriptShell = New-Object -ComObject WScript.Shell

function New-Shortcut($path) {
  $sc = $WScriptShell.CreateShortcut($path)
  $sc.TargetPath = 'wscript.exe'
  $sc.Arguments = '"' + $Vbs + '"'
  $sc.WorkingDirectory = $WorkerDir
  # A bank-ish icon from the Windows shell icon library
  $sc.IconLocation = "$env:SystemRoot\System32\imageres.dll,109"
  $sc.Description = 'SpendWise Worker — bank sync agent'
  $sc.Save()
}

$desktop = [Environment]::GetFolderPath('Desktop')
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'SpendWise'
if (-not (Test-Path $startMenu)) { New-Item -ItemType Directory -Path $startMenu -Force | Out-Null }

New-Shortcut (Join-Path $desktop 'SpendWise Worker.lnk')
New-Shortcut (Join-Path $startMenu 'SpendWise Worker.lnk')

Write-Host "OK — 'SpendWise Worker' added to your Desktop and Start Menu." -ForegroundColor Green
Write-Host "Double-click it to open the worker. Tick 'Launch on Windows startup' inside to run it automatically after every reboot." -ForegroundColor Gray
Write-Host ""
Write-Host "Optional — build a standalone .exe instead of the shortcut:" -ForegroundColor Gray
Write-Host "  Install-Module ps2exe -Scope CurrentUser" -ForegroundColor DarkGray
Write-Host "  Invoke-ps2exe worker\SpendWise-Worker.ps1 worker\SpendWiseWorker.exe -noConsole -iconFile <icon.ico>" -ForegroundColor DarkGray
