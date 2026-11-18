[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SdkArchive,
    [string] $SourceDirectory = 'C:\Dev\Tools\gd_cubism-v0.9.1',
    [switch] $BuildRelease
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$archive = (Resolve-Path -LiteralPath $SdkArchive).Path
$python = Join-Path $source '.venv\Scripts\python.exe'
$thirdParty = Join-Path $source 'thirdparty'
$cubismSourceAddon = Join-Path $source 'demo\addons\gd_cubism'
$cubismProjectAddon = Join-Path $repo 'addons\gd_cubism'
$compatibilityPatch = Join-Path $PSScriptRoot 'gd_cubism-r5.patch'

if (!(Test-Path -LiteralPath $python)) {
    throw "找不到 gd_cubism 的 SCons 环境：$python"
}

Expand-Archive -LiteralPath $archive -DestinationPath $thirdParty -Force
if (!(Test-Path -LiteralPath (Join-Path $thirdParty 'CubismSdkForNative-5-r.5\Core\include\Live2DCubismCore.h'))) {
    throw '请使用 Cubism Native SDK 5 R5 压缩包。'
}

& git -C $source apply --reverse --check $compatibilityPatch 2>$null
if ($LASTEXITCODE -ne 0) {
    & git -C $source apply $compatibilityPatch
    if ($LASTEXITCODE -ne 0) {
        throw '无法把 Native SDK R5 兼容补丁应用到 gd_cubism v0.9.1。'
    }
}

$targets = @('template_debug')
if ($BuildRelease) {
    $targets += 'template_release'
}
Push-Location -LiteralPath $source
try {
    foreach ($target in $targets) {
        & $python -m SCons platform=windows arch=x86_64 "target=$target" -j8
        if ($LASTEXITCODE -ne 0) {
            throw "gd_cubism 编译失败：$target（退出码 $LASTEXITCODE）"
        }
    }
} finally {
    Pop-Location
}

New-Item -ItemType Directory -Path $cubismProjectAddon -ErrorAction SilentlyContinue | Out-Null
$projectBin = Join-Path $cubismProjectAddon 'bin'
New-Item -ItemType Directory -Path $projectBin -ErrorAction SilentlyContinue | Out-Null
foreach ($target in $targets) {
    $configuration = $target.Substring('template_'.Length)
    $library = Join-Path $cubismSourceAddon "bin\libgd_cubism.windows.$configuration.x86_64.dll"
    Copy-Item -LiteralPath $library -Destination $projectBin -Force
}
foreach ($entry in @('res', 'gd_cubism.gdextension')) {
    Copy-Item -LiteralPath (Join-Path $cubismSourceAddon $entry) -Destination $cubismProjectAddon -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $source 'LICENSE.en.adoc') -Destination $cubismProjectAddon -Force
