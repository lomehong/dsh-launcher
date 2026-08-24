# DeepSeek Harness (dsh) 一键启动器 —— Windows 通用版

一个给不懂命令行的人用的「双击即用」启动器。任何 **Windows 10 / 11（64 位）** 电脑，
把 `dsh一键启动.exe` 复制过去，**双击**即可自动完成安装并启动，全程无需打开命令行、
无需管理员权限、不会影响电脑上已有的 Node.js。

启动器会默认安装 **9 个社区插件**（at-file、genui、visualize、automation、better-sidebar、
mnemon、vision-toolkit、market、yuyi-御驿），GitHub 源直连不可达时自动切换国内代理，装好即可用。
yuyi 需现场构建，并自动配好含 17 个 `yuyi_*` 工具的会话 preset（standard-yuyi，设为默认）。
检测到用户手动/开发安装的插件（profile 依赖以 `link:`/`file:` 指向启动器目录之外，如 yuyi 链到
开发检出）会尊重现状、不替换源码；但其 `node_modules\@deepseek-ai` 会被自动统一为指向便携
harness 依赖集的 junction（幂等）——否则检出自带的依赖副本会与 harness 形成双实例，导致插件
宿主半边挂载失败、`/api/<ns>/*` 全部 404。在开发检出里跑过 `pnpm install` 后重跑启动器即可自愈。

> **v1.6.0 修复**：Job Object 进程隔离真正生效（web 进程启动即入 job，关窗/退出即整树终止）· 下载改为 HttpClient（连接超时 30s + 整体看门狗 10 分钟，弱网不再无限等待；`.part` 断点续传真实可用）· plugins.json 条目（id/pkgName/source）白名单校验，拒绝 cmd 元字符注入 · `--check --json` 拆分 `healthy`（环境完整）与 `webRunning`（服务在跑）语义 · 单实例锁改为按用户（Local），多 Windows 用户可各自运行 · monorepo 回退装包时显式告警并提示 repoSub。
>
> **v1.4.0 新增**：结构化日志（7 天滚动）+ `--check --json` 机器可读自检 + `config.json` 替代 `config.txt`（自动迁移）+ 单实例锁 + Job Object 隔离子进程 + SHA256 校验（Node zip）+ TLS 1.3 + 语义化退出码 + 可通过 `plugins.json` 自定义默认插件 + 通过 GitHub API 探测默认分支自动重试。

## 文件说明

