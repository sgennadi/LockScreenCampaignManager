#requires -Version 5.1
# Pure offline fixtures: no GPO/AD/SMB/network operations.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'smb_canary_rsop.ps1')
$guid=[Guid]::Parse('12345678-1111-4222-8333-abcdef123456')
$other=[Guid]::Parse('12345678-1111-4222-8333-abcdef000000')
function Assert([bool]$condition,[string]$message) {
    if (-not $condition) { throw ('RSOP contract failed: '+$message) }
}
function New-Fixture([string]$enabled='true',[string]$valid='true',
    [string]$filter='true',[string]$denied='false',[string]$pathGuid='',
    [bool]$computer=$true,[bool]$duplicate=$false,[bool]$onlyUser=$false,
    [bool]$missingFilter=$false,[bool]$spoofName=$false) {
    if ([string]::IsNullOrEmpty($pathGuid)) { $pathGuid=$guid.ToString('D') }
    $path='LDAP://CN={'+$pathGuid+'},CN=Policies,CN=System,DC=example,DC=com'
    $filterNode=if($missingFilter){''}else{'<FilterAllowed>'+$filter+'</FilterAllowed>'}
    $name=if($spoofName){'Fake '+$guid.ToString('D')}else{'Lab policy'}
    $entry='<GPO><Name>'+$name+'</Name><Path>'+$path+'</Path>'+
        '<Enabled>'+$enabled+'</Enabled><IsValid>'+$valid+'</IsValid>'+
        $filterNode+'<AccessDenied>'+$denied+'</AccessDenied></GPO>'
    $second=if($duplicate){$entry}else{''}
    $user=if($onlyUser){'<UserResults>'+$entry+'</UserResults>'}else{''}
    $pc=if($computer -and -not $onlyUser){
        '<ComputerResults>'+$entry+$second+'</ComputerResults>'
    }else{''}
    return [xml]('<Rsop xmlns="http://www.microsoft.com/GroupPolicy/Rsop">'+
        $pc+$user+'</Rsop>')
}
$ok=Get-LscmComputerGpoEvidence -Report (New-Fixture) -ExpectedGpoId $guid
Assert ($ok.applied -and $ok.status -eq 'AppliedComputerGpo') 'expected enabled computer GPO'
foreach($args in @(
    @{enabled='false'}, @{valid='false'}, @{filter='false'}, @{denied='true'}
)) {
    $failed=Get-LscmComputerGpoEvidence -Report (New-Fixture @args) -ExpectedGpoId $guid
    Assert (-not $failed.applied -and $failed.matched -and
        $failed.status -eq 'GpoFilteredOrDisabled') 'blocked/disabled GPO'
}
$unknown=Get-LscmComputerGpoEvidence -Report (New-Fixture -missingFilter $true) -ExpectedGpoId $guid
Assert (-not $unknown.applied -and $unknown.status -eq 'IncompleteGpoEvidence') 'missing flags must fail'
$wrong=Get-LscmComputerGpoEvidence -Report (New-Fixture -pathGuid ($other.ToString('D')) -spoofName $true) -ExpectedGpoId $guid
Assert (-not $wrong.applied -and $wrong.status -eq 'NotApplied') 'ignore fake name GUID'
$user=Get-LscmComputerGpoEvidence -Report (New-Fixture -onlyUser $true) -ExpectedGpoId $guid
Assert (-not $user.applied -and $user.status -eq 'RsopUnavailable') 'never match user-only GPO'
$noComputer=Get-LscmComputerGpoEvidence -Report (New-Fixture -computer $false) -ExpectedGpoId $guid
Assert (-not $noComputer.applied -and $noComputer.status -eq 'RsopUnavailable') 'missing computer evidence'
$duplicate=Get-LscmComputerGpoEvidence -Report (New-Fixture -duplicate $true) -ExpectedGpoId $guid
Assert (-not $duplicate.applied -and $duplicate.status -eq 'AmbiguousGpoIdentity') 'duplicate entries must fail closed'
Write-Host 'PASS: exact Computer GPO GUID GPRESULT XML fixtures, including spoofing and filtering.'
