@echo off
rem ---------------------------------------------------------------------------
rem SpendWise RAW debug toggle.
rem Flips scraped-data\RAW_DEBUG. When ON, the NEXT worker sync also (a) bypasses
rem the agent cooldown and (b) drops the raw scraper output (every field,
rem unmapped) to scraped-data\raw-*.html/json. A visible marker file appears on
rem the Desktop while ON, so you always know the state.
rem Toggle ON -> run a sync from the worker -> read the report -> toggle OFF.
rem ---------------------------------------------------------------------------
setlocal
set "DATADIR=%~dp0scraped-data"
set "FLAG=%DATADIR%\RAW_DEBUG"
if not exist "%DATADIR%" mkdir "%DATADIR%"
if exist "%FLAG%" (
  del "%FLAG%" >nul 2>&1
  set "STATE=OFF"
) else (
  break > "%FLAG%"
  set "STATE=ON"
)
rem Desktop indicator: a clearly-named file present only while debug is ON.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$d=[Environment]::GetFolderPath('Desktop'); $m=Join-Path $d 'SpendWise RAW DEBUG is ON.txt'; if('%STATE%' -eq 'ON'){ Set-Content -LiteralPath $m -Value 'RAW debug is ON. The next worker sync bypasses cooldown and writes scraped-data\raw-*.html + .json. Run the toggle again to turn OFF.' -Encoding UTF8 } else { Remove-Item -LiteralPath $m -Force -ErrorAction SilentlyContinue }" 2>nul
echo.
echo    SpendWise RAW debug is now:  %STATE%
echo.
if /I "%STATE%"=="ON" (
  echo    A "SpendWise RAW DEBUG is ON" file is now on your Desktop.
  echo    The next worker "sync now" bypasses cooldown and writes
  echo    scraped-data\raw-*.html + .json  ^(every raw field^).
) else (
  echo    Back to normal sync. Nothing extra written. Desktop marker removed.
)
echo.
echo    Run this again to switch. You can close this window.
echo.
pause
