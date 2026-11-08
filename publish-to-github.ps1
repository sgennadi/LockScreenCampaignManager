# Publishes source changes only, as a new branch; never overwrites main.
# Requires Git for Windows with interactive GitHub sign-in (Git Credential Manager).
# Does not store any GitHub token or password in this project.
# GitHub repository is private. This script runs on the administrator's computer.
[CmdletBinding()]
param(
    [string]$Repository = 'https://github.com/sgennadi/LockScreenCampaignManager.git',
    [string]$Branch = 'feature/client-feedback-0.4.0',
    [switch]$KeepCheckout
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) {
    throw 'Git for Windows not found. Install Git or use an existing Visual Studio Git installation.'
}
if ($Branch -notmatch '^feature/[a-z0-9_-]+(?:[./-][a-z0-9_-]+)*$') {
    throw 'Invalid safe feature branch name.'
}
$source = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifest = Join-Path $source 'GITHUB_OVERLAY_FILES.txt'
if (-not (Test-Path -LiteralPath $manifest)) { throw 'Overlay file manifest missing.' }
$paths = @(Get-Content -LiteralPath $manifest | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
foreach ($rel in $paths) {
    if ($rel.StartsWith('/') -or $rel.Contains('..') -or $rel.StartsWith('.git/')) {
        throw 'Unsafe path in overlay manifest.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $source $rel) -PathType Leaf)) {
        throw ('Project file missing: ' + $rel)
    }
}
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('LSCM-Git-' + [guid]::NewGuid().ToString('N'))
try {
    & git.exe clone --quiet --single-branch --branch main $Repository $temporary
    if ($LASTEXITCODE -ne 0) { throw 'GitHub clone failed. Check sign-in/permissions.' }
    & git.exe -C $temporary switch -c $Branch
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create a clean feature branch.' }
    foreach ($relative in $paths) {
        $dest = Join-Path $temporary $relative
        $dir = Split-Path -Parent $dest
        if (-not (Test-Path -LiteralPath $dir)) { New-Item -Path $dir -ItemType Directory -Force | Out-Null }
        Copy-Item -LiteralPath (Join-Path $source $relative) -Destination $dest -Force
        & git.exe -C $temporary add -- $relative
        if ($LASTEXITCODE -ne 0) { throw ('Cannot stage file: ' + $relative) }
    }
    & git.exe -C $temporary diff --cached --check
    if ($LASTEXITCODE -ne 0) { throw 'Git diff validation failed.' }
    & git.exe -C $temporary diff --cached --quiet
    if ($LASTEXITCODE -eq 0) { throw 'No source changes compared to main.' }
    & git.exe -C $temporary commit -m 'Merge GPO lifecycle, Client Integrity and live SMB feedback (0.4.0-preview.1)'
    if ($LASTEXITCODE -ne 0) { throw 'Git commit failed. Configure user.name and user.email.' }
    & git.exe -C $temporary push --set-upstream origin $Branch
    if ($LASTEXITCODE -ne 0) { throw 'GitHub push failed. Check branch rights and connection.' }
    Write-Host 'Uploaded to branch:' $Branch -ForegroundColor Green
    Write-Host 'Open compare / Pull Request:' -ForegroundColor Cyan
    Write-Host 'https://github.com/sgennadi/LockScreenCampaignManager/compare/main...feature/client-feedback-0.4.0?expand=1'
    Write-Host 'GitHub Actions will build x64/x86 EXE when the PR is opened.'
}
finally {
    if (-not $KeepCheckout -and (Test-Path -LiteralPath $temporary)) {
        Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
    }
    elseif ($KeepCheckout) { Write-Host ('Checkout retained: '+$temporary) }
}
