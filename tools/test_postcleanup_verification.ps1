#requires -Version 5.1
# Isolated Windows PowerShell test: never contacts AD, SMB or Remote Registry.
[CmdletBinding()]
param()
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$asset=Join-Path $repo 'src\LockScreenCampaignManager\Assets\Campaign-PushNow.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile(
    $asset,[ref]$tokens,[ref]$errors)
if(@($errors).Count -ne 0){throw 'Post-cleanup domain helper PowerShell syntax invalid.'}
$functions=@($ast.FindAll({
    param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]
},$true))
$matches=@($functions | Where-Object { $_.Name -ceq 'RemoteRegMatches' })
if($matches.Count -ne 1){throw 'Real production RemoteRegMatches helper unavailable.'}
. ([scriptblock]::Create($matches[0].Extent.Text))
function Assert([bool]$ok,[string]$name){
    if(-not $ok){throw ('FAIL: '+$name)}
    Write-Output ('PASS: '+$name)
}
$dw=[pscustomobject]@{Exists=$true;Kind='DWord';Value=0}
Assert (RemoteRegMatches $dw $dw) 'Remote HKLM zero DWORD is preserved, not treated as absent'
Assert (-not (RemoteRegMatches $dw ([pscustomobject]@{Exists=$false;Kind='DWord';Value=$null}))) 'Present DWORD cannot match an absent value'
Assert (-not (RemoteRegMatches $dw ([pscustomobject]@{Exists=$true;Kind='String';Value=0}))) 'Registry kind mismatch fails closed'
Assert (-not (RemoteRegMatches $dw ([pscustomobject]@{Exists=$true;Kind='DWord';Value=1}))) 'Registry value mismatch fails closed'
$none=[pscustomobject]@{Exists=$false;Kind='String';Value=$null}
Assert (RemoteRegMatches $none $none) 'Originally absent HKLM value is verified as absent'
$multi=[pscustomobject]@{Exists=$true;Kind='MultiString';Value=[string[]]@('a','b')}
Assert (RemoteRegMatches $multi $multi) 'Multistring registry values compare canonically'
Assert (-not (RemoteRegMatches $multi ([pscustomobject]@{Exists=$true;Kind='MultiString';Value=[string[]]@('b','a')}))) 'Ordered multi-values cannot silently change'
$source=[IO.File]::ReadAllText($asset)
$at=$source.IndexOf("if(" + '$Mode' + " -eq 'VerifyCleanup')", [StringComparison]::Ordinal)
if($at -lt 0){throw 'Manual read-only entrypoint missing'}
$end=$source.IndexOf([string][char]10 + '    $now=[TimeZoneInfo]', $at, [StringComparison]::Ordinal)
if($end -lt $at){throw 'Missing read-only verification branch terminator'}
$block=$source.Substring($at, $end-$at)
Assert (-not [regex]::IsMatch($block,'(?i)ScheduledNow|RegisterTaskDefinition|Set-Item|Remove-Item|Unregister-ScheduledTask|Write-JsonNew')) 'Read-only entrypoint never schedules remote work or writes to AD/client'
Assert ($block.Contains('Read-RemoteCleanup') -and $block.Contains('exit 0')) 'Remote observation returns before any existing remote task execution path'
