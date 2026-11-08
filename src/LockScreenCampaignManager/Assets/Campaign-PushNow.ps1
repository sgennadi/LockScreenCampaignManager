#requires -Version 5.1
# Install, repair, report, cleanup or read-only post-cleanup audit on selected AD computers.
# No WinRM/RSAT. Uses Task Scheduler RPC (Schedule.Service COM) and SYSTEM.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RequestPath,
    [Parameter(Mandatory=$true)][string]$ResponsePath,
    [ValidateSet('List','Preview','Push','Cleanup','Report','Repair','VerifyCleanup')][string]$Mode='List'
)
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.DirectoryServices.Protocols
Add-Type -AssemblyName System.DirectoryServices
function Result([bool]$ok,[string]$message,$data) {
    [IO.File]::WriteAllText($ResponsePath,([ordered]@{success=$ok;message=$message;data=$data}|ConvertTo-Json -Depth 12 -Compress),[Text.UTF8Encoding]::new($false))
}
function A($entry,[string]$key){$v=$entry.Attributes[$key];if($null -ne $v -and $v.Count -gt 0){return [string]$v[0]};return ''}
function Prop($e,[string]$name){if($e.Properties[$name].Count -gt 0){return $e.Properties[$name][0]};return $null}
function DirEntry([string]$dc,[string]$dn){
    $e=[DirectoryServices.DirectoryEntry]::new(('LDAP://{0}/{1}' -f $dc,$dn),$null,$null,([DirectoryServices.AuthenticationTypes]::Secure -bor [DirectoryServices.AuthenticationTypes]::Signing -bor [DirectoryServices.AuthenticationTypes]::Sealing))
    try{$e.RefreshCache();return $e}catch{$e.Dispose();throw}
}
function Esc([string]$t){return $t.Replace('\','\5c').Replace('*','\2a').Replace('(','\28').Replace(')','\29').Replace([char]0,'\00')}
function DomainInfo([string]$domain,[string]$server){
    if($domain -notmatch '^(?=.{3,253}$)[a-z0-9-]+(\.[a-z0-9-]+)+$'){throw 'Invalid DNS domain.'}
    if(-not $server){$server=$domain}
    if($server -notmatch '^[A-Za-z0-9.-]{1,253}$' -or $server -match '^\d+\.\d+\.\d+\.\d+$'){throw 'Use DC hostname.'}
    $root=DirEntry $server 'RootDSE'
    try {
        $base=[string](Prop $root 'defaultNamingContext')
        $actual=([regex]::Matches($base,'(?i)(?:^|,)DC=([^,]+)') | ForEach-Object {$_.Groups[1].Value}) -join '.'
        if($domain -ine $actual){throw 'DC/domain mismatch.'}
        $dc=[string](Prop $root 'dnsHostName');if(-not $dc){$dc=$server}
        return [pscustomobject]@{dc=$dc;dns=$actual;base=$base}
    }finally{$root.Dispose()}
}
function EligibleComputers($info,[string]$ouDn,[string]$groupDn){
    if(-not $ouDn.EndsWith(','+$info.base,[StringComparison]::OrdinalIgnoreCase)) {throw 'OU outside domain.'}
    if(-not $groupDn.EndsWith(','+$info.base,[StringComparison]::OrdinalIgnoreCase)) {throw 'Group outside domain.'}
    $conn=[DirectoryServices.Protocols.LdapConnection]::new([DirectoryServices.Protocols.LdapDirectoryIdentifier]::new($info.dc,389,$false,$false))
    $conn.AuthType=[DirectoryServices.Protocols.AuthType]::Negotiate
    $conn.SessionOptions.ProtocolVersion=3;$conn.SessionOptions.Signing=$true;$conn.SessionOptions.Sealing=$true
    $conn.Timeout=[TimeSpan]::FromSeconds(20)
    try {
        $conn.Bind()
        $filter='(&(objectCategory=computer)(memberOf:1.2.840.113556.1.4.1941:='+ (Esc $groupDn)+'))'
        $cookie=[byte[]]@();$output=@()
        do {
            $search=[DirectoryServices.Protocols.SearchRequest]::new($ouDn,$filter,[DirectoryServices.Protocols.SearchScope]::Subtree,[string[]]@('sAMAccountName','dNSHostName','distinguishedName'))
            $page=[DirectoryServices.Protocols.PageResultRequestControl]::new(200);$page.Cookie=$cookie
            [void]$search.Controls.Add($page)
            $response=[DirectoryServices.Protocols.SearchResponse]$conn.SendRequest($search)
            foreach($e in $response.Entries){
                $fqdn=A $e 'dNSHostName';$sam=A $e 'sAMAccountName'
                if($fqdn -and $fqdn -match '^[A-Za-z0-9][A-Za-z0-9.-]{1,253}$'){
                    $output += [pscustomobject]@{name=$fqdn;sam=$sam;dn=$e.DistinguishedName}
                }
                if($output.Count -ge 2000){return @($output|Sort-Object name)}
            }
            $cookie=[byte[]]@()
            foreach($c in $response.Controls){if($c -is [DirectoryServices.Protocols.PageResultResponseControl]){$cookie=$c.Cookie}}
        }while($cookie.Length -gt 0)
        return @($output|Sort-Object name)
    }finally{$conn.Dispose()}
}
function GetManagedSource($info,[guid]$guid,[string]$ouDn,[bool]$allowDisabled){
    $name='{'+$guid.ToString().ToUpperInvariant()+'}'
    $gpo=DirEntry $info.dc ('CN='+$name+',CN=Policies,CN=System,'+$info.base)
    try {
        if(@($gpo.Properties['objectClass']) -notcontains 'groupPolicyContainer'){throw 'Not a GPO.'}
        if(-not $allowDisabled -and ([int](Prop $gpo 'flags') -band 2) -ne 0){throw 'Computer configuration is disabled in this GPO.'}
        $gpoDn=$gpo.Properties['distinguishedName'][0]
    } finally{$gpo.Dispose()}
    $ou=DirEntry $info.dc $ouDn
    try {
        $raw=[string](Prop $ou 'gPLink')
        $pattern='(?i)\[LDAP://'+[regex]::Escape([string]$gpoDn)+';(?<flags>\d+)\]'
        $m=[regex]::Match($raw,$pattern)
        # Cleanup may be needed after Disable or Unlink. The object, campaign ID,
        # selected OU and computer group are still revalidated; only a new Push
        # needs an enabled GPO link at the OU.
        if(-not $m.Success -and -not $allowDisabled) {throw 'GPO is not linked at selected OU.'}
        if($m.Success -and -not $allowDisabled -and (([int]$m.Groups['flags'].Value -band 1) -ne 0)) {
            throw 'GPO is disabled at selected OU.'
        }
    }finally{$ou.Dispose()}
    $ini='\\{0}\SYSVOL\{1}\Policies\{2}\Machine\Scripts\psscripts.ini' -f $info.dc,$info.dns,$name
    if(-not (Test-Path -LiteralPath $ini)){throw 'Managed startup GPO metadata not found.'}
    $text=[IO.File]::ReadAllText($ini,[Text.Encoding]::Unicode)
    if($text -notmatch '(?im)^0CmdLine=\\\\[^\r\n]+\\scripts\\LockScreenCampaigns\\(?<id>[a-z0-9_-]+)\\LockScreen-Agent\.ps1\s*$') {throw 'This is not a managed LockScreen campaign.'}
    if($text -notmatch '(?im)^0Parameters=-Mode Install\s*$') {throw 'Unexpected GPO startup parameters.'}
    # Use exact campaign id from the path. The above second -match replaced $Matches, so re-evaluate:
    [void]($text -match '(?im)^0CmdLine=\\\\[^\r\n]+\\scripts\\LockScreenCampaigns\\(?<id>[a-z0-9_-]+)\\LockScreen-Agent\.ps1\s*$')
    $id=$Matches['id']
    $source='\\{0}\SYSVOL\{1}\scripts\LockScreenCampaigns\{2}' -f $info.dc,$info.dns,$id
    $json=Join-Path $source 'campaign.json'
    if(-not (Test-Path -LiteralPath $json)){throw 'Campaign manifest not found on selected DC.'}
    $cfg=Get-Content -LiteralPath $json -Raw -Encoding UTF8|ConvertFrom-Json
    if([string]$cfg.campaignId -cne $id){throw 'Campaign manifest ID mismatch.'}
    $end=[datetime]::ParseExact([string]$cfg.endLocal,'yyyy-MM-ddTHH:mm:ss',[Globalization.CultureInfo]::InvariantCulture)
    $zone=[TimeZoneInfo]::FindSystemTimeZoneById([string]$cfg.timeZoneId)
    return [pscustomobject]@{id=$id;cfg=$cfg;end=$end;zone=$zone;
        script=('\\{0}\SYSVOL\{0}\scripts\LockScreenCampaigns\{1}\LockScreen-Agent.ps1' -f $info.dns,$id)}
}

# Read-only endpoint observation. No task registration, client scripts, RPC retries
# or registry mutation. An unavailable remote service is Unknown, never "clean".
function Read-FileOrNull([string]$path,[int]$maximum=8192) {
    try {
        $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    } catch [System.Management.Automation.ItemNotFoundException] { return $null }
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Remote path is a reparse point; refusing to follow.'
    }
    if ($item.PSIsContainer -or $item.Length -lt 8 -or $item.Length -gt $maximum) {
        throw 'Remote proof file has unexpected size/type.'
    }
    return Get-Content -LiteralPath $path -Raw -Encoding UTF8 -ErrorAction Stop
}
function RemoteTaskPresent($folder,[string]$taskName) {
    try {
        $task=$folder.GetTask($taskName)
        return ($null -ne $task)
    } catch [Runtime.InteropServices.COMException] {
        if ($_.Exception.HResult -eq -2147024894) { return $false }
        throw
    }
}
function RemoteRegState($base,[string]$valueName) {
    $key=$base.OpenSubKey('SOFTWARE\Policies\Microsoft\Windows\Personalization',$false)
    if($null -eq $key) { return @{Exists=$false;Kind='String';Value=$null} }
    try {
        if(@($key.GetValueNames()) -cnotcontains $valueName) {
            return @{Exists=$false;Kind='String';Value=$null}
        }
        return @{
            Exists=$true
            Kind=$key.GetValueKind($valueName).ToString()
            Value=$key.GetValue($valueName,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        }
    } finally { $key.Dispose() }
}
function RemoteRegMatches($observed,$expected) {
    if($null -eq $expected -or
        $expected.PSObject.Properties.Name -notcontains 'Exists' -or
        $expected.PSObject.Properties.Name -notcontains 'Kind') { return $false }
    if([bool]$observed.Exists -ne [bool]$expected.Exists) { return $false }
    if(-not [bool]$expected.Exists) { return $true }
    if([string]$observed.Kind -cne [string]$expected.Kind) { return $false }
    if([string]$observed.Kind -notin @('String','ExpandString','DWord','QWord','Binary','MultiString')) {return $false}
    $same=($observed.Value | ConvertTo-Json -Depth 5 -Compress) -ceq
        ($expected.Value | ConvertTo-Json -Depth 5 -Compress)
    return [bool]$same
}
function Read-RemoteCleanup([string]$fqdn,[string]$id) {
    $answer=[ordered]@{
        computer=$fqdn;campaignId=$id
        observedUtc=([datetime]::UtcNow.ToString('o'))
        status='Unknown';reason='NotChecked'
        receiptValid=$false;tasksGone=$false;localFolderGone=$false
        ownerReleased=$false;registryRestored=$false
    }
    $service=$null;$folder=$null;$base=$null
    try {
        if ($fqdn -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]{2,252}$' -or
            $id -notmatch '^[a-z0-9][a-z0-9_-]{0,70}$') {
            throw 'Invalid verified client identity.'
        }
        # Read the remotely enumerated parent. Test-Path alone can hide SMB access
        # failures: Get-ChildItem must succeed before absence can mean "gone".
        $unc='\\{0}\C$\ProgramData\LockScreenCampaigns' -f $fqdn
        $items=@(Get-ChildItem -LiteralPath $unc -Force -ErrorAction Stop)
        $folderItem=@($items | Where-Object {$_.Name -ceq $id})
        if($folderItem.Count -gt 0) {
            $answer.status='Pending';$answer.reason='LocalCampaignFolderStillPresent'
            return [pscustomobject]$answer
        }
        $answer.localFolderGone=$true
        $ownerPath=Join-Path $unc 'active-owner.json'
        $ownerText=Read-FileOrNull $ownerPath 8192
        if($null -ne $ownerText) {
            $owner=$ownerText | ConvertFrom-Json -ErrorAction Stop
            if([string]$owner.CampaignId -cne $id) {
                # Another campaign now owns the policy: cannot certify the old
                # registry is still in its restored state.
                $answer.status='NeedsReview';$answer.reason='AnotherCampaignOwnsRegistry'
                return [pscustomobject]$answer
            }
            $answer.status='Pending';$answer.reason='CampaignOwnerStillPresent'
            return [pscustomobject]$answer
        }
        $answer.ownerReleased=$true
        $receiptPath=Join-Path (Join-Path $unc 'Completed') ($id+'.postcleanup.json')
        $receiptText=Read-FileOrNull $receiptPath 16384
        if($null -eq $receiptText) {
            $answer.status='Pending';$answer.reason='NoPostCleanupReceipt'
            return [pscustomobject]$answer
        }
        $receipt=$receiptText | ConvertFrom-Json -ErrorAction Stop
        if([int]$receipt.schemaVersion -ne 1 -or
           [string]$receipt.campaignId -cne $id -or
           [string]$receipt.cleanupStage -cne 'AfterFileAndTaskRemoval' -or
           -not [bool]$receipt.localRootRemoved -or
           -not [bool]$receipt.scheduledTaskRemoved -or
           -not [bool]$receipt.feedbackTaskRemoved -or
           -not [bool]$receipt.ownerReleased -or
           -not [bool]$receipt.restoredOwnRegistry -or
           $null -eq $receipt.expectedLockScreenImage -or
           $null -eq $receipt.expectedNoChangingLockScreen) {
            $answer.status='NeedsReview';$answer.reason='IncompleteOrUnrestoredReceipt'
            return [pscustomobject]$answer
        }
        $at=[datetime]::MinValue
        if(-not [datetime]::TryParse([string]$receipt.observedUtc,[ref]$at) -or
           $at.ToUniversalTime() -gt [datetime]::UtcNow.AddMinutes(5)) {
            $answer.status='NeedsReview';$answer.reason='InvalidReceiptTime'
            return [pscustomobject]$answer
        }
        $answer.receiptValid=$true
        $service=New-Object -ComObject 'Schedule.Service'
        $service.Connect($fqdn)
        $folder=$service.GetFolder('\')
        $a=RemoteTaskPresent $folder ('LockScreen-LS-'+$id)
        $b=RemoteTaskPresent $folder ('LockScreen-LS-Feedback-'+$id)
        $c=RemoteTaskPresent $folder ('LSCM-Now-'+$id)
        $answer.tasksGone= -not ($a -or $b -or $c)
        if(-not $answer.tasksGone) {
            $answer.status='Pending';$answer.reason='CampaignTaskStillPresent'
            return [pscustomobject]$answer
        }
        # Explicit 64-bit view even when the manager process is x86.
        # LockScreen personalization policy on Windows 11 belongs to native HKLM.
        $base=[Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey(
            [Microsoft.Win32.RegistryHive]::LocalMachine,$fqdn,
            [Microsoft.Win32.RegistryView]::Registry64)
        $observedImage=RemoteRegState $base 'LockScreenImage'
        $observedLock=RemoteRegState $base 'NoChangingLockScreen'
        $answer.registryRestored=(RemoteRegMatches $observedImage $receipt.expectedLockScreenImage) -and
            (RemoteRegMatches $observedLock $receipt.expectedNoChangingLockScreen)
        if($answer.registryRestored) {
            $answer.status='Verified';$answer.reason='RemoteFootprintMatchesPostCleanupReceipt'
        } else {
            $answer.status='NeedsReview';$answer.reason='RegistryDiffersFromReceipt'
        }
    } catch {
        $answer.status='Unknown'
        $answer.reason=if(Is-TemporaryOffline $_.Exception){'ClientOfflineOrUnreachable'}else{'ReadDeniedOrInvalidEvidence'}
    } finally {
        try{if($null -ne $base){$base.Dispose()}}catch{}
        foreach($o in @($folder,$service)){
            try{if($null -ne $o -and [Runtime.InteropServices.Marshal]::IsComObject($o)){
                [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($o)
            }}catch{}
        }
    }
    return [pscustomobject]$answer
}

# Only known network/RPC availability failures are safe for unattended retries.
# Permission errors, invalid campaign/scope, duplicate jobs and corrupt files are NOT offline.
function Is-TemporaryOffline([Exception]$error) {
    $codes=@('800706BA','800706BF','80070035','80070043','800704CF',
        '800705B4','80070079','80070040','80072EE7')
    for($i=0;$i -lt 5 -and $null -ne $error;$i++) {
        $code=([int64]$error.HResult -band [int64]4294967295).ToString('X8')
        if($codes -contains $code) {return $true}
        if($error -is [System.Net.Sockets.SocketException]) {
            if($error.SocketErrorCode -in @(
                [System.Net.Sockets.SocketError]::HostNotFound,
                [System.Net.Sockets.SocketError]::HostUnreachable,
                [System.Net.Sockets.SocketError]::NetworkUnreachable,
                [System.Net.Sockets.SocketError]::TimedOut)) {return $true}
        }
        $error=$error.InnerException
    }
    return $false
}
function ScheduledNow([string]$hostName,[string]$script,[string]$mode,[string]$id) {
    $taskName='LSCM-Now-'+$id
    if ($hostName -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]{1,253}$') { throw 'Invalid AD computer DNS name.' }
    $service=New-Object -ComObject 'Schedule.Service'
    $registered=$null;$folder=$null;$registrationAttempted=$false
    try {
        $service.Connect($hostName)
        $folder=$service.GetFolder('\')
        try {$old=$folder.GetTask($taskName)} catch {$old=$null}
        if($null -ne $old){throw 'An earlier one-time LSCM task exists. Inspect it on target before retry.'}
        $task=$service.NewTask(0)
        $task.RegistrationInfo.Description='LSCM approved remote campaign '+$mode
        $task.Principal.UserId='SYSTEM';$task.Principal.LogonType=5;$task.Principal.RunLevel=1
        $task.Settings.Enabled=$true;$task.Settings.Hidden=$true
        $task.Settings.AllowDemandStart=$true;$task.Settings.StartWhenAvailable=$true
        $task.Settings.ExecutionTimeLimit='PT10M'
        $task.Settings.DisallowStartIfOnBatteries=$false
        $action=$task.Actions.Create(0)
        $action.Path=$env:SystemRoot+'\System32\WindowsPowerShell\v1.0\powershell.exe'
        $action.Arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$script+'" -Mode '+$mode
        # After this call starts, any RPC loss is ambiguous: the task may exist
        # or have run. NEVER report it as offline eligible for automatic retry.
        $registrationAttempted=$true
        $registered=$folder.RegisterTaskDefinition($taskName,$task,2,'SYSTEM',$null,5)
        [void]$registered.Run($null)
        $deadline=[DateTime]::UtcNow.AddSeconds(80)
        $running=$true;$seenStarted=$false
        do {
            Start-Sleep -Seconds 2
            $registered=$folder.GetTask($taskName)
            $last=[datetime]$registered.LastRunTime
            if($last -gt [DateTime]::Now.AddMinutes(-5)) {$seenStarted=$true}
            $running=([int]$registered.State -eq 4)
        } while([DateTime]::UtcNow -lt $deadline -and ($running -or -not $seenStarted))
        if($running -or -not $seenStarted){return [pscustomobject]@{host=$hostName;status='pending';exit=$null;note='Remote task still running or not started. Task left for inspection.'}}
        $exit=[int]$registered.LastTaskResult
        $folder.DeleteTask($taskName,0)
        return [pscustomobject]@{host=$hostName;status=$(if($exit -eq 0){'success'}else{'failed'});exit=$exit;
            note='Remote task completed; one-time task deleted. Local campaign task may remain intentionally.'}
    } catch {
        if($registrationAttempted) {
            return [pscustomobject]@{host=$hostName;status='pending';exit=$null;
                note='Remote task registration or result is uncertain; inspect Task Scheduler and client health. No automatic RPC retry.'}
        }
        throw
    } finally {
        foreach($o in @($registered,$folder,$service)){
            try{if($null -ne $o -and [Runtime.InteropServices.Marshal]::IsComObject($o)){
                [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($o)
            }}catch{}
        }
    }
}
try {
    $req=Get-Content -LiteralPath $RequestPath -Raw -Encoding UTF8|ConvertFrom-Json
    $domain=([string]$req.domain).Trim().ToLowerInvariant();$dc=([string]$req.dc).Trim()
    $ouDn=([string]$req.ouDn).Trim();$groupDn=([string]$req.groupDn).Trim()
    $info=DomainInfo $domain $dc
    $group=DirEntry $info.dc $groupDn
    try {
        if(@($group.Properties['objectClass']) -notcontains 'group'){throw 'Selected object is not a security group.'}
        if([int64](Prop $group 'groupType') -ge 0){throw 'Selected group is not a security group.'}
    }finally{$group.Dispose()}
    $eligible=@(EligibleComputers $info $ouDn $groupDn)
    if($Mode -eq 'List') {Result $true 'Eligible AD computer accounts in the selected OU AND group (nested membership). No changes made.' $eligible;exit 0}
    if($eligible.Count -gt 2000){throw 'Too many computers; narrow the scope.'}
    $guid=[guid]([string]$req.gpoId)
    $purpose=if($req.PSObject.Properties.Name -contains 'purpose'){[string]$req.purpose}else{'Push'}
    if($purpose -notin @('Push','Cleanup','Report','Repair','VerifyCleanup')){throw 'Invalid purpose.'}
    $managed=GetManagedSource $info $guid $ouDn ($purpose -in @('Cleanup','Report','VerifyCleanup'))
    $selected=@($req.computers)
    if($selected.Count -lt 1 -or $selected.Count -gt 25){throw 'Choose between 1 and 25 computers per push.'}
    $allow=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($e in $eligible){[void]$allow.Add($e.name)}
    $confirmed=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($item in $selected) {
        $hostName=([string]$item).Trim()
        if(-not $allow.Contains($hostName) -or -not $confirmed.Add($hostName)) {throw 'Computer is not in the selected OU+group or appears twice.'}
    }
    if($Mode -eq 'VerifyCleanup') {
        if($purpose -cne 'VerifyCleanup'){throw 'Read-only mode/purpose mismatch.'}
        $scan=@()
        foreach($hostName in $selected){$scan+= Read-RemoteCleanup $hostName $managed.id}
        Result $true 'Read-only post-cleanup checks finished. Missing/unavailable checks remain Unknown.' ([ordered]@{
            campaignId=$managed.id;gpoId=$guid.ToString('D');results=@($scan)
        })
        exit 0
    }
    $now=[TimeZoneInfo]::ConvertTimeFromUtc([datetime]::UtcNow,$managed.zone)
    if($purpose -in @('Push','Repair') -and $now -ge $managed.end){throw 'Campaign has ended. Reuse the GPO with a new campaign.'}
    $manifestPath='\\{0}\SYSVOL\{1}\scripts\LockScreenCampaigns\{2}\campaign.json' -f $info.dc,$info.dns,$managed.id
    $manifestHash=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    $hashInput=$info.dns+'|'+$info.dc+'|'+$ouDn+'|'+$groupDn+'|'+$guid+'|'+$managed.id+'|'+$purpose+'|'+$manifestHash+'|'+
        (($selected|Sort-Object) -join ',')
    $hash=[Security.Cryptography.SHA256]::Create()
    try{$planHash=[BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($hashInput))).Replace('-','').ToLowerInvariant()}
    finally{$hash.Dispose()}
    if($Mode -eq 'Preview'){
        Result $true 'Preview: no remote tasks created.' ([ordered]@{computers=$selected;count=$selected.Count;campaignId=$managed.id;purpose=$purpose;
            gpoId=$guid.ToString('D');hash=$planHash;start=[string]$managed.cfg.slides[0].atLocal;end=[string]$managed.cfg.endLocal;
            note=$(if($purpose -eq 'Push'){'Push installs immediately, but the image only appears when scheduled start has passed.'}elseif($purpose -eq 'Report'){'Report publishes a fresh client heartbeat; it does not change registry or GPO.'}elseif($purpose -eq 'Repair'){'Repair verifies the trusted SYSVOL package and repairs files on an already eligible computer; it respects the original backup and campaign owner.'}else{'Cleanup restores original registry values and removes local campaign tasks.'})+' Requires Task Scheduler RPC/firewall and local admin on each PC.'});exit 0
    }
    if([string]$req.planHash -cne $planHash -or [string]$req.approveGuid -ine $guid.ToString('D')){throw 'Reconfirm target list and exact GPO GUID.'}
    if($Mode -notin @('Push','Cleanup','Report','Repair') -or $Mode -ne $purpose){throw 'Remote action mismatch; preview again.'}
    $script=if($Mode -in @('Push','Repair')){$managed.script}else{'C:\ProgramData\LockScreenCampaigns\'+$managed.id+'\LockScreen-Agent.ps1'}
    $callMode=if($Mode -eq 'Repair'){'Repair'}elseif($Mode -eq 'Push'){'Install'}elseif($Mode -eq 'Report'){'Report'}else{'Cleanup'}
    $results=@()
    foreach($hostName in $selected){
        try{$results+=ScheduledNow $hostName $script $callMode $managed.id}
        catch {
            $offline=Is-TemporaryOffline $_.Exception
            $results+=[pscustomobject]@{
                host=$hostName;status=$(if($offline){'offline'}else{'failed'});exit=$null;
                reason=$(if($offline){'RPC endpoint temporarily unreachable'}else{'RPC, credentials, task or campaign error'})
                hresult=('0x'+([int64]$_.Exception.HResult -band [int64]4294967295).ToString('X8'))
                note=('Task Scheduler RPC: '+$_.Exception.GetType().Name+
                      $(if($offline){'; eligible for deferred retry'}else{'; manual investigation required'}))
            }
        }
    }
    Result $true 'Remote push finished. Review each host result; verify local agent status.' ([ordered]@{
        results=$results;successful=@($results|Where-Object{$_.status -eq 'success'}).Count;
        pending=@($results|Where-Object{$_.status -eq 'pending'}).Count;
        offline=@($results|Where-Object{$_.status -eq 'offline'}).Count;
        failed=@($results|Where-Object{$_.status -eq 'failed'}).Count;
        note='Remote task action is recorded per host; actual visible lock screen may refresh at the next lock/logon.'
    })
} catch {Result $false $_.Exception.Message $null;exit 1}
