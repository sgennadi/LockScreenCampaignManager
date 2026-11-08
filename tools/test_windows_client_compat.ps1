#requires -Version 5.1
<#
.SYNOPSIS
    Offline Windows 10/11 client OS edition/build gate validation.
.DESCRIPTION
    Executes the production Assert-SupportedWindows function extracted from
    LockScreen-Agent.ps1. Only the OS registry query is mocked. Never writes
    HKLM, deploys GPO, starts scheduled tasks, or connects to a domain.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$agent=Join-Path $repo 'src\LockScreenCampaignManager\Assets\LockScreen-Agent.ps1'
$tokens=$null
$parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile(
    $agent,[ref]$tokens,[ref]$parseErrors)
if (@($parseErrors).Count -ne 0) { throw 'Production agent PowerShell syntax failed.' }
$functions=@($ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Assert-SupportedWindows'
},$true))
if ($functions.Count -ne 1) {
    throw 'Production Assert-SupportedWindows function missing or duplicated.'
}
. ([scriptblock]::Create($functions[0].Extent.Text))
$script:MockBuild=''
$script:MockEdition=''
function Get-ItemProperty {
    param([string]$Path)
    if ($Path -cne 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion') {
        throw 'Unexpected registry read during OS compatibility test.'
    }
    return [pscustomobject]@{
        CurrentBuildNumber=$script:MockBuild
        EditionID=$script:MockEdition
    }
}
$cases=@(
    @{ Build='14393'; Edition='EnterpriseS'; Allowed=$true; Name='Windows 10 Enterprise LTSB 2016' },
    @{ Build='17763'; Edition='EnterpriseS'; Allowed=$true; Name='Windows 10 Enterprise LTSC 2019' },
    @{ Build='19044'; Edition='IoTEnterpriseS'; Allowed=$true; Name='Windows 10 IoT Enterprise LTSC 2021' },
    @{ Build='19045'; Edition='Enterprise'; Allowed=$true; Name='Windows 10 Enterprise 22H2' },
    @{ Build='19045'; Edition='Education'; Allowed=$true; Name='Windows 10 Education 22H2' },
    @{ Build='22000'; Edition='Enterprise'; Allowed=$true; Name='Windows 11 Enterprise' },
    @{ Build='26100'; Edition='Education'; Allowed=$true; Name='Windows 11 Education' },
    @{ Build='26100'; Edition='IoTEnterprise'; Allowed=$true; Name='Windows 11 IoT Enterprise' },
    @{ Build='19045'; Edition='Professional'; Allowed=$false; Name='Windows 10 Pro (GPO unsupported)' },
    @{ Build='26100'; Edition='Professional'; Allowed=$false; Name='Windows 11 Pro (GPO unsupported)' },
    @{ Build='19045'; Edition='Core'; Allowed=$false; Name='Windows 10 Home' },
    @{ Build='10586'; Edition='Enterprise'; Allowed=$false; Name='Windows 10 1511 below policy baseline' },
    @{ Build='invalid'; Edition='Enterprise'; Allowed=$false; Name='Invalid build metadata' },
    @{ Build='19045'; Edition='ServerStandard'; Allowed=$false; Name='Server edition not implicitly supported' }
)
foreach ($case in $cases) {
    $script:MockBuild=$case.Build
    $script:MockEdition=$case.Edition
    $accepted=$true
    try { Assert-SupportedWindows }
    catch { $accepted=$false }
    if ($accepted -ne [bool]$case.Allowed) {
        throw ('FAIL: {0} - expected={1} actual={2}' -f
            $case.Name,$case.Allowed,$accepted)
    }
    Write-Output ('PASS: {0}' -f $case.Name)
}
Write-Output ('PASS: {0} Windows 10/11 production agent OS-gate fixtures' -f $cases.Count)
