#requires -Version 5.1
# Read-only domain GPO / Registry.pol / GPP Registry and optional remote HKLM audit.
# No RSAT, WinRM or local policy mutation. Scans linked OU + ancestors.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RequestPath,
    [Parameter(Mandatory=$true)][string]$ResponsePath,
    [ValidateSet('Scan','Effective','VerifyEndpoint','VerifyEvidence')][string]$Mode='Scan'
)
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.DirectoryServices
function Result([bool]$ok,[string]$message,$data) {
    [IO.File]::WriteAllText($ResponsePath,([ordered]@{success=$ok;message=$message;data=$data}|ConvertTo-Json -Depth 12 -Compress),[Text.UTF8Encoding]::new($false))
}
function Prop($e,[string]$name) { if ($e.Properties[$name].Count -gt 0) { return $e.Properties[$name][0] };return $null }
function Entry([string]$dc,[string]$dn) {
    $e=[DirectoryServices.DirectoryEntry]::new(('LDAP://{0}/{1}' -f $dc,$dn),$null,$null,([DirectoryServices.AuthenticationTypes]::Secure -bor [DirectoryServices.AuthenticationTypes]::Signing -bor [DirectoryServices.AuthenticationTypes]::Sealing))
    try{$e.RefreshCache();return $e}catch{$e.Dispose();throw}
}
function Get-Info([string]$domain,[string]$dc) {
    if($domain -notmatch '^(?=.{3,253}$)[a-z0-9-]+(\.[a-z0-9-]+)+$') {throw 'Invalid DNS domain.'}
    $server=if($dc){$dc}else{$domain}
    if($server -notmatch '^[a-zA-Z0-9.-]{1,253}$' -or $server -match '^\d+\.\d+\.\d+\.\d+$') {throw 'Use a DC hostname.'}
    $root=Entry $server 'RootDSE'
    try {
        $base=[string](Prop $root 'defaultNamingContext')
        $found=([regex]::Matches($base,'(?i)(?:^|,)DC=([^,]+)')|ForEach-Object {$_.Groups[1].Value}) -join '.'
        if($found -ine $domain){throw 'DC/domain mismatch.'}
        $name=[string](Prop $root 'dnsHostName');if(-not $name){$name=$server}
        return [pscustomobject]@{dc=$name;base=$base;domain=$found;config=[string](Prop $root 'configurationNamingContext')}
    } finally {$root.Dispose()}
}
function ReadLinks([string]$text) {
    # GPMC Link Order 1 is the rightmost gPLink entry, the highest
    # precedence for links on this container. Byte offsets are not link order.
    $links=[regex]::Matches($text,'(?i)\[LDAP://(?<dn>[^;\]]+);(?<flags>\d+)\]')
    for($i=0;$i -lt $links.Count;$i++) {
        $m=$links[$i]
        [pscustomobject]@{dn=$m.Groups['dn'].Value;flags=[int]$m.Groups['flags'].Value;
            order=($links.Count-$i)}
    }
}
function TargetValues([string]$key,[string]$name,[string]$data) {
    $k=$key.TrimStart('\').ToLowerInvariant();$n=$name.ToLowerInvariant()
    if ($n -in @('lockscreenimage','nochanginglockscreen','nolockscreen') -and $k -like '*\policies\microsoft\windows\personalization') {
        $risk=if($n -eq 'nochanginglockscreen'){'Info'}else{'High'}
        return [pscustomobject]@{key=$key;name=$name;value=$data;risk=$risk;reason='Potential managed Lock Screen policy overlap'}
    }
    if ($n -in @('rotatinglockscreenenabled','disablewindowsspotlightfeatures','rotatinglockscreenoverlayenabled',
        'configurewindowsspotlight','disablecloudoptimizedcontent') -and
        ($k -like '*\cloudcontent' -or $k -like '*\personalization')) {
        return [pscustomobject]@{key=$key;name=$name;value=$data;risk='Warning';reason='Windows Spotlight / rotating Lock Screen setting'}
    }
    return $null
}
function BinaryRegistryPol([string]$path) {
    $out=@()
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $out }
    $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    $reader=[IO.BinaryReader]::new($stream,[Text.Encoding]::Unicode)
    try {
        if ($reader.ReadUInt32() -ne 0x67655250) { throw 'Invalid Registry.pol signature.' }
        $version=$reader.ReadUInt32()
        if ($version -lt 1 -or $version -gt 10) { throw 'Unrecognized Registry.pol version.' }
        while ($stream.Position -lt $stream.Length) {
            $op=$reader.ReadUInt16()
            if ($op -eq 0) { continue }
            if ($op -ne 91) { throw 'Malformed Registry.pol entry.' }
            $key='';$name=''
            foreach($part in @('key','value')) {
                $buf=[Text.StringBuilder]::new()
                while($true) {
                    if($stream.Position + 2 -gt $stream.Length){throw 'Truncated Registry.pol string.'}
                    $c=$reader.ReadUInt16()
                    if($c -eq 59){break}
                    if($c -ne 0){[void]$buf.Append([char]$c)}
                    if($buf.Length -gt 4096){throw 'Invalid Registry.pol entry length.'}
                }
                if($part -eq 'key'){$key=$buf.ToString()}else{$name=$buf.ToString()}
            }
            $type=$reader.ReadUInt32()
            if($reader.ReadUInt16() -ne 59){throw 'Malformed Registry.pol type separator.'}
            $size=$reader.ReadUInt32()
            if($size -gt 65535 -or $reader.ReadUInt16() -ne 59){throw 'Malformed Registry.pol size separator.'}
            $value=$reader.ReadBytes([int]$size)
            if($value.Length -ne $size -or $reader.ReadUInt16() -ne 93){throw 'Truncated Registry.pol entry.'}
            $display=switch($type) {
                1 { [Text.Encoding]::Unicode.GetString($value).TrimEnd([char]0) }
                2 { [Text.Encoding]::Unicode.GetString($value).TrimEnd([char]0) }
                4 { if($value.Length -ge 4){[BitConverter]::ToUInt32($value,0).ToString()}else{'[invalid DWORD]'} }
                7 { [Text.Encoding]::Unicode.GetString($value).Trim([char]0) }
                default { '[binary or unsupported]' }
            }
            $found=TargetValues $key $name $display
            if($null -ne $found){$out+=$found}
        }
    } finally {$reader.Dispose();$stream.Dispose()}
    return $out
}
function GppRegistry([string]$path) {
    $results=@()
    if(-not (Test-Path -LiteralPath $path -PathType Leaf)){return $results}
    $settings=New-Object Xml.XmlReaderSettings
    $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver=$null
    $file=[IO.File]::OpenRead($path)
    $reader=[Xml.XmlReader]::Create($file,$settings)
    try {
        $xml=New-Object Xml.XmlDocument
        $xml.XmlResolver=$null
        $xml.Load($reader)
        foreach($item in @($xml.SelectNodes('//Registry'))) {
            if ([string]$item.GetAttribute('disabled') -eq '1') { continue }
            foreach ($p in @($item.SelectNodes('./Properties'))) {
                $hive=[string]$p.GetAttribute('hive')
                if ($hive -notin @('HKEY_LOCAL_MACHINE','HKLM')) { continue }
                $key=[string]$p.GetAttribute('key');$name=[string]$p.GetAttribute('name');$value=[string]$p.GetAttribute('value')
                $found=TargetValues $key $name $value
                if($null -ne $found){$results+=$found}
            }
        }
    } finally {$reader.Dispose();$file.Dispose()}
    return $results
}
function LocalRegistry([string]$hostName) {
    $output=@()
    $remote=$null
    try {
        $remote=[Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,$hostName,[Microsoft.Win32.RegistryView]::Registry64)
        foreach($path in @('SOFTWARE\Policies\Microsoft\Windows\Personalization','SOFTWARE\Policies\Microsoft\Windows\CloudContent')) {
            $key=$remote.OpenSubKey($path)
            if($null -eq $key){continue}
            try {
                foreach($name in @($key.GetValueNames())) {
                    $raw=$key.GetValue($name)
                    $value=if($raw -is [array]){[string]::Join(';',[string[]]$raw)}else{[string]$raw}
                    $hit=TargetValues $path $name $value
                    if($hit){$output+=$hit}
                }
            } finally {$key.Dispose()}
        }
        return [pscustomobject]@{reachable=$true;values=$output;note='Read-only remote HKLM. Resultant policy source still needs gpresult.'}
    } catch {
        return [pscustomobject]@{reachable=$false;values=@();note='Remote Registry unavailable/denied; this is NOT proof of no conflict. Check RemoteRegistry/firewall/RPC and run gpresult /h locally.'}
    } finally {if($null -ne $remote){$remote.Dispose()}}
}
# Effective mode: read-only AD scope, ACL hints and possible site-linked policies.
# Never present estimated policy applicability as endpoint RSoP.
function Escape-Ldap([string]$value) {
    $buffer=[Text.StringBuilder]::new()
    foreach($character in $value.ToCharArray()) {
        switch([int][char]$character) {
            0 {[void]$buffer.Append('\00')}
            40 {[void]$buffer.Append('\28')}
            41 {[void]$buffer.Append('\29')}
            42 {[void]$buffer.Append('\2a')}
            92 {[void]$buffer.Append('\5c')}
            default {[void]$buffer.Append($character)}
        }
    }
    return $buffer.ToString()
}
function Is-InsideDn([string]$dn,[string]$container) {
    return $dn.Equals($container,[StringComparison]::OrdinalIgnoreCase) -or
        $dn.EndsWith(','+$container,[StringComparison]::OrdinalIgnoreCase)
}
function Safe-ComputerFqdn([string]$name,[string]$domain) {
    return $name -match '^(?=.{3,253}$)[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+$' -and
        $name.EndsWith('.'+$domain,[StringComparison]::OrdinalIgnoreCase) -and
        $name -notmatch '(^|\.)(-|[^.]+-\.)'
}
function Scope-Computer($info,[string]$ouDn,[string]$groupDn,[string]$fqdn) {
    if(-not (Safe-ComputerFqdn $fqdn $info.domain) -or
        -not $ouDn.StartsWith('OU=',[StringComparison]::OrdinalIgnoreCase) -or
        -not (Is-InsideDn $ouDn $info.base) -or
        -not $groupDn.StartsWith('CN=',[StringComparison]::OrdinalIgnoreCase) -or
        -not (Is-InsideDn $groupDn $info.base)) {throw 'InvalidEffectiveGpoScope'}
    $ou=Entry $info.dc $ouDn
    $group=Entry $info.dc $groupDn
    $results=$null;$computer=$null
    try {
        if(@($ou.Properties['objectClass']) -notcontains 'organizationalUnit' -or
           @($group.Properties['objectClass']) -notcontains 'group'){
            throw 'UnverifiedOuOrSecurityGroup'
        }
        $groupType=[long](Prop $group 'groupType')
        # Signed and unsigned LDAP representations of SECURITY_ENABLED are valid.
        if(($groupType -band 0x80000000) -eq 0){throw 'SelectedGroupMustBeSecurityEnabled'}
        $rawSid=Prop $group 'objectSid'
        if($null -eq $rawSid){throw 'SelectedGroupSidUnavailable'}
        $groupSid=[Security.Principal.SecurityIdentifier]::new([byte[]]$rawSid,0)
        $groupRid=[int]($groupSid.Value.Split('-')[-1])
        $search=[DirectoryServices.DirectorySearcher]::new($ou)
        try {
            $search.SearchScope=[DirectoryServices.SearchScope]::Subtree
            $search.Filter='(&(objectCategory=computer)(dNSHostName='+
                (Escape-Ldap $fqdn)+'))'
            $search.SizeLimit=2
            $results=$search.FindAll()
            if($results.Count -ne 1){throw 'ComputerNotUniqueInSelectedOu'}
            $computer=$results[0].GetDirectoryEntry()
        }finally{$search.Dispose()}
        $computer.RefreshCache(@('distinguishedName','dNSHostName','primaryGroupID','objectSid'))
        $dn=[string](Prop $computer 'distinguishedName')
        $resolved=[string](Prop $computer 'dNSHostName')
        if(-not (Is-InsideDn $dn $ouDn) -or
            -not $resolved.Equals($fqdn,[StringComparison]::OrdinalIgnoreCase)){
            throw 'ComputerIdentityChanged'
        }
        $primary=[int](Prop $computer 'primaryGroupID') -eq $groupRid
        $membership=[DirectoryServices.DirectorySearcher]::new($computer)
        try {
            $membership.SearchScope=[DirectoryServices.SearchScope]::Base
            $membership.Filter='(&(objectCategory=computer)(memberOf:1.2.840.113556.1.4.1941:='+
                (Escape-Ldap $groupDn)+'))'
            $nested=$null -ne $membership.FindOne()
        }finally{$membership.Dispose()}
        if(-not $primary -and -not $nested){
            throw 'ComputerOutsideSelectedSecurityGroup'
        }
        $token=@();$tokenStatus='Unknown'
        try {
            $computer.RefreshCache(@('tokenGroups'))
            foreach($bytes in $computer.Properties['tokenGroups']){
                $sid=[Security.Principal.SecurityIdentifier]::new([byte[]]$bytes,0)
                $token+= $sid.Value
            }
            if($token.Count -gt 0){
                $computerSid=[Security.Principal.SecurityIdentifier]::new(
                    [byte[]](Prop $computer 'objectSid'),0)
                $token+=@('S-1-1-0','S-1-5-11',$computerSid.Value)
                $token=@($token|Sort-Object -Unique)
                $tokenStatus='Read'
            }
        }catch{
            $token=@();$tokenStatus='Unavailable'
        }
        return [pscustomobject]@{fqdn=$resolved;dn=$dn;
            tokenSids=@($token);tokenStatus=$tokenStatus;
            membership=if($primary){'Primary'}else{'Nested/direct'}}
    }finally {
        if($null -ne $computer){$computer.Dispose()}
        if($null -ne $results){$results.Dispose()}
        $group.Dispose();$ou.Dispose()
    }
}
function Decide-Link([bool]$enabled,[bool]$enforced,
    [bool]$inheritanceBlocked,[bool]$machineOff) {
    if(-not $enabled){return 'Disabled link'}
    if($machineOff){return 'Computer configuration disabled'}
    if($inheritanceBlocked -and -not $enforced){return 'Blocked inheritance'}
    return 'Candidate'
}
function Decide-Security($entry,[string[]]$sids,[string]$tokenStatus) {
    if($tokenStatus -ne 'Read' -or $sids.Count -eq 0){return 'Unknown token'}
    try {
        $entry.Options.SecurityMasks=[DirectoryServices.SecurityMasks]::Dacl
        $entry.RefreshCache(@('ntSecurityDescriptor'))
        $aces=$entry.ObjectSecurity.GetAccessRules($true,$true,
            [Security.Principal.SecurityIdentifier])
        $applyGuid=[guid]'edacfd8f-ffb3-11d1-b41d-00a0c968f939'
        $set=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach($sid in $sids){[void]$set.Add($sid)}
        $readAllow=$false;$applyAllow=$false;$denied=$false
        foreach($ace in $aces){
            if(-not $set.Contains($ace.IdentityReference.Value)){continue}
            $rights=$ace.ActiveDirectoryRights
            $all=($rights -band [DirectoryServices.ActiveDirectoryRights]::GenericAll) -ne 0
            $read=$all -or
                (($rights -band [DirectoryServices.ActiveDirectoryRights]::GenericRead) -ne 0) -or
                (($rights -band [DirectoryServices.ActiveDirectoryRights]::ReadProperty) -ne 0)
            $apply=$all -or
                ((($rights -band [DirectoryServices.ActiveDirectoryRights]::ExtendedRight) -ne 0) -and
                    ($ace.ObjectType -eq $applyGuid -or $ace.ObjectType -eq [guid]::Empty))
            if($ace.AccessControlType -eq [Security.AccessControl.AccessControlType]::Deny){
                if($read -or $apply){$denied=$true}
            }else{
                if($read){$readAllow=$true}
                if($apply){$applyAllow=$true}
            }
        }
        if($denied){return 'Matching deny ACE'}
        if($readAllow -and $applyAllow){return 'Read and Apply grants observed'}
        return 'Read/Apply grants not established'
    }catch{return 'ACL unreadable'}
}
function Linked-Guid([string]$dn,[string]$base) {
    $container='CN=Policies,CN=System,'+$base
    if(-not (Is-InsideDn $dn $container) -or
        $dn -notmatch '^CN=\{(?<id>[0-9a-fA-F-]{36})\},CN=Policies,'){
        throw 'LinkedGpoOutsideSelectedDomain'
    }
    $id=[guid]::Empty
    if(-not [guid]::TryParse($Matches['id'],[ref]$id) -or
        $id -eq [guid]::Empty){throw 'InvalidLinkedGpoId'}
    return $id
}
function Read-CandidateValues($info,[guid]$id) {
    $machine='\\{0}\SYSVOL\{1}\Policies\{{{2}}}\Machine' -f
        $info.dc,$info.domain,$id.ToString().ToUpperInvariant()
    $values=@();$state='Read';$scripts=$false
    try{
        $dir=Get-Item -LiteralPath $machine -Force -ErrorAction Stop
        if(-not $dir.PSIsContainer -or
            ($dir.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){
            throw 'UnsafeGpoSysvolPath'
        }
        [void](Get-ChildItem -LiteralPath $machine -Force -ErrorAction Stop |
            Select-Object -First 1)
    }catch {return [pscustomobject]@{settings=@();state='Unavailable';scripts=$false}}
    foreach($file in @('Registry.pol','Preferences\Registry\Registry.xml')){
        $path=Join-Path $machine $file
        try{
            $found=$false
            try {
                $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
                if($item.PSIsContainer -or
                   ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                   $item.Length -gt 2097152){throw 'UnsafeGpoRegistryFile'}
                $found=$true
            }catch [System.Management.Automation.ItemNotFoundException] {
                # Genuine absence is okay; permission/RPC errors are not.
                $found=$false
            }
            $matches=if(-not $found){@()}
                elseif($file -eq 'Registry.pol'){@(BinaryRegistryPol $path)}
                else{@(GppRegistry $path)}
            foreach($hit in $matches){
                $value=[string]$hit.value
                if($value.Length -gt 450){$value=$value.Substring(0,450)+' [truncated]'}
                $values+= [pscustomobject]@{
                    source=$file;key=[string]$hit.key;name=[string]$hit.name;
                    value=$value;risk=[string]$hit.risk;reason=[string]$hit.reason
                }
                if($values.Count -gt 120){throw 'TooManyLockScreenRegistryEntries'}
            }
        }catch{$state='Partial or unreadable'}
    }
    foreach($name in @('Scripts\psscripts.ini','Scripts\scripts.ini')){
        try {
            $path=Join-Path $machine $name
            try {
                $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
                if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){
                    throw 'UnsafeStartupScriptLink'
                }
                $scripts=$true
            }catch [System.Management.Automation.ItemNotFoundException] {
                # Missing startup script is a normal GPO configuration.
            }
        }catch{$state='Partial or unreadable'}
    }
    return [pscustomobject]@{settings=@($values);state=$state;scripts=$scripts}
}
function Inspect-Candidate($info,$link,[string]$linkedAt,
    [bool]$blockedBelow,[string]$siteDn,$computer,[int]$scopeDepth) {
    $id=Linked-Guid ([string]$link.dn) $info.base
    $entry=Entry $info.dc ([string]$link.dn)
    try{
        $name=[string](Prop $entry 'displayName')
        $flags=[int](Prop $entry 'flags')
        $wmi=[string](Prop $entry 'gPCWQLFilter')
        $enforced=($link.flags -band 2) -ne 0
        $linkState=Decide-Link (($link.flags -band 1) -eq 0) $enforced $blockedBelow (($flags -band 2) -ne 0)
        $security=Decide-Security $entry $computer.tokenSids $computer.tokenStatus
    }finally{$entry.Dispose()}
    $details=Read-CandidateValues $info $id
    $category=if($linkState -ne 'Candidate'){'Excluded by link/config'}
        elseif($siteDn -ne ''){'Site applicability unknown'}
        elseif($security -eq 'Matching deny ACE'){'Security deny candidate'}
        elseif($security -ne 'Read and Apply grants observed' -or
            $wmi -ne '' -or $details.state -ne 'Read' -or $details.scripts){
            'Needs RSoP / verification'
        }else{'Potentially applicable'}
    return [pscustomobject]@{
        gpo=$name;gpoId=$id.ToString('D');linkedAt=$linkedAt;site=$siteDn
        link=$linkState;linkOrder=[int]$link.order;scopeDepth=$scopeDepth
        enforced=$enforced;blockedBelow=$blockedBelow
        machinePartDisabled=(($flags -band 2) -ne 0)
        security=$security;wmi=if($wmi){'Present - not evaluated'}else{'None'}
        staticRead=$details.state;startupScript=$details.scripts
        category=$category;settings=@($details.settings)
        note='Estimated candidate only. Client RSoP, WMI result, GPP targeting and actual Site membership unknown.'
    }
}
function Read-SiteCandidates($info,$computer) {
    if([string]::IsNullOrWhiteSpace($info.config) -or
        -not $info.config.EndsWith($info.base,[StringComparison]::OrdinalIgnoreCase)){
        throw 'ConfigurationNamingContextNotVerified'
    }
    $sitesDn='CN=Sites,'+$info.config
    $base=Entry $info.dc $sitesDn
    $search=[DirectoryServices.DirectorySearcher]::new($base)
    $result=$null
    try {
        $search.SearchScope=[DirectoryServices.SearchScope]::OneLevel
        $search.Filter='(objectClass=site)'
        $search.PageSize=100
        $search.SizeLimit=200
        [void]$search.PropertiesToLoad.Add('gPLink')
        [void]$search.PropertiesToLoad.Add('distinguishedName')
        $result=$search.FindAll()
        if($result.Count -ge 200){throw 'SiteInventoryIncomplete'}
        $rows=@()
        foreach($site in $result){
            $dn=[string]$site.Properties['distinguishedname'][0]
            if(-not (Is-InsideDn $dn $sitesDn)){throw 'InvalidSiteDn'}
            $links=if($site.Properties['gplink'].Count -eq 0){''}else{
                [string]$site.Properties['gplink'][0]}
            foreach($link in @(ReadLinks $links)){
                if($link.flags -gt 3){throw 'InvalidSiteLinkFlags'}
                if(($link.flags -band 1) -ne 0){continue}
                try{$rows+= Inspect-Candidate $info $link $dn $false $dn $computer -1}
                catch{throw 'SiteLinkedPolicyUnreadable'}
                if($rows.Count -gt 100){throw 'TooManySitePolicyCandidates'}
            }
        }
        return @($rows)
    }finally {
        if($null -ne $result){$result.Dispose()}
        $search.Dispose();$base.Dispose()
    }
}

