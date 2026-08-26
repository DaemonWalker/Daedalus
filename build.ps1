# Daedalus 构建脚本：编译整个解决方案（src + plugins + tests）。
# 插件工程的生成后事件会把产物自动部署到 App 输出目录 plugins/，构建即得可运行目录。
#
# 用法：
#   ./build.ps1                     # Debug 构建（默认）
#   ./build.ps1 -Configuration Release
#   ./build.ps1 -Clean              # 先清理 bin/obj 再构建
#   ./build.ps1 -Configuration Release -Clean

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$sln = Join-Path $root 'Daedalus.sln'

if ($Clean) {
    Write-Host "清理 bin/obj ..." -ForegroundColor Cyan
    Get-ChildItem -Path $root -Recurse -Directory -Include bin, obj |
        Remove-Item -Recurse -Force
}

Write-Host "构建 Daedalus.sln（$Configuration）..." -ForegroundColor Cyan
dotnet build $sln --nologo -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$appOut = Join-Path $root "src\Daedalus.App\bin\$Configuration\net10.0-windows"
Write-Host ""
Write-Host "完成。App 输出目录：$appOut" -ForegroundColor Green
if (Test-Path (Join-Path $appOut 'plugins')) {
    Get-ChildItem (Join-Path $appOut 'plugins') -Filter *.dll |
        ForEach-Object { Write-Host "  插件：$($_.Name)" }
}
