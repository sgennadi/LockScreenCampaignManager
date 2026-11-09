#requires -Version 5.1
<#
.SYNOPSIS
    Universal Lock Screen Campaign Agent (schema 1, agent 0.4.0-preview.73).
.DESCRIPTION
    -Mode Install from GPO Computer Startup stages and verifies the campaign.
    -Mode Apply runs from the local SYSTEM task at each image change, daily and at startup.
    -Mode Repair can restore missing/corrupt local files using the original trusted source.
    -Mode Status is read-only and reports local integrity and ownership.
    -Mode Cleanup restores the original lock screen before deleting local campaign files.
    Installation never captures the original registry while the campaign is pending.
.NOTES
    No RSAT, third-party modules, WinRM or installed .NET runtime are required.
    Client-side comments are intentionally in English.
#>
[CmdletBinding()]
param(
    [ValidateSet('Install','Apply','Repair','Cleanup','Status','Report','Test')]
    [string]$Mode = 'Install',
    [ValidateRange(1,30)][int]$TestImage = 1,
    [ValidateRange(10,1800)][int]$TestSeconds = 180
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$ConfigPath = Join-Path $Here 'campaign.json'
# The local manifest must be repaired before it is parsed. Repair is permitted only
# from the immutable source recorded on first installation and only with a valid hash.
$EarlySourceRecord = Join-Path $Here 'source-state.json'
if (Test-Path -LiteralPath $EarlySourceRecord -PathType Leaf) {
    $early = Get-Content -LiteralPath $EarlySourceRecord -Raw -Encoding UTF8 | ConvertFrom-Json
    $expected = [string]$early.ManifestSha256
    $rootName = Split-Path -Leaf $Here
    $parentName = Split-Path -Leaf (Split-Path -Parent $Here)
    if ([string]$early.CampaignId -cne $rootName -or
        $parentName -cne 'LockScreenCampaigns' -or
        $expected -cnotmatch '^[a-f0-9]{64}$' -or
        -not [IO.Path]::IsPathRooted([string]$early.SourceDirectory)) {
        throw 'Invalid local source identity. Refusing to parse campaign manifest.'
    }
    $currentHash = if (Test-Path -LiteralPath $ConfigPath -PathType Leaf) {
        (Get-FileHash -LiteralPath $ConfigPath -Algorithm SHA256).Hash
    } else { '' }
    if ($currentHash -ine $expected) {
        # Diagnostic modes must never modify a damaged local manifest.
        if ($Mode -eq 'Status') {
            [pscustomobject]@{
                AgentVersion='0.4.0-preview.73';CampaignId=[string]$early.CampaignId
                Health='CorruptManifest';ConfigHashOK=$false;Installed=$true
                RepairAction='Run -Mode Repair from verified SYSVOL source.'
            }
            return
        }
        if ($Mode -eq 'Report') { throw 'Cannot report: local manifest digest mismatch.' }
        $src = Join-Path ([string]$early.SourceDirectory) 'campaign.json'
        if (-not (Test-Path -LiteralPath $src -PathType Leaf) -or
            (Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash -ine $expected) {
            throw 'Local manifest corrupt and verified SYSVOL source unavailable.'
        }
        $stage = $ConfigPath + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
        $rollback = $ConfigPath + '.' + [guid]::NewGuid().ToString('N') + '.old'
        try {
            Copy-Item -LiteralPath $src -Destination $stage -ErrorAction Stop
            if ((Get-FileHash -LiteralPath $stage -Algorithm SHA256).Hash -ine $expected) {
                throw 'Staged manifest failed SHA-256 verification.'
            }
            if (Test-Path -LiteralPath $ConfigPath -PathType Leaf) {
                [IO.File]::Replace($stage,$ConfigPath,$rollback,$true)
            } else { [IO.File]::Move($stage,$ConfigPath) }
        } finally { Remove-Item -LiteralPath $stage,$rollback -Force -ErrorAction SilentlyContinue }
    }
}
if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
    throw ('Missing campaign.json next to the agent: {0}' -f $ConfigPath)
}
$Config = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([int]$Config.schemaVersion -ne 1) { throw 'Unsupported campaign schemaVersion.' }
$CampaignId = [string]$Config.campaignId
if ($CampaignId -cnotmatch '^[a-z0-9][a-z0-9_-]{0,70}$') { throw 'Invalid campaignId.' }
$TimeZone = [TimeZoneInfo]::FindSystemTimeZoneById([string]$Config.timeZoneId)
$Slides = @($Config.slides)
if ($Slides.Count -lt 1 -or $Slides.Count -gt 30) { throw 'Expected 1-30 campaign slides.' }
$DateFormat = 'yyyy-MM-ddTHH:mm:ss'
$Culture = [Globalization.CultureInfo]::InvariantCulture
$CampaignStart = [datetime]::ParseExact([string]$Slides[0].atLocal, $DateFormat, $Culture)
$CampaignEnd = [datetime]::ParseExact([string]$Config.endLocal, $DateFormat, $Culture)
$CampaignBase = Join-Path $env:ProgramData 'LockScreenCampaigns'
$LocalRoot = Join-Path $CampaignBase $CampaignId
$LocalImages = Join-Path $LocalRoot 'images'
$LocalScript = Join-Path $LocalRoot 'LockScreen-Agent.ps1'
$LocalConfig = Join-Path $LocalRoot 'campaign.json'
$SourceStatePath = Join-Path $LocalRoot 'source-state.json'
$BackupPath = Join-Path $LocalRoot 'registry-backup.json'
$LogPath = Join-Path $LocalRoot 'campaign.log'
$OwnerPath = Join-Path $CampaignBase 'active-owner.json'
$CompletedPath = Join-Path (Join-Path $CampaignBase 'Completed') ($CampaignId + '.json')
# This independent receipt is written ONLY after the cleanup has removed local files/tasks.
$PostCleanupReceiptPath = Join-Path (Join-Path $CampaignBase 'Completed') ($CampaignId + '.postcleanup.json')
$TaskName = 'LockScreen-LS-' + $CampaignId
$FeedbackTaskName = 'LockScreen-LS-Feedback-' + $CampaignId
$RegistryPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'
# All campaigns share a global lock: a campaign-specific mutex cannot prevent races.
$MutexName = 'Global\LockScreenCampaign_Coordinator'
$PreviousStart = [datetime]::MinValue
foreach ($slide in $Slides) {
    $at = [datetime]::ParseExact([string]$slide.atLocal, $DateFormat, $Culture)
    if ($at -le $PreviousStart -or $at -ge $CampaignEnd) { throw 'Invalid slide ordering.' }
    if ($TimeZone.IsInvalidTime($at) -or $TimeZone.IsAmbiguousTime($at)) { throw 'Invalid or ambiguous slide time.' }
    if ([string]$slide.fileName -cnotmatch '^[0-9]{2}\.jpg$' -or
        [string]$slide.sha256 -cnotmatch '^[0-9a-fA-F]{64}$') { throw 'Invalid image name/hash.' }
    $PreviousStart = $at
}
if ($CampaignEnd -le $PreviousStart -or
    $TimeZone.IsInvalidTime($CampaignEnd) -or $TimeZone.IsAmbiguousTime($CampaignEnd)) {
    throw 'Invalid campaign end time.'
}

# Each scheduled atLocal remains immutable. Optional randomization shuffles
# WHICH locally hash-verified JPG is assigned to each slot. The permutation
# depends only on campaign ID and computer name; no per-run randomness,
# machine-wide persisted secret or extra network request is needed.
$ResolvedSlotImages = @($Slides)
if ($Config.PSObject.Properties.Name -contains 'randomizePerDevice') {
    if ($Config.randomizePerDevice -isnot [bool]) {
        throw 'Invalid randomizePerDevice value in campaign manifest.'
    }
    if ([bool]$Config.randomizePerDevice) {
        if ([string]::IsNullOrWhiteSpace($env:COMPUTERNAME)) {
            throw 'Cannot assign per-device images without a computer name.'
        }
        $hasher = [Security.Cryptography.SHA256]::Create()
        try {
            $entries = @(
                for ($idx = 0; $idx -lt $Slides.Count; $idx++) {
                    $imageName = [string]$Slides[$idx].fileName
                    $key = '{0}|{1}|{2}' -f $CampaignId.ToLowerInvariant(),
                        $env:COMPUTERNAME.ToUpperInvariant(), $imageName
                    $digest = $hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($key))
                    [pscustomobject]@{
                        Hash = ([BitConverter]::ToString($digest).Replace('-', ''))
                        Index = $idx
                        Slide = $Slides[$idx]
                    }
                }
            )
            $ResolvedSlotImages = @($entries |
                Sort-Object Hash, Index |
                ForEach-Object { $_.Slide })
            if ($ResolvedSlotImages.Count -ne $Slides.Count) {
                throw 'Unable to build stable image order.'
            }
        }
        finally { $hasher.Dispose() }
    }
}

