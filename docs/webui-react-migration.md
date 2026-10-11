# WebUI 重写方案:原生 HTML/JS → Vite + React + TypeScript

> 状态:**已完成(2026-10-11)**。旧前端已删除,`dotnet build` 构建并提供的就是新前端,§7、§8 全部验证通过。
> 本文留作这次重写的记录;日常开发看 `CLAUDE.md` 的 WebUI 一节,结论摘要在 `csharp-rewrite-design.md` §11。

## 1. 目标与范围

把 `src/SmartOnmyoji.Host/wwwroot/` 下的原生前端(`index.html` 303 行 + `js/` 5 个模块约 1100 行 + `Styles/app.tw.css` 326 行)重写为 Vite + React + TypeScript 应用。

**在范围内**

- 运行页、目标管理页、截图编辑器、点击分析弹窗的全部现有功能,行为对齐(验收清单见 §7)。
- 构建接线:`dotnet build` / `dotnet publish` 仍是一条命令。
- 样式从手写部件类迁到 shadcn/ui 组件 + 语义 token。

**不在范围内**

- REST 路由、SSE 事件格式、`Contracts.cs` 的 DTO 形状:全部不变,后端 C# 代码零改动(csproj 除外)。
- 新功能。重写期间发现的旧行为缺陷只修 §6 列出的那一项,其余记下来另行处理。
- WebView2 外壳(`MainForm`)、端口选择、提权逻辑。

## 2. 技术栈

| 用途 | 选型 | 说明 |
|---|---|---|
| 包管理 / 脚本 | bun | 本机已装 1.4.2 |
| 构建 | Vite 8 + `@vitejs/plugin-react` | 本机 Node v24.20,满足 Vite 8 要求 |
| 语言 | TypeScript(strict) | |
| UI | React + shadcn/ui(Base UI)+ lucide-react | 基础组件一律 `bunx shadcn add` |
| 样式 | Tailwind v4,经 `@tailwindcss/vite` | 取代 `tools/tailwindcss.exe` standalone CLI |
| 服务端数据 | TanStack Query | 目标集、窗口、运行参数、状态 |
| 实时状态 | Zustand(`subscribeWithSelector`) | SSE 连接、引擎状态、日志、点击统计 |
| 表单 | React Hook Form + Zod + `@hookform/resolvers` | 运行参数表单 |
| 拖拽排序 | `@atlaskit/pragmatic-drag-and-drop` | 目标管理的图片优先级排序 |

版本在安装时取当时的最新稳定版,由 `bun.lock` 锁定并提交。

**明确不引入**

- **路由库**:只有两个页签,WebView2 里没有地址栏,用一个 `useState` 切换即可;也免去 Kestrel 加 SPA fallback。这一点偏离了全局约定里的"Vite + React Router",理由如上,请确认。
- **图表库**:点击分析是 244 行手绘 canvas,按"图表走 ref 直接更新"的约定原样移植成一个 ref 驱动的组件,不换库。
- **OpenAPI 生成类型**:要给后端加 `Microsoft.AspNetCore.OpenApi`,为 22 个端点不值得;类型用手写 Zod schema 对齐 `Contracts.cs`(见 §4.2)。

## 3. 目录与构建接线

### 3.1 目录

```
src/
├─ SmartOnmyoji.Host/    只有 C#;源码目录里没有 wwwroot/
└─ SmartOnmyoji.WebUI/   前端工程(不是 .NET 项目,不在 sln 里)
   ├─ package.json  bun.lock  vite.config.ts  tsconfig.json  components.json
   ├─ index.html
   ├─ dist/              Vite 构建产物(gitignore)
   └─ src/
      ├─ main.tsx  App.tsx  index.css
      ├─ api/            client.ts(fetch 封装)· schemas.ts(Zod)· queries.ts(Query hooks 与命令)
      ├─ stores/         engine.ts(SSE + 状态 + 日志 + 点击统计)· replay.ts(重连去重)
      ├─ lib/            notify.ts(toast)
      ├─ components/     confirm.tsx(确认弹窗)· ui/(shadcn 生成的组件)
      └─ features/
         ├─ run/         RunPage · OptionsForm · TargetSetCard · WindowList · LogPanel
         ├─ analyze/     AnalyzeDialog · charts.ts(canvas 绘制函数)
         └─ manage/      ManagePage · ImageList · CaptureDialog · geometry.ts(坐标换算)
```

