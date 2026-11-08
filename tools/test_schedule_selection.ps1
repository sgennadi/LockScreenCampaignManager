# Offline Windows PowerShell 5.1 regression test. Extract and execute the
# exact permutation and slot selector from the production agent without
# sourcing its initialization, touching HKLM, AD, GPO, Tasks or RPC.
$ErrorActionPreference = 'Stop'
$agent = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\LockScreenCampaignManager\Assets\LockScreen-Agent.ps1') -Raw
$permutation = [regex]::Match($agent,
    '(?ms)^\$ResolvedSlotImages = @\(\$Slides\)\r?\n.*?(?=^# Client feedback)')
$selector = [regex]::Match($agent,
    '(?ms)^function Get-ExpectedSlide \{.*?^\}\r?\n(?=function Get-ImagePath \{)')
if (-not $permutation.Success -or -not $selector.Success) {
    throw 'The production agent permutation or slot selector could not be isolated.'
}
$doPermute = [scriptblock]::Create($permutation.Value)
. ([scriptblock]::Create($selector.Value))
$CampaignId = 'fixture-images-202611081400'
$Culture = [Globalization.CultureInfo]::InvariantCulture
$DateFormat = 'yyyy-MM-ddTHH:mm:ss'
$CampaignEnd = [datetime]'2026-11-20T14:00:00'
$Slides = @(1..8 | ForEach-Object {
    [pscustomobject]@{
        fileName=('{0:D2}.jpg' -f $_)
        atLocal=([datetime]'2026-11-08T14:00:00').AddDays($_ - 1).ToString($DateFormat)
    }
})
$Config = [pscustomobject]@{randomizePerDevice=$true}
$oldHost = $env:COMPUTERNAME
function Resolve-Sample([string]$computer) {
    $env:COMPUTERNAME = $computer
    . $doPermute
    return @($ResolvedSlotImages | ForEach-Object { [string]$_.fileName })
}
try {
    $first = @(Resolve-Sample 'TESTPC01')
    $again = @(Resolve-Sample 'TESTPC01')
    $other = @(Resolve-Sample 'TESTPC02')
    if ($first.Count -ne 8 -or ($first -join ',') -ne ($again -join ',')) {
        throw 'Randomized image mapping changed between the same device runs.'
    }
    if (($first | Sort-Object) -join ',' -ne
        ((@($Slides | ForEach-Object { $_.fileName }) | Sort-Object) -join ',')) {
        throw 'The shuffled image mapping did not preserve the full verified image set.'
    }
    if (($first -join ',') -eq ($other -join ',')) {
        throw 'Two sample machines received an identical mapping; investigate shuffle seed.'
    }
    $Config.randomizePerDevice = $false
    $plain = @(Resolve-Sample 'TESTPC01')
    if (($plain -join ',') -ne ((@($Slides | ForEach-Object { $_.fileName })) -join ',')) {
        throw 'Legacy fixed image order was changed by the optional shuffle implementation.'
    }
    $Config.randomizePerDevice = 'true'
    $rejected = $false
    try { $null = Resolve-Sample 'TESTPC01' } catch { $rejected = $true }
    if (-not $rejected) { throw 'Malformed shuffle option was not rejected.' }

    $Config.randomizePerDevice = $true
    $env:COMPUTERNAME = 'TESTPC01'
    . $doPermute
    function Get-NowInCampaignZone { return $script:SampleNow }
    $script:SampleNow = [datetime]'2026-11-07T23:59:59'
    if ($null -ne (Get-ExpectedSlide)) { throw 'Image applied before the first scheduled slot.' }
    $script:SampleNow = [datetime]'2026-11-10T14:00:00'
    $expected = Get-ExpectedSlide
    if ([string]$expected.fileName -ne $first[2]) {
        throw 'Third scheduled slot did not resolve the stable third image.'
    }
    $script:SampleNow = $CampaignEnd
    if ($null -ne (Get-ExpectedSlide)) {
        throw 'Image was selected after campaign completion.'
    }
    Write-Output 'PASS: fixed versus stable randomized per-device image order, date slots and malformed flag (production agent code).'
}
finally {
    $env:COMPUTERNAME = $oldHost
}
