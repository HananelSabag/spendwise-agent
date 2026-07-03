# ============================================================================
#  Build-Exe.ps1 - compiles SpendWiseWorker.exe using the C# compiler that
#  ships with every Windows install (.NET Framework v4.0.30319) - no
#  external package, no internet download, no ps2exe.
#
#  The exe is a tiny native launcher: it carries the real app icon (so the
#  desktop shortcut / taskbar / Explorer all show our logo, not a generic
#  Windows icon) and silently starts SpendWise-Worker.ps1 with no console
#  flash. A version stamp (build date + time) is embedded in the exe's
#  file properties AND printed in the worker window's footer, so you can
#  always tell at a glance whether you're running the latest build.
#
#  Run whenever SpendWise-Worker.ps1 or the icon changes:
#    powershell -ExecutionPolicy Bypass -File worker\Build-Exe.ps1
# ============================================================================

$ErrorActionPreference = 'Stop'
$WorkerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$BuildDir  = Join-Path $WorkerDir 'build'
$IconFile  = Join-Path $WorkerDir 'spendwise.ico'
$CsSource  = Join-Path $BuildDir 'Launcher.cs'
$OutExe    = Join-Path $WorkerDir 'SpendWiseWorker.exe'

$csc = 'C:\WINDOWS\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = 'C:\WINDOWS\Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { Write-Error 'No .NET Framework C# compiler found (csc.exe).'; exit 1 }
if (-not (Test-Path $IconFile)) { Write-Error "Icon not found: $IconFile - run Make-Icon.ps1 first."; exit 1 }

# ── Version stamp: yy.MM.dd.HHmm as a valid 4-part assembly version ────────
$now = Get-Date
$version = '{0}.{1}.{2}.{3}' -f $now.ToString('yy'), $now.Month, $now.Day, ($now.Hour * 100 + $now.Minute)
Write-Output "Build version: $version"

# Patch the version constant into a temp copy of the C# source (keep the
# checked-in source at the neutral placeholder so repeated builds don't
# dirty git).
$src = Get-Content $CsSource -Raw
$src = $src -replace 'public const string BuildVersion = "[^"]*";', "public const string BuildVersion = `"$version`";"
$tmpSrc = Join-Path $BuildDir 'Launcher.generated.cs'
Set-Content -Path $tmpSrc -Value $src -Encoding UTF8

# Also stamp the version into the worker script's footer label so it's
# visible in the running window itself, not just in exe file properties.
# Matches both the neutral text (first build) and an already-stamped footer
# from a previous build (every build after that) so this keeps re-stamping
# instead of silently doing nothing from the second build onward.
$workerPs1 = Join-Path $WorkerDir 'SpendWise-Worker.ps1'
$workerContent = Get-Content $workerPs1 -Raw
$stamped = $workerContent -replace 'SpendWise \. by Hananel Sabag(\s*\.\s*build\s*[0-9.]+)?', "SpendWise . by Hananel Sabag . build $version"
if ($stamped -eq $workerContent) {
  Write-Output "NOTE: footer stamp pattern not found in SpendWise-Worker.ps1 - version won't show in-window (exe file properties still have it)."
} else {
  Set-Content -Path $workerPs1 -Value $stamped -Encoding UTF8
  Write-Output "Stamped version into SpendWise-Worker.ps1 footer."
}

# ── Compile ──────────────────────────────────────────────────────────────
& $csc /nologo /target:winexe /platform:x64 `
  /out:"$OutExe" `
  /win32icon:"$IconFile" `
  /reference:System.Windows.Forms.dll `
  "$tmpSrc"

if ($LASTEXITCODE -ne 0) { Write-Error "Compile failed (exit $LASTEXITCODE)"; exit 1 }
Remove-Item $tmpSrc -Force -ErrorAction SilentlyContinue

Write-Output ""
Write-Output "Built: $OutExe"
Write-Output "Version: $version"
Write-Output "Size: $((Get-Item $OutExe).Length) bytes"
