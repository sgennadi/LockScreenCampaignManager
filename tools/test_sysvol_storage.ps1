# Offline Windows PowerShell 5.1 tests of the actual embedded storage functions.
# No AD, SYSVOL, network connection, filesystem deletion, GPO or client access.
$ErrorActionPreference='Stop'
$asset=Join-Path $PSScriptRoot '..\src\LockScreenCampaignManager\Assets\Sysvol-Storage.ps1'
$source=Get-Content -LiteralPath $asset -Raw -Encoding UTF8
$null=$tokens=$errors=$null
[void][Management.Automation.Language.Parser]::ParseFile($asset,[ref]$tokens,[ref]$errors)
if (@($errors).Count -ne 0) {
    throw ('SYSVOL storage PS 5.1 parse error: ' +
        ((@($errors) | ForEach-Object { $_.Message }) -join '; '))
}
foreach($must in @(
    'RootDSE', 'currentTime', 'SHA256', 'Assert-NoLink',
    'Get-FileHash', 'Gpo-References', 'Guard-Plan',
    'Assert-DcClock', 'clockSkewSeconds', 'Remove-Item',
    "ValidateSet('Scan','Preview','Cleanup')",
    '30-day post-end grace', 'previewUtc', 'planHash', 'approveId'
)) {
    if (-not $source.Contains($must)) { throw ('Missing safety contract: '+$must) }
}
function Pure([string]$name,[string]$next) {
    $regex='(?ms)^function '+[regex]::Escape($name)+'\b[^\r\n]*\{.*?(?=^function '+
        [regex]::Escape($next)+'\b)'
    $m=[regex]::Match($source,$regex)
    if (-not $m.Success) { throw ('Missing pure production function '+$name) }
    return $m.Value
}
$code=(Pure 'Sha' 'SafeId') + [Environment]::NewLine +
    (Pure 'SafeId' 'Assert-NoLink') + [Environment]::NewLine +
    (Pure 'Parse-DcTime' 'Assert-DcClock') + [Environment]::NewLine +
    (Pure 'Assert-DcClock' 'Get-Scope') + [Environment]::NewLine +
    (Pure 'Analyze' 'Read-PackageComparison')
# Guard-Plan ends at the top-level try, not another function.
$guard=[regex]::Match($source,'(?ms)^function Guard-Plan\b[^\r\n]*\{.*?(?=^try \{)')
if (-not $guard.Success) { throw 'Could not isolate production cleanup guard.' }
$code+=[Environment]::NewLine+$guard.Value
. ([scriptblock]::Create($code))
$expected=[datetime]::Parse('2026-11-07T17:00:00Z').ToUniversalTime()
if ((Parse-DcTime '20261107170000.0Z') -ne $expected -or
    (Parse-DcTime '20261107170000Z') -ne $expected) {
    throw 'AD RootDSE generalized UTC time decoded incorrectly.'
}
foreach($bad in @('20261107170000+0300','20261107170000.0+0200',
        '20261107170000garbage','20261307170000.0Z')) {
    $caught=$false
    try { $null=Parse-DcTime $bad } catch { $caught=$true }
    if (-not $caught) { throw ('Unsafe DC time parsed successfully: '+$bad) }
}
$tempRoot=Join-Path ([IO.Path]::GetTempPath()) ('lscm-sysvol-offline-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($tempRoot)
$scope=[pscustomobject]@{
    root=$tempRoot;domain='example.com';dc='dc01.example.com'
    dcUtc=[datetime]::UtcNow;skewSeconds=299;writable=$true
}
Assert-DcClock $scope
$scope.skewSeconds=301
$blocked=$false
try { Assert-DcClock $scope } catch { $blocked=$true }
if (-not $blocked) { throw 'More than 5 minutes clock drift was permitted.' }
$scope.skewSeconds=0
if (-not (SafeId 'campaign-20261108') -or (SafeId '..\Policies') -or
    (SafeId 'C:\Windows')) {
    throw 'Unsafe campaign ID was accepted.'
}
$utc=[datetime]::UtcNow
$hash='a'*64
$inventory=@(
    [pscustomobject]@{id='alpha';valid=$true
        fingerprint=('b'*64);endUtc=$utc.AddDays(-45).ToString('o')
        imageCount=1;imageBytes=100;slides=@([pscustomobject]@{hash=$hash;bytes=100})},
    [pscustomobject]@{id='beta';valid=$true
        fingerprint=('c'*64);endUtc=$utc.AddDays(-45).ToString('o')
        imageCount=1;imageBytes=100;slides=@([pscustomobject]@{hash=$hash;bytes=100})}
)
$gpos=@([pscustomobject]@{name='Live reference';campaignIds=@('beta')})
$analysis=@(Analyze $inventory $gpos $utc)
if ($analysis.Count -ne 2 -or
    $analysis[0].duplicateImages -ne 0 -or
    $analysis[1].duplicateImages -ne 1 -or
    $analysis[1].duplicateBytes -ne 100 -or
    $analysis[0].cleanupEligible -ne $true -or
    $analysis[1].cleanupEligible -ne $false) {
    throw 'Duplicate SHA-256 accounting or referenced GPO protection failed.'
}
$plan=Guard-Plan $scope $inventory @() 'alpha' $utc.ToString('o')
if ($plan.campaignId -cne 'alpha' -or $plan.planHash.Length -ne 64) {
    throw 'Cleanup guard did not return a deterministic approval digest.'
}
$blocked=$false
try { $null=Guard-Plan $scope $inventory $gpos 'beta' $utc.ToString('o') }
catch { $blocked=$true }
if (-not $blocked) { throw 'GPO-referenced source was eligible for deletion.' }
$inventory[0].endUtc=$utc.AddDays(-29).ToString('o')
$blocked=$false
try { $null=Guard-Plan $scope $inventory @() 'alpha' $utc.ToString('o') }
catch { $blocked=$true }
if (-not $blocked) { throw '30-day cleanup grace period was bypassed.' }
$inventory[0].endUtc=$utc.AddDays(-45).ToString('o')
[void][IO.Directory]::CreateDirectory((Join-Path $tempRoot '.staging-offline-test'))
$blocked=$false
try { $null=Guard-Plan $scope $inventory @() 'alpha' $utc.ToString('o') }
catch { $blocked=$true }
if (-not $blocked) { throw 'Concurrent staging folder was ignored by cleanup guard.' }
[IO.Directory]::Delete($tempRoot,$true)
Write-Output 'PASS: embedded SYSVOL engine parses in Windows PowerShell 5.1; selected DC UTC clock, skew guard, SHA-256 duplicates, GPO references and expiry safeguards pass.'
Write-Output 'NOTE: No live domain, SYSVOL or cleanup operation was executed.'
