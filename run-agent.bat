@echo off
rem spendwise-agent — scheduled run (no pause, for Task Scheduler)
cd /d "%~dp0"
node src\agent.js
