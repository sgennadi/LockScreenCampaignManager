# Management EXE: supported Windows operating systems

This document describes the **graphical LockScreen Campaign Manager EXE**, not the startup agent installed on target PCs.

## Intended manager platforms

- Windows 11 x64/x86 (interactive desktop)
- Windows 10 Enterprise/LTSC x64/x86, on an appropriately serviced build
- Windows Server **2016, 2019, 2022 and 2025 with Desktop Experience**, x64

The application is a self-contained .NET 10 WinForms executable (`net10.0-windows`) and does not need a separately installed .NET Desktop Runtime. Microsoft's .NET 10 Windows support matrix includes Windows Server 2016/2019/2022/2025 x64, and includes LTSC/Enterprise Windows 10. Use the **win-x64** EXE package on Windows Server.

Do **not** use the WinForms GUI on Windows Server Core or Nano Server: those installations lack the interactive desktop UI that the manager requires. Headless agent/script execution is a separate workflow.

The manager does not need to run on a domain controller. Local campaign editing is available without AD access or administrator privileges. Directory inventory needs network access to AD DNS/LDAP; deployment requires separately verified delegated GPO/SYSVOL/OU permissions. Running on an administrative workstation or a desktop-enabled server is supported by design, but deployment still requires appropriate rights.

## Validation boundary

GitHub-hosted WinForms, compiler and PowerShell tests run on Windows runners and validate GUI layout contracts, x86/x64 artifacts and offline LDAP/GPO fixtures. **They do not execute the app on each Windows Server version.**

Before using on a production Windows Server, pilot the executable on at least one desktop-enabled server in your environment, verify AD bind/DC discovery, creation of a disposable test GPO and image copy to SYSVOL, and use the built-in Diagnostics if needed. Avoid deploying directly to production OU during the pilot.

The Windows 10/11 **client agent** edition requirements are described separately in [Windows 10 client compatibility](WINDOWS10-CLIENT-COMPATIBILITY.md).

Official reference: https://learn.microsoft.com/en-us/dotnet/core/install/windows