前端必须放在 Host 项目目录之外:`Microsoft.NET.Sdk` 的默认通配不排除 `node_modules`,放在里面时 Host 项目求值会把 `node_modules` 的两万多个文件全部收成项目项。

### 3.2 `dotnet build` 接线

`SmartOnmyoji.Host.csproj` 里两个 target:

- **`BuildWeb`**(`BeforeTargets="BeforeBuild"`):依次执行 `bun install --frozen-lockfile` 和 `bun run build`,Vite 输出到 `SmartOnmyoji.WebUI/dist/`。
  - 用 `Inputs`(前端源码、`package.json`、`bun.lock`、配置文件)和 `Outputs`(`dist/index.html`)做增量,前端没改时不重跑 Vite。
  - 找不到 `bun` 时用 `<Error>` 给出明确提示,不静默跳过。
  - `-p:SkipWebBuild=true` 跳过,供只改后端时加速。
- **`IncludeWebContent`**(`BuildWeb` 之后、`AssignTargetPaths` 之前):把 `dist/**` 声明为 `Content`,`Link` 到 `wwwroot\…`,于是输出目录和单文件发布里仍是 ASP.NET 默认的 `wwwroot/`,后端代码不用知道前端在哪。

两个 MSBuild 细节:

- `dist/**` 的通配必须写在 target 里。写在顶层 `ItemGroup` 会在求值阶段展开,早于 `BuildWeb` 执行,干净 clone 的第一次构建会漏拷前端。
- target 里的 `Include` 不能直接写 `Link="wwwroot\%(RecursiveDir)…"`:未限定的元数据引用会按别的项批处理,取不到本项的值。要先收进一个中间项,再用 `%(中间项.RecursiveDir)`。

### 3.3 开发流程

两个进程:

```bash
dotnet run --project src/SmartOnmyoji.Host -- serve 8770
```

```bash
bun run --cwd src/SmartOnmyoji.WebUI dev
```

- Vite dev server(固定 `http://127.0.0.1:5173`)把 `/api` 代理到 `http://127.0.0.1:8770`,浏览器开 Vite 的地址即可热更新。
- 前端的检查命令都在 `src/SmartOnmyoji.WebUI/` 下跑:`bun run typecheck`、`bun test`、`bun run lint`、`bun run build`。
- 要驱动真实游戏时,`serve` 那个终端必须提权(UIPI);Vite 不需要。
- WebView2 外壳的验证走完整构建后的 `ui` 命令,开发期不让 WebView2 指向 Vite(`ui` 模式端口是随机的,为此加 CLI 参数不值得)。

SSE 经 Vite 代理实时到达(已验证)。但代理有一个开发期限定的现象:后端进程退出后,代理不会关闭浏览器这一侧的事件流,所以顶栏的"连接已断开"提示在开发模式下不会出现;直连 Kestrel 时由浏览器自己感知断线。

## 4. 架构

### 4.1 状态归属

| 状态 | 放哪 | 理由 |
|---|---|---|
| 目标集列表 / 详情 / 编辑模型、窗口列表、已保存的运行参数 | TanStack Query | 服务端数据;写操作后 `invalidateQueries`,取代现在到处手动调 `mgLoadSets()` + `loadTargets()` |
| 引擎状态(running / paused / round / progress)、日志行、点击统计、SSE 连接状态 | Zustand `engine` store | 由 SSE 推送驱动,跨运行页、顶栏、分析弹窗三处读 |
| 运行参数表单 | React Hook Form | |
| 选中的窗口句柄、当前页签、目标管理的编辑草稿、截图编辑器的选区 | 组件内 `useState` / `useReducer` | 只在一处用 |

组件一律用 selector 订阅 store 的单个字段:顶栏只订 `round` / `progress` / `running` / `paused`,日志面板只订 `lines`。不使用 Context 传递这些值。

### 4.2 类型与契约

`api/schemas.ts` 里用 Zod 逐个对齐 `Contracts.cs` 和 `ApiEndpoints.cs` 末尾的请求 record,类型一律 `z.infer`,不另写 interface。

