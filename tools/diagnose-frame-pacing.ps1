# 用法：关闭此项目 Unity 后执行 ./tools/diagnose-frame-pacing.ps1 -Label baseline 或 -Label after。
# V3 保留图形设备；每后端 2 秒预热和 8 秒真实帧采样，另测显式暂停检查点；不进入常规 Quick 测试计数。
param(
    [ValidateSet('baseline', 'after')][string]$Label = 'baseline',
    [string]$EditorPath = 'D:\Program Files\Unity\Editor\2022.3.62f2\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$project = Join-Path $repository 'DevProject'
$verification = Join-Path $repository 'Verification/v3-frame-pacing'
. (Join-Path $PSScriptRoot 'unity-process.ps1')
$timeoutSeconds = 1800
$expectedTest = 'MiniGame.Tests.ProjectExpeditionPlayModeTests.FramePacing_V3_FileAndMemory_ActualUnityFrames'
$template = Join-Path $PSScriptRoot 'templates/diagnostics/ProjectExpeditionV3FramePacing.cs.template'
$source = Join-Path $project 'Assets/Tests/PlayMode/ProjectExpeditionPlayModeTests.V3FramePacing.cs'
$meta = "$source.meta"
$log = Join-Path $verification "$Label.log"
$result = Join-Path $verification "$Label.xml"
$report = Join-Path $verification "$Label.json"
$sourceCreated = $false
$metaCreated = $false
$process = $null
$runLock = $null
$previousEnabled = [Environment]::GetEnvironmentVariable('PROJECT_V3_FRAME_PACING', 'Process')
$previousLabel = [Environment]::GetEnvironmentVariable('PROJECT_V3_FRAME_PACING_LABEL', 'Process')

try {
    if (-not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) { throw "找不到 Unity Editor：$EditorPath" }
    if (-not (Test-Path -LiteralPath $template -PathType Leaf)) { throw "找不到诊断模板：$template" }
    if (-not (Test-Path -LiteralPath (Split-Path -Parent $source) -PathType Container)) { throw "项目测试目录不存在：$source" }
    New-Item -ItemType Directory -Path $verification -Force | Out-Null
    $runLock = [IO.File]::Open((Join-Path $repository 'Verification/verify.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    Assert-UnityProjectAvailable -Project $project
    if ((Test-Path -LiteralPath $source) -or (Test-Path -LiteralPath $meta)) {
        throw "临时诊断源码或 meta 已存在，拒绝覆盖或清理：$source；$meta"
    }

    $templateBytes = [IO.File]::ReadAllBytes($template)
    $sourceStream = [IO.File]::Open($source, 'CreateNew', 'Write', 'None')
    $sourceCreated = $true
    try { $sourceStream.Write($templateBytes, 0, $templateBytes.Length) } finally { $sourceStream.Dispose() }
    $metaStream = [IO.File]::Open($meta, 'CreateNew', 'Write', 'None')
    $metaCreated = $true
    try {
        $metaText = "fileFormatVersion: 2`nguid: $([Guid]::NewGuid().ToString('N'))`nMonoImporter:`n  externalObjects: {}`n" +
            "  serializedVersion: 2`n  defaultReferences: []`n  executionOrder: 0`n  icon: {instanceID: 0}`n" +
            "  userData:`n  assetBundleName:`n  assetBundleVariant:`n"
        $metaBytes = [Text.UTF8Encoding]::new($false).GetBytes($metaText)
        $metaStream.Write($metaBytes, 0, $metaBytes.Length)
    } finally { $metaStream.Dispose() }

    [Environment]::SetEnvironmentVariable('PROJECT_V3_FRAME_PACING', '1', 'Process')
    [Environment]::SetEnvironmentVariable('PROJECT_V3_FRAME_PACING_LABEL', $Label, 'Process')
    $arguments = @('-batchmode', '-projectPath', (ConvertTo-UnityArgument $project), '-logFile', (ConvertTo-UnityArgument $log),
        '-runTests', '-testPlatform', 'PlayMode', '-testFilter', (ConvertTo-UnityArgument $expectedTest),
        '-testResults', (ConvertTo-UnityArgument $result))
    Assert-UnityProjectAvailable -Project $project
    $startedUtc = [DateTime]::UtcNow
    $process = Start-Process -FilePath $EditorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Host "[$Label] Unity PID=$($process.Id)，采样约 20 秒，导入/加载另计；结果：$report"
    $deadline = $startedUtc.AddSeconds($timeoutSeconds)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -lt $deadline) { continue }
        throw "诊断 Unity 超时（$timeoutSeconds 秒）：$log"
    }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "诊断 Unity 执行失败（$($process.ExitCode)）：$log" }
    if (-not (Test-Path -LiteralPath $log -PathType Leaf)) { throw "Unity 未写入诊断日志：$log" }
    if (Select-String -LiteralPath $log -Pattern 'error CS\d+' -Quiet) { throw "诊断源码编译失败：$log" }
    foreach ($artifact in @($result, $report)) {
        if (-not (Test-Path -LiteralPath $artifact -PathType Leaf) -or (Get-Item -LiteralPath $artifact).LastWriteTimeUtc -lt $startedUtc) {
            throw "Unity 未写入本次诊断产物，旧文件不能作为通过证据：$artifact"
        }
    }
    [xml]$results = Get-Content -LiteralPath $result -Raw -Encoding UTF8
    $run = $results.'test-run'
    if ([int]$run.total -ne 1 -or [int]$run.passed -ne 1 -or [int]$run.failed -ne 0 -or [int]$run.inconclusive -ne 0 -or
        [int]$run.skipped -ne 0 -or $run.result -ne 'Passed') { throw "诊断入口必须 1/1 通过且无跳过或不确定：$result" }
    $cases = @($results.SelectNodes('//test-case'))
    if ($cases.Count -ne 1 -or $cases[0].fullname -ne $expectedTest -or $cases[0].result -ne 'Passed') {
        throw "实际执行的测试与独立帧率诊断入口不一致：$result"
    }
    $measurement = Get-Content -LiteralPath $report -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($measurement.Label -ne $Label -or @($measurement.Backends).Count -ne 2) { throw "帧率报告标签或后端数量无效：$report" }
    foreach ($arm in $measurement.Backends) {
        if ($arm.TargetFrameRate -ne 60 -or $arm.VSyncCount -ne 0 -or -not $arm.BusinessCheckpointMatches -or
            $arm.ColdRestoreTickDifference -ne 0 -or $arm.SampleJournalRevisionChange -ne 0 -or
            $arm.SimulationWrittenRecords -ne 0 -or $arm.WrittenRecords -lt 1) {
            throw "帧率报告缺少统一限帧、普通帧无保存或业务检查点冷读证据：$report"
        }
        Write-Output ("{0}: Unity {1:N2} fps，world {2:N2} tick/s，loop P95 {3:N2} ms，显式检查点 {4:N2} ms" -f
            $arm.Backend, $arm.UnityFramesPerSecond, $arm.WorldTicksPerSecond, $arm.MainLoopMilliseconds.P95, $arm.CheckpointSaveMilliseconds)
    }
    Write-Output "帧率诊断通过 1/1（不判定硬件 FPS 达标）。报告：$report；结果：$result；日志：$log"
} finally {
    try {
        if ($null -ne $process -and -not $process.HasExited) {
            $process.Kill()
            if (-not $process.WaitForExit(10000)) { throw "本次 Unity PID=$($process.Id) 尚未退出，保留临时诊断文件：$source" }
        }
        if ($metaCreated -and (Test-Path -LiteralPath $meta)) { Remove-Item -LiteralPath $meta }
        if ($sourceCreated -and (Test-Path -LiteralPath $source)) { Remove-Item -LiteralPath $source }
    } finally {
        [Environment]::SetEnvironmentVariable('PROJECT_V3_FRAME_PACING', $previousEnabled, 'Process')
        [Environment]::SetEnvironmentVariable('PROJECT_V3_FRAME_PACING_LABEL', $previousLabel, 'Process')
        if ($null -ne $runLock) { $runLock.Dispose() }
        if ($null -ne $process) { $process.Dispose() }
    }
}
