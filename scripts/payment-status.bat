@echo off
setlocal
chcp 65001 >nul
title ZhuoDazi Payment Status - Read Only
cd /d "%~dp0"
if not exist "runtime\python\python.exe" (
  echo Bundled Python runtime is missing. Rebuild the standalone folder.
  pause
  exit /b 2
)
"runtime\python\python.exe" -I "scripts\payment_console.py" --profile production --status
set "STATUS_EXIT=%ERRORLEVEL%"
echo.
echo Read-only viewer closed. The background monitor was not stopped.
pause
exit /b %STATUS_EXIT%
