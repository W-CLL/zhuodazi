@echo off
call "%~dp0start-payments.bat" --profile production
exit /b %ERRORLEVEL%
