#requires -Version 5.1
# Read-only SYSVOL GPO accessibility observation from an approved LOCAL SYSTEM
# Windows 10/11 lab session. This file never runs a GPO, SCR, EXE or PS1
# from SYSVOL. No AD, registry, domain, share or policy changes are performed.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ConfigPath,
    [Parameter(Mandatory=$true)][string]$ReportPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

function Assert-LocalFile([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path) -or $path.Length -gt 1024 -or
        -not [IO.Path]::IsPathRooted($path) -or $path.StartsWith('\\') -or
        [IO.Path]::GetFullPath($path).StartsWith('\\')) {
        throw 'Only protected local configuration/report paths are allowed.'
    }
    $full=[IO.Path]::GetFullPath($path)
    $parent=[IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($full))
    if (-not $parent.Exists) { throw 'Local probe directory is absent.' }
    for($node=$parent;$null -ne $node;$node=$node.Parent) {
        if (($node.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Linked local probe directory is forbidden.'
        }
    }
    return $full
}
function Read-Source([string]$path,[int64]$maxBytes) {
    try {
        $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
        if ($item -isnot [IO.FileInfo] -or
            ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            $item.Length -lt 1 -or $item.Length -gt $maxBytes) {
            return 'Unsafe'
        }
        $stream=[IO.File]::Open($path,[IO.FileMode]::Open,
            [IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
        try {
            if ($stream.ReadByte() -lt 0) { return 'Unsafe' }
            return 'Read'
        } finally { $stream.Dispose() }
    } catch [UnauthorizedAccessException] { return 'Denied' }
      catch [Security.SecurityException] { return 'Denied' }
      catch [Management.Automation.ItemNotFoundException] { return 'Missing' }
      catch [IO.FileNotFoundException] { return 'Missing' }
      catch [IO.DirectoryNotFoundException] { return 'Missing' }
      catch [IO.IOException] { return 'Unavailable' }
      catch { return 'Unavailable' }
}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if ($null -eq $identity -or $identity.User.Value -ne 'S-1-5-18') {
    throw 'LOCAL_SYSTEM_REQUIRED: no report was written.'
}
$config=Assert-LocalFile $ConfigPath
$destination=Assert-LocalFile $ReportPath
if ((Get-Item -LiteralPath $config -Force).Length -gt 8192 -or
    (Test-Path -LiteralPath $destination)) {
    throw 'Oversized configuration or existing report.'
}
$raw=Get-Content -LiteralPath $config -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
$domain=[string]$raw.domain
$dc=[string]$raw.dc
$computer=[string]$raw.computer
$id=[string]$raw.gpoId
[Guid]$guid=[Guid]::Empty
if ([string]$raw.schema -cne 'lscm-sysvol-system-config/v1' -or
    $domain -cnotmatch '^(?=.{4,253}$)[a-zA-Z0-9-]+(\.[a-zA-Z0-9-]+)+$' -or
    $dc -cnotmatch '^(?=.{4,253}$)[a-zA-Z0-9-]+(\.[a-zA-Z0-9-]+)+$' -or
    -not $dc.EndsWith('.'+$domain,[StringComparison]::OrdinalIgnoreCase) -or
    $computer -ine ($env:COMPUTERNAME+'.'+$domain) -or
    -not [Guid]::TryParseExact($id,'D',[ref]$guid)) {
    throw 'Invalid exact lab domain/DC/computer/GPO identity.'
}
$os=Get-WmiObject Win32_OperatingSystem -ErrorAction Stop
$pc=Get-WmiObject Win32_ComputerSystem -ErrorAction Stop
if ([int]$os.ProductType -ne 1 -or [int]$os.BuildNumber -lt 10240 -or
    -not $pc.PartOfDomain -or [string]$pc.Domain -ine $domain) {
    throw 'Only the approved domain-joined Windows client is supported.'
}
$base='\\'+$dc+'\SYSVOL\'+$domain+'\Policies\{'+
    $guid.ToString('D').ToUpperInvariant()+'}'
# A UNC path can be redirected by DFS; this probe proves only that this
# exact local SYSTEM process could read bytes through the requested path.
$gpt=Read-Source ($base+'\GPT.INI') 65536
$userPol=Read-Source ($base+'\User\Registry.pol') 8388608
$machinePol=Read-Source ($base+'\Machine\Registry.pol') 8388608
$result=if($gpt -eq 'Read'){'GPT_READ'}else{'GPT_NOT_READ'}
$report=[ordered]@{
    schema='lscm-sysvol-system/v1'
    observedUtc=[datetime]::UtcNow.ToString('o')
    domain=$domain.ToLowerInvariant()
    dc=$dc.ToLowerInvariant()
    computer=$computer.ToLowerInvariant()
    gpoId=$guid.ToString('D')
    localSystem=$true
    gptIni=$gpt
    userRegistryPol=$userPol
    machineRegistryPol=$machinePol
    result=$result
}
# Report is private: it contains domain names and the exact GPO identifier.
# The protected local parent directory is created by the explicit harness.
$json=$report | ConvertTo-Json -Depth 3 -Compress
$bytes=[Text.UTF8Encoding]::new($false).GetBytes($json)
$writer=[IO.FileStream]::new($destination,[IO.FileMode]::CreateNew,
    [IO.FileAccess]::Write,[IO.FileShare]::None)
try { $writer.Write($bytes,0,$bytes.Length);$writer.Flush($true) }
finally { $writer.Dispose() }
