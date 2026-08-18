# DeepSeek Harness (dsh) 一键启动器 —— Windows 通用版

一个给不懂命令行的人用的「双击即用」启动器。任何 **Windows 10 / 11（64 位）** 电脑，
把 `dsh一键启动.exe` 复制过去，**双击**即可自动完成安装并启动，全程无需打开命令行、
无需管理员权限、不会影响电脑上已有的 Node.js。

启动器会默认安装 **9 个社区插件**（at-file、genui、visualize、automation、better-sidebar、
mnemon、vision-toolkit、market、yuyi-御驿），GitHub 源直连不可达时自动切换国内代理，装好即可用。
yuyi 需现场构建，并自动配好含 17 个 `yuyi_*` 工具的会话 preset（standard-yuyi，设为默认）；
检测到用户手动/开发安装的 yuyi（依赖指向启动器目录之外）会尊重现状跳过，不替换。

## 文件说明

| 文件 | 作用 |
| --- | --- |
| `dsh一键启动.exe` | 主程序，**双击它即可启动**（可单独复制到别的电脑使用） |
| `dsh-launcher.exe` | 同一个程序的英文名副本，功能完全一样 |
| `环境自检.bat` | 双击运行：检查 Node.js / dsh 安装状态、端口占用、最新版本 |
| `升级dsh.bat` | 双击运行：手动把 dsh 升级到最新版（平时也会每天自动检查更新） |
| `src\DshLauncher.cs` | 源码（可自行修改重新编译，见文末） |

## 使用方法

1. 把整个文件夹复制到目标电脑（推荐，三个入口都能用）。
   - 只想复制一个文件的话，复制 `dsh一键启动.exe` 即可正常双击启动，
     但此时「环境自检 / 升级dsh」两个 .bat 入口不可用（它们依赖 `dsh-launcher.exe`）。
2. **双击 `dsh一键启动.exe`**。
3. 首次运行会自动完成（约 5~10 分钟，视网速而定）：
   - 从国内镜像下载**便携版 Node.js**（约 35MB，只装在当前用户的 `%LOCALAPPDATA%\dsh-launcher` 下）；
   - 用国内 npm 镜像安装 `@deepseek-ai/dsh`；
   - 安装 **9 个默认插件**（见下）；
   - 启动 `dsh web` 并**自动打开浏览器**界面（默认地址 http://127.0.0.1:3080）。
4. 以后每次双击：几秒内即可启动，并自动检查 dsh 是否有新版本（每天最多一次）。

> 关闭启动器窗口 = 停止 dsh web 服务。窗口里会实时显示进度和日志。

## 默认插件

| 插件 | 来源 | 说明 |
| --- | --- | --- |
| dsh-at-file | GitHub 源码 | 文件处理增强 |
| dsh-genui | GitHub 源码 | dsh-ui 渲染能力 |
| dsh-visualize | GitHub 源码 | 可视化 |
| dsh-automation | GitHub 源码 | 定时任务自动化 |
| dsh-better-sidebar | npm 源 | 侧边栏增强 |
| dsh-mnemon | npm 源 | 记忆桥接 |
| dsh-vision-toolkit | npm 源 | 视觉工具集 |
| dsh-market | npm 源 | 插件市场（@dsh-market/plugin） |
| dsh-yuyi | GitHub 源码 | 御驿跨会话通信（17 个 yuyi_* 工具 + Web 标签页；需现场构建） |

- 安装方式：npm 源插件直接从国内镜像（npmmirror）安装；GitHub 源码插件下载仓库 zip
  （直连 codeload，失败自动尝试 `ghfast.top` / `gh-proxy.com` 等国内代理），自动装依赖、
  缺构建产物时自动构建，再注册进 web profile。
- **yuyi 连接配置**：插件默认休眠，需配置 hub/token 才连接——网页「设置」里配置（推荐），
  或环境变量 `YUYI_HUB` / `YUYI_TOKEN`，或 `~/.yuyi/env` 文件。装好后自动创建
  `standard-yuyi` 会话 preset（内置 standard + yuyi 工具行）并设为默认。
- 每次启动会检查是否齐全，缺了自动补装；已安装的跳过（秒过）。
- 想卸载某个插件：命令行执行 `dsh plugin --profile web remove <包名>` 即可。

## 自动更新与自检

