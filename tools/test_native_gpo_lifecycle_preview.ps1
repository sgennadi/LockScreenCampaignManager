#requires -Version 5.1
# Native C# Preview <-> real legacy PowerShell Plan SHA-256 contract.
# All data resides in an isolated temporary local folder. No AD or real
# SYSVOL access, PowerShell mocks, domain writes, or remote client actions.
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot '..\src\LockScreenCampaignManager\Assets\Gpo-Lifecycle.ps1'
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $scriptPath, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'Legacy GPO lifecycle engine has syntax errors.' }
foreach ($functionName in @('Hash', 'ReadPackage', 'Plan')) {
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
    $imageDir = Join-Path $fixture 'images'
    New-Item -ItemType Directory -Path $sourceDir -Force | Out-Null
    New-Item -ItemType Directory -Path $imageDir -Force | Out-Null

    $gptText = '[General]' + [Environment]::NewLine + 'Version=42' + [Environment]::NewLine
    [IO.File]::WriteAllText((Join-Path $fixture 'gpt.ini'), $gptText, [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText((Join-Path $fixture 'psscripts.ini'),
        'startup fixture', [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText((Join-Path $sourceDir 'campaign.json'),
        '{"campaignId":"alpha","endLocal":"2026-01-01T00:00:00","timeZoneId":"UTC"}',
        [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText((Join-Path $fixture 'LockScreen-Agent.ps1'),
        '# test agent', [Text.Encoding]::ASCII)
    $imageFile = Join-Path $imageDir '01.jpg'
    [IO.File]::WriteAllText($imageFile, 'image fixture', [Text.Encoding]::ASCII)
    $imageHash = (Get-FileHash -LiteralPath $imageFile -Algorithm SHA256).Hash
    $manifest = '{"campaignId":"new_campaign","schemaVersion":1,' +
        '"timeZoneId":"UTC","endLocal":"2027-01-01T00:00:00",' +
        '"slides":[{"fileName":"01.jpg","atLocal":"2026-12-01T00:00:00",' +
        '"sha256":"' + $imageHash + '"}]}'
    [IO.File]::WriteAllText((Join-Path $fixture 'campaign.json'),
        $manifest, [Text.Encoding]::ASCII)

    $guid = '01234567-89AB-CDEF-0123-456789ABCDEF'
    $gpoDn = 'CN={' + $guid + '},CN=Policies,CN=System,DC=example,DC=com'
    $ouDn = 'OU=Clients,DC=example,DC=com'
    $rawLink = '[LDAP://' + $gpoDn + ';0]'
    # The legacy Plan reads the path only in Reuse's no-destination check.
    # localhost keeps that read-only check entirely on the CI host.
    $info = [pscustomobject]@{
        dns='example.com'
        dc='localhost'
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

    $plan = Plan $info $state 'Disable' ''
    $expected = '4c552d75251cf09317b885ddd9136a39b0f09a1279f19db9cba9da92d305e1a8'
    if ($plan.hash -cne $expected -or $plan.action -cne 'Disable' -or
        $plan.gpoId -cne '01234567-89ab-cdef-0123-456789abcdef') {
        throw 'Legacy Disable Plan differs from native C# golden SHA-256.'
    }

    $reuse = Plan $info $state 'Reuse' $fixture
    $expectedReuse = '4ad6c02566f179a40d523f3f70577722969676e90daae01840731c18d60cf0c3'
    if ($reuse.hash -cne $expectedReuse -or $reuse.newCampaignId -cne 'new_campaign') {
        throw 'Legacy Reuse Plan differs from native C# golden SHA-256.'
    }

    $denied = $false
    try { $null = Plan $info $state 'Delete' '' }
    catch { $denied = $true }
    if (-not $denied) { throw 'Linked GPO deletion must be rejected.' }

    Write-Host 'Native GPO Lifecycle Preview vs legacy Apply Plan: PASS'
}
finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
}
