param(
    [ValidateSet('Gameplay', 'Window', 'Scene')][string]$Kind,
    [string]$Name,
    [ValidateSet('Page', 'Popup', 'Modal')][string]$Preset = 'Modal',
    [ValidateSet('Prefab', 'UnityScene')][string]$ContentMode = 'Prefab',
    [string]$Gameplay,
    [string]$Window,
    [int]$WindowId = 0,
    [switch]$IncludeModel,
    [switch]$SetStartup,
    [string]$StartupScene,
    [string]$RequestFile,
    [switch]$Regenerate,
    [switch]$Validate,
    [switch]$RefreshApiIndex,
    [switch]$CheckApiIndex,
    [switch]$DryRun,
    [string]$EditorPath = 'D:\Program Files\Unity\Editor\2022.3.62f2\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$scaffoldRepository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$scaffoldProject = Join-Path $scaffoldRepository 'DevProject'
$scaffoldOutput = Join-Path $scaffoldRepository 'verification'
$scaffoldModes = @($Regenerate, $Validate, $RefreshApiIndex, $CheckApiIndex, [bool]$RequestFile, [bool]$Kind) |
    Where-Object { $_ }
if ($scaffoldModes.Count -ne 1) {
    throw '请选择一项：-Kind、-RequestFile、-Regenerate、-Validate、-RefreshApiIndex 或 -CheckApiIndex。'
}
if (-not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) { throw "找不到 Unity Editor：$EditorPath" }
if (-not (Test-Path -LiteralPath (Join-Path $scaffoldProject 'ProjectSettings/ProjectVersion.txt'))) {
    throw "找不到开发工程：$scaffoldProject"
}
if ($Kind -and [string]::IsNullOrWhiteSpace($Name)) { throw '-Kind 需要同时填写 -Name。' }
if ($SetStartup -and $Kind -and $Kind -ne 'Scene') { throw '-SetStartup 只能用于新建场景。' }
if ($DryRun -and ($Validate -or $RefreshApiIndex -or $CheckApiIndex)) { throw '-DryRun 仅用于生成计划。' }

# 只提取工程路径，不输出进程的完整命令行。
foreach ($scaffoldProcess in Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'") {
    $scaffoldMatch = [regex]::Match($scaffoldProcess.CommandLine, '(?i)-projectPath\s+(?:"([^"]+)"|(\S+))')
    if (-not $scaffoldMatch.Success) { continue }
    $scaffoldOpenPath = $scaffoldMatch.Groups[1].Value + $scaffoldMatch.Groups[2].Value
    if ([IO.Path]::GetFullPath($scaffoldOpenPath).TrimEnd('\', '/') -ieq $scaffoldProject.TrimEnd('\', '/')) {
        throw '当前工程已在 Unity 中打开。请使用 Scaffolding 菜单，或关闭该工程后运行命令。'
    }
}
$scaffoldUnityLock = Join-Path $scaffoldProject 'Temp/UnityLockfile'
if (Test-Path -LiteralPath $scaffoldUnityLock) {
    try {
        $scaffoldLockProbe = [IO.File]::Open($scaffoldUnityLock, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        $scaffoldLockProbe.Dispose()
    }
    catch [IO.IOException] { throw 'Unity 工程锁正在使用，请先关闭同一工程的 Editor。' }
}

New-Item -ItemType Directory -Path $scaffoldOutput -Force | Out-Null
$scaffoldRunLock = [IO.File]::Open((Join-Path $scaffoldOutput 'scaffold.lock'), [IO.FileMode]::OpenOrCreate,
    [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)

# 路径值使用 Windows 原生参数引用；不经 shell 再解释。
function ConvertTo-ScaffoldArgument {
    param([string]$Value)
    if ($Value.Contains('"') -or $Value.Contains([char]0)) { throw '命令参数不能包含双引号或空字符。' }
    return '"' + $Value.TrimEnd('\') + '"'
}

function Invoke-ScaffoldEditor {
    param([object]$Request, [string]$Phase)
    $scaffoldRequestPath = Join-Path $scaffoldOutput "scaffold-$Phase-request.json"
    $scaffoldResultPath = Join-Path $scaffoldOutput "scaffold-$Phase-result.json"
    $scaffoldLogPath = Join-Path $scaffoldOutput "scaffold-$Phase.log"
    [IO.File]::WriteAllText($scaffoldRequestPath, ($Request | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $scaffoldResultPath) { Remove-Item -LiteralPath $scaffoldResultPath }
    $scaffoldArguments = @('-batchmode', '-nographics', '-quit', '-projectPath', (ConvertTo-ScaffoldArgument $scaffoldProject),
        '-executeMethod', 'GameFramework.Editor.Scaffolding.ScaffoldBatch.RunFromCommandLine',
        '-scaffoldRequest', (ConvertTo-ScaffoldArgument $scaffoldRequestPath),
        '-scaffoldResult', (ConvertTo-ScaffoldArgument $scaffoldResultPath), '-logFile', (ConvertTo-ScaffoldArgument $scaffoldLogPath))
    Write-Output "Unity 脚手架：$Phase"
    $scaffoldEditorProcess = Start-Process -FilePath $EditorPath -ArgumentList $scaffoldArguments -WindowStyle Hidden -PassThru
    $scaffoldEditorProcess.WaitForExit()
    if ($scaffoldEditorProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $scaffoldResultPath)) {
        if (Test-Path -LiteralPath $scaffoldResultPath) { Get-Content -LiteralPath $scaffoldResultPath -Raw | Write-Host }
        throw "Unity $Phase 失败（退出码 $($scaffoldEditorProcess.ExitCode)），日志：$scaffoldLogPath"
    }
    $scaffoldResult = Get-Content -LiteralPath $scaffoldResultPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $scaffoldResult.success) {
        $scaffoldResult.errors | ForEach-Object { Write-Host $_ }
        throw "脚手架 $Phase 未通过，结果：$scaffoldResultPath"
    }
    Write-Output "完成：$($scaffoldResult.phase)；结果：$scaffoldResultPath"
}

try {
    if ($RequestFile) {
        $scaffoldRequest = Get-Content -LiteralPath $RequestFile -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($DryRun) { $scaffoldRequest | Add-Member -MemberType NoteProperty -Name dryRun -Value $true -Force }
    }
    else {
        $scaffoldOperation = 'generate'
        if ($Validate) { $scaffoldOperation = 'validate' }
        elseif ($RefreshApiIndex) { $scaffoldOperation = 'refresh-api' }
        elseif ($CheckApiIndex) { $scaffoldOperation = 'check-api' }
        $scaffoldEntries = @()
        if ($Kind) {
            $scaffoldEntries = @([ordered]@{
                kind = $Kind; name = $Name; preset = $Preset; contentMode = $ContentMode
                gameplay = $Gameplay; window = $Window; windowId = $WindowId; includeModel = [bool]$IncludeModel
            })
        }
        $scaffoldStart = $StartupScene
        if ($SetStartup -and [string]::IsNullOrWhiteSpace($scaffoldStart)) { $scaffoldStart = $Name }
        $scaffoldRequest = [ordered]@{
            operation = $scaffoldOperation; entries = $scaffoldEntries; dryRun = [bool]$DryRun
            setStartup = [bool]$SetStartup; startupScene = $scaffoldStart
        }
    }
    if ([string]::IsNullOrWhiteSpace($scaffoldRequest.operation)) { throw '请求必须包含 operation 字段。' }
    $scaffoldPhase = if ($scaffoldRequest.dryRun) { 'plan' } else { $scaffoldRequest.operation }
    Invoke-ScaffoldEditor -Request $scaffoldRequest -Phase $scaffoldPhase
    if ($scaffoldRequest.operation -eq 'generate' -and -not $scaffoldRequest.dryRun) {
        Invoke-ScaffoldEditor -Request @{ operation = 'validate'; entries = @() } -Phase 'validate'
    }
}
finally {
    $scaffoldRunLock.Dispose()
}
