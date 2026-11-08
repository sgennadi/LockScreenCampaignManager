#requires -Version 5.1
# Offline tests: parse production scanner and execute selected pure functions only.
# No live domain, remote registry, GPO writes, SYSVOL or client RPC is accessed.
[CmdletBinding()]
param()
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$file=Join-Path $repo 'src\LockScreenCampaignManager\Assets\Gpo-ConflictScan.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile(
    $file,[ref]$tokens,[ref]$errors)
if(@($errors).Count -ne 0){
    foreach($error in $errors){Write-Error ("Parse line {0}: {1}" -f $error.Extent.StartLineNumber,$error.Message)}
    throw 'Production GPO candidate analyzer is not valid Windows PowerShell 5.1.'
}
$methods=@($ast.FindAll({
    param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]
},$true))
foreach($name in @('Escape-Ldap','Is-InsideDn','Safe-ComputerFqdn','Decide-Link','ReadLinks')){
    $fn=@($methods|Where-Object {$_.Name -ceq $name})
    if($fn.Count -ne 1){throw ('Missing production pure GPO helper: '+$name)}
    . ([scriptblock]::Create($fn[0].Extent.Text))
}
function Check([bool]$ok,[string]$name){
    if(-not $ok){throw ('FAIL: '+$name)}
    Write-Output ('PASS: '+$name)
}
Check (Safe-ComputerFqdn 'PC01.example.com' 'example.com') 'Domain PC FQDN allowed'
Check (-not (Safe-ComputerFqdn 'PC01.foreign.com' 'example.com')) 'Cross-domain computer rejected'
Check (-not (Safe-ComputerFqdn 'PC01.example.com.attacker.net' 'example.com')) 'Spoofed domain suffix rejected'
Check (-not (Safe-ComputerFqdn '-PC01.example.com' 'example.com')) 'Unsafe DNS labels rejected'
Check (-not (Safe-ComputerFqdn 'PC01..example.com' 'example.com')) 'Double-dot hostname rejected'
Check ((Escape-Ldap 'PC*(3)\') -ceq 'PC\2a\283\29\5c') 'LDAP filter metacharacters escaped'
$base='DC=example,DC=com'
Check (Is-InsideDn 'OU=Windows11,OU=Clients,DC=example,DC=com' $base) 'Nested OU stays inside target AD domain'
Check (-not (Is-InsideDn 'OU=Clients,DC=example,DC=com.evil' $base)) 'Foreign DN suffix rejected'
$sample='[LDAP://CN={11111111-1111-1111-1111-111111111111},CN=Policies,CN=System,DC=example,DC=com;0]' +
    '[LDAP://CN={22222222-2222-2222-2222-222222222222},CN=Policies,CN=System,DC=example,DC=com;2]' +
    '[LDAP://CN={33333333-3333-3333-3333-333333333333},CN=Policies,CN=System,DC=example,DC=com;0]'
$links=@(ReadLinks $sample)
Check ($links.Count -eq 3 -and $links[0].order -eq 3 -and
    $links[1].order -eq 2 -and $links[2].order -eq 1) 'GPMC link order rightmost has priority 1'
Check ((Decide-Link $true $true $true $false) -ceq 'Candidate') 'Enforced GPO survives Block Inheritance'
Check ((Decide-Link $true $false $true $false) -ceq 'Blocked inheritance') 'Non-enforced GPO blocked by descendant OU'
Check ((Decide-Link $false $true $false $false) -ceq 'Disabled link') 'Disabled link remains excluded even if Enforced'
Check ((Decide-Link $true $true $false $true) -ceq 'Computer configuration disabled') 'Computer-disabled GPO is not applied'
$source=[IO.File]::ReadAllText($file)
Check ($source.Contains("if(" + '$Mode' + " -eq 'Effective')") -and
    $source.Contains('Effective-Candidates $info $req')) 'Effective operation is explicit and separate from legacy Scan'
Check ($source.Contains("memberOf:1.2.840.113556.1.4.1941:=") -and
    $source.Contains('tokenGroups') -and $source.Contains('GetAccessRules') -and
    $source.Contains('gPCWQLFilter') -and $source.Contains('CN=Sites,')) 'Real script covers nested membership, token, ACL, WMI and site candidates'
Check ($source.Contains('Site applicability unknown') -and
    $source.Contains('Needs RSoP / verification') -and
    $source.Contains('Site GPO inventory incomplete')) 'Uncertain GPO evidence cannot be silently omitted'
Check ($source.Contains('$actualContainer=$computer.dn.Substring') -and
    $source.Contains('actualComputerContainer=$actualContainer') -and
    $source.Contains('scopeDepth=$scopeDepth') -and
    $source.Contains('linkOrder=[int]$link.order')) 'Winning policy analyzer uses actual AD computer OU and explicit GPMC order'

Check ($source.Contains("[ValidateSet('Scan','Effective','VerifyEndpoint','VerifyEvidence')]") -and
    $source.Contains("if(" + '$Mode' + " -eq 'VerifyEndpoint'") -and
    $source.Contains('$verified=Effective-Candidates $info $req') -and
    $source.Contains('Observe-RemoteLockScreen ([string]$verified.computer)')) 'Manual endpoint read always revalidates managed GPO, domain OU and computer group first'
Check ($source.Contains('[Microsoft.Win32.RegistryView]::Registry64') -and
    $source.Contains('[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames') -and
    $source.Contains('registryStatus=') -and
    $source.Contains('LockScreenImagePath') -and
    $source.Contains('LockScreenImageStatus')) 'Endpoint observer captures native 64-bit HKLM + MDM CSP with explicit availability'
Check ($source.Contains('function Observe-RsopSettingSource') -and
    $source.Contains('Get-WmiObject -ComputerName $computer') -and
    $source.Contains('RSOP_RegistryPolicySetting') -and
    $source.Contains('Observe-RsopSettingSource ([string]$verified.computer)')) 'Cached RSoP CSE query is read-only and AD-scoped'
Check ($source.Contains('function Observe-ImageIntegrity') -and
    $source.Contains('Get-FileHash -LiteralPath $current') -and
    $source.Contains('ReparsePointNotAllowed') -and
    $source.Contains('Observe-ImageIntegrity $info')) 'SMB JPEG hash is a bounded read-only verification'

Check (-not $source.Contains('CreateSubKey(') -and
    -not $source.Contains('.SetValue(') -and
    -not $source.Contains('Start-Service') -and
    -not $source.Contains('Enable-PSRemoting')) 'Endpoint scanner does not create keys or start remote services'

$commands=@($ast.FindAll({
    param($node) $node -is [Management.Automation.Language.CommandAst]
},$true) | ForEach-Object {$_.GetCommandName()})
foreach($forbidden in @('Set-ADObject','Set-ADComputer','Set-GPRegistryValue',
    'Set-GPLink','Set-GPPermission','New-GPO','Remove-GPO','Set-ItemProperty',
    'Remove-Item','Register-ScheduledTask','Invoke-Command','Copy-Item','Move-Item')){
    Check (-not ($commands -contains $forbidden)) ('GPO scanner has no mutating command: '+$forbidden)
}
