#requires -Version 5.1
# Read-only, locale-independent GPRESULT XML parser.
# Loaded only from the protected LOCAL canary folder. Does not access AD.
Set-StrictMode -Version Latest

function Get-LscmComputerGpoEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][xml]$Report,
        [Parameter(Mandatory=$true)][Guid]$ExpectedGpoId
    )
    $result=[ordered]@{
        applied=$false
        matched=$false
        status='RsopUnavailable'
    }
    if ($null -eq $Report.DocumentElement -or
        $Report.DocumentElement.LocalName -cne 'Rsop') {
        return [pscustomobject]$result
    }
    # ONLY computer-level RSoP GPO nodes. NEVER search arbitrary XML text,
    # user policy entries or GPO display names (which can contain fake GUIDs).
    $computer=$Report.SelectSingleNode(
        "/*[local-name()='Rsop']/*[local-name()='ComputerResults']")
    if ($null -eq $computer) { return [pscustomobject]$result }
    $result.status='NotApplied'
    $gpos=@($computer.SelectNodes("./*[local-name()='GPO']"))
    $id=[regex]::Escape($ExpectedGpoId.ToString('D'))
    $pattern='(?i)(?:CN=\{?' + $id + '\}?)(?:,|$)'
    $matches=New-Object 'System.Collections.Generic.List[object]'
    foreach($gpo in $gpos) {
        $pathNode=$gpo.SelectSingleNode("./*[local-name()='Path']")
        if ($null -eq $pathNode) { continue }
        $path=[string]$pathNode.InnerText
        # Match only an actual LDAP CN=<GUID> segment inside a Policies
        # object path, never arbitrary mentions in any setting or GPO name.
        if ($path -notmatch '(?i)(?:^|[/,])CN=\{?[0-9a-f-]{36}\}?,CN=Policies,') {
            continue
        }
        if ($path -match $pattern) { [void]$matches.Add($gpo) }
    }
    if ($matches.Count -eq 0) { return [pscustomobject]$result }
    $result.matched=$true
    if ($matches.Count -ne 1) {
        $result.status='AmbiguousGpoIdentity'
        return [pscustomobject]$result
    }
    $item=$matches[0]
    $flags=@{}
    foreach ($name in @('Enabled','IsValid','FilterAllowed','AccessDenied')) {
        $node=$item.SelectSingleNode("./*[local-name()='"+$name+"']")
        if ($null -eq $node -or
            [string]$node.InnerText -notmatch '^(?i:true|false)$') {
            $result.status='IncompleteGpoEvidence'
            return [pscustomobject]$result
        }
        $flags[$name]=[string]$node.InnerText -ieq 'true'
    }
    if (-not $flags['Enabled'] -or -not $flags['IsValid'] -or
        -not $flags['FilterAllowed'] -or $flags['AccessDenied']) {
        $result.status='GpoFilteredOrDisabled'
        return [pscustomobject]$result
    }
    $result.applied=$true
    $result.status='AppliedComputerGpo'
    return [pscustomobject]$result
}
