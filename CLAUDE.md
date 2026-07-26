# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> 本仓库正在进行一次 **彻底重写**:把旧的 Python + PyQt5 版逐步移植为 C# / .NET 9。
> C# 新版是仓库主体,直接在根目录(`SmartOnmyoji.sln`、`src/`、`tests/`);旧 Python 版已整体归档到 `python-legacy/`,仅作行为参照,达到功能对齐后淘汰。
> **新功能一律写在根目录的 C# 项目里,不要再往 `python-legacy/` 加东西。**

## 权威文档

`docs/csharp-rewrite-design.md` 是重写的**单一事实来源**——范围、技术选型、分层、领域模型、
引擎循环、拟人化/防检测设计、配置与目标 schema、分阶段计划(P0–P8)、以及 **§11「当前进度」**(每完成一阶段就更新)。
改动前先读它;完成一个阶段后回来更新进度段。核心原则一句话:**行为对齐,实现重构——旧代码里不合理的逻辑不照搬**(见设计文档 §2.2 的「不照搬」清单)。

## 常用命令(C# 新版)

解决方案在根目录 `SmartOnmyoji.sln`。`Core`/`Vision`/测试是 `net9.0`,`Windows`/`Host` 是 `net9.0-windows`,**Host 只能在 Windows 上构建/运行**。

```bash
# 构建整个解决方案
dotnet build SmartOnmyoji.sln

# 跑全部单测(仅 Core.Tests,纯逻辑、无平台依赖)
dotnet test SmartOnmyoji.sln

# 跑单个测试类 / 单个测试方法
dotnet test SmartOnmyoji.sln --filter "FullyQualifiedName~RoundStateMachineTests"
dotnet test SmartOnmyoji.sln --filter "Name=Deviation_zero_returns_target"
```

### WebUI(P7,`args[0]` = `ui` / `serve`)

Host 现在是**进程内 Kestrel(仅 127.0.0.1)+ 静态 WebUI(`wwwroot/` 原生 HTML/JS)+ REST + SSE 事件流**的组合根;命令(前端→后端)走 REST,事件(后端→前端)走浏览器原生 `EventSource`(SSE,非 SignalR,见设计文档 §10 取舍)。

```bash
dotnet run --project src/SmartOnmyoji.Host -- ui          # 默认:开 WebView2 桌面窗口指向本地 Kestrel(需 WebView2 运行时)
dotnet run --project src/SmartOnmyoji.Host -- serve 8770  # headless / 浏览器模式:只起服务,浏览器开 http://127.0.0.1:8770(不依赖 WebView2,便于验证)
```

- **样式 = Tailwind v4(官方 standalone CLI,无 node)**:源 `Styles/app.tw.css`(`@import "tailwindcss"` + `@theme` 令牌 + `@layer components`)→ 生成**已提交**的 `wwwroot/tailwind.css`。`dotnet build` **默认不跑 CLI、直接用已提交产物**(构建不依赖工具链)。改样式后重新生成:`dotnet build src/SmartOnmyoji.Host -p:TailwindBuild=true`,或 `tools/tailwindcss.exe -i …/app.tw.css -o …/tailwind.css --minify --watch`(CLI 已 gitignore,官方 v4 standalone)。设计令牌 + 部件在 CSS,布局可在 HTML 直接写工具类。
- **前端**:原生 HTML/JS,无 node 前端构建,随 `dotnet build` 拷 `wwwroot` 到输出。两个页签:
  - **运行**:目标集下拉+缩略图、窗口枚举/多选/倒计时点选、运行参数表单、防检测折叠面板、启动/停止、实时日志(含**匹配缩略图**内联)+进度+回合。
  - **目标管理**(§9.3 写侧):输入玩法名建集、软件内截图 canvas **框选取模板**、逐图设 flag/优先级/删图、在截图上**点选归一化偏移点击点**,全部由软件托管写回 `target.json`(用户不碰 JSON)。
