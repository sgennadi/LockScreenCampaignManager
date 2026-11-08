#requires -Version 5.1
# Runs ONLY on a dedicated, domain-joined Windows 11 lab VM as SYSTEM.
# Installed by the management runner through remote Task Scheduler (RPC), not WinRM.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidatePattern('^lscm-ci-[0-9]+-[0-9]+$')]
    [string]$CampaignId,
    [Parameter(Mandatory=$true)]
    [ValidateSet('Preflight','Verify','Cleanup')]
    [string]$Mode,
    [ValidateSet('01.jpg','02.jpg')][string]$ExpectedImage='01.jpg',
    [ValidatePattern('^[a-z0-9][a-z0-9.-]{2,252}$')][string]$ExpectedDomain='example.com',
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z0-9-]{3,15}$')][string]$ExpectedComputer
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$labRoot=Join-Path (Join-Path $env:ProgramData 'LSCM-CI-Lab') $CampaignId
$report=Join-Path $labRoot ($Mode.ToLowerInvariant()+$(if ($Mode -eq 'Verify') {'-'+$ExpectedImage.Substring(0,2)} else {''})+'.json')
$campaign=Join-Path (Join-Path $env:ProgramData 'LockScreenCampaigns') $CampaignId
$agent=Join-Path $campaign 'LockScreen-Agent.ps1'
$task='LockScreen-LS-'+$CampaignId
$gpoName='LSCM-CI-Lab-'+$CampaignId
$out=[ordered]@{
    mode=$Mode;campaignId=$CampaignId;passed=$false
    osWindows11=$false;domainJoined=$false;installed=$false
    agentHealthy=$false;imagesValid=$false;taskPresent=$false
    registryMatches=$false;gpoApplied=$false;gpoRemoved=$false;completed=$false
    sourceRemoved=$false;originalRestored=$false;pristine=$false
    expectedImage=$ExpectedImage;bootUtc='';observedUtc='';timeSource=''
    error=''
}
try {
    $os=Get-WmiObject -Class Win32_OperatingSystem -ErrorAction Stop
    $computer=Get-WmiObject -Class Win32_ComputerSystem -ErrorAction Stop
    $out.osWindows11=([int]$os.BuildNumber -ge 22000 -and
        [string]$os.Caption -match '(?i)Windows 11' -and
        [string]$os.Caption -match '(?i)Enterprise|Education')
    $out.domainJoined=([bool]$computer.PartOfDomain -and
        [string]$computer.Domain -ieq $ExpectedDomain)
    if ($env:COMPUTERNAME -ine $ExpectedComputer) {
        throw 'Remote test endpoint identity is not the approved AD computer.'
    }
    $out.bootUtc=[Management.ManagementDateTimeConverter]::ToDateTime(
        [string]$os.LastBootUpTime).ToUniversalTime().ToString('o')
    $out.observedUtc=[datetime]::UtcNow.ToString('o')
    $out.timeSource=(& "$env:SystemRoot\System32\w32tm.exe" /query /source 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $out.timeSource.Length -lt 2 -or
        $out.timeSource -match '(?i)Local CMOS Clock|Free-running System Clock') {
        throw 'Windows 11 client does not have an acceptable Windows Time source.'
    }
    if (-not $out.osWindows11 -or -not $out.domainJoined) {
        throw 'The client must be domain-joined Windows 11 Enterprise/Education.'
    }
    $reg='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'
    if ($Mode -eq 'Preflight') {
        $registered=@(Get-ScheduledTask -ErrorAction Stop | Where-Object {
            $_.TaskName -like 'LockScreen-LS-*'
        })
        $lock=Get-ItemProperty -LiteralPath $reg -Name LockScreenImage -ErrorAction SilentlyContinue
        $flag=Get-ItemProperty -LiteralPath $reg -Name NoChangingLockScreen -ErrorAction SilentlyContinue
        $owner=Join-Path (Join-Path $env:ProgramData 'LockScreenCampaigns') 'active-owner.json'
        $out.pristine=($registered.Count -eq 0 -and
            -not (Test-Path -LiteralPath $owner) -and
            -not (Test-Path -LiteralPath $campaign) -and
            $null -eq $lock -and $null -eq $flag)
        $out.passed=$out.pristine
        if (-not $out.passed) { throw 'Test PC is not pristine: existing lock-screen policy/owner/task/campaign.' }
    }
    elseif ($Mode -eq 'Verify') {
        if (-not (Test-Path -LiteralPath $agent -PathType Leaf)) {
            throw 'GPO Startup did not install the campaign agent.'
        }
        $status=& $agent -Mode Status | Where-Object {
            $_ -is [psobject] -and $_.PSObject.Properties.Name -contains 'Health'
        } | Select-Object -Last 1
        if ($null -eq $status) { throw 'Client agent returned no status.' }
        $out.installed=([bool]$status.Installed)
        $out.agentHealthy=([string]$status.Health -eq 'Healthy' -and
            [bool]$status.ConfigHashOK -and [bool]$status.AgentHashOK)
        $out.taskPresent=($null -ne (Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue))
        $manifest=Get-Content -LiteralPath (Join-Path $campaign 'campaign.json') -Raw -Encoding UTF8 |
            ConvertFrom-Json
        $allValid=$true
        foreach($slide in @($manifest.slides)) {
            $image=Join-Path (Join-Path $campaign 'images') ([string]$slide.fileName)
            if (-not (Test-Path -LiteralPath $image -PathType Leaf) -or
                (Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash -ine
                    [string]$slide.sha256) { $allValid=$false }
        }
        $out.imagesValid=$allValid
        $regValue=[string](Get-ItemProperty -LiteralPath $reg -Name LockScreenImage -ErrorAction Stop).LockScreenImage
        $expected=Join-Path (Join-Path $campaign 'images') $ExpectedImage
        $out.registryMatches=$regValue.Equals($expected,[StringComparison]::OrdinalIgnoreCase)
        if ($out.registryMatches) {
            $expectedHash=[string](@($manifest.slides | Where-Object { $_.fileName -eq $ExpectedImage }) |
                Select-Object -First 1).sha256
            $out.registryMatches=(Get-FileHash -LiteralPath $regValue -Algorithm SHA256).Hash -ieq $expectedHash
        }
        # Group Policy evidence is separate from the agent's own health claims.
        $gpresult=& "$env:SystemRoot\System32\gpresult.exe" /scope computer /r 2>&1 | Out-String
        $out.gpoApplied=($LASTEXITCODE -eq 0 -and
            $gpresult.IndexOf($gpoName,[StringComparison]::OrdinalIgnoreCase) -ge 0)
        $out.passed=$out.installed -and $out.agentHealthy -and
            $out.imagesValid -and $out.taskPresent -and $out.registryMatches -and
            $out.gpoApplied
        if (-not $out.passed) { throw 'Client GPO/agent/registry/task verification failed.' }
    }
    else {
        if (-not (Test-Path -LiteralPath $agent -PathType Leaf)) {
            throw 'Cannot perform managed client cleanup: campaign agent is missing.'
        }
        & $agent -Mode Cleanup
        $completedDir=Join-Path (Join-Path $env:ProgramData 'LockScreenCampaigns') 'Completed'
        $completedMarker=Join-Path $completedDir ($CampaignId+'.json')
        $out.completed=Test-Path -LiteralPath $completedMarker -PathType Leaf
        $out.sourceRemoved=-not (Test-Path -LiteralPath $campaign)
        $out.taskPresent=($null -ne (Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue))
        # Lab preflight requires both values to be absent. Cleanup must restore
        # exactly that state; absence is handled safely under StrictMode Latest.
        $imagePolicy=Get-ItemProperty -LiteralPath $reg -Name LockScreenImage -ErrorAction SilentlyContinue
        $noChangePolicy=Get-ItemProperty -LiteralPath $reg -Name NoChangingLockScreen -ErrorAction SilentlyContinue
        $out.originalRestored=($null -eq $imagePolicy -and $null -eq $noChangePolicy)
        # Do not claim source cleanup is safe until the unlinked test GPO
        # disappears from the client's refreshed machine RSoP.
        & "$env:SystemRoot\System32\gpupdate.exe" /force /target:computer /wait:60 | Out-Null
        $refreshOk=($LASTEXITCODE -eq 0)
        $gpresult=& "$env:SystemRoot\System32\gpresult.exe" /scope computer /r 2>&1 | Out-String
        $out.gpoRemoved=($refreshOk -and $LASTEXITCODE -eq 0 -and
            $gpresult.IndexOf($gpoName,[StringComparison]::OrdinalIgnoreCase) -lt 0)
        $out.passed=$out.completed -and $out.sourceRemoved -and
            -not $out.taskPresent -and $out.originalRestored -and $out.gpoRemoved
        if (-not $out.passed) { throw 'Client agent cleanup could not be verified.' }
    }
}
catch {
    $out.error=$_.Exception.Message
}
finally {
    if (-not (Test-Path -LiteralPath $labRoot -PathType Container)) {
        New-Item -ItemType Directory -Path $labRoot -Force | Out-Null
    }
    $temporary=$report+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    try {
        [IO.File]::WriteAllText($temporary,($out | ConvertTo-Json -Depth 7),
            [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $report -Force -ErrorAction Stop
    } finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
}
if (-not $out.passed) { exit 1 }
