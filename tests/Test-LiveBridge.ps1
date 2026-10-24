param([Parameter(Mandatory=$true)][string]$ProjectPath)
$ErrorActionPreference = 'Stop'
$client = Join-Path $PSScriptRoot '../Send-UnityCommand.ps1'
$testResults = [Collections.Generic.List[string]]::new()
function Command([string]$Action, [hashtable]$Data = @{}) {
    $raw = & $client -ProjectPath $ProjectPath -Action $Action -ArgumentsJson ($Data | ConvertTo-Json -Depth 15 -Compress)
    return ($raw | ConvertFrom-Json)
}
function Check([bool]$Condition,[string]$Message) { if(!$Condition) { throw "FAIL: $Message" }; $testResults.Add($Message); Write-Output "PASS: $Message" }
function Reject([string]$Action,[hashtable]$Data,[string]$Message) {
    $rejected = $false
    try { $null = Command $Action $Data } catch { $rejected = $true }
    Check $rejected $Message
}
function Inspect([string]$Id,[string]$Component,[int]$Index=0) { return (Command 'inspect' @{instanceId=$Id;componentType=$Component;componentIndex=$Index}) }
function Value($Reply,[string]$Path) { return ($Reply.properties | Where-Object path -eq $Path).value }
$testId = [guid]::NewGuid().ToString('N').Substring(0,8)
$assetFolder = "Assets/BridgeIntegration_$testId"
$className = "BridgeFixture_$testId"
$fixture = "using UnityEngine; public class $className : MonoBehaviour { public float speed=1; public GameObject target; public Transform targetTransform; public int[] numbers = new int[0]; public System.Collections.Generic.List<string> labels = new System.Collections.Generic.List<string>(); }"
$rootId = $null
try {
    $null = Command write_script @{assetPath="$assetFolder/$className.cs";source=$fixture}
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do { Start-Sleep -Milliseconds 500; $status = Get-Content -Raw (Join-Path $ProjectPath '.codex-bridge/status.json') | ConvertFrom-Json } while(($status.compiling -or $status.updating) -and [DateTime]::UtcNow -lt $deadline)
    $rootId = (Command create_empty @{name="Bridge Integration $testId"}).instanceId
    $cubeId = (Command create @{name='Test Cube';primitive='Cube';scale=@{x=1;y=1;z=1}}).instanceId
    $null = Command parent @{instanceId=$cubeId;parentId=$rootId}
    $null = Command rename @{instanceId=$cubeId;name='Renamed Cube'}
    $null = Command rotate @{instanceId=$cubeId;rotation=@{x=0;y=45;z=0}}
    $hierarchy = Command hierarchy
    $cube = $hierarchy.objects | Where-Object instanceId -eq $cubeId
    Check ($cube.name -eq 'Renamed Cube' -and [Math]::Abs($cube.rotation.y-45) -lt 0.01) 'Rename and rotation read back correctly'
    Check ($cube.parentId -eq $rootId) 'Parenting is reflected in hierarchy'
    Reject parent @{instanceId=$rootId;parentId=$cubeId} 'Hierarchy cycles are rejected'
    $copyId = (Command duplicate @{instanceId=$cubeId;name='Duplicate Cube'}).instanceId
    Check ($copyId -ne $cubeId) 'Duplicate receives a distinct ID'
    $null = Command delete @{instanceId=$copyId}
    Check (@((Command hierarchy).objects | Where-Object instanceId -eq $copyId).Count -eq 0) 'Deleted duplicate disappears'
    $null = Command undo
    Check (@((Command hierarchy).objects | Where-Object name -eq 'Duplicate Cube').Count -eq 1) 'Delete can be undone'
    $null = Command create_material @{assetPath="$assetFolder/Test.mat";name='Bridge Test Material'}
    $null = Command assign_material @{instanceId=$cubeId;assetPath="$assetFolder/Test.mat"}
    $renderer = Inspect $cubeId 'UnityEngine.MeshRenderer'
    Check ((Value $renderer 'm_Materials.Array.data[0]') -eq "$assetFolder/Test.mat") 'Material assignment reads back correctly'
    $null = Command save_prefab @{instanceId=$cubeId;assetPath="$assetFolder/Test.prefab"}
    $prefabId = (Command instantiate_prefab @{assetPath="$assetFolder/Test.prefab";name='Test Prefab Instance';position=@{x=4;y=0;z=0}}).instanceId
    $null = Command parent @{instanceId=$prefabId;parentId=$rootId}
    Check (@((Command hierarchy).objects | Where-Object instanceId -eq $prefabId).Count -eq 1) 'Prefab instantiates into scene'
    $firstComponent = (Command add_component @{instanceId=$cubeId;componentType=$className}).instanceId
    $secondComponent = (Command add_component @{instanceId=$cubeId;componentType=$className}).instanceId
    Check ($firstComponent -ne $secondComponent) 'Multiple components have distinct stable IDs'
    $null = Command set_property @{instanceId=$cubeId;componentType=$className;componentIndex=1;propertyPath='speed';value='7.25'}
    Check ((Value (Inspect $cubeId $className 0) 'speed') -eq '1') 'Component index edit leaves first component unchanged'
    Check ((Value (Inspect $cubeId $className 1) 'speed') -eq '7.25') 'Component index selects second component'
    $null = Command set_property @{instanceId=$cubeId;componentId=$firstComponent;propertyPath='speed';value='3.5'}
    Check ((Value (Inspect $cubeId $className 0) 'speed') -eq '3.5') 'Component ID targets exact component'
    $null = Command set_property @{instanceId=$cubeId;componentId=$firstComponent;propertyPath='target';referenceId=$rootId}
    $reference = (Inspect $cubeId $className).properties | Where-Object path -eq target
    Check ($reference.referenceId -eq $rootId) 'Scene GameObject reference reads back correctly'
    $rootTransform = (Inspect $rootId '').componentDetails | Where-Object type -eq UnityEngine.Transform
    $null = Command set_property @{instanceId=$cubeId;componentId=$firstComponent;propertyPath='targetTransform';referenceId=$rootTransform.id}
    $transformRef = (Inspect $cubeId $className).properties | Where-Object path -eq targetTransform
    Check ($transformRef.referenceId -eq $rootTransform.id) 'Scene component reference reads back correctly'
    Reject set_property @{instanceId=$cubeId;componentId=$firstComponent;propertyPath='targetTransform';referenceId=$rootId} 'Wrong scene reference type rejected'
    $transformRef = (Inspect $cubeId $className).properties | Where-Object path -eq targetTransform
    Check ($transformRef.referenceId -eq $rootTransform.id) 'Rejected reference edit preserves prior value'
    $null = Command set_property @{instanceId=$cubeId;componentId=$firstComponent;propertyPath='target';value='null'}
    Check ((Value (Inspect $cubeId $className) 'target') -eq 'null') 'Scene reference can be cleared'
    $null = Command resize_array @{instanceId=$cubeId;componentId=$firstComponent;propertyPath='numbers';arraySize=3}
    $null = Command set_property @{instanceId=$cubeId;componentId=$firstComponent;propertyPath='numbers.Array.data[1]';value='42'}
    Check ((Value (Inspect $cubeId $className) 'numbers.Array.data[1]') -eq '42') 'Array resizing and element editing work'
    $null = Command resize_array @{instanceId=$cubeId;componentId=$firstComponent;propertyPath='labels';arraySize=2}
    $null = Command set_property @{instanceId=$cubeId;componentId=$firstComponent;propertyPath='labels.Array.data[0]';value='hello'}
    Check ((Value (Inspect $cubeId $className) 'labels.Array.data[0]') -eq 'hello') 'Generic list resizing and editing work'
    Reject resize_array @{instanceId=$cubeId;componentId=$firstComponent;propertyPath='numbers';arraySize=-1} 'Negative array sizes rejected'
    Reject inspect @{instanceId=$cubeId;componentType=$className;componentIndex=20} 'Invalid component index rejected'
    Reject set_property @{instanceId=$cubeId;componentId=$rootTransform.id;propertyPath='speed';value='9'} 'Foreign component IDs rejected'
    Reject set_property @{instanceId=$cubeId;componentId=$firstComponent;propertyPath='speed';value='invalid'} 'Invalid numeric values rejected'
    Reject set_property @{instanceId=$cubeId;componentId=$firstComponent;propertyPath='target';referenceId='missing'} 'Missing scene references rejected'
    $null = Command set_property @{instanceId=$cubeId;componentId=$firstComponent;propertyPath='target';referenceId=$rootId}
    Reject set_property @{instanceId=$cubeId;componentId=$firstComponent;propertyPath='target';value='Assets/Missing.asset'} 'Missing assets rejected without clearing existing reference'
    $reference = (Inspect $cubeId $className).properties | Where-Object path -eq target
    Check ($reference.referenceId -eq $rootId) 'Missing asset rejection preserves existing scene reference'
    Reject assign_material @{instanceId=$cubeId;assetPath="$assetFolder/Test.mat";materialIndex=40} 'Invalid material slots rejected'
    $null = Command play
    Start-Sleep -Milliseconds 500
    Reject rename @{instanceId=$cubeId;name='Should Not Change'} 'Scene edits blocked during Play mode'
    $null = Command stop
    Start-Sleep -Milliseconds 500
    $null = Command open_panel
    $null = Command ping
    Start-Sleep -Milliseconds 400
    $status = Get-Content -Raw (Join-Path $ProjectPath '.codex-bridge/status.json') | ConvertFrom-Json
    Check ($status.lastAction -eq 'ping' -and $status.lastCommandOk) 'Panel heartbeat records last command and success'
    Check ($status.errors.Count -gt 0) 'Panel error history records rejected commands'
    $null = Command clear_errors
    $null = Command write_script @{assetPath="$assetFolder/DeliberateCompileError.cs";source='public class DeliberateCompileError { this is intentionally invalid; }'}
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 300
        $status = Get-Content -Raw (Join-Path $ProjectPath '.codex-bridge/status.json') | ConvertFrom-Json
    } while(!($status.errors -match 'DeliberateCompileError') -and [DateTime]::UtcNow -lt $deadline)
    Check ([bool]($status.errors -match 'DeliberateCompileError')) 'Compiler errors appear in connection status'
    [IO.File]::WriteAllText((Join-Path $ProjectPath "$assetFolder/DeliberateCompileError.cs"),'public class DeliberateCompileError { }')
    $null = Command refresh
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 300
        $status = Get-Content -Raw (Join-Path $ProjectPath '.codex-bridge/status.json') | ConvertFrom-Json
    } while(($status.compiling -or $status.errors.Count -gt 0) -and [DateTime]::UtcNow -lt $deadline)
    Check (!$status.compiling -and $status.errors.Count -eq 0) 'Connection recovers after fixing compile errors'
    Write-Output ("Completed " + $testResults.Count + ' integration checks.')
} finally {
    if($rootId) { try { $null = Command delete @{instanceId=$rootId} } catch { Write-Warning 'Test root cleanup failed.' } }
    # Only remove this run's generated fixture assets, never user project content.
    $resolvedProject = [IO.Path]::GetFullPath($ProjectPath)
    $fixturePath = [IO.Path]::GetFullPath((Join-Path $resolvedProject $assetFolder))
    if(!$fixturePath.StartsWith((Join-Path $resolvedProject 'Assets') + [IO.Path]::DirectorySeparatorChar) -or !(Split-Path $fixturePath -Leaf).StartsWith('BridgeIntegration_')) { throw 'Unsafe test cleanup path.' }
    if(Test-Path -LiteralPath $fixturePath) { Remove-Item -LiteralPath $fixturePath -Recurse -Force }
    if(Test-Path -LiteralPath ($fixturePath+'.meta')) { Remove-Item -LiteralPath ($fixturePath+'.meta') -Force }
    $null = Command refresh
}
