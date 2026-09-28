param(
    [Parameter(Mandatory = $true)][string]$Destination,
    [Parameter(Mandatory = $true)][string]$ProjectName,
    [Parameter(Mandatory = $true)][string]$CompanyName,
    [Parameter(Mandatory = $true)][string]$ApplicationId,
    [ValidateSet('Demo', 'Minimal')][string]$Template = 'Demo',
    [switch]$DryRun,
    [switch]$AllowNestedVerification
)

$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\', '/')
if (-not [IO.Path]::IsPathRooted($Destination) -or $Destination -notmatch '^[A-Za-z]:[\\/]|^\\\\') {
    throw 'Destination 必须是完整的绝对路径。'
}
$destinationRoot = [IO.Path]::GetFullPath($Destination).TrimEnd('\', '/')
if ($destinationRoot -eq [IO.Path]::GetPathRoot($destinationRoot).TrimEnd('\', '/')) { throw 'Destination 不能是磁盘根目录。' }

foreach ($value in @(@{ Name = 'ProjectName'; Value = $ProjectName }, @{ Name = 'CompanyName'; Value = $CompanyName })) {
    $name = $value.Value.Trim()
    if ($name.Length -eq 0 -or $name.Length -gt 80 -or $name -eq '.' -or $name -eq '..' -or
        $name -match '[\x00-\x1f<>:"/\\|?*]' -or $name.EndsWith('.') -or $name.EndsWith(' ')) {
        throw "$($value.Name) 需要是 1–80 字符、可作为目录名的非空名称。"
    }
}
$ProjectName = $ProjectName.Trim()
$CompanyName = $CompanyName.Trim()
if ($ApplicationId -cnotmatch '^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*){1,}$' -or $ApplicationId.Length -gt 255) {
    throw 'ApplicationId 必须是小写反向域名，例如 com.example.mygame。'
}

$comparison = [StringComparison]::OrdinalIgnoreCase
$sourcePrefix = $source + [IO.Path]::DirectorySeparatorChar
if ($destinationRoot.Equals($source, $comparison) -or $destinationRoot.StartsWith($sourcePrefix, $comparison)) {
    $verificationPrefix = Join-Path $source 'verification'
    $verificationPrefix += [IO.Path]::DirectorySeparatorChar
    if (-not $AllowNestedVerification -or -not $destinationRoot.StartsWith($verificationPrefix, $comparison)) {
        throw 'Destination 不能在模板目录内；仅验证副本可用 -AllowNestedVerification 放在 verification 下。'
    }
}
if (Test-Path -LiteralPath $destinationRoot) {
    if (-not (Test-Path -LiteralPath $destinationRoot -PathType Container)) { throw 'Destination 已是文件。' }
    if (Get-ChildItem -LiteralPath $destinationRoot -Force | Select-Object -First 1) { throw 'Destination 已非空，拒绝覆盖。' }
}

$required = @(
    'DevProject/Assets/Scenes/Main.unity', 'DevProject/Assets/Scenes/Loading.unity',
    'DevProject/Assets/Resources/Framework.prefab', 'DevProject/Assets/GameResources/Config/GamePackageSettings.json',
    'DevProject/Assets/GameResources/Input/GameInputActions.inputactions',
    'DevProject/Assets/GameResources/Config/Tables/Table_ExampleItems.json',
    'DevProject/Assets/Scripts/GameLogic/Runtime/Gen/GameTables.Gen.cs',
    'DevProject/Assets/Scripts/GameLogic/Runtime/GameLogic.asmdef',
    'DevProject/Packages/manifest.json', 'DevProject/ProjectSettings/ProjectVersion.txt',
    'DevProject/Assets/GameResources/UI/UI_RoundGame.prefab', 'DevProject/Assets/GameResources/Rounds/RoundPlayer.prefab',
    'config/excel/ExampleItems.xlsx', 'tool/package-lock.json'
)
if ($Template -eq 'Minimal') {
    $required = @($required | Where-Object { $_ -notmatch 'ExampleItems|UI_RoundGame|RoundPlayer' })
    $required += @('tools/templates/minimal/DevProject/Assets/Scripts/GameLogic/Runtime/GameMain.cs.template',
        'tools/templates/minimal/config/scaffold/project.json.template')
}
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $relative) -PathType Leaf)) { throw "模板缺少必需文件：$relative" }
}

