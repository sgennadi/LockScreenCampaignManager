# Windows PowerShell 5.1 contract test. Does not access AD or contact any computer.
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot '..\src\LockScreenCampaignManager\Assets\Campaign-PushNow.ps1'
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if(@($errors).Count -gt 0){throw ($errors | Out-String)}
$functions=@($ast.FindAll({param($x) $x -is [Management.Automation.Language.FunctionDefinitionAst]},$true))
$matches=@($functions|Where-Object{$_.Name -eq 'Is-TemporaryOffline'})
if($matches.Count -ne 1){throw 'Expected exactly one offline classifier'}
Invoke-Expression $matches[0].Extent.Text
function Check([bool]$value,[string]$label){if(-not $value){throw "FAIL: $label"};Write-Host "PASS: $label"}
$offline=[Runtime.InteropServices.COMException]::new('RPC server unavailable',-2147023174) # 0x800706BA
$access=[Runtime.InteropServices.COMException]::new('Access denied',-2147024891) # 0x80070005
$other=[InvalidOperationException]::new('Campaign manifest rejected')
$unknown=[TimeoutException]::new('Remote task result unknown')
Check (Is-TemporaryOffline $offline) 'RPC offline classified for delayed repair'
Check (-not (Is-TemporaryOffline $access)) 'Access denied NEVER auto retried'
Check (-not (Is-TemporaryOffline $other)) 'Campaign mismatch NEVER auto retried'
Check (-not (Is-TemporaryOffline $unknown)) 'Unknown task state NEVER duplicated'
$wrapper=[InvalidOperationException]::new('Wrapped RPC', $offline)
Check (Is-TemporaryOffline $wrapper) 'Nested RPC failure recognized'
Write-Host 'PASS: Deferred repair classification tests'
