#requires -Version 5.1
# Manually approved, local-only lab harness. Uses a TEMPORARY LOCAL task
# to run a read-only SYSVOL access probe as this PC's machine identity.
# No remote task, gpupdate, GPO editing, SYSVOL write or executable launch.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Domain,
    [Parameter(Mandatory=$true)][string]$Dc,
    [Parameter(Mandatory=$true)][string]$ComputerFqdn,
    [Parameter(Mandatory=$true)][string]$GpoId
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$who=[Security.Principal.WindowsPrincipal]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $who.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run elevated on the specifically approved Windows 10/11 pilot PC.'
}
if ($Domain -cnotmatch '^(?=.{4,253}$)[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$' -or
    $Dc -cnotmatch '^(?=.{4,253}$)[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$' -or
    -not $Dc.EndsWith('.'+$Domain,[StringComparison]::OrdinalIgnoreCase) -or
    $ComputerFqdn -ine ($env:COMPUTERNAME+'.'+$Domain)) {
    throw 'The selected DC/domain/PC must match the local domain PC.'
}
[Guid]$gpo=[Guid]::Empty
if (-not [Guid]::TryParseExact($GpoId,'D',[ref]$gpo)) {
    throw 'Specify one exact linked GPO GUID (D format).'
}
$os=Get-WmiObject Win32_OperatingSystem -ErrorAction Stop
$pc=Get-WmiObject Win32_ComputerSystem -ErrorAction Stop
if ([int]$os.ProductType -ne 1 -or [int]$os.BuildNumber -lt 10240 -or
    -not [bool]$pc.PartOfDomain -or [string]$pc.Domain -ine $Domain) {
    throw 'The host must be an approved domain-joined Windows 10/11 pilot PC.'
}
$probe=Join-Path $PSScriptRoot 'sysvol_system_probe.ps1'
if (-not (Test-Path -LiteralPath $probe -PathType Leaf)) {
    throw 'Missing read-only SYSTEM SYSVOL probe.'
}
Write-Host 'LOCAL SYSTEM SYSVOL READ CHECK (lab only).'
Write-Host 'A temporary LOCAL SYSTEM scheduled task reads GPT.INI and Registry.pol.'
Write-Host 'No GPO, SYSVOL, registry or client settings are modified.'
Write-Host 'A private, unsigned JSON report is retained locally. Nothing is uploaded.'
$approval=Read-Host 'Type I_APPROVE_SYSVOL_READ to continue'
if ($approval -cne 'I_APPROVE_SYSVOL_READ') {
    throw 'Explicit local approval not given.'
}
function Protect-Directory([string]$dir) {
    $item=Get-Item -LiteralPath $dir -Force -ErrorAction Stop
    if ($item -isnot [IO.DirectoryInfo] -or
        ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Invalid linked local report directory.'
    }
    $acl=[Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true,$false)
    $inherit=[Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
        [Security.AccessControl.InheritanceFlags]::ObjectInherit
    foreach($sid in @('S-1-5-18','S-1-5-32-544')) {
        $id=[Security.Principal.SecurityIdentifier]::new($sid)
        $rule=[Security.AccessControl.FileSystemAccessRule]::new(
            $id,[Security.AccessControl.FileSystemRights]::FullControl,
            $inherit,[Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow)
        [void]$acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $dir -AclObject $acl -ErrorAction Stop
    if (-not (Get-Acl -LiteralPath $dir).AreAccessRulesProtected) {
        throw 'Could not protect private local evidence.'
    }
}
$base=Join-Path $env:ProgramData 'LSCM-SYSVOL-Canary'
if (-not (Test-Path -LiteralPath $base)) {
    [void](New-Item -ItemType Directory -Path $base -ErrorAction Stop)
}
Protect-Directory $base
$runId=[Guid]::NewGuid().ToString('N')
$work=Join-Path $base $runId
[void](New-Item -ItemType Directory -Path $work -ErrorAction Stop)
Protect-Directory $work
$localScript=Join-Path $work 'probe.ps1'
$configPath=Join-Path $work 'config.json'
$reportPath=Join-Path $work 'report.json'
Copy-Item -LiteralPath $probe -Destination $localScript -ErrorAction Stop
$config=[ordered]@{
    schema='lscm-sysvol-system-config/v1'
    domain=$Domain.ToLowerInvariant()
    dc=$Dc.ToLowerInvariant()
    computer=$ComputerFqdn.ToLowerInvariant()
    gpoId=$gpo.ToString('D')
}
[IO.File]::WriteAllText($configPath,
    ($config | ConvertTo-Json -Compress),
    [Text.UTF8Encoding]::new($false))
$task='LSCM-SYSVOL-Canary-'+$runId
$registered=$false
try {
    $ps=Join-Path $env:SystemRoot 'System32\\WindowsPowerShell\\v1.0\\powershell.exe'
    $arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+
        $localScript+'" -ConfigPath "'+$configPath+'" -ReportPath "'+$reportPath+'"'
    $action=New-ScheduledTaskAction -Execute $ps -Argument $arguments
    $principal=New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 2)
    Register-ScheduledTask -TaskName $task -Action $action -Principal $principal `
        -Settings $settings -ErrorAction Stop | Out-Null
    $registered=$true
    Start-ScheduledTask -TaskName $task -ErrorAction Stop
    $deadline=[datetime]::UtcNow.AddSeconds(90)
    while (-not (Test-Path -LiteralPath $reportPath -PathType Leaf) -and
        [datetime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 400
    }
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw 'No SYSTEM report. Inspect LOCAL Task Scheduler history and access.'
    }
    $report=Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 |
        ConvertFrom-Json -ErrorAction Stop
    Write-Host ('GPT.INI as SYSTEM: '+[string]$report.gptIni)
    Write-Host ('User Registry.pol as SYSTEM: '+[string]$report.userRegistryPol)
    Write-Host ('Machine Registry.pol as SYSTEM: '+[string]$report.machineRegistryPol)
    Write-Host ('Private JSON (import manually in Pilot Readiness): '+$reportPath)
} finally {
    if ($registered) {
        try { Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue } catch {}
        try { Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction Stop }
        catch { Write-Warning 'Temporary local task cleanup needs manual inspection.' }
    }
    foreach($file in @($configPath,$localScript)) {
        if (Test-Path -LiteralPath $file) {
            try { Remove-Item -LiteralPath $file -Force -ErrorAction Stop }
            catch { Write-Warning 'A temporary private lab file needs manual cleanup.' }
        }
    }
}
