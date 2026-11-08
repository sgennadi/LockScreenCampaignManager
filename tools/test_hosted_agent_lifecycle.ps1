#requires -Version 5.1
<#
.SYNOPSIS
    Hosted Windows integration tests using the real client agent functions.
.DESCRIPTION
    Execute real image selection, SHA-256 recovery, ownership, HKCU registry
    backup/restore and campaign cleanup on an isolated disposable test hive.
    NOT a real GPO/DC, Windows 11, HKLM or SYSTEM Task Scheduler test.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$agent=Join-Path $repo 'src\LockScreenCampaignManager\Assets\LockScreen-Agent.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($agent,[ref]$tokens,[ref]$errors)
if (@($errors).Count -ne 0) { throw 'Cannot parse production agent.' }
$definitions=@($ast.FindAll({
    param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]
},$true))
$required=@(
    'Get-FileHashOrNull','Test-FileHash','Write-Log','Write-JsonNew',
    'Replace-VerifiedFile','Ensure-File','Get-RegState','Restore-RegState',
    'Get-BackupDigest','Get-ValidatedBackup','Save-BackupIfMissing',
    'Get-Owner','Assert-NoOtherOwner','Claim-Owner','Get-ExpectedSlide',
    'Get-ImagePath','Get-SourceState','Resolve-SourceDirectory',
    'Set-CampaignImage','Invoke-Apply','Remove-LocalCampaign',
    'Test-ExactRegState','Invoke-Cleanup'
)
foreach($name in $required) {
    $found=@($definitions | Where-Object { $_.Name -eq $name })
    if ($found.Count -ne 1) { throw ('Production function not found exactly once: '+$name) }
    . ([scriptblock]::Create($found[0].Extent.Text))
}
function Verify([bool]$condition,[string]$test) {
    if (-not $condition) { throw ('FAIL: '+$test) }
    Write-Output ('PASS: '+$test)
}
# Sandboxed substitutes only for hardware/OS-specific effects.
function Get-NowInCampaignZone { return $script:MockNow }
function Protect-Directory {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        [void](New-Item -Path $Path -ItemType Directory -Force)
    }
}
function Send-FeedbackSafe {
    param([string]$StageOverride='')
    $script:FeedbackStages+= $StageOverride
}
$script:MockTasks=@()
$script:RemovedTasks=@()
function Get-ScheduledTask {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)][string]$TaskName)
    if ($script:MockTasks -contains $TaskName) {
        return [pscustomobject]@{TaskName=$TaskName}
    }
    return $null
}
function Unregister-ScheduledTask {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param([Parameter(Mandatory=$true)][string]$TaskName)
    if ($script:MockTasks -notcontains $TaskName) {
        throw ('Unexpected test scheduled task deletion: '+$TaskName)
    }
    $script:MockTasks=@($script:MockTasks | Where-Object { $_ -cne $TaskName })
    $script:RemovedTasks+= $TaskName
}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('LSCM-HOSTED-'+[guid]::NewGuid().ToString('N'))
$regId='LSCM-HOSTED-'+[guid]::NewGuid().ToString('N')
$regRoot='HKCU:\Software\'+$regId
$script:FeedbackStages=@()
try {
    [void](New-Item -Path $temp -ItemType Directory -Force)
    $global:CampaignId='lscm-hosted-'+([guid]::NewGuid().ToString('N').Substring(0,12))
    $global:CampaignBase=Join-Path $temp 'campaigns'
    $global:LocalRoot=Join-Path $CampaignBase $CampaignId
    $global:LocalImages=Join-Path $LocalRoot 'images'
    $global:LocalScript=Join-Path $LocalRoot 'LockScreen-Agent.ps1'
    $global:LocalConfig=Join-Path $LocalRoot 'campaign.json'
    $global:BackupPath=Join-Path $LocalRoot 'registry-backup.json'
    $global:OwnerPath=Join-Path $CampaignBase 'active-owner.json'
    $global:SourceStatePath=Join-Path $LocalRoot 'source-state.json'
    $global:LogPath=Join-Path $LocalRoot 'campaign.log'
    $global:TaskName='LockScreen-LS-'+$CampaignId
    $global:FeedbackTaskName='LockScreen-LS-Feedback-'+$CampaignId
    $global:CompletedPath=Join-Path (Join-Path $CampaignBase 'Completed') ($CampaignId+'.json')
    $global:PostCleanupReceiptPath=Join-Path (Join-Path $CampaignBase 'Completed') ($CampaignId+'.postcleanup.json')
    $global:RegistryPath=Join-Path $regRoot 'Personalization'
    $global:TimeZone=[TimeZoneInfo]::FindSystemTimeZoneById('UTC')
    $global:DateFormat='yyyy-MM-ddTHH:mm:ss'
    $global:Culture=[Globalization.CultureInfo]::InvariantCulture
    $global:CampaignStart=[datetime]'2026-11-07T10:00:00'
    $global:CampaignEnd=[datetime]'2026-11-07T12:00:00'
    $script:MockTasks=@($TaskName,$FeedbackTaskName)

    [void](New-Item -Path $LocalImages -ItemType Directory -Force)
    [void](New-Item -Path $RegistryPath -ItemType Directory -Force)
    $original='C:\Windows\Web\Screen\img100.jpg'
    New-ItemProperty -LiteralPath $RegistryPath -Name 'LockScreenImage' -PropertyType String -Value $original -Force | Out-Null
    New-ItemProperty -LiteralPath $RegistryPath -Name 'NoChangingLockScreen' -PropertyType DWord -Value 0 -Force | Out-Null
    New-ItemProperty -LiteralPath $RegistryPath -Name 'LSCM-UnrelatedPolicyTest' -PropertyType DWord -Value 7 -Force | Out-Null

    $initialFlag=Get-RegState 'NoChangingLockScreen'
    Verify ([bool]$initialFlag.Exists -and [string]$initialFlag.Value -ceq '0') 'initial zero DWORD exists before agent changes'
    $sourceRoot=Join-Path $temp 'trusted-source'
    $sourceImages=Join-Path $sourceRoot 'images'
    [void](New-Item -Path $sourceImages -ItemType Directory -Force)
    Copy-Item -LiteralPath $agent -Destination $LocalScript
    Copy-Item -LiteralPath $agent -Destination (Join-Path $sourceRoot 'LockScreen-Agent.ps1')
    $slides=@()
    for($index=1;$index -le 2;$index++) {
        $filename=('{0:D2}.jpg' -f $index)
        $bytes=[byte[]]($index,10,20,30,40,50,60,70,80,90,100)
        [IO.File]::WriteAllBytes((Join-Path $sourceImages $filename),$bytes)
        Copy-Item -LiteralPath (Join-Path $sourceImages $filename) -Destination (Join-Path $LocalImages $filename)
        $slides+= [pscustomobject]@{
            atLocal=([datetime]'2026-11-07T10:00:00').AddHours($index-1).ToString($DateFormat)
            fileName=$filename
            sha256=(Get-FileHash -LiteralPath (Join-Path $sourceImages $filename) -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $global:Slides=$slides
    $global:ResolvedSlotImages=$slides
    $cfg=[ordered]@{
        schemaVersion=1;campaignId=$CampaignId;timeZoneId='UTC'
        endLocal='2026-11-07T12:00:00';slides=$slides
    }
    $cfgJson=$cfg | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($LocalConfig,$cfgJson,[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $sourceRoot 'campaign.json'),$cfgJson,[Text.UTF8Encoding]::new($false))
    $manifestHash=(Get-FileHash -LiteralPath $LocalConfig -Algorithm SHA256).Hash.ToLowerInvariant()
    $agentHash=(Get-FileHash -LiteralPath $LocalScript -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-JsonNew $SourceStatePath ([ordered]@{
        CampaignId=$CampaignId
        SourceDirectory=$sourceRoot
        ManifestSha256=$manifestHash
        AgentSha256=$agentHash
    })

    $script:MockNow=[datetime]'2026-11-07T10:10:00'
    Invoke-Apply
    $first=Join-Path $LocalImages '01.jpg'
    Verify ((Get-ItemProperty -LiteralPath $RegistryPath -Name LockScreenImage).LockScreenImage -eq $first) 'production Apply selects first image in isolated HKCU'
    Verify ((Get-ItemProperty -LiteralPath $RegistryPath -Name NoChangingLockScreen).NoChangingLockScreen -eq 1) 'production Apply enforces lockscreen setting'
    $savedBackup=Get-ValidatedBackup
    Verify ($savedBackup.CampaignId -ceq $CampaignId) 'original registry backup is authenticated and bound to campaign'
    Verify ([bool]$savedBackup.NoChangingLockScreen.Exists -and
        [string]$savedBackup.NoChangingLockScreen.Value -ceq '0') 'original zero DWORD was retained in authenticated recovery snapshot'
    Verify ((Get-Owner).CampaignId -ceq $CampaignId) 'production owner record is created'
    Verify ((Get-ItemProperty -LiteralPath $RegistryPath -Name 'LSCM-UnrelatedPolicyTest').('LSCM-UnrelatedPolicyTest') -eq 7) 'Apply preserves unrelated registry values'

    $script:MockNow=[datetime]'2026-11-07T11:15:00'
    Invoke-Apply
    $second=Join-Path $LocalImages '02.jpg'
    Verify ((Get-ItemProperty -LiteralPath $RegistryPath -Name LockScreenImage).LockScreenImage -eq $second) 'scheduled next image changes registry path'

    [IO.File]::WriteAllBytes($second,[byte[]](255,0,255,0,255))
    Invoke-Apply
    Verify ((Get-FileHash -LiteralPath $second -Algorithm SHA256).Hash -ieq $slides[1].sha256) 'corrupted local image repaired by SHA-256 verified source'

    $external=Join-Path $temp 'external-policy.jpg'
    [IO.File]::WriteAllBytes($external,[byte[]](1,5,9,12))
    Set-ItemProperty -LiteralPath $RegistryPath -Name LockScreenImage -Value $external
    $blocked=$false
    try { Invoke-Apply } catch { $blocked=$true }
    Verify $blocked 'external policy override blocks agent writes'
    Verify ((Get-ItemProperty -LiteralPath $RegistryPath -Name LockScreenImage).LockScreenImage -eq $external) 'blocked Apply does not erase external policy'
    # Restore own image to test the cleanup path independently.
    Set-ItemProperty -LiteralPath $RegistryPath -Name LockScreenImage -Value $second

    $backupBytes=[IO.File]::ReadAllBytes($BackupPath)
    try {
        $broken=Get-Content -LiteralPath $BackupPath -Raw | ConvertFrom-Json
        $broken.LockScreenImage.Value=$external
        [IO.File]::WriteAllText($BackupPath,($broken | ConvertTo-Json -Depth 9))
        $blocked=$false
        try { Invoke-Apply } catch { $blocked=$true }
        Verify $blocked 'tampered recovery snapshot blocks registry writes'
    }
    finally { [IO.File]::WriteAllBytes($BackupPath,$backupBytes) }

    $beforeCleanup=Get-RegState 'NoChangingLockScreen'
    Verify ([bool]$beforeCleanup.Exists -and [string]$beforeCleanup.Value -ceq '1') 'managed DWORD still equals 1 before cleanup'
    $script:MockNow=$CampaignEnd
    Invoke-Apply
    Verify (-not (Test-Path -LiteralPath $LocalRoot)) 'campaign end automatically cleans local source files'
    Verify (-not (Test-Path -LiteralPath $OwnerPath)) 'cleanup releases campaign ownership'
    Verify (Test-Path -LiteralPath $CompletedPath -PathType Leaf) 'cleanup records completion marker'
    Verify (Test-Path -LiteralPath $PostCleanupReceiptPath -PathType Leaf) 'post-cleanup receipt exists only after task/file cleanup'
    $receipt=Get-Content -LiteralPath $PostCleanupReceiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Verify ([string]$receipt.cleanupStage -ceq 'AfterFileAndTaskRemoval' -and
        [bool]$receipt.localRootRemoved -and [bool]$receipt.scheduledTaskRemoved -and
        [bool]$receipt.feedbackTaskRemoved -and [bool]$receipt.ownerReleased -and
        [bool]$receipt.restoredOwnRegistry -and
        [string]$receipt.campaignId -ceq $CampaignId -and
        [bool]$receipt.expectedLockScreenImage.Exists -and
        [string]$receipt.expectedLockScreenImage.Value -ceq $original -and
        [string]$receipt.expectedNoChangingLockScreen.Value -ceq '0')
        'post-cleanup receipt records observed task removal and original HKCU registry restoration'
    Verify ((Get-ItemProperty -LiteralPath $RegistryPath -Name LockScreenImage).LockScreenImage -ceq $original) 'original lockscreen registry string restored'
    Verify ((Get-ItemProperty -LiteralPath $RegistryPath -Name NoChangingLockScreen).NoChangingLockScreen -eq 0) 'original DWORD 0 value restored'
    Verify ((Get-ItemProperty -LiteralPath $RegistryPath -Name 'LSCM-UnrelatedPolicyTest').('LSCM-UnrelatedPolicyTest') -eq 7) 'Cleanup preserves unrelated registry values'
    Verify ($script:MockTasks.Count -eq 0 -and $script:RemovedTasks.Count -eq 2) 'cleanup unregisters both mocked campaign/feedback tasks'
    Verify ($script:FeedbackStages -contains 'Completed') 'completion feedback reaches the stubbed reporting hook'

    Invoke-Cleanup
    Verify ((Get-ItemProperty -LiteralPath $RegistryPath -Name LockScreenImage).LockScreenImage -ceq $original) 'repeated cleanup leaves original registry untouched'
    Write-Output 'PASS: GitHub-hosted Windows ran real agent image selection, HKCU backup/restore, hash repair and cleanup.'
    Write-Output 'NOTE: This was NOT a real DC/GPO/SYSTEM/Windows 11 test.'
}
finally {
    if (Test-Path -LiteralPath $regRoot) { Remove-Item -LiteralPath $regRoot -Recurse -Force -ErrorAction Stop }
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction Stop }
}
