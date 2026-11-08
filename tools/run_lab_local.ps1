#requires -Version 5.1
# Offline manual controller. Run from an elevated domain-joined Windows workstation.
# Reports remain local; no GitHub runner, tokens, or credentials required.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Preflight','Full')][string]$Mode,
    [string]$ConfigPath
)
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
$kitRoot=Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath=Join-Path $kitRoot 'lab.config.json'
}
if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
    throw ('Missing configuration '+$ConfigPath+'. Copy lab.config.sample.json to lab.config.json and edit.')
}
$cfg=Get-Content -LiteralPath $ConfigPath -Encoding UTF8 -Raw | ConvertFrom-Json
foreach($key in @('domain','dc','ouDn','groupDn','clients')) {
    if ($cfg.PSObject.Properties.Name -notcontains $key) {
        throw ('Missing configuration field: '+$key)
    }
}
$domain=([string]$cfg.domain).Trim().ToLowerInvariant()
$dc=([string]$cfg.dc).Trim()
$ouDn=([string]$cfg.ouDn).Trim()
$groupDn=([string]$cfg.groupDn).Trim()
$clients=@($cfg.clients | ForEach-Object { ([string]$_).Trim().ToUpperInvariant() } | Where-Object { $_.Length -gt 0 })
if ($clients.Count -lt 1 -or $clients.Count -gt 3 -or @($clients | Select-Object -Unique).Count -ne $clients.Count) {
    throw 'Use exactly 1-3 distinct disposable Windows 11 PCs.'
}
if ($domain -eq 'example.com' -or $domain.EndsWith('.invalid') -or
    $domain -notmatch '^[a-z0-9-]+(\.[a-z0-9-]+)+$') {
    throw 'Replace sample domain with your AD DNS domain.'
}
if ($dc -notmatch '^[A-Za-z0-9.-]{3,253}$' -or
    $dc.IndexOf('.') -lt 1 -or $dc -notlike ('*.'+$domain)) {
    throw 'Specify an explicit DC DNS hostname belonging to the selected domain.'
}
if ($ouDn -notmatch '^(?i)OU=LSCM-CI(?:-|,)' -or
    $groupDn -notmatch '^(?i)CN=LSCM-CI(?:-|,)') {
    throw 'Use only the dedicated LSCM-CI test OU and security group.'
}
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run as Administrator on a domain-joined Windows management workstation.'
}
$os=Get-WmiObject -Class Win32_OperatingSystem -ErrorAction Stop
$machine=Get-WmiObject -Class Win32_ComputerSystem -ErrorAction Stop
if ([int]$os.ProductType -ne 1 -or -not [bool]$machine.PartOfDomain -or
    [string]$machine.Domain -ine $domain) {
    throw 'Run only from a domain-joined Windows workstation, never a DC.'
}
foreach($pc in $clients) {
    if ($pc -cnotmatch '^[A-Z0-9-]{3,15}$' -or $pc -ieq $env:COMPUTERNAME) {
        throw 'Only named disposable test clients are allowed, not the controller.'
    }
}
$harness=Join-Path $PSScriptRoot 'lab_ad_integration.ps1'
if (-not (Test-Path -LiteralPath $harness -PathType Leaf)) {
    throw 'Incomplete Live AD Lab kit.'
}
$runId=[datetime]::UtcNow.ToString('yyyyMMddHHmmss')
$attempt=1
$campaignId='lscm-ci-'+$runId+'-'+$attempt
$reports=Join-Path (Join-Path $kitRoot 'reports') $campaignId
if (Test-Path -LiteralPath $reports) { throw 'Test run ID already exists; retry later.' }
# A Full test requires a fresh successful preflight on this same isolated scope.
$scopeText=($domain+'|'+$dc.ToLowerInvariant()+'|'+$ouDn.ToLowerInvariant()+
    '|'+$groupDn.ToLowerInvariant()+'|'+(($clients | Sort-Object) -join ';'))
