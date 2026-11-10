#requires -Version 5.1
# Explicit local-only lab harness. Stages an immutable read-only probe under SYSTEM.
# No remote Task Scheduler, no AD/SYSVOL changes, no script execution from SMB.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ReceiptPath,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z0-9-]{3,15}$')]
    [string]$ExpectedComputer
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-LocalAdmin {
    $principal=New-Object Security.Principal.WindowsPrincipal(
        [Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Start an elevated PowerShell window on the disposable test PC.'
    }
}
function Set-PrivateAcl([string]$folder) {
    $acl=New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true,$false)
    $flags=[Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
        [Security.AccessControl.InheritanceFlags]::ObjectInherit
    foreach($sidString in @('S-1-5-18','S-1-5-32-544')) {
        $sid=New-Object Security.Principal.SecurityIdentifier($sidString)
        $rule=New-Object Security.AccessControl.FileSystemAccessRule(
            $sid,[Security.AccessControl.FileSystemRights]::FullControl,
            $flags,[Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow)
        [void]$acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $folder -AclObject $acl -ErrorAction Stop
    if (-not (Get-Acl -LiteralPath $folder).AreAccessRulesProtected) {
        throw 'Cannot secure local private canary evidence directory.'
    }
}
function Assert-RegularDirectory([string]$path) {
    $entry=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if ($entry -isnot [IO.DirectoryInfo] -or
        ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Cannot use a linked/junction local report directory.'
    }
}
Assert-LocalAdmin
if ($env:COMPUTERNAME -ine $ExpectedComputer) {
    throw 'This is not the explicitly named local lab computer.'
}
$os=Get-WmiObject -Class Win32_OperatingSystem -ErrorAction Stop
$computer=Get-WmiObject -Class Win32_ComputerSystem -ErrorAction Stop
if ([int]$os.ProductType -ne 1 -or [int]$os.BuildNumber -lt 10240 -or
    -not [bool]$computer.PartOfDomain) {
    throw 'Only a domain-joined Windows 10/11 lab client is supported.'
}
$receipt=(Get-Content -LiteralPath $ReceiptPath -Raw -Encoding UTF8 |
    ConvertFrom-Json -ErrorAction Stop)
$id=[string]$receipt.campaignId
$root=[string]$receipt.contentShareRoot
$target=[string]$receipt.packageStoragePath
$domain=[string]$receipt.domain
$manifest=[string]$receipt.manifestSha256
$agent=[string]$receipt.agentSha256
$method=[string]$receipt.clientType
if ([string]$receipt.contentSource -cne 'SmbShare' -or
    $id -cnotmatch '^[a-z0-9-]{5,75}$' -or
    $manifest -notmatch '^[a-fA-F0-9]{64}$' -or
    $agent -notmatch '^[a-fA-F0-9]{64}$' -or
    $domain -cnotmatch '^[A-Za-z0-9][A-Za-z0-9.-]{2,252}$' -or
    $method -cnotin @('PowerShell','Executable') -or
    $root -notmatch '^\\\\[a-zA-Z0-9][a-zA-Z0-9.-]+\\[A-Za-z0-9_]' -or
    $root.Length -gt 200 -or $root.Contains('..') -or
    $root.Contains('"') -or $root.Contains('$') -or
    $root.Contains('/') -or $root.Contains(':') -or
    $target -cne ($root.TrimEnd('\\')+'\\'+$id)) {
    throw 'Invalid pinned SMB deployment receipt. Refusing to start.'
}
if ([string]$computer.Domain -ine $domain) {
    throw 'The computer is joined to another domain than the approved receipt.'
}
$source=Join-Path $PSScriptRoot 'smb_system_canary_probe.ps1'
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw 'Missing sibling read-only SYSTEM probe.'
}
Write-Host 'LOCAL LAB SMB SOURCE CHECK. No AD/GPO, SYSVOL or registry settings will be modified.'
Write-Host 'The harness temporarily creates a LOCAL Scheduled Task as SYSTEM.'
Write-Host 'The probe reads the selected SMB package and optional local cache, then'
Write-Host 'writes an admin-only local JSON report. Nothing is uploaded.'
$approval=Read-Host 'Type I_APPROVE_SMB_CANARY to continue'
if ($approval -cne 'I_APPROVE_SMB_CANARY') { throw 'Lab approval not given.' }

$base=Join-Path $env:ProgramData 'LSCM-SMB-Canary'
if (-not (Test-Path -LiteralPath $base)) {
    New-Item -ItemType Directory -Path $base -ErrorAction Stop | Out-Null
}
Assert-RegularDirectory $base
Set-PrivateAcl $base
$runId=[Guid]::NewGuid().ToString('N')
$work=Join-Path $base $runId
New-Item -ItemType Directory -Path $work -ErrorAction Stop | Out-Null
Set-PrivateAcl $work
$localProbe=Join-Path $work 'probe.ps1'
$configFile=Join-Path $work 'config.json'
$report=Join-Path $work 'report.json'
Copy-Item -LiteralPath $source -Destination $localProbe -ErrorAction Stop
$config=[ordered]@{
    schema='lscm-smb-canary-config/v1'
    contentShareRoot=$root
    campaignId=$id
    expectedComputer=$ExpectedComputer
    expectedDomain=$domain
    manifestSha256=$manifest.ToLowerInvariant()
    agentSha256=$agent.ToLowerInvariant()
    clientType=$method
}
[IO.File]::WriteAllText($configFile,($config | ConvertTo-Json -Depth 5),
    [Text.UTF8Encoding]::new($false))

$taskName='LSCM-SMB-Canary-'+$runId
$registered=$false
try {
    $powershell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $parameters='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+
        $localProbe+'" -ConfigPath "'+$configFile+'" -ReportPath "'+$report+'"'
    $action=New-ScheduledTaskAction -Execute $powershell -Argument $parameters
    $principal=New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 3)
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal `
        -Settings $settings -ErrorAction Stop | Out-Null
    $registered=$true
    Start-ScheduledTask -TaskName $taskName -ErrorAction Stop
    $deadline=[datetime]::UtcNow.AddSeconds(120)
    while (-not (Test-Path -LiteralPath $report -PathType Leaf) -and
        [datetime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 400
    }
    if (-not (Test-Path -LiteralPath $report -PathType Leaf)) {
        throw 'SYSTEM lab probe produced no report. Inspect local Task Scheduler history.'
    }
    $result=Get-Content -LiteralPath $report -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host ('Result: '+[string]$result.result)
    Write-Host ('Source verified as SYSTEM: '+[bool]$result.smbSourceReadable+
        ' / manifest: '+[bool]$result.manifestSha256Verified+
        ' / agent: '+[bool]$result.agentSha256Verified+
        ' / photos: '+[bool]$result.allImagesVerified)
    Write-Host ('Local cache verified: '+[bool]$result.localCacheHashVerified)
    Write-Host ('Private JSON report: '+$report)
    if ([string]$result.result -cne 'SOURCE_VERIFIED_AS_LOCAL_SYSTEM_ONLY') {
        throw 'SMB canary check failed closed; inspect the local JSON findings.'
    }
}
finally {
    if ($registered) {
        try { Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue } catch {}
        try { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction Stop }
        catch { Write-Warning 'Cannot remove temporary LOCAL canary task; inspect Task Scheduler.' }
    }
    # Keep only the sanitized report. Do not preserve the private receipt-derived
    # UNC/domain inputs or PS1 action executable in the evidence directory.
    if (Test-Path -LiteralPath $configFile) {
        Remove-Item -LiteralPath $configFile -Force -ErrorAction Stop
    }
    if (Test-Path -LiteralPath $localProbe) {
        Remove-Item -LiteralPath $localProbe -Force -ErrorAction Stop
    }
}
