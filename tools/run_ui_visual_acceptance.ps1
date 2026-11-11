#requires -Version 5.1
# Local, operator-assisted Windows 10/11 screen-layout acceptance.
# No AD/GPO/SMB access, registry writes, screenshots or automatic reporting.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidatePattern('^[A-Za-z0-9-]{3,15}$')]
    [string]$ExpectedComputer,

    [Parameter(Mandatory=$true)]
    [string]$ManagerExePath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Use Windows PowerShell 5.1, not PowerShell 7 or ISE.'
}
if ($env:COMPUTERNAME -ine $ExpectedComputer) {
    throw 'This is not the explicitly approved local test computer.'
}
if (-not [Environment]::UserInteractive -or
    (Get-Process -Id $PID).SessionId -le 0 -or
    $env:SESSIONNAME -cne 'Console') {
    throw 'Run in the interactive local Windows console desktop, not as a service, CI job, or RDP session.'
}
$os = Get-WmiObject Win32_OperatingSystem -ErrorAction Stop
if ([int]$os.ProductType -ne 1 -or [int]$os.BuildNumber -lt 10240) {
    throw 'This pilot requires a Windows 10/11 client, not Windows Server.'
}

$exe = [IO.Path]::GetFullPath($ManagerExePath)
if ($exe.StartsWith('\\', [StringComparison]::Ordinal) -or
    [IO.Path]::GetExtension($exe) -ine '.exe' -or
    -not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw 'Select a locally copied Manager EXE; UNC executable paths are not supported.'
}
$item = Get-Item -LiteralPath $exe -Force -ErrorAction Stop
if ($item.Name -ine 'LockScreenCampaignManager.exe' -or
    ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
    $item.Length -lt 100KB -or $item.Length -gt 600MB) {
    throw 'Incorrect, linked or unexpectedly sized Manager executable.'
}
$interop = Join-Path $PSScriptRoot 'LscmDisplayMonitorInterop.cs'
if (-not (Test-Path -LiteralPath $interop -PathType Leaf) -or
    (Get-Item -LiteralPath $interop).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'Missing or linked local display interop source.'
}

Write-Host 'Local UI visual acceptance. No domain, GPO, registry or network operations.'
Write-Host 'The only executable command is Manager --ui-smoke-test, after your approval.'
Write-Host 'Review the local EXE against the expected published release before continuing.'
$approval = Read-Host 'Type I_APPROVE_UI_VISUAL_TEST to inspect the display and run offline smoke checks'
if ($approval -cne 'I_APPROVE_UI_VISUAL_TEST') { throw 'Operator approval not granted.' }

# This API reads effective Windows scale; it does not depend on the DPI mode of
# powershell.exe, and it does not change the current display settings.
Add-Type -Path $interop -ErrorAction Stop
$report = [ordered]@{
    schema = 'lscm-local-ui-visual/v1'
    observedUtc = [datetime]::UtcNow.ToString('o')
    osBuild = [int]$os.BuildNumber
    localConsole = $true
    source = 'unsigned-local-operator-observation'
    monitorScalesPercent = @()
    display200Present = $false
    uiSmokePassed = $false
    operatorChecks = @()
    screenshotsCaptured = $false
    remoteActionsPerformed = $false
    result = 'INCOMPLETE'
}
$dir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'LockScreenCampaignManager\UiVisualLab'
if (-not (Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Path $dir -Force -ErrorAction Stop | Out-Null
}
$folder = Get-Item -LiteralPath $dir -Force -ErrorAction Stop
if ($folder -isnot [IO.DirectoryInfo] -or
    ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Refuse linked evidence folder.'
}
$reportFile = Join-Path $dir ('ui-visual-' + [guid]::NewGuid().ToString('N') + '.json')

try {
    $monitors = [LscmDisplayMonitorInterop]::Read()
    $report.monitorScalesPercent = @($monitors | ForEach-Object { [int]$_.ScalePercent })
    $report.display200Present = @($monitors | Where-Object { $_.ScalePercent -eq 200 }).Count -gt 0
    if (-not $report.display200Present) {
        $report.result = 'NOT_READY_200_PERCENT'
        Write-Warning 'No monitor at exactly 200% Windows scaling. Adjust the disposable test desktop, then run again.'
    }
    else {
        Write-Host '200% effective Windows monitor scale detected.'
        Write-Host 'Manually open the Manager on THAT monitor to visually inspect its real layout.'
        $process = Start-Process -FilePath $exe -ArgumentList '--ui-smoke-test' -PassThru -WindowStyle Hidden -ErrorAction Stop
        if (-not $process.WaitForExit(240000)) {
            try { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } catch {}
            $report.result = 'UI_SMOKE_TIMEOUT'
        }
        else {
            $report.uiSmokePassed = ($process.ExitCode -eq 0)
            if (-not $report.uiSmokePassed) {
                $report.result = 'UI_SMOKE_FAILED'
            }
            else {
                Write-Host 'Offline GUI smoke test PASS. The following items require your REAL visual inspection.'
                $checks = @(
                    @{ id='en'; prompt='EN: Wizard, AD connection, Client Health and Updater have no clipped buttons or letters?' },
                    @{ id='ru'; prompt='RU: Wizard, AD connection, Client Health and Updater have no clipped buttons or letters?' },
                    @{ id='he'; prompt='HE: mirrored RTL Wizard, Client Health, Updater and bottom rows fit without clipping?' },
                    @{ id='ltr'; prompt='Mixed LTR technical GUID/UNC/versions/timestamps are displayed correctly in Hebrew RTL?' },
                    @{ id='back'; prompt='RU to HE to EN restores expected captions and selections after reopening dialogs?' },
                    @{ id='physical'; prompt='Did you open and inspect the actual Manager on the Windows monitor running at 200%?' }
                )
                $all = $true
                foreach ($check in $checks) {
                    $response = Read-Host ($check.prompt + ' (YES/NO)')
                    $ok = ($response -ceq 'YES')
                    $report.operatorChecks += [ordered]@{ id=$check.id; confirmed=$ok }
                    if (-not $ok) { $all = $false }
                }
                $report.result = if ($all) {
                    'LOCAL_OPERATOR_OBSERVATIONS_RECORDED_NOT_ATTESTED'
                } else {
                    'OPERATOR_REVIEW_INCOMPLETE'
                }
            }
        }
    }
}
catch {
    $report.result = 'FAILED_CLOSED'
    # Do not persist exception text: local user paths may be sensitive.
    Write-Warning 'The local test failed. Inspect console output privately; nothing was uploaded.'
}
finally {
    $temp = $reportFile + '.tmp'
    try {
        [IO.File]::WriteAllText($temp, ($report | ConvertTo-Json -Depth 8),
            (New-Object Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $temp -Destination $reportFile -ErrorAction Stop
        Write-Host ('Local report: ' + $reportFile)
        Write-Host ('Result: ' + $report.result)
        Write-Host 'Operator observations are unsigned. They are not proof of physical screen behavior or AD policy state.'
    }
    finally { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
}
if ($report.result -cne 'LOCAL_OPERATOR_OBSERVATIONS_RECORDED_NOT_ATTESTED') { exit 1 }
exit 0
