# Offline Windows PowerShell 5.1 regression tests for the embedded
# computer-group assignment engine. No directory connections or writes.
$ErrorActionPreference = 'Stop'
$asset = Join-Path $PSScriptRoot '..\src\LockScreenCampaignManager\Assets\Computer-GroupAssignment.ps1'
$source = Get-Content -LiteralPath $asset -Raw -Encoding UTF8
$tokens = $null
$parseErrors = $null
[void][Management.Automation.Language.Parser]::ParseFile($asset,
    [ref]$tokens, [ref]$parseErrors)
if (@($parseErrors).Count -ne 0) {
    throw ('Embedded AD group script has parse errors: ' +
        ((@($parseErrors) | ForEach-Object {
            'line ' + $_.Extent.StartLineNumber + ' / ' + $_.Message +
            ' / ' + $_.Extent.Text
        }) -join '; '))
}
$required = @(
    'Signing', 'Sealing', "ValidateSet('Search','Preview','Apply')",
    'Get-Scope', 'Get-Computer', 'Has-EffectiveMembership',
    'Convert-AdUsn', 'Assert-AllowedGroupSid',
    'primaryGroupID', 'GroupRid', 'Is-PrimaryGroup',
    'Get-Plan', 'planHash', 'approveSam', 'approveGroupDn',
    'GroupUsn', 'GroupGuid', 'previewUtc', "Invoke('Add'",
    "Invoke('Remove'", "Protected or critical AD groups"
)
foreach($item in $required) {
    if (-not $source.Contains($item)) {
        throw ("Missing required group membership safety check: " + $item)
    }
}
function Load-Pure([string]$name, [string]$next) {
    # Production functions take explicit parameter lists before the open brace.
    $pattern='(?ms)^function '+[regex]::Escape($name)+
        '\b[^\r\n]*\{.*?(?=^function '+[regex]::Escape($next)+'\b)'
    $match=[regex]::Match($source,$pattern)
    if (-not $match.Success) {
        throw ('Could not extract the production helper function: '+$name)
    }
    return $match.Value
}
$functions=(Load-Pure 'IsInside' 'Assert-Class') +
    [Environment]::NewLine + (Load-Pure 'Escape-Filter' 'Sha256') +
    [Environment]::NewLine + (Load-Pure 'Sha256' 'Convert-AdUsn') +
    [Environment]::NewLine + (Load-Pure 'Convert-AdUsn' 'Assert-AllowedGroupSid') +
    [Environment]::NewLine + (Load-Pure 'Assert-AllowedGroupSid' 'Get-Scope') +
    [Environment]::NewLine + (Load-Pure 'Is-PrimaryGroup' 'Get-Plan')
. ([scriptblock]::Create($functions))
$root = 'DC=example,DC=com'
$ou = 'OU=Clients,DC=example,DC=com'
if (-not (IsInside 'CN=PC01,OU=Clients,DC=example,DC=com' $ou)) {
    throw 'Valid computer DN was excluded from the selected OU.'
}
if (IsInside 'CN=PC02,OU=Other,DC=example,DC=com' $ou) {
    throw 'Computer outside selected OU was incorrectly authorized.'
}
if (IsInside 'CN=PC03,OU=Clients,DC=another,DC=com' $ou) {
    throw 'Cross-domain DN was incorrectly authorized.'
}
$escaped = Escape-Filter 'x*)(memberOf=*)\'
if (-not $escaped.Contains('\2a') -or -not $escaped.Contains('\28') -or
    -not $escaped.Contains('\29') -or -not $escaped.Contains('\5c')) {
    throw 'LDAP search input was not escaped against filter injection.'
}
$a=Sha256 'group-preview|pc01'
$b=Sha256 'group-preview|pc01'
$c=Sha256 'group-preview|pc02'
if ($a.Length -ne 64 -or $a -ne $b -or $a -eq $c) {
    throw 'Preview digest is not a stable SHA-256 digest.'
}
if ((Convert-AdUsn ([long]4294967297)) -ne '4294967297') {
    throw 'Int64 AD group uSNChanged was not normalized correctly.'
}
if ((Convert-AdUsn ([pscustomobject]@{HighPart=1;LowPart=15})) -ne '4294967311') {
    throw 'ADSI IADsLargeInteger high/low representation was not normalized correctly.'
}
if ((Convert-AdUsn ([pscustomobject]@{HighPart=0;LowPart=-1})) -ne '4294967295') {
    throw 'ADSI IADsLargeInteger signed low part was not normalized correctly.'
}
$usnRejected = $false
try { $null = Convert-AdUsn ([pscustomobject]@{Unknown=1}) }
catch { $usnRejected = $true }
if (-not $usnRejected) { throw 'Unverifiable AD group USN was accepted.' }
function SidBytes([string]$value) {
    $sid = New-Object Security.Principal.SecurityIdentifier($value)
    $bytes = New-Object byte[] ($sid.BinaryLength)
    $sid.GetBinaryForm($bytes,0)
    return ,$bytes
}
$customSid='S-1-5-21-111111111-222222222-333333333-1200'
if ((Assert-AllowedGroupSid (SidBytes $customSid)) -ne $customSid) {
    throw 'Non-privileged custom security group SID was incorrectly rejected.'
}
$builtinRejected=$false
try { $null=Assert-AllowedGroupSid (SidBytes 'S-1-5-21-111111111-222222222-333333333-512') }
catch { $builtinRejected=$true }
if (-not $builtinRejected) { throw 'Renamed Domain Admins SID (RID 512) was accepted.' }
if (-not (Is-PrimaryGroup 1200 1200) -or (Is-PrimaryGroup 515 1200) -or
    (Is-PrimaryGroup 0 1200) -or (Is-PrimaryGroup 1200 0)) {
    throw 'Computer primaryGroupID is not compared safely with the selected group RID.'
}
if (-not $source.Contains("Selected group is the computer primary group;") -or
    -not $source.Contains('primaryGroupID') -or
    -not $source.Contains('$beforeWrite.PrimaryGroupRid -ne $plan.primaryGroupRid')) {
    throw 'Primary group must be visible in Search and blocked in Preview/Apply.'
}
Write-Output 'PASS: AD group engine parses in Windows PowerShell 5.1; OU/filter/hash, ADSI USN and protected group SID checks passed.'
Write-Output 'NOTE: No live AD group membership write was attempted; require a controlled OU/group pilot.'
