#requires -Version 5.1
# Read-only, explicitly scoped DC clock / SYSVOL / GPO consistency scan.
# No RSAT, no WinRM, no AD writes, no service configuration, no SYSVOL changes.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RequestPath,
    [Parameter(Mandatory=$true)][string]$ResponsePath,
    [ValidateSet('Scan')][string]$Mode='Scan'
)
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.DirectoryServices
function Reply([bool]$success,[string]$message,$data) {
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes(
        ([ordered]@{success=$success;message=$message;data=$data} |
            ConvertTo-Json -Depth 14 -Compress))
    [IO.File]::WriteAllBytes($ResponsePath,$bytes)
}
function ValidHost([string]$hostName,[string]$domain) {
    return $hostName -match '^(?=.{3,253}$)[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+$' -and
        $hostName.EndsWith('.'+$domain,[StringComparison]::OrdinalIgnoreCase) -and
        $hostName -notmatch '\.\.|(^|\.)(-|.*?-\.)'
}
function Require-RegularFile([string]$path,[long]$maximum) {
    $entry=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if($entry.PSIsContainer -or
       ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
       $entry.Length -lt 1 -or $entry.Length -gt $maximum) {
        throw 'InvalidFileTypeOrLength'
    }
    return $entry
}
function Require-RegularDirectory([string]$path) {
    $entry=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if(-not $entry.PSIsContainer -or
       ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'InvalidDirectoryOrReparsePoint'
    }
}
function FileDigest([string]$path,[long]$maximum) {
    $entry=Require-RegularFile $path $maximum
    return [pscustomobject]@{
        sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        bytes=[long]$entry.Length
    }
}
function Parse-DcUtc([string]$value) {
    if($value -cnotmatch '^(?<stamp>\d{14})(?:\.\d{1,7})?Z$') {
        throw 'InvalidDomainControllerUtc'
    }
    return [datetime]::ParseExact($Matches['stamp'],'yyyyMMddHHmmss',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal -bor
        [Globalization.DateTimeStyles]::AdjustToUniversal)
}
function Read-DcRoot([string]$dc,[string]$domain) {
    $authentication=[DirectoryServices.AuthenticationTypes]::Secure -bor
        [DirectoryServices.AuthenticationTypes]::Signing -bor
        [DirectoryServices.AuthenticationTypes]::Sealing
    $root=[DirectoryServices.DirectoryEntry]::new('LDAP://'+$dc+'/RootDSE',
        $null,$null,$authentication)
    try {
        $begin=[datetime]::UtcNow
        $root.RefreshCache(@('defaultNamingContext','dnsHostName','isRODC','currentTime'))
        $end=[datetime]::UtcNow
        if($root.Properties['defaultNamingContext'].Count -ne 1 -or
           $root.Properties['dnsHostName'].Count -ne 1 -or
           $root.Properties['currentTime'].Count -ne 1) {
            throw 'MissingDomainControllerRootDseFields'
        }
        $base=[string]$root.Properties['defaultNamingContext'][0]
        $actual=([regex]::Matches($base,'(?i)(?:^|,)DC=([^,]+)') |
            ForEach-Object {$_.Groups[1].Value}) -join '.'
        $actualHost=[string]$root.Properties['dnsHostName'][0]
        if($actual -ine $domain -or
           -not $actualHost.Equals($dc,[StringComparison]::OrdinalIgnoreCase)) {
            throw 'DomainControllerIdentityMismatch'
        }
        $utc=Parse-DcUtc ([string]$root.Properties['currentTime'][0])
        $mid=$begin.AddTicks([long](($end-$begin).Ticks / 2))
        $samplingSeconds=[math]::Round(($end-$begin).TotalSeconds,2)
        if($samplingSeconds -gt 30) {throw 'SlowOrUnverifiableDcTimeSample'}
        $rodc='Unknown'
        if($root.Properties['isRODC'].Count -gt 0) {
            $rodc=if([string]$root.Properties['isRODC'][0] -ieq 'TRUE'){'Yes'}else{'No'}
        }
        return [pscustomobject]@{
            base=$base;utc=$utc;rodc=$rodc
            offsetSeconds=[int][math]::Round(($utc-$mid).TotalSeconds)
            sampledUtc=$mid.ToString('o')
        }
    } finally { $root.Dispose() }
}
function Read-GpoVersion([string]$dc,[string]$base,[guid]$gpo) {
    $auth=[DirectoryServices.AuthenticationTypes]::Secure -bor
        [DirectoryServices.AuthenticationTypes]::Signing -bor
        [DirectoryServices.AuthenticationTypes]::Sealing
    $name='{'+$gpo.ToString().ToUpperInvariant()+'}'
    $entry=[DirectoryServices.DirectoryEntry]::new(
        ('LDAP://{0}/CN={1},CN=Policies,CN=System,{2}' -f $dc,$name,$base),
        $null,$null,$auth)
    try {
        $entry.RefreshCache(@('objectClass','versionNumber'))
        if(@($entry.Properties['objectClass']) -notcontains 'groupPolicyContainer' -or
           $entry.Properties['versionNumber'].Count -ne 1) {throw 'ManagedGpoAdMissing'}
        return [long]$entry.Properties['versionNumber'][0]
    } finally { $entry.Dispose() }
}
function Scan-One([string]$dc,[string]$domain,[string]$campaignId,[guid]$gpo) {
    $result=[ordered]@{
        dc=$dc;status='Unavailable';reason='ReadFailed'
        dcUtc='';sampledUtc='';offsetSeconds=0;rodc='Unknown'
        manifestHash='';agentHash='';gptIniHash='';psScriptsHash=''
        adVersion=-1;gptVersion=-1;images=@()
    }
    try {
        $root=Read-DcRoot $dc $domain
        $result.dcUtc=$root.utc.ToString('o')
        $result.sampledUtc=$root.sampledUtc
        $result.offsetSeconds=$root.offsetSeconds
        $result.rodc=$root.rodc
        $base='\\{0}\SYSVOL\{1}\scripts\LockScreenCampaigns\{2}' -f
            $dc,$domain,$campaignId
        $images=Join-Path $base 'images'
        Require-RegularDirectory $base
        Require-RegularDirectory $images
        $entries=@(Get-ChildItem -LiteralPath $base -Force -ErrorAction Stop)
        if($entries.Count -ne 3 -or
           @($entries | Where-Object {$_.Name -notin @(
               'campaign.json','LockScreen-Agent.ps1','images')}).Count -gt 0) {
            throw 'UnexpectedCampaignLayout'
        }
        $manifest=Join-Path $base 'campaign.json'
        $agent=Join-Path $base 'LockScreen-Agent.ps1'
        $manifestDigest=FileDigest $manifest 1048576
        $agentDigest=FileDigest $agent 5242880
        $config=Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 |
            ConvertFrom-Json -ErrorAction Stop
        if([int]$config.schemaVersion -ne 1 -or
           [string]$config.campaignId -cne $campaignId -or
           [string]$config.agentSha256 -cnotmatch '^[a-fA-F0-9]{64}$' -or
           $agentDigest.sha256 -cne ([string]$config.agentSha256).ToLowerInvariant()) {
            throw 'ManifestOrAgentIntegrityMismatch'
        }
        $slides=@($config.slides)
        if($slides.Count -lt 1 -or $slides.Count -gt 30) {throw 'InvalidManifestImageCount'}
        $imageItems=@(Get-ChildItem -LiteralPath $images -Force -ErrorAction Stop)
        if($imageItems.Count -ne $slides.Count) {throw 'UnexpectedImageCount'}
        $seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        $hashes=@()
        foreach($slide in $slides) {
            $file=[string]$slide.fileName
            $expected=[string]$slide.sha256
            if($file -cnotmatch '^[0-9]{2}\.jpg$' -or
               -not $seen.Add($file) -or
               $expected -cnotmatch '^[a-fA-F0-9]{64}$') {
                throw 'InvalidImageManifestEntry'
            }
            $h=FileDigest (Join-Path $images $file) 31457280
            if($h.sha256 -cne $expected.ToLowerInvariant()) {
                throw 'ImageSha256Mismatch'
            }
            $hashes+= [pscustomobject]@{name=$file;sha256=$h.sha256;bytes=$h.bytes}
        }
        foreach($item in $imageItems) {
            if(-not $seen.Contains($item.Name)) {throw 'UnexpectedImageFile'}
        }
        # AD GPO version and both GPT files are read from this SAME named DC.
        $gpt='\\{0}\SYSVOL\{1}\Policies\{{{2}}}' -f
            $dc,$domain,$gpo.ToString().ToUpperInvariant()
        $ini=Join-Path $gpt 'gpt.ini'
        $startup=Join-Path (Join-Path (Join-Path $gpt 'Machine') 'Scripts') 'psscripts.ini'
        Require-RegularDirectory $gpt
        Require-RegularDirectory (Join-Path $gpt 'Machine')
        Require-RegularDirectory (Join-Path (Join-Path $gpt 'Machine') 'Scripts')
        $gptDigest=FileDigest $ini 1048576
        $startupDigest=FileDigest $startup 1048576
        # gpt.ini is commonly ANSI/ASCII (psscripts.ini is UTF-16 LE).
        # Read the former using the same .NET detection as GPO Lifecycle.
        $iniText=[IO.File]::ReadAllText($ini)
        $startupText=Get-Content -LiteralPath $startup -Raw -Encoding Unicode
        $versionMatch=[regex]::Match($iniText,'(?im)^\s*Version=(\d+)\s*$')
        if(-not $versionMatch.Success){throw 'GptVersionMissing'}
        [long]$gptVersion=0
        if(-not [long]::TryParse($versionMatch.Groups[1].Value,[ref]$gptVersion)){
            throw 'InvalidGptVersion'
        }
        $escaped=[regex]::Escape($campaignId)
        if($startupText -notmatch ('(?im)^0CmdLine=\\\\[^\r\n]+\\scripts\\LockScreenCampaigns\\'+
            $escaped+'\\LockScreen-Agent\.ps1\s*$') -or
           $startupText -notmatch '(?im)^0Parameters=-Mode Install\s*$'){
            throw 'GpoStartupTargetsOtherCampaign'
        }
        $adVersion=Read-GpoVersion $dc $root.base $gpo
        if($adVersion -ne $gptVersion) {throw 'AdGptVersionMismatch'}
        # Re-read the manifest after all hashes to catch concurrent updates.
        if((FileDigest $manifest 1048576).sha256 -cne $manifestDigest.sha256) {
            throw 'CampaignChangedDuringScan'
        }
        $result.manifestHash=$manifestDigest.sha256
        $result.agentHash=$agentDigest.sha256
        $result.gptIniHash=$gptDigest.sha256
        $result.psScriptsHash=$startupDigest.sha256
        $result.adVersion=$adVersion
        $result.gptVersion=$gptVersion
        $result.images=@($hashes | Sort-Object name)
        $result.status='Readable'
        $result.reason='OK'
    } catch {
        # Do not send private UNC paths or detailed exception text into JSON/UI.
        $result.status=if($_.Exception.Message -match
            '^(Unexpected|Manifest|Invalid|ImageSha256|GptVersion|AdGpt|GpoStartup|CampaignChanged|ManagedGpo)') {
            'Invalid'
        }else{'Unavailable'}
        $result.reason=if($result.status -eq 'Invalid') {
            [string]$_.Exception.Message
        }else{'ReadDeniedOrUnavailable'}
    }
    return [pscustomobject]$result
}
try {
    $request=Get-Content -LiteralPath $RequestPath -Encoding UTF8 -Raw |
        ConvertFrom-Json -ErrorAction Stop
    $domain=([string]$request.domain).Trim().ToLowerInvariant()
    $selected=([string]$request.dc).Trim().ToLowerInvariant()
    $campaignId=[string]$request.campaignId
    if($domain -notmatch '^(?=.{3,253}$)[a-z0-9-]+(?:\.[a-z0-9-]+)+$' -or
       -not (ValidHost $selected $domain) -or
       $campaignId -cnotmatch '^[a-z0-9][a-z0-9_-]{0,70}$') {
        throw 'Invalid DNS domain, explicit selected DC or campaign identifier.'
    }
    $gpo=[guid]::Empty
    if(-not [guid]::TryParse([string]$request.gpoId,[ref]$gpo) -or
       $gpo -eq [guid]::Empty) {throw 'Invalid managed GPO GUID.'}
    $dcs=@($request.controllers)
    if($dcs.Count -lt 1 -or $dcs.Count -gt 12) {
        throw 'DC audit requires 1-12 explicitly discovered controllers.'
    }
    $unique=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach($item in $dcs) {
        if($null -eq $item -or -not (ValidHost ([string]$item) $domain) -or
            -not $unique.Add([string]$item)) {
            throw 'Duplicate, unsafe or cross-domain controller in requested audit.'
        }
    }
    if(-not $unique.Contains($selected)){
        throw 'Selected DC is not included in verified discovered controllers.'
    }
    $rows=@()
    foreach($hostName in @($dcs | Sort-Object)) {
        $rows+=Scan-One ([string]$hostName) $domain $campaignId $gpo
    }
    Reply $true 'Read-only multi-DC time, SYSVOL and GPO inspection complete.' ([ordered]@{
        domain=$domain;selectedDc=$selected;campaignId=$campaignId;gpoId=$gpo.ToString('D')
        scannedUtc=([datetime]::UtcNow.ToString('o'))
        rows=@($rows)
    })
} catch {
    Reply $false ('DC consistency scan rejected: '+$_.Exception.GetType().Name) $null
    exit 1
}