$folders = @('DevProject/Assets', 'DevProject/Packages', 'DevProject/ProjectSettings', 'config', 'tool', 'tools', 'docs')
$files = @('README.md', 'AGENTS.md', '.gitignore', '.gitattributes')
$copyPlan = New-Object 'System.Collections.Generic.List[object]'
$directoryPlan = New-Object 'System.Collections.Generic.List[string]'

function Add-Tree {
    param([string]$RelativeDirectory)
    $absolute = Join-Path $source $RelativeDirectory
    if (-not (Test-Path -LiteralPath $absolute -PathType Container)) { throw "模板缺少目录：$RelativeDirectory" }
    $directoryPlan.Add($RelativeDirectory)
    foreach ($item in Get-ChildItem -LiteralPath $absolute -Force) {
        $relative = ($RelativeDirectory + '/' + $item.Name).Replace('\', '/')
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "模板目录含链接，拒绝跟随：$relative" }
        $skip = $relative -match '^DevProject/Assets/(StreamingAssets|HybridCLRGenerate)(/|\.meta$|$)' -or
            $relative -match '^DevProject/Assets/Resources/HotUpdateMetadata(/|\.meta$|$)' -or
            $relative -match '^DevProject/Assets/GameResources/Code/GameLogic\.dll\.bytes(\.meta)?$' -or
            $relative -match '^DevProject/Packages/[^/]*[Cc]ache(/|$)' -or
            $relative -match '^tool/(node_modules|\.excel-stage-[^/]*)(/|$)' -or
            $relative -match '(^|/)(\.git|Library|Temp|Logs|Obj|UserSettings|verification|AssetBundleBuild|HybridCLRData)(/|$)'
        if ($Template -eq 'Minimal') {
            if ($relative -match '^DevProject/Assets/Scripts/GameLogic/Runtime/') {
                $skip = $skip -or $relative -notmatch '/GameLogic\.asmdef(\.meta)?$'
            }
            if ($relative -match '^DevProject/Assets/GameResources/') {
                $keepContent = $relative -match '^DevProject/Assets/GameResources/(Config|Input)(/|\.meta$|$)' -and
                    ($item.PSIsContainer -or $relative -match '/(GamePackageSettings\.json|GameInputActions\.inputactions|UI_VirtualControls\.prefab)(\.meta)?$' -or
                    $relative -match '/(Config|Input)\.meta$')
                $skip = $skip -or -not $keepContent
            }
            $skip = $skip -or $relative -match '^config/(excel|scaffold)(/|$)' -or $relative -eq 'tool/.excel-export-manifest.json' -or
                $relative -match '^DevProject/Assets/Editor/(RoundUIBuilder|RoundDemoAssetsBuilder|GameUISampleBuilder)\.cs(\.meta)?$' -or
                $relative -match '^DevProject/Assets/Tests/PlayMode/FrameworkDemoPlayModeTests\.cs(\.meta)?$' -or
                $relative -match '^DevProject/Assets/Scripts/GameFramework/Tests/(Core/(HotUpdateBootPlayModeTests|RoundDemoPlayModeTests)|Save/(PlayerRoundSaveTests|SaveStartupTests))\.cs(\.meta)?$'
        }
        if ($skip) { continue }
        if ($item.PSIsContainer) { Add-Tree -RelativeDirectory $relative }
        else { $copyPlan.Add([pscustomobject]@{ Source = $item.FullName; Relative = $relative }) }
    }
}

foreach ($folder in $folders) { Add-Tree -RelativeDirectory $folder }
foreach ($file in $files) {
    $absolute = Join-Path $source $file
    if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) { throw "模板缺少文件：$file" }
    $copyPlan.Add([pscustomobject]@{ Source = $absolute; Relative = $file })
}

