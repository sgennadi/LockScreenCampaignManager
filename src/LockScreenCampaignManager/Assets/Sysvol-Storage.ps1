#requires -Version 5.1
# Smart SYSVOL Storage. Offline inventory and explicit, guarded cleanup.
# No deduplication links, no GPO edits, no silent writes or background deletes.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RequestPath,
    [Parameter(Mandatory=$true)][string]$ResponsePath,
    [ValidateSet('Scan','Preview','Cleanup')][string]$Mode='Scan'
)
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.DirectoryServices

function Reply([bool]$ok,[string]$message,$data) {
    [IO.File]::WriteAllText($ResponsePath,
        ([ordered]@{success=$ok;message=$message;data=$data} |
            ConvertTo-Json -Depth 12 -Compress),
        [Text.UTF8Encoding]::new($false))
}
function Sha([string]$text) {
    $h=[Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString(
        $h.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)))).Replace('-','').ToLowerInvariant() }
    finally { $h.Dispose() }
}
function SafeId([string]$id) {
    return $id -cmatch '^[a-z0-9][a-z0-9_-]{0,70}$'
}
function Assert-NoLink([string]$path) {
    $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Reparse points and links are not allowed in campaign storage.'
    }
}
function Parse-DcTime([string]$value) {
    # AD RootDSE currentTime is UTC generalized time.
    if ($value -notmatch '^(?<stamp>\d{14})(?:\.\d{1,7})?Z$') {
        throw 'Selected DC does not provide a verifiable UTC currentTime.'
    }
    return [datetime]::ParseExact($Matches['stamp'],'yyyyMMddHHmmss',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal -bor
        [Globalization.DateTimeStyles]::AdjustToUniversal)
}
function Assert-DcClock($scope) {
    if ([math]::Abs($scope.skewSeconds) -gt 300) {
        throw ('Computer clock differs from DC by {0} seconds (>5 minutes). Synchronize Windows Time before changing SYSVOL.' -f $scope.skewSeconds)
    }
}
function Get-Scope($request) {
    $domain=([string]$request.domain).Trim().ToLowerInvariant()
    $dc=([string]$request.dc).Trim().ToLowerInvariant()
    if ($domain -notmatch '^(?=.{3,253}$)[a-z0-9-]+(\.[a-z0-9-]+)+$' -or
        $dc -notmatch '^[a-z0-9][a-z0-9.-]{1,252}$' -or
        $dc -match '^\d+\.\d+\.\d+\.\d+$') {
        throw 'Enter a valid DNS domain and explicit DC hostname.'
    }
    $auth=[DirectoryServices.AuthenticationTypes]::Secure -bor
        [DirectoryServices.AuthenticationTypes]::Signing -bor
        [DirectoryServices.AuthenticationTypes]::Sealing
    $entry=[DirectoryServices.DirectoryEntry]::new(
        ('LDAP://{0}/RootDSE' -f $dc),$null,$null,$auth)
    try {
        $entry.RefreshCache(@('defaultNamingContext','isRODC','currentTime'))
        $base=[string]$entry.Properties['defaultNamingContext'][0]
        $actual=([regex]::Matches($base,'(?i)(?:^|,)DC=([^,]+)') |
            ForEach-Object { $_.Groups[1].Value }) -join '.'
        if ($actual -ine $domain) { throw 'DC does not belong to selected domain.' }
        $rodc=if ($entry.Properties['isRODC'].Count -gt 0) {
            [string]$entry.Properties['isRODC'][0] } else { 'UNKNOWN' }
        $writable=$rodc -ieq 'False'
        if ($entry.Properties['currentTime'].Count -lt 1) {
            throw 'Selected DC did not return RootDSE currentTime.'
        }
        $dcUtc=Parse-DcTime ([string]$entry.Properties['currentTime'][0])
        $skew=[int][math]::Round(([datetime]::UtcNow-$dcUtc).TotalSeconds)
    }
    finally { $entry.Dispose() }
    return [pscustomobject]@{
        domain=$domain;dc=$dc;base=$base;writable=$writable
        dcUtc=$dcUtc;skewSeconds=$skew
        root=('\\{0}\SYSVOL\{1}\scripts\LockScreenCampaigns' -f $dc,$domain)
        policies=('\\{0}\SYSVOL\{1}\Policies' -f $dc,$domain)
    }
}
function Read-VerifiedFolder([string]$folder,[string]$expectedId,[bool]$exactLayout=$true) {
    if (-not (SafeId $expectedId)) { throw 'Unsafe campaign directory name.' }
    Assert-NoLink $folder
    $cfgFile=Join-Path $folder 'campaign.json'
    $agent=Join-Path $folder 'LockScreen-Agent.ps1'
    $images=Join-Path $folder 'images'
    if (-not (Test-Path -LiteralPath $cfgFile -PathType Leaf) -or
        -not (Test-Path -LiteralPath $agent -PathType Leaf) -or
        -not (Test-Path -LiteralPath $images -PathType Container)) {
        throw 'Campaign must have a manifest, agent and images folder.'
    }
    foreach($p in @($cfgFile,$agent,$images)) { Assert-NoLink $p }
    $cfgInfo=Get-Item -LiteralPath $cfgFile
    if ($cfgInfo.Length -lt 10 -or $cfgInfo.Length -gt 1048576) {
        throw 'Manifest size is invalid.'
    }
    $cfg=Get-Content -LiteralPath $cfgFile -Encoding UTF8 -Raw | ConvertFrom-Json
    if ([string]$cfg.campaignId -cne $expectedId -or
        [int]$cfg.schemaVersion -ne 1) { throw 'Campaign ID/schema mismatch.' }
    $tz=[TimeZoneInfo]::FindSystemTimeZoneById([string]$cfg.timeZoneId)
    $end=[datetime]::ParseExact([string]$cfg.endLocal,'yyyy-MM-ddTHH:mm:ss',
        [Globalization.CultureInfo]::InvariantCulture)
    if ($tz.IsAmbiguousTime($end) -or $tz.IsInvalidTime($end)) {
        throw 'Unverifiable campaign end timezone.'
    }
    $endUtc=[TimeZoneInfo]::ConvertTimeToUtc($end,$tz)
    $slides=@($cfg.slides)
    if ($slides.Count -lt 1 -or $slides.Count -gt 30) {
        throw 'Campaign must have 1-30 images.'
    }
    $all=@(Get-ChildItem -LiteralPath $folder -Force)
    if ($exactLayout -and ($all.Count -ne 3 -or
        @($all | Where-Object { $_.Name -notin @('campaign.json','LockScreen-Agent.ps1','images') }).Count -gt 0)) {
        throw 'Unexpected files in campaign directory; cleanup is disabled.'
    }
    $imageFiles=@(Get-ChildItem -LiteralPath $images -Force)
    if ($imageFiles.Count -ne $slides.Count) {
        throw 'Image file count does not match campaign manifest.'
    }
    $seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $hashes=@()
    [long]$bytes=0
    foreach($s in $slides) {
        $leaf=[string]$s.fileName
        $expected=[string]$s.sha256
        if ($leaf -cnotmatch '^[0-9]{2}\.jpg$' -or
            -not $seen.Add($leaf) -or
            $expected -notmatch '^[0-9a-fA-F]{64}$') {
            throw 'Unsafe or duplicate image manifest entry.'
        }
        $path=Join-Path $images $leaf
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw 'An image is missing from SYSVOL.'
        }
        Assert-NoLink $path
        $info=Get-Item -LiteralPath $path
        if ($info.Length -lt 1 -or $info.Length -gt 31457280) {
            throw 'Image size is outside allowed bounds.'
        }
        $actual=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -cne $expected.ToLowerInvariant()) {
            throw 'An image does not match its manifest SHA-256.'
        }
        $bytes+=$info.Length
        $hashes+= [pscustomobject]@{hash=$actual;bytes=[long]$info.Length;file=$leaf}
    }
    if (@($imageFiles | Where-Object { $_.Name -notin @($slides | ForEach-Object { $_.fileName }) }).Count -gt 0) {
        throw 'Unexpected image files found; cleanup is disabled.'
    }
    $agentHash=(Get-FileHash -LiteralPath $agent -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($cfg.PSObject.Properties.Name -contains 'agentSha256' -and
        $agentHash -cne ([string]$cfg.agentSha256).ToLowerInvariant()) {
        throw 'Agent SHA-256 mismatch.'
    }
    $cfgHash=(Get-FileHash -LiteralPath $cfgFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $fingerprint=Sha ($expectedId+'|'+$cfgHash+'|'+$agentHash+'|'+
        (($hashes | ForEach-Object { $_.file+':'+$_.hash+':'+$_.bytes }) -join '|'))
    return [pscustomobject]@{
        id=$expectedId;path=$folder;endUtc=$endUtc.ToString('o')
        imageCount=$hashes.Count;imageBytes=$bytes
        slides=$hashes;fingerprint=$fingerprint;valid=$true
    }
}
function Inventory($scope) {
    $result=New-Object 'System.Collections.Generic.List[object]'
    if (-not (Test-Path -LiteralPath $scope.root -PathType Container)) {
        return $result.ToArray()
    }
    Assert-NoLink $scope.root
    $dirs=@(Get-ChildItem -LiteralPath $scope.root -Directory -Force)
    if ($dirs.Count -gt 500) { throw 'Over 500 SYSVOL directories; narrow scope before cleanup.' }
    foreach($dir in $dirs) {
        if ($dir.Name.StartsWith('.')) { continue }
        if (-not (SafeId $dir.Name)) { continue }
        try {
            [void]$result.Add((Read-VerifiedFolder $dir.FullName $dir.Name))
        }
        catch {
            [void]$result.Add([pscustomobject]@{
                id=$dir.Name;path=$dir.FullName;valid=$false
                reason='Invalid or unverified campaign files';imageCount=0
                imageBytes=0;endUtc='';fingerprint='';slides=@()
            })
        }
    }
    return $result.ToArray()
}
function Gpo-References($scope) {
    # Every GPO in the domain must have a readable GPT before deletion is offered.
    # Text/binary policies may still contain external references unknown to GPMC.
    $auth=[DirectoryServices.AuthenticationTypes]::Secure -bor
        [DirectoryServices.AuthenticationTypes]::Signing -bor
        [DirectoryServices.AuthenticationTypes]::Sealing
    $root=[DirectoryServices.DirectoryEntry]::new(
        ('LDAP://{0}/CN=Policies,CN=System,{1}' -f $scope.dc,$scope.base),
        $null,$null,$auth)
    $search=[DirectoryServices.DirectorySearcher]::new($root)
    $rows=New-Object 'System.Collections.Generic.List[object]'
    try {
        $search.Filter='(objectClass=groupPolicyContainer)'
        $search.PageSize=250
        [void]$search.PropertiesToLoad.Add('name')
        [void]$search.PropertiesToLoad.Add('displayName')
        $items=$search.FindAll()
        try {
            foreach($item in $items) {
                if ($rows.Count -ge 2500) { throw 'GPO count exceeds bounded audit.' }
                $id=[string]$item.Properties['name'][0]
                if ($id -notmatch '^\{[0-9a-fA-F-]{36}\}$') {
                    throw 'Unrecognized GPO GUID; cannot complete reference audit.'
                }
                $gpt=Join-Path $scope.policies $id
                if (-not (Test-Path -LiteralPath $gpt -PathType Container)) {
                    throw 'Missing GPO SYSVOL directory; replication may be incomplete.'
                }
                Assert-NoLink $gpt
                $allDirs=@(Get-ChildItem -LiteralPath $gpt -Directory -Recurse -Force)
                if ($allDirs.Count -gt 5000) { throw 'GPO directory tree exceeds bounded audit.' }
                foreach($directory in $allDirs) { Assert-NoLink $directory.FullName }
                $files=@(Get-ChildItem -LiteralPath $gpt -File -Recurse -Force)
                if ($files.Count -gt 5000) { throw 'GPO has too many files to audit safely.' }
                $references=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
                foreach($f in $files) {
                    Assert-NoLink $f.FullName
                    # Scan every readable GPO file: references can be embedded
                    # in binary files, not just .ini/.xml/.pol. Fail closed on
                    # content too large to audit. This affects cleanup only.
                    if ($f.Length -gt 2097152) {
                        throw 'Oversized GPO policy data cannot be safely audited.'
                    }
                    if ($f.Length -eq 0) { continue }
                    $raw=[IO.File]::ReadAllBytes($f.FullName)
                    foreach($encoding in @([Text.Encoding]::Unicode,
                            [Text.Encoding]::BigEndianUnicode,[Text.Encoding]::UTF8)) {
                        $body=$encoding.GetString($raw)
                        foreach($match in [regex]::Matches($body,
                                '(?i)(?:\\|/)LockScreenCampaigns(?:\\|/)([a-z0-9_-]+)(?:\\|/|$)')) {
                            [void]$references.Add($match.Groups[1].Value)
                        }
                    }
                }
                [void]$rows.Add([pscustomobject]@{
                    id=$id;name=[string]$item.Properties['displayname'][0]
                    campaignIds=@($references)
                })
            }
        } finally { $items.Dispose() }
    } finally { $search.Dispose();$root.Dispose() }
    return $rows.ToArray()
}
function Analyze($inventory,$gpos,$utc,[bool]$auditComplete=$true) {
    $first=@{}
    foreach($folder in $inventory) {
        if (-not $folder.valid) { continue }
        foreach($slide in $folder.slides) {
            if (-not $first.ContainsKey($slide.hash)) {
                $first[$slide.hash]=$folder.id
            }
        }
    }
    $result=@()
    foreach($folder in $inventory) {
        $gpoHits=@($gpos | Where-Object { $_.campaignIds -contains $folder.id })
        $duplicates=0
        [long]$duplicateBytes=0
        if ($folder.valid) {
            foreach($slide in $folder.slides) {
                if ($first[$slide.hash] -cne $folder.id) {
                    $duplicates++;$duplicateBytes+=$slide.bytes
                }
            }
        }
        $past=$false
        if ($folder.valid) {
            $past=[datetime]::Parse($folder.endUtc).ToUniversalTime().AddDays(30) -le $utc
        }
        $result+= [pscustomobject]@{
            campaignId=$folder.id;valid=[bool]$folder.valid
            endUtc=$folder.endUtc;imageCount=$folder.imageCount;imageBytes=$folder.imageBytes
            duplicateImages=$duplicates;duplicateBytes=$duplicateBytes
            gpoReferences=@($gpoHits | ForEach-Object { $_.name })
            gpoCount=$gpoHits.Count;cleanupEligible=($auditComplete -and $folder.valid -and $past -and $gpoHits.Count -eq 0)
            reason=$(if (-not $auditComplete) {'GPO reference audit incomplete; cleanup disabled'}
                elseif (-not $folder.valid) {'Invalid files: manual inspection required'}
                elseif (-not $past) {'Campaign is active or within 30-day grace period'}
                elseif ($gpoHits.Count -gt 0) {'GPO still references this campaign'}
                else {'Review eligible campaign before cleanup'})
        }
    }
    return $result
}
function Read-PackageComparison([string]$package,$inventory) {
    if ([string]::IsNullOrWhiteSpace($package)) { return $null }
    if (-not (Test-Path -LiteralPath $package -PathType Container)) {
        throw 'Local comparison package directory is unavailable.'
    }
    $id=Split-Path $package -Leaf
    $incoming=Read-VerifiedFolder $package $id $false
    $known=@{}
    foreach($existing in $inventory) {
        if (-not $existing.valid) { continue }
        foreach($s in $existing.slides) { $known[$s.hash]=$true }
    }
    $matches=@($incoming.slides | Where-Object { $known.ContainsKey($_.hash) })
    [long]$bytes=0
    foreach($s in $matches) { $bytes+=$s.bytes }
    return [pscustomobject]@{
        imageCount=$incoming.imageCount;duplicateImages=$matches.Count
        duplicateBytes=$bytes;totalBytes=$incoming.imageBytes
        note='Identical JPEGs are independently copied; SYSVOL has no shared-file links.'
    }
}
function Guard-Plan($scope,$inventory,$gpos,[string]$id,[string]$timestamp) {
    if (-not (SafeId $id)) { throw 'Invalid campaign ID for cleanup.' }
    if (-not $scope.writable) { throw 'Writable DC could not be verified; cleanup blocked.' }
    Assert-DcClock $scope
    # A deployment/Reuse staging directory may be in flight: never delete
    # source folders while there is an unfinished SYSVOL copy transaction.
    $staging=@(Get-ChildItem -LiteralPath $scope.root -Directory -Force |
        Where-Object { $_.Name -like '.staging-*' -or $_.Name -like '.lscm-stage-*' })
    if ($staging.Count -gt 0) {
        throw 'SYSVOL contains staging directories; resolve incomplete/concurrent deployment before cleanup.'
    }
    $matches=@($inventory | Where-Object { $_.id -ceq $id })
    if ($matches.Count -ne 1 -or -not $matches[0].valid) {
        throw 'Campaign folder missing or not fully verified.'
    }
    $folder=$matches[0]
    $end=[datetime]::Parse($folder.endUtc).ToUniversalTime()
    if ($scope.dcUtc -lt $end.AddDays(30)) {
        throw 'Campaign has not passed its 30-day post-end grace period.'
    }
    $hit=@($gpos | Where-Object { $_.campaignIds -contains $id })
    if ($hit.Count -gt 0) { throw 'At least one GPO still references this campaign.' }
    $plan=Sha ($scope.domain+'|'+$scope.dc+'|'+$id+'|'+$folder.fingerprint+
        '|'+$timestamp+'|'+$gpos.Count)
    return [pscustomobject]@{
        campaignId=$id;path=$folder.path;previewUtc=$timestamp;planHash=$plan
        imageCount=$folder.imageCount;imageBytes=$folder.imageBytes
        note='No GPO file reference found on selected DC. Check offline PCs, custom references and DFSR replication before approving deletion.'
    }
}
try {
    $req=Get-Content -LiteralPath $RequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $scope=Get-Scope $req
    if (-not (Test-Path -LiteralPath $scope.root -PathType Container)) {
        if ($Mode -ne 'Scan') { throw 'SYSVOL LockScreenCampaigns directory does not exist.' }
    }
    $inventory=@(Inventory $scope)
    $auditComplete=$true
    $gpos=@()
    try { $gpos=@(Gpo-References $scope) }
    catch {
        $auditComplete=$false
        if ($Mode -ne 'Scan') { throw 'GPO reference audit is incomplete; cleanup is blocked.' }
    }
    switch($Mode) {
        'Scan' {
            $details=@(Analyze $inventory $gpos $scope.dcUtc $auditComplete)
            $comparison=Read-PackageComparison ([string]$req.packagePath) $inventory
            Reply $true 'Read-only SYSVOL inventory and GPO reference audit complete.' (
                [pscustomobject]@{
                    domain=$scope.domain;dc=$scope.dc;root=$scope.root
                    writable=$scope.writable;gpoAudited=$gpos.Count
                    gpoAuditComplete=$auditComplete
                    dcUtc=$scope.dcUtc.ToString('o');clockSkewSeconds=$scope.skewSeconds
                    campaigns=$details;comparison=$comparison
                })
        }
        'Preview' {
            $timestamp=$scope.dcUtc.ToString('o')
            $plan=Guard-Plan $scope $inventory $gpos ([string]$req.campaignId) $timestamp
            Reply $true 'Read-only cleanup preview complete; no files changed.' $plan
        }
        'Cleanup' {
            $id=[string]$req.campaignId
            $stamp=[string]$req.previewUtc
            $when=[datetime]::MinValue
            if (-not [datetime]::TryParse($stamp,[ref]$when)) {
                throw 'Cleanup preview timestamp is invalid.'
            }
            $when=$when.ToUniversalTime()
            if ($when -gt $scope.dcUtc.AddSeconds(3) -or
                $when.AddMinutes(5) -lt $scope.dcUtc) {
                throw 'Cleanup preview expired; preview again.'
            }
            $plan=Guard-Plan $scope $inventory $gpos $id $stamp
            if ([string]$req.planHash -cne $plan.planHash -or
                [string]$req.approveId -cne $id) {
                throw 'Cleanup approval or source fingerprint mismatch.'
            }
            # All files, manifest hashes and all GPOs were inspected again
            # in THIS execution. No GPO, OU or registry is changed.
            $folder=$plan.path
            if ((Split-Path $folder -Parent) -ine $scope.root -or
                (Split-Path $folder -Leaf) -cne $id) {
                throw 'Target directory is outside exact verified SYSVOL root.'
            }
            Assert-NoLink $folder
            Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction Stop
            if (Test-Path -LiteralPath $folder) {
                throw 'Cleanup could not verify complete directory removal.'
            }
            Reply $true 'Campaign source folder deleted after explicit cleanup confirmation.' (
                [pscustomobject]@{campaignId=$id;path=$folder;deleted=$true
                    note='GPOs and client computers were not modified.'})
        }
    }
}
catch {
    Reply $false $_.Exception.Message $null
    exit 1
}