- REST 响应在 `client.ts` 里过 schema 解析,后端 DTO 改了而前端没跟上时当场报错。
- SSE 事件定义为按 `type` 区分的 discriminated union(`state` / `round` / `matched` / `clicked` / `waiting` / `progress` / `paused` / `stopped` / `error` / `log`),来源是 `EngineManager.cs` 的事件封装;未知 `type` 落到日志里显示原始 JSON,和现状一致。
- 错误约定沿用现状:非 2xx 时取响应体的 `error` 字段作为消息;`POST /api/targets/{name}/images` 的 409 单独处理,返回冲突文件名。
- `PUT /api/targets/{name}` 的 body 是 Core 的 `target.json` schema(字符串枚举 flag),不是 `Contracts.cs` 里的 DTO,schema 要对着 `Core/Targets/TargetSetJson.cs` 写。

### 4.3 SSE store

- 单个 `EventSource('/api/events')`,由 store 持有,组件不直接接触。重连用浏览器内置机制。
- 连接状态(`connecting` / `open` / `closed`)进 store;断线期间顶栏状态标记为失联,不继续显示断线前的回合和进度。
- 日志上限 800 行,超出丢最旧的。
- `matched` 事件带的缩略图是 data URL,直接存进日志行。

### 4.4 样式

- 现有调色板(`--bg` / `--panel` / `--card` / `--border` / `--text` / `--muted` / `--accent` / `--ok` / `--warn` / `--error`,明暗两套)迁到 shadcn 的语义 token 名(`--background` / `--card` / `--border` / `--foreground` / `--muted-foreground` / `--primary` 等),色值不变;`ok` / `warn` 作为新增 token 保留。
- 明暗继续跟随系统(`prefers-color-scheme`),这也是 Tailwind v4 `dark:` 变体的默认策略,不引入手动切换。
- `app.tw.css` 里 `@layer components` 的手写部件类随对应界面迁移逐步删除,切换完成时该文件整体删除。
- 视觉以现有界面为基准,不借重写改设计。

### 4.5 需要留意的实现点

- **运行锁定**:`<fieldset disabled={running}>` 包住参数、目标集、窗口三块,原生控件(输入框、下拉、按钮)由浏览器负责向下传递。Base UI 的 Checkbox / ToggleGroup 不继承 fieldset 的禁用态,需要显式传 `disabled`。
- **参数校验分两层**:`optionsSchema` 只管形状,用于读写 `config.json`;`optionsFormSchema` 在其上叠加 `EngineOptionsValidation.cs` 的规则,用于表单标红和启动前检查。分开的原因是 `config.json` 里可以合法地存着"对当前未选模式无效"的值(例如按回合模式下时长为 0),这些值必须照常加载和保存。
- **参数自动保存**:订阅表单值,防抖 600ms 后与上次已保存的快照比较,形状合法且有变化才 `PUT /api/options`,失败静默。靠快照比较而非事件类型来排除初次填充。
- **按模式切换的字段要带 `key`**:"回合数"和"时长"在同一位置二选一渲染,不带 `key` 时 React 会复用同一个 `<input>`,两个字段的注册互相覆盖对方的值。
- **下拉统一用 NativeSelect**:选项都是动态的短列表,原生下拉能继承 fieldset 禁用态,也支持禁用单个选项(空目标集)。
- **页签面板用类名隐藏**:两个面板都 `keepMounted`(切走不丢选择和草稿)。Base UI 要等退出过渡结束才给面板加 `hidden`,在此之前两个面板会同时占位,所以由 `App.tsx` 按当前页签直接切 `hidden` 类。
- **截图编辑器**:框选用 pointer 事件。显示坐标到原图像素的换算和偏移点的归一化(保留 4 位小数)在 `features/manage/geometry.ts`,有单测。
- **重连去重**:`stores/replay.ts`,有单测(见 §6)。
- **`confirm()`**:覆盖模板、删除模板两处换成 AlertDialog(`components/confirm.tsx` 的 `useConfirm`)。
- **toast**:用 shadcn 为 Base UI 提供的 `toast` 组件,经 `lib/notify.ts` 调用。

## 5. 后端改动

只动构建配置,不动 C# 代码:

- `SmartOnmyoji.Host.csproj`:`BuildTailwind` target 换成 `BuildWeb`(§3.2)。
- `.gitignore`:`node_modules/` 和 `dist/` 由 `SmartOnmyoji.WebUI/.gitignore` 忽略,根 `.gitignore` 不需要为前端加条目。
- 发布配置 `win-x64.pubxml` 不用改:`IncludeAllContentForSelfExtract` 照旧把 `wwwroot` 打进单文件。

