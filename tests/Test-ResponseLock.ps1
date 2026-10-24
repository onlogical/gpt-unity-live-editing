param([Parameter(Mandatory=$true)][string]$ProjectPath)
$ErrorActionPreference='Stop'
$client=Join-Path $PSScriptRoot '../Send-UnityCommand.ps1'
$bridgeRoot=Join-Path $ProjectPath '.codex-bridge'
if(Test-Path (Join-Path $bridgeRoot 'request.json')) { throw 'Another command is pending.' }
$null = & $client -ProjectPath $ProjectPath -Action ping
$lockId=[guid]::NewGuid().ToString()
$testName='Bridge Lock Regression '+$lockId
$responseFolder=Join-Path $bridgeRoot 'responses'
New-Item -ItemType Directory -Force $responseFolder | Out-Null
$replyPath=Join-Path $responseFolder ($lockId+'.json')
$lockStream=[IO.File]::Open(($replyPath+'.tmp'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try {
    $request=@{id=$lockId;action='create_empty';name=$testName} | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $bridgeRoot 'lock-test.tmp'),$request)
    Move-Item -LiteralPath (Join-Path $bridgeRoot 'lock-test.tmp') -Destination (Join-Path $bridgeRoot ('queue/0000000000000000-'+$lockId+'.json'))
    Start-Sleep -Milliseconds 1200
} finally { $lockStream.Dispose() }
$deadline=[DateTime]::UtcNow.AddSeconds(10)
do { Start-Sleep -Milliseconds 200; $reply=if(Test-Path -LiteralPath $replyPath) { Get-Content -Raw $replyPath | ConvertFrom-Json } else { $null } } while($reply.id -ne $lockId -and [DateTime]::UtcNow -lt $deadline)
if($reply.id -ne $lockId -or !$reply.ok) { throw 'Locked response retry failed.' }
try {
    $hierarchy=(& $client -ProjectPath $ProjectPath -Action hierarchy) | ConvertFrom-Json
    if(@($hierarchy.objects | Where-Object name -eq $testName).Count -ne 1) { throw 'Locked response repeated the mutation.' }
    Write-Output 'PASS: Locked response retries successfully without duplicating object creation.'
} finally {
    & $client -ProjectPath $ProjectPath -Action delete -ArgumentsJson (@{instanceId=$reply.instanceId} | ConvertTo-Json) | Out-Null
}
