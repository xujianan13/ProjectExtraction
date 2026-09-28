# Unity 子进程只使用自己的进程 ID 等待和终止，不干预其他工程。
function Assert-UnityProjectAvailable {
    param([string]$Project)
    $resolved = [IO.Path]::GetFullPath($Project).TrimEnd('\', '/')
    foreach ($item in Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'") {
        $match = [regex]::Match($item.CommandLine, '(?i)-projectPath\s+(?:"([^"]+)"|(\S+))')
        if (-not $match.Success) { continue }
        $openPath = $match.Groups[1].Value + $match.Groups[2].Value
        if ([IO.Path]::GetFullPath($openPath).TrimEnd('\', '/') -ieq $resolved) {
            throw "Unity 已打开工程 $resolved，请关闭该工程后运行验收。"
        }
    }
    $lockPath = Join-Path $resolved 'Temp/UnityLockfile'
    if (Test-Path -LiteralPath $lockPath) {
        $probe = [IO.File]::Open($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        $probe.Dispose()
    }
}

function ConvertTo-UnityArgument {
    param([string]$Value)
    if ($Value.Contains('"') -or $Value.Contains([char]0)) { throw '参数包含非法引号或空字符。' }
    return '"' + $Value.TrimEnd('\') + '"'
}

function Invoke-FrameworkUnity {
    param([string]$EditorPath, [string]$Project, [string]$Log, [string]$Method, [string]$TestResults,
        [string]$TestFilter, [int]$TimeoutSeconds = 1800)
    Assert-UnityProjectAvailable -Project $Project
    $arguments = @('-batchmode','-nographics','-projectPath',(ConvertTo-UnityArgument $Project),'-logFile',(ConvertTo-UnityArgument $Log))
    if ($TestResults) {
        if (Test-Path -LiteralPath $TestResults) { Remove-Item -LiteralPath $TestResults }
        $arguments += @('-runTests','-testPlatform','PlayMode','-testResults',(ConvertTo-UnityArgument $TestResults))
        if ($TestFilter) { $arguments += @('-testFilter',(ConvertTo-UnityArgument $TestFilter)) }
    } else {
        $arguments += '-quit'
        if ($Method) { $arguments += @('-executeMethod', $Method) }
    }
    $process = Start-Process -FilePath $EditorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -lt $deadline) { continue }
        Stop-Process -Id $process.Id -Force
        throw "Unity 执行超时（$TimeoutSeconds 秒）：$Log"
    }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0 -or (Select-String -LiteralPath $Log -Pattern 'error CS\d+' -Quiet)) {
        throw "Unity 执行失败（$($process.ExitCode)）：$Log"
    }
    if ($TestResults) {
        if (-not (Test-Path -LiteralPath $TestResults)) { throw "Unity 未写入测试结果：$TestResults" }
        [xml]$results = Get-Content -LiteralPath $TestResults -Raw
        $run = $results.'test-run'
        if ([int]$run.total -le 0 -or [int]$run.failed -ne 0 -or [int]$run.passed -le 0 -or [int]$run.inconclusive -ne 0 -or
            $run.result -notin @('Passed', 'Skipped', 'Skipped:Ignored')) { throw "测试结果未通过：$TestResults" }
        $unexpected = @($results.SelectNodes('//test-case[@result="Skipped"]') | Where-Object {
            $_.fullname -ne 'MiniGame.Tests.ScaffoldContentPlayModeTests.GeneratedScenesLoadCancelReenterAndRelease'
        })
        if ($unexpected.Count -gt 0) { throw "发现未约定跳过的测试：$($unexpected.fullname -join ', ')" }
        return [pscustomobject]@{total=[int]$run.total; passed=[int]$run.passed; skipped=[int]$run.skipped; report=$TestResults}
    }
}
