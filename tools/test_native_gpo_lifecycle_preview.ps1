#requires -Version 5.1
# Cross-engine regression: extract the real legacy Hash and Plan functions.
# Mock only filesystem reads and package construction. No AD, SYSVOL, GPO
# or remote computer is accessed. The script makes no domain changes.
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot '..\src\LockScreenCampaignManager\Assets\Gpo-Lifecycle.ps1'
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $scriptPath, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'Legacy GPO lifecycle engine has syntax errors.' }
foreach ($functionName in @('Hash', 'Plan')) {
    $function = @($ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq $functionName
    }, $true))
    if ($function.Count -ne 1) {
        throw ('Missing/duplicate legacy function: ' + $functionName)
    }
    . ([scriptblock]::Create($function[0].Extent.Text))
}

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('LSCM-Lifecycle-Preview-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
try {
    $sourceDir = Join-Path $fixture 'previous'
    New-Item -ItemType Directory -Path $sourceDir -Force | Out-Null
    $gptText = '[General]' + [Environment]::NewLine + 'Version=42' + [Environment]::NewLine
    [IO.File]::WriteAllText((Join-Path $fixture 'gpt.ini'), $gptText, [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText((Join-Path $sourceDir 'campaign.json'),
        '{"campaignId":"alpha","endLocal":"2026-01-01T00:00:00","timeZoneId":"UTC"}',
        [Text.UTF8Encoding]::new($false))

    $guid = '01234567-89AB-CDEF-0123-456789ABCDEF'
    $gpoDn = ('CN={'+$guid+'},CN=Policies,CN=System,DC=example,DC=com')
    $ouDn = 'OU=Clients,DC=example,DC=com'
    $rawLink = '[LDAP://'+$gpoDn+';0]'
    $info = [pscustomobject]@{
        dns='example.com'
        dc='dc01.example.com'
        dcUtc=[datetime]::ParseExact('20261108180000','yyyyMMddHHmmss',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal -bor
            [Globalization.DateTimeStyles]::AdjustToUniversal)
        clockSkewSeconds=0
    }
    $state = [pscustomobject]@{
        gpo=[pscustomobject]@{dn=$gpoDn;name='Managed fixture';version=[int64]42;flags=0}
        managed=[pscustomobject]@{
            id='01234567-89ab-cdef-0123-456789abcdef'
            campaignId='alpha'
            ini=(Join-Path $fixture 'psscripts.ini')
            gpt=$fixture
            source=$sourceDir
        }
        ouDn=$ouDn
        gplink=$rawLink
        link=[pscustomobject]@{flags=0;text=$rawLink;dn=$gpoDn}
        links=@([pscustomobject]@{ou=$ouDn;flags=0;enabled=$true;enforced=$false})
    }

    $global:LifecyclePreviewFixtureRoot = $fixture
    # Mock these external dependencies only. Production Plan and Hash
    # above run unmodified, including their original SHA-256 preimage.
    function global:Get-FileHash {
        param([string]$LiteralPath,[string]$Algorithm)
        $leaf = Split-Path -Path $LiteralPath -Leaf
        if ($leaf -eq 'campaign.json') {
            return [pscustomobject]@{Hash=('B'*64)}
        }
        if ($leaf -eq 'LockScreen-Agent.ps1') {
            return [pscustomobject]@{Hash=('C'*64)}
        }
        return [pscustomobject]@{Hash=('A'*64)}
    }
    function global:Test-Path {
        param([string]$LiteralPath)
        return ($LiteralPath -eq (Join-Path $global:LifecyclePreviewFixtureRoot 'gpt.ini'))
    }
    function global:ReadPackage {
        param([string]$folder,[datetime]$dcUtc)
        return [pscustomobject]@{id='new_campaign'}
    }

    $plan=Plan $info $state 'Disable' ''
    $expected='59909b422c6b7158ab5d0a54ebc569243d70d5b4f8025930271f4e5d929c69e2'
    if ($plan.hash -cne $expected -or $plan.action -cne 'Disable' -or
        $plan.gpoId -cne '01234567-89ab-cdef-0123-456789abcdef') {
        throw 'Native Disable golden hash diverges from the real legacy Plan function.'
    }
    $reuse=Plan $info $state 'Reuse' $fixture
    $expectedReuse='a84a855068f2db247778dee5515b53913d801e9c03da8f52fb173df8ed89e981'
    if ($reuse.hash -cne $expectedReuse -or $reuse.newCampaignId -cne 'new_campaign') {
        throw 'Native Reuse golden hash diverges from the real legacy Plan function.'
    }
    $state.links=@([pscustomobject]@{ou=$ouDn;enabled=$true})
    $denied=$false
    try { $null=Plan $info $state 'Delete' '' }
    catch { $denied=$true }
    if (-not $denied) { throw 'Legacy GPO deletion preview must reject linked policies.' }

    Write-Host 'Native GPO lifecycle Preview -> legacy Apply hash compatibility: PASS'
}
finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
}
