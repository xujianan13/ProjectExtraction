param(
    [ValidateSet('Quick', 'Full')][string]$Mode = 'Quick',
    [string]$EditorPath = 'D:\Program Files\Unity\Editor\2022.3.62f2\Editor\Unity.exe',
    [switch]$WindowsPlayer,
    [string]$HybridClrSource,
    [int]$TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$project = Join-Path $repository 'DevProject'
$verification = Join-Path $repository 'verification'
$copy = Join-Path $verification 'unified-reuse'
$markerText = 'UnityMiniGameFramework verification copy v1'
. (Join-Path $PSScriptRoot 'unity-process.ps1')
New-Item -ItemType Directory -Path $verification -Force | Out-Null
$runLock = [IO.File]::Open((Join-Path $verification 'verify.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
$report = [ordered]@{ schemaVersion=1; mode=$Mode; windowsPlayer=[bool]$WindowsPlayer; success=$false;
    startedUtc=[DateTime]::UtcNow.ToString('o'); finishedUtc=''; failedStage=''; error=''; stages=@() }
$currentStage = ''

function Invoke-Stage {
    param([string]$Name, [scriptblock]$Action)
    $script:currentStage = $Name
    Write-Host "[$Name] 开始"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $details = @(& $Action)
    $script:report.stages += [ordered]@{name=$Name; success=$true; seconds=[Math]::Round($watch.Elapsed.TotalSeconds,2); details=$details}
    Write-Host "[$Name] 通过"
}

function Invoke-NodeChecked {
    param([string[]]$Arguments, [string]$Log)
    & node @Arguments 2>&1 | Tee-Object -FilePath $Log | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Node 执行失败：$Log" }
}

function Invoke-UnityStage {
    param([string]$Name, [string]$Target=$project, [string]$Method, [string]$Filter, [switch]$Tests)
    Invoke-Stage $Name {
        $options = @{EditorPath=$EditorPath; Project=$Target; Log=(Join-Path $verification "$Name.log");
            Method=$Method; TimeoutSeconds=$TimeoutSeconds}
        if ($Tests) { $options.TestResults=Join-Path $verification "$Name.xml"; $options.TestFilter=$Filter }
        Invoke-FrameworkUnity @options
    }
}

try {
    Invoke-Stage 'preflight' {
        if ($WindowsPlayer -and $Mode -ne 'Full') { throw '-WindowsPlayer 需要 -Mode Full。' }
        if (-not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) { throw "找不到 Unity Editor：$EditorPath" }
        Assert-UnityProjectAvailable -Project $project
    }
    Invoke-Stage 'node-tests' {
        $nodeTests = @(Get-ChildItem -LiteralPath (Join-Path $repository 'tool') -Filter '*.test.mjs' | ForEach-Object FullName)
        if ($nodeTests.Count -eq 0) { throw '未发现导表测试。' }
        Invoke-NodeChecked -Arguments (@('--test')+$nodeTests) -Log (Join-Path $verification 'node-tests.log')
    }
    Invoke-Stage 'excel-verify' {
        Invoke-NodeChecked -Arguments @((Join-Path $repository 'tool/export-excel.mjs'),'verify') -Log (Join-Path $verification 'excel-verify.log')
    }
    Invoke-UnityStage 'project-check' -Method 'MiniGame.Editor.ProjectVerification.Validate'
    Invoke-UnityStage 'playmode' -Tests
    if ($Mode -eq 'Full') {
        Invoke-Stage 'minimal-create' {
            if (Test-Path -LiteralPath $copy) {
                $resolvedCopy = (Resolve-Path -LiteralPath $copy).ProviderPath.TrimEnd('\','/')
                $expectedCopy = [IO.Path]::GetFullPath((Join-Path $repository 'verification/unified-reuse')).TrimEnd('\','/')
                $marker = Join-Path $resolvedCopy '.framework-verification-copy'
                if ($resolvedCopy -ine $expectedCopy -or (Get-Item -LiteralPath $copy).Attributes -band [IO.FileAttributes]::ReparsePoint -or
                    -not (Test-Path -LiteralPath $marker) -or [IO.File]::ReadAllText($marker).Trim() -ne $markerText) {
                    throw "验收目录不是此工具创建的副本，拒绝删除：$copy"
                }
                Assert-UnityProjectAvailable -Project (Join-Path $copy 'DevProject')
                Remove-Item -LiteralPath $resolvedCopy -Recurse -Force
            }
            & (Join-Path $PSScriptRoot 'new-project.ps1') -Destination $copy -ProjectName FrameworkVerification -CompanyName FrameworkVerification `
                -ApplicationId com.frameworkverification.minimal -Template Minimal -AllowNestedVerification
            [IO.File]::WriteAllText((Join-Path $copy '.framework-verification-copy'), $markerText)
        }
        $copyProject = Join-Path $copy 'DevProject'
        Invoke-UnityStage 'minimal-import' -Target $copyProject -Method 'MiniGame.Editor.ProjectVerification.InitializeTemplate'
        Invoke-UnityStage 'minimal-initialize' -Target $copyProject -Method 'MiniGame.Editor.ProjectVerification.InitializeTemplate'
        Invoke-UnityStage 'minimal-check' -Target $copyProject -Method 'MiniGame.Editor.ProjectVerification.Validate'
        $contentFilter = 'MiniGame.Tests.ScaffoldContentPlayModeTests;MiniGame.Tests.ScaffoldStartupTests'
        Invoke-UnityStage 'minimal-playmode' -Target $copyProject -Tests -Filter $contentFilter
        Invoke-Stage 'semantic-fixture' {
            Invoke-NodeChecked -Arguments @((Join-Path $PSScriptRoot 'create-verification-content.mjs'),$copy) -Log (Join-Path $verification 'semantic-fixture.log')
        }
        Invoke-Stage 'scaffold-generate' {
            & (Join-Path $copy 'tools/scaffold.ps1') -RequestFile (Join-Path $copy 'config/scaffold/example-request.json') -EditorPath $EditorPath
        }
        Invoke-UnityStage 'reuse-check' -Target $copyProject -Method 'MiniGame.Editor.ProjectVerification.Validate'
        Invoke-UnityStage 'reuse-editor-playmode' -Target $copyProject -Tests -Filter $contentFilter
        Invoke-UnityStage 'reuse-offline-build' -Target $copyProject -Method 'MiniGame.Editor.ProjectVerification.BuildOffline'
        Invoke-UnityStage 'reuse-offline-playmode' -Target $copyProject -Tests -Filter $contentFilter
        if ($WindowsPlayer) {
            Invoke-Stage 'player-probe-install' {
                $probeDirectory = Join-Path $copyProject 'Assets/Scripts/Boot/Runtime/Verification'
                New-Item -ItemType Directory -Path $probeDirectory -Force | Out-Null
                Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'templates/verification/ProjectVerificationProbe.cs.template') `
                    -Destination (Join-Path $probeDirectory 'ProjectVerificationProbe.cs')
            }
            $previousSource = $env:MINIGAME_HYBRIDCLR_SOURCE
            $previousOutput = $env:MINIGAME_PLAYER_OUTPUT
            try {
                if ($HybridClrSource) { $env:MINIGAME_HYBRIDCLR_SOURCE=$HybridClrSource }
                $env:MINIGAME_PLAYER_OUTPUT=Join-Path $copyProject 'Builds/Windows/Game.exe'
                Invoke-UnityStage 'player-prepare' -Target $copyProject -Method 'MiniGame.Editor.ProjectBuildPipeline.PrepareWindows'
                Invoke-UnityStage 'player-build' -Target $copyProject -Method 'MiniGame.Editor.ProjectVerification.BuildWindows'
            } finally { $env:MINIGAME_HYBRIDCLR_SOURCE=$previousSource; $env:MINIGAME_PLAYER_OUTPUT=$previousOutput }
            Invoke-Stage 'player-smoke' {
                $player = Join-Path $copyProject 'Builds/Windows/Game.exe'
                $resultPath = Join-Path $verification 'player-smoke.json'
                if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
                $arguments = @('-batchmode','-nographics','-logFile',(ConvertTo-UnityArgument (Join-Path $verification 'player-smoke.log')),
                    '-frameworkSmokeResult',(ConvertTo-UnityArgument $resultPath))
                $process = Start-Process -FilePath $player -ArgumentList $arguments -WindowStyle Hidden -PassThru
                $deadline = [DateTime]::UtcNow.AddSeconds(150)
                while (-not $process.WaitForExit(1000)) {
                    if ([DateTime]::UtcNow -lt $deadline) { continue }
                    Stop-Process -Id $process.Id -Force
                    throw '独立 Player 启动验证超时。'
                }
                $process.WaitForExit()
                if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath)) { throw '独立 Player 启动失败，请检查 player-smoke.log。' }
                $result = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
                if (-not $result.success) { throw "独立 Player 探针失败：$($result.error)" }
                $result
            }
        }
    }
    $report.success=$true
} catch {
    $report.failedStage=$currentStage
    $report.error=$_.Exception.Message
    throw
} finally {
    $report.finishedUtc=[DateTime]::UtcNow.ToString('o')
    try {
        [IO.File]::WriteAllText((Join-Path $verification 'verify-result.json'), ($report | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    } finally { $runLock.Dispose() }
}
Write-Output "验收完成：$Mode。结果：$(Join-Path $verification 'verify-result.json')"
