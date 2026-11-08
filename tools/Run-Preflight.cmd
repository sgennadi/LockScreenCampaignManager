@echo off
setlocal
cd /d "%~dp0"
echo LockScreen Campaign Manager - isolated domain preflight
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\run_lab_local.ps1" -Mode Preflight
set "result=%ERRORLEVEL%"
echo.
echo Result code: %result%. Review the reports folder for live-ad-lab.json.
pause
exit /b %result%