# Client feedback never modifies HKLM and is non-fatal for the campaign.
# Reports contain only computer ID, campaign ID, image name, state and health flags.
function Get-FeedbackSettings {
    if ($Config.PSObject.Properties.Name -notcontains 'feedback' -or
        $null -eq $Config.feedback -or -not [bool]$Config.feedback.enabled) { return $null }
    $root=([string]$Config.feedback.shareRoot).TrimEnd('\')
    if ($root -cnotmatch '^\\\\[a-zA-Z0-9][a-zA-Z0-9.-]{0,252}\\[a-zA-Z0-9_$.-]{1,80}$' -or
        $root.Contains('..')) { throw 'Invalid feedback SMB share root.' }
    $share=($root -split '\\')[-1]
    if ($share -match '^(?i:SYSVOL|NETLOGON|ADMIN\$|IPC\$|[A-Z]\$)$') {
        throw 'Feedback cannot be stored in SYSVOL, NETLOGON or administrative shares.'
    }
    $minutes=[int]$Config.feedback.intervalMinutes
    if ($minutes -lt 5 -or $minutes -gt 60) { throw 'Invalid feedback interval.' }
    return [pscustomobject]@{Root=$root;Interval=$minutes}
}
function Get-FeedbackRecord {
    param([string]$StageOverride='')
    $now=Get-NowInCampaignZone
    $slide=Get-ExpectedSlide
    $taskPresent=($null -ne (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue))
    $missing=0;$corrupt=0
    foreach($item in $Slides) {
        $file=Get-ImagePath $item
        if(-not (Test-Path -LiteralPath $file -PathType Leaf)) {$missing++}
        elseif(-not (Test-FileHash $file ([string]$item.sha256))){$corrupt++}
    }
    $stage=if($StageOverride -ne ''){$StageOverride}
           elseif($now -ge $CampaignEnd){'Completed'}
           elseif($null -eq $slide){'Pending'}
           else{'Active'}
    $expected=if($null -eq $slide){''}else{[string]$slide.fileName}
    $imageOk=($missing -eq 0 -and $corrupt -eq 0)
    $registryOk=$false
    if($stage -eq 'Active' -and $null -ne $slide){
        $cur=Get-RegState 'LockScreenImage'
        $lock=Get-RegState 'NoChangingLockScreen'
        $registryOk=([bool]$cur.Exists -and
            [string]$cur.Value -eq [string](Get-ImagePath $slide) -and
            [bool]$lock.Exists -and [string]$lock.Value -eq '1')
    }
    $record=Get-SourceState
    $manifestOk=($null -ne $record -and (Test-FileHash $LocalConfig ([string]$record.ManifestSha256)))
    $agentOk=($null -ne $record -and (Test-FileHash $LocalScript ([string]$record.AgentSha256)))
    $health=if($stage -eq 'Completed'){'Completed'}
            elseif(-not $imageOk -or -not $manifestOk -or -not $agentOk -or -not $taskPresent -or
                ($stage -eq 'Active' -and -not $registryOk)){'RepairNeeded'}
            else{'Healthy'}
    if($stage -eq 'Error'){$health='Error'}
    return [ordered]@{
        schemaVersion=1;campaignId=$CampaignId;computerName=$env:COMPUTERNAME.ToUpperInvariant()
        timestampUtc=[DateTime]::UtcNow.ToString('o')
        agentVersion='0.4.0-preview.73';clientType='PowerShell';stage=$stage;health=$health
        expectedImage=$expected;imageMatches=$imageOk;registryMatches=$registryOk
        taskPresent=$taskPresent;missingFiles=$missing;corruptFiles=$corrupt
    }
}
function Send-Feedback {
    param([string]$StageOverride='')
    $feedback=Get-FeedbackSettings
    if($null -eq $feedback){return}
    $directory=Join-Path $feedback.Root $CampaignId
    if(-not (Test-Path -LiteralPath $directory -PathType Container)){
        throw 'Feedback campaign directory is missing or SMB is unreachable.'
    }
    if($env:COMPUTERNAME -notmatch '^[A-Za-z0-9][A-Za-z0-9-]{0,62}$'){
        throw 'Invalid machine name for feedback report.'
    }
    $remote=Join-Path $directory ($env:COMPUTERNAME.ToUpperInvariant()+'.json')
    $temp=Join-Path $directory ('.'+$env:COMPUTERNAME+'.'+[guid]::NewGuid().ToString('N')+'.tmp')
    $backup=Join-Path $directory ('.'+$env:COMPUTERNAME+'.'+[guid]::NewGuid().ToString('N')+'.old')
    $payload=Get-FeedbackRecord -StageOverride $StageOverride | ConvertTo-Json -Depth 4
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes($payload)
    if($bytes.Length -gt 8192){throw 'Feedback payload exceeded 8 KiB.'}
    try {
        [IO.File]::WriteAllBytes($temp,$bytes)
        if(Test-Path -LiteralPath $remote -PathType Leaf){
            [IO.File]::Replace($temp,$remote,$backup,$true)
        }else{[IO.File]::Move($temp,$remote)}
    }finally{
        Remove-Item -LiteralPath $temp,$backup -Force -ErrorAction SilentlyContinue
    }
}
function Send-FeedbackSafe {
    param([string]$StageOverride='')
    try {Send-Feedback -StageOverride $StageOverride}
    catch {Write-Log ('Feedback unavailable: '+$_.Exception.GetType().Name)}
}
function Register-FeedbackTask {
    $feedback=Get-FeedbackSettings
    if($null -eq $feedback){return}
    $trigger=New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(2) `
        -RepetitionInterval (New-TimeSpan -Minutes $feedback.Interval) `
        -RepetitionDuration (New-TimeSpan -Days 45)
    $powershell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $args='-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" -Mode Report' -f $LocalScript
    $action=New-ScheduledTaskAction -Execute $powershell -Argument $args
    $principal=New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew `
        -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Minutes 5)
    Register-ScheduledTask -TaskName $FeedbackTaskName -Action $action `
        -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
}

function Get-NowInCampaignZone {
    return [TimeZoneInfo]::ConvertTimeFromUtc([datetime]::UtcNow, $TimeZone)
}
function Get-FileHashOrNull {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '' }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
}
function Test-FileHash {
    param([string]$Path,[string]$Expected)
    return ((Get-FileHashOrNull $Path) -eq $Expected.ToLowerInvariant())
}
function Write-Log {
    param([string]$Message)
    try {
        if (Test-Path -LiteralPath $LocalRoot -PathType Container) {
            Add-Content -LiteralPath $LogPath -Value ('{0:o} {1}' -f (Get-Date), $Message) -Encoding UTF8
        }
    } catch { }
}
function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if ($identity.User.Value -ne 'S-1-5-18' -and
        -not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run from Computer Startup as SYSTEM or elevated Windows PowerShell.'
    }
}
function Assert-SupportedWindows {
    # Windows 10 1607+ and Windows 11 Enterprise/Education/IoT Enterprise.
    # GPO forced lock-screen policy does not support Pro/Home.
    $win = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $build = 0
    if (-not [int]::TryParse([string]$win.CurrentBuildNumber, [ref]$build)) {
        throw 'Unable to determine Windows client build. Refusing to change lock screen policy.'
    }
    $edition = [string]$win.EditionID
    if ($build -lt 14393 -or
        $edition -notmatch '^(Enterprise|Education|IoTEnterprise)(S|SN|N)?$') {
        throw (('Unsupported lock screen GPO client: build {0}, edition {1}. ' +
            'Requires Windows 10 1607+ or Windows 11 Enterprise/Education/IoT Enterprise (including LTSC).') -f $build,$edition)
    }
}
function Protect-Directory {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        New-Item -Path $Path -ItemType Directory -Force -ErrorAction Stop | Out-Null
    }
    & "$env:SystemRoot\System32\icacls.exe" $Path '/inheritance:r' '/grant:r' `
        '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' `
        '*S-1-5-32-545:(OI)(CI)RX' '*S-1-15-2-1:(OI)(CI)RX' `
        '*S-1-15-2-2:(OI)(CI)RX' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw ('Failed to secure directory ACL: {0}' -f $Path) }
}
function Write-JsonNew {
    param([string]$Path,$Value)
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 10))
    $temp = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllBytes($temp,$bytes)
        # File.Move without overwrite fails closed if another process already wrote it.
        [IO.File]::Move($temp,$Path)
    } finally { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
}
function Replace-VerifiedFile {
    param([string]$Source,[string]$Destination,[string]$Expected)
    if (-not (Test-FileHash $Source $Expected)) {
        throw ('Trusted source is unavailable or fails SHA-256: {0}' -f $Source)
    }
    $temp = $Destination + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $backup = $Destination + '.' + [guid]::NewGuid().ToString('N') + '.old'
    try {
        Copy-Item -LiteralPath $Source -Destination $temp -ErrorAction Stop
        if (-not (Test-FileHash $temp $Expected)) { throw 'Staged file failed SHA-256 verification.' }
        if (Test-Path -LiteralPath $Destination -PathType Leaf) {
            [IO.File]::Replace($temp,$Destination,$backup,$true)
        } else {
            [IO.File]::Move($temp,$Destination)
        }
        if (-not (Test-FileHash $Destination $Expected)) {
            throw 'Installed file SHA-256 verification failed after replacement.'
        }
    }
    finally {
        Remove-Item -LiteralPath $temp,$backup -Force -ErrorAction SilentlyContinue
    }
}
function Get-RegState {
    param([string]$Name)
    $key = Get-Item -LiteralPath $RegistryPath -ErrorAction SilentlyContinue
    if ($null -eq $key -or $key.GetValueNames() -notcontains $Name) {
        return @{ Exists=$false; Kind='String'; Value=$null }
    }
    return @{
        Exists=$true; Kind=$key.GetValueKind($Name).ToString();
        Value=$key.GetValue($Name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    }
}
function Restore-RegState {
    param([string]$Name,$State)
    if (-not [bool]$State.Exists) {
        Remove-ItemProperty -LiteralPath $RegistryPath -Name $Name -ErrorAction SilentlyContinue
        return
    }
    $kind = [string]$State.Kind
    $value = $State.Value
    switch ($kind) {
        'DWord' { $value = [int]$value }
        'QWord' { $value = [long]$value }
        'Binary' { $value = [byte[]]$value }
        'MultiString' { $value = [string[]]$value }
    }
    # New-Item -Force on an existing registry key can discard unrelated
    # policy values. Create the key only if absent, never recreate it.
    if (-not (Test-Path -LiteralPath $RegistryPath -PathType Container)) {
        New-Item -Path $RegistryPath -Force | Out-Null
    }
    New-ItemProperty -LiteralPath $RegistryPath -Name $Name -PropertyType $kind -Value $value -Force | Out-Null
}
function Get-BackupDigest {
    param($Backup)
    $canonical = [ordered]@{
        Schema=2; CampaignId=[string]$Backup.CampaignId
        RegistryKeyExisted=[bool]$Backup.RegistryKeyExisted
        LockScreenImage=[ordered]@{
            Exists=[bool]$Backup.LockScreenImage.Exists
            Kind=[string]$Backup.LockScreenImage.Kind
            Value=$Backup.LockScreenImage.Value
        }
        NoChangingLockScreen=[ordered]@{
            Exists=[bool]$Backup.NoChangingLockScreen.Exists
            Kind=[string]$Backup.NoChangingLockScreen.Kind
            Value=$Backup.NoChangingLockScreen.Value
        }
    } | ConvertTo-Json -Depth 9 -Compress
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($canonical)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Get-ValidatedBackup {
    if (-not (Test-Path -LiteralPath $BackupPath -PathType Leaf)) { return $null }
    $data = Get-Content -LiteralPath $BackupPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([int]$data.Schema -notin @(1,2)) { throw 'Unsupported original registry backup schema.' }
    if ($null -eq $data.LockScreenImage -or $null -eq $data.NoChangingLockScreen -or
        $null -eq $data.RegistryKeyExisted) { throw 'Registry backup is incomplete.' }
    foreach ($state in @($data.LockScreenImage,$data.NoChangingLockScreen)) {
        if ($null -eq $state.Exists -or [string]$state.Kind -notin
            @('String','ExpandString','DWord','QWord','Binary','MultiString')) {
            throw 'Invalid original registry value in backup.'
        }
    }
    if ([int]$data.Schema -eq 2) {
        if ([string]$data.CampaignId -cne $CampaignId -or
            (Get-BackupDigest $data) -cne [string]$data.Digest) {
            throw 'Registry backup identity/digest mismatch. Refusing to modify policy.'
        }
    }
    return $data
}
function Save-BackupIfMissing {
    $existing = Get-ValidatedBackup
    if ($null -ne $existing) { return }
    $state = [ordered]@{
        Schema=2; CampaignId=$CampaignId
        RegistryKeyExisted=(Test-Path -LiteralPath $RegistryPath)
        LockScreenImage=(Get-RegState 'LockScreenImage')
        NoChangingLockScreen=(Get-RegState 'NoChangingLockScreen')
    }
    $state['Digest'] = Get-BackupDigest $state
    Write-JsonNew $BackupPath $state
    [void](Get-ValidatedBackup)
}
function Get-Owner {
    if (-not (Test-Path -LiteralPath $OwnerPath -PathType Leaf)) { return $null }
    $owner = Get-Content -LiteralPath $OwnerPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$owner.CampaignId -cnotmatch '^[a-z0-9][a-z0-9_-]{0,70}$') {
        throw 'Active ownership record is corrupt.'
    }
    return $owner
}
function Assert-NoOtherOwner {
    $owner = Get-Owner
    if ($null -ne $owner -and [string]$owner.CampaignId -cne $CampaignId) {
        throw ('Another campaign owns the Lock Screen: {0}. Clean it up before activation.' -f $owner.CampaignId)
    }
    $current = [string](Get-RegState 'LockScreenImage').Value
    if (($current.StartsWith(($CampaignBase + '\'),[StringComparison]::OrdinalIgnoreCase) -and
        -not $current.StartsWith(($LocalImages + '\'),[StringComparison]::OrdinalIgnoreCase)) -or
        ($current.StartsWith(($env:ProgramData.TrimEnd([char]92) + [char]92),[StringComparison]::OrdinalIgnoreCase) -and
        -not $current.StartsWith(($LocalImages + [char]92),[StringComparison]::OrdinalIgnoreCase))) {
        throw 'Another local or legacy campaign controls LockScreenImage. Refusing to overwrite it.'
    }
}
function Claim-Owner {
    Assert-NoOtherOwner
    if ($null -ne (Get-Owner)) { return }
    Write-JsonNew $OwnerPath ([ordered]@{
        CampaignId=$CampaignId; ClaimedUtc=([datetime]::UtcNow.ToString('o'))
    })
}
function Get-ExpectedSlide {
    $now = Get-NowInCampaignZone
    if ($now -ge $CampaignEnd) { return $null }
    $slot = -1
    for ($idx = 0; $idx -lt $Slides.Count; $idx++) {
        $at = [datetime]::ParseExact([string]$Slides[$idx].atLocal,$DateFormat,$Culture)
        if ($now -ge $at) { $slot = $idx } else { break }
    }
    if ($slot -lt 0) { return $null }
    # Health, actual Apply, and the local task all use the same resolved JPG.
    return $ResolvedSlotImages[$slot]
}
function Get-ImagePath {
    param($Slide)
    return Join-Path $LocalImages ([string]$Slide.fileName)
}
function Get-SourceState {
    if (-not (Test-Path -LiteralPath $SourceStatePath -PathType Leaf)) { return $null }
    $state = Get-Content -LiteralPath $SourceStatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$state.CampaignId -cne $CampaignId -or
        [string]$state.ManifestSha256 -cnotmatch '^[0-9a-f]{64}$' -or
        [string]$state.AgentSha256 -cnotmatch '^[0-9a-f]{64}$' -or
        -not [IO.Path]::IsPathRooted([string]$state.SourceDirectory)) {
        throw 'Installed source identity/manifest record is corrupt.'
    }
    return $state
}
function Resolve-SourceDirectory {
    $record = Get-SourceState
    if ($null -ne $record) { return [string]$record.SourceDirectory }
    return $Here
}
function Get-AgentExpectedHash {
    if ($Config.PSObject.Properties.Name -contains 'agentSha256' -and
        [string]$Config.agentSha256 -match '^[0-9a-fA-F]{64}$') {
        return ([string]$Config.agentSha256).ToLowerInvariant()
    }
    $state = Get-SourceState
    if ($null -ne $state) { return ([string]$state.AgentSha256).ToLowerInvariant() }
    return ''
}
function Assert-ManifestIdentity {
    param([string]$TrustedConfig)
    $state = Get-SourceState
    if ($null -ne $state -and -not (Test-FileHash $TrustedConfig ([string]$state.ManifestSha256))) {
        throw 'Source campaign.json changed for an existing CampaignID. Use a new CampaignID.'
    }
}
function Ensure-File {
    param([string]$Destination,[string]$Expected,[string]$Source)
    if (Test-FileHash $Destination $Expected) { return $false }
    Write-Log ('Integrity mismatch, attempting repair: {0}' -f [IO.Path]::GetFileName($Destination))
    Replace-VerifiedFile $Source $Destination $Expected
    Write-Log ('Repaired file: {0}' -f [IO.Path]::GetFileName($Destination))
    return $true
}
function Ensure-LocalConfigAndScript {
    $record = Get-SourceState
    if ($null -eq $record) {
        if (-not $PSCommandPath.Equals($LocalScript,[StringComparison]::OrdinalIgnoreCase)) {
            throw 'Missing source-state record on an existing campaign. Verify before repair.'
        }
        return
    }
    $source = [string]$record.SourceDirectory
    [void](Ensure-File $LocalConfig ([string]$record.ManifestSha256) (Join-Path $source 'campaign.json'))
    [void](Ensure-File $LocalScript ([string]$record.AgentSha256) (Join-Path $source 'LockScreen-Agent.ps1'))
}
function Assert-NoScheduleOverlap {
    # Check other staged campaigns even if neither has yet written registry values.
    $thisStartUtc = [TimeZoneInfo]::ConvertTimeToUtc([datetime]::SpecifyKind($CampaignStart,[DateTimeKind]::Unspecified),$TimeZone)
    $thisEndUtc = [TimeZoneInfo]::ConvertTimeToUtc([datetime]::SpecifyKind($CampaignEnd,[DateTimeKind]::Unspecified),$TimeZone)
    foreach ($folder in @(Get-ChildItem -LiteralPath $CampaignBase -Directory -ErrorAction SilentlyContinue)) {
        if ($folder.Name -ceq $CampaignId -or $folder.Name -eq 'Completed') { continue }
        $otherPath = Join-Path $folder.FullName 'campaign.json'
        if (-not (Test-Path -LiteralPath $otherPath -PathType Leaf)) { continue }
        try {
            $other = Get-Content -LiteralPath $otherPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $zone = [TimeZoneInfo]::FindSystemTimeZoneById([string]$other.timeZoneId)
            $firstSlide = @($other.slides)[0]
            $first = [datetime]::ParseExact([string]$firstSlide.atLocal,$DateFormat,$Culture)
            $last = [datetime]::ParseExact([string]$other.endLocal,$DateFormat,$Culture)
            $otherStartUtc = [TimeZoneInfo]::ConvertTimeToUtc([datetime]::SpecifyKind($first,[DateTimeKind]::Unspecified),$zone)
            $otherEndUtc = [TimeZoneInfo]::ConvertTimeToUtc([datetime]::SpecifyKind($last,[DateTimeKind]::Unspecified),$zone)
            if ($thisStartUtc -lt $otherEndUtc -and $otherStartUtc -lt $thisEndUtc) {
                throw ('Campaign schedule overlaps an installed campaign: {0}' -f $folder.Name)
            }
        }
        catch {
            # A corrupt foreign manifest is a reason to stop, not to claim its registry keys.
            throw ('Cannot verify other campaign {0}: {1}' -f $folder.Name,$_.Exception.Message)
        }
    }
}
function Convert-ToMachineTime {
    param([datetime]$CampaignLocalTime)
    $utc = [TimeZoneInfo]::ConvertTimeToUtc(
        [datetime]::SpecifyKind($CampaignLocalTime,[DateTimeKind]::Unspecified),$TimeZone)
    return [TimeZoneInfo]::ConvertTimeFromUtc($utc,[TimeZoneInfo]::Local)
}
function Register-CampaignTask {
    $events = @($Slides | ForEach-Object {
        [datetime]::ParseExact([string]$_.atLocal,$DateFormat,$Culture)
    })
    $events += $CampaignEnd
    $triggers = @(New-ScheduledTaskTrigger -AtStartup)
    foreach ($event in $events) {
        $triggers += New-ScheduledTaskTrigger -Once -At (Convert-ToMachineTime $event)
    }
    # Daily self-check heals the active JPG after accidental damage without a reboot.
    $triggers += New-ScheduledTaskTrigger -Daily -At '03:00'
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" -Mode Apply' -f $LocalScript
    $action = New-ScheduledTaskAction -Execute $powershell -Argument $arguments
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 10)
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers `
        -Principal $principal -Settings $settings -Force | Out-Null
}
function Set-CampaignImage {
    param([string]$Path)
    if ($null -eq (Get-ValidatedBackup)) { throw 'Missing original registry backup.' }
    Assert-NoOtherOwner
    # New-Item -Force on an existing registry key can discard unrelated
    # policy values. Create the key only if absent, never recreate it.
    if (-not (Test-Path -LiteralPath $RegistryPath -PathType Container)) {
        New-Item -Path $RegistryPath -Force | Out-Null
    }
    New-ItemProperty -LiteralPath $RegistryPath -Name LockScreenImage -PropertyType String -Value $Path -Force | Out-Null
    New-ItemProperty -LiteralPath $RegistryPath -Name NoChangingLockScreen -PropertyType DWord -Value 1 -Force | Out-Null
}
function Invoke-Apply {
    if ((Get-NowInCampaignZone) -ge $CampaignEnd) {
        Invoke-Cleanup
        return
    }
    $slide = Get-ExpectedSlide
    if ($null -eq $slide) { return }
    if (-not (Test-Path -LiteralPath $LocalConfig -PathType Leaf)) {
        throw 'Campaign is not installed. Run -Mode Install from SYSVOL.'
    }
    $state = Get-SourceState
    if ($null -ne $state -and -not (Test-FileHash $LocalConfig ([string]$state.ManifestSha256))) {
        # Agent reads campaign.json before this function. A GPO Startup repair can
        # reconstruct the manifest from its immutable source-state hash.
        throw 'Local manifest hash mismatch. Run Install from trusted GPO source.'
    }
    if ($null -ne $state -and -not (Test-FileHash $LocalScript ([string]$state.AgentSha256))) {
        # Do not continue to run a modified script as SYSTEM.
        throw 'Local agent script hash mismatch. Run Install from trusted GPO source.'
    }
    Assert-NoOtherOwner
    $path = Get-ImagePath $slide
    if (-not (Test-FileHash $path ([string]$slide.sha256))) {
        $source = Join-Path (Join-Path (Resolve-SourceDirectory) 'images') ([string]$slide.fileName)
        [void](Ensure-File $path ([string]$slide.sha256) $source)
    }
    # A separate policy changing the value during the campaign is not silently overwritten.
    $current = Get-RegState 'LockScreenImage'
    $owner = Get-Owner
    if ($null -ne $owner -and [string]$owner.CampaignId -ceq $CampaignId) {
        $isOurs = [bool]$current.Exists -and
            ([string]$current.Value).StartsWith(($LocalImages + '\'),[StringComparison]::OrdinalIgnoreCase)
        $backup = Get-ValidatedBackup
        $isOriginal = $null -ne $backup -and
            [string]$current.Value -eq [string]$backup.LockScreenImage.Value
        if ([bool]$current.Exists -and -not $isOurs -and -not $isOriginal) {
            throw 'LockScreenImage was overridden by another policy; refusing to overwrite it.'
        }
    }
    $before = Get-RegState 'LockScreenImage'
    if ([bool]$before.Exists -and
        ([string]$before.Value).StartsWith(($LocalImages + '\'),[StringComparison]::OrdinalIgnoreCase) -and
        $null -eq (Get-ValidatedBackup)) {
        throw 'Cannot recapture an original backup after own campaign was already active.'
    }
    Save-BackupIfMissing
    Claim-Owner
    Set-CampaignImage $path
    Write-Log ('Applied image {0}; SHA-256 validated.' -f $slide.fileName)
    Send-FeedbackSafe
}
function Invoke-Install {
    Assert-SupportedWindows
    if ((Get-NowInCampaignZone) -ge $CampaignEnd) { Invoke-Cleanup; return }
    Protect-Directory $CampaignBase
    Protect-Directory $LocalRoot
    if (Test-Path -LiteralPath $CompletedPath -PathType Leaf) {
        throw 'Campaign was already completed on this computer. Use a NEW campaign ID.'
    }
    Assert-NoScheduleOverlap
    $record = Get-SourceState
    $isSource = -not $Here.Equals($LocalRoot,[StringComparison]::OrdinalIgnoreCase)
    $sourceDir = if ($null -eq $record) { $Here } else { [string]$record.SourceDirectory }
    if ($isSource) { $sourceDir = $Here }
    $trustedManifest = Join-Path $sourceDir 'campaign.json'
    $trustedAgent = Join-Path $sourceDir 'LockScreen-Agent.ps1'
    $manifestHash = Get-FileHashOrNull $trustedManifest
    $agentHash = Get-FileHashOrNull $trustedAgent
    if ($manifestHash -notmatch '^[0-9a-f]{64}$' -or $agentHash -notmatch '^[0-9a-f]{64}$') {
        throw 'Source campaign files cannot be verified.'
    }
    if (($Config.PSObject.Properties.Name -contains 'agentSha256') -and
        ([string]$Config.agentSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
         $agentHash -ine [string]$Config.agentSha256)) {
        throw 'Source agent does not match campaign manifest SHA-256.'
    }
    if ($null -ne $record -and $manifestHash -ne [string]$record.ManifestSha256) {
        throw 'Source campaign manifest changed for existing CampaignID.'
    }
    if ($null -ne $record -and $agentHash -ne [string]$record.AgentSha256) {
        throw 'Source agent was replaced for an existing CampaignID. Use a new campaign package.'
    }
    foreach ($slide in $Slides) {
        $src = Join-Path (Join-Path $sourceDir 'images') ([string]$slide.fileName)
        if (-not (Test-FileHash $src ([string]$slide.sha256))) {
            throw ('Source image hash mismatch: {0}' -f $slide.fileName)
        }
    }
    if ($null -eq $record) {
        if ((Test-Path -LiteralPath $LocalConfig) -and -not (Test-FileHash $LocalConfig $manifestHash)) {
            throw 'Existing local campaign manifest differs; refuse to replace without trusted source-state.'
        }
        Write-JsonNew $SourceStatePath ([ordered]@{
            CampaignId=$CampaignId; SourceDirectory=$sourceDir
            ManifestSha256=$manifestHash; AgentSha256=$agentHash
        })
    }
    [void](Ensure-File $LocalConfig $manifestHash $trustedManifest)
    [void](Ensure-File $LocalScript $agentHash $trustedAgent)
    if (-not (Test-Path -LiteralPath $LocalImages -PathType Container)) {
        New-Item -Path $LocalImages -ItemType Directory -Force | Out-Null
    }
    $repaired=0
    foreach ($slide in $Slides) {
        $filename=[string]$slide.fileName
        if (Ensure-File (Join-Path $LocalImages $filename) ([string]$slide.sha256) `
            (Join-Path (Join-Path $sourceDir 'images') $filename)) { $repaired++ }
    }
    # No registry backup is captured here: Install can happen days before activation.
    Register-CampaignTask
    Register-FeedbackTask
    Send-FeedbackSafe
    Write-Log ('Installed/verified. Repaired {0} image(s).' -f $repaired)
    Invoke-Apply
}
function Remove-LocalCampaign {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($null -ne $task) { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop }
    $feedbackTask=Get-ScheduledTask -TaskName $FeedbackTaskName -ErrorAction SilentlyContinue
    if($null -ne $feedbackTask){
        Unregister-ScheduledTask -TaskName $FeedbackTaskName -Confirm:$false -ErrorAction Stop
    }
    if (Test-Path -LiteralPath $LocalRoot -PathType Container) {
        Remove-Item -LiteralPath $LocalRoot -Recurse -Force -ErrorAction Stop
    }
}
function Test-ExactRegState {
    param([string]$Name,$Expected)
    if ($null -eq $Expected) { return $false }
    $actual=Get-RegState $Name
    if ([bool]$actual.Exists -ne [bool]$Expected.Exists) { return $false }
    if (-not [bool]$Expected.Exists) { return $true }
    if ([string]$actual.Kind -cne [string]$Expected.Kind) { return $false }
    # JSON converts byte arrays, integers and string arrays consistently in PS 5.1.
    return (($actual.Value | ConvertTo-Json -Depth 5 -Compress) -ceq
        ($Expected.Value | ConvertTo-Json -Depth 5 -Compress))
}
function Invoke-Cleanup {
    if (-not (Test-Path -LiteralPath $LocalRoot -PathType Container)) { return }
    $backup = Get-ValidatedBackup
    $owner = Get-Owner
    $current = Get-RegState 'LockScreenImage'
    $isOurs = [bool]$current.Exists -and
        ([string]$current.Value).StartsWith(($LocalImages + '\'),[StringComparison]::OrdinalIgnoreCase)
    if ($null -ne $owner -and [string]$owner.CampaignId -cne $CampaignId -and $isOurs) {
        throw 'Ownership conflict during cleanup; no registry or files changed.'
    }
    if ($isOurs -and $null -eq $backup) {
        throw 'Campaign owns current registry image, but original backup is missing.'
    }
    if ($isOurs) {
        Restore-RegState 'LockScreenImage' $backup.LockScreenImage
        $flag = Get-RegState 'NoChangingLockScreen'
        if ([bool]$flag.Exists -and [string]$flag.Value -eq '1') {
            Restore-RegState 'NoChangingLockScreen' $backup.NoChangingLockScreen
        }
        if (-not [bool]$backup.RegistryKeyExisted -and (Test-Path -LiteralPath $RegistryPath)) {
            $key = Get-Item -LiteralPath $RegistryPath
            if ($key.GetValueNames().Count -eq 0 -and $key.GetSubKeyNames().Count -eq 0) {
                Remove-Item -LiteralPath $RegistryPath -Force
            }
        }
        Write-Log 'Original Lock Screen registry values restored.'
    }
    elseif ($null -ne $backup) {
        Write-Log 'LockScreenImage belongs to another owner; registry left unchanged.'
    }
    if ($null -ne $owner -and [string]$owner.CampaignId -ceq $CampaignId) {
        # If the current path is no longer ours, do not remove someone else's policy.
        Remove-Item -LiteralPath $OwnerPath -Force -ErrorAction Stop
    }
    $completedDir = Split-Path -Parent $CompletedPath
    Protect-Directory $completedDir
    if (-not (Test-Path -LiteralPath $CompletedPath -PathType Leaf)) {
        Write-JsonNew $CompletedPath ([ordered]@{
            CampaignId=$CampaignId; CompletedUtc=([datetime]::UtcNow.ToString('o'))
            RestoredOwnRegistry=$isOurs; Status='Completed'
        })
    }
    # 'Completed' heartbeat is a progress hint, NOT a post-deletion attestation.
    Send-FeedbackSafe -StageOverride Completed
    Remove-LocalCampaign
    # The completion receipt is produced AFTER the actual cleanup. A failed
    # cleanup leaves the earlier marker but NO final receipt (fail closed).
    $taskGone=$null -eq (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)
    $feedbackGone=$null -eq (Get-ScheduledTask -TaskName $FeedbackTaskName -ErrorAction SilentlyContinue)
    $localGone=-not (Test-Path -LiteralPath $LocalRoot)
    $finalOwner=Get-Owner
    $released=($null -eq $finalOwner -or [string]$finalOwner.CampaignId -cne $CampaignId)
    $restored=$isOurs -and $null -ne $backup -and
        (Test-ExactRegState 'LockScreenImage' $backup.LockScreenImage) -and
        (Test-ExactRegState 'NoChangingLockScreen' $backup.NoChangingLockScreen)
    if (-not $taskGone -or -not $feedbackGone -or -not $localGone -or -not $released) {
        throw 'Post-cleanup local footprint check failed. Keep evidence for administrative review.'
    }
    if (-not (Test-Path -LiteralPath $PostCleanupReceiptPath -PathType Leaf)) {
        Write-JsonNew $PostCleanupReceiptPath ([ordered]@{
            schemaVersion=1;campaignId=$CampaignId
            observedUtc=([datetime]::UtcNow.ToString('o'))
            cleanupStage='AfterFileAndTaskRemoval'
            localRootRemoved=$localGone
            scheduledTaskRemoved=$taskGone;feedbackTaskRemoved=$feedbackGone
            ownerReleased=$released;restoredOwnRegistry=$restored
            expectedLockScreenImage=$(if($restored){$backup.LockScreenImage}else{$null})
            expectedNoChangingLockScreen=$(if($restored){$backup.NoChangingLockScreen}else{$null})
        })
    }
}
function Show-Status {
    $missing = [Collections.Generic.List[string]]::new()
    $corrupt = [Collections.Generic.List[string]]::new()
    foreach ($slide in $Slides) {
        $path = Get-ImagePath $slide
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $missing.Add([string]$slide.fileName) }
        elseif (-not (Test-FileHash $path ([string]$slide.sha256))) { $corrupt.Add([string]$slide.fileName) }
    }
    $record = $null
    $recordError = ''
    try { $record = Get-SourceState } catch { $recordError = $_.Exception.Message }
    $backupState = 'Missing'
    try {
        $backup = Get-ValidatedBackup
        if ($null -ne $backup) { $backupState='Valid' }
    } catch { $backupState='Corrupt' }
    $owner = $null
    $ownerError = ''
    try { $owner = Get-Owner } catch { $ownerError = $_.Exception.Message }
    $expected = Get-ExpectedSlide
    $cfgOk = $null -ne $record -and (Test-FileHash $LocalConfig ([string]$record.ManifestSha256))
    $agentOk = $null -ne $record -and (Test-FileHash $LocalScript ([string]$record.AgentSha256))
    $health = if ($recordError -or $ownerError -or $backupState -eq 'Corrupt') { 'Error' }
        elseif (@($missing.ToArray()).Count -gt 0 -or @($corrupt.ToArray()).Count -gt 0 -or -not $cfgOk -or -not $agentOk) { 'RepairNeeded' }
        else { 'Healthy' }
    [pscustomobject]@{
        AgentVersion='0.4.0-preview.73'; CampaignId=$CampaignId; Health=$health
        TimeZone=[string]$Config.timeZoneId
        NowInCampaignTimeZone=(Get-NowInCampaignZone)
        Start=[string]$Slides[0].atLocal; End=[string]$Config.endLocal
        ExpectedImage=$(if ((Get-NowInCampaignZone) -ge $CampaignEnd) {'Finished'} elseif ($null -eq $expected) {'Not started'} else {[string]$expected.fileName})
        Installed=(Test-Path -LiteralPath $LocalScript -PathType Leaf)
        MissingImages=@($missing.ToArray()); CorruptImages=@($corrupt.ToArray())
        ConfigHashOK=$cfgOk; AgentHashOK=$agentOk
        SourceRecordError=$recordError; BackupState=$backupState
        ActiveOwner=$(if ($null -eq $owner) {''} else {[string]$owner.CampaignId})
        OwnerError=$ownerError; Completed=(Test-Path -LiteralPath $CompletedPath -PathType Leaf)
        ScheduledTask=($null -ne (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue))
        CurrentRegistryImage=(Get-RegState 'LockScreenImage').Value
    }
}
function Invoke-Test {
    Assert-SupportedWindows
    $now = Get-NowInCampaignZone
    if ($now -ge $CampaignEnd -or
        $now.AddSeconds($TestSeconds) -ge $CampaignStart) {
        throw 'Test may not overlap with live campaign or finished campaign.'
    }
    if ($TestImage -gt $Slides.Count) { throw 'TestImage exceeds number of slides.' }
    Assert-NoOtherOwner
    $slide = $Slides[$TestImage-1]
    $candidate = Get-ImagePath $slide
    if (-not (Test-FileHash $candidate ([string]$slide.sha256))) {
        $candidate = Join-Path (Join-Path (Resolve-SourceDirectory) 'images') ([string]$slide.fileName)
    }
    if (-not (Test-FileHash $candidate ([string]$slide.sha256))) { throw 'Test image failed SHA-256.' }
    $beforeImage = Get-RegState 'LockScreenImage'
    $beforeFlag = Get-RegState 'NoChangingLockScreen'
    $keyExisted = Test-Path -LiteralPath $RegistryPath
    try {
        # Do not recreate an existing registry key: unrelated values must survive.
        if (-not (Test-Path -LiteralPath $RegistryPath -PathType Container)) {
            New-Item -Path $RegistryPath -Force | Out-Null
        }
        New-ItemProperty -LiteralPath $RegistryPath -Name LockScreenImage -PropertyType String -Value $candidate -Force | Out-Null
        New-ItemProperty -LiteralPath $RegistryPath -Name NoChangingLockScreen -PropertyType DWord -Value 1 -Force | Out-Null
        Write-Host ('Showing {0} for {1} seconds. Do not close PowerShell.' -f $slide.fileName,$TestSeconds)
        & "$env:SystemRoot\System32\rundll32.exe" 'user32.dll,LockWorkStation'
        Start-Sleep -Seconds $TestSeconds
    }
    finally {
        Restore-RegState 'LockScreenImage' $beforeImage
        Restore-RegState 'NoChangingLockScreen' $beforeFlag
        if (-not $keyExisted -and (Test-Path -LiteralPath $RegistryPath)) {
            $key=Get-Item -LiteralPath $RegistryPath
            if ($key.GetValueNames().Count -eq 0 -and $key.GetSubKeyNames().Count -eq 0) {
                Remove-Item -LiteralPath $RegistryPath -Force
            }
        }
    }
}

if ($Mode -eq 'Status') { Show-Status; return }
if ($Mode -eq 'Report') {
    if ($null -eq (Get-FeedbackSettings)) { throw 'Feedback is disabled for this campaign.' }
    Send-Feedback
    return
}
Assert-Admin
$mutex = [Threading.Mutex]::new($false,$MutexName)
$acquired = $false
try {
    try { $acquired = $mutex.WaitOne([TimeSpan]::FromSeconds(40)) }
    catch [Threading.AbandonedMutexException] { $acquired = $true }
    if (-not $acquired) { throw 'Another Lock Screen campaign operation is running.' }
    switch ($Mode) {
        'Install' { Invoke-Install }
        'Repair' { Invoke-Install }
        'Apply' { Invoke-Apply }
        'Cleanup' { Invoke-Cleanup }
        'Test' { Invoke-Test }
    }
}
catch {
    Write-Log ('ERROR: '+$_.Exception.Message)
    Send-FeedbackSafe -StageOverride Error
    throw
}
finally {
    if ($acquired) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
