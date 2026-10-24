param(
    [Parameter(Mandatory=$true)][string]$Action,
    [Parameter(Mandatory=$true)][string]$ProjectPath,
    [string]$ArgumentsJson = '{}',
    [int]$TimeoutSeconds = 30
)
$ErrorActionPreference = 'Stop'
$bridgeRoot = Join-Path $ProjectPath '.codex-bridge'
if (!(Test-Path (Join-Path $bridgeRoot 'status.json'))) { throw 'Unity bridge is not running. Open Unity and refresh Assets.' }
$heartbeat = Get-Content -Raw -LiteralPath (Join-Path $bridgeRoot 'status.json') | ConvertFrom-Json
$heartbeatTime = if($heartbeat.timestamp -is [DateTime]) { [DateTimeOffset]$heartbeat.timestamp } else { [DateTimeOffset]::Parse($heartbeat.timestamp,[Globalization.CultureInfo]::InvariantCulture) }
if ([DateTimeOffset]::UtcNow.Subtract($heartbeatTime).TotalSeconds -gt 30) {
    throw 'Unity bridge heartbeat is stale. Finish package installation or compilation and bring Unity forward before sending commands.'
}
$requestPath = Join-Path $bridgeRoot 'request.json'
if (Test-Path $requestPath) { throw 'A command is already pending.' }
$payload = ConvertFrom-Json $ArgumentsJson
$commandId = [guid]::NewGuid().ToString()
$payload | Add-Member -NotePropertyName id -NotePropertyValue $commandId -Force
$payload | Add-Member -NotePropertyName action -NotePropertyValue $Action -Force
$temporaryPath = Join-Path $bridgeRoot ($commandId + '.tmp')
[IO.File]::WriteAllText($temporaryPath, ($payload | ConvertTo-Json -Depth 12))
Move-Item -LiteralPath $temporaryPath -Destination $requestPath
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while ([DateTime]::UtcNow -lt $deadline) {
    $replyPath = Join-Path $bridgeRoot 'response.json'
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
            $reply | ConvertTo-Json -Depth 12
            return
        }
    }
    Start-Sleep -Milliseconds 150
}
throw "Unity did not acknowledge command $commandId within $TimeoutSeconds seconds. Check for dialogs or compilation errors; command may still be pending."

