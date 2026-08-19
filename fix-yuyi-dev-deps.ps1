# 修复 dsh-yuyi 开发检出在便携 dsh 运行时下的 /api/yuyi/* 404：
# 开发检出（E:\code\nodejs\Yuyi\adapters\dsh-yuyi）里 pnpm install 会自装一套真实的
# @deepseek-ai 包（如 rc.6），与启动器便携 dsh 运行时（rc.7）形成 typert-protocol
# 双实例，宿主半边挂载失败 -> /api/yuyi/status 404、御驿 Tab "未配置"、设置区块"读取中"。
#
# 修法：把 <插件>\node_modules\@deepseek-ai 换成指向便携 dsh 包自带 @deepseek-ai
# 依赖集的 junction（与启动器对自身插件副本的处理一致）。
#
# 什么时候需要重跑：
#   - 开发检出里执行过 pnpm install（会还原真实目录）
#   - 启动器升级/更换了 Node 目录（junction 目标路径变了）
# 跑完后重启 dsh web（关掉启动器控制台重开，或重新运行 dsh一键启动.exe）。
param(
    [string]$PluginDir = 'E:\code\nodejs\Yuyi\adapters\dsh-yuyi'
)

$ErrorActionPreference = 'Stop'

# 1. 定位便携 dsh 自带的 @deepseek-ai 依赖集（取最新 node-v*-win-x64）
$runtimeRoot = Join-Path $env:LOCALAPPDATA 'dsh-launcher\node'
$target = $null
$nodeExe = $null
$nodeDirs = Get-ChildItem $runtimeRoot -Directory -Filter 'node-v*-win-x64' -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending
foreach ($nd in $nodeDirs) {
    $t = Join-Path $nd.FullName 'node_modules\@deepseek-ai\dsh\node_modules\@deepseek-ai'
    if (Test-Path (Join-Path $t 'dsh-typert-protocol\package.json')) {
        $target = $t
        $nodeExe = Join-Path $nd.FullName 'node.exe'
        break
    }
}
if (-not $target) {
    Write-Error "未在 $runtimeRoot 下找到便携 dsh 的 @deepseek-ai 依赖集，请先运行启动器完成安装。"
    exit 1
}

# 2. 幂等：junction 已指向正确目标且功能有效则直接通过
$link = Join-Path $PluginDir 'node_modules\@deepseek-ai'
$item = $null
if (Test-Path $link) { $item = Get-Item $link -Force }
if ($item -and $item.LinkType -eq 'Junction' -and $item.Target -eq $target -and
    (Test-Path (Join-Path $link 'cordis\package.json'))) {
    Write-Host "[OK] junction 已正确，无需修复："
    Write-Host "     $link -> $target"
    exit 0
}

# 3. 删除真实目录/旧 junction 并重建
if (Test-Path $link) { Remove-Item $link -Recurse -Force }
New-Item -ItemType Junction -Path $link -Target $target | Out-Null
Write-Host "[FIX] 已重建 junction："
Write-Host "      $link -> $target"

# 4. 功能校验（目录存在性 + Node 实际解析版本）
foreach ($check in @('cordis\package.json', 'dsh-typert-protocol\package.json', 'dsh-settings\package.json')) {
    if (-not (Test-Path (Join-Path $link $check))) {
        Write-Error "junction 校验失败：$check 不存在"
        exit 1
    }
}
$reqJs = "const {createRequire}=require('module');const path=require('path');" +
    "const rq=createRequire(process.argv[1]+'/lib/src/service.js');" +
    "const spec=rq.resolve('@deepseek-ai/dsh-typert-protocol');" +
    "let d=path.dirname(spec);for(let i=0;i<5;i++){try{console.log(createRequire(d)(path.join(d,'package.json')).version);break}catch{}d=path.dirname(d)}"
$ver = & $nodeExe -e $reqJs "$PluginDir"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Node 解析 @deepseek-ai/dsh-typert-protocol 失败"
    exit 1
}
Write-Host "[OK] 插件视角解析 dsh-typert-protocol => $ver（应与便携 dsh 版本一致）"
Write-Host "完成。请重启 dsh web 后验证御驿 Tab。"
