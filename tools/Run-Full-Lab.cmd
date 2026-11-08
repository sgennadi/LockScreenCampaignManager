@echo off
setlocal
cd /d "%~dp0"
echo WARNING: REAL test - creates GPO, copies to SYSVOL, reboots TEST PCs.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\run_lab_local.ps1" -Mode Full
set "result=%ERRORLEVEL%"
echo.
echo Result code: %result%. Review the reports folder for live-ad-lab.json.
pause
exit /b %result%