## 6. 顺带修复的旧缺陷

`EngineManager.Subscribe()` 在每次订阅时会先补发状态快照和最近事件。`EventSource` 断线自动重连后,旧前端会把这批事件再追加一遍:日志出现重复行,点击统计重复计数。新实现记住已收到的原始事件(事件带高精度时间戳,同一事件的原文完全相同),重连后把补发里已见过的丢掉,只放行断线期间漏掉的。状态快照不带时间戳,始终应用。

## 7. 行为对齐清单

重写后逐条对照旧 UI 验收。条目来自旧代码里的注释和分支。

勾选 = 已在浏览器里对着 `serve` 后端实际操作验证(2026-10-11)。验证时游戏未运行,引擎是对一个无关窗口跑的,所以没有产生真实的匹配和点击事件:日志缩略图、落点红点、点击分析图表是用注入的模拟数据验证的,真实事件留到 §8 M5。其中"句柄剪枝""点选的窗口不在列表""重截更新 `baseSize`"三条没有真实条件,是在页面里临时替换接口返回值来触发的。未勾选的条目代码已实现,但还没实际操作过。

**运行页**

- [x] 空目标集(0 图)在下拉里列出但不可选,文案为"尚无模板"
- [x] 切换目标集后显示来源、图数、告警数和缩略图(按 flag 着色);告警逐条写入日志
- [x] 窗口列表按标题子串筛选,默认筛选词"阴阳师";无匹配与未枚举到窗口是两种空态文案
- [x] 重新枚举后,已不存在的句柄从选择中剔除并提示
- [x] 点选:3 秒倒计时期间按钮禁用,取到前台窗口后加入选择
- [x] 点选:点中的窗口不在可选列表时如实报错,不报"已选"
- [x] 启动时读取已保存参数;改动后防抖写回;程序化填充不触发保存
- [x] 模式切换时"回合数"与"时长"二选一显示
- [x] 未选窗口或目标集时点启动,给出提示而不发请求
- [x] 运行中:参数、防检测、目标集、窗口选择全部置灰,显示锁定说明;启停按钮和日志区不受影响
- [x] 暂停按钮的文案跟随 SSE 的 `state`,不在点击瞬间切换
- [x] 顶栏状态胶囊(空闲 / 运行中 / 已暂停)、回合数、进度条
- [x] 防检测面板可折叠
- [x] 日志:各事件类型的文案格式不变;匹配缩略图内联,点击落点按归一化坐标叠加红点
- [x] 日志:自动滚动可关;可清空;上限 800 行
- [x] 页面刷新或重连后,从补发的状态快照恢复运行态

**点击分析**

- [x] 只统计本次运行;启动新一轮时清空
- [x] 散点图坐标轴按所选窗口的客户区尺寸(多窗口取各维最大值),不按点的 min/max
- [x] 目标名到颜色的映射稳定,散点图和柱状图共用
- [x] 弹窗打开时随新点击实时刷新(每帧至多重绘一次);关闭时不绘制
- [x] 各目标点击次数、点击时段分布两张图
- [x] 清空按钮

**目标管理**

- [x] 首次切到该页签才加载数据
- [x] 新建目标集后自动选中,运行页下拉同步刷新
- [x] 未选目标集时"打开文件夹""打开截图编辑器""保存 target.json"禁用
- [x] 图片列表:缩略图、文件名、基准分辨率(或"基准未记录 · 不缩放"及其提示)、flag 下拉、优先级、偏移点数量
- [x] 优先级等于列表顺序,自动编号;只有拖动手柄能发起排序
- [x] 删除模板需确认;删除后两处列表刷新
- [x] 保存 target.json 后两处列表刷新
- [x] "打开文件夹"调起资源管理器

**截图编辑器**

- [x] 每次打开都重新枚举窗口并保留已选项
- [x] 截图后显示整图
- [x] 再次截图替换,旧 object URL 释放
- [x] 框选小于 5px 视为取消(单测覆盖);选区信息显示原图像素的尺寸和位置
- [x] 保存:文件名缺扩展名时补 `.png`;连同整图原始宽高作为 `baseWidth` / `baseHeight` 上传
- [x] 重名返回 409 时询问是否覆盖;覆盖的若是同名不同扩展名的旧文件,列表里同步剔除旧项
- [x] 同名覆盖后缩略图立即更新(URL 带版本号绕过缓存)
- [x] 重截已有模板时更新其 `baseSize`
- [x] 点选模式:标题显示当前图片名,单击添加归一化偏移点并显示在截图上;对应列表行高亮;"清点"清空
- [x] Esc 能关闭
- [x] 点击遮罩、关闭按钮能关闭

