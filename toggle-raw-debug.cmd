@echo off
rem ---------------------------------------------------------------------------
rem SpendWise RAW debug toggle.
rem Flips scraped-data\RAW_DEBUG. When ON, the NEXT worker sync also drops the
rem raw scraper output (every field, unmapped) to scraped-data\raw-*.html/json.
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
echo.
echo    SpendWise RAW debug is now:  %STATE%
echo.
echo    ON  = the next worker "sync now" writes scraped-data\raw-*.html + .json
echo    OFF = normal sync, nothing extra written
echo.
echo    Run this again to switch. You can close this window.
echo.
pause
