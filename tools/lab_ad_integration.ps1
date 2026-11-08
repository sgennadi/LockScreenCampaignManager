#requires -Version 5.1
# Dedicated self-hosted Windows lab controller only. Never run against production OUs.
# AD and GPO operations use existing verified project engines; no WinRM or RSAT.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Preflight','Full')][string]$Mode,
    [Parameter(Mandatory=$true)][string]$Domain,
    [Parameter(Mandatory=$true)][string]$Dc,
    [Parameter(Mandatory=$true)][string]$OuDn,
    [Parameter(Mandatory=$true)][string]$GroupDn,
    [Parameter(Mandatory=$true)][string]$Clients,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9]{1,15}$')][string]$RunId,
    [Parameter(Mandatory=$true)][ValidateRange(1,99)][int]$Attempt,
    [Parameter(Mandatory=$true)][string]$ReportDirectory
)
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
$Id='lscm-ci-'+$RunId+'-'+$Attempt
$PolicyName='LSCM-CI-Lab-'+$Id
$Names=@($Clients.Split(';') | ForEach-Object { $_.Trim().ToUpperInvariant() } |
    Where-Object { $_.Length -gt 0 })
if ($Names.Count -lt 1 -or $Names.Count -gt 3 -or
    @($Names | Select-Object -Unique).Count -ne $Names.Count) {
    throw 'Specify one to three distinct disposable Windows 11 test PCs.'
}
foreach($name in $Names) {
    if ($name -cnotmatch '^[A-Z0-9-]{3,15}$' -or $name -eq $env:COMPUTERNAME) {
        throw 'Invalid test-PC name or runner is itself a test PC.'
    }
}
if ($Domain -cnotmatch '^[a-z0-9-]+(\.[a-z0-9-]+)+$' -or
    $Dc -cnotmatch '^[a-zA-Z0-9.-]{3,253}$' -or
    $OuDn -notmatch '^(?i)OU=LSCM-CI(?:-|,)') {
    throw 'Use a real DC hostname and a dedicated first-level LSCM-CI lab OU.'
}
if ($GroupDn -notmatch '^(?i)CN=LSCM-CI(?:-|,)') {
    throw 'Target computer security group must have an LSCM-CI name.'
}
$Domain=$Domain.ToLowerInvariant()
$root=Split-Path -Parent $PSScriptRoot
$probeScript=Join-Path $PSScriptRoot 'lab_win11_probe.ps1'
$deployEngine=Join-Path $root 'src\LockScreenCampaignManager\Assets\GPO-Deploy.ps1'
$lifecycleEngine=Join-Path $root 'src\LockScreenCampaignManager\Assets\Gpo-Lifecycle.ps1'
$conflictEngine=Join-Path $root 'src\LockScreenCampaignManager\Assets\Gpo-ConflictScan.ps1'
$agentSource=Join-Path $root 'src\LockScreenCampaignManager\Assets\LockScreen-Agent.ps1'
$Work=Join-Path ([IO.Path]::GetTempPath()) ('LSCM-LIVE-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $Work -Force | Out-Null
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$summary=[ordered]@{
    mode=$Mode;runId=$RunId;attempt=$Attempt;campaignId=$Id;policyName=$PolicyName
    preflightPassed=$false;deployPassed=$false
    gpoId='';clientChecks=@();rotationChecks=@()
    clientCleanupVerified=$false;gpoUnlinked=$false
    gpoDeleted=$false;sourceDeleted=$false;passed=$false
    warnings=@();error=''
}
$remotePaths=@{}
$clientBootBefore=@{}
$deployment=$null
$allClean=$true

function Invoke-Engine([string]$script,[string]$mode,[object]$body) {
    $request=Join-Path $Work (([guid]::NewGuid().ToString('N'))+'-request.json')
    $response=Join-Path $Work (([guid]::NewGuid().ToString('N'))+'-response.json')
    [IO.File]::WriteAllText($request,($body | ConvertTo-Json -Depth 12),
        [Text.UTF8Encoding]::new($false))
    $exe=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $script -Mode $mode -RequestPath $request -ResponsePath $response | Out-Null
    $code=$LASTEXITCODE
    if (-not (Test-Path -LiteralPath $response)) {
        throw ('Domain engine did not return response: '+(Split-Path $script -Leaf))
    }
    $result=Get-Content -LiteralPath $response -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($code -ne 0 -or -not [bool]$result.success) {
        throw ('Domain engine failed ({0}): {1}' -f $mode,[string]$result.message)
    }
    return $result.data
}
function Read-Ad([string]$dn) {
    $flags=[DirectoryServices.AuthenticationTypes]::Secure -bor
        [DirectoryServices.AuthenticationTypes]::Signing -bor
        [DirectoryServices.AuthenticationTypes]::Sealing
    $entry=[DirectoryServices.DirectoryEntry]::new(
        ('LDAP://'+$Dc+'/'+$dn),$null,$null,$flags)
    try { $entry.RefreshCache(); return $entry } catch { $entry.Dispose(); throw }
}
function Property($entry,[string]$property) {
    if ($entry.Properties[$property].Count -gt 0) { return $entry.Properties[$property][0] }
    return $null
}
function Assert-LabScope {
    $rootDse=Read-Ad 'RootDSE'
    try {
        $actualBase=[string](Property $rootDse 'defaultNamingContext')
        $actualDomain=([regex]::Matches($actualBase,'(?i)(?:^|,)DC=([^,]+)') |
            ForEach-Object {$_.Groups[1].Value}) -join '.'
        if ($actualDomain -ine $Domain) { throw 'Selected DC belongs to another domain.' }
        if ([string](Property $rootDse 'isRODC') -ine 'FALSE') {
            throw 'Writable DC is not confirmed.'
        }
        $utcText=[string](Property $rootDse 'currentTime')
        if ($utcText -notmatch '^(?<time>\d{14})(?:\.\d{1,7})?Z$') {
            throw 'Selected DC currentTime unavailable.'
        }
        $utc=[datetime]::ParseExact($Matches['time'],'yyyyMMddHHmmss',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal -bor
            [Globalization.DateTimeStyles]::AdjustToUniversal)
        if ([math]::Abs(([datetime]::UtcNow-$utc).TotalSeconds) -gt 120) {
            throw 'Lab controller differs from chosen DC time by over 2 minutes.'
        }
    } finally { $rootDse.Dispose() }
    if (-not $OuDn.EndsWith(','+$actualBase,[StringComparison]::OrdinalIgnoreCase) -or
        -not $GroupDn.EndsWith(','+$actualBase,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Lab OU/group must be within the selected domain.'
    }
    $ou=Read-Ad $OuDn
    try {
        if (@($ou.Properties['objectClass']) -notcontains 'organizationalUnit') {
            throw 'Target is not an OU.'
        }
        $search=New-Object DirectoryServices.DirectorySearcher($ou)
        try {
            $search.SearchScope=[DirectoryServices.SearchScope]::OneLevel
            $search.Filter='(objectClass=*)'
            [void]$search.PropertiesToLoad.Add('objectClass')
            [void]$search.PropertiesToLoad.Add('distinguishedName')
            [void]$search.PropertiesToLoad.Add('sAMAccountName')
            [void]$search.PropertiesToLoad.Add('userAccountControl')
            [void]$search.PropertiesToLoad.Add('operatingSystem')
            $found=$search.FindAll()
            try {
                $computers=@()
                foreach($row in $found) {
                    $classes=@($row.Properties['objectclass'] | ForEach-Object { [string]$_ })
                    if ($classes -notcontains 'computer') {
                        throw 'Test OU must contain ONLY approved computer objects; child OUs, groups and other objects are forbidden.'
                    }
                    $computers+= [pscustomobject]@{
                        dn=[string]$row.Properties['distinguishedname'][0]
                        sam=[string]$row.Properties['samaccountname'][0]
                        os=[string]$row.Properties['operatingsystem'][0]
                        uac=[int]$row.Properties['useraccountcontrol'][0]
                    }
                }
            } finally { $found.Dispose() }
        } finally { $search.Dispose() }
    } finally { $ou.Dispose() }
    if ($computers.Count -ne $Names.Count) {
        throw 'Lab OU must contain exactly the requested test computer objects.'
    }
    foreach($name in $Names) {
        $matched=@($computers | Where-Object { $_.sam -ieq ($name+'$') })
        if ($matched.Count -ne 1 -or ($matched[0].uac -band 2) -ne 0 -or
            $matched[0].os -notmatch '(?i)Windows 11') {
            throw ('Test PC missing, disabled or not Windows 11 in isolated OU: '+$name)
        }
    }
    $group=Read-Ad $GroupDn
    try {
        if (@($group.Properties['objectClass']) -notcontains 'group' -or
            [int](Property $group 'groupType') -ge 0) {
            throw 'Selected LSCM-CI group is not an AD security group.'
        }
        $members=@($group.Properties['member'] | ForEach-Object { [string]$_ })
        $sid=[Security.Principal.SecurityIdentifier]::new(
            [byte[]](Property $group 'objectSid'),0)
        $rid=[int]($sid.Value.Split('-')[-1])
        if ($rid -lt 1000 -or [int](Property $group 'adminCount') -eq 1 -or
            [string](Property $group 'isCriticalSystemObject') -ieq 'TRUE') {
            throw 'Default, protected or privileged domain groups cannot be used as a CI lab security filter.'
        }
        if ($members.Count -ne $Names.Count) {
            throw 'Test security group must contain only selected computers.'
        }
        foreach($computer in $computers) {
            if (@($members | Where-Object { $_ -ieq $computer.dn }).Count -ne 1) {
                throw 'Lab security group direct membership does not match the isolated OU.'
            }
        }
    } finally { $group.Dispose() }
    $conflicts=Invoke-Engine $conflictEngine 'Scan' @{
        domain=$Domain;dc=$Dc;ouDn=$OuDn;computer=''
    }
    if ([int]$conflicts.highRisk -gt 0) {
        throw 'Conflicting inherited computer lock-screen policies detected in lab OU.'
    }
    return $utc
}
function Remote-Folder([string]$client) {
    return ('\\'+$client+'\C$\ProgramData\LSCM-CI-Lab\'+$Id)
}
function Schedule-Remote([string]$client,[string]$tag,[string]$command,[string]$arguments) {
    $service=New-Object -ComObject 'Schedule.Service'
    $service.Connect(($client+'.'+$Domain))
    $folder=$service.GetFolder('\')
    $name='LSCM-CI-'+$Id+'-'+$tag
    if ($name.Length -gt 220) { throw 'Remote Task Scheduler name too long.' }
    try {
        $existing=$folder.GetTask($name)
        if ($null -ne $existing) { throw 'The named lab task already exists.' }
    } catch [System.Runtime.InteropServices.COMException] {
        # 0x80070002: file not found means the task does not exist.
        if ($_.Exception.HResult -ne -2147024894) { throw }
    }
    $definition=$service.NewTask(0)
    $definition.RegistrationInfo.Description='Disposable LSCM live lab '+$Id
    $definition.Settings.Enabled=$true
    $definition.Settings.AllowDemandStart=$true
    $definition.Settings.ExecutionTimeLimit='PT5M'
    $definition.Principal.UserId='SYSTEM'
    $definition.Principal.LogonType=5
    $definition.Principal.RunLevel=1
    $action=$definition.Actions.Create(0)
    $action.Path=$command
    $action.Arguments=$arguments
    # TASK_CREATE=2; TASK_LOGON_SERVICE_ACCOUNT=5. Never replace an existing task.
    $task=$folder.RegisterTaskDefinition($name,$definition,2,'SYSTEM',$null,5,$null)
    [void]$task.Run($null)
    return $name
}
function Remove-RemoteTask([string]$client,[string]$name) {
    $service=New-Object -ComObject 'Schedule.Service'
    $service.Connect(($client+'.'+$Domain))
    $folder=$service.GetFolder('\')
    try { $folder.DeleteTask($name,0) }
    catch [System.Runtime.InteropServices.COMException] {
        if ($_.Exception.HResult -ne -2147024894) { throw }
    }
}
function Run-Probe([string]$client,[string]$mode,[string]$image,[int]$seconds=120) {
    $base=Remote-Folder $client
    $leaf=$mode.ToLowerInvariant()+$(if ($mode -eq 'Verify') {'-'+$image.Substring(0,2)} else {''})+'.json'
    $report=Join-Path $base $leaf
    if (Test-Path -LiteralPath $report) { throw 'A stale probe result already exists.' }
    $scriptPath='C:\ProgramData\LSCM-CI-Lab\'+$Id+'\lab_win11_probe.ps1'
    $args='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$scriptPath+'"'+
        ' -CampaignId '+$Id+' -Mode '+$mode+' -ExpectedImage '+$image+
        ' -ExpectedDomain '+$Domain+' -ExpectedComputer '+$client
    $taskName=Schedule-Remote $client ($mode+'-'+$image.Substring(0,2)) (
        'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe') $args
    try {
        $cutoff=[datetime]::UtcNow.AddSeconds($seconds)
        while ([datetime]::UtcNow -lt $cutoff) {
            if (Test-Path -LiteralPath $report -PathType Leaf) {
                $content=Get-Content -LiteralPath $report -Raw -Encoding UTF8 | ConvertFrom-Json
                if ([string]$content.campaignId -cne $Id -or
                    [string]$content.mode -cne $mode) {
                    throw 'Unexpected client result identity.'
                }
                return $content
            }
            Start-Sleep -Seconds 6
        }
        throw ('Timed out waiting for '+$mode+' result from '+$client)
    }
    finally { Remove-RemoteTask $client $taskName }
}
function Await-Image([string]$client,[string]$image,[datetime]$deadline) {
    $base=Remote-Folder $client
    $leaf='verify-'+$image.Substring(0,2)+'.json'
    $report=Join-Path $base $leaf
    while ([datetime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $report -PathType Leaf) {
            $response=Get-Content -LiteralPath $report -Raw -Encoding UTF8 | ConvertFrom-Json
            if ([bool]$response.passed) { return $response }
            Remove-Item -LiteralPath $report -Force
        }
        try {
            $response=Run-Probe $client 'Verify' $image 90
            if ([bool]$response.passed) { return $response }
            $summary.warnings+=('Client '+$client+': '+[string]$response.error)
            Remove-Item -LiteralPath $report -Force -ErrorAction SilentlyContinue
        }
        catch {
            $summary.warnings+=('Probe retry '+$client+': '+$_.Exception.Message)
            Remove-Item -LiteralPath $report -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Seconds 18
    }
    throw ('Client '+$client+' did not pass the '+$image+' GPO/registry/agent check.')
}
function Make-Package([datetime]$dcUtc) {
    Add-Type -AssemblyName System.Drawing
    $package=Join-Path $Work $Id
    $img=Join-Path $package 'images'
    New-Item -ItemType Directory -Path $img -Force | Out-Null
    $colors=@([Drawing.Color]::DarkCyan,[Drawing.Color]::DarkOrange)
    $slides=@()
    $start=$dcUtc.AddMinutes(-2)
    $second=$dcUtc.AddMinutes(15)
    for($i=1;$i -le 2;$i++) {
        $file=('{0:D2}.jpg' -f $i)
        $bmp=[Drawing.Bitmap]::new(800,450)
        try {
            $graphics=[Drawing.Graphics]::FromImage($bmp)
            try {
                $graphics.Clear($colors[$i-1])
                $font=[Drawing.Font]::new('Segoe UI',27,[Drawing.FontStyle]::Bold)
                try {
                    $graphics.DrawString(('LSCM CI LAB '+$Id+' IMAGE '+$i),$font,
                        [Drawing.Brushes]::White,20,180)
                } finally { $font.Dispose() }
            } finally { $graphics.Dispose() }
            $bmp.Save((Join-Path $img $file),[Drawing.Imaging.ImageFormat]::Jpeg)
        } finally { $bmp.Dispose() }
        $at=if ($i -eq 1) {$start}else{$second}
        $slides+= [ordered]@{
            atLocal=$at.ToString('yyyy-MM-ddTHH:mm:ss')
            fileName=$file
            sha256=(Get-FileHash -LiteralPath (Join-Path $img $file) -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    Copy-Item -LiteralPath $agentSource -Destination (Join-Path $package 'LockScreen-Agent.ps1') -ErrorAction Stop
    $manifest=[ordered]@{
        schemaVersion=1;campaignId=$Id;name=$PolicyName
        timeZoneId='UTC';randomizePerDevice=$false
        agentSha256=(Get-FileHash -LiteralPath (Join-Path $package 'LockScreen-Agent.ps1') -Algorithm SHA256).Hash.ToLowerInvariant()
        endLocal=$dcUtc.AddMinutes(60).ToString('yyyy-MM-ddTHH:mm:ss')
        slides=$slides
    }
    [IO.File]::WriteAllText((Join-Path $package 'campaign.json'),
        ($manifest | ConvertTo-Json -Depth 9),[Text.UTF8Encoding]::new($false))
    return [pscustomobject]@{path=$package;second=$second;end=$dcUtc.AddMinutes(60)}
}
function Gpo-Action([string]$gpoId,[string]$action) {
    $request=@{domain=$Domain;dc=$Dc;ouDn=$OuDn;gpoId=$gpoId;action=$action}
    $plan=Invoke-Engine $lifecycleEngine 'Preview' $request
    if ([string]$plan.action -cne $action -or
        [string]$plan.gpoId -ine $gpoId) {
        throw 'GPO lifecycle preview identity mismatch.'
    }
    $request.approveGuid=$gpoId
    $request.planHash=[string]$plan.hash
    [void](Invoke-Engine $lifecycleEngine 'Apply' $request)
}
function Remove-LabSource([string]$gpoId) {
    $parent=('\\{0}\SYSVOL\{1}\scripts\LockScreenCampaigns' -f $Dc,$Domain)
    $source=Join-Path $parent $Id
    if ((Split-Path $source -Parent) -ine $parent -or
        (Split-Path $source -Leaf) -cne $Id) {
        throw 'Refusing to remove non-lab SYSVOL path.'
    }
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { return }
    $cfg=Get-Content -LiteralPath (Join-Path $source 'campaign.json') -Raw -Encoding UTF8 |
        ConvertFrom-Json
    if ([string]$cfg.campaignId -cne $Id -or [string]$cfg.name -cne $PolicyName) {
        throw 'SYSVOL package identity is no longer owned by this CI run.'
    }
    $found=Invoke-Engine $lifecycleEngine 'List' @{domain=$Domain;dc=$Dc}
    if (@($found | Where-Object { $_.id -ieq $gpoId }).Count -gt 0) {
        throw 'Managed GPO still exists; keep SYSVOL source for inspection.'
    }
    # Verify the raw AD GPO object is gone too: an invalid GPT might make
    # the managed-GPO listing omit a still-existing policy.
    $rootDse=Read-Ad 'RootDSE'
    try { $baseDn=[string](Property $rootDse 'defaultNamingContext') }
    finally { $rootDse.Dispose() }
    $container=Read-Ad ('CN=Policies,CN=System,'+$baseDn)
    $finder=New-Object DirectoryServices.DirectorySearcher($container)
    try {
        $finder.SearchScope=[DirectoryServices.SearchScope]::OneLevel
        $finder.Filter='(&(objectClass=groupPolicyContainer)(name={'+([guid]$gpoId).ToString('D')+'}))'
        if ($null -ne $finder.FindOne()) {
            throw 'Test GPO still exists in AD; refusing to remove source files.'
        }
    } finally { $finder.Dispose();$container.Dispose() }
    $item=Get-Item -LiteralPath $source
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Refusing to delete SYSVOL reparse point.'
    }
    foreach($child in @(Get-ChildItem -LiteralPath $source -Recurse -Force)) {
        if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Refusing to delete linked SYSVOL content.'
        }
    }
    Remove-Item -LiteralPath $source -Recurse -Force -ErrorAction Stop
}
function Store-Report {
    $dest=Join-Path $ReportDirectory 'live-ad-lab.json'
    [IO.File]::WriteAllText($dest,($summary | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false))
}

try {
    $clock=Assert-LabScope
    foreach($pc in $Names) {
        $remote=Remote-Folder $pc
        if (Test-Path -LiteralPath $remote) {
            throw ('Previous run files exist on client '+$pc+'; lab is not clean.')
        }
        New-Item -ItemType Directory -Path $remote -Force | Out-Null
        $remotePaths[$pc]=$remote
        $dst=Join-Path $remote 'lab_win11_probe.ps1'
        Copy-Item -LiteralPath $probeScript -Destination $dst -ErrorAction Stop
        if ((Get-FileHash $dst -Algorithm SHA256).Hash -ine
            (Get-FileHash $probeScript -Algorithm SHA256).Hash) {
            throw 'Probe script transfer to test PC failed hash verification.'
        }
        $baseline=Run-Probe $pc 'Preflight' '01.jpg'
        if (-not [bool]$baseline.passed) {
            throw ('Windows 11 client preflight failed on '+$pc+': '+$baseline.error)
        }
        $clientUtc=[datetime]::Parse([string]$baseline.observedUtc).ToUniversalTime()
        if ([math]::Abs(([datetime]::UtcNow-$clientUtc).TotalSeconds) -gt 120) {
            throw ('Windows 11 client has clock skew greater than 2 minutes: '+$pc)
        }
        $clientBootBefore[$pc]=[datetime]::Parse([string]$baseline.bootUtc).ToUniversalTime()
        $summary.clientChecks+= [pscustomobject]@{
            name=$pc;stage='Preflight';passed=$true
            windows11=$baseline.osWindows11;domainJoined=$baseline.domainJoined
            timeSource=$baseline.timeSource;observedUtc=$baseline.observedUtc
            originalBootUtc=$baseline.bootUtc
        }
    }
    $summary.preflightPassed=$true
    if ($Mode -eq 'Preflight') {
        $summary.passed=$true
    }
    else {
        $package=Make-Package $clock
        $request=@{
            domain=$Domain;dc=$Dc;ouDn=$OuDn;groupDn=$GroupDn
            gpoName=$PolicyName;packagePath=$package.path
        }
        $plan=Invoke-Engine $deployEngine 'Plan' $request
        if ([string]$plan.gpoName -cne $PolicyName -or
            [string]$plan.campaignId -cne $Id) {
            throw 'GPO deployment preview did not return the expected test identity.'
        }
        $deployment=Invoke-Engine $deployEngine 'Deploy' $request
        $guid=[guid]([string]$deployment.gpoId)
        $summary.gpoId=$guid.ToString('D')
        $summary.deployPassed=$true
        # Allow replication before startup processing, then reboot each disposable VM.
        Start-Sleep -Seconds 90
        foreach($pc in $Names) {
            $task=Schedule-Remote $pc 'reboot' 'C:\Windows\System32\shutdown.exe' '/r /t 5 /f'
            # Do not remove the task before reboot; it is removed during cleanup.
        }
        $firstDeadline=$package.second.AddMinutes(-1)
        foreach($pc in $Names) {
            $status=Await-Image $pc '01.jpg' $firstDeadline
            if ([datetime]::Parse([string]$status.bootUtc).ToUniversalTime() -le
                $clientBootBefore[$pc]) {
                throw ('A real Windows 11 reboot was not observed: '+$pc)
            }
            $summary.clientChecks+= [pscustomobject]@{
                name=$pc;stage='First image';passed=$status.passed
                gpoApplied=$status.gpoApplied;agentHealthy=$status.agentHealthy
                imagesValid=$status.imagesValid;registryMatches=$status.registryMatches
                scheduledTask=$status.taskPresent;bootUtc=$status.bootUtc
            }
        }
        $secondDeadline=$package.second.AddMinutes(9)
        foreach($pc in $Names) {
            $status=Await-Image $pc '02.jpg' $secondDeadline
            $summary.rotationChecks+= [pscustomobject]@{
                name=$pc;stage='Scheduled image rotation';passed=$status.passed
                gpoApplied=$status.gpoApplied;agentHealthy=$status.agentHealthy
                imagesValid=$status.imagesValid;registryMatches=$status.registryMatches
                scheduledTask=$status.taskPresent;expectedImage='02.jpg'
            }
        }
        $summary.passed=$true
    }
}
catch {
    $summary.error=$_.Exception.Message
    $summary.passed=$false
}
finally {
    # Never remove another GPO or client policy. An incomplete run preserves
    # GPO/SYSVOL artifacts for investigation, after best-effort safe unlink.
    if ($null -ne $deployment -and $summary.gpoId -ne '') {
        try { Gpo-Action $summary.gpoId 'Unlink';$summary.gpoUnlinked=$true }
        catch { $summary.warnings+=('Unlink failed; MANUAL ACTION REQUIRED: '+$_.Exception.Message) }
        if ($summary.gpoUnlinked) {
            # Allow inter-DC GPO link replication before client gpupdate/RSoP audit.
            Start-Sleep -Seconds 90
            foreach($pc in $Names) {
                if (-not $remotePaths.ContainsKey($pc)) { $allClean=$false;continue }
                try {
                    $target=Join-Path $remotePaths[$pc] 'cleanup.json'
                    if (Test-Path -LiteralPath $target) { throw 'Stale cleanup result.' }
                    $cleanup=Run-Probe $pc 'Cleanup' '01.jpg' 120
                    if (-not [bool]$cleanup.passed) {
                        throw ([string]$cleanup.error)
                    }
                    $summary.clientChecks+= [pscustomobject]@{
                        name=$pc;stage='Client cleanup';passed=$true
                        restored=$cleanup.originalRestored;gpoAbsent=$cleanup.gpoRemoved
                        completionMarker=$cleanup.completed;filesRemoved=$cleanup.sourceRemoved
                    }
                } catch {
                    $allClean=$false
                    $summary.warnings+=('CLIENT CLEANUP FAILED '+$pc+': '+$_.Exception.Message)
                }
            }
            $summary.clientCleanupVerified=$allClean
            if ($allClean) {
                try {
                    Gpo-Action $summary.gpoId 'Delete'
                    $summary.gpoDeleted=$true
                    Remove-LabSource $summary.gpoId
                    $summary.sourceDeleted=$true
                } catch {
                    $summary.warnings+=('Exact GPO/source cleanup incomplete; inspect test GPO and SYSVOL: '+$_.Exception.Message)
                }
            }
        }
    }
    foreach($pc in $Names) {
        try {
            Remove-RemoteTask $pc ('LSCM-CI-'+$Id+'-reboot')
        } catch {
            $summary.warnings+=('Remote reboot task cleanup incomplete on '+$pc)
        }
        if ($remotePaths.ContainsKey($pc) -and
            ($Mode -eq 'Preflight' -or $summary.clientCleanupVerified)) {
            try { Remove-Item -LiteralPath $remotePaths[$pc] -Recurse -Force -ErrorAction Stop }
            catch { $summary.warnings+=('Lab probe directory cleanup incomplete: '+$pc) }
        }
    }
    if ($Mode -eq 'Full' -and (-not $summary.gpoDeleted -or
        -not $summary.sourceDeleted -or -not $summary.clientCleanupVerified)) {
        $summary.passed=$false
    }
    Store-Report
    try { Remove-Item -LiteralPath $Work -Recurse -Force -ErrorAction Stop }
    catch { $summary.warnings+=('Controller scratch deletion failed.') }
}
if (-not $summary.passed) { throw ('Live AD Lab did not pass. Inspect live-ad-lab.json. '+$summary.error) }
Write-Output ('PASS: '+$Mode+' on '+$Names.Count+' Windows 11 lab client(s).')
