#requires -Version 5.1
# CI-only offline pure checks of the production DC audit functions.
# No domain connections, no GPO/RPC activity, no SYSVOL writes.
[CmdletBinding()]
param()
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$source=Join-Path $repo 'src\LockScreenCampaignManager\Assets\Dc-Consistency.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile(
    $source,[ref]$tokens,[ref]$errors)
if(@($errors).Count -ne 0){throw 'Production DC consistency script fails PowerShell 5.1 parse.'}
$functions=@($ast.FindAll({
    param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]
},$true))
foreach($name in @('ValidHost','Parse-DcUtc','Require-RegularFile',
                   'Require-RegularDirectory','FileDigest')){
    $matches=@($functions | Where-Object {$_.Name -ceq $name})
    if($matches.Count -ne 1){throw ('Missing production audit helper: '+$name)}
    . ([scriptblock]::Create($matches[0].Extent.Text))
}
function Check([bool]$ok,[string]$name){
    if(-not $ok){throw ('FAIL: '+$name)}
    Write-Output ('PASS: '+$name)
}
Check (ValidHost 'dc01.example.com' 'example.com') 'Valid discovered FQDN accepted'
Check (-not (ValidHost 'dc01.other.net' 'example.com')) 'Cross-domain DC refused'
Check (-not (ValidHost 'dc01.example.com.evil.net' 'example.com')) 'Suffix spoofed FQDN refused'
Check (-not (ValidHost '-dc.example.com' 'example.com')) 'Invalid leading hyphen DC refused'
Check (-not (ValidHost 'dc01..example.com' 'example.com')) 'Invalid double-dot DC refused'
$parsed=Parse-DcUtc '20261107190718.0Z'
Check ($parsed.Kind -eq 'Utc' -and
    $parsed.ToString('yyyy-MM-ddTHH:mm:ssZ') -ceq '2026-11-07T19:07:18Z') 'DC generalized UTC precision is parsed without local-time shift'
$wrongTime=$false
try { [void](Parse-DcUtc '20261107210718+0200') }catch {$wrongTime=$true}
Check $wrongTime 'Non-UTC AD timestamps rejected'
$sourceText=[IO.File]::ReadAllText($source)
Check ($sourceText.Contains("RootDSE") -and
    $sourceText.Contains("currentTime") -and
    $sourceText.Contains("versionNumber") -and
    $sourceText.Contains("gpt.ini") -and
    $sourceText.Contains("psscripts.ini")) 'Real production script checks DC time, AD/GPT version and startup'
Check ($sourceText.Contains("Get-FileHash") -and
    $sourceText.Contains("agentSha256") -and
    $sourceText.Contains("ReadOnly") -eq $false) 'Production hash verification does not use a mutable fixture'
$forbidden=@('Set-ADObject','Set-GP','New-GPO','Remove-GPO',
    'Copy-Item','Move-Item','Remove-Item','Set-ItemProperty',
    'Register-ScheduledTask','Unregister-ScheduledTask','Restart-Service')
$commands=@($ast.FindAll({
    param($node) $node -is [Management.Automation.Language.CommandAst]
},$true) | ForEach-Object {$_.GetCommandName()})
foreach($name in $forbidden){
    Check (-not ($commands -contains $name)) ('Read-only script has no '+$name+' command')
}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('LSCM-DC-AUDIT-'+[guid]::NewGuid().ToString('N'))
[void](New-Item -Path $temp -ItemType Directory)
try {
    $file=Join-Path $temp '01.jpg'
    [IO.File]::WriteAllBytes($file,([Text.Encoding]::UTF8.GetBytes('Offline fixture')))
    $hash=FileDigest $file 100
    Check ($hash.sha256 -ceq
        (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()) 'Production FileDigest calculates stable SHA-256'
    $lengthBlocked=$false
    try {[void](FileDigest $file 3)}catch{$lengthBlocked=$true}
    Check $lengthBlocked 'Oversized file fails closed during DC inspection'
}finally{
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
