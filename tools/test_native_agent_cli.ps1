param(
    [Parameter(Mandatory = $true)]
    [string]$PublishedExe
)
$ErrorActionPreference = 'Stop'
$dir = Join-Path ([IO.Path]::GetTempPath()) ('LSCM-Agent-Status-' + [guid]::NewGuid().ToString('N'))
New-Item -Path $dir -ItemType Directory | Out-Null
try {
    $exe = Join-Path $dir 'LockScreen-Agent.exe'
    Copy-Item -LiteralPath $PublishedExe -Destination $exe -ErrorAction Stop
    $sha = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    $id = 'native-ci-' + [guid]::NewGuid().ToString('N').Substring(0, 16)
    $at = [datetime]::UtcNow.AddDays(-2).ToString('yyyy-MM-ddTHH:mm:ss')
    $end = [datetime]::UtcNow.AddDays(2).ToString('yyyy-MM-ddTHH:mm:ss')
    $manifest = [ordered]@{
        schemaVersion = 1
        campaignId = $id
        name = 'CI native client read-only health'
        clientType = 'Executable'
        agentSha256 = $sha
        timeZoneId = 'UTC'
        endLocal = $end
        randomizePerDevice = $false
        slides = @([ordered]@{
            atLocal = $at
            fileName = '01.jpg'
            sha256 = ('0' * 64)
        })
    }
    $json = Join-Path $dir 'campaign.json'
    $manifest | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $json -Encoding utf8
    $valid = Start-Process -FilePath $exe -ArgumentList '--client-agent Status' -Wait -PassThru
    if ($valid.ExitCode -ne 0) {
        throw "Read-only native EXE Status failed (exit $($valid.ExitCode))."
    }
    $manifest.agentSha256 = 'f' * 64
    $manifest | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $json -Encoding utf8
    $tampered = Start-Process -FilePath $exe -ArgumentList '--client-agent Status' -Wait -PassThru
    if ($tampered.ExitCode -ne 1) {
        throw "Native EXE accepted a tampered immutable agent hash (exit $($tampered.ExitCode))."
    }
    Write-Host 'PASS: native EXE headless Status works and rejects a mismatched binary SHA-256.'
}
finally {
    Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
}
