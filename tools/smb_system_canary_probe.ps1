#requires -Version 5.1
# Read-only SMB campaign evidence under LOCAL SYSTEM on an approved lab endpoint.
# This script does not execute source scripts/binaries or mutate AD/GPO/registry.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ConfigPath,
    [Parameter(Mandatory=$true)][string]$ReportPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$checks=[ordered]@{
    schema='lscm-smb-system-canary/v1'
    utc=[datetime]::UtcNow.ToString('o')
    windowsClient=$false
    osBuild=0
    localSystem=$false
    approvedComputer=$false
    approvedDomain=$false
    smbSourceReadable=$false
    manifestSha256Verified=$false
    agentSha256Verified=$false
    allImagesVerified=$false
    imageCount=0
    localCachePresent=$false
    localCacheHashVerified=$false
    computerGpresultAvailable=$false
    actualGpoAppliedVerified=$false
    realLockScreenDisplayVerified=$false
    registryRestorationVerified=$false
    result='INCOMPLETE'
    findings=@()
}
$findings=New-Object 'System.Collections.Generic.List[string]'

function Finding([string]$name) { [void]$script:findings.Add($name) }

function File-Hash([string]$path,[int64]$max) {
    $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if ($item -isnot [IO.FileInfo] -or
        ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $item.Length -lt 1 -or $item.Length -gt $max) {
        throw 'INVALID_SOURCE_FILE'
    }
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
}
function Check-Directory([string]$path) {
    $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if ($item -isnot [IO.DirectoryInfo] -or
        ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'INVALID_SOURCE_DIRECTORY'
    }
}
function Verify-Images([string]$folder,[object[]]$slides) {
    $imageDir=Join-Path $folder 'images'
    Check-Directory $imageDir
    $seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach($item in $slides) {
        $name=[string]$item.fileName
        $expected=[string]$item.sha256
        if ($name -cnotmatch '^[0-9]{2}\.jpg$' -or
            $expected -notmatch '^[0-9a-fA-F]{64}$' -or
            -not $seen.Add($name) -or
            (File-Hash (Join-Path $imageDir $name) 30MB) -ine $expected) {
            throw 'IMAGE_INTEGRITY_FAILED'
        }
    }
    if (@(Get-ChildItem -LiteralPath $imageDir -Force).Count -ne $slides.Count) {
        throw 'EXTRA_IMAGE_CONTENT'
    }
}
try {
    $who=[Security.Principal.WindowsIdentity]::GetCurrent()
    $checks.localSystem=($null -ne $who -and $who.User.Value -eq 'S-1-5-18')
    if (-not $checks.localSystem) { throw 'NOT_LOCAL_SYSTEM' }

    $conf=Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json
    $id=[string]$conf.campaignId
    $root=[string]$conf.contentShareRoot
    $expectedManifest=[string]$conf.manifestSha256
    $expectedAgent=[string]$conf.agentSha256
    $expectedComputer=[string]$conf.expectedComputer
    $expectedDomain=[string]$conf.expectedDomain
    $method=[string]$conf.clientType
    if ($id -cnotmatch '^[a-z0-9-]{5,75}$' -or
        $expectedManifest -notmatch '^[A-Fa-f0-9]{64}$' -or
        $expectedAgent -notmatch '^[A-Fa-f0-9]{64}$' -or
        $expectedComputer -cnotmatch '^[A-Za-z0-9-]{3,15}$' -or
        $expectedDomain -cnotmatch '^[a-zA-Z0-9][a-zA-Z0-9.-]{2,252}$' -or
        $method -cnotin @('PowerShell','Executable') -or
        $root -notmatch '^\\\\[a-zA-Z0-9][a-zA-Z0-9.-]+\\[a-zA-Z0-9_][a-zA-Z0-9_. -]*' -or
        $root.Length -gt 200 -or $root.Contains('..') -or
        $root.Contains('/') -or $root.Contains(':') -or
        $root.Contains('"') -or $root.Contains('$')) {
        throw 'INVALID_CANARY_CONFIG'
    }
    $parts=$root.Substring(2).Split('\')
    if ($parts.Count -lt 2 -or $parts.Count -gt 8 -or
        @($parts | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' -or
            $_.EndsWith(' ') -or $_.EndsWith('.') -or $_ -match '[;\[\]]' }).Count -ne 0 -or
        @($parts | Where-Object { $_ -match '^(?i:SYSVOL|NETLOGON|IPC)$' }).Count -gt 0) {
        throw 'INVALID_CANARY_CONFIG'
    }

    $checks.approvedComputer=($env:COMPUTERNAME -ieq $expectedComputer)
    if (-not $checks.approvedComputer) { throw 'WRONG_COMPUTER' }
    $os=Get-WmiObject -Class Win32_OperatingSystem -ErrorAction Stop
    $pc=Get-WmiObject -Class Win32_ComputerSystem -ErrorAction Stop
    $checks.osBuild=[int]$os.BuildNumber
    $checks.windowsClient=([int]$os.ProductType -eq 1 -and [int]$os.BuildNumber -ge 10240)
    $checks.approvedDomain=([bool]$pc.PartOfDomain -and [string]$pc.Domain -ieq $expectedDomain)
    if (-not $checks.windowsClient -or -not $checks.approvedDomain) {
        throw 'UNAPPROVED_ENDPOINT_OR_DOMAIN'
    }

    $folder=Join-Path $root $id
    $agentName=if($method -eq 'Executable'){'LockScreen-Agent.exe'}else{'LockScreen-Agent.ps1'}
    Check-Directory $root
    Check-Directory $folder
    Check-Directory (Join-Path $folder 'images')
    $configFile=Join-Path $folder 'campaign.json'
    $checks.smbSourceReadable=$true
    $observedManifest=File-Hash $configFile 1MB
    if ($observedManifest -ine $expectedManifest) { throw 'MANIFEST_HASH_MISMATCH' }
    $checks.manifestSha256Verified=$true
    $manifest=Get-Content -LiteralPath $configFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $actualMethod=if($manifest.PSObject.Properties.Name -notcontains 'clientType' -or
        [string]$manifest.PSObject.Properties['clientType'].Value -eq ''){
        'PowerShell'
    }else{[string]$manifest.clientType}
    $slides=@($manifest.slides)
    if ([int]$manifest.schemaVersion -ne 1 -or
        [string]$manifest.campaignId -cne $id -or
        $actualMethod -cne $method -or
        [string]$manifest.agentSha256 -ine $expectedAgent -or
        $slides.Count -lt 1 -or $slides.Count -gt 30) {
        throw 'MANIFEST_IDENTITY_MISMATCH'
    }
    $limit=if($method -eq 'Executable'){250MB}else{5MB}
    if ((File-Hash (Join-Path $folder $agentName) $limit) -ine $expectedAgent) {
        throw 'AGENT_HASH_MISMATCH'
    }
    $checks.agentSha256Verified=$true
    Verify-Images $folder $slides
    $checks.allImagesVerified=$true
    $checks.imageCount=$slides.Count

    $cache=Join-Path (Join-Path $env:ProgramData 'LockScreenCampaigns') $id
    if (Test-Path -LiteralPath $cache -PathType Container) {
        $checks.localCachePresent=$true
        try {
            Check-Directory $cache
            if ((File-Hash (Join-Path $cache 'campaign.json') 1MB) -ne $expectedManifest) {
                throw 'LOCAL_MANIFEST_HASH_MISMATCH'
            }
            if ((File-Hash (Join-Path $cache $agentName) $limit) -ne $expectedAgent) {
                throw 'LOCAL_AGENT_HASH_MISMATCH'
            }
            Verify-Images $cache $slides
            $checks.localCacheHashVerified=$true
        }
        catch { Finding 'LOCAL_CACHE_INTEGRITY_NEEDS_REVIEW' }
    } else {
        Finding 'LOCAL_CACHE_NOT_INSTALLED_YET'
    }
    # GPRESULT is inspected only for command success. Actual winning policy
    # applicability and displayed lock screen still require a human lab review.
    try {
        $null= & "$env:SystemRoot\System32\gpresult.exe" /scope computer /r 2>&1
        $checks.computerGpresultAvailable=($LASTEXITCODE -eq 0)
    } catch {}
    if (-not $checks.computerGpresultAvailable) {
        Finding 'COMPUTER_GPO_RSOP_UNAVAILABLE'
    }
    Finding 'MANUAL_GPO_WINNER_REVIEW_REQUIRED'
    Finding 'MANUAL_DISPLAY_AND_ROLLBACK_REQUIRED'
    $checks.result='SOURCE_VERIFIED_AS_LOCAL_SYSTEM_ONLY'
}
catch {
    $message=[string]$_.Exception.Message
    $allow=@('NOT_LOCAL_SYSTEM','INVALID_CANARY_CONFIG','WRONG_COMPUTER',
        'UNAPPROVED_ENDPOINT_OR_DOMAIN','INVALID_SOURCE_FILE',
        'INVALID_SOURCE_DIRECTORY','IMAGE_INTEGRITY_FAILED',
        'EXTRA_IMAGE_CONTENT','MANIFEST_HASH_MISMATCH',
        'MANIFEST_IDENTITY_MISMATCH','AGENT_HASH_MISMATCH')
    if ($message -in $allow) { Finding $message }
    else { Finding 'SOURCE_UNREADABLE_OR_VERIFICATION_FAILED' }
    $checks.result='FAIL_CLOSED'
}
finally {
    $checks.findings=@($findings.ToArray())
    # The caller controls only an already-protected LOCAL report path.
    # Do not include the UNC, DC, computer, domain, account, GPO name or SID.
    if (-not [IO.Path]::IsPathRooted($ReportPath) -or
        $ReportPath.StartsWith('\\') -or
        $ReportPath.Contains('..')) { throw 'Invalid local report destination.' }
    $json=$checks | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText($ReportPath,$json,[Text.UTF8Encoding]::new($false))
}
if ($checks.result -ne 'SOURCE_VERIFIED_AS_LOCAL_SYSTEM_ONLY') { exit 2 }
