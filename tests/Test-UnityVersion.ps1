param(
    [Parameter(Mandatory=$true)][string]$UnityExe,
    [Parameter(Mandatory=$true)][string]$Version,
    [int]$TimeoutSeconds=240
)
$ErrorActionPreference='Stop'
if($Version -notmatch '^6000\.[0-9]+\.[0-9]+[a-z][0-9]+$') { throw 'Supply a Unity 6 editor version.' }
$project=Join-Path ([IO.Path]::GetTempPath()) ('unity-bridge-compat-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $project 'Assets/Editor'),(Join-Path $project 'Packages'),(Join-Path $project 'ProjectSettings') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'CompatibilitySmoke.cs') (Join-Path $project 'Assets/Editor/CompatibilitySmoke.cs')
$package=(Resolve-Path (Join-Path $PSScriptRoot '../UnityBridge')).Path.Replace('\','/')
@{dependencies=@{'com.community.unity-live-bridge'=('file:'+$package);'com.unity.modules.imgui'='1.0.0';'com.unity.modules.physics'='1.0.0';'com.unity.modules.jsonserialize'='1.0.0'}} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $project 'Packages/manifest.json')
('m_EditorVersion: '+$Version) | Set-Content (Join-Path $project 'ProjectSettings/ProjectVersion.txt')
$log=Join-Path $project 'editor.log'
$arguments=@('-batchmode','-nographics','-projectPath',('"'+$project+'"'),'-executeMethod','CompatibilitySmoke.Run','-logFile',('"'+$log+'"'))
$process=Start-Process -FilePath $UnityExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
$deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while(!$process.HasExited) {
    if([DateTime]::UtcNow -gt $deadline) { Stop-Process -Id $process.Id; throw "Compatibility test timed out. Inspect $log" }
    Start-Sleep -Seconds 1; $process.Refresh()
}
$resultPath=Join-Path $project 'compatibility-result.json'
if($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $resultPath)) { Get-Content $log -Tail 30; throw "Unity compatibility check failed. Project retained at $project" }
$result=Get-Content -Raw $resultPath | ConvertFrom-Json
if(!$result.ok) { throw 'Unity compatibility check did not pass.' }
$result | ConvertTo-Json
Write-Output ("Disposable compatibility project retained at: "+$project)