## 8. 进度 TODO

### M0 脚手架与构建接线

- [x] 确认 §2 的依赖清单和"不引入路由库"
- [x] `web/` 下初始化 Vite + React + TS,接入 Tailwind v4 和 shadcn(Base UI)
- [x] Vite 代理 `/api` → `127.0.0.1:8770`;验证 SSE 经代理实时到达
- [x] 调色板迁到 shadcn token,明暗两套
- [x] `node_modules/`、`dist/` 由 `web/.gitignore` 忽略

### M1 基础设施

- [x] `api/client.ts`:fetch 封装与错误约定
- [x] `api/schemas.ts`:全部 DTO、请求体、SSE 事件的 Zod schema
- [x] `api/queries.ts`:目标集、窗口、参数的 Query hooks 与 mutation
- [x] `stores/engine.ts`:SSE 连接、状态、日志、点击统计,含 §6 的重连去重
- [x] 应用外壳:顶栏(页签、状态胶囊、回合、进度)

### M2 运行页

- [x] 运行参数表单(RHF + Zod)与自动保存
- [x] 防检测折叠面板
- [x] 目标集卡片与缩略图
- [x] 窗口列表、筛选、点选
- [x] 启动 / 暂停 / 停止与运行锁定
- [x] 日志面板
- [x] §7"运行页"逐条验收(`serve` + 浏览器)

### M3 点击分析

- [x] canvas 绘制函数移植为 `charts.ts`
- [x] `AnalyzeDialog`:ref 驱动重绘,订阅点击统计
- [x] §7"点击分析"逐条验收

### M4 目标管理

- [x] 新建 / 选择目标集、打开文件夹
- [x] 图片列表:flag 编辑、拖动排序、删除
- [x] 保存 target.json
- [x] 截图编辑器:选窗、截图、框选、保存模板(含 409 覆盖流程)
- [x] 截图编辑器:偏移点点选模式
- [x] 坐标换算纯函数的单测
- [x] §7"目标管理""截图编辑器"逐条验收

M2–M4 的"逐条验收"以 §7 的勾选状态为准,未勾选的条目并入 M5 一起过。

### M5 切换

- [x] 对真实游戏窗口(1200×600)跑通 建集 → 截模板 → 运行 → 卡死保护自动停止 → 点击分析:真实的匹配缩略图、落点红点、点击统计都正确(2026-10-11)。后端未提权,模板取的是画面标题文字,所以这一轮只验证了界面展示,没有验证点击在游戏里生效
- [x] 提权后用真实玩法的目标集跑完整回合(用户实测通过,2026-10-11)
- [x] 把 §7 里未勾选的条目操作一遍(只剩"打开文件夹"没点,它会在桌面弹出资源管理器窗口)
- [x] csproj 加 `BuildWeb` target(§3.2),删除 `BuildTailwind` target
- [x] 删除旧 `wwwroot/index.html`、`wwwroot/js/`、`wwwroot/tailwind.css`、`Styles/app.tw.css`(删除前的副本在 `.artifacts/legacy-wwwroot/`)
- [x] 前端工程搬到 `src/SmartOnmyoji.WebUI/`,输出到自己的 `dist/`,Host 源码目录不再有 `wwwroot/`(§3.1)
- [x] 删掉 `dist/` 后 `dotnet build`:输出目录的 `wwwroot/` 里是 `index.html` 加两个 asset;不改源码再次构建会跳过 Vite
- [x] 删掉 `dist/` 后单文件 `dotnet publish`(Debug 配置、输出到临时目录):exe 以 `serve` 启动后能提供新 UI
- [x] `node_modules/` 和 `dist/` 都不存在时 `dotnet build`:自动重装 440 个包(约 42 秒)并构建出前端(2026-10-11)
- [x] WebView2 外壳:用户用打包出来的 release 单文件实测正常(2026-10-11)

### M6 文档收尾

- [x] `CLAUDE.md`:更新 WebUI 一节的技术栈、构建命令、开发流程
- [x] `csharp-rewrite-design.md`:§10 的前端描述、§11 进度