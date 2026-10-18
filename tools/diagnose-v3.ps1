# 用法：关闭此项目 Unity 后执行 ./tools/diagnose-v3.ps1；-ValidateOnly 只检查入口，不启动 Unity。
# 保留图形设备并临时导入 V3 探针，输出横竖屏各 20 张真实窗口截图及布局记录。
# 最终阶段捕获 Boss 宝箱、逐轮领奖和出口确认，必须点击带回结束才进入结果。
param(
    [ValidateSet('Visual')][string]$Mode = 'Visual',
    [switch]$ValidateOnly,
    [string]$EditorPath = 'D:\Program Files\Unity\Editor\2022.3.62f2\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$project = Join-Path $repository 'DevProject'
$verification = Join-Path $repository 'Verification'
. (Join-Path $PSScriptRoot 'unity-process.ps1')
$timeoutSeconds = 1800

$templateName = 'ProjectExpeditionV3Visual.cs.template'
$sourceName = 'ProjectExpeditionPlayModeTests.VisualV3.cs'
$environmentName = 'PROJECT_V3_UI_CAPTURE'
$resultName = 'v3-ui-tests'
$expectedTotal = 2
$expectedTests = @('MiniGame.Tests.ProjectExpeditionPlayModeTests.VisualV3_NarrowPortrait_RealRoomsAndWindows',
    'MiniGame.Tests.ProjectExpeditionPlayModeTests.VisualV3_Landscape_RealRoomsAndWindows')
$screens = @('home', 'hud', 'progression', 'room-exit', 'chest-loot', 'inventory', 'entry-equipment', 'pause', 'ad-extraction',
    'npc-room', 'merchant', 'boss-room', 'boss-chest', 'boss-loot', 'boss-final-reward', 'final-exit', 'result',
    'death-choice', 'death-actions', 'revived-hud')
$prefixes = @('portrait-360x800', 'landscape-1280x720')
$template = Join-Path $PSScriptRoot "templates/diagnostics/$templateName"
$source = Join-Path $project "Assets/Tests/PlayMode/$sourceName"
$meta = "$source.meta"
$log = Join-Path $verification "$resultName.log"
$result = Join-Path $verification "$resultName.xml"
$sourceCreated = $false
$metaCreated = $false
$process = $null
$runLock = $null
$previousEnvironment = [Environment]::GetEnvironmentVariable($environmentName, 'Process')

try {
    if (-not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) { throw "找不到 Unity Editor：$EditorPath" }
    if (-not (Test-Path -LiteralPath $template -PathType Leaf)) { throw "找不到诊断模板：$template" }
    if (-not (Test-Path -LiteralPath (Split-Path -Parent $source) -PathType Container)) { throw "项目测试目录不存在：$source" }
    $templateText = [IO.File]::ReadAllText($template)
    foreach ($name in $expectedTests) {
        if (-not $templateText.Contains(($name -split '\.')[-1] + '(')) { throw "诊断模板缺少入口：$name" }
    }
    if ($templateText -match 'RegionMapWindow|OpenMap|RogueChoice|TryEquip|TryUnequip|PROJECT_V2_') {
        throw "V3 诊断模板仍含已移除业务 API：$template"
    }
    if ($ValidateOnly) {
        Write-Output "V3 图形诊断入口检查通过；将执行 2 个测试并产出 40 张截图及 2 份布局记录；未启动 Unity。"
        return
    }
    New-Item -ItemType Directory -Path $verification -Force | Out-Null
    $runLock = [IO.File]::Open((Join-Path $verification 'verify.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
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

    [Environment]::SetEnvironmentVariable($environmentName, '1', 'Process')
    $arguments = @('-batchmode', '-projectPath', (ConvertTo-UnityArgument $project), '-logFile', (ConvertTo-UnityArgument $log),
        '-runTests', '-testPlatform', 'PlayMode', '-testFilter', (ConvertTo-UnityArgument ($expectedTests -join ';')),
        '-testResults', (ConvertTo-UnityArgument $result))
    Assert-UnityProjectAvailable -Project $project
    $startedUtc = [DateTime]::UtcNow
    $process = Start-Process -FilePath $EditorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Host "[$Mode] Unity PID=$($process.Id)，超时 $timeoutSeconds 秒；结果：$result"
    $deadline = $startedUtc.AddSeconds($timeoutSeconds)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -lt $deadline) { continue }
        throw "诊断 Unity 超时（$timeoutSeconds 秒）：$log"
    }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "诊断 Unity 执行失败（$($process.ExitCode)）：$log" }
    if (-not (Test-Path -LiteralPath $log -PathType Leaf)) { throw "Unity 未写入诊断日志：$log" }
    if (Select-String -LiteralPath $log -Pattern 'error CS\d+' -Quiet) { throw "诊断源码编译失败：$log" }
    if (-not (Test-Path -LiteralPath $result -PathType Leaf) -or (Get-Item -LiteralPath $result).LastWriteTimeUtc -lt $startedUtc) {
        throw "Unity 未写入本次诊断结果，旧结果不能作为通过证据：$result"
    }
    [xml]$results = Get-Content -LiteralPath $result -Raw -Encoding UTF8
    $run = $results.'test-run'
    if ([int]$run.total -ne $expectedTotal -or [int]$run.passed -ne $expectedTotal -or [int]$run.failed -ne 0 -or
        [int]$run.inconclusive -ne 0 -or [int]$run.skipped -ne 0 -or $run.result -ne 'Passed') {
        throw "诊断结果未满足 total=$expectedTotal、全部通过且无失败/跳过/不确定：$result"
    }
    $cases = @($results.SelectNodes('//test-case'))
    if ($cases.Count -ne $expectedTotal -or @($cases | Where-Object { $_.fullname -notin $expectedTests -or $_.result -ne 'Passed' }).Count -ne 0) {
        throw "实际执行的测试与本模式诊断入口不一致：$result"
    }
    $captures = Join-Path $verification 'v3-ui'
    foreach ($prefix in $prefixes) {
        foreach ($screen in $screens) {
            $capture = Join-Path $captures "$prefix-$screen.png"
            if (-not (Test-Path -LiteralPath $capture -PathType Leaf)) { throw "缺少图形证据：$capture" }
            $file = Get-Item -LiteralPath $capture
            if ($file.Length -le 32 -or $file.LastWriteTimeUtc -lt $startedUtc) { throw "截图为空或不是本次输出：$capture" }
        }
        $layout = Join-Path $captures "$prefix-layout.txt"
        if (-not (Test-Path -LiteralPath $layout -PathType Leaf) -or (Get-Item -LiteralPath $layout).LastWriteTimeUtc -lt $startedUtc -or
            -not (Select-String -LiteralPath $layout -Pattern '^LayoutChecks=PASS$' -Quiet)) { throw "缺少本次通过的布局证据：$layout" }
    }
    Write-Output "$Mode 诊断通过：$($run.passed)/$($run.total)。结果：$result；日志：$log"
} finally {
    try {
        if ($null -ne $process -and -not $process.HasExited) {
            $process.Kill()
            if (-not $process.WaitForExit(10000)) { throw "本次 Unity PID=$($process.Id) 尚未退出，保留临时诊断文件：$source" }
        }
        if ($metaCreated -and (Test-Path -LiteralPath $meta)) { Remove-Item -LiteralPath $meta }
        if ($sourceCreated -and (Test-Path -LiteralPath $source)) { Remove-Item -LiteralPath $source }
    } finally {
        [Environment]::SetEnvironmentVariable($environmentName, $previousEnvironment, 'Process')
        if ($null -ne $runLock) { $runLock.Dispose() }
        if ($null -ne $process) { $process.Dispose() }
    }
}
