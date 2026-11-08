@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-to-github.ps1"
set "CODE=%ERRORLEVEL%"
if not "%CODE%"=="0" echo GitHub publish failed with exit code %CODE%.
pause
exit /b %CODE%