$sha=[Security.Cryptography.SHA256]::Create()
try {
    $scopeHash=[BitConverter]::ToString(
        $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($scopeText))).Replace('-','').ToLowerInvariant()
} finally { $sha.Dispose() }
$gatePath=Join-Path (Join-Path $kitRoot 'reports') 'last-successful-preflight.json'
$lock=$null
try {
    $lock=[IO.File]::Open((Join-Path $kitRoot '.offline-lab.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    if ($Mode -eq 'Preflight' -and (Test-Path -LiteralPath $gatePath -PathType Leaf)) {
        # A failed new preflight must not leave an earlier authorization valid.
        Remove-Item -LiteralPath $gatePath -Force -ErrorAction Stop
    }
    [void](New-Item -Path $reports -ItemType Directory -Force)
    Write-Host ('Mode: '+$Mode+' | Domain: '+$domain+' | DC: '+$dc)
    Write-Host ('Test OU: '+$ouDn)
    Write-Host ('Test group: '+$groupDn)
    Write-Host ('Disposable Windows 11 PCs: '+($clients -join ', '))
    Write-Host ('Campaign ID: '+$campaignId)
    Write-Host ('Local report: '+$reports)
    if ($Mode -eq 'Full') {
        if (-not (Test-Path -LiteralPath $gatePath -PathType Leaf)) {
            throw 'A successful Preflight is required before Full. Run Run-Preflight.cmd first.'
        }
        $gate=Get-Content -LiteralPath $gatePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $verifiedUtc=[datetime]::Parse([string]$gate.verifiedUtc).ToUniversalTime()
        if ([string]$gate.scopeHash -cne $scopeHash -or
            $verifiedUtc -gt [datetime]::UtcNow.AddMinutes(1) -or
            $verifiedUtc.AddMinutes(60) -lt [datetime]::UtcNow -or
            -not (Test-Path -LiteralPath ([string]$gate.reportPath) -PathType Leaf)) {
            throw 'Last successful Preflight is stale or for a different lab scope. Run Preflight again.'
        }
        $prior=Get-Content -LiteralPath ([string]$gate.reportPath) -Raw -Encoding UTF8 |
            ConvertFrom-Json
        if ([string]$prior.mode -cne 'Preflight' -or
            -not [bool]$prior.passed -or -not [bool]$prior.preflightPassed) {
            throw 'Previous report is not a successful Preflight.'
        }
        Write-Warning 'FULL CREATES A REAL GPO, WRITES SYSVOL AND REBOOTS THE LISTED TEST COMPUTERS.'
        Write-Warning 'Partial deployment/cleanup may require administrator intervention.'
        $approval=Read-Host 'Type I_APPROVE_AD_LAB_TEST to authorize the full test'
        if ($approval -cne 'I_APPROVE_AD_LAB_TEST') {
            throw 'Full live test was not approved. No domain changes requested.'
        }
        # Consume this approval prerequisite: never silently repeat a full run.
        Remove-Item -LiteralPath $gatePath -Force -ErrorAction Stop
    }
    $invoke=@{
        Mode=$Mode;Domain=$domain;Dc=$dc;OuDn=$ouDn;GroupDn=$groupDn
        Clients=($clients -join ';');RunId=$runId;Attempt=$attempt;ReportDirectory=$reports
    }
    & $harness @invoke
    $reportFile=Join-Path $reports 'live-ad-lab.json'
    if (-not (Test-Path -LiteralPath $reportFile -PathType Leaf)) {
        throw ('Lab returned no report. Expected '+$reportFile)
    }
    $report=Get-Content -LiteralPath $reportFile -Encoding UTF8 -Raw | ConvertFrom-Json
    if (-not [bool]$report.passed) { throw ('Lab reported failure. Review '+$reportFile) }
    if ($Mode -eq 'Preflight') {
        $preflightGate=[ordered]@{
            scopeHash=$scopeHash;verifiedUtc=[datetime]::UtcNow.ToString('o')
            reportPath=[IO.Path]::GetFullPath($reportFile)
        }
        [IO.File]::WriteAllText($gatePath,($preflightGate | ConvertTo-Json -Depth 4),
            [Text.UTF8Encoding]::new($false))
        Write-Host 'Preflight validated for this exact lab scope. Full may be started within 60 minutes.'
    }
    Write-Host ('PASS - report retained locally at '+$reportFile)
}
catch {
    Write-Host ('FAILED: '+$_.Exception.Message) -ForegroundColor Red
    exit 1
}
finally {
    if ($null -ne $lock) { $lock.Dispose() }
}
