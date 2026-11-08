@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
set PROJECT=src\LockScreenCampaignManager\LockScreenCampaignManager.csproj
where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET 10 SDK is required to BUILD. The final EXE does not require it.
    exit /b 1
)
for %%R in (win-x64 win-x86) do (
    echo.
    echo ===== BUILD %%R =====
    dotnet publish "%PROJECT%" -c Release -r %%R --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o "dist\%%R"
    if errorlevel 1 exit /b 1
    if not exist "dist\%%R\LockScreenCampaignManager.exe" (
        echo ERROR: EXE was not generated.
        exit /b 2
    )
    powershell.exe -NoProfile -Command "Compress-Archive -Path 'dist\%%R\LockScreenCampaignManager.exe' -DestinationPath 'dist\LockScreenCampaignManager-0.3.0-preview.1-%%R.zip' -Force"
    if errorlevel 1 exit /b 3
)
echo.
echo OK: Portable single-file EXEs available in dist\win-x64 and dist\win-x86
echo No separate .NET runtime installation is required on the target PC.
exit /b 0
