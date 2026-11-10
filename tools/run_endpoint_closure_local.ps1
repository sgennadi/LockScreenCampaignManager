#requires -Version 5.1
# Explicit local-only SYSTEM acceptance audit for an approved disposable test PC.
# Baseline must precede new GPO deploy; VerifyCleanup must follow GPO unlink.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Baseline','VerifyCleanup')][string]$Mode,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z0-9-]{3,15}$')][string]$ExpectedComputer,
    [ValidatePattern('^[a-z0-9][a-z0-9_-]{0,70}$')][string]$CampaignId='',
    [string]$ReceiptPath=''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

function Assert-LocalAdmin {
    $p=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Start elevated Windows PowerShell on the disposable test computer.'
    }
}
function Assert-RealFolder([string]$path) {
    $folder=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if ($folder -isnot [IO.DirectoryInfo] -or ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Linked/junction local evidence folders are not permitted.'
    }
}
function Set-PrivateAcl([string]$path) {
    $acl=New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true,$false)
    $flags=[Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
    foreach($sidText in @('S-1-5-18','S-1-5-32-544')) {
        $sid=New-Object Security.Principal.SecurityIdentifier($sidText)
        $rule=New-Object Security.AccessControl.FileSystemAccessRule(
            $sid,[Security.AccessControl.FileSystemRights]::FullControl,$flags,
            [Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
        [void]$acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $path -AclObject $acl -ErrorAction Stop
    if (-not (Get-Acl -LiteralPath $path -ErrorAction Stop).AreAccessRulesProtected) {
        throw 'Private endpoint evidence ACL was not installed.'
    }
}
Assert-LocalAdmin
if ($env:COMPUTERNAME -ine $ExpectedComputer) {
    throw 'Wrong local computer: explicit expected name is required.'
}
$os=Get-WmiObject Win32_OperatingSystem -ErrorAction Stop
$pc=Get-WmiObject Win32_ComputerSystem -ErrorAction Stop
if ([int]$os.ProductType -ne 1 -or [int]$os.BuildNumber -lt 10240 -or -not [bool]$pc.PartOfDomain) {
    throw 'Only a domain-joined Windows 10/11 disposable test PC is supported.'
}
$gpoId=''
$deployedUtc=''
$domain=[string]$pc.Domain
if ($Mode -ceq 'Baseline') {
    if ([string]::IsNullOrWhiteSpace($CampaignId) -or -not [string]::IsNullOrEmpty($ReceiptPath)) {
        throw 'Baseline requires CampaignId before deployment, without a receipt.'
    }
} else {
    if (-not [string]::IsNullOrEmpty($CampaignId) -or [string]::IsNullOrWhiteSpace($ReceiptPath) -or
        $ReceiptPath -cnotmatch '^[A-Za-z]:\\' -or
        $ReceiptPath.StartsWith('\') -or -not (Test-Path -LiteralPath $ReceiptPath -PathType Leaf)) {
        throw 'VerifyCleanup requires one existing LOCAL deployment receipt.'
    }
    $receipt=Get-Content -LiteralPath $ReceiptPath -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
    $CampaignId=[string]$receipt.campaignId
    $gpoId=[string]$receipt.gpoId
    $deployedUtc=[string]$receipt.deployedUtc
    [Guid]$verifiedGpo=[Guid]::Empty
    [DateTimeOffset]$verifiedDate=[DateTimeOffset]::MinValue
    if ($CampaignId -cnotmatch '^[a-z0-9][a-z0-9_-]{0,70}$' -or
        -not [Guid]::TryParseExact($gpoId,'D',[ref]$verifiedGpo) -or
        -not [DateTimeOffset]::TryParseExact($deployedUtc,'o',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind,[ref]$verifiedDate) -or
        [string]$receipt.domain -ine $domain) {
        throw 'Receipt campaign, GPO, deployment UTC or domain does not match.'
    }
}
$probe=Join-Path $PSScriptRoot 'endpoint_closure_probe.ps1'
$rsop=Join-Path $PSScriptRoot 'smb_canary_rsop.ps1'
foreach($source in @($probe,$rsop)) {
    $item=Get-Item -LiteralPath $source -Force -ErrorAction Stop
    if ($item -isnot [IO.FileInfo] -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Missing or linked local verification script.'
    }
}
Write-Host 'LOCAL SYSTEM audit: no AD, GPO, SYSVOL, SMB or registry setting changes.'
Write-Host 'Only a temporary local SYSTEM Scheduled Task and private local evidence files are written.'
Write-Host 'Baseline must be before GPO creation; VerifyCleanup after unlink, cleanup and policy refresh.'
$approval=Read-Host ('Type I_APPROVE_ENDPOINT_'+$Mode.ToUpperInvariant()+' to proceed')
if ($approval -cne ('I_APPROVE_ENDPOINT_'+$Mode.ToUpperInvariant())) {
    throw 'Endpoint audit approval not given.'
}
$root=Join-Path $env:ProgramData 'LSCM-Endpoint-Evidence'
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    New-Item -ItemType Directory -Path $root -ErrorAction Stop | Out-Null
}
Assert-RealFolder $root
Set-PrivateAcl $root
$campaignFolder=Join-Path $root $CampaignId
if (-not (Test-Path -LiteralPath $campaignFolder -PathType Container)) {
    New-Item -ItemType Directory -Path $campaignFolder -ErrorAction Stop | Out-Null
}
Assert-RealFolder $campaignFolder
Set-PrivateAcl $campaignFolder
$baseline=Join-Path $campaignFolder 'baseline.json'
if ($Mode -eq 'Baseline' -and (Test-Path -LiteralPath $baseline)) {
    throw 'Baseline already exists; NEVER overwrite predeployment evidence.'
}
if ($Mode -eq 'VerifyCleanup' -and -not (Test-Path -LiteralPath $baseline -PathType Leaf)) {
    throw 'No predeployment baseline exists. Cannot independently attest cleanup.'
}
$runId=[Guid]::NewGuid().ToString('N')
$work=Join-Path $campaignFolder $runId
New-Item -ItemType Directory -Path $work -ErrorAction Stop | Out-Null
Assert-RealFolder $work
Set-PrivateAcl $work
$localProbe=Join-Path $work 'probe.ps1'
$localParser=Join-Path $work 'smb_canary_rsop.ps1'
$configPath=Join-Path $work 'config.json'
$reportPath=Join-Path $work 'report.json'
$taskName='LSCM-Endpoint-Closure-'+$runId
$registered=$false
try {
    Copy-Item -LiteralPath $probe -Destination $localProbe -ErrorAction Stop
    Copy-Item -LiteralPath $rsop -Destination $localParser -ErrorAction Stop
    $cfg=[ordered]@{
        schema='lscm-closure-config/v1'
        mode=$Mode
        campaignId=$CampaignId
        expectedComputer=$ExpectedComputer
        expectedDomain=$domain
        expectedGpoId=$gpoId
        deployedUtc=$deployedUtc
    }
    [IO.File]::WriteAllText($configPath,($cfg | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
    $powershell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $argsString='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+
        $localProbe+'" -ConfigPath "'+$configPath+'" -BaselinePath "'+
        $baseline+'" -ReportPath "'+$reportPath+'"'
    $action=New-ScheduledTaskAction -Execute $powershell -Argument $argsString
    $principal=New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 3)
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -ErrorAction Stop | Out-Null
    $registered=$true
    Start-ScheduledTask -TaskName $taskName -ErrorAction Stop
    $deadline=[datetime]::UtcNow.AddSeconds(120)
    while (-not (Test-Path -LiteralPath $reportPath -PathType Leaf) -and [datetime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 400
    }
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw 'SYSTEM audit did not produce a report. Check local task history.'
    }
    $result=Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
    Write-Host ('Audit result: '+[string]$result.result)
    Write-Host ('Registry same as predeployment: '+[bool]$result.registryValuesMatchBaseline)
    Write-Host ('Campaign folder and agent tasks gone: '+([bool]$result.campaignFolderAbsent -and
        [bool]$result.mainTaskAbsent -and [bool]$result.feedbackTaskAbsent))
    Write-Host ('Exact Computer GPO no longer applied: '+[bool]$result.expectedGpoNotApplied)
    Write-Host ('Private JSON evidence: '+$reportPath)
    if ([string]$result.result -cnotin @('PREDEPLOYMENT_BASELINE_CAPTURED',
        'POSTCLEANUP_LOCAL_STATE_MATCHES_BASELINE')) {
        throw 'Independent endpoint evidence incomplete; review private findings.'
    }
}
finally {
    if ($registered) {
        try { Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue } catch {}
        try { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction Stop }
        catch { Write-Warning 'Cannot remove LOCAL temporary audit task; inspect Task Scheduler.' }
    }
    foreach($tmp in @($configPath,$localProbe,$localParser)) {
        if (Test-Path -LiteralPath $tmp -PathType Leaf) {
            Remove-Item -LiteralPath $tmp -Force -ErrorAction Stop
        }
    }
}
