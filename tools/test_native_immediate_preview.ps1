#requires -Version 5.1
# Cross-engine read-only Immediate Actions preview hash compatibility test.
# Extracts the original PowerShell SHA-256 preimage expression from the
# production Campaign-PushNow.ps1 AST; never invokes AD, SYSVOL or RPC.
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot '..\src\LockScreenCampaignManager\Assets\Campaign-PushNow.ps1'
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $path, [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) { throw 'Legacy Immediate Actions script has syntax errors.' }
$assignments = @($ast.FindAll({
    param($n)
    $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and
    $n.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
    $n.Left.VariablePath.UserPath -eq 'hashInput'
}, $true))
if ($assignments.Count -ne 1) {
    throw 'Legacy Immediate Actions plan hash preimage is missing or ambiguous.'
}
$info = [pscustomobject]@{
    dns='example.com'
    dc='dc01.example.com'
}
$ouDn = 'OU=Clients,DC=example,DC=com'
$groupDn = 'CN=CampaignGroup,OU=Groups,DC=example,DC=com'
$guid = [guid]'01234567-89ab-cdef-0123-456789abcdef'
$managed = [pscustomobject]@{ id='campaign-a' }
$purpose = 'Repair'
$manifestHash = 'A' * 64
$selected = @('pc02.example.com', 'pc01.example.com')
. ([scriptblock]::Create($assignments[0].Extent.Text))
$sha256 = [Security.Cryptography.SHA256]::Create()
try {
    $hash = [BitConverter]::ToString(
        $sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($hashInput))
    ).Replace('-', '').ToLowerInvariant()
} finally { $sha256.Dispose() }
$expected = 'c33eacacdffb9233024c9edeb49a952c1b9c3c57fedb4067c6b7cf88f0c8781e'
if ($hash -cne $expected) {
    throw "Native C# / legacy PowerShell preview hash changed: $hash"
}
if (-not $hashInput.EndsWith('|pc01.example.com,pc02.example.com',
        [StringComparison]::Ordinal)) {
    throw 'Legacy immediate action sort order no longer matches native C#.'
}
Write-Host 'Native Immediate Actions Preview -> legacy Task Scheduler Apply plan: PASS'