- **自动更新**：每次启动时若距上次检查超过 24 小时，会自动向 npm 查询最新版 dsh，发现新版本自动升级。
- **手动升级**：双击 `升级dsh.bat`。
- **环境自检**：双击 `环境自检.bat`，检查 Node.js / dsh 状态、端口 3080 是否被占用、最新版本号。

## 常见问题

**1. 首次双击后弹出「Windows 已保护你的电脑」？**
程序没有数字签名，Windows SmartScreen 会提示。点「更多信息」→「仍要运行」即可（只在首次出现）。

**2. 提示「端口 3080 已被占用」？**
说明已经有一个 dsh web（或其他程序）在运行 3080 端口。如果页面能打开就直接用；
如果页面不对，请先关闭占用该端口的程序，再重新双击启动器。

**3. 安装失败 / 下载失败？**
一般是没有网络或网络不稳定。检查网络后重新双击即可（已下载的部分会自动跳过）。
如果公司网络有代理，请先在系统里配置好代理，或修改 npm 源（见下）。

**4. 想换回官方 npm 源或使用其他源？**
在 `%LOCALAPPDATA%\dsh-launcher\config.txt` 里写一行（没有就新建）：
```
registry=https://registry.npmjs.org
```
也可以设置环境变量 `DSH_REGISTRY`，环境变量优先级更高。

**4b. 插件从 GitHub 下载太慢 / 想指定代理？**
默认会先直连 GitHub（codeload），失败后自动尝试内置国内代理列表
（ghfast.top、gh-proxy.com、mirror.ghproxy.com、ghproxy.net）。
也可在 config.txt 指定固定代理（替换整个内置列表）：
```
githubProxy=https://ghfast.top/
```
或设置环境变量 `DSH_GITHUB_PROXY`，优先级更高。

**5. 如何完全卸载？**
关掉启动器窗口，删除整个 `%LOCALAPPDATA%\dsh-launcher` 文件夹即可。
启动器不写注册表、不装系统服务、不改系统 PATH。

**6. 它会影响电脑上原有的 Node.js 吗？**
不会。启动器始终使用自己下载的便携版 Node.js，与系统已有的 Node 完全隔离。

**7. 启动后没自动打开浏览器？**
手动在浏览器打开 http://127.0.0.1:3080 即可。若仍打不开，双击 `环境自检.bat` 看端口状态。

## 技术原理（简述）

- 启动器是一个 C# 控制台程序，用系统自带的 .NET Framework 编译，任何 Windows 10/11 都能直接运行，无需安装运行库。
- 便携版 Node.js 从 npmmirror 镜像下载（失败自动切换 nodejs.org 官方源），解压到 `%LOCALAPPDATA%\dsh-launcher\node`。
- `npm install -g @deepseek-ai/dsh` 安装到便携 Node 目录内（用户目录，不需要管理员权限），npm 缓存也在 `%LOCALAPPDATA%\dsh-launcher\npm-cache`。
- 默认插件：npm 源插件用 `dsh plugin --profile web add <包名>`（走 npmmirror）；
  GitHub 源插件下载仓库 zip（直连失败走国内代理）→ 解压到 `plugins\<id>` → 删除提交的
  lockfile、去掉 devDependencies 后 `pnpm install` 装运行依赖 → 缺 `lib/` 自动 `pnpm build`
  → `dsh plugin add <目录>` 以链接方式注册（自动加入 bundle 层）。
- **peer 依赖**：插件的 `@deepseek-ai/*` peer 由 junction 提供——把插件目录和 profile 的
  `node_modules/@deepseek-ai` 链接到便携 dsh 包自己的 `@deepseek-ai` 依赖集，与 harness
  解析到同一份物理包（无需从 registry 装 peers，避免撞上未发布包如
  `dsh-type-meta`/`dsh-compact` 的 404；也避免 Typert 标记双实例导致客户端 Remote 404）。
- 启动 `dsh web` 后轮询 3080 端口，就绪即自动打开默认浏览器。

## 从源码重新编译（可选）

- 双击 `build.bat` 即可重建（使用 Windows 自带的 .NET Framework 编译器，产出 `dsh-launcher.exe`）。
- 或在 PowerShell 中执行：

```powershell
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /codepage:65001 /optimize+ /target:exe `
    /out:'dsh-launcher.exe' /r:System.IO.Compression.FileSystem.dll src\DshLauncher.cs
copy /y dsh-launcher.exe 'dsh一键启动.exe'
```
