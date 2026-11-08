# Windows PowerShell 5.1: function-level integration tests with isolated temporary files.
# No AD, registry modification, SYSVOL access or scheduled task creation.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$root = Split-Path -Parent $PSScriptRoot
$agent = Join-Path $root 'src\LockScreenCampaignManager\Assets\LockScreen-Agent.ps1'
$tokens=$null; $issues=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($agent,[ref]$tokens,[ref]$issues)
if (@($issues).Count -ne 0) { throw ('PowerShell parser errors: '+($issues | Out-String)) }
$names=@('Get-FileHashOrNull','Test-FileHash','Replace-VerifiedFile','Ensure-File','Write-Log',
         'Write-JsonNew','Get-BackupDigest','Get-ValidatedBackup','Get-SourceState',
         'Assert-NoScheduleOverlap')
$functions = @($ast.FindAll({ param($n)
    $n -is [Management.Automation.Language.FunctionDefinitionAst] },$true))
foreach ($name in $names) {
    $fun = @($functions | Where-Object { $_.Name -eq $name })
    if ($fun.Count -ne 1) { throw ('Expected exactly one function: '+$name) }
    Invoke-Expression $fun[0].Extent.Text
}
# Override a log operation that isn't relevant to file-integrity tests.
function Write-Log { param([string]$Message) }
function Check([bool]$OK,[string]$Name) { if (-not $OK) { throw ('FAILED: '+$Name) } Write-Host ('PASS: '+$Name) }
$temp=Join-Path ([IO.Path]::GetTempPath()) ('lscm-client-health-'+[guid]::NewGuid().ToString('N'))
[void](New-Item -Path $temp -ItemType Directory -Force)
try {
    $source=Join-Path $temp 'trusted.jpg'
    $dest=Join-Path $temp 'client.jpg'
    [IO.File]::WriteAllBytes($source,[byte[]](1,2,3,4,5,0,9))
    $good=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    Check (-not (Test-FileHash $dest $good)) 'missing file recognized'
    Check ([bool](Ensure-File $dest $good $source)) 'missing image repaired'
    Check (Test-FileHash $dest $good) 'repaired image hash verified'
    Check (-not [bool](Ensure-File $dest $good $source)) 'identical image not overwritten'
    [IO.File]::WriteAllBytes($dest,[byte[]](5,4,3,2,1))
    Check ([bool](Ensure-File $dest $good $source)) 'corrupted local JPG repaired atomically'
    Check (Test-FileHash $dest $good) 'atomic repair SHA matches'
    [IO.File]::WriteAllBytes($dest,[byte[]](9,8,7))
    [IO.File]::WriteAllBytes($source,[byte[]](1,3,5))
    $threw=$false
    try { [void](Ensure-File $dest $good $source) } catch { $threw=$true }
    Check $threw 'invalid source refuses repair'
    Check (-not (Test-FileHash $dest $good)) 'failed repair never pretends to succeed'

    $global:CampaignId='sample-202801011400'
    $global:BackupPath=Join-Path $temp 'registry-backup.json'
    $backup=[ordered]@{
        Schema=2; CampaignId=$CampaignId; RegistryKeyExisted=$true
        LockScreenImage=[ordered]@{Exists=$true;Kind='String';Value='C:\Windows\Web\Screen\img100.jpg'}
        NoChangingLockScreen=[ordered]@{Exists=$false;Kind='DWord';Value=$null}
    }
    $backup['Digest']=Get-BackupDigest $backup
    Write-JsonNew $BackupPath $backup
    $saved=Get-ValidatedBackup
    Check ([string]$saved.CampaignId -eq $CampaignId) 'backup identity and hash validated after JSON roundtrip'
    $tampered=Get-Content -LiteralPath $BackupPath -Raw | ConvertFrom-Json
    $tampered.LockScreenImage.Value='C:\other\wallpaper.jpg'
    [IO.File]::WriteAllText($BackupPath,($tampered | ConvertTo-Json -Depth 9))
    $threw=$false
    try { [void](Get-ValidatedBackup) } catch { $threw=$true }
    Check $threw 'backup modification detected before restoration'

    $global:CampaignBase=Join-Path $temp 'Campaigns'
    [void](New-Item -Path $CampaignBase -ItemType Directory)
    $global:TimeZone=[TimeZoneInfo]::FindSystemTimeZoneById('UTC')
    $global:DateFormat='yyyy-MM-ddTHH:mm:ss'
    $global:Culture=[Globalization.CultureInfo]::InvariantCulture
    $global:Slides=@([pscustomobject]@{atLocal='2028-01-01T14:00:00'})
    $global:CampaignStart=[datetime]'2028-01-01T14:00:00'
    $global:CampaignEnd=[datetime]'2028-01-03T14:00:00'
    $prevDir=Join-Path $CampaignBase 'other-2028'
    [void](New-Item -Path $prevDir -ItemType Directory)
    $previous=[ordered]@{timeZoneId='UTC';endLocal='2028-01-04T14:00:00';slides=@(@{atLocal='2028-01-02T14:00:00'})}
    $previous | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $prevDir 'campaign.json')
    $threw=$false
    try { Assert-NoScheduleOverlap } catch { $threw=$true }
    Check $threw 'overlapping staged campaigns refused'
    $previous.slides=@(@{atLocal='2028-01-04T14:00:00'})
    $previous.endLocal='2028-01-05T14:00:00'
    $previous | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $prevDir 'campaign.json')
    Assert-NoScheduleOverlap
    Check $true 'nonoverlapping staged campaigns permitted'
    Write-Host 'PASS: client integrity and recovery test suite'
}
finally { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
