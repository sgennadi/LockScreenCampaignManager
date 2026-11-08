#requires -Version 5.1
# Computer/Group assignment: signed/sealed LDAP, no RSAT, no GPO writes.
# Modes Search and Preview are read-only. Apply requires a fresh matching
# SHA-256 preview, verified object GUIDs, a writable DC and a typed PC SAM.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RequestPath,
    [Parameter(Mandatory=$true)][string]$ResponsePath,
    [ValidateSet('Search','Preview','Apply')][string]$Mode='Search'
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.DirectoryServices

function Respond([bool]$success,[string]$message,$data) {
    [IO.File]::WriteAllText($ResponsePath,
        ([ordered]@{success=$success;message=$message;data=$data} |
            ConvertTo-Json -Depth 12 -Compress),
        [Text.UTF8Encoding]::new($false))
}
function Prop($entry,[string]$name) {
    $values = $entry.Properties[$name]
    if ($null -ne $values -and $values.Count -gt 0) { return $values[0] }
    return $null
}
function Entry([string]$dc,[string]$dn) {
    $flags = [DirectoryServices.AuthenticationTypes]::Secure -bor
        [DirectoryServices.AuthenticationTypes]::Signing -bor
        [DirectoryServices.AuthenticationTypes]::Sealing
    $item = [DirectoryServices.DirectoryEntry]::new(('LDAP://{0}/{1}' -f $dc,$dn),
        $null,$null,$flags)
    try { $item.RefreshCache(); return $item }
    catch { $item.Dispose(); throw }
}
function IsInside([string]$dn,[string]$parent) {
    return $dn.Equals($parent,[StringComparison]::OrdinalIgnoreCase) -or
        $dn.EndsWith(','+$parent,[StringComparison]::OrdinalIgnoreCase)
}
function Assert-Class($entry,[string]$class) {
    $names = @($entry.Properties['objectClass'] | ForEach-Object { [string]$_ })
    if (-not ($names -contains $class)) { throw "Expected AD $class object." }
}
function Escape-Filter([string]$value) {
    $b = [Text.StringBuilder]::new()
    foreach($ch in $value.ToCharArray()) {
        switch ([int][char]$ch) {
            0 { [void]$b.Append('\00');break }
            40 { [void]$b.Append('\28');break }
            41 { [void]$b.Append('\29');break }
            42 { [void]$b.Append('\2a');break }
            92 { [void]$b.Append('\5c');break }
            default { [void]$b.Append($ch) }
        }
    }
    return $b.ToString()
}
function Sha256([string]$value) {
    $h = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $h.ComputeHash([Text.Encoding]::UTF8.GetBytes($value)))).Replace('-','').ToLowerInvariant()
    } finally { $h.Dispose() }
}
function Convert-AdUsn($value) {
    # ADSI uSNChanged may be Int64 on one machine and a COM IADsLargeInteger
    # (HighPart/LowPart) on another. Comparing [string] COM objects would
    # silently compare "System.__ComObject" and miss real group changes.
    if ($null -eq $value) { throw 'Cannot verify the AD group change sequence.' }
    [long]$number = 0
    if ([long]::TryParse([string]$value,[ref]$number)) {
        if ($number -le 0) { throw 'Invalid AD group change sequence.' }
        return $number.ToString([Globalization.CultureInfo]::InvariantCulture)
    }
    try {
        [long]$high = $value.HighPart
        [long]$low = $value.LowPart
        $number = ($high * [long]4294967296) +
            ($low -band [long]4294967295)
        if ($number -le 0) { throw 'Invalid AD group change sequence.' }
        return $number.ToString([Globalization.CultureInfo]::InvariantCulture)
    }
    catch {
        # Fail closed; never generate a preview whose freshness cannot be
        # verified from an actual numeric AD uSNChanged value.
        throw 'Cannot decode AD group uSNChanged; membership cannot be changed safely.'
    }
}
function Assert-AllowedGroupSid($rawSid) {
    # Names/adminCount alone do not protect renamed default AD groups.
    # Only custom security groups with RIDs >= 1000 may be targeted.
    if ($null -eq $rawSid) { throw 'AD security group SID is unavailable.' }
    try {
        $sid = [Security.Principal.SecurityIdentifier]::new([byte[]]$rawSid,0)
        $rid = [int]($sid.Value.Split('-')[-1])
    }
    catch { throw 'AD security group SID could not be verified.' }
    if ($rid -lt 1000) {
        throw 'Built-in/domain-default security groups cannot be modified by this manager.'
    }
    return $sid.Value
}
function Get-Scope($request) {
    $domain = ([string]$request.domain).Trim().ToLowerInvariant()
    $dc = ([string]$request.dc).Trim()
    $ouDn = ([string]$request.ouDn).Trim()
    $groupDn = ([string]$request.groupDn).Trim()
    if ($domain -notmatch '^(?=.{3,253}$)[a-z0-9-]+(\.[a-z0-9-]+)+$' -or
        $dc -notmatch '^[a-zA-Z0-9.-]{1,253}$' -or
        $dc -match '^\d+\.\d+\.\d+\.\d+$') {
        throw 'Enter a valid domain and Kerberos DC hostname.'
    }
    $root = Entry $dc 'RootDSE'
    try {
        $base = [string](Prop $root 'defaultNamingContext')
        $actualDomain = ([regex]::Matches($base,'(?i)(?:^|,)DC=([^,]+)') |
            ForEach-Object { $_.Groups[1].Value }) -join '.'
        if ($actualDomain -ine $domain) { throw 'Chosen DC belongs to another domain.' }
        # An absent RootDSE flag is UNKNOWN, not proof of a writable DC.
        # Search/Preview still work, but Apply must fail closed.
        $rodcValue = Prop $root 'isRODC'
        $writable = $null -ne $rodcValue -and
            [string]$rodcValue -ieq 'FALSE'
    }
    finally { $root.Dispose() }
    if (-not $ouDn.StartsWith('OU=',[StringComparison]::OrdinalIgnoreCase) -or
        -not (IsInside $ouDn $base) -or
        -not $groupDn.StartsWith('CN=',[StringComparison]::OrdinalIgnoreCase) -or
        -not (IsInside $groupDn $base)) {
        throw 'Chosen OU and security group must be inside the verified domain.'
    }
    $ou = Entry $dc $ouDn
    $group = $null
    try {
        Assert-Class $ou 'organizationalUnit'
        $group = Entry $dc $groupDn
        Assert-Class $group 'group'
        $type = [long](Prop $group 'groupType')
        if ([int](Prop $group 'adminCount') -eq 1 -or
            [string](Prop $group 'isCriticalSystemObject') -ieq 'TRUE') {
            throw 'Protected or critical AD groups cannot be modified from this manager.'
        }
        if (($type -band 2147483648) -eq 0)
            { throw 'The selected AD group is not security-enabled.' }
        $groupSid = Assert-AllowedGroupSid (Prop $group 'objectSid')
        $sam = [string](Prop $group 'sAMAccountName')
        if ($sam -in @('Domain Admins','Enterprise Admins','Administrators',
                'Schema Admins','Account Operators','Server Operators',
                'Backup Operators','Group Policy Creator Owners')) {
            throw 'Privileged AD groups cannot be changed from this campaign manager.'
        }
        return [pscustomobject]@{
            Domain=$domain;Dc=$dc;Base=$base;OuDn=$ouDn;GroupDn=$groupDn
            GroupGuid=$group.Guid.ToString('D')
            GroupUsn=(Convert-AdUsn (Prop $group 'uSNChanged'))
            GroupSam=$sam;GroupRid=[int]($groupSid.Split('-')[-1])
            Writable=$writable
        }
    }
    finally { if ($null -ne $group) {$group.Dispose()};$ou.Dispose() }
}
function Get-Computer([string]$dc,[string]$base,[string]$ouDn,[string]$dn) {
    if ([string]::IsNullOrWhiteSpace($dn) -or
        -not (IsInside $dn $ouDn) -or -not (IsInside $dn $base)) {
        throw 'Computer must belong to the selected OU subtree in this domain.'
    }
    $computer = Entry $dc $dn
    try {
        Assert-Class $computer 'computer'
        $sam = [string](Prop $computer 'sAMAccountName')
        if ($sam -notmatch '^[A-Za-z0-9_.-]{1,60}\$')
            { throw 'Selected AD computer has an invalid SAM account name.' }
        # Primary group membership is omitted from AD memberOf; treating it
        # as None could lead an admin to attempt an invalid Add/Remove.
        $rawPrimary = Prop $computer 'primaryGroupID'
        [int]$primaryRid = 0
        if ($null -eq $rawPrimary -or
            -not ([int]::TryParse([string]$rawPrimary,[ref]$primaryRid)) -or
            $primaryRid -le 0) {
            throw 'Cannot verify computer primary group; membership change blocked.'
        }
        return [pscustomobject]@{
            Dn=[string](Prop $computer 'distinguishedName')
            Sam=$sam;Guid=$computer.Guid.ToString('D')
            Name=[string](Prop $computer 'name');PrimaryGroupRid=$primaryRid
            DirectMemberOf=@($computer.Properties['memberOf'] |
                ForEach-Object { [string]$_ })
        }
    }
    finally { $computer.Dispose() }
}
function Has-EffectiveMembership([string]$dc,[string]$dn,[string]$groupDn) {
    $entry = Entry $dc $dn
    $search = [DirectoryServices.DirectorySearcher]::new($entry)
    try {
        $search.SearchScope=[DirectoryServices.SearchScope]::Base
        $search.Filter='(&(objectCategory=computer)(memberOf:1.2.840.113556.1.4.1941:='+
            (Escape-Filter $groupDn)+'))'
        $match=$search.FindOne()
        return $null -ne $match
    } finally { $search.Dispose();$entry.Dispose() }
}
function Is-PrimaryGroup([int]$computerPrimaryRid,[int]$groupRid) {
    return $computerPrimaryRid -gt 0 -and
        $groupRid -gt 0 -and $computerPrimaryRid -eq $groupRid
}
function Get-Plan($scope,[string]$dn,[string]$action,[string]$utcText) {
    if ($action -notin @('Add','Remove')) { throw 'Select Add or Remove.' }
    $computer = Get-Computer $scope.Dc $scope.Base $scope.OuDn $dn
    $direct = @($computer.DirectMemberOf |
        Where-Object { $_.Equals($scope.GroupDn,[StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
    if (Is-PrimaryGroup $computer.PrimaryGroupRid $scope.GroupRid) {
        throw 'Selected group is the computer primary group; direct Add/Remove is not supported.'
    }
    $effective = if ($direct) { $true } else {
        Has-EffectiveMembership $scope.Dc $computer.Dn $scope.GroupDn
    }
    if ($action -eq 'Add' -and $effective) {
        throw 'This computer already has effective group membership (direct or nested).'
    }
    if ($action -eq 'Remove' -and -not $direct) {
        throw 'Computer is not a direct member; nested membership cannot be removed here.'
    }
    $key = (@($scope.Domain,$scope.Dc.ToLowerInvariant(),$scope.OuDn,
        $scope.GroupDn,$scope.GroupGuid,$scope.GroupUsn,$computer.Dn,
        $computer.Guid,$computer.Sam,[string]$computer.PrimaryGroupRid,
        $action,[string]$direct,[string]$effective,$utcText) -join '|').ToLowerInvariant()
    return [pscustomobject]@{
        domain=$scope.Domain;dc=$scope.Dc;ouDn=$scope.OuDn
        groupDn=$scope.GroupDn;groupSam=$scope.GroupSam
        groupGuid=$scope.GroupGuid;computerDn=$computer.Dn
        computerSam=$computer.Sam;computerName=$computer.Name
        computerGuid=$computer.Guid;primaryGroupRid=$computer.PrimaryGroupRid
        action=$action
        directMember=$direct;effectiveMember=$effective
        writable=$scope.Writable;previewUtc=$utcText;hash=(Sha256 $key)
        message=("Will {0} direct membership in the selected security group." -f $action)
    }
}
try {
    $request = Get-Content -LiteralPath $RequestPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    $scope = Get-Scope $request
    switch ($Mode) {
        'Search' {
            $searchText=([string]$request.search).Trim()
            if ($searchText.Length -gt 48 -or $searchText -match '[\x00-\x1f]')
                { throw 'Computer search must be 0–48 characters.' }
            $ou=Entry $scope.Dc $scope.OuDn
            $finder=[DirectoryServices.DirectorySearcher]::new($ou)
            try {
                $finder.SearchScope=[DirectoryServices.SearchScope]::Subtree
                $finder.PageSize=150
                $finder.SizeLimit=150
                $filter='(objectCategory=computer)'
                if ($searchText.Length -gt 0) {
                    $term=Escape-Filter $searchText
                    $filter='(&(objectCategory=computer)(|(name=*'+$term+
                        '*)(sAMAccountName=*'+$term+'*)))'
                }
                $finder.Filter=$filter
                [void]$finder.PropertiesToLoad.Add('distinguishedName')
                $matches=$finder.FindAll()
                try {
                    $rows=@(foreach ($item in $matches) {
                        $dn=[string]$item.Properties['distinguishedname'][0]
                        $pc=Get-Computer $scope.Dc $scope.Base $scope.OuDn $dn
                        $direct=@($pc.DirectMemberOf |
                            Where-Object { $_.Equals($scope.GroupDn,
                                [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
                        $primary=Is-PrimaryGroup $pc.PrimaryGroupRid $scope.GroupRid
                        $effective=if ($direct -or $primary) {$true} else {
                            Has-EffectiveMembership $scope.Dc $dn $scope.GroupDn
                        }
                        [pscustomobject]@{
                            name=$pc.Name;sam=$pc.Sam;dn=$dn;guid=$pc.Guid
                            membership=$(if ($primary) {'Primary'} elseif ($direct) {'Direct'} elseif ($effective) {'Nested'} else {'None'})
                            direct=$direct;effective=$effective
                        }
                    })
                    $data=[pscustomobject]@{
                        domain=$scope.Domain;ouDn=$scope.OuDn
                        groupDn=$scope.GroupDn;groupSam=$scope.GroupSam
                        writable=$scope.Writable;count=$rows.Count
                        computers=@($rows | Sort-Object name)
                    }
                }
                finally { $matches.Dispose() }
            }
            finally { $finder.Dispose();$ou.Dispose() }
            Respond $true 'Signed AD search completed; no membership changed.' $data
        }
        'Preview' {
            $action=[string]$request.action
            $dn=[string]$request.computerDn
            $timestamp=[datetime]::UtcNow.ToString('o',[Globalization.CultureInfo]::InvariantCulture)
            $plan=Get-Plan $scope $dn $action $timestamp
            Respond $true 'Read-only membership preview completed.' $plan
        }
        'Apply' {
            if (-not $scope.Writable) { throw 'Cannot change group members on a read-only DC.' }
            $action=[string]$request.action
            $dn=[string]$request.computerDn
            $previewUtc=[string]$request.previewUtc
            try {
                $date=[datetime]::ParseExact($previewUtc,'o',
                    [Globalization.CultureInfo]::InvariantCulture,
                    [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
            } catch { throw 'Membership preview timestamp is invalid.' }
            $age=([datetime]::UtcNow-$date).TotalMinutes
            if ($age -lt 0 -or $age -gt 5)
                { throw 'Membership preview has expired. Preview again.' }
            $plan=Get-Plan $scope $dn $action $previewUtc
            if ([string]$request.planHash -cne $plan.hash -or
                [string]$request.approveSam -cne $plan.computerSam -or
                [string]$request.approveGroupDn -ine $plan.groupDn) {
                throw 'Group or computer changed since preview; refresh before applying.'
            }
            $group=Entry $scope.Dc $scope.GroupDn
            try {
                if ($group.Guid.ToString('D') -ne $scope.GroupGuid -or
                    (Convert-AdUsn (Prop $group 'uSNChanged')) -ne $scope.GroupUsn)
                    { throw 'Security group was modified after membership preview.' }
                # Re-read the targeted computer immediately before the write;
                # do not mutate after an OU move, delete/re-create, or rename.
                $beforeWrite=Get-Computer $scope.Dc $scope.Base $scope.OuDn $dn
                if ($beforeWrite.Guid -ne $plan.computerGuid -or
                    $beforeWrite.Sam -cne $plan.computerSam -or
                    $beforeWrite.Dn -ine $plan.computerDn -or
                    $beforeWrite.PrimaryGroupRid -ne $plan.primaryGroupRid -or
                    (Is-PrimaryGroup $beforeWrite.PrimaryGroupRid $scope.GroupRid)) {
                    throw 'Computer changed since membership preview; refresh the selection.'
                }
                $path='LDAP://'+$scope.Dc+'/'+$plan.computerDn
                if ($action -eq 'Add') {
                    [void]$group.Invoke('Add',[object[]]@($path))
                } else {
                    [void]$group.Invoke('Remove',[object[]]@($path))
                }
            } finally { $group.Dispose() }
            $after=Get-Computer $scope.Dc $scope.Base $scope.OuDn $dn
            $direct=@($after.DirectMemberOf |
                Where-Object { $_.Equals($scope.GroupDn,[StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
            if (($action -eq 'Add') -ne $direct)
                { throw 'AD membership write was attempted, but verification failed. Inspect the selected DC.' }
            Respond $true ('Direct group membership '+$action+' verified on '+$scope.Dc) (
                [pscustomobject]@{computerSam=$after.Sam;groupDn=$scope.GroupDn
                    action=$action;directMember=$direct;verified=$true;dc=$scope.Dc})
        }
    }
}
catch {
    Respond $false $_.Exception.Message $null
    exit 1
}
