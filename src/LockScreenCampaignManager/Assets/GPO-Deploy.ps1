#requires -Version 5.1
# Native Windows GPO/ADSI deployment: NO RSAT, no ActiveDirectory or GroupPolicy modules.
# Creates only a NEW GPO and links it only after payload, ACL, and startup metadata validate.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RequestPath,
    [Parameter(Mandatory=$true)][string]$ResponsePath,
    [ValidateSet('Plan','Deploy')][string]$Mode='Plan'
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.DirectoryServices

function Send-Result {
    param([bool]$Ok,[string]$Message,$Data)
    $json=[ordered]@{success=$Ok;message=$Message;data=$Data} | ConvertTo-Json -Depth 12 -Compress
    [IO.File]::WriteAllText($ResponsePath,$json,[Text.UTF8Encoding]::new($false))
}
function Property-Value {
    param($Entry,[string]$Name)
    $values=$Entry.Properties[$Name]
    if ($null -eq $values -or $values.Count -eq 0) { return $null }
    return $values[0]
}
function Open-AdEntry {
    param([string]$DC,[string]$DN)
    $entry=[DirectoryServices.DirectoryEntry]::new(('LDAP://{0}/{1}' -f $DC,$DN),$null,$null,([DirectoryServices.AuthenticationTypes]::Secure -bor [DirectoryServices.AuthenticationTypes]::Signing -bor [DirectoryServices.AuthenticationTypes]::Sealing))
    try { $entry.RefreshCache(); return $entry }
    catch { $entry.Dispose(); throw }
}
function Dn-InDomain {
    param([string]$DN,[string]$Base)
    return ($DN -ieq $Base -or $DN.EndsWith(','+$Base,[StringComparison]::OrdinalIgnoreCase))
}
function Parse-DcTime([string]$value) {
    # Selected DC RootDSE currentTime is UTC generalized time, not local time.
    if ($value -notmatch '^(?<stamp>\d{14})(?:\.\d{1,7})?Z$') {
        throw 'Selected DC RootDSE currentTime is invalid or unavailable.'
    }
    return [datetime]::ParseExact($Matches['stamp'],'yyyyMMddHHmmss',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal -bor
        [Globalization.DateTimeStyles]::AdjustToUniversal)
}
function Get-DomainInfo {
    param([string]$RequestedDomain,[string]$RequestedDc)
    $domain=$RequestedDomain.ToLowerInvariant()
    $dc=$RequestedDc
    if (-not $dc) {
        $context=[DirectoryServices.ActiveDirectory.DirectoryContext]::new(
            [DirectoryServices.ActiveDirectory.DirectoryContextType]::Domain,$domain)
        $adDomain=[DirectoryServices.ActiveDirectory.Domain]::GetDomain($context)
        try {
            foreach ($controller in $adDomain.DomainControllers) {
                $candidate=$controller.Name
                $r=[DirectoryServices.DirectoryEntry]::new(('LDAP://{0}/RootDSE' -f $candidate),$null,$null,([DirectoryServices.AuthenticationTypes]::Secure -bor [DirectoryServices.AuthenticationTypes]::Signing -bor [DirectoryServices.AuthenticationTypes]::Sealing))
                try {
                    $r.RefreshCache(@('isRODC','defaultNamingContext'))
                    if (([string](Property-Value $r 'isRODC')) -ine 'TRUE') { $dc=$candidate;break }
                } catch {} finally { $r.Dispose() }
            }
        } finally { $adDomain.Dispose() }
        if (-not $dc) { throw 'Cannot find a writable DC.' }
    }
    $root=[DirectoryServices.DirectoryEntry]::new(('LDAP://{0}/RootDSE' -f $dc),$null,$null,([DirectoryServices.AuthenticationTypes]::Secure -bor [DirectoryServices.AuthenticationTypes]::Signing -bor [DirectoryServices.AuthenticationTypes]::Sealing))
    try {
        $root.RefreshCache(@('defaultNamingContext','dnsHostName','isRODC','currentTime'))
        $base=[string](Property-Value $root 'defaultNamingContext')
        $actual=([regex]::Matches($base,'(?i)(?:^|,)DC=([^,]+)') | ForEach-Object { $_.Groups[1].Value }) -join '.'
        if ($actual -ine $domain) { throw ('Domain mismatch: DC {0} belongs to {1}' -f $dc,$actual) }
        if (([string](Property-Value $root 'isRODC')) -ine 'FALSE') { throw ('Cannot verify writable DC: {0}' -f $dc) }
        $dcUtc=Parse-DcTime ([string](Property-Value $root 'currentTime'))
        $clockSkewSeconds=[int][math]::Round(([datetime]::UtcNow-$dcUtc).TotalSeconds)
        if ([math]::Abs($clockSkewSeconds) -gt 300) {
            throw ('Computer time differs from DC by {0} seconds. Synchronize Windows Time (max 5 min).' -f $clockSkewSeconds)
        }
        $hostName=[string](Property-Value $root 'dnsHostName')
        if (-not $hostName) { $hostName=$dc }
        return [pscustomobject]@{domain=$domain;dc=$hostName;baseDn=$base
            dcUtc=$dcUtc;clockSkewSeconds=$clockSkewSeconds}
    } finally { $root.Dispose() }
}
function Ensure-ObjectClass {
    param($Entry,[string]$Required)
    $classes=@($Entry.Properties['objectClass'] | ForEach-Object { [string]$_ })
    if (-not ($classes -contains $Required)) { throw ('Unexpected AD object class (expected {0}): {1}' -f $Required,$Entry.Path) }
}
function Assert-Package {
    param([string]$Package)
    foreach ($name in @('campaign.json','LockScreen-Agent.ps1')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Package $name) -PathType Leaf)) {
            throw ('Required package file missing: {0}' -f $name)
        }
    }
    $cfg=Get-Content -LiteralPath (Join-Path $Package 'campaign.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $id=[string]$cfg.campaignId
    if ($id -cnotmatch '^[a-z0-9][a-z0-9_-]{0,70}$') { throw 'Invalid campaign ID.' }
    if ([int]$cfg.schemaVersion -ne 1) { throw 'Unsupported campaign schema.' }
    # New packages include an immutable SHA-256 for the SYSTEM-executed script.
    # Legacy schema-1 packages without this field remain readable.
    if ($cfg.PSObject.Properties.Name -contains 'agentSha256') {
        $expected=[string]$cfg.agentSha256
        if ($expected -notmatch '^[0-9a-fA-F]{64}$' -or
            (Get-FileHash -LiteralPath (Join-Path $Package 'LockScreen-Agent.ps1') -Algorithm SHA256).Hash -ine $expected) {
            throw 'Agent SHA256 mismatch or invalid agentSha256 field.'
        }
    }
    if ($cfg.PSObject.Properties.Name -contains 'randomizePerDevice' -and
        $cfg.randomizePerDevice -isnot [bool]) {
        throw 'Invalid stable shuffle flag in campaign manifest.'
    }
    if ([string]::IsNullOrWhiteSpace([string]$cfg.timeZoneId)) { throw 'Missing timezone.' }
    $zone=[TimeZoneInfo]::FindSystemTimeZoneById([string]$cfg.timeZoneId)
    $end=[DateTime]::ParseExact([string]$cfg.endLocal,'yyyy-MM-ddTHH:mm:ss',[Globalization.CultureInfo]::InvariantCulture)
    $slides=@($cfg.slides)
    if ($slides.Count -lt 1 -or $slides.Count -gt 30) { throw 'Expected 1-30 slides.' }
    $previous=[DateTime]::MinValue
    $seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $bytes=[int64]0
    foreach ($slide in $slides) {
        $filename=[string]$slide.fileName
        if ($filename -cnotmatch '^[0-9]{2}\.jpg$' -or -not $seen.Add($filename)) { throw 'Invalid or duplicate image file name.' }
        $date=[DateTime]::ParseExact([string]$slide.atLocal,'yyyy-MM-ddTHH:mm:ss',[Globalization.CultureInfo]::InvariantCulture)
        if ($date -le $previous -or $date -ge $end) { throw 'Campaign dates are not increasing or exceed end.' }
        if ($zone.IsInvalidTime($date) -or $zone.IsAmbiguousTime($date)) { throw 'Ambiguous/invalid local time in campaign.' }
        $previous=$date
        $path=Join-Path (Join-Path $Package 'images') $filename
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Missing image {0}' -f $filename) }
        $sha=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($sha -ine [string]$slide.sha256) { throw ('Image SHA256 mismatch: {0}' -f $filename) }
        $bytes+=(Get-Item -LiteralPath $path).Length
    }
    if ($zone.IsInvalidTime($end) -or $zone.IsAmbiguousTime($end)) { throw 'Ambiguous/invalid campaign end.' }
    return [pscustomobject]@{cfg=$cfg;id=$id;sizeBytes=$bytes;count=$slides.Count;end=$end;zone=$zone}
}
function Get-ExistingGpoByName {
    param([string]$DC,[string]$BaseDn,[string]$Name)
    $container=Open-AdEntry $DC ('CN=Policies,CN=System,'+$BaseDn)
    $searcher=New-Object DirectoryServices.DirectorySearcher($container)
    try {
        $searcher.Filter='(objectClass=groupPolicyContainer)'
        $searcher.PageSize=250
        $searcher.PropertiesToLoad.Add('displayName') | Out-Null
        $matches=$searcher.FindAll()
        try {
            foreach ($item in $matches) {
                if ($item.Properties['displayname'].Count -gt 0 -and
                    [string]$item.Properties['displayname'][0] -ieq $Name) { return $true }
            }
        } finally { $matches.Dispose() }
    } finally { $searcher.Dispose();$container.Dispose() }
    return $false
}
function Verify-CopiedPackage {
    param([string]$From,[string]$To,$Config)
    $names=@('campaign.json','LockScreen-Agent.ps1')
    foreach ($slide in @($Config.slides)) { $names+=('images\'+[string]$slide.fileName) }
    foreach ($file in $names) {
        $a=Join-Path $From $file
        $b=Join-Path $To $file
        if ((Get-FileHash -LiteralPath $a -Algorithm SHA256).Hash -ine
            (Get-FileHash -LiteralPath $b -Algorithm SHA256).Hash) { throw ('SYSVOL copy failed verification: {0}' -f $file) }
    }
}
function Ensure-NativeGpoCom {
    if ('LscmNativeGpo.GpoObject' -as [type]) { return }
    $typeDefinition=@'
using System;
using System.Text;
using System.Runtime.InteropServices;
namespace LscmNativeGpo {
    [ComImport, Guid("EA502722-A23D-11d1-A7D3-0000F87571E3")]
    public class GpoObject { }
    [ComImport, Guid("EA502723-A23D-11d1-A7D3-0000F87571E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
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
'@
    Add-Type -TypeDefinition $typeDefinition -Language CSharp -ErrorAction Stop
}
function New-NativeGpo {
    param([string]$DC,[string]$Name)
    Ensure-NativeGpoCom
    $obj=[LscmNativeGpo.IGpo]([Activator]::CreateInstance([type][LscmNativeGpo.GpoObject]))
    try {
        # Pin the DC used by LDAP and SYSVOL for consistent writes.
        $obj.New(('LDAP://{0}' -f $DC),$Name,0)
        $buffer=New-Object Text.StringBuilder 512
        $obj.GetName($buffer,$buffer.Capacity)
        $guid=[guid]::Parse($buffer.ToString().Trim('{}'))
        return $guid
    } finally { if ($null -ne $obj) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($obj) } }
}
function Register-StartupScript {
    param([string]$DC,[string]$Domain,[string]$BaseDn,[guid]$Id,[string]$Script)
    $name='{'+$Id.ToString().ToUpperInvariant()+'}'
    $root='\\{0}\SYSVOL\{1}\Policies\{2}' -f $DC,$Domain,$name
    $gpt=Join-Path $root 'gpt.ini'
    if (-not (Test-Path -LiteralPath $gpt -PathType Leaf)) { throw ('Native GPO has no SYSVOL GPT: {0}' -f $root) }
    $directory=Join-Path $root 'Machine\Scripts'
    New-Item -Path $directory -ItemType Directory -Force | Out-Null
    $ini=Join-Path $directory 'psscripts.ini'
    if (Test-Path -LiteralPath $ini) { throw 'New GPO already has a startup script. Aborting.' }
    $text="[ScriptsConfig]`r`nStartExecutePSFirst=true`r`n[Startup]`r`n0CmdLine=$Script`r`n0Parameters=-Mode Install`r`n"
    [IO.File]::WriteAllText($ini,$text,[Text.UnicodeEncoding]::new($false,$true))
    $entry=Open-AdEntry $DC ('CN='+$name+',CN=Policies,CN=System,'+$BaseDn)
    try {
        Ensure-ObjectClass $entry 'groupPolicyContainer'
        $old=[int64](Property-Value $entry 'versionNumber')
        if ($old -ne 0) { throw 'New GPO version is unexpectedly nonzero.' }
        $extension='[{42B5FAAE-6536-11D2-AE5A-0000F87571E3}{40B6664F-4972-11D1-A7CA-0000F87571E3}]'
        $oldExtensions=[string](Property-Value $entry 'gPCMachineExtensionNames')
        if ($oldExtensions -and $oldExtensions -ne $extension) { throw 'New GPO already has machine extensions; aborting.' }
        $gptText=[IO.File]::ReadAllText($gpt,[Text.Encoding]::ASCII)
        if ($gptText -notmatch '(?m)^Version=0\s*$') { throw 'Unexpected GPT version (must be 0).' }
        $gptText=[regex]::Replace($gptText,'(?m)^Version=0\s*$','Version=1')
        [IO.File]::WriteAllText($gpt,$gptText,[Text.Encoding]::ASCII)
        $entry.Properties['versionNumber'].Value=1
        $entry.Properties['gPCMachineExtensionNames'].Value=$extension
        $entry.Properties['flags'].Value=1  # Disable user configuration; leave machine enabled.
        $entry.CommitChanges()
        $entry.RefreshCache(@('versionNumber','gPCMachineExtensionNames'))
        if ([int64](Property-Value $entry 'versionNumber') -ne 1 -or
            [string](Property-Value $entry 'gPCMachineExtensionNames') -ne $extension) {
            throw 'GPO machine-side attributes were not saved on DC.'
        }
        if (-not (Test-Path -LiteralPath $ini)) { throw 'PowerShell startup script INI not written.' }
    } finally { $entry.Dispose() }
}
function Set-SecurityFilter {
    param([string]$DC,[string]$BaseDn,[guid]$Id,[byte[]]$GroupSid)
    $gpoName='{'+$Id.ToString().ToUpperInvariant()+'}'
    $entry=Open-AdEntry $DC ('CN='+$gpoName+',CN=Policies,CN=System,'+$BaseDn)
    try {
        $sd=$entry.ObjectSecurity
        $au=New-Object Security.Principal.SecurityIdentifier 'S-1-5-11'
        $chosen=New-Object Security.Principal.SecurityIdentifier ($GroupSid,0)
        $right=[guid]'edacfd8f-ffb3-11d1-b41d-00a0c968f939'
        $applies=0
        foreach ($rule in @($sd.GetAccessRules($true,$false,[Security.Principal.SecurityIdentifier]))) {
            if ($rule.IdentityReference.Value -eq $au.Value -and $rule.AccessControlType -eq 'Allow' -and
                $rule.ObjectType -eq $right -and
                ($rule.ActiveDirectoryRights -band [DirectoryServices.ActiveDirectoryRights]::ExtendedRight)) {
                $sd.RemoveAccessRuleSpecific($rule);$applies++
            }
        }
        if ($applies -eq 0) { throw 'Cannot find explicit Authenticated Users Apply Group Policy ACE. Refusing broad targeting.' }
        $sd.AddAccessRule([DirectoryServices.ActiveDirectoryAccessRule]::new(
            $chosen,[DirectoryServices.ActiveDirectoryRights]::GenericRead,
            [Security.AccessControl.AccessControlType]::Allow))
        $sd.AddAccessRule([DirectoryServices.ActiveDirectoryAccessRule]::new(
            $chosen,[DirectoryServices.ActiveDirectoryRights]::ExtendedRight,
            [Security.AccessControl.AccessControlType]::Allow,$right))
        $entry.ObjectSecurity=$sd
        $entry.CommitChanges()
        $entry.RefreshCache()
        $check=$entry.ObjectSecurity.GetAccessRules($true,$false,[Security.Principal.SecurityIdentifier])
        $filterFound=$false;$broad=$false
        foreach ($rule in $check) {
            if ($rule.ObjectType -eq $right -and $rule.AccessControlType -eq 'Allow' -and
                ($rule.ActiveDirectoryRights -band [DirectoryServices.ActiveDirectoryRights]::ExtendedRight)) {
                if ($rule.IdentityReference.Value -eq $au.Value) { $broad=$true }
                if ($rule.IdentityReference.Value -eq $chosen.Value) { $filterFound=$true }
            }
        }
        if ($broad -or -not $filterFound) { throw 'Security filter verification failed. GPO has NOT been linked.' }
    } finally { $entry.Dispose() }
}
function Add-GpoLinkToOu {
    param([string]$DC,[string]$OuDn,[string]$BaseDn,[guid]$Id)
    $entry=Open-AdEntry $DC $OuDn
    try {
        Ensure-ObjectClass $entry 'organizationalUnit'
        $gpoDn='CN={'+$Id.ToString().ToUpperInvariant()+'},CN=Policies,CN=System,'+$BaseDn
        $link='[LDAP://'+$gpoDn+';0]'
        $old=[string](Property-Value $entry 'gPLink')
        if ($old -and $old.IndexOf($gpoDn,[StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw 'GPO already linked to this OU.'
        }
        $entry.Properties['gPLink'].Value=$link+$old
        $entry.CommitChanges()
        $entry.RefreshCache(@('gPLink'))
        if ([string](Property-Value $entry 'gPLink') -notlike ('*'+$gpoDn+'*')) { throw 'OU link could not be verified.' }
    } finally { $entry.Dispose() }
}
try {
    $req=Get-Content -LiteralPath $RequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $domain=([string]$req.domain).Trim().ToLowerInvariant()
    $dc=([string]$req.dc).Trim()
    $ouDn=([string]$req.ouDn).Trim()
    $groupDn=([string]$req.groupDn).Trim()
    $gpoName=([string]$req.gpoName).Trim()
    $package=[IO.Path]::GetFullPath([string]$req.packagePath)
    if ($domain -notmatch '^(?=.{3,253}$)[a-z0-9-]+(\.[a-z0-9-]+)+$') { throw 'Invalid domain name.' }
    if ($dc -and $dc -notmatch '^[a-zA-Z0-9.-]{1,253}$') { throw 'Invalid DC name.' }
    if ($gpoName.Length -lt 3 -or $gpoName.Length -gt 120 -or $gpoName -match '[\x00-\x1f]') { throw 'Invalid GPO display name.' }
    if (-not $ouDn -or -not $groupDn) { throw 'Select an OU and a security group.' }
    $info=Get-DomainInfo $domain $dc
    $dc=$info.dc
    $baseDn=$info.baseDn
    if (-not (Dn-InDomain $ouDn $baseDn) -or -not (Dn-InDomain $groupDn $baseDn)) {
        throw 'OU and security group must be inside the selected domain.'
    }
    $ou=Open-AdEntry $dc $ouDn
    try { Ensure-ObjectClass $ou 'organizationalUnit' }
    finally { $ou.Dispose() }
    $group=Open-AdEntry $dc $groupDn
    try {
        Ensure-ObjectClass $group 'group'
        if ([int64](Property-Value $group 'groupType') -ge 0) { throw 'Selected group is not a Security group.' }
        $sid=[byte[]](Property-Value $group 'objectSid')
        if (-not $sid) { throw 'Group has no SID.' }
        $groupSam=[string](Property-Value $group 'sAMAccountName')
        $memberCount=$group.Properties['member'].Count
    } finally { $group.Dispose() }
    $pkg=Assert-Package $package
    $id=$pkg.id
    $dfsPackage='\\{0}\SYSVOL\{0}\scripts\LockScreenCampaigns\{1}' -f $domain,$id
    $dcPackage='\\{0}\SYSVOL\{1}\scripts\LockScreenCampaigns\{2}' -f $dc,$domain,$id
    $clientScript=Join-Path $dfsPackage 'LockScreen-Agent.ps1'
    if ($clientScript.Length -ge 260) { throw 'Startup script UNC path too long.' }
    if (Get-ExistingGpoByName $dc $baseDn $gpoName) { throw 'GPO already exists with this display name.' }
    if (Test-Path -LiteralPath $dcPackage) { throw 'Campaign already exists in SYSVOL.' }
    $plan=[ordered]@{
        domain=$domain;dc=$dc;ou=$ouDn;group=$groupDn;groupSam=$groupSam
        directGroupMembers=$memberCount;gpoName=$gpoName;campaignId=$id
        imageCount=$pkg.count;imageBytes=$pkg.sizeBytes;sysvolOnDc=$dcPackage
        clientScriptPath=$clientScript;operation=$Mode;rsatRequired=$false;nativeEngine='Windows ADSI + gpedit.dll COM'
        dcUtc=$info.dcUtc.ToString('o');clockSkewSeconds=$info.clockSkewSeconds
    }
    if ($Mode -eq 'Plan') {
        Send-Result $true 'Preflight complete. No AD or SYSVOL changes.' $plan
        exit 0
    }
    $now=[TimeZoneInfo]::ConvertTimeFromUtc($info.dcUtc,$pkg.zone)
    if ($now -ge $pkg.end) { throw 'Campaign has ended. Deployment blocked.' }
    $base='\\{0}\SYSVOL\{1}\scripts\LockScreenCampaigns' -f $dc,$domain
    $stage=Join-Path $base ('.staging-'+[guid]::NewGuid().ToString('N'))
    $newGpo=$null
    $linked=$false
    try {
        New-Item -Path $base -ItemType Directory -Force | Out-Null
        New-Item -Path (Join-Path $stage 'images') -ItemType Directory -Force | Out-Null
        foreach ($name in @('campaign.json','LockScreen-Agent.ps1')) {
            Copy-Item -LiteralPath (Join-Path $package $name) -Destination (Join-Path $stage $name) -ErrorAction Stop
        }
        foreach ($slide in @($pkg.cfg.slides)) {
            $file=[string]$slide.fileName
            Copy-Item -LiteralPath (Join-Path (Join-Path $package 'images') $file) -Destination (Join-Path (Join-Path $stage 'images') $file) -ErrorAction Stop
        }
        Verify-CopiedPackage $package $stage $pkg.cfg
        Move-Item -LiteralPath $stage -Destination $dcPackage -ErrorAction Stop
        $newGpo=New-NativeGpo $dc $gpoName
        Set-SecurityFilter $dc $baseDn $newGpo $sid
        Register-StartupScript $dc $domain $baseDn $newGpo $clientScript
        Add-GpoLinkToOu $dc $ouDn $baseDn $newGpo
        $linked=$true
        $plan.gpoId=[string]$newGpo
        $plan.linked=$linked
        Send-Result $true 'GPO created without RSAT, scoped, startup script registered, OU linked.' $plan
    } catch {
        # Fail closed: preserve an unlinked, partially-created GPO for manual inspection.
        # Do not roll back existing GPOs or affect any other OU.
        $gpoLabel=if($newGpo){[string]$newGpo}else{'(not created)'}
        throw ('Incomplete deployment: {0}. New GPO: {1}; OU link: {2}; SYSVOL package: {3}. Inspect before retry.' -f $_.Exception.Message,$gpoLabel,$linked,$dcPackage)
    } finally {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
    }
} catch {
    Send-Result $false $_.Exception.Message $null
    exit 1
}