- **写侧全在 `Ui/TargetSetCatalog`**(建集/存图/写 json/删图/edit 对账),文件名/名称一律裸名校验 + 拦路径穿越。运行匹配缩略图:`TargetMatched` 带可空 `GrayThumbnail`(Core 灰度)→ `EngineManager` 编码 PNG 塞事件。
- **剩:WebView2 实机联调**(需桌面+提权驱动真实游戏)。⚠️ 引擎实跑会点击游戏,仍须**以管理员启动**(见下)。`serve`/纯读/目标管理接口无需提权。

### Host 冒烟命令(对真实游戏窗口手动验证 Win32/视觉环节)

`args[0]` 选命令。截图存到根目录 `.artifacts/`,模板从仓库 `img/<目录>/` 取。

```bash
dotnet run --project src/SmartOnmyoji.Host -- demo            # 假引擎跑 3 轮,验证事件流
dotnet run --project src/SmartOnmyoji.Host -- list            # 枚举可见窗口(句柄/pid/客户区尺寸/标题)
dotnet run --project src/SmartOnmyoji.Host -- capture "阴阳师"  # 后台截客户区并存 PNG
dotnet run --project src/SmartOnmyoji.Host -- match "阴阳师" yuling   # 自裁剪回配 + 对 img/yuling 逐图匹配 + 计时
dotnet run --project src/SmartOnmyoji.Host -- scalematch test        # 离线验证模板跨分辨率复用(不需要游戏窗口)
dotnet run --project src/SmartOnmyoji.Host -- click "阴阳师" yuling win_shengli  # 匹配后拟人化后台点击,并算前后画面差异
```

- 标题参数是**子串包含**匹配;省略则取当前前台窗口。
- **图片测试用 `img/yuling/` 目录**;其中 `start`、`win_shengli` 已知在旧版可用,适合做验证基准。
- ⚠️ `click` 会真的操作游戏、`P4` 起的循环会**反复点击**——涉及真机操作游戏的动作,执行前先跟用户确认。

### 旧版 Python(已归档到 `python-legacy/`,仅参照,一般不动)

```bash
cd python-legacy && uv sync && uv run smart_onmyoji_start.py     # 入口 smart_onmyoji_start.py,逻辑在 modules/
```

- `img/` 仍在仓库根(与 `python-legacy/` 同级,未随归档移动);`ModuleGetTargetInfo.py`/`smart_onmyoji_start.py` 里定位 `img/` 的路径已相应多回溯一层。

## 架构(C# 新版)

**四层,依赖单向指向 `Core`;`Core` 不依赖任何平台实现。** 这是整个重写的骨架,读多个文件才能拼出全貌:

```
Core (net9.0, 纯逻辑, 可离线单测)
 ├─ Abstractions/Interfaces.cs   四个接口: IWindowService / IScreenCapturer / IMatcher / IInputSender
 ├─ Engine/                      RoundStateMachine · EngineOptions · IAutomationEngine · FakeAutomationEngine
 ├─ Targets/Targets.cs           TargetFlag · TargetImage · ClickSpec · TargetSet
 ├─ Humanize/OffsetSampler.cs    拟人化点击偏移(截断正态 + 偏向窗口内侧 + 边界截断)
 ├─ Matching/ · Events/ · Primitives.cs · RecentBuffer.cs
      ▲                         ▲
Vision (net9.0)            Windows (net9.0-windows)
 OpenCvSharp4 实现          手写 P/Invoke 实现
 TemplateMatcher/ImageOps   WindowService · GdiWindowCapturer · SendMessageInput · DpiAwareness
      ▲                         ▲
      └──────── Host (net9.0-windows, 组合根: DI + 冒烟命令) ────────┘
```

`Windows`/`Vision` 各自实现 `Core` 的抽象;`Host` 用 `Microsoft.Extensions.DependencyInjection` 组装。四个接口都能换成假实现,所以引擎/状态机/采样器完全可离线单测。

### 贯穿全局的几个关键约定(不理解会踩坑)