# Operator-confirmed point-in-time native 64-bit HKLM observation.
# Not an authentic GPO winning-setting result or physical display proof.
# NEVER start services, change firewall/GPO, write registry, or run remote code.
function Observe-RemoteLockScreen([string]$hostName) {
    $base=$null
    $observedUtc=[datetime]::UtcNow.ToString('o')
    try {
        $base=[Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey(
            [Microsoft.Win32.RegistryHive]::LocalMachine,$hostName,
            [Microsoft.Win32.RegistryView]::Registry64)
    } catch {
        return [pscustomobject]@{
            registryStatus='Unavailable';osStatus='Unknown';osBuild=''
            observedUtc=$observedUtc;values=@()
            note='Remote Registry connection unavailable or access denied; no absent values inferred.'
        }
    }
    $out=@();$hadError=$false;$build='';$osStatus='Unknown'
    try {
        $osKey=$null
        try {
            $osKey=$base.OpenSubKey('SOFTWARE\Microsoft\Windows NT\CurrentVersion')
            if($null -ne $osKey){
                $build=[string]$osKey.GetValue('CurrentBuildNumber','',
                    [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                $installation=[string]$osKey.GetValue('InstallationType','',
                    [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                if($build -match '^\d{4,6}$' -and $installation.Length -gt 0){
                    if([int]$build -ge 22000 -and $installation -eq 'Client'){
                        $osStatus='Windows11'
                    } else { $osStatus='OtherWindows' }
                }
            }
        }catch{$osStatus='Unknown';$build='';$hadError=$true}
        finally{if($null -ne $osKey){$osKey.Dispose()}}

        $specs=@(
            [pscustomobject]@{key='SOFTWARE\Policies\Microsoft\Windows\Personalization'
                names=@('LockScreenImage','NoChangingLockScreen','NoLockScreen')},
            [pscustomobject]@{key='SOFTWARE\Policies\Microsoft\Windows\CloudContent'
                names=@('RotatingLockScreenEnabled','DisableWindowsSpotlightFeatures',
                    'RotatingLockScreenOverlayEnabled','ConfigureWindowsSpotlight',
                    'DisableCloudOptimizedContent')},
            [pscustomobject]@{key='SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP'
                names=@('LockScreenImagePath','LockScreenImageUrl','LockScreenImageStatus')}
        )
        foreach($spec in $specs){
            $sub=$null;$state='Readable';$presentNames=@()
            try {
                $sub=$base.OpenSubKey([string]$spec.key)
                if($null -ne $sub){$presentNames=@($sub.GetValueNames())}
                else{$state='MissingKey'}
            }catch{$state='Unreadable';$hadError=$true}
            try {
                foreach($name in @($spec.names)){
                    $status='Absent';$kind='Unknown';$value=''
                    if($state -eq 'Unreadable'){$status='Unreadable'}
                    elseif($state -eq 'MissingKey'){$status='Absent'}
                    elseif($presentNames -icontains $name){
                        try {
                            $kind=[string]$sub.GetValueKind($name)
                            if($kind -in @('String','ExpandString','DWord','QWord')){
                                $raw=$sub.GetValue($name,$null,
                                    [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                                if($null -eq $raw){
                                    $kind='Unknown';$status='Unreadable';$hadError=$true
                                } else {
                                    $value=[string]$raw
                                    if($value.Length -gt 1024){
                                        $value='';$kind='Unknown';$status='Oversized';$hadError=$true
                                    } else {$status='Present'}
                                }
                            } else {$kind='Other';$status='Unsupported'}
                        }catch{
                            $value='';$kind='Unknown';$status='Unreadable';$hadError=$true
                        }
                    }
                    $out+= [pscustomobject]@{
                        key=[string]$spec.key;name=[string]$name;status=$status
                        kind=$kind;value=$value
                    }
                }
            }finally{if($null -ne $sub){$sub.Dispose()}}
        }
    }finally{if($null -ne $base){$base.Dispose()}}
    return [pscustomobject]@{
        registryStatus=if($hadError){'Partial'}else{'Readable'}
        osStatus=$osStatus;osBuild=$build
        observedUtc=([datetime]::UtcNow.ToString('o'));values=@($out)
        note='Read-only native 64-bit HKLM observation: no verified GPO origin, file hash or physical lock screen.'
    }
}


# Read the existing client Registry Policy CSE result via remote WMI/DCOM,
# never forcing a GP refresh. The cached CSE result may be stale.
function Observe-RsopSettingSource([string]$computer) {
    $observedUtc=[datetime]::UtcNow.ToString('o')
    $result=@()
    try {
        $filter="valueName='LockScreenImage' OR valueName='NoLockScreen' OR " +
            "valueName='NoChangingLockScreen' OR valueName='RotatingLockScreenEnabled' OR " +
            "valueName='DisableWindowsSpotlightFeatures' OR " +
            "valueName='RotatingLockScreenOverlayEnabled' OR " +
            "valueName='ConfigureWindowsSpotlight' OR valueName='DisableCloudOptimizedContent'"
        # Standard Computer RSoP namespace, read via DCOM not WinRM.
        $records=@(Get-WmiObject -ComputerName $computer -Namespace 'root\RSOP\Computer' -Class RSOP_RegistryPolicySetting -Filter $filter -ErrorAction Stop)
        if($records.Count -gt 150){throw 'RsopSettingResultLimitExceeded'}
        foreach($row in $records){
            if($null -eq $row){continue}
            $name=[string]$row.valueName
            $key=([string]$row.registryKey).Replace('/','\').Trim('\')
            if($key -match '^(?i:HKEY_LOCAL_MACHINE|HKLM)\\') {
                $key=$key -replace '^(?i:HKEY_LOCAL_MACHINE|HKLM)\\',''
            }
            $key=$key.ToUpperInvariant()
            if($key -notin @('SOFTWARE\POLICIES\MICROSOFT\WINDOWS\PERSONALIZATION',
                'SOFTWARE\POLICIES\MICROSOFT\WINDOWS\CLOUDCONTENT')){continue}
            if($name -notin @('LockScreenImage','NoLockScreen','NoChangingLockScreen',
                'RotatingLockScreenEnabled','DisableWindowsSpotlightFeatures',
                'RotatingLockScreenOverlayEnabled','ConfigureWindowsSpotlight',
                'DisableCloudOptimizedContent')){continue}
            $order=[uint32]$row.precedence
            if($order -lt 1 -or $order -gt 10000){throw 'InvalidRsopPrecedence'}
            $guid=''
            $rawId=[string]$row.GPOID
            $match=[regex]::Match($rawId,
                '(?i)\{(?<g>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\}')
            if($match.Success){$guid=([guid]$match.Groups['g'].Value).ToString('D')}
            $result+= [pscustomobject]@{
                key=$key;name=$name;precedence=[int]$order;gpoId=$guid
                deleted=[bool]$row.deleted;valueType=[int]$row.valueType
            }
            if($result.Count -gt 96){throw 'TooManyRelevantRsopSettings'}
        }
        return [pscustomobject]@{
            status=if($result.Count){'Available'}else{'Empty'}
            observedUtc=$observedUtc;settings=@($result)
            note='Cached computer Registry Policy CSE precedence; WMI query time is not policy refresh time.'
        }
    }catch{
        return [pscustomobject]@{
            status='Unavailable';observedUtc=$observedUtc;settings=@()
            note='Remote RSoP WMI was inaccessible, incomplete or rejected; no GPO source inferred.'
        }
    }
}

# Hash only an existing campaign-owned JPEG under the expected ProgramData
# directory via administrative SMB. Never follow remote reparse points or
# silently hash arbitrary files supplied by a registry value.
function Observe-ImageIntegrity($info,[string]$computer,[string]$campaign,$observation) {
    $utc=[datetime]::UtcNow.ToString('o')
    $result=[ordered]@{
        status='Unknown';observedUtc=$utc;fileName='';expectedSha256=''
        actualSha256='';bytes=0
        note='No trusted campaign-local image is verifiable at this time.'
    }
    if($observation.registryStatus -eq 'Unavailable'){
        $result.status='RegistryUnavailable'
        return [pscustomobject]$result
    }
    $image=@($observation.values|Where-Object {
        $_.key -eq 'SOFTWARE\Policies\Microsoft\Windows\Personalization' -and
        $_.name -eq 'LockScreenImage'})
    if($image.Count -ne 1 -or $image[0].status -ne 'Present'){
        $result.status='NoObservedImagePath'
        return [pscustomobject]$result
    }
    $value=[string]$image[0].value
    $prefix='C:\ProgramData\LockScreenCampaigns\'+$campaign+'\images\'
    if(-not $value.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){
        $result.status='OtherOrUnsupportedPath'
        $result.note='Observed path is outside the supported managed C: ProgramData image directory.'
        return [pscustomobject]$result
    }
    $name=$value.Substring($prefix.Length)
    if($name -cnotmatch '^[0-9]{2}\.jpg$'){
        $result.status='UnsafeImagePath'
        return [pscustomobject]$result
    }
    $result.fileName=$name
    try {
        $manifestPath='\\{0}\SYSVOL\{1}\scripts\LockScreenCampaigns\{2}\campaign.json' -f
            $info.dc,$info.domain,$campaign
        $manifestFile=Get-Item -LiteralPath $manifestPath -ErrorAction Stop
        if(($manifestFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            $manifestFile.Length -gt 2097152){throw 'UnsafeManifest'}
        $manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 -ErrorAction Stop |
            ConvertFrom-Json -ErrorAction Stop
        if([string]$manifest.campaignId -cne $campaign){throw 'CampaignManifestMismatch'}
        $matching=@($manifest.slides|Where-Object { [string]$_.fileName -ceq $name })
        if($matching.Count -ne 1 -or
            [string]$matching[0].sha256 -cnotmatch '^[0-9a-fA-F]{64}$'){
            throw 'MissingOrAmbiguousExpectedJpegHash'
        }
        $result.expectedSha256=([string]$matching[0].sha256).ToLowerInvariant()
    }catch{
        $result.status='ManifestUnavailable'
        $result.note='Selected DC manifest could not be read/validated; cannot confirm expected image hash.'
        return [pscustomobject]$result
    }
    # Fixed C$ mapping avoids probing arbitrary path/device input.
    $base='\\'+$computer+'\C$'
    $segments=@('ProgramData','LockScreenCampaigns',$campaign,'images',$name)
    $current=$base
    try {
        foreach($segment in $segments){
            $current=Join-Path $current $segment
            $item=Get-Item -LiteralPath $current -Force -ErrorAction Stop
            if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){
                throw 'ReparsePointNotAllowed'
            }
        }
        if($item.PSIsContainer -or $item.Length -le 0 -or
            $item.Length -gt 33554432){throw 'UnsafeJpegFileSize'}
        $result.bytes=[long]$item.Length
        $result.actualSha256=(Get-FileHash -LiteralPath $current -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        $result.status=if($result.actualSha256 -ceq $result.expectedSha256){
            'HashMatches'
        }else{'HashDiffers'}
        $result.note='Read-only SMB file hash vs selected-DC manifest; no proof of physical display or atomic read.'
    }catch {
        $result.status='FileUnavailable'
        $result.actualSha256=''
        $result.note='Remote SMB read failed, image missing/unsafe or reparse point found.'
    }
    $result.observedUtc=[datetime]::UtcNow.ToString('o')
    return [pscustomobject]$result
}

function Effective-Candidates($info,$req) {
    $ouDn=([string]$req.ouDn).Trim()
    $groupDn=([string]$req.groupDn).Trim()
    $computerFqdn=([string]$req.computer).Trim()
    $campaign=[string]$req.campaignId
    $managed=[guid]::Empty
    if(-not [guid]::TryParse([string]$req.managedGpoId,[ref]$managed) -or
        $managed -eq [guid]::Empty -or
        $campaign -cnotmatch '^[a-z0-9][a-z0-9_-]{0,70}$') {
        throw 'SelectedManagedCampaignInvalid'
    }
    $computer=Scope-Computer $info $ouDn $groupDn $computerFqdn

    # Verify this is the app's own managed campaign on the selected DC.
    $root='\\{0}\SYSVOL\{1}\scripts\LockScreenCampaigns\{2}' -f $info.dc,$info.domain,$campaign
    $manifestPath=Join-Path $root 'campaign.json'
    $manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    if([int]$manifest.schemaVersion -ne 1 -or
       [string]$manifest.campaignId -cne $campaign) {
        throw 'ManagedManifestNotVerified'
    }
    $startup='\\{0}\SYSVOL\{1}\Policies\{{{2}}}\Machine\Scripts\psscripts.ini' -f $info.dc,$info.domain,$managed.ToString().ToUpperInvariant()
    $ini=Get-Content -LiteralPath $startup -Raw -Encoding Unicode -ErrorAction Stop
    $linePattern='(?im)^0CmdLine=\\\\[^\r\n]+\\scripts\\LockScreenCampaigns\\'+
        [regex]::Escape($campaign)+'\\LockScreen-Agent\.ps1\s*$'
    if($ini -notmatch $linePattern -or
       $ini -notmatch '(?im)^0Parameters=-Mode Install\s*$') {
        throw 'ManagedStartupNotVerified'
    }

    # Follow the selected computer's real AD parent (including nested OUs
    # and Block Inheritance) instead of stopping at the chosen search OU.
    $separator=[regex]::Match([string]$computer.dn,'(?<!\\),')
    if(-not $separator.Success){throw 'InvalidComputerDistinguishedName'}
    $actualContainer=$computer.dn.Substring($separator.Index+1)
    if(-not (Is-InsideDn $actualContainer $ouDn)){
        throw 'ComputerContainerOutsideSelectedOu'
    }
    $chain=[Collections.Generic.List[string]]::new()
    $dn=$actualContainer
    for($i=0;$i -lt 64;$i++) {
        $chain.Add($dn)
        if($dn.Equals($info.base,[StringComparison]::OrdinalIgnoreCase)){break}
        $separator=[regex]::Match($dn,'(?<!\\),')
        if(-not $separator.Success){throw 'UnverifiableOuChain'}
        $dn=$dn.Substring($separator.Index+1)
    }
    if($chain[$chain.Count-1] -ine $info.base){throw 'UnverifiableOuChain'}
    $chain.Reverse()
    $levels=@();$candidates=@();$warnings=@()
    for($i=0;$i -lt $chain.Count;$i++){
        $entry=Entry $info.dc $chain[$i]
        try {
            $raw=[string](Prop $entry 'gPLink')
            if($raw.Length -gt 32000){throw 'TooManyGpoLinks'}
            $links=@(ReadLinks $raw)
            if($links.Count -gt 120){throw 'TooManyGpoLinks'}
            $levels+= [pscustomobject]@{
                dn=$chain[$i];depth=$i
                blocked=(([int](Prop $entry 'gPOptions') -band 1) -ne 0)
                links=$links
            }
        }finally{$entry.Dispose()}
    }
    foreach($level in $levels){
        foreach($link in @($level.links)){
            if($link.flags -gt 3){$warnings+='Invalid OU GPO link flags';continue}
            $blocked=$false
            for($i=$level.depth+1;$i -lt $levels.Count;$i++){
                if($levels[$i].blocked){$blocked=$true}
            }
            try {
                $candidates+= Inspect-Candidate $info $link $level.dn $blocked '' $computer ([int]$level.depth)
            }catch {
                $warnings+='An OU-linked GPO could not be inspected.'
            }
            if($candidates.Count -gt 200){throw 'TooManyPolicyCandidates'}
        }
    }
    $sites=@()
    try {$sites=@(Read-SiteCandidates $info $computer)}
    catch {$warnings+='Site GPO inventory incomplete: applicability remains Unknown.'}
    $all=@($candidates)+@($sites)
    $possibleHigh=@($all|Where-Object {
        $_.category -eq 'Potentially applicable' -and
        @($_.settings|Where-Object{$_.risk -eq 'High'}).Count -gt 0
    }).Count
    return [pscustomobject]@{
        domain=$info.domain;dc=$info.dc;targetOu=$ouDn
        groupDn=$groupDn;computer=$computer.fqdn
        campaignId=$campaign;managedGpoId=$managed.ToString('D')
        analyzedUtc=([datetime]::UtcNow.ToString('o'))
        membership=$computer.membership;tokenStatus=$computer.tokenStatus
        siteAssociation='Unknown'
        actualComputerContainer=$actualContainer
        siteInventoryComplete=($warnings -notcontains
            'Site GPO inventory incomplete: applicability remains Unknown.')
        linkedCount=$candidates.Count;siteCandidates=$sites.Count
        potentialHigh=$possibleHigh
        policies=@($all);warnings=@($warnings)
        scope='Read-only candidate assessment, NOT RSoP. Site mapping, WMI filters, Group Policy CSE, GPP item-level targeting, local policies and replication may differ.'
    }
}


try {
    $req=Get-Content -LiteralPath $RequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $domain=([string]$req.domain).Trim().ToLowerInvariant();$dc=([string]$req.dc).Trim()
    $ouDn=([string]$req.ouDn).Trim()
    $info=Get-Info $domain $dc
    if($Mode -eq 'Effective'){
        $data=Effective-Candidates $info $req
        Result $true 'Scoped read-only GPO candidate assessment complete; not an RSoP.' $data
        exit 0
    }
    if($Mode -eq 'VerifyEndpoint' -or $Mode -eq 'VerifyEvidence'){
        # Revalidate the actual computer, OU, group, managed GPO and manifest
        # on selected DC before any read-only endpoint RPC is attempted.
        $verified=Effective-Candidates $info $req
        $observation=Observe-RemoteLockScreen ([string]$verified.computer)
        $data=[ordered]@{
            domain=$verified.domain;dc=$verified.dc;targetOu=$verified.targetOu
            groupDn=$verified.groupDn;computer=$verified.computer
            managedGpoId=$verified.managedGpoId;campaignId=$verified.campaignId
            actualComputerContainer=$verified.actualComputerContainer
            observedUtc=$observation.observedUtc
            registryStatus=$observation.registryStatus
            osStatus=$observation.osStatus;osBuild=$observation.osBuild
            values=@($observation.values);note=$observation.note
        }
        if($Mode -eq 'VerifyEvidence'){
            if($observation.osStatus -ne 'Windows11'){
                $data.rsop=[ordered]@{
                    status='Skipped';observedUtc=$observation.observedUtc
                    settings=@();note='Windows 11 status not established.'
                }
                $data.image=[ordered]@{
                    status='Skipped';observedUtc=$observation.observedUtc
                    fileName='';expectedSha256='';actualSha256='';bytes=0
                    note='Windows 11 status not established.'
                }
            }else{
                $data.rsop=Observe-RsopSettingSource ([string]$verified.computer)
                $data.image=Observe-ImageIntegrity $info ([string]$verified.computer) ([string]$verified.campaignId) $observation
            }
        }
        Result $true 'Scoped read-only endpoint verification; no physical display claim.' $data
        exit 0
    }
    if (-not ($ouDn -ieq $info.base -or $ouDn.EndsWith(','+$info.base,[StringComparison]::OrdinalIgnoreCase))) {throw 'Target OU is outside domain.'}
    $chain=[Collections.Generic.List[string]]::new()
    $dn=$ouDn
    for($i=0;$i -lt 64;$i++) {
        $chain.Add($dn)
        if($dn -ieq $info.base){break}
        $m=[regex]::Match($dn,'(?<!\\),')
        if(-not $m.Success){throw 'Cannot locate OU hierarchy.'}
        $dn=$dn.Substring($m.Index+1)
    }
    if($chain[$chain.Count-1] -ine $info.base){throw 'OU ancestry could not be resolved.'}
    $chain.Reverse()
    $levels=@()
    for($i=0;$i -lt $chain.Count;$i++) {
        $entry=Entry $info.dc $chain[$i]
        try {
            $links=@(ReadLinks ([string](Prop $entry 'gPLink')))
            $blocked=(([int](Prop $entry 'gPOptions') -band 1) -eq 1)
            $levels+=[pscustomobject]@{dn=$chain[$i];blocked=$blocked;links=$links;depth=$i}
        }finally{$entry.Dispose()}
    }
    $records=@();$warnings=@()
    foreach($level in $levels) {
        foreach($link in @($level.links)) {
            $linkEnabled=(($link.flags -band 1) -eq 0)
            $enforced=(($link.flags -band 2) -ne 0)
            $blockedBelow=$false
            for($i=$level.depth+1;$i -lt $levels.Count;$i++) {if($levels[$i].blocked){$blockedBelow=$true}}
            $inScope=($linkEnabled -and ($enforced -or -not $blockedBelow))
            $idMatch=[regex]::Match($link.dn,'(?i)^CN=\{(?<id>[0-9a-f-]{36})\},CN=Policies,')
            if(-not $idMatch.Success){$warnings+=('Unknown GPO link in '+$level.dn);continue}
            $id=[guid]$idMatch.Groups['id'].Value
            $gpo=Entry $info.dc $link.dn
            try {
                $name=[string](Prop $gpo 'displayName')
                $flags=[int](Prop $gpo 'flags')
                $wmi=[string](Prop $gpo 'gPCWQLFilter')
                if(($flags -band 2) -ne 0){$inScope=$false}
            } finally {$gpo.Dispose()}
            $root='\\{0}\SYSVOL\{1}\Policies\{{{2}}}\Machine' -f $info.dc,$info.domain,$id.ToString().ToUpperInvariant()
            # Any linked startup script can set registry values dynamically.
            # We do not execute or interpret arbitrary scripts; mark coverage as uncertain.
            foreach($scriptIni in @('Scripts\psscripts.ini','Scripts\scripts.ini')) {
                $candidate=Join-Path $root $scriptIni
                if($inScope -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                    $warnings+=('Additional startup script setting in '+$name+'; inspect through gpresult/GPMC.')
                }
            }
            foreach($f in @('Registry.pol','Preferences\Registry\Registry.xml')) {
                $path=Join-Path $root $f
                try {
                    $hits=if($f -eq 'Registry.pol'){@(BinaryRegistryPol $path)}else{@(GppRegistry $path)}
                    foreach($hit in @($hits)) {
                        $effectiveRisk=if(-not $inScope){'Inactive/filtered'}else{$hit.risk}
                        $records+=[pscustomobject]@{gpo=$name;gpoId=$id.ToString('D');linkedAt=$level.dn;
                            enforced=$enforced;disabled=(-not $linkEnabled);inheritanceBlocked=$blockedBelow;
                            computerPartDisabled=(($flags -band 2) -ne 0);wmiFilterPresent=([bool]$wmi);
                            source=$f;key=$hit.key;name=$hit.name;value=$hit.value;risk=$effectiveRisk;reason=$hit.reason;
                            applicability='Estimated: security filtering, WMI, site/local policies and RSoP not fully evaluated.'}
                    }
                } catch {$warnings+=('Could not inspect '+$name+' '+$f+': '+$_.Exception.GetType().Name)}
            }
        }
    }
    $local=$null
    $computer=if($req.PSObject.Properties.Name -contains 'computer'){([string]$req.computer).Trim()}else{''}
    if($computer) {
        if($computer -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]{0,253}$'){throw 'Computer must be DNS hostname, not IP or path.'}
        $local=LocalRegistry $computer
        if(-not $local.reachable){$warnings+=$local.note}
    }
    $activeHigh=@($records|Where-Object{$_.risk -eq 'High'}).Count
    $activeWarning=@($records|Where-Object{$_.risk -eq 'Warning'}).Count
    $data=[ordered]@{domain=$info.domain;dc=$info.dc;targetOu=$ouDn;scannedLevels=@($levels|Select-Object dn,blocked,depth);
        matches=$records;count=$records.Count;highRisk=$activeHigh;warningsCount=$activeWarning;
        notes=$warnings;remoteRegistry=$local;
        scope='Read-only candidate audit. Not a definitive Resultant Set of Policy (RSoP). Site-level, local, WMI, item-level targeting and security permissions require verification.'}
    Result $true 'Read-only conflict scan complete.' $data
} catch { Result $false $_.Exception.Message $null;exit 1 }