Write-Output "模板：$source"
Write-Output "目标：$destinationRoot"
Write-Output "计划复制 $($copyPlan.Count) 个文件、创建 $($directoryPlan.Count) 个目录；排除缓存、构建产物和本地设置。"
Write-Output "项目模板：$Template"
Write-Output '身份配置：DevProject 的 PlayerSettings 将在首次 Unity 导入时应用。'
if ($DryRun) { Write-Output 'DryRun 完成，未写入文件。'; return }

# 目标非空检查先于任何写入；失败时保留部分副本供检查，不递归删除。
New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
foreach ($directory in $directoryPlan) {
    New-Item -ItemType Directory -Path (Join-Path $destinationRoot $directory) -Force | Out-Null
}
foreach ($entry in $copyPlan) {
    $target = Join-Path $destinationRoot $entry.Relative
    $parent = Split-Path -Parent $target
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    if (Test-Path -LiteralPath $target) { throw "目标文件已存在，拒绝覆盖：$target" }
    Copy-Item -LiteralPath $entry.Source -Destination $target
    if ([IO.Path]::GetExtension($target) -eq '.md') {
        $content = [IO.File]::ReadAllText($target)
        $content = $content.Replace($source, $destinationRoot).Replace($source.Replace('\', '/'), $destinationRoot.Replace('\', '/'))
        [IO.File]::WriteAllText($target, $content, (New-Object System.Text.UTF8Encoding($false)))
    }
}

if ($Template -eq 'Minimal') {
    foreach ($directory in @('config/excel', 'config/scaffold')) {
        New-Item -ItemType Directory -Path (Join-Path $destinationRoot $directory) -Force | Out-Null
    }
    $templateRoot = Join-Path $source 'tools/templates/minimal'
    foreach ($templateFile in Get-ChildItem -LiteralPath $templateRoot -File -Recurse) {
        $relative = $templateFile.FullName.Substring($templateRoot.Length).TrimStart('\', '/').Replace('\', '/')
        if (-not $relative.EndsWith('.template')) { continue }
        $relative = $relative.Substring(0, $relative.Length - '.template'.Length)
        $target = Join-Path $destinationRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $templateFile.FullName -Destination $target
    }
    Copy-Item -LiteralPath (Join-Path $source 'config/scaffold/example-request.json') -Destination (Join-Path $destinationRoot 'config/scaffold/example-request.json')
}
$templateIdentity = [ordered]@{ schemaVersion = 1; profile = $Template }
[IO.File]::WriteAllText((Join-Path $destinationRoot 'config/project-template.json'), ($templateIdentity | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$identity = [ordered]@{ projectName = $ProjectName; companyName = $CompanyName; applicationId = $ApplicationId; template = $Template }
$identityPath = Join-Path $destinationRoot '.project-identity.json'
[IO.File]::WriteAllText($identityPath, ($identity | ConvertTo-Json -Depth 2), (New-Object System.Text.UTF8Encoding($false)))
if (-not (Get-Command npm.cmd -ErrorAction SilentlyContinue)) {
    throw "项目文件已复制，但未找到 npm.cmd。安装 Node.js 后在 $destinationRoot 运行 npm ci --prefix tool，再打开 Unity。"
}
Write-Output '正在安装新项目的 Excel 导表依赖...'
& npm.cmd ci --prefix (Join-Path $destinationRoot 'tool') --no-audit --no-fund
if ($LASTEXITCODE -ne 0) {
    throw "项目文件已复制，但 npm ci 失败。请在 $destinationRoot 运行 npm ci --prefix tool 后打开 Unity。"
}
if ($Template -eq 'Minimal') {
    & node (Join-Path $destinationRoot 'tool/export-excel.mjs') export
    if ($LASTEXITCODE -ne 0) { throw '最小项目的空数据表入口生成失败。' }
}
Write-Output "已创建项目。用 Unity 2022.3.62f2 打开：$(Join-Path $destinationRoot 'DevProject')"
