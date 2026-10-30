@echo off
setlocal
chcp 65001 >nul
title ZhuoDazi Payment Monitor
cd /d "%~dp0"
if not exist "runtime\python\python.exe" (
  echo Bundled Python runtime is missing. Rebuild the standalone folder.
  pause
  exit /b 2
)
"runtime\python\python.exe" -I "scripts\payment_console.py" %*
set "PAYMENT_EXIT=%ERRORLEVEL%"
echo.
echo Payment monitor stopped. Exit code: %PAYMENT_EXIT%
pause
exit /b %PAYMENT_EXIT%
