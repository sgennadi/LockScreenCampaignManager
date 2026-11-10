#requires -Version 5.1
# Read-only client audit: predeployment baseline vs postcleanup state.
# The only writes are to a private local baseline, temp RSoP XML and report.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ConfigPath,
    [Parameter(Mandatory=$true)][string]$BaselinePath,
    [Parameter(Mandatory=$true)][string]$ReportPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$evidence=[ordered]@{
    schema='lscm-endpoint-closure/v1'
    observedUtc=[datetime]::UtcNow.ToString('o')
    osBuild=0
    localSystem=$false
    authorizedTestComputer=$false
    baselineBeforeDeployment=$false
    registryValuesMatchBaseline=$false
    registryKeyPresenceMatchesBaseline=$false
    campaignFolderAbsent=$false
    mainTaskAbsent=$false
    feedbackTaskAbsent=$false
    ownerRecordAbsent=$false
    cleanupReceiptVerified=$false
    computerGpresultAvailable=$false
    expectedGpoNotApplied=$false
    gpoRsopStatus='NotCollected'
    lockScreenDisplayVisuallyVerified=$false
    result='FAIL_CLOSED'
    findings=@()
}
$findings=New-Object 'System.Collections.Generic.List[string]'
function Finding([string]$code) { [void]$script:findings.Add($code) }
function Fail([string]$code) { throw $code }
function Assert-LocalEvidencePath([string]$candidate) {
    $root=[IO.Path]::GetFullPath((Join-Path $env:ProgramData 'LSCM-Endpoint-Evidence'))
    $candidate=[IO.Path]::GetFullPath($candidate)
    if (-not $candidate.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -or
        $candidate.StartsWith('\\',[StringComparison]::OrdinalIgnoreCase) -or
        $candidate.Contains('..')) { Fail 'INVALID_LOCAL_EVIDENCE_PATH' }
    for ($directory=[IO.DirectoryInfo]::new((Split-Path -Parent $candidate));
         $null -ne $directory; $directory=$directory.Parent) {
        if ($directory.Exists -and
            ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            Fail 'LINKED_LOCAL_EVIDENCE_PATH'
        }
    }
    return $candidate
}
function Read-PrivateJson([string]$file) {
    $info=Get-Item -LiteralPath $file -Force -ErrorAction Stop
    if ($info -isnot [IO.FileInfo] -or $info.Length -lt 10 -or
        $info.Length -gt 1MB -or
        ($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        Fail 'INVALID_PRIVATE_JSON_FILE'
    }
    return (Get-Content -LiteralPath $file -Raw -Encoding UTF8 -ErrorAction Stop |
        ConvertFrom-Json -ErrorAction Stop)
}
function Digest-Text([string]$text) {
    $sha=[Security.Cryptography.SHA256]::Create()
    try {
        $bytes=[Text.UTF8Encoding]::new($false).GetBytes($text)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','').ToLowerInvariant()
    } finally { $sha.Dispose() }
}
function Read-RegistryValue([object]$key,[string]$name) {
    if ($null -eq $key -or $key.GetValueNames() -notcontains $name) {
        return [ordered]@{ Exists=$false; Kind='String'; Value=$null }
    }
    $kind=$key.GetValueKind($name)
    $raw=$key.GetValue($name,$null,
        [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    return [ordered]@{ Exists=$true; Kind=$kind.ToString(); Value=$raw }
}
function Read-PolicyFingerprint {
    $key=Get-Item -LiteralPath 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization' -ErrorAction SilentlyContinue
    $snapshot=[ordered]@{
        KeyExists=($null -ne $key)
        LockScreenImage=(Read-RegistryValue $key 'LockScreenImage')
        NoChangingLockScreen=(Read-RegistryValue $key 'NoChangingLockScreen')
    }
    $json=$snapshot | ConvertTo-Json -Depth 6 -Compress
    return [pscustomobject]@{
        KeyExists=[bool]$snapshot.KeyExists
        LockScreenImage=$snapshot.LockScreenImage
        Fingerprint=(Digest-Text $json)
    }
}
function Read-TaskFootprint([string]$campaignId) {
    # A failed Task Scheduler query must NOT be interpreted as task absence.
    $tasks=@(Get-ScheduledTask -TaskPath '\' -ErrorAction Stop)
    $main='LockScreen-LS-'+$campaignId
    $feedback='LockScreen-LS-Feedback-'+$campaignId
    return [pscustomobject]@{
        MainPresent=(@($tasks | Where-Object { $_.TaskName -ieq $main }).Count -gt 0)
        FeedbackPresent=(@($tasks | Where-Object { $_.TaskName -ieq $feedback }).Count -gt 0)
    }
}
function No-ActiveOwner {
    $file=Join-Path $env:ProgramData 'LockScreenCampaigns\active-owner.json'
    if (-not (Test-Path -LiteralPath $file)) { return $true }
    $owner=Read-PrivateJson $file
    if ([string]$owner.CampaignId -cnotmatch '^[a-z0-9_-]{1,71}$') {
        Fail 'INVALID_ACTIVE_OWNER'
    }
    # A newer/different owner still prevents independent "restored" claims.
    return $false
}
function Read-ExactComputerRsop([Guid]$gpoGuid,[string]$privateFolder) {
    $temp=Join-Path $privateFolder ('rsop-'+[Guid]::NewGuid().ToString('N')+'.xml')
    try {
        . (Join-Path $PSScriptRoot 'smb_canary_rsop.ps1')
        $null= & "$env:SystemRoot\System32\gpresult.exe" /scope computer /x $temp /f 2>&1
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $temp -PathType Leaf)) {
            Fail 'COMPUTER_RSOP_NOT_AVAILABLE'
        }
        $info=Get-Item -LiteralPath $temp -Force
        if ($info.Length -lt 100 -or $info.Length -gt 16MB -or
            ($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            Fail 'INVALID_COMPUTER_RSOP'
        }
        $settings=New-Object System.Xml.XmlReaderSettings
        $settings.DtdProcessing=[System.Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver=$null
        $reader=[System.Xml.XmlReader]::Create($temp,$settings)
        try {
            $doc=New-Object System.Xml.XmlDocument
            $doc.XmlResolver=$null
            $doc.Load($reader)
        } finally { $reader.Dispose() }
        return Get-LscmComputerGpoEvidence -Report $doc -ExpectedGpoId $gpoGuid
    } finally {
        if (Test-Path -LiteralPath $temp) {
            Remove-Item -LiteralPath $temp -Force -ErrorAction Stop
        }
    }
}
try {
    $who=[Security.Principal.WindowsIdentity]::GetCurrent()
    $evidence.localSystem=($null -ne $who -and $who.User.Value -eq 'S-1-5-18')
    if (-not $evidence.localSystem) { Fail 'NOT_LOCAL_SYSTEM' }
    $null=Assert-LocalEvidencePath $ConfigPath
    $config=Read-PrivateJson $ConfigPath
    $mode=[string]$config.mode
    $id=[string]$config.campaignId
    if ([string]$config.schema -cne 'lscm-closure-config/v1' -or
        $mode -cnotin @('Baseline','VerifyCleanup') -or
        $id -cnotmatch '^[a-z0-9][a-z0-9_-]{0,70}$' -or
        [string]$config.expectedComputer -cnotmatch '^[A-Za-z0-9-]{3,15}$') {
        Fail 'INVALID_EVIDENCE_CONFIGURATION'
    }
    $baselineFile=Assert-LocalEvidencePath $BaselinePath
    $null=Assert-LocalEvidencePath $ReportPath
    if ($env:COMPUTERNAME -ine [string]$config.expectedComputer) {
        Fail 'WRONG_TEST_COMPUTER'
    }
    $os=Get-WmiObject Win32_OperatingSystem -ErrorAction Stop
    $computer=Get-WmiObject Win32_ComputerSystem -ErrorAction Stop
    $evidence.osBuild=[int]$os.BuildNumber
    if ([int]$os.ProductType -ne 1 -or [int]$os.BuildNumber -lt 10240 -or
        -not [bool]$computer.PartOfDomain) {
        Fail 'UNSUPPORTED_OR_NOT_DOMAIN_JOINED'
    }
    $evidence.authorizedTestComputer=$true
    $state=Read-PolicyFingerprint
    $tasks=Read-TaskFootprint $id
    $campaignDir=Join-Path (Join-Path $env:ProgramData 'LockScreenCampaigns') $id
    if (Test-Path -LiteralPath $campaignDir) {
        $existing=Get-Item -LiteralPath $campaignDir -Force
        if (($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            Fail 'LINKED_LOCAL_CAMPAIGN'
        }
    }
    $evidence.campaignFolderAbsent=-not (Test-Path -LiteralPath $campaignDir)
    $evidence.mainTaskAbsent=-not [bool]$tasks.MainPresent
    $evidence.feedbackTaskAbsent=-not [bool]$tasks.FeedbackPresent
    $evidence.ownerRecordAbsent=No-ActiveOwner

    if ($mode -ceq 'Baseline') {
        $image=$state.LockScreenImage
        if (-not $evidence.campaignFolderAbsent -or
            -not $evidence.mainTaskAbsent -or
            -not $evidence.feedbackTaskAbsent -or
            -not $evidence.ownerRecordAbsent -or
            ($image.Exists -and
             [string]$image.Value -match '(?i)\\LockScreenCampaigns\\')) {
            Fail 'BASELINE_NOT_CLEAN_PREDEPLOYMENT'
        }
        if (Test-Path -LiteralPath $baselineFile) {
            Fail 'BASELINE_ALREADY_EXISTS_NEVER_OVERWRITE'
        }
        $completedRoot=Join-Path $env:ProgramData 'LockScreenCampaigns\Completed'
        if (Test-Path -LiteralPath (Join-Path $completedRoot ($id+'.json')) -or
            Test-Path -LiteralPath (Join-Path $completedRoot ($id+'.postcleanup.json'))) {
            Fail 'PRIOR_CAMPAIGN_COMPLETION_ID_NOT_REUSABLE'
        }
        $data=[ordered]@{
            schema='lscm-endpoint-baseline/v1'
            capturedUtc=[datetime]::UtcNow.ToString('o')
            campaignId=$id
            computerName=$env:COMPUTERNAME
            domainName=[string]$computer.Domain
            osBuild=[int]$os.BuildNumber
            keyExists=$state.KeyExists
            registryFingerprint=$state.Fingerprint
        }
        $stage=$baselineFile+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
        try {
            [IO.File]::WriteAllText($stage,($data | ConvertTo-Json -Depth 5),
                [Text.UTF8Encoding]::new($false))
            [IO.File]::Move($stage,$baselineFile)
        } finally {
            if (Test-Path -LiteralPath $stage) {
                Remove-Item -LiteralPath $stage -Force
            }
        }
        $evidence.result='PREDEPLOYMENT_BASELINE_CAPTURED'
    } else {
        $baseline=Read-PrivateJson $baselineFile
        [Guid]$guid=[Guid]::Empty
        [DateTimeOffset]$deployUtc=[DateTimeOffset]::MinValue
        [DateTimeOffset]$baselineUtc=[DateTimeOffset]::MinValue
        if ([string]$baseline.schema -cne 'lscm-endpoint-baseline/v1' -or
            [string]$baseline.campaignId -cne $id -or
            [string]$baseline.computerName -ine $env:COMPUTERNAME -or
            [string]$baseline.domainName -ine [string]$computer.Domain -or
            [string]$baseline.registryFingerprint -notmatch '^[a-f0-9]{64}$' -or
            -not [DateTimeOffset]::TryParse([string]$baseline.capturedUtc,[ref]$baselineUtc) -or
            -not [DateTimeOffset]::TryParse([string]$config.deployedUtc,[ref]$deployUtc) -or
            -not [Guid]::TryParseExact([string]$config.expectedGpoId,'D',[ref]$guid) -or
            [string]$config.expectedDomain -ine [string]$computer.Domain) {
            Fail 'INVALID_BASELINE_OR_DEPLOYMENT_RECEIPT'
        }
        $evidence.baselineBeforeDeployment=($baselineUtc -lt $deployUtc -and
            $deployUtc -le [DateTimeOffset]::UtcNow.AddMinutes(5))
        if (-not $evidence.baselineBeforeDeployment) {
            Fail 'BASELINE_WAS_NOT_BEFORE_DEPLOYMENT'
        }
        $evidence.registryValuesMatchBaseline=(
            $state.Fingerprint -ceq [string]$baseline.registryFingerprint)
        $evidence.registryKeyPresenceMatchesBaseline=(
            [bool]$state.KeyExists -eq [bool]$baseline.keyExists)
        $receipt=Join-Path (Join-Path $env:ProgramData 'LockScreenCampaigns\Completed') (
            $id+'.postcleanup.json')
        if (Test-Path -LiteralPath $receipt -PathType Leaf) {
            $done=Read-PrivateJson $receipt
            $evidence.cleanupReceiptVerified=(
                [int]$done.schemaVersion -eq 1 -and
                [string]$done.campaignId -ceq $id -and
                [string]$done.cleanupStage -ceq 'AfterFileAndTaskRemoval' -and
                [bool]$done.localRootRemoved -and [bool]$done.scheduledTaskRemoved -and
                [bool]$done.feedbackTaskRemoved -and [bool]$done.ownerReleased -and
                [bool]$done.restoredOwnRegistry)
        }
        try {
            $rsop=Read-ExactComputerRsop $guid (Split-Path -Parent $ReportPath)
            $evidence.computerGpresultAvailable=($rsop.status -cne 'RsopUnavailable')
            $evidence.gpoRsopStatus=[string]$rsop.status
            $evidence.expectedGpoNotApplied=($rsop.status -ceq 'NotApplied')
        } catch { Finding 'COMPUTER_RSOP_NOT_VERIFIED' }
        if ($evidence.registryValuesMatchBaseline -and
            $evidence.registryKeyPresenceMatchesBaseline -and
            $evidence.campaignFolderAbsent -and
            $evidence.mainTaskAbsent -and $evidence.feedbackTaskAbsent -and
            $evidence.ownerRecordAbsent -and $evidence.cleanupReceiptVerified -and
            $evidence.expectedGpoNotApplied) {
            $evidence.result='POSTCLEANUP_LOCAL_STATE_MATCHES_BASELINE'
        } else {
            $evidence.result='POSTCLEANUP_REQUIRES_REVIEW'
            if (-not $evidence.registryValuesMatchBaseline) {
                Finding 'REGISTRY_VALUES_DIFFER_FROM_PREDEPLOYMENT'
            }
            if (-not $evidence.campaignFolderAbsent -or
                -not $evidence.mainTaskAbsent -or
                -not $evidence.feedbackTaskAbsent) {
                Finding 'LOCAL_CAMPAIGN_FILES_OR_TASKS_REMAIN'
            }
            if (-not $evidence.cleanupReceiptVerified) {
                Finding 'FINAL_AGENT_RECEIPT_MISSING_OR_INVALID'
            }
            if (-not $evidence.expectedGpoNotApplied) {
                Finding 'EXPECTED_COMPUTER_GPO_STILL_APPLIED_OR_UNKNOWN'
            }
        }
    }
} catch {
    $message=[string]$_.Exception.Message
    $known=@('NOT_LOCAL_SYSTEM','INVALID_EVIDENCE_CONFIGURATION',
        'WRONG_TEST_COMPUTER','UNSUPPORTED_OR_NOT_DOMAIN_JOINED',
        'LINKED_LOCAL_CAMPAIGN','BASELINE_NOT_CLEAN_PREDEPLOYMENT',
        'BASELINE_ALREADY_EXISTS_NEVER_OVERWRITE',
        'INVALID_BASELINE_OR_DEPLOYMENT_RECEIPT',
        'BASELINE_WAS_NOT_BEFORE_DEPLOYMENT',
        'INVALID_LOCAL_EVIDENCE_PATH','LINKED_LOCAL_EVIDENCE_PATH',
        'INVALID_PRIVATE_JSON_FILE','INVALID_ACTIVE_OWNER',
        'PRIOR_CAMPAIGN_COMPLETION_ID_NOT_REUSABLE')
    if ($message -in $known) { Finding $message }
    else { Finding 'EVIDENCE_COLLECTION_INCOMPLETE' }
    $evidence.result='FAIL_CLOSED'
} finally {
    $evidence.findings=@($findings.ToArray())
    $null=Assert-LocalEvidencePath $ReportPath
    $partial=$ReportPath+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
    [IO.File]::WriteAllText($partial,($evidence | ConvertTo-Json -Depth 6),
        [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($partial,$ReportPath)
}
if ($evidence.result -notin @('PREDEPLOYMENT_BASELINE_CAPTURED',
    'POSTCLEANUP_LOCAL_STATE_MATCHES_BASELINE')) { exit 2 }
