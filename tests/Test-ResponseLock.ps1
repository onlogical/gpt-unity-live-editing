param([Parameter(Mandatory=$true)][string]$ProjectPath)
$ErrorActionPreference='Stop'
$client=Join-Path $PSScriptRoot '../Send-UnityCommand.ps1'
$bridgeRoot=Join-Path $ProjectPath '.codex-bridge'
if(Test-Path (Join-Path $bridgeRoot 'request.json')) { throw 'Another command is pending.' }
$null = & $client -ProjectPath $ProjectPath -Action ping
$lockId=[guid]::NewGuid().ToString()
$testName='Bridge Lock Regression '+$lockId
$lockStream=[IO.File]::Open((Join-Path $bridgeRoot 'response.json'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try {
    $request=@{id=$lockId;action='create_empty';name=$testName} | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $bridgeRoot 'lock-test.tmp'),$request)
    Move-Item -LiteralPath (Join-Path $bridgeRoot 'lock-test.tmp') -Destination (Join-Path $bridgeRoot 'request.json')
    Start-Sleep -Milliseconds 1200
} finally { $lockStream.Dispose() }
$deadline=[DateTime]::UtcNow.AddSeconds(10)
do { Start-Sleep -Milliseconds 200; $reply=Get-Content -Raw (Join-Path $bridgeRoot 'response.json') | ConvertFrom-Json } while($reply.id -ne $lockId -and [DateTime]::UtcNow -lt $deadline)
if($reply.id -ne $lockId -or !$reply.ok) { throw 'Locked response retry failed.' }
try {
    $hierarchy=(& $client -ProjectPath $ProjectPath -Action hierarchy) | ConvertFrom-Json
    if(@($hierarchy.objects | Where-Object name -eq $testName).Count -ne 1) { throw 'Locked response repeated the mutation.' }
    Write-Output 'PASS: Locked response retries successfully without duplicating object creation.'
} finally {
    & $client -ProjectPath $ProjectPath -Action delete -ArgumentsJson (@{instanceId=$reply.instanceId} | ConvertTo-Json) | Out-Null
}
