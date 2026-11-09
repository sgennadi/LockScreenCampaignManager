# JPG Screen Saver computer GPO Startup - isolated LAB pilot only.
# Windows PowerShell 5.1. No AD, registry, GPO or remote writes.
# This script is generated from a pinned SHA256-verified photo package.
[CmdletBinding()]
param([ValidateSet('Install','Verify')][string]$Mode = 'Verify')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$campaignId = '__LSCM_CAMPAIGN_ID__'
$sourceFolder = '__LSCM_SOURCE_FOLDER__'
$manifestHash = '__LSCM_MANIFEST_SHA256__'
$scrHash = '__LSCM_SCR_SHA256__'
$slideSeconds = __LSCM_SLIDE_SECONDS__
$installRoot = 'C:\ProgramData\LSCM-PhotoSaver'
$installPath = Join-Path $installRoot $campaignId
$sourcePath = Join-Path $PSScriptRoot $sourceFolder

function Assert-Directory([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($item -isnot [System.IO.DirectoryInfo] -or
        (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw 'JPG Screen Saver directory must be regular, not a junction or symlink.'
    }
}

function Assert-File([string]$Path, [string]$Hash) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($item -isnot [System.IO.FileInfo] -or
        (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) -or
        $item.Length -lt 1 -or $item.Length -gt 250MB) {
        throw 'JPG Screen Saver file is missing, oversized, or linked.'
    }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ine $Hash) {
        throw 'JPG Screen Saver SHA256 mismatch: refuse to install or activate.'
    }
}

function Assert-Package([string]$Folder) {
    Assert-Directory $Folder
    $imagesFolder = Join-Path $Folder 'images'
    Assert-Directory $imagesFolder
    Assert-File (Join-Path $Folder 'slideshow.json') $manifestHash
    Assert-File (Join-Path $Folder 'PhotoScreenSaver.scr') $scrHash
    $manifest = Get-Content -LiteralPath (Join-Path $Folder 'slideshow.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.schema -cne 'lscm-photo-saver/v1' -or
        [int]$manifest.intervalSeconds -ne $slideSeconds -or
        $manifest.executableSha256 -ine $scrHash) {
        throw 'Wrong JPG Screen Saver manifest schema, interval or binary.'
    }
    $images = @($manifest.images)
    if ($images.Count -lt 1 -or $images.Count -gt 100) {
        throw 'Invalid JPG image count.'
    }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($image in $images) {
        $name = [string]$image.name
        $hash = [string]$image.sha256
        if ($name -cnotmatch '^[0-9]{2,3}\.jpg$' -or
            $hash -notmatch '^[a-fA-F0-9]{64}$' -or
            -not $seen.Add($name)) {
            throw 'Unsafe or duplicate JPG image name/hash.'
        }
        Assert-File (Join-Path $imagesFolder $name) $hash
    }
    if (@(Get-ChildItem -LiteralPath $imagesFolder -Force).Count -ne $images.Count) {
        throw 'Unexpected file in JPG screen saver images directory.'
    }
    return $images
}

function Set-ProtectedPilotAcl([string]$Path) {
    $acl = New-Object System.Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $inheritance = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
    $permissionList = @(
        @('S-1-5-18', [System.Security.AccessControl.FileSystemRights]::FullControl),
        @('S-1-5-32-544', [System.Security.AccessControl.FileSystemRights]::FullControl),
        @('S-1-5-32-545', [System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
    )
    foreach ($permission in $permissionList) {
        $sid = New-Object System.Security.Principal.SecurityIdentifier($permission[0])
        $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            $sid, $permission[1], $inheritance,
            [System.Security.AccessControl.PropagationFlags]::None,
            [System.Security.AccessControl.AccessControlType]::Allow)
        [void]$acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl -ErrorAction Stop
}

function Assert-InstalledAcl([string]$Path) {
    Assert-Directory $Path
    $acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
    if (-not $acl.AreAccessRulesProtected) {
        throw 'JPG Screen Saver directory inherits unapproved permissions.'
    }
    $allowed = @('S-1-5-18', 'S-1-5-32-544', 'S-1-5-32-545')
    foreach ($rule in $acl.GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -ne [System.Security.AccessControl.AccessControlType]::Allow -or
            $rule.IdentityReference.Value -notin $allowed) {
            throw 'Unexpected screen saver directory ACL trustee.'
        }
        if ($rule.IdentityReference.Value -eq 'S-1-5-32-545' -and
            (($rule.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::Write) -ne 0)) {
            throw 'Users may not modify installed JPG Screen Saver files.'
        }
    }
}

if ($campaignId -cnotmatch '^[a-z0-9-]{5,75}$' -or
    $sourceFolder -cnotmatch '^JPG-ScreenSaver-[a-z0-9-]{5,75}$' -or
    $manifestHash -notmatch '^[a-fA-F0-9]{64}$' -or
    $scrHash -notmatch '^[a-fA-F0-9]{64}$' -or
    $slideSeconds -lt 5 -or $slideSeconds -gt 3600) {
    throw 'Invalid generated JPG GPO pilot identity.'
}
if ($env:ProgramData -ine 'C:\ProgramData') {
    throw 'This lab kit only supports the explicit C:\ProgramData destination.'
}
if ($Mode -eq 'Verify') {
    Assert-InstalledAcl $installRoot
    Assert-InstalledAcl $installPath
    [void](Assert-Package $installPath)
    Write-Output 'VERIFIED: installed JPG Screen Saver package.'
    exit 0
}
if ([System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne 'S-1-5-18') {
    throw 'Startup installation is permitted under LocalSystem only.'
}
[void](Assert-Package $sourcePath)
$staging = ''
try {
    if (-not (Test-Path -LiteralPath $installRoot -PathType Container)) {
        New-Item -Path $installRoot -ItemType Directory -ErrorAction Stop | Out-Null
    }
    Assert-Directory $installRoot
    Set-ProtectedPilotAcl $installRoot
    Assert-InstalledAcl $installRoot
    if (Test-Path -LiteralPath $installPath) {
        Assert-InstalledAcl $installPath
        [void](Assert-Package $installPath)
        Write-Output 'VERIFIED: previously installed package is unchanged.'
        exit 0
    }
    $staging = Join-Path $installRoot ('.staging-' + [Guid]::NewGuid().ToString('N'))
    New-Item -Path $staging -ItemType Directory -ErrorAction Stop | Out-Null
    Set-ProtectedPilotAcl $staging
    New-Item -Path (Join-Path $staging 'images') -ItemType Directory -ErrorAction Stop | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourcePath 'PhotoScreenSaver.scr') -Destination $staging -ErrorAction Stop
    Copy-Item -LiteralPath (Join-Path $sourcePath 'slideshow.json') -Destination $staging -ErrorAction Stop
    $manifest = Get-Content -LiteralPath (Join-Path $sourcePath 'slideshow.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($image in $manifest.images) {
        Copy-Item -LiteralPath (Join-Path (Join-Path $sourcePath 'images') $image.name) -Destination (Join-Path $staging 'images') -ErrorAction Stop
    }
    [void](Assert-Package $staging)
    if (Test-Path -LiteralPath $installPath) {
        throw 'Competing JPG Screen Saver installation appeared; no overwrite.'
    }
    Move-Item -LiteralPath $staging -Destination $installPath -ErrorAction Stop
    $staging = ''
    Assert-InstalledAcl $installPath
    [void](Assert-Package $installPath)
    Write-Output 'INSTALLED: verified local files; no User GPO or registry was modified.'
}
finally {
    if ($staging -and (Test-Path -LiteralPath $staging)) {
        Assert-Directory $staging
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction Stop
    }
}