- **统一客户区坐标系(物理像素)**:截图捕获客户区、匹配在客户区图上出点、点击用客户区坐标 `SendMessage`——三者同一坐标系。**因此彻底删掉了旧版 `cy = py + pos[1] - 40` 这类标题栏硬编码偏移。** 见设计文档附录 B。
- **零缩放**:旧版那套 DPI/压缩/`real_pos`+`scal_rate` 三层**坐标**缩放换算**全部省掉并已实测验证**。做法:① 启动即 `DpiAwareness.EnablePerMonitorV2()` 按物理像素捕获;② 默认不压缩(全分辨率 ~14ms/张够快);③ 点击坐标用**客户区归一化坐标(0~1)**(`ClickSpec` / `NormalizedPoint`),乘当前客户区尺寸即像素,天生分辨率无关。改这一块前务必先读设计文档 §2.2 / §9.2,别把坐标缩放逻辑加回来。
- **唯一保留的缩放:模板的分辨率适配**(§9.4,与上一条不冲突——缩的是**模板图像**,不是坐标)。`target.json` 每图记 `baseSize`(截该模板时的客户区尺寸),运行时客户区尺寸不同就把模板按比例缩放再 `matchTemplate`,**同一套模板跨分辨率复用,不必换分辨率重截图**。裁决在 `Core/Matching/TemplateScale.cs`(纯函数,拿不准一律退回 1.0 不缩放),重采样+缓存在 `TemplateMatcher`。这也是**不移植旧版 SIFT 特征匹配**的原因(理由与实测见 §9.4)。
- **事件驱动、引擎不知道 UI 存在**:引擎经 `Channel<EngineEvent>` 发结构化事件(`RoundStarted`/`TargetMatched`/`Clicked`/`Waiting`/`EngineStopped`/`LogMessage`…),取代旧版 `print("<br>")` 兼作日志+UI 的死耦合。
- **`RoundStateMachine` 收拢旧 flag 逻辑**:旧 `img_pos.json` 的 flag `""`/`start`/`mark`/`skip`/`stop` → 枚举 `Normal`/`RoundStart`/`Once`/`Skip`/`Stop`,加「连续 N 次同名即终止」的卡死保护,集中一处裁决、逐分支单测。
- **协作式取消**:`CancellationToken` 取代旧 `QThread.terminate()`。
- **中文路径**:`ImageOps` 用 `Cv2.ImDecode(File.ReadAllBytes(...))` 读图,别用会踩中文路径的 `Cv2.ImRead`。
- **P/Invoke 手写**:`SmartOnmyoji.Windows/NativeMethods.cs` 手写 `DllImport` 起步(透明可控),设计文档留了后续迁 CsWin32 源生成的口子。
- **后台点击序列**:`SendMessageInput` 沿用旧版验证可用的 `WM_ACTIVATE + WM_LBUTTONDOWN/UP`,lParam 为客户区坐标、按下时长随机。`ForegroundInputSender` 是前台"兼容模式"备选(`SetForegroundWindow`+真光标+`mouse_event`)。
- **⚠ 必须以管理员运行**:阴阳师进程以管理员权限运行,未提权进程的合成输入(`SendMessage`/`mouse_event`)会被 Windows **UIPI 静默丢弃**(`SendMessage` 还照样返回成功,极具迷惑性)。这是"后台点击没反应"的真正原因,**与系统缩放无关**(我们的 DPI/坐标处理是对的)。开发期要驱动游戏的命令(`click`/`fgclick`/`run`)必须在**提权终端**里跑,或用 `Start-Process -Verb RunAs`;不触及游戏的命令(单测/`list`/`capture`/`match`/`geom`)无需提权。发行版应加 `app.manifest` 的 `requireAdministrator` 或启动自提权。
- **诊断命令**:`geom <窗口>` 打印客户区/边框/标题栏/DPI/感知级别;`children <窗口>` 列子窗口;选窗支持 `0x<句柄>` 精确指定(解决多开同名取窗不确定)。

### 当前移植进度(细节以设计文档 §11 为准)