| 文件 | 作用 |
| --- | --- |
| `dsh-launcher-gui.exe` | **图形界面主程序**（WPF，双击即用；同目录需有同名 dll/json 支撑文件） |
| `dsh-launcher.exe` / `dsh一键启动.exe` | 控制台版（同一程序的双名副本；`--check`/`--update`/`--uninstall` 等子命令见下） |
| `build.bat` | 一键重建 CLI + GUI（需 .NET SDK；产物拷贝到仓库根目录） |
| `环境自检.bat` | 双击运行：检查 Node.js / dsh 安装状态、端口占用、最新版本 |
| `升级dsh.bat` | 双击运行：手动把 dsh 升级到最新版（平时也会每天自动检查更新） |
| `卸载dsh.bat` | 双击运行：删除启动器自带环境（保留用户 ~/.dsh；加 `--purge` 一并清空） |
| `DshLauncher.sln` | 解决方案：`src/DshLauncher.Core`（业务库）+ `src/DshLauncher.Cli`（控制台）+ `src/DshLauncher.Gui`（WPF）+ `tests/`（154 个 xUnit 测试） |
| `fix-yuyi-dev-deps.ps1` | 修复 yuyi 开发检出在便携 dsh 下的 /api/yuyi/* 404（重建 @deepseek-ai junction） |

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

> 关闭启动器窗口 = 停止 dsh web 服务（启动器使用 Windows Job Object 隔离进程树：web 进程启动后即加入 job，launcher 退出/被杀时内核自动终止整棵进程树，含 node 子进程）。GUI 最小化到托盘期间服务不受影响，托盘「退出」时一并停止。
> 错误与警告同时写入 `%LOCALAPPDATA%\dsh-launcher\logs\launcher-YYYY-MM-DD.log`（保留 7 天），关窗后仍可复盘。

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
一般是没有网络或网络不稳定。检查网络后重新双击即可：已下载完整的文件会直接跳过，
下载到一半的（`.part` 文件）下次会从断点续传（服务器不支持续传时自动整档重下）。
连接级超时 30 秒、整体 10 分钟，弱网下不会再无限卡住。
如果公司网络有代理，请先在系统里配置好代理，或修改 npm 源（见下）。

**4. 想换回官方 npm 源或使用其他源？**
在 `%LOCALAPPDATA%\dsh-launcher\config.json` 里写一项（没有就新建）：
```json
{ "registry": "https://registry.npmjs.org" }
```
也可以设置环境变量 `DSH_REGISTRY`，环境变量优先级更高。v1.4.0 已自动把旧的 `config.txt` 迁移到 `config.json`。

**4b. 插件从 GitHub 下载太慢 / 想指定代理？**
默认会先直连 GitHub（codeload），失败后自动尝试内置国内代理列表
（ghfast.top、gh-proxy.com、mirror.ghproxy.com、ghproxy.net）。
也可在 config.json 指定固定代理（替换整个内置列表）：
```json
{ "githubProxy": "https://ghfast.top/" }
```
或设置环境变量 `DSH_GITHUB_PROXY`，优先级更高。

**5. 如何完全卸载？**
双击 `卸载dsh.bat`（或命令行 `dsh-launcher.exe --uninstall --yes`）。
默认只删除 `%LOCALAPPDATA%\dsh-launcher`（便携 Node + dsh + 启动器数据），**保留 `~/.dsh`**（用户 profile、会话历史）。
要一并清空用户数据，加 `--purge`（或编辑 `卸载dsh.bat`）。
启动器不写注册表、不装系统服务、不改系统 PATH。

**6. 它会影响电脑上原有的 Node.js 吗？**
不会。启动器始终使用自己下载的便携版 Node.js，与系统已有的 Node 完全隔离。

**7. 启动后没自动打开浏览器？**
手动在浏览器打开 http://127.0.0.1:3080 即可。若仍打不开，双击 `环境自检.bat` 看端口状态。

**8. 想自定义/减少默认安装的插件？**
在 `%LOCALAPPDATA%\dsh-launcher\plugins.json` 里写：
```json
{
  "plugins": [
    { "id": "dsh-yuyi", "display": "yuyi", "pkgName": "dsh-yuyi", "viaNpm": false, "source": "lomehong/dsh-yuyi/main", "required": true }
  ]
}
```
只列你想要的插件即可；缺省或解析失败时回退内置 9 个默认。`required: false` 的插件缺失不会报错。

**9. 想保护外部/开发检出不被启动器改动？**
设置环境变量 `DSH_PROTECT_EXTERNAL=1`（或在 config.json 写 `"protectExternal": true`）。
默认启动器会把 `link:`/`file:` 进来的开发检出的 `node_modules/@deepseek-ai` 接管为 junction，
避免双实例导致 `/api/<ns>/*` 404。opt-out 后这部分由用户自己负责。

**10. 想严格校验下载完整性？**
在 config.json 写 `"integrity": "strict"`。`lax`（默认）只在有 SHA 时校验；`none` 完全跳过；
`strict` 校验失败会回退下一个镜像。

## 命令参数

```
dsh-launcher.exe --update        仅检查并更新 dsh（不启动 web）
dsh-launcher.exe --install-only  仅安装/更新环境（不启动 web）
dsh-launcher.exe --uninstall     删除启动器自带环境（保留 ~/.dsh；加 --purge 一并清空）
dsh-launcher.exe --uninstall --yes   跳过交互确认（非交互/CI 用）
dsh-launcher.exe --uninstall --purge  清空包括用户 profile + 插件的所有数据
```

| 码 | 含义 |
| --- | --- |
| 0 | 成功 |
| 10 | 网络失败（下载全部镜像失败） |
| 20 | Node.js 准备失败 |
| 30 | dsh 安装/更新失败 |
| 40 | 插件安装部分失败 |
| 50 | 配置/参数错误 |
| 60 | 内部异常 |
| 70 | 已有 dsh-launcher 在运行 |

## 配置（%LOCALAPPDATA%\dsh-launcher\config.json）

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `registry` | URL | `https://registry.npmmirror.com` | npm 源 |
| `githubProxy` | URL | 空 | GitHub 代理前缀（必须 `https://` 且以 `/` 结尾）。空 = 用内置代理列表 |
| `integrity` | enum | `lax` | `strict` / `lax` / `none` —— 下载完整性校验严格度 |
| `protectExternal` | bool | `false` | `true` = 不接管外部 link/file 检出的 `@deepseek-ai` |
| `logLevel` | enum | `info` | `silent` / `info` / `verbose` |
| `pinnedNodeVersion` | semver | `v24.19.0` | 镜像索引失败时的兜底 Node 版本 |
| `sharedCoreSpecs` | object | 空 | harness 核心依赖版本钉子逐包覆盖（如 `{ "dsh-llm": "0.2.0", "cordis": "4.1.0-rc.1" }`）。空 = 用内置默认；上游 rc tag 失效时改这里即可修复自愈路径，无需发新版启动器 |


## 技术原理（简述）

- 启动器分两层：`DshLauncher.Core`（net10.0 业务库）+ 两个宿主——WPF 图形界面（`dsh-launcher-gui.exe`）与控制台（`dsh-launcher.exe`）。构建需 .NET SDK，运行需 .NET 8+ 运行时；所有子进程（npm/pnpm/dsh）在 GUI 模式下静默重定向进内置日志面板。
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

双击 `build.bat`，或：

```powershell
dotnet publish src/DshLauncher.Cli/DshLauncher.Cli.csproj -c Release -f net10.0-windows -r win-x64 --self-contained false -o dist/cli
dotnet publish src/DshLauncher.Gui/DshLauncher.Gui.csproj -c Release -f net10.0-windows -r win-x64 --self-contained false -o dist/gui
```

- 需要 .NET SDK（8.0+；本项目在 10.0 上开发验证）。
- 产物是 **apphost 模式**：exe 必须与同名 `.dll` / `.deps.json` / `.runtimeconfig.json` 及 `DshLauncher.Core.dll` 放同一目录。`build.bat` 会把它们一并拷到仓库根目录。
- 项目结构：`src/DshLauncher.Core`（业务逻辑库，net10.0）、`src/DshLauncher.Cli`（控制台入口）、`src/DshLauncher.Gui`（WPF 图形界面）、`tests/`（xUnit）。构建产物与 exe 均不入 git，克隆后先跑 `build.bat`。

## 运行单元测试

```powershell
dotnet test
```

当前共 **154 个 xUnit 测试**，覆盖：
- JSON 解析器（嵌套对象、字符串转义、拒绝尾垃圾、布尔变体）
- 版本号提取与比较（含 prerelease）
- npm warn 行过滤
- registry / proxy URL 白名单校验
- `link:` / `file:` 依赖路径解析（含 `E://code//...` 双斜杠）
- 默认 9 个插件完整性校验
- ProfileHasPlugin 嵌套 bundles 判定（回归测试）
- Uninstaller 集成测试（隔离 temp dir 验证 `--yes`/`--purge`/取消/无操作）
- SHASUMS256.txt 解析（含 `*` 可执行标记、CRLF/LF）
- PluginInputParser（GitHub URL / SSH / owner-repo / scoped npm 包名）
- **Job Object 隔离**（进程入 job、Dispose 即终止进程树含孙进程——锁死"关窗即停"契约）
- **注入白名单**（IsSafePkgName / IsSafeSlug 拒绝 cmd 元字符；plugins.json 恶意条目被 Load 过滤；内置 9 插件全量过白名单回归）
- RunCapture 退出码透传（"命令失败"不再被误读为"未安装"）
- `--check --json` 语义（healthy 与 webRunning 分离：环境齐全 + 服务在跑 = healthy）
- sharedCoreSpecs 钉子外部化（config 覆盖默认 rc 版本；注入形态被拒；未知包名无害）
