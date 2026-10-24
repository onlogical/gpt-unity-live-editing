param(
    [Parameter(Mandatory=$true)][string]$Action,
    [Parameter(Mandatory=$true)][string]$ProjectPath,
    [string]$ArgumentsJson = '{}',
    [int]$TimeoutSeconds = 45,
    [switch]$NoWait,
    [string]$RequestId
)
$ErrorActionPreference = 'Stop'
function Read-BridgeJson([string]$Path) {
    for($attempt=0;$attempt -lt 10;$attempt++) {
        try {
            $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            try { $reader=[IO.StreamReader]::new($stream); try { return ($reader.ReadToEnd() | ConvertFrom-Json) } finally { $reader.Dispose() } } finally { $stream.Dispose() }
        } catch [IO.IOException] { if($attempt -eq 9) { throw }; Start-Sleep -Milliseconds 25 }
    }
}
$bridgeRoot = Join-Path $ProjectPath '.codex-bridge'
if($Action -eq 'result' -or $Action -eq 'cancel') {
    if($RequestId -notmatch '^[A-Za-z0-9_-]{1,100}$') { throw 'Provide a valid -RequestId.' }
    $resultPath=Join-Path $bridgeRoot ('responses/'+$RequestId+'.json')
    if(Test-Path -LiteralPath $resultPath) { Read-BridgeJson $resultPath | ConvertTo-Json -Depth 25; return }
    if($Action -eq 'cancel') {
        $cancelFolder=Join-Path $bridgeRoot 'cancel'; New-Item -ItemType Directory -Force $cancelFolder | Out-Null
        $cancelPath=Join-Path $cancelFolder ($RequestId+'.json')
        [IO.File]::WriteAllText($cancelPath,(@{id=$RequestId} | ConvertTo-Json))
        @{id=$RequestId;outcome='cancellation_requested'} | ConvertTo-Json; return
    }
    @{id=$RequestId;outcome='pending'} | ConvertTo-Json; return
}
if (!(Test-Path (Join-Path $bridgeRoot 'status.json'))) { throw 'Unity bridge is not running. Open Unity and refresh Assets.' }
$heartbeat = Read-BridgeJson (Join-Path $bridgeRoot 'status.json')
$heartbeatTime = if($heartbeat.timestamp -is [DateTime]) { [DateTimeOffset]$heartbeat.timestamp } else { [DateTimeOffset]::Parse($heartbeat.timestamp,[Globalization.CultureInfo]::InvariantCulture) }
if ([DateTimeOffset]::UtcNow.Subtract($heartbeatTime).TotalSeconds -gt 30) {
    throw 'Unity bridge heartbeat is stale. Finish package installation or compilation and bring Unity forward before sending commands.'
}
if($Action -eq 'status') { $heartbeat | ConvertTo-Json -Depth 20; return }
if(!$heartbeat.protocolVersion -or $heartbeat.protocolVersion -lt 2) { throw 'Update the Unity bridge to 0.3.0 or later for queued commands.' }
$queueFolder=Join-Path $bridgeRoot 'queue'; New-Item -ItemType Directory -Force $queueFolder | Out-Null
$payload = ConvertFrom-Json $ArgumentsJson
$commandId = [guid]::NewGuid().ToString()
$requestPath = Join-Path $queueFolder (([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds().ToString().PadLeft(16,'0'))+'-'+$commandId+'.json')
$payload | Add-Member -NotePropertyName id -NotePropertyValue $commandId -Force
$payload | Add-Member -NotePropertyName action -NotePropertyValue $Action -Force
$temporaryPath = Join-Path $bridgeRoot ($commandId + '.tmp')
[IO.File]::WriteAllText($temporaryPath, ($payload | ConvertTo-Json -Depth 12))
Move-Item -LiteralPath $temporaryPath -Destination $requestPath
if($NoWait) { @{id=$commandId;outcome='queued'} | ConvertTo-Json; return }
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while ([DateTime]::UtcNow -lt $deadline) {
    $replyPath = Join-Path $bridgeRoot ('responses/'+$commandId+'.json')
    if (Test-Path $replyPath) {
        try {
            $replyStream = [IO.File]::Open($replyPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            try {
                $replyReader = [IO.StreamReader]::new($replyStream)
                try { $reply = $replyReader.ReadToEnd() | ConvertFrom-Json } finally { $replyReader.Dispose() }
            } finally { $replyStream.Dispose() }
        } catch { Start-Sleep -Milliseconds 100; continue }
        if ($reply.id -eq $commandId) {
            if (!$reply.ok) { throw $reply.error }
            $reply | ConvertTo-Json -Depth 25
            return
        }
    }
    Start-Sleep -Milliseconds 150
}
$cancelFolder=Join-Path $bridgeRoot 'cancel'; New-Item -ItemType Directory -Force $cancelFolder | Out-Null
[IO.File]::WriteAllText((Join-Path $cancelFolder ($commandId+'.json')),(@{id=$commandId} | ConvertTo-Json))
throw "Unity request $commandId timed out. Cancellation was requested. Inspect its result before retrying; a running single edit may already have completed."