- **P0–P3 已完成并对真实游戏(阴阳师,1200×600)实测通过**:后台截图、模板匹配+坐标、后台点击、拟人化偏移、回合状态机全部跑通。
- **P4 已完成**:真 `AutomationEngine`(Core.Engine)串起 截图→`MatchFirst`→状态机裁决→拟人化点击→间隔 循环,替换了假引擎,并以管理员对真实游戏跑通 `run` 冒烟。
- **P5 已完成**:`AntiDetectionPolicy`(概率随机等待 + 时间窗口频次上限,按 RoundStart 计数、轮末组级施加、双开安全);策略 + 引擎接线单测全绿。
- **P6 已完成**:类型化 `EngineOptions` + 校验(`Core/Config/EngineOptionsValidation.cs`);`target.json` schema + 序列化/加载(`Core/Targets/TargetSetJson.cs`、`TargetSetLoader.cs`);`img_pos.json`→`target.json` 纯函数导入器(`Core/Targets/ImgPosImporter.cs`,flag 映射 + 文件名排序优先级 + 缺图告警;**旧 click_pos/real_pos 坐标不导入**——经确认几乎全错、坐标系不兼容,`click` 留空 → 点匹配中心)。Host 新增 `import <目录>` 命令;`run` 目标集加载三级优先(`target.json` → 旧 `img_pos.json` 内存导入补齐 flag → 目录扫描兜底),**旧目标集导入即用**。**全解决方案 72 单测全绿**。
- **P7 进行中**:WebUI 两切片跑通。Host 升级为**进程内 Kestrel + 静态 WebUI(原生 HTML/JS,`src/SmartOnmyoji.Host/wwwroot/`)+ REST + SSE**,WebView2 桌面窗口承载(`Ui/WebUiApp`、`MainForm`、`EngineManager`、`ApiEndpoints`、`TargetSetCatalog`、`Contracts`)。`ui`(WebView2)/`serve`(headless)命令;事件流用 **SSE 非 SignalR**(设计文档 §10/§11 取舍)。
  - **切片一(运行)**:目标集/窗口/参数/防检测表单、启动停止、实时日志+进度+回合。
  - **切片二**:①**目标管理写侧(§9.3)**——建集 / 软件内截图 canvas 框选取模板 / 逐图设 flag·删图 / 截图上点选归一化偏移点、`target.json` 由软件托管写回;优先级不再手填,改为**按图片列表顺序自动设置 + 拖动排序**(手柄触发的 HTML5 drag-and-drop);另有「打开文件夹」按钮(`POST /api/targets/{name}/open-folder`)在资源管理器打开该目标集的磁盘目录。写侧全在 `TargetSetCatalog`(裸名校验 + 拦路径穿越)。②**运行匹配缩略图**——`TargetMatched` 带可空 `GrayThumbnail`,`EngineManager` 编码 PNG 塞 `matched` 事件,前端日志内联。
  - 已 headless+curl 实测两切片全链路(建集/存图/edit 对账/PUT 持久化/删图/缩略图/校验/SSE),**72 单测仍全绿**。**WebView2 外壳已真机启动、功能大致正常(用户 2026-07-22 确认)**——§3 主推路线端到端跑通。
- **下一步**:P7 按真机使用反馈打磨 → P8 打包(单文件+Trimming、评估 NativeAOT、`app.manifest` 提权)。
- **模板跨分辨率复用已完成(2026-07-26)**:见上「唯一保留的缩放」与设计文档 §9.4;`scalematch` 冒烟命令可离线回归。旧目标集(没记 `baseSize`)行为不变,**重新截一次模板即获得跨分辨率能力**。
- 已知待办:多开时 `Enumerate` 顺序不稳定(拟改为可显式指定句柄);OpenCvSharp 的 NativeAOT 打包兼容性尚未验证;诱饵点击/时段警告(`DecoyClick`/`PlaytimeWarning`)留作可选后续补;需偏移点击的图后续在 `target.json` 手动补客户区归一化坐标;**每图 `Hint=Feature` 目前静默失效**——注入的 `TemplateMatcher` 不看 `options.Method`,要用特征匹配需先补按 `Method` 分发的 `CompositeMatcher`。
