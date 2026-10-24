$ErrorActionPreference='Stop'
$client=Join-Path $PSScriptRoot '../Send-UnityCommand.ps1'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('unity-bridge-client-'+[guid]::NewGuid().ToString('N'))
$stateFolder=Join-Path $fixture '.codex-bridge'
New-Item -ItemType Directory -Force $stateFolder | Out-Null
try {
    @{timestamp=[DateTime]::UtcNow.AddMinutes(-2).ToString('o')} | ConvertTo-Json | Set-Content (Join-Path $stateFolder 'status.json')
    $rejected=$false
    try { & $client -ProjectPath $fixture -Action ping -TimeoutSeconds 1 | Out-Null }
    catch { if($_ -match 'heartbeat is stale') { $rejected=$true } else { throw } }
    if(!$rejected -or (Test-Path (Join-Path $stateFolder 'request.json'))) { throw 'Stale heartbeat was not rejected before writing a command.' }
    Write-Output 'PASS: Stale bridge rejected before sending any command (including UTC timestamp deserialization).'
} finally {
    Remove-Item -LiteralPath (Join-Path $stateFolder 'status.json') -Force
    [IO.Directory]::Delete($stateFolder)
    [IO.Directory]::Delete($fixture)
}
