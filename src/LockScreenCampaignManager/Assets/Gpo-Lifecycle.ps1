#requires -Version 5.1
# Universal Active Directory GPO lifecycle; no RSAT/WinRM.
# Only manages GPOs whose computer Startup script matches our campaign manifest.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RequestPath,
    [Parameter(Mandatory=$true)][string]$ResponsePath,
    [ValidateSet('List','Preview','Apply')][string]$Mode='List'
)
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.DirectoryServices

function Result([bool]$ok,[string]$message,$data) {
    [IO.File]::WriteAllText($ResponsePath,([ordered]@{success=$ok;message=$message;data=$data} | ConvertTo-Json -Depth 12 -Compress),[Text.UTF8Encoding]::new($false))
}
function Prop($e,[string]$name) { if ($e.Properties[$name].Count -gt 0) { return $e.Properties[$name][0] }; return $null }
function Entry([string]$dc,[string]$dn) {
    $e=[DirectoryServices.DirectoryEntry]::new(('LDAP://{0}/{1}' -f $dc,$dn),$null,$null,([DirectoryServices.AuthenticationTypes]::Secure -bor [DirectoryServices.AuthenticationTypes]::Signing -bor [DirectoryServices.AuthenticationTypes]::Sealing))
    try { $e.RefreshCache();return $e } catch { $e.Dispose();throw }
}
function Hash([string]$s) {
    $h=[Security.Cryptography.SHA256]::Create()
    try { $b=$h.ComputeHash([Text.Encoding]::UTF8.GetBytes($s));return [BitConverter]::ToString($b).Replace('-','').ToLowerInvariant() }
    finally { $h.Dispose() }
}
function IsInDomain([string]$dn,[string]$base) {
    return ($dn -ieq $base -or $dn.EndsWith(','+$base,[StringComparison]::OrdinalIgnoreCase))
}
function Parse-DcTime([string]$value) {
    if ($value -notmatch '^(?<stamp>\d{14})(?:\.\d{1,7})?Z$') {
        throw 'Selected DC currentTime cannot be verified.'
    }
    return [datetime]::ParseExact($Matches['stamp'],'yyyyMMddHHmmss',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal -bor
        [Globalization.DateTimeStyles]::AdjustToUniversal)
}
function DomainInfo([string]$requestedDomain,[string]$requestedDc) {
    if ($requestedDomain -notmatch '^(?=.{3,253}$)[a-z0-9-]+(\.[a-z0-9-]+)+$') { throw 'Invalid DNS domain.' }
    $hostName=if ($requestedDc) {$requestedDc} else {$requestedDomain}
    if ($hostName -notmatch '^[a-zA-Z0-9.-]{1,253}$' -or $hostName -match '^\d+\.\d+\.\d+\.\d+$') { throw 'Use a DC hostname, not an IP.' }
    $e=Entry $hostName 'RootDSE'
    try {
        $e.RefreshCache(@('defaultNamingContext','dnsHostName','isRODC','currentTime'))
        $base=[string](Prop $e 'defaultNamingContext')
        $dns=([regex]::Matches($base,'(?i)(?:^|,)DC=([^,]+)') | ForEach-Object { $_.Groups[1].Value }) -join '.'
        if ($dns -ine $requestedDomain) { throw ('Selected DC belongs to another domain: '+$dns) }
        if ([string](Prop $e 'isRODC') -ine 'FALSE') { throw 'Writable DC must be verified.' }
        $dcUtc=Parse-DcTime ([string](Prop $e 'currentTime'))
        $skew=[int][math]::Round(([datetime]::UtcNow-$dcUtc).TotalSeconds)
        $dc=[string](Prop $e 'dnsHostName');if (-not $dc) {$dc=$hostName}
        return [pscustomobject]@{dns=$dns;dc=$dc;base=$base
            dcUtc=$dcUtc;clockSkewSeconds=$skew}
    } finally { $e.Dispose() }
}
function GetLinks([string]$raw) {
    $list=@()
    foreach ($m in [regex]::Matches($raw,'(?i)\[LDAP://(?<dn>[^;\]]+);(?<flags>\d+)\]')) {
        $list+= [pscustomobject]@{text=$m.Value;dn=$m.Groups['dn'].Value;flags=[int]$m.Groups['flags'].Value;index=$m.Index}
    }
    return $list
}
function GpoPath([string]$dc,[string]$domain,[guid]$id) {
    return ('\\{0}\SYSVOL\{1}\Policies\{{{2}}}' -f $dc,$domain,$id.ToString().ToUpperInvariant())
}
function ManagedGpo([string]$dc,[string]$domain,[guid]$id) {
    $root=GpoPath $dc $domain $id
    $ini=Join-Path $root 'Machine\Scripts\psscripts.ini'
    if (-not (Test-Path -LiteralPath $ini -PathType Leaf)) { return $null }
    $text=[IO.File]::ReadAllText($ini,[Text.Encoding]::Unicode)
    if ($text -notmatch '(?im)^0CmdLine=(?<script>\\\\[^\r\n]+\\scripts\\LockScreenCampaigns\\(?<id>[a-z0-9_-]+)\\LockScreen-Agent\.ps1)\s*$') { return $null }
    $path=$Matches['script'];$campaignId=$Matches['id']
    if ($text -notmatch '(?im)^0Parameters=-Mode Install\s*$') { return $null }
    $source=('\\{0}\SYSVOL\{1}\scripts\LockScreenCampaigns\{2}' -f $dc,$domain,$campaignId)
    $cfgPath=Join-Path $source 'campaign.json'
    if (-not (Test-Path -LiteralPath $cfgPath -PathType Leaf)) { return $null }
    $cfg=Get-Content -LiteralPath $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$cfg.campaignId -cne $campaignId -or [int]$cfg.schemaVersion -ne 1) { return $null }
    if ($text -match '(?im)^1CmdLine=') { return $null }
    return [pscustomobject]@{id=$id.ToString('D');campaignId=$campaignId;end=[string]$cfg.endLocal;zone=[string]$cfg.timeZoneId;script=$path;source=$source;gpt=$root;ini=$ini}
}
function AllDomainLinks([string]$dc,[string]$base,[string]$gpoDn) {
    $root=Entry $dc $base
    $search=New-Object DirectoryServices.DirectorySearcher($root)
    try {
        $search.Filter='(|(objectClass=organizationalUnit)(objectClass=domainDNS))'
        $search.PageSize=500
        [void]$search.PropertiesToLoad.Add('gPLink')
        $rows=$search.FindAll()
        try {
            $result=@()
            foreach ($row in $rows) {
                $value=if ($row.Properties['gplink'].Count -gt 0) {[string]$row.Properties['gplink'][0]}else{''}
                foreach ($link in @(GetLinks $value)) {
                    if ($link.dn -ieq $gpoDn) {
                        $result+= [pscustomobject]@{ou=$row.Path -replace '^LDAP://[^/]+/','';flags=$link.flags;enabled=(($link.flags -band 1) -eq 0);enforced=(($link.flags -band 2) -ne 0)}
                    }
                }
            }
            return $result
        } finally {$rows.Dispose()}
    } finally {$search.Dispose();$root.Dispose()}
}
function AllSiteLinks([string]$dc,[string]$gpoDn) {
    $root=Entry $dc 'RootDSE'
    try {$config=[string](Prop $root 'configurationNamingContext')}
    finally {$root.Dispose()}
    if ([string]::IsNullOrWhiteSpace($config)) { throw 'Cannot inspect site policy links: configurationNamingContext missing.' }
    $sites=Entry $dc ('CN=Sites,'+$config)
    $search=New-Object DirectoryServices.DirectorySearcher($sites)
    try {
        $search.Filter='(objectClass=site)';$search.PageSize=250
        [void]$search.PropertiesToLoad.Add('gPLink')
        $items=$search.FindAll()
        try {
            $links=@()
            foreach($item in $items) {
                $val=if($item.Properties['gplink'].Count -gt 0){[string]$item.Properties['gplink'][0]}else{''}
                foreach($link in @(GetLinks $val)) {
                    if($link.dn -ieq $gpoDn) {
                        $links+=[pscustomobject]@{ou=$item.Path;flags=$link.flags;
                            enabled=(($link.flags -band 1) -eq 0);enforced=(($link.flags -band 2) -ne 0);type='Site'}
                    }
                }
            }
            return $links
        }finally{$items.Dispose()}
    }finally{$search.Dispose();$sites.Dispose()}
}
function ReadGpo([string]$dc,[string]$base,[guid]$id) {
    $dn='CN={'+$id.ToString().ToUpperInvariant()+'},CN=Policies,CN=System,'+$base
    $e=Entry $dc $dn
    try {
        if (@($e.Properties['objectClass'] | ForEach-Object { [string]$_ }) -notcontains 'groupPolicyContainer') { throw 'Target is not a GPO.' }
        return [pscustomobject]@{dn=$dn;name=[string](Prop $e 'displayName');version=[int64](Prop $e 'versionNumber');flags=[int](Prop $e 'flags')}
    } finally {$e.Dispose()}
}
function ReadState($info,[string]$ouDn,[guid]$id) {
    $gpo=ReadGpo $info.dc $info.base $id
    $managed=ManagedGpo $info.dc $info.dns $id
    if ($null -eq $managed) { throw 'GPO not recognized as a managed LockScreen campaign. Refusing to modify.' }
    if (-not (IsInDomain $ouDn $info.base)) { throw 'OU must belong to the selected domain.' }
    $ou=Entry $info.dc $ouDn
    try {
        if (@($ou.Properties['objectClass'] | ForEach-Object { [string]$_ }) -notcontains 'organizationalUnit') { throw 'Selected target is not an OU.' }
        $raw=[string](Prop $ou 'gPLink')
        $matches=@(GetLinks $raw | Where-Object {$_.dn -ieq $gpo.dn})
        if ($matches.Count -gt 1) { throw 'Duplicate GPO links detected; fix manually.' }
        $link=if ($matches.Count -eq 1) {$matches[0]}else{$null}
        return [pscustomobject]@{gpo=$gpo;managed=$managed;ouDn=$ouDn;gplink=$raw;link=$link;
            links=(@(AllDomainLinks $info.dc $info.base $gpo.dn)+@(AllSiteLinks $info.dc $gpo.dn))}
    } finally {$ou.Dispose()}
}
function ReadPackage([string]$folder,[datetime]$dcUtc) {
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) { throw 'Export campaign first; package folder not found.' }
    $manifest=Join-Path $folder 'campaign.json'
    $agent=Join-Path $folder 'LockScreen-Agent.ps1'
    if (-not (Test-Path -LiteralPath $manifest) -or -not (Test-Path -LiteralPath $agent)) { throw 'Missing campaign manifest or agent.' }
    $cfg=Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
    $id=[string]$cfg.campaignId
    if ($id -cnotmatch '^[a-z0-9][a-z0-9_-]{0,70}$') { throw 'Invalid campaign ID.' }
    $zone=[TimeZoneInfo]::FindSystemTimeZoneById([string]$cfg.timeZoneId)
    $end=[datetime]::ParseExact([string]$cfg.endLocal,'yyyy-MM-ddTHH:mm:ss',[Globalization.CultureInfo]::InvariantCulture)
    if ([TimeZoneInfo]::ConvertTimeFromUtc($dcUtc,$zone) -ge $end) { throw 'New campaign has already ended.' }
    if (@($cfg.slides).Count -lt 1 -or @($cfg.slides).Count -gt 30) { throw 'Invalid slide count.' }
    $previous=[datetime]::MinValue
    foreach($slide in @($cfg.slides)) {
        $file=[string]$slide.fileName
        $at=[datetime]::ParseExact([string]$slide.atLocal,'yyyy-MM-ddTHH:mm:ss',[Globalization.CultureInfo]::InvariantCulture)
        if ($at -le $previous -or $at -ge $end) { throw 'Invalid slide schedule.' }
        if ($zone.IsAmbiguousTime($at) -or $zone.IsInvalidTime($at)) { throw 'Ambiguous/invalid DST time.' }
        $previous=$at
        if ($file -cnotmatch '^[0-9]{2}\.jpg$') { throw 'Unsafe image file name.' }
        $p=Join-Path (Join-Path $folder 'images') $file
        if ((Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ine [string]$slide.sha256) { throw 'Image SHA256 mismatch.' }
    }
    return [pscustomobject]@{id=$id;cfg=$cfg;end=$end;zone=$zone}
}
function Plan($info,$state,[string]$action,[string]$packagePath) {
    if ($action -notin @('Disable','Enable','Unlink','Relink','Delete','Reuse')) { throw 'Unsupported action.' }
    if ($action -in @('Disable','Enable','Unlink') -and $null -eq $state.link) { throw 'GPO is not linked to selected OU.' }
    if ($action -eq 'Relink' -and $null -ne $state.link) { throw 'GPO is already linked to selected OU.' }
    if ($action -eq 'Delete' -and @($state.links).Count -gt 0) { throw 'Unlink the GPO from all OUs/domain first. GPO cannot be deleted while linked.' }
    $newId=''
    $packageHash=''
    if ($action -eq 'Reuse') {
        if ([math]::Abs($info.clockSkewSeconds) -gt 300) {
            throw 'Computer and selected DC clocks differ by more than 5 minutes; synchronize Windows Time.'
        }
        $package=ReadPackage $packagePath $info.dcUtc
        $newId=$package.id
        $packageHash=(Get-FileHash -LiteralPath (Join-Path $packagePath 'campaign.json') -Algorithm SHA256).Hash + ':' + (Get-FileHash -LiteralPath (Join-Path $packagePath 'LockScreen-Agent.ps1') -Algorithm SHA256).Hash
        if ($newId -eq $state.managed.campaignId) { throw 'Reuse requires a NEW campaignId. Change campaign name or date.' }
        $oldSource=Join-Path $state.managed.source 'campaign.json'
        $oldCfg=Get-Content -LiteralPath $oldSource -Raw -Encoding UTF8 | ConvertFrom-Json
        $oldEnd=[datetime]::ParseExact([string]$oldCfg.endLocal,'yyyy-MM-ddTHH:mm:ss',[Globalization.CultureInfo]::InvariantCulture)
        $oldZone=[TimeZoneInfo]::FindSystemTimeZoneById([string]$oldCfg.timeZoneId)
        if ([TimeZoneInfo]::ConvertTimeFromUtc($info.dcUtc,$oldZone) -lt $oldEnd) { throw 'Current campaign is still active; wait until it ends and cleans up.' }
        $newDest='\\{0}\SYSVOL\{1}\scripts\LockScreenCampaigns\{2}' -f $info.dc,$info.dns,$newId
        if (Test-Path -LiteralPath $newDest) { throw 'New campaign ID already present in SYSVOL.' }
    }
    $iniHash=(Get-FileHash -LiteralPath $state.managed.ini -Algorithm SHA256).Hash
    $gptIni=Join-Path $state.managed.gpt 'gpt.ini'
    if (-not (Test-Path -LiteralPath $gptIni)) { throw 'GPO gpt.ini not found.' }
    $gptText=[IO.File]::ReadAllText($gptIni)
    if ($gptText -notmatch '(?im)^Version=(\d+)\s*$') { throw 'Invalid gpt.ini version.' }
    $diskVersion=[int64]$Matches[1]
    if ($diskVersion -ne $state.gpo.version) { throw 'GPO version mismatch between AD and SYSVOL.' }
    $source=($info.dns+'|'+$info.dc+'|'+$state.gpo.dn+'|'+$state.ouDn+'|'+$state.gplink+'|'+
        $state.gpo.version+'|'+$state.gpo.flags+'|'+$iniHash+'|'+$action+'|'+$newId+'|'+$packageHash)
    $hash=Hash $source
    return [pscustomobject]@{action=$action;name=$state.gpo.name;gpoId=$state.managed.id;campaignId=$state.managed.campaignId;
        newCampaignId=$newId;domain=$info.dns;dc=$info.dc;ou=$state.ouDn;
        linked=($null -ne $state.link);linkEnabled=($null -ne $state.link -and (($state.link.flags -band 1) -eq 0));
        links=@($state.links);hash=$hash;version=$diskVersion;
        note=$(switch($action){
            Disable {'Disables ONLY the chosen OU link. Already installed local tasks CONTINUE until separately cleaned up.'}
            Enable {'Re-enables ONLY the chosen OU link. Expired campaigns do not restart. Use Reuse to replace the manifest.'}
            Unlink {'Removes ONLY the selected OU link. GPO and SYSVOL content stay available.'}
            Relink {'Recreates an enabled link to the selected OU. Existing GPO and security filtering stay unchanged.'}
            Delete {'Permanently deletes the UNLINKED GPO. The campaign source folder stays in SYSVOL for separate manual review.'}
            Reuse {'Updates SAME GPO with a NEW campaign manifest/script. Keeps its GUID, link states and security filtering unchanged.'}
        })
    }
}
function ReplaceLink($info,$state,[string]$action) {
    $link=$state.link
    $ou=Entry $info.dc $state.ouDn
    try {
        $actual=[string](Prop $ou 'gPLink')
        if ($actual -cne $state.gplink) { throw 'OU gPLink changed after preview; refresh.' }
        if ($action -eq 'Unlink') { $new=$actual.Replace($link.text,'') }
        elseif ($action -eq 'Relink') { $new='[LDAP://'+$state.gpo.dn+';0]'+$actual }
        else {
            $flags=[int]$link.flags
            if ($action -eq 'Disable') { $flags=$flags -bor 1 }
            else { $flags=$flags -band (-bnot 1) }
            $new=$actual.Replace($link.text,('['+'LDAP://'+$link.dn+';'+$flags+']'))
        }
        if ([string]::IsNullOrEmpty($new)) { $ou.Properties['gPLink'].Clear() }
        else { $ou.Properties['gPLink'].Value=$new }
        $ou.CommitChanges()
        $ou.RefreshCache(@('gPLink'))
        if ([string](Prop $ou 'gPLink') -cne $new) { throw 'OU link update verification failed.' }
    } finally {$ou.Dispose()}
}
function CopyAndVerify($package,[string]$target) {
    New-Item -ItemType Directory -Path (Join-Path $target 'images') -Force | Out-Null
    foreach($leaf in @('campaign.json','LockScreen-Agent.ps1')) {
        Copy-Item -LiteralPath (Join-Path $package $leaf) -Destination (Join-Path $target $leaf) -ErrorAction Stop
    }
    $cfg=Get-Content -LiteralPath (Join-Path $package 'campaign.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach($s in @($cfg.slides)) {
        $leaf=[string]$s.fileName
        Copy-Item -LiteralPath (Join-Path (Join-Path $package 'images') $leaf) -Destination (Join-Path (Join-Path $target 'images') $leaf) -ErrorAction Stop
    }
    foreach ($f in @(Get-ChildItem -LiteralPath $package -File) + @(Get-ChildItem -LiteralPath (Join-Path $package 'images') -File)) {
        $out=if ($f.Directory.Name -eq 'images') { Join-Path (Join-Path $target 'images') $f.Name }else{Join-Path $target $f.Name}
        if (Test-Path -LiteralPath $out) {
            if ((Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash -ine (Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash) { throw 'SYSVOL package hash mismatch.' }
        }
    }
}
function ReuseGpo($info,$state,[string]$packageFolder,[string]$newId) {
    $package=ReadPackage $packageFolder $info.dcUtc
    $sourceBase='\\{0}\SYSVOL\{1}\scripts\LockScreenCampaigns' -f $info.dc,$info.dns
    $newDir=Join-Path $sourceBase $newId
    $stage=Join-Path $sourceBase ('.lscm-stage-'+[guid]::NewGuid().ToString('N'))
    if (Test-Path -LiteralPath $newDir) { throw 'New source directory already exists.' }
    $ini=$state.managed.ini;$gpt=Join-Path $state.managed.gpt 'gpt.ini'
    $oldIni=[IO.File]::ReadAllBytes($ini);$oldGpt=[IO.File]::ReadAllBytes($gpt)
    $oldVersion=$state.gpo.version
    $updatedIni=$false;$updatedGpt=$false;$updatedAd=$false
    try {
        CopyAndVerify $packageFolder $stage
        Move-Item -LiteralPath $stage -Destination $newDir
        $client='\\{0}\SYSVOL\{0}\scripts\LockScreenCampaigns\{1}\LockScreen-Agent.ps1' -f $info.dns,$newId
        if ($client.Length -ge 260) { throw 'New script UNC path too long.' }
        $iniText="[ScriptsConfig]`r`nStartExecutePSFirst=true`r`n[Startup]`r`n0CmdLine=$client`r`n0Parameters=-Mode Install`r`n"
        $newVer=[int64]($oldVersion - ($oldVersion -band 65535) + (($oldVersion+1) -band 65535))
        if (($oldVersion -band 0xFFFF) -ge 65535) { throw 'GPO computer version overflow.' }
        [IO.File]::WriteAllText($ini,$iniText,[Text.UnicodeEncoding]::new($false,$true))
        $updatedIni=$true
        $gptText=[IO.File]::ReadAllText($gpt)
        $versionMatch=[regex]::Match($gptText,'(?im)^Version=(?<version>\d+)\s*$')
        if (-not $versionMatch.Success -or [int64]$versionMatch.Groups['version'].Value -ne $oldVersion) {
            throw 'Unexpected GPT version while reusing GPO. Reload and preview.'
        }
        $gptText=[regex]::Replace($gptText,'(?im)^Version=\d+\s*$',('Version='+$newVer))
        [IO.File]::WriteAllText($gpt,$gptText,[Text.Encoding]::ASCII)
        $updatedGpt=$true
        $gpo=Entry $info.dc $state.gpo.dn
        try {
            if ([int64](Prop $gpo 'versionNumber') -ne $oldVersion) { throw 'GPO changed during reuse.' }
            $gpo.Properties['versionNumber'].Value=[int64]$newVer
            $gpo.CommitChanges();$updatedAd=$true
        } finally {$gpo.Dispose()}
        if ((ManagedGpo $info.dc $info.dns ([guid]$state.managed.id)).campaignId -cne $newId) { throw 'New GPO Startup reference not verified.' }
        return $newVer
    } catch {
        # Best-effort rollback: source package is preserved so administrators can diagnose replication.
        try {if ($updatedIni) {[IO.File]::WriteAllBytes($ini,$oldIni)}} catch {}
        try {if ($updatedGpt) {[IO.File]::WriteAllBytes($gpt,$oldGpt)}} catch {}
        try {if ($updatedAd) { $gpo=Entry $info.dc $state.gpo.dn;try{$gpo.Properties['versionNumber'].Value=[int64]$oldVersion;$gpo.CommitChanges()}finally{$gpo.Dispose()} }} catch {}
        throw ('Reuse failed (rollback attempted; verify replication manually): '+$_.Exception.Message)
    } finally {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
function DeleteGpo($info,[guid]$id) {
    # The COM interface is the built-in Windows gpedit.dll API, not GroupPolicy PowerShell/RSAT.
    if (-not ('LscmNativeGpo.GpoObject' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
namespace LscmNativeGpo {
 [ComImport, Guid("EA502722-A23D-11d1-A7D3-0000F87571E3")]
 public class GpoObject { }
 [ComImport, Guid("EA502723-A23D-11d1-A7D3-0000F87571E3"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 public interface IGpo {
  void New([MarshalAs(UnmanagedType.LPWStr)]string domain,[MarshalAs(UnmanagedType.LPWStr)]string name,uint flags);
  void OpenDSGPO([MarshalAs(UnmanagedType.LPWStr)]string path,uint flags);
  void OpenLocalMachineGPO(uint flags);
  void OpenRemoteMachineGPO([MarshalAs(UnmanagedType.LPWStr)]string path,uint flags);
  void Save([MarshalAs(UnmanagedType.Bool)]bool machine,[MarshalAs(UnmanagedType.Bool)]bool add,ref Guid extension,ref Guid snapin);
  void Delete();
  void GetName([MarshalAs(UnmanagedType.LPWStr)]StringBuilder name,int capacity);
 }
}
'@ -Language CSharp
    }
    $gpo=ReadGpo $info.dc $info.base $id
    $p='LDAP://'+$info.dc+'/'+$gpo.dn
    $obj=[LscmNativeGpo.IGpo]([Activator]::CreateInstance([type][LscmNativeGpo.GpoObject]))
    try {
        $obj.OpenDSGPO($p,0)
        $obj.Delete()
    } finally {if ($null -ne $obj) {[void][Runtime.InteropServices.Marshal]::ReleaseComObject($obj)}}
    # Never auto-delete source images; user can archive them outside of SYSVOL separately.
}
try {
    $req=Get-Content -LiteralPath $RequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $domain=([string]$req.domain).Trim().ToLowerInvariant();$dc=([string]$req.dc).Trim()
    $info=DomainInfo $domain $dc
    if ($Mode -eq 'List') {
        $policies=Entry $info.dc ('CN=Policies,CN=System,'+$info.base)
        $search=New-Object DirectoryServices.DirectorySearcher($policies)
        try {
            $search.Filter='(objectClass=groupPolicyContainer)';$search.PageSize=250
            [void]$search.PropertiesToLoad.Add('displayName')
            $rows=$search.FindAll()
            try {
                $list=@()
                foreach($row in $rows) {
                    $match=[regex]::Match($row.Path,'(?i)CN=\{(?<id>[0-9a-f-]{36})\},CN=Policies,')
                    if (-not $match.Success) { continue }
                    $id=[guid]$match.Groups['id'].Value
                    $managed=ManagedGpo $info.dc $info.dns $id
                    if ($null -eq $managed) { continue }
                    $gpo=ReadGpo $info.dc $info.base $id
                    $links=@(AllDomainLinks $info.dc $info.base $gpo.dn)+@(AllSiteLinks $info.dc $gpo.dn)
                    $list+=[pscustomobject]@{id=$managed.id;name=$gpo.name;campaignId=$managed.campaignId;
                        end=$managed.end;timeZone=$managed.zone;links=$links;linkCount=$links.Count;gpoFlags=$gpo.flags}
                }
                Result $true 'Read-only list of GPOs managed by this application.' @($list)
            } finally {$rows.Dispose()}
        } finally {$search.Dispose();$policies.Dispose()}
        exit 0
    }
    $id=[guid]([string]$req.gpoId)
    $ouDn=[string]$req.ouDn
    $action=[string]$req.action
    $package=if($req.PSObject.Properties.Name -contains 'packagePath'){[string]$req.packagePath}else{''}
    $state=ReadState $info $ouDn $id
    $plan=Plan $info $state $action $package
    if ($Mode -eq 'Preview') {Result $true 'Preview is read-only. No changes made.' $plan;exit 0}
    if ([string]$req.approveGuid -ine $id.ToString('D') -or [string]$req.planHash -cne $plan.hash) {
        throw 'Confirmation/preview mismatch. Refresh and confirm the exact GPO GUID.'
    }
    switch ($action) {
        'Disable' {ReplaceLink $info $state 'Disable'}
        'Enable' {ReplaceLink $info $state 'Enable'}
        'Unlink' {ReplaceLink $info $state 'Unlink'}
        'Relink' {ReplaceLink $info $state 'Relink'}
        'Delete' {DeleteGpo $info $id}
        'Reuse' { $newVersion=ReuseGpo $info $state $package $plan.newCampaignId }
    }
    $data=[ordered]@{action=$action;gpoId=$plan.gpoId;campaignId=$plan.campaignId;ou=$ouDn;successful=$true;
        note=$plan.note;oldSourcePreserved=$true}
    if ($action -eq 'Reuse') {$data.newCampaignId=$plan.newCampaignId;$data.newVersion=$newVersion}
    Result $true 'GPO operation completed on the selected DC; allow AD/SYSVOL replication.' $data
} catch {
    Result $false $_.Exception.Message $null
    exit 1
}
