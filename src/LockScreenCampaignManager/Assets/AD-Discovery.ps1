#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RequestPath,
    [Parameter(Mandatory=$true)][string]$ResponsePath,
    [ValidateSet('Connect','DomainControllers','OUs','Groups','Users','Snapshot','Permissions')]
    [string]$Mode = 'Connect'
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.DirectoryServices.Protocols

function Send-Result {
    param([bool]$Success, [string]$Message, $Data)
    $json = [ordered]@{ success=$Success; message=$Message; data=$Data } | ConvertTo-Json -Depth 9 -Compress
    [IO.File]::WriteAllText($ResponsePath,$json,[Text.UTF8Encoding]::new($false))
}
function Get-Attribute {
    param($Entry,[string]$Name)
    $attr = $Entry.Attributes[$Name]
    if ($null -ne $attr -and $attr.Count -gt 0) { return [string]$attr[0] }
    return ''
}
function New-Ldap {
    param([string]$HostName)
    $identifier = [System.DirectoryServices.Protocols.LdapDirectoryIdentifier]::new($HostName,389,$false,$false)
    $conn = [System.DirectoryServices.Protocols.LdapConnection]::new($identifier)
    $conn.AuthType = [System.DirectoryServices.Protocols.AuthType]::Negotiate
    $conn.SessionOptions.ProtocolVersion = 3
    # Signed + sealed LDAP protects the account's directory queries on the wire.
    $conn.SessionOptions.Signing = $true
    $conn.SessionOptions.Sealing = $true
    $conn.Timeout = [TimeSpan]::FromSeconds(25)
    $conn.Bind()
    return $conn
}
function Search-Entries {
    param($Connection,[string]$Base,[string]$Filter,[string[]]$Attributes,[int]$Limit=5000)
    $entries = [Collections.Generic.List[object]]::new()
    $cookie = [byte[]]@()
    do {
        $request = [System.DirectoryServices.Protocols.SearchRequest]::new(
            $Base,$Filter,[System.DirectoryServices.Protocols.SearchScope]::Subtree,$Attributes)
        $page = [System.DirectoryServices.Protocols.PageResultRequestControl]::new(500)
        $page.Cookie = $cookie
        [void]$request.Controls.Add($page)
        $response = [System.DirectoryServices.Protocols.SearchResponse]$Connection.SendRequest($request)
        foreach ($entry in $response.Entries) {
            $entries.Add($entry)
            if ($entries.Count -ge $Limit) { return @($entries.ToArray()) }
        }
        $cookie = [byte[]]@()
        foreach ($ctrl in $response.Controls) {
            if ($ctrl -is [System.DirectoryServices.Protocols.PageResultResponseControl]) { $cookie = $ctrl.Cookie }
        }
    } while ($cookie.Length -gt 0)
    return @($entries.ToArray())
}
# Read-only, conservative effective rights hints. The final SYSVOL/GPO COM
# write cannot be proven without writing; never report FULLY AUTHORIZED.
function Get-ReadOnlyDacl {
    param($Connection,[string]$DistinguishedName)
    $request = [System.DirectoryServices.Protocols.SearchRequest]::new(
        $DistinguishedName,'(objectClass=*)',
        [System.DirectoryServices.Protocols.SearchScope]::Base,
        [string[]]@('nTSecurityDescriptor'))
    [void]$request.Controls.Add(
        [System.DirectoryServices.Protocols.SecurityDescriptorFlagControl]::new(
            [System.DirectoryServices.Protocols.SecurityMasks]::Dacl))
    $response = [System.DirectoryServices.Protocols.SearchResponse]$Connection.SendRequest($request)
    if ($response.Entries.Count -ne 1 -or
        -not $response.Entries[0].Attributes.Contains('nTSecurityDescriptor')) {
        throw 'Directory security descriptor could not be inspected.'
    }
    $bytes = [byte[]]$response.Entries[0].Attributes['nTSecurityDescriptor'][0]
    return [Security.AccessControl.RawSecurityDescriptor]::new($bytes,0)
}
function Get-PrincipalSids {
    param($Connection,[string]$BaseDn,[string]$Login)
    $name = $Login.Trim()
    if ($name.Contains('\')) { $name = $name.Split('\')[-1] }
    $filter = if ($name.Contains('@')) {
        '(userPrincipalName=' + (Escape-Filter $name) + ')'
    } else {
        '(sAMAccountName=' + (Escape-Filter $name) + ')'
    }
    $users = @(Search-Entries $Connection $BaseDn
        ('(&(objectCategory=person)(objectClass=user)' + $filter + ')')
        @('objectSid','tokenGroups') 2)
    if ($users.Count -ne 1) { throw 'Cannot resolve exact account to inspect permissions.' }
    $sids = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    [void]$sids.Add('S-1-1-0')
    [void]$sids.Add('S-1-5-11')
    foreach ($key in @('objectSid','tokenGroups')) {
        $attr = $users[0].Attributes[$key]
        if ($null -eq $attr) { continue }
        foreach ($raw in $attr) {
            try {
                $sid = [Security.Principal.SecurityIdentifier]::new([byte[]]$raw,0)
                [void]$sids.Add($sid.Value)
            } catch { }
        }
    }
    return $sids
}
function Test-PotentialDirectoryAccess {
    param($Descriptor,$Sids,[int]$DesiredMask,[guid]$ObjectType)
    $allow=$false; $deny=$false
    foreach ($ace in $Descriptor.DiscretionaryAcl) {
        if ($ace -isnot [Security.AccessControl.QualifiedAce] -or
            -not $Sids.Contains($ace.SecurityIdentifier.Value)) { continue }
        $mask=[int64]$ace.AccessMask
        $matching=(($mask -band 0x10000000) -ne 0) -or
            (($mask -band $DesiredMask) -ne 0)
        if (-not $matching) { continue }
        if ($ace -is [Security.AccessControl.ObjectAce] -and
            ($ace.ObjectAceFlags -band [Security.AccessControl.ObjectAceFlags]::ObjectAceTypePresent) -ne 0 -and
            $ace.ObjectAceType -ne $ObjectType) { continue }
        if ($ace.AceQualifier -eq [Security.AccessControl.AceQualifier]::AccessDenied) {
            $deny=$true
        } elseif ($ace.AceQualifier -eq [Security.AccessControl.AceQualifier]::AccessAllowed) {
            $allow=$true
        }
    }
    if ($deny) { return 'POSSIBLE_DENY' }
    if ($allow) { return 'POTENTIAL_ALLOW' }
    return 'NO_MATCHING_ALLOW'
}
function Escape-Filter {
    param([string]$Value)
    $sb = [Text.StringBuilder]::new()
    foreach ($c in $Value.ToCharArray()) {
        switch ([int][char]$c) {
            0  { [void]$sb.Append('\00'); break }
            40 { [void]$sb.Append('\28'); break }
            41 { [void]$sb.Append('\29'); break }
            42 { [void]$sb.Append('\2a'); break }
            92 { [void]$sb.Append('\5c'); break }
            default { [void]$sb.Append($c) }
        }
    }
    return $sb.ToString()
}
try {
    $req = Get-Content -LiteralPath $RequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $domain = ([string]$req.domain).Trim().ToLowerInvariant()
    $dc = ([string]$req.dc).Trim()
    if (-not $domain) {
        try { $domain = [DirectoryServices.ActiveDirectory.Domain]::GetComputerDomain().Name.ToLowerInvariant() }
        catch { throw 'Cannot detect a joined AD domain. Enter a DNS domain name.' }
    }
    if ($domain -notmatch '^(?=.{3,253}$)[a-z0-9-]+(\.[a-z0-9-]+)+$') { throw 'Invalid DNS domain.' }
    if ($dc -and ($dc -notmatch '^[a-zA-Z0-9.-]{1,253}$' -or $dc -match '^\d+\.\d+\.\d+\.\d+$')) {
        throw 'Enter a DC hostname, not an IP address (Kerberos requires DNS).'
    }
    $server = if ($dc) { $dc } else { $domain }
    $conn = New-Ldap $server
    try {
        $rootRequest = [System.DirectoryServices.Protocols.SearchRequest]::new(
            '', '(objectClass=*)', [System.DirectoryServices.Protocols.SearchScope]::Base,
            [string[]]@('defaultNamingContext','dnsHostName','configurationNamingContext','isRODC'))
        $rootResp = [System.DirectoryServices.Protocols.SearchResponse]$conn.SendRequest($rootRequest)
        if ($rootResp.Entries.Count -eq 0) { throw 'LDAP RootDSE could not be read.' }
        $root = $rootResp.Entries[0]
        $baseDn = Get-Attribute $root 'defaultNamingContext'
        $actualDc = Get-Attribute $root 'dnsHostName'
        if (-not $baseDn -or -not $actualDc) { throw 'LDAP RootDSE missing domain/DC data.' }
        $resolvedDomain = (([regex]::Matches($baseDn,'(?i)(?:^|,)DC=([^,]+)') | ForEach-Object { $_.Groups[1].Value }) -join '.').ToLowerInvariant()
        if ($resolvedDomain -ne $domain) { throw ('Selected DC belongs to {0}, not {1}.' -f $resolvedDomain,$domain) }
        $isRODC = (Get-Attribute $root 'isRODC') -eq 'TRUE'
        switch ($Mode) {
            'Snapshot' {
                # Step 1: read-only LDAP/RootDSE from ONE signed+sealed session.
                # No GPO or AD writes and no remote client execution.
                $dcRows = @(Search-Entries $conn $baseDn '(&(objectCategory=computer)(|(primaryGroupID=516)(primaryGroupID=521)))' @('name','dNSHostName','primaryGroupID') 5000)
                $controllers = @($dcRows | ForEach-Object {
                    $name = Get-Attribute $_ 'dNSHostName'
                    if ($name) {
                        [pscustomobject]@{
                            name=$name
                            writable=((Get-Attribute $_ 'primaryGroupID') -ne '521')
                        }
                    }
                } | Sort-Object name)
                if ($controllers.Count -eq 0) {
                    $controllers = @([pscustomobject]@{name=$actualDc;writable=(-not $isRODC)})
                }
                $ouRows = @(Search-Entries $conn $baseDn '(objectCategory=organizationalUnit)' @('name','distinguishedName') 5000)
                $ous = @($ouRows | ForEach-Object {
                    [pscustomobject]@{name=(Get-Attribute $_ 'name');dn=$_.DistinguishedName}
                } | Sort-Object dn)
                $groupRows = @(Search-Entries $conn $baseDn '(&(objectCategory=group)(groupType:1.2.840.113556.1.4.803:=2147483648))' @('name','sAMAccountName','distinguishedName') 5000)
                $groups = @($groupRows | ForEach-Object {
                    [pscustomobject]@{
                        name=(Get-Attribute $_ 'name')
                        sam=(Get-Attribute $_ 'sAMAccountName')
                        dn=$_.DistinguishedName
                    }
                } | Sort-Object name)
                $policyRoot = 'CN=Policies,CN=System,' + $baseDn
                $policyRows = @(Search-Entries $conn $policyRoot '(objectClass=groupPolicyContainer)' @('displayName','name') 5000)
                $gpos = @($policyRows | ForEach-Object {
                    [pscustomobject]@{
                        name=(Get-Attribute $_ 'displayName')
                        id=(Get-Attribute $_ 'name')
                    }
                } | Sort-Object name)
                $result = [pscustomobject]@{
                    domain=$resolvedDomain
                    dc=$actualDc
                    baseDn=$baseDn
                    dcWritable=(-not $isRODC)
                    readOnly=$true
                    observedUtc=[datetime]::UtcNow.ToString('o')
                    capped=($dcRows.Count -ge 5000 -or $ouRows.Count -ge 5000 -or
                        $groupRows.Count -ge 5000 -or $policyRows.Count -ge 5000)
                    controllers=@($controllers)
                    ous=@($ous)
                    groups=@($groups)
                    gpos=@($gpos)
                }
            }
            'Connect' {
                $result = [pscustomobject]@{
                    domain=$resolvedDomain; dc=$actualDc; baseDn=$baseDn
                    credentialContext=[Security.Principal.WindowsIdentity]::GetCurrent().Name
                    gpoToolsAvailable=$true
                    rsatRequired=$false
                    deploymentMode='Native ADSI/LDAP and Windows GPO COM, no RSAT/WinRM'
                    dcWritable=(-not $isRODC)
                }
            }
            'DomainControllers' {
                $computers = @(Search-Entries $conn $baseDn '(&(objectCategory=computer)(|(primaryGroupID=516)(primaryGroupID=521)))' @('name','dNSHostName','primaryGroupID') 5000)
                $result = @($computers | ForEach-Object {
                    $host = Get-Attribute $_ 'dNSHostName'
                    if ($host) {
                        [pscustomobject]@{ name=$host; site=''; writable=((Get-Attribute $_ 'primaryGroupID') -ne '521') }
                    }
                } | Sort-Object name)
                if (@($result).Count -eq 0) { $result = @([pscustomobject]@{ name=$actualDc;site='';writable=(-not $isRODC) }) }
            }
            'OUs' {
                $result = @(Search-Entries $conn $baseDn '(objectCategory=organizationalUnit)' @('name','distinguishedName') 5000 | ForEach-Object {
                    [pscustomobject]@{ name=(Get-Attribute $_ 'name'); dn=$_.DistinguishedName }
                } | Sort-Object dn)
            }
            'Groups' {
                $result = @(Search-Entries $conn $baseDn '(&(objectCategory=group)(groupType:1.2.840.113556.1.4.803:=2147483648))' @('name','sAMAccountName','distinguishedName') 5000 | ForEach-Object {
                    [pscustomobject]@{ name=(Get-Attribute $_ 'name'); sam=(Get-Attribute $_ 'sAMAccountName'); dn=$_.DistinguishedName }
                } | Sort-Object name)
            }
            'Permissions' {
                $user = ([string]$req.user).Trim()
                if (-not $user) { $user = [string]$env:USERNAME }
                $ouDn = ([string]$req.ouDn).Trim()
                if (-not $ouDn -or $ouDn -notlike ('*,' + $baseDn)) {
                    throw 'Select an OU within this domain before rights preflight.'
                }
                $sids = Get-PrincipalSids $conn $baseDn $user
                $policies = Get-ReadOnlyDacl $conn ('CN=Policies,CN=System,' + $baseDn)
                $ouAcl = Get-ReadOnlyDacl $conn $ouDn
                # Schema GUIDs: groupPolicyContainer and gPLink attribute.
                $create = Test-PotentialDirectoryAccess $policies $sids 1 (
                    [guid]'f30e3bc2-9ff0-11d1-b603-0000f80367c1')
                $link = Test-PotentialDirectoryAccess $ouAcl $sids 32 (
                    [guid]'f30e3bc1-9ff0-11d1-b603-0000f80367c1')
                $result = [pscustomobject]@{
                    domain=$resolvedDomain;dc=$actualDc
                    user=$user;readOnly=$true;dcWritable=(-not $isRODC)
                    gpoCreateAce=$create;ouGpoLinkAce=$link
                    sysvolWrite='NOT_CHECKED_NO_WRITES'
                    fullPermissionVerified=$false
                    message='ACL hints only; nested/deny inheritance, GPO COM and SYSVOL writes need final verification.'
                }
            }
            'Users' {
                $search = ([string]$req.search).Trim()
                if ($search.Length -lt 2 -or $search.Length -gt 48) { throw 'User search must have 2-48 characters.' }
                $term = Escape-Filter $search
                $filter = '(&(objectCategory=person)(objectClass=user)(|(sAMAccountName=*' + $term + '*)(displayName=*' + $term + '*)))'
                $result = @(Search-Entries $conn $baseDn $filter @('name','sAMAccountName','distinguishedName') 75 | ForEach-Object {
                    [pscustomobject]@{ name=(Get-Attribute $_ 'name'); sam=(Get-Attribute $_ 'sAMAccountName'); dn=$_.DistinguishedName }
                } | Sort-Object sam)
            }
        }
    }
    finally { $conn.Dispose() }
    Send-Result -Success $true -Message 'Signed LDAP discovery succeeded; no workstation RSAT needed.' -Data $result
}
catch {
    Send-Result -Success $false -Message $_.Exception.Message -Data $null
    exit 1
}
