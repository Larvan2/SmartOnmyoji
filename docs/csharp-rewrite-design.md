# SmartOnmyoji C# 重写设计方案

> 目标:用 C# / .NET 8 彻底重写,采用"Web 前端 + 原生后端"的 Tauri 式架构。
> 原则:**行为对齐,实现重构**。旧代码里不合理的逻辑不照搬,借重写的机会做更优处理。
> 现状(Python)保留为参考实现,直到新版达到功能对齐(parity)再淘汰。

---

## 1. 范围与目标

- **平台**:仅 Windows(旧项目本就 Windows 专属,ADB/安卓分支已废弃,不再保留)。
- **功能对齐面**:后台截图 → 图像匹配 → 拟人化后台点击 的自动循环;多开;回合状态机;防检测策略;目标集(图片 + 行为定义);运行日志与进度。
- **重写要拿到的收益**:
  1. 引擎与 UI 彻底解耦,引擎无 UI 依赖、可单测、可无头运行。
  2. 用事件流取代 `print("<br>")` 重定向。
  3. 类型化配置 / 目标模型,消灭 12 元组与 `other_setting[16]` 式索引。
  4. 协作式取消取代 `QThread.terminate()`。
  5. 真并行的多开(无 GIL)。
  6. DPI、标题栏偏移等"魔法值"用正确的 Win32 客户区换算替代。

---

## 2. 现有系统行为清单

### 2.1 必须保留的核心行为(重写时逐条对照验收)

| 行为 | 旧实现位置 | 说明 |
|---|---|---|
| 后台窗口截图 | `ModuleGetScreenCapture.window_screen` | `PrintWindow(PW_RENDERFULLCONTENT)`,失败回退 `BitBlt` |
| 模板匹配 | `ModuleGetPos.GetPosByTemplateMatch` | `TM_CCOEFF_NORMED`,阈值 0.80,按目标顺序早退,返回中心点 |
| 特征点匹配 | `ModuleGetPos.GetPosBySiftMatch` | SIFT + FLANN + Lowe ratio 0.6 + 单应矩阵求中心,抗缩放旋转 |
| 拟人化点击偏移 | `ModuleClickModSet` + `ModuleDoClick.get_p_pos` | 正态分布点击模型 + 按目标在窗口中的九宫格方位旋转偏移 |
| 后台点击 | `ModuleDoClick.windows_click` | `SendMessage` WM_LBUTTONDOWN/UP,随机按下时长 |
| 诱饵点击 | `ModuleDoClick.get_ex_click_pos` | 按概率在别处多点一下,混淆热区 |
| 回合状态机 | `ModuleRunThread.run` + `img_pos.json` 的 flag | flag: `""`/`start`/`mark`/`skip`/`stop` 驱动回合与点击 |
| 偏移坐标覆盖 | `ModuleStartMatching.matching` | 关键图片用 `click_pos` 列表随机取点,按 `real_pos` 缩放比换算 |
| 随机等待防检测 | `ModuleRunThread.run` | 按概率触发等待、10 分钟频次上限强制等待 |
| 连续同图终止 | `ModuleRunThread.run` | 连续 5 次匹配同一目标则停(卡住/无体力保护) |
| 游戏时段警告 | `ModuleStartMatching.time_warming` | 凌晨时段提示延迟 |
| 多开遍历 | `ModuleStartMatching.start_match_click` | 对每个句柄依次截图/匹配/点击 |
| 进程优先级 | `ModuleHandleSet.set_priority` | 防闪退,需管理员 |
| 目标集加载 | `ModuleGetTargetInfo` | 文件夹内 jpg/png + `img_pos.json`,支持中文路径 |
| 点选目标窗口 | `ModuleHandleSet.get_active_window` | 倒计时内取前台窗口句柄 |

### 2.2 明确要"重新考虑 / 丢弃"的旧逻辑(**不照搬**)

| 旧逻辑 | 问题 | 重写方向 |
|---|---|---|
| `cy = py + pos[1] - 40` 硬编码标题栏高度 | 脆弱,不同窗口/DPI 会错位 | 用 `GetClientRect` + `ClientToScreen` 在**客户区坐标系**里算,不猜像素 |
| `print("<br>...")` 兼作日志与 UI | 表现层与逻辑死耦合 | 结构化事件 + `ILogger`;UI 只是事件订阅者之一 |
| 12 元组 / `to_list()[:12]` / `other_setting[16]` | 契约脆弱、易错位 | 类型化 record(`EngineOptions`/`AppConfig`),Options 校验 |
| 每次点击/每个句柄都 `ReadConfigFile()` | 反复读盘的全局可变态 | 启动一次性加载,DI 注入不可变快照 |
| `QThread.terminate()`(代码自承"不安全") | 强杀线程 | `CancellationToken` 协作式取消 |
| `for i in range(20000)` / `9999999999` 哨兵 | 靠魔法上限控制循环 | 基于时间 / 轮次的显式循环条件 |
| `success_target_list=[0,1,2,3,4,5]` + `repeat_tolerance-len(set())` | 晦涩 | 定长环形缓冲 + "最近 N 次同名"判定 |
| 点击模型里的 `1.373 / 0.888 / 0.816 …` 魔法系数 + 九宫格旋转 | 不可维护、不可调 | 保留"正态偏移 + 偏向窗口内侧 + 边界截断"的**思想**,重写为参数化、可单测的采样器(见 §8) |
| 匹配优先级 = 文件名排序隐式决定 | 隐式、易踩坑 | 目标定义里显式 `priority` 字段 |
| 压缩逻辑在模板/特征两条路径里各写一遍且不一致 | 易漂移 | 统一在 `ImageOps` 一处处理,匹配选项声明式 |
| `img_pos.json` 用图片文件名字符串关联行为 | 文件名即契约,重命名即坏 | 迁移为显式 `target.json`(见 §9),旧格式提供一次性导入器 |
| 已在 config 里标注"无用了"的 `#12 if_match_then_stop`/`#13 stop_target_img_name`、`adb_wifi_*`、`screen_scale_rate`、`connect_mod`、`run_mode` 兼容模式 | 死配置 | 直接不迁移 |
| DPI 处理散落且被注释关掉(`_capture_dimensions` 忽略缩放) | 半残 | app.manifest 声明 Per-Monitor-V2 DPI 感知,统一按物理像素捕获 |
| 雷电模拟器 `FindWindowEx "TheRender"` 特例 | 混在句柄逻辑里 | 抽象为可插拔的"窗口目标解析策略",默认策略 + 模拟器策略 |

---

## 3. 技术选型

| 关注点 | 选型 | 理由 |
|---|---|---|
| 运行时 | **.NET 8** | LTS、单文件发布、无 GIL 真并行 |
| 计算机视觉 | **OpenCvSharp4**(+ `runtime.win-x64`) | 直接映射现有 OpenCV 逻辑(matchTemplate/SIFT/FLANN) |
| Win32 互操作 | **CsWin32**(源生成 P/Invoke) | 类型安全、免手写 DllImport;必要处再手写 |
| 桌面壳 | **WebView2**(内嵌)或 **Photino.NET** | 系统 WebView,不打包 Chromium;等价于 Tauri 的 WebView 层 |
| 前后端通信 | **ASP.NET Core Minimal API + SignalR**(本地 Kestrel) | 事件流天然适配 SignalR 推送;命令走 API。见 §10 |
| 前端 | **Svelte**(或 Vue/原生) | 轻;旧输出本就是 HTML,迁移顺滑 |
| DI / 配置 / 日志 | `Microsoft.Extensions.*` + Serilog | Options 校验、结构化日志 |
| 测试 | xUnit + 假实现(fake capturer/matcher) | 引擎/状态机/采样器可离线单测 |

> **壳的两条路线**
> - 主推:**Host 进程内起 Kestrel(仅 localhost)**,提供 WebUI 静态资源 + SignalR + API,再用 **WebView2 窗口**指向 `http://127.0.0.1:port`。最"Tauri 味",事件推送 DX 最好。
> - 更轻量:**Photino 自带消息桥**,不起 web server,事件用 JSON 走桥。体积更小,但事件流要手写。首版按主推路线,后续可换。
>
> **体积现实**:.NET 自包含发布带运行时,几十 MB;可用 单文件 + Trimming 压缩;NativeAOT 更小更快,但需**先验证 OpenCvSharp 原生依赖在 AOT 下可用**(列为风险点)。仍远小于 Electron.NET(打包 Chromium)。

---

## 4. 解决方案分层与项目划分

```
SmartOnmyoji.sln
├─ src/
│  ├─ SmartOnmyoji.Core/         # 纯逻辑,零 Windows/CV 依赖,可单测
│  │  ├─ Abstractions/           # IWindowService / IScreenCapturer / IMatcher / IInputSender
│  │  ├─ Engine/                 # AutomationEngine, RoundStateMachine, StopConditions
│  │  ├─ Targets/                # TargetSet, TargetImage, TargetFlag, ClickSpec
│  │  ├─ Humanize/               # OffsetSampler, WaitPolicy, DecoyClickPolicy, AntiDetection
│  │  ├─ Config/                 # AppConfig, EngineOptions (typed + validated)
│  │  └─ Events/                 # EngineEvent 记录类型
│  ├─ SmartOnmyoji.Vision/       # OpenCvSharp 实现 IMatcher
│  │  ├─ TemplateMatcher.cs
│  │  ├─ FeatureMatcher.cs       # SIFT/ORB
│  │  └─ ImageOps.cs             # 灰度/压缩/Mat<->bytes
│  ├─ SmartOnmyoji.Windows/      # win32 实现
│  │  ├─ GdiWindowCapturer.cs    # PrintWindow/BitBlt
│  │  ├─ PostMessageInput.cs     # 后台点击
│  │  ├─ WindowService.cs        # 枚举/查找/客户区/优先级/前台点选
│  │  └─ WindowTargetResolver.cs # 默认 + 模拟器(雷电)策略
│  ├─ SmartOnmyoji.Host/         # 组合根:DI + Kestrel + SignalR + WebView2 壳
│  │  ├─ Program.cs
│  │  ├─ Hubs/EngineHub.cs
│  │  ├─ Api/CommandEndpoints.cs
│  │  └─ Shell/MainWindow (WebView2)
│  └─ SmartOnmyoji.WebUI/        # Svelte 前端,构建产物内嵌进 Host
├─ tests/
│  └─ SmartOnmyoji.Core.Tests/   # 状态机/采样器/等待策略/停止条件
└─ assets/                       # 目标集(img/) 与默认配置
```

依赖方向:`Windows`/`Vision` → 依赖 `Core` 的抽象;`Host` 组装一切。`Core` 不反向依赖任何平台实现。

---

## 5. 核心抽象接口

```csharp
namespace SmartOnmyoji.Core.Abstractions;

public interface IWindowService
{
    IReadOnlyList<WindowInfo> Enumerate();
    WindowInfo? Find(string title);
    WindowInfo? GetForeground();                 // 用户倒计时点选
    ClientArea GetClientArea(nint hwnd);          // 客户区尺寸 + 屏幕原点
    bool IsAlive(nint hwnd);
    void SetPriority(int pid, ProcessPriority priority);
}

public interface IScreenCapturer
{
    // 捕获物理像素;DPI 由实现处理。返回灰度帧(匹配用)可含原始 BGRA(调试用)
    CaptureFrame Capture(nint hwnd);
}

public interface IMatcher
{
    // 单目标匹配;命中返回客户区坐标系下的中心点与分数
    MatchResult? Match(CaptureFrame frame, TargetImage target, MatchOptions options);
}

public interface IInputSender
{
    ClickOutcome Click(nint hwnd, Point clientPoint);   // 后台点击,客户区坐标
}
```

要点:
- 坐标一律用**客户区坐标系**贯穿(截图、匹配、点击),消灭 `-40` 魔法值。
- `IMatcher` 是纯函数式(输入帧+目标+选项 → 结果),用样例图片即可单测,不碰窗口/句柄。
- 四个接口都可被假实现替换,引擎完全可离线跑。

---

## 6. 领域模型

### 6.1 目标集与 flag 状态机

```csharp
public enum TargetFlag { Normal, RoundStart, Once, Skip, Stop }
//                       ""      start       mark   skip   stop

public sealed record ClickSpec(
    Point? RealPos,                 // 参照点,用于界面缩放换算
    IReadOnlyList<Point> ClickPos); // 候选点击点,随机取一

public sealed record TargetImage(
    string Name,
    string FilePath,
    int Priority,                   // 显式优先级(替代文件名隐式排序)
    TargetFlag Flag,
    ClickSpec? Click,               // 关键图的偏移点击定义
    MatchHint Hint);                // Template / Feature / Auto(阈值不再每图覆盖,统一走运行级)

public sealed class TargetSet
{
    public string Name { get; }
    public IReadOnlyList<TargetImage> Images { get; }  // 已按 Priority 排序
}
```

**回合状态机**(把散落在 `run()` 里的 flag_mark/rounds/success_target_list 收拢到一处):

```csharp
public sealed class RoundStateMachine
{
    public int Round { get; private set; }
    private bool _onceClickedThisRound;
    private readonly RingBuffer<string> _recent = new(capacity: 5);

    public RoundDecision Decide(TargetImage hit)
    {
        _recent.Push(hit.Name);
        if (_recent.AllSame())                       // 连续 N 次同图 → 终止
            return RoundDecision.Stop(StopReason.RepeatedSameTarget);

        switch (hit.Flag)
        {
            case TargetFlag.Stop:  return RoundDecision.Stop(StopReason.StopFlag);
            case TargetFlag.Skip:  _onceClickedThisRound = true;
                                   return RoundDecision.NoClick;
            case TargetFlag.Once:  if (_onceClickedThisRound) return RoundDecision.NoClick;
                                   _onceClickedThisRound = true;
                                   return RoundDecision.Click;
            case TargetFlag.RoundStart:
                                   Round++; _onceClickedThisRound = false;
                                   return RoundDecision.Click.WithRoundStarted(Round);
            default:               return RoundDecision.Click;
        }
    }
}
```

> 语义对照旧代码:`start` 起新回合并复位 mark;`mark`(Once) 每回合只点一次;`skip` 匹配但不点且抑制本回合 mark;`stop` 终止;连续同名 5 次终止。均由状态机集中裁决,单测可覆盖每条分支。

### 6.2 引擎事件

```csharp
public abstract record EngineEvent(DateTimeOffset At);
public record RoundStarted(int Round)                               : EngineEvent;
public record TargetMatched(string Name, Point Pos, double Score)   : EngineEvent;
public record Clicked(string Name, IReadOnlyList<Point> Points)     : EngineEvent;
public record Waiting(TimeSpan Duration, string Reason)             : EngineEvent;
public record ProgressChanged(int Percent)                         : EngineEvent;
public record EngineStopped(StopReason Reason)                     : EngineEvent;
public record EngineError(string Message)                          : EngineEvent;
public record LogMessage(LogLevel Level, string Text)              : EngineEvent;
```

引擎通过 `IProgress<EngineEvent>` / `Channel<EngineEvent>` 对外发事件,Host 层把它桥到 SignalR。**引擎不知道 UI 存在**。

---

## 7. 引擎循环设计

```csharp
public async Task RunAsync(EngineOptions opts, IReadOnlyList<nint> windows,
                           TargetSet targets, CancellationToken ct)
{
    var deadline = opts.Mode == RunMode.ByMinutes
        ? DateTimeOffset.Now + opts.Duration : DateTimeOffset.MaxValue;

    while (!ct.IsCancellationRequested && !Finished(deadline))
    {
        var roundStarted = false;              // 聚合"本轮是否有任一窗口开局"(多窗口合并为组的一次)
        foreach (var hwnd in windows)          // 多开:可并行(Parallel.ForEachAsync)
        {
            if (!_windows.IsAlive(hwnd)) { Emit(Warn); await Retry(ct); continue; }

            var frame = _capturer.Capture(hwnd);
            var hit = _matcher.MatchFirst(frame, targets, opts.Match);  // 按 Priority
            if (hit is null) continue;

            var decision = _state.Decide(hit.Target);
            Emit(new TargetMatched(hit.Target.Name, hit.Pos, hit.Score));

            if (decision.RoundStarted is int r) { Emit(new RoundStarted(r)); roundStarted = true; }
            if (decision.Kind == Stop) { Emit(new EngineStopped(decision.Reason)); return; }
            if (decision.Kind == Click)
            {
                var point = _humanizer.Resolve(hit, _windows.GetClientArea(hwnd));
                var outcome = _input.Click(hwnd, point);
                Emit(new Clicked(hit.Target.Name, outcome.Points));
            }
        }
        // 防检测等待放在**轮末**、按"开局(RoundStart)"计数,而非旧版的 per-窗口/per-点击。
        // 组级、作用于整个循环:双开组队"要歇一起歇",不会一个窗口开局、另一个还在等(旧版 bug)。
        foreach (var wait in _antiDetection.EvaluateAfterRound(roundStarted))
        {
            Emit(new Waiting(wait.Duration, wait.Reason));
            await Task.Delay(wait.Duration, ct);            // 概率随机等待 / 频次上限 强制减速
        }
        await _antiDetection.DelayAsync(opts.Interval, ct);  // 区间随机 + 抖动
        Emit(new ProgressChanged(ComputeProgress()));
    }
    Emit(new EngineStopped(StopReason.Completed));
}
```

对比旧 `run()`:无 `print`、无 `terminate`、无魔法上限;停止条件、等待策略、拟人化都是独立可注入组件。

---

## 8. 拟人化 / 防检测(重新设计)

保留旧版"看起来像人"的**意图**,但重写为参数化、可单测的策略,丢弃九宫格旋转 + 一堆魔法系数。

- **OffsetSampler**:偏移量 `~ 截断正态(σ = deviation·k)`;方向**偏向窗口内侧**(用目标点相对客户区中心的向量做偏置,替代九宫格旋转的本意就是"别往窗口外点");纵向略压扁(保留旧版手感,做成参数)。产出可用统计断言单测(均值、越界率、分布形状)。
- **DecoyClickPolicy**:按概率追加一次别处点击(对齐 `ex_click`),概率与落点区域参数化。
- **AntiDetectionPolicy**(两层叠加,均以「开局 RoundStart」为计数节拍,`Enabled` 各自独立可开关):
  ① **概率随机等待**——每次开局按概率触发一段随机等待,打破节奏规律;带「两次等待最小间隔」保护(对齐旧 150s)。
  ② **时间窗口频次上限**——滑动 N 分钟窗口内开局数超阈值后,之后每次开局强制额外等待(对齐 `success_match_then_wait`)。
  关键:等待在**轮末组级**统一施加(见 §7),而非旧版 per-窗口——从根上修掉「双开一个窗口开局、另一个在等」的组队错位;
  纯逻辑、时钟经 `TimeProvider` 注入,可离线单测(概率/间隔/频次窗口逐分支)。
- **ClickTiming**:按下→弹起随机时长(对齐旧 `randint(5,15)/100`)。
- **PlaytimeWarning**:凌晨时段延迟提示。

所有参数进 `EngineOptions.AntiDetection`,集中一处、可调、有默认值。

---

## 9. 配置与目标定义(重新设计)

### 9.1 配置:`config.ini` → `appsettings.json` + 类型化 Options

只迁移仍然有意义的项;死配置(§2.2)不迁移。

```jsonc
{
  "Engine": {
    "Mode": "ByRounds",              // ByMinutes | ByRounds
    "Duration": "01:40:00",
    "Rounds": 100,
    "Interval": { "Min": 2.0, "Max": 4.0 },
    "Match": { "Method": "Template", "Threshold": 0.80, "CompressRatio": 1.0 },
    "MultiWindow": false,
    "SetProcessPriority": "High",
    "AntiDetection": {
      "ClickDeviation": 25,
      "DecoyClickProbability": 0.15,
      "RandomWait": { "Enabled": true, "Probability": 0.01, "SecondsRange": [10, 40] },
      "FrequencyCap": { "WindowMinutes": 10, "MaxHits": 160, "ExtraWaitSeconds": 5 },
      "PlaytimeWarning": true
    }
  },
  "Ui": { "SaveOnRun": true, "PlaySound": true, "SaveClickLog": true }
}
```

绑定为 `EngineOptions` 并用 `IValidateOptions` 校验(区间合法、阈值范围等)。

### 9.2 目标:`img_pos.json` → `target.json`(显式、可迁移)

```jsonc
{
  "name": "御魂",
  "defaults": { "matcher": "Template" },
  "images": [
    { "file": "tiaozhan.jpg",  "priority": 10, "flag": "RoundStart",
      "baseSize": { "width": 1200, "height": 600 } },
    { "file": "win_jiangli.jpg","priority": 20, "flag": "Normal",
      "baseSize": { "width": 1200, "height": 600 },
      "click": { "clickPos": [[0.75, 0.92], [0.71, 0.89]] } },
    { "file": "00_marked.png",  "priority": 30, "flag": "Skip" },
    { "file": "0_end.jpg",      "priority": 5,  "flag": "Stop" }
  ]
}
```

- 显式 `priority` 取代文件名隐式排序。
- **`baseSize` = 截取该模板时的客户区尺寸(物理像素)**,由目标管理截图取模板时自动记录(用户不填)。
  运行时客户区若是别的尺寸,匹配器按比例把模板缩放过去再匹配 → **同一套模板跨分辨率复用,不必换个分辨率就重截一遍图**。
  缺省(旧目标集/导入数据)= 不缩放,行为与加入该字段前完全一致。详见 §9.4。
- 每图可覆盖 matcher(匹配器)。**阈值不再每图/默认覆盖,统一走运行级 `EngineOptions.Match.Threshold`(运行页「匹配阈值」)**——旧的每图/默认阈值无 UI、只会架空运行页设置,已删除。
- **`clickPos` 用客户区归一化坐标(0~1)**,乘当前客户区尺寸即得像素——天生分辨率无关,
  彻底删掉旧版 `real_pos` + `scal_rate` + `abs(pos-real_pos)<100` 的缩放换算(已在 P2 验证:命中中心即客户区可点坐标)。
- 提供 **`img_pos.json` → `target.json` 一次性导入器**:旧 `flag` 空串→Normal、其余映射到枚举;优先级按文件名排序还原旧遍历次序。
  **旧 `click_pos`/`real_pos` 坐标不导入**——经确认几乎全是错的(且标注在整窗坐标系、与新客户区归一化坐标系不兼容);
  导入后 `click` 一律留空 → 引擎点匹配中心(反而正确)。确需偏移点击的图,后续在 `target.json` 里用客户区归一化坐标(0~1)手动补 `click.clickPos`。

### 9.3 目标管理:从"手摆文件"到 UI 驱动(P7 方向)

旧版让用户手动往 `img/<玩法>/` 摆图片 + 手写 `img_pos.json`,契约脆弱、门槛高。新版 **P7 的「目标管理」应把整套流程 UI 化**:

- 用户在软件里**输入玩法名**(如 `huntu`/`huodong`)→ 软件**自动创建对应目录**并生成/维护该目录的 `target.json`。
- 用户**在软件内对游戏窗口截图**、框选出模板图,直接落盘为该目标集的图片(取代手动截图 + 拷文件)。
- 用户**在软件内可视化设置每图的 flag**(Normal/RoundStart/Once/Skip/Stop)、优先级,以及(需要时)在截图上**点选偏移点击点**并存为客户区归一化坐标。(匹配阈值统一在运行页设,不在每图。)
- `target.json` 由软件读写托管,用户不必手碰 JSON;`img_pos.json` 导入器退居为"迁移旧目标集"的一次性入口。

> P6 已把底层跑通(schema + 序列化/加载 + 导入器 + 三级加载优先);P7 在其上补这层交互(前端表单/截图取模板/flag 编辑器 + 后端 `POST /api/targets`、截图/裁剪端点)。

### 9.4 模板跨分辨率复用:`baseSize` + 缩放模板匹配(取代旧版 SIFT 路线)

**要解决的真问题**:模板在 A 分辨率截的,换到 B 分辨率(换机器、改窗口大小)就匹配不上,用户被迫**为每个分辨率重截一套图**。
旧版给的答案是第二条匹配路径——**SIFT 特征点匹配**(`GetPosBySiftMatch`:SIFT + FLANN + Lowe ratio 0.6 + `findHomography` 求中心),
号称抗缩放旋转,但旧作者自己在注释里写"准确度不好说,用起来有点难受"。

**新版不走 SIFT,改用「记基准尺寸 + 缩放模板」**,理由:

- 游戏 UI 换分辨率时的变换是**纯相似变换**(等比缩放 + 平移),既无旋转也无透视——SIFT 那 8 自由度单应是为一个 1 自由度的问题上的重型工具。
- UI 图标是**低纹理、边缘锐利的合成图像**,恰是 SIFT 的弱项:特征点少且不稳定。旧版 `len(good) > 9` 的绝对门槛让小图标恒不命中,大图标又易误命中(两个相似按钮)。
- 代价差一个量级:SIFT 每轮要对整张截图做一次特征提取;缩放模板匹配仍是一次 `matchTemplate`,**且缩放结果可缓存**,稳态零额外开销。
- 分数语义不变(仍是 `TM_CCOEFF_NORMED` 的 0~1),运行页阈值、日志、状态机全部照旧;SIFT 路径则根本给不出可比的分数。

**做法**(三处,各自很薄):

1. **写侧**:目标管理截图取模板时,前端把那张整帧截图的原始尺寸(= 客户区物理像素)随图一起 POST;
   `TargetSetCatalog.SaveImage` 落图后立刻把 `baseSize` 写进 `target.json`——这个值**只有截图那一刻知道**,漏记就再也补不回来,所以不等用户点「保存 target.json」。
2. **裁决**:`Core/Matching/TemplateScale.cs` 纯函数算缩放比(可离线单测)。等比变化取两方向均值;明显不等比(窗口被拉伸/游戏加黑边)取**较小比例**(UI 内容一般按较短边等比适配);
   比例落在 `[0.2, 5.0]` 之外或基准尺寸缺失/非正 → **一律返回 1.0 不缩放**,拿不准时宁可退回旧行为。
3. **匹配**:`TemplateMatcher` 把模板缩放到目标尺寸再 `matchTemplate`,按(路径 + 目标像素尺寸)缓存缩放结果;
   与既有压缩(`CompressRatio`)复合时,**模板总缩放 = 分辨率适配比 × 压缩比,而命中坐标只按压缩比还原**——模板缩放改的是模板大小,命中位置始终落在截图坐标系里。

**实测**(`scalematch` 离线命令,素材 `img/test/`:1200×600 整帧 + 从中裁的 82×38 / 168×102 模板):

| 目标分辨率 | 记了 `baseSize` | 旧行为(不缩放) |
|---|---|---|
| ×0.75(900×450) | 0.964 / 0.986,误差 ≤1px | 0.281 / 0.562,坐标偏 110~151px |
| ×0.90(1080×540) | 0.914 / 0.992,误差 ≤1px | 0.399 / 0.691 |
| ×1.25(1500×750) | 0.961 / 0.995,误差 ≤1px | 0.326 / 0.598,坐标偏 407~667px |
| ×1.50(1800×900) | 0.980 / 0.996,误差 ≤1px | 0.318 / 0.654 |

记了 `baseSize` 的全部超过 0.80 阈值且坐标误差 ≤1px;旧行为全部低于阈值、坐标乱飞。**同一套模板跨分辨率复用成立。**

> 特征点匹配路径**没有删掉口子**:`MatchMethod.Feature` / `MatchHint.Feature` 枚举位仍在,
> 将来若出现缩放解决不了的场景(例如真需要旋转不变)可再补 `Vision/FeatureMatcher.cs`。
> 但那时需先补一个按 `options.Method` 分发的 `CompositeMatcher`——**现在注入的 `TemplateMatcher` 并不看 `options.Method`,
> 每图 `Hint=Feature` 目前是静默失效的**(已知待办)。

---

## 10. 前后端通信契约

**方向一致性**:命令(前端→后端)走 HTTP API;事件(后端→前端)走 SignalR 推送。

```
前端 (WebView2 内,Svelte)
  │  POST /api/engine/start   { profile, target, options }
  │  POST /api/engine/pause | resume | stop
  │  POST /api/windows/pick   → 倒计时点选,返回句柄
  │  GET  /api/targets        → 目标集列表(含缩略图)
  ▼
Host (Kestrel, localhost)
  ├─ CommandEndpoints  → 调 AutomationEngine
  └─ EngineHub (SignalR):
        engine.on("event", payload)   // TargetMatched/Clicked/Waiting/Progress/Stopped/Log...
```

- 引擎的 `EngineEvent` 由 Host 的桥接器序列化后 `Clients.All.SendAsync("event", ...)`。
- 日志/匹配缩略图直接作为事件字段推送(旧版的 `<img src>` 日志天然对应)。
- 好处:前端纯展示;将来也能从浏览器远程控制;引擎侧零 UI 耦合。

> **P7 实现取舍(2026-07-22):事件流用 SSE 而非 SignalR。** 事件是纯服务端→前端的单向流,浏览器原生 `EventSource` 即可胜任;
> 在「原生前端 + WebView2 离线」的选型下,SSE **零客户端依赖**,省掉 vendoring SignalR JS 客户端。命令仍走 REST。
> 落地:`EngineManager` 扇出、`GET /api/events` 挂 SSE(见 §11 P7 进度)。同一契约方向、同一解耦收益,更轻的实现。

---

## 11. 分阶段移植计划

每个阶段结束都应有**可运行**的产物;Python 版并存作参照直到对齐。

| 阶段 | 内容 | 验收 |
|---|---|---|
| **P0 脚手架** ✅ | 建 sln/项目/DI;空引擎发假事件;WebUI 壳显示日志;按钮→命令→事件→UI 打通 | 端到端假数据跑通 |
| **P1 Win32 基础** ✅ | `WindowService`(枚举/查找/点选/客户区/优先级)、`GdiWindowCapturer` | 能点选窗口并把一帧截图存盘核对 |
| **P2 视觉** ✅ | `TemplateMatcher`(OpenCvSharp);加载目标集;对截帧匹配出坐标 | 坐标与旧版一致;后加 `FeatureMatcher` |
| **P3 点击** ✅ | `PostMessageInput` 后台点击 + `OffsetSampler` 拟人化 | 真机点击命中游戏内目标 |
| **P4 引擎/状态机** ✅ | 串起 截图→匹配→点击 循环;`RoundStateMachine`;停止条件;**单测** | 假实现下状态机分支全绿 |
| **P5 防检测** ✅ | `AntiDetectionPolicy`:概率随机等待 + 频次上限(两层,按 RoundStart 计数、轮末组级施加、双开安全);诱饵点击/时段警告留可选 | 行为与旧版对齐、可调、可禁用;策略+接线单测全绿 |
| **P6 配置与目标** ✅ | 类型化 `EngineOptions` + 校验;`target.json` schema + 旧格式导入器 | 旧目标集导入即用 |
| **P7 UI 完善** | 配置表单、目标管理(建集 / 截图取模板 / flag 编辑,见 §9.3)、日志分析、缩略图 | 覆盖旧 UI 功能 |
| **P8 打包** | 单文件 + Trimming;评估 NativeAOT;安装包 | 干净机器可运行 |

**建议先做 P1**:Win32 后台截图/点击是全项目最高风险区,越早在 C# 里验证越好。

### 当前进度

- **P0 ✅ 已完成**(2026-07-22)。落地内容:
  - 解决方案 `csharp/SmartOnmyoji.sln`(与 Python 版并存;后于整理归档时移至仓库根 `SmartOnmyoji.sln`),`net9.0`,.NET SDK 9.0.316。
  - `SmartOnmyoji.Core`:四个抽象接口、领域模型(`TargetSet`/`TargetImage`/`TargetFlag`/`ClickSpec`)、
    事件体系(`EngineEvent` 记录类型)、`RoundStateMachine`(收拢旧 flag 逻辑)、`EngineOptions`、
    `MatchFirst` 优先级匹配扩展、`RecentBuffer`。
  - `FakeAutomationEngine`:不接 Win32/CV,按脚本驱动真状态机,经 `Channel<EngineEvent>` 发事件。
  - `SmartOnmyoji.Host`:控制台组合根(DI + 事件流消费),已端到端跑通竖切。
  - `SmartOnmyoji.Core.Tests`:11 个测试全绿(状态机各分支 + 假引擎管道 + 取消)。
- **P1 ✅ 核心已验证**(2026-07-22)。落地内容:
  - `SmartOnmyoji.Windows`(`net9.0-windows`,手写 P/Invoke 起步,后续可迁 CsWin32)。
  - `WindowService`:枚举 / 查找 / 前台点选 / `GetClientRect`+`ClientToScreen` 客户区换算 / 优先级 / 存活检测。
  - `GdiWindowCapturer`:`PrintWindow(PW_RENDERFULLCONTENT|PW_CLIENTONLY)` 后台截**客户区**,失败回退 `BitBlt`;`GetBitmapBits` 取 BGRA。
  - `DpiAwareness.EnablePerMonitorV2()`:统一 DPI 感知,取代旧代码散落且被注释关掉的 DPI 处理。
  - Host 增加 `list` / `capture <标题>` 冒烟命令。
  - **实测**:对真实运行中的「阴阳师-网易游戏」(1200x600,后台/被遮挡)后台截图成功,非黑像素 100%,
    存盘 PNG 目视核对——内容真实、朝向正确、颜色正确。多开(两个游戏窗口)枚举正常。
  - 待补(低风险):`WindowService` 的前台点选 / 优先级设置仅编译未实机验证。
- **P2 ✅ 已验证**(2026-07-22)。落地内容:
  - `SmartOnmyoji.Vision`(OpenCvSharp4 4.13 + runtime.win),`TemplateMatcher : IMatcher`、`ImageOps`(中文路径 `ImDecode`、灰度 Mat、可选压缩)。
  - Host 增加 `match <标题> [目录]` 冒烟命令:自裁剪回配 + 真实模板匹配 + 计时。
  - **实测**(对真实运行的「阴阳师」1200x600 客户区帧):
    - 自裁剪回配:期望中心 (600,300) → 命中 (600,300),分数 **0.996**(确定性证明坐标数学正确)。
    - 真实模板 `img/yuling/win_jiangli` 分数 **0.960 ✔** @(674,499),`win_jiangli2` 0.822 ✔,存盘目视确认落点正确。
    - 全分辨率未压缩 **~14 ms/张** → 证实可默认不压缩。
  - **三层缩放全部省掉并验证**:命中中心即客户区可点坐标,无 DPI 换算、无压缩还原、无 real_pos/scal_rate。
    `ClickSpec` 已改为客户区归一化坐标(0~1)。详见 §2.2 / §9.2。
  - 待办:多开选窗顺序不稳定(`Enumerate` 顺序不定),后续改为可显式指定句柄;OpenCvSharp 的 NativeAOT 打包验证仍未做。
- **P3 ✅ 已验证**(2026-07-22)。落地内容:
  - `SendMessageInput : IInputSender`(Windows):`WM_ACTIVATE + WM_LBUTTONDOWN/UP`,lParam = **客户区坐标**(无 `-40` 修正),按下时长随机。
  - `OffsetSampler`(Core.Humanize):重写拟人化偏移——截断正态 + 偏向窗口内侧 + 边界截断,丢弃旧版九宫格旋转与魔法系数,4 个统计断言单测。
  - 诱饵点击:按用户决定**默认关闭**(`DecoyClickProbability` 默认 0),当前不实现,保留为可选。
  - Host 增加 `click <标题> [目录] [目标]` 冒烟:点击前后各截一帧、算画面差异。
  - **实测**(对真实运行的「阴阳师」):点击客户区中心(拟人化落点 623,287),游戏领奖界面响应("点击屏幕继续"消失、场景推进),前后差异 13.6%。后台点击在该 DirectX 游戏上生效。
- **里程碑**:两个最高风险原生环节(后台截图 + 后台点击)与视觉匹配均已对真实游戏跑通。
- **P4 ✅ 已完成**(2026-07-22)。落地内容:
  - `AutomationEngine : IAutomationEngine`(Core.Engine):串起 截图 → `MatchFirst` 优先级匹配 → `RoundStateMachine` 裁决 →
    偏移点击基准(`ClickSpec` 归一化坐标 or 匹配中心)→ `OffsetSampler` 拟人化 → `IInputSender` 后台点击 → 区间随机等待 的循环。
    经 `Channel<EngineEvent>` 发结构化事件;`CancellationToken` 协作式取消;单窗口异常(句柄失效/截图失败)只告警跳过、不拖垮整轮;
    启动时按 `SetPriority` 设进程优先级(失败仅告警)。取代旧 `run()`:无 `print`、无 `terminate()`、无魔法上限。
  - **集成单测(P4 验收:假实现下状态机分支全绿)**:`EngineFakes`(SequenceCapturer 打周期号 / ScriptedMatcher 按脚本回放 / RecordingInput / FakeWindowService)+
    `AutomationEngineTests` 10 例,覆盖 RoundStart 计回合与事件、Once 每回合一次、Skip 匹配不点且抑制 Once、Stop 终止、连续同名终止、
    无匹配容错、ByRounds 完成、取消 → `Cancelled`、`ClickSpec` 归一化→像素、启动设优先级。**全解决方案 25 测试全绿**。
  - Host 增 `run <标题> <目录> [分钟]` 冒烟命令(真引擎驱动真窗口;目录图暂按 `Normal`,Ctrl+C 停)。
  - 已知取舍:多开当前共用一台 `RoundStateMachine`(对齐设计 §7),逐窗口独立状态列为后续细化;`run` 的 flag 语义待 P6 导入器。
- **P4 后 · 关键根因排查(2026-07-22)——后台点击"没反应"**:
  - 现象:`run` 冒烟里 `SendMessage` 点击"挑战"按钮无效(连续同名触发卡住保护),后台/前台点击画面差异均 ~1%。
  - 逐条排除:子窗口(无)、坐标(match 1.000、落点在按钮上)、DPI/缩放(系统 125% 但游戏窗口自身 PerMonitor 感知、物理 1200×600,坐标处理正确)、消息序列(与旧版 py 逐字一致)。新增 `children`/`geom`/按句柄选窗(`0x…`)三个诊断能力。
  - **根因:UIPI/权限**。阴阳师进程以**管理员**运行;未提权进程的合成输入被 Windows 静默丢弃(`SendMessage` 还照返回成功)。经 UAC 提权跑同一条后台点击 → **画面差异 96.3%,成功进入战斗加载**。
  - 结论:**本工具必须以管理员运行**(旧版 py 一直以管理员跑,故一直可用)。之前 P3 的"点击屏幕继续生效"实为用户手点,后台点击当时即被拦——已在文档更正。`ForegroundInputSender`(前台/兼容模式)已落地并保留为备选。
  - **待办**:给发行版加 `app.manifest`(`requestedExecutionLevel requireAdministrator`)或启动时自提权;开发期冒烟仍用"提权终端 / `Start-Process -Verb RunAs`"跑触及游戏的命令。
- **P4 后 · `run` 冒烟实跑(2026-07-22)**:以管理员对真实游戏(阴阳师 1200×600,`img/juexing`:`start`/`end`)跑通 `run` 冒烟,截图→匹配(分数 1.00)→后台点击→轮末间隔 循环正常,`end`/`start` 稳定命中并点击。
- **P5 防检测已完成(2026-07-22)**:`AntiDetectionPolicy`(`Core/Humanize`)两层叠加——概率随机等待 + 时间窗口频次上限,均以「开局 RoundStart」为计数节拍、`Enabled` 各自独立可开关(配置进 `EngineOptions.AntiDetection`)。
  - **等待改到轮末组级施加**(引擎循环重构:`ProcessWindowAsync` 回传本窗口 `RoundStarted`,外层聚合后统一等待),修正了设计原稿「接在点击后」的 per-窗口方案——**从根上修掉双开组队「一窗口开局、另一窗口在等」的错位**(游戏侧还靠「`start` 按钮需队友进房才可点」兜底时序,故无需跨轮临界区)。
  - 纯逻辑、时钟 `TimeProvider` 注入:新增 8 个策略单测(概率/最小间隔/频次窗口滑动/两层叠加)+ 3 个引擎接线单测(轮末发 `Waiting`、`roundStarted` 上报、双开合并为一轮),**全解决方案 36 单测全绿**。
- **P6 配置与目标已完成(2026-07-22)**:
  - **`target.json` schema**(`Core/Targets/TargetSetJson.cs`):显式、可迁移的目标定义(camelCase + 字符串枚举 flag + defaults + 归一化 `clickPos`),取代旧 `img_pos.json` 靠文件名字符串关联行为那套。`TargetSetSerializer` 读写,`TargetSetLoader` 把 DTO 解析为运行期 `TargetSet`(解析路径、套用 defaults、`clickPos`→`ClickSpec`)。
  - **`img_pos.json`→`target.json` 导入器**(`Core/Targets/ImgPosImporter.cs`,纯函数可离线单测):flag 空串→Normal / start→RoundStart / mark→Once / skip→Skip / stop→Stop(未知→Normal+告警);**优先级按文件名排序**赋值(step 10)忠实还原旧「文件夹遍历顺序+早退」次序(`00_marked`/`0_end` 前导数字文件名天然排最前);目录有图但 json 没配→Normal 收入,json 配了但缺图→告警跳过。**旧 `click_pos`/`real_pos` 坐标一律不导入**——经确认几乎全错、且坐标系不兼容;`click` 留空 → 引擎点匹配中心。
  - **`EngineOptions` 校验**(`Core/Config/EngineOptionsValidation.cs`):纯逻辑,返回可读错误列表(区间/阈值/频次/概率等逐项校验),`ValidateAndThrow` 供组合根启动时拦非法配置;P7 接 `IValidateOptions` 时复用。
  - **Host 接线**:新增 `import <目录>` 冒烟命令(落盘 `target.json` + 打印优先级/flag + 告警);`run` 的目标集加载改为三级优先:① `target.json` → ② 旧 `img_pos.json` **内存导入**(补齐 flag 语义、不落盘)→ ③ 目录扫描全 Normal 兜底——**旧目标集无需手动转换即用**,`run` 冒烟前也做配置校验。
  - **实测**:对 `img/yuling` 跑 `import` 通过——9 图正确映射 flag/优先级,中文文件名 `0协作.png` 正常,忠实告警缺图条目(reward2/xuzuo_mark),产出 `img/yuling/target.json`(仅 flag、无坐标)。
  - **决策(用户确认)**:旧 `click_pos`/`real_pos` 几乎全错,**不导入坐标**;偏移点击留待需要时在 `target.json` 手动补客户区归一化坐标(0~1)。
  - **单测**:新增 36 例(导入器 flag/优先级/告警/端到端 + 序列化器往返/defaults 覆盖 + 配置校验逐项),**全解决方案 72 单测全绿**。
- **P7 UI 进行中(2026-07-22)——第一切片:端到端可运行的 WebUI**:
  - **架构落地**:`SmartOnmyoji.Host` 从纯控制台组合根升级为**进程内 Kestrel(仅 127.0.0.1)+ 静态 WebUI + REST + 事件流**,再由 **WebView2 桌面窗口**(WinForms 宿主,STA 线程跑消息循环)指向它——即设计文档 §3 的主推路线。原有冒烟命令(`list`/`capture`/`match`/`click`/`run`/`import`…)**全部保留**,新增 `ui`(默认,开 WebView2 窗口)与 `serve [端口]`(headless / 浏览器模式,不依赖 WebView2 运行时,便于验证与无壳运行)。
  - **前端**:选**原生 HTML/JS/CSS**(用户确认;无构建步骤,`dotnet build` 自包含拷 `wwwroot` 到输出,不依赖 node)。`wwwroot/`:目标集下拉 + 缩略图(按 flag 上色)、窗口枚举/多选/倒计时点选、运行参数表单(模式/回合/时长/间隔/阈值/优先级/多开)、防检测折叠面板(两层等待 + 卡死保护逐项)、启动/停止、实时日志(按事件类型着色 + 进度条 + 回合数),主题随系统明暗。
  - **事件推送改用 SSE**(浏览器原生 `EventSource`)而非设计原稿 §10 的 SignalR:原生前端 + WebView2 离线场景下 SignalR 需 vendoring JS 客户端;SSE 零客户端依赖、天然契合「服务端→前端单向事件流」,**同一意图更轻的实现**。命令仍走 REST(`POST /api/engine/start|stop`、`/api/windows/pick`…)。
  - **后端接线**:`EngineManager`(单例)持有引擎生命周期,把 `AutomationEngine` 的 `EngineEvent` 序列化后**扇出**给所有 SSE 订阅者并维护可查询状态;订阅即补状态快照 + 最近事件。引擎本身仍**不知道 UI 存在**——`EngineManager` 是唯一把事件流接到 HTTP 的地方(对齐 §10)。`TargetSetCatalog` 集中 P6 的「三级加载优先」,冒烟命令与 WebUI 共用、避免漂移。DTO 层(`OptionsMapping`)把 UI 表单 ↔ 类型化 `EngineOptions` 双向映射,启动前复用 `EngineOptionsValidation` 拦非法配置。
  - **实测(headless `serve` + curl)**:静态首页/`app.js`/`style.css` 正常;`/api/targets` 列出 12 目标集(含 target.json / img_pos.json / 目录扫描三种来源);`/api/targets/yuling` flag/优先级正确;缩略图端点返回图片字节且**拦路径穿越**(`../` → 404);`/api/windows` 枚举 22 窗口;`/api/engine/start` 空窗口/坏句柄均被校验拦下并回可读错误;SSE 首帧 `: connected` + 状态帧正常;**全解决方案 72 单测仍全绿**。
- **P7 UI 第二切片(2026-07-22)——目标管理写侧(§9.3)+ 运行匹配缩略图**:
  - **目标管理写侧**:WebUI 加「目标管理」页签,把「摆文件 + 手写 json」整套流程 UI 化。
    - **建集**:输入玩法名(校验为安全目录名)→ `POST /api/targets/create` 建 `img/<名>/` + 空 `target.json`。
    - **软件内截图取模板**:选窗口 → `POST /api/capture` 后台截客户区回 PNG → 前端 canvas **框选**(显示像素按 `naturalWidth/clientWidth` 比换算回自然像素,保真裁剪)→ `POST /api/targets/{name}/images`(dataURL 落盘)。
    - **可视化编辑**:`GET /api/targets/{name}/edit` 返回**对账后**的 target.json 形状(磁盘图片 ∪ json 条目:新截的图补 Normal+递增优先级,json 里缺图的条目丢弃)——天然与 `PUT` 同形状、可往返;逐图设 flag(下拉)/优先级(数字)/删图,以及在截图上**点选客户区归一化偏移点击点**(单击加点、可清空);`PUT /api/targets/{name}` 用 Core 的 `TargetSetSerializer`(字符串枚举 flag)写回。`target.json` 全程由软件托管,用户不碰 JSON。
    - **写侧全在 `TargetSetCatalog`**(`CreateSet`/`SaveImage`/`SaveJson`/`DeleteImage`/`EditModel`),文件名/名称一律**裸文件名校验 + 拦路径穿越**;PNG 编码用 `DebugImage.EncodePng/EncodeGrayPng`(Windows 层 System.Drawing,不落盘)。
  - **运行匹配缩略图**:`TargetMatched` 加可空 `GrayThumbnail`(Core 只持中立灰度裁剪,以命中中心裁 160×120、边界截断);`AutomationEngine` 命中时附带,`EngineManager` 把灰度编码成 8bpp PNG → base64 `data:` 塞进 `matched` 事件的 `thumb` 字段;前端日志行内联展示。**对齐 §10「匹配缩略图作为事件字段推送」**。
  - **实测(headless `serve` + curl)· 第二切片**:`create`(含重名/非法名/路径穿越拒绝)→ `images` 存图(含无扩展名/穿越文件名拒绝)→ `edit` 对账 → `PUT` flag+clickPos 持久化 → 缩略图端点 200 → `DELETE` 落盘生效,整链往返正确;**全解决方案 72 单测仍全绿**。
  - **WebView2 外壳实机验证(用户 2026-07-22)**:`dotnet run -- ui` 在真机启动 WebView2 桌面窗口,加载本地 Kestrel WebUI,**功能大致正常**。至此设计文档 §3 主推路线(进程内 Kestrel + 静态 WebUI + WebView2 壳)端到端跑通。细节打磨(具体交互手感/边界情况)按需继续。
  - **样式改用 Tailwind v4(用户要求,2026-07-22)**:选**官方 standalone CLI(无 node)+ 提交产物**路线——源 `Styles/app.tw.css`(`@import "tailwindcss"` + `@theme` 令牌 + `@layer components`)经 `tools/tailwindcss.exe`(v4.3.3,已 gitignore)生成**已提交**的 `wwwroot/tailwind.css`。**`dotnet build` 默认不跑 CLI、直接用已提交产物**(保住「构建不依赖 node/工具链、离线可用」);改样式用 `-p:TailwindBuild=true`(可选 MSBuild 目标)或 CLI watch。设计令牌随系统明暗、preflight + 工具类可用于 HTML;现有部件类平移进 `@layer components`,HTML/JS 无改动、零回归。(选此路线而非 CDN/浏览器 JIT/npm:CDN 破离线、npm 违背无 node 构建;见对话决策。)
  - **仍未做**:多开逐窗口独立状态机细化;真运行下的缩略图流与框选/点选交互的深度实测(功能已具备,待随真机使用打磨)。
- **P7 打磨(2026-07-23)——命中缩略图标出点击落点 + 点击位置分析(轻量版)**:
  - **点击落点红点**:`GrayThumbnail` 加可空 `ClickMark`(把拟人化后的实际点击点换算到缩略图坐标系,落裁剪框外则不标);`AutomationEngine` 将点击落点计算**提前到发 `TargetMatched` 之前**(仅 `Click` 分支算,`Skip`/`Stop`/`mark` 不点击故不标),`EngineManager` 把 `ClickMark` 归一化成 `mark` 塞进 `matched` 事件,前端在命中缩略图上叠红点(`.click-dot`,归一化百分比定位、随缩放自适应)。直观回答"这一下点在了图的哪儿"。
  - **点击位置分析(轻量版,不落文件)**:运行日志头加「点击分析」弹窗,基于事件流累积**本次运行**的点击点,`canvas` 自绘(无第三方库)三图——按目标着色的点击散点、各目标点击次数、点击时段分布(相对用时分桶);启动新运行即重置、关页即清。旧版是独立 `modules/tools/log_analysis.html` 读落盘的 `click_log_*.txt` + echarts 画历史;新版事件流已结构化,**取轻量 session 内实时统计**(用户 2026-07-23 选定此程度,而非落文件历史 + 独立分析页)。
  - **修复散点图边界(用户反馈)**:最初按「本次点击点的 min/max」定坐标轴范围,点集中在窗口一角时会看不出这批点落在整个窗口的什么位置,且不保持宽高比。改为坐标轴范围用**本次运行选中窗口的真实客户区分辨率**(`/api/windows` 已带 `width`/`height`,`start()` 时按 `selectedHandles` 记下,双开分辨率不同取最大值兜底)、按窗口宽高比 **contain 适配(letterbox)**,避免拉伸失真;只有理论上不会出现的"取不到窗口尺寸"兜底分支才退回旧的按数据范围。
  - Core 新增 1 单测(`Click_marks_thumbnail_with_click_point_and_leaves_noclick_unmarked`:点击标记落缩略图正中、不点击不标);样式经 `tools/tailwindcss.exe` 重新生成并提交 `wwwroot/tailwind.css`。
  - **修复「运行页匹配阈值不生效」+ 删除每图/默认阈值(用户反馈)**:根因是 `TargetSetLoader` 用 `img.Threshold ?? defaults.Threshold` 给每图都灌满阈值(默认 0.80),`MatchFirst` 又让每图阈值覆盖运行级 base,于是运行页 UI 阈值(如 0.9)永远被 target.json 的 defaults(0.80)架空——分数 0.82 仍命中点击。经确认「每图阈值」既无 UI 入口也无实际用途,**彻底删除目标集里的每图/默认阈值**(`TargetImage.ScoreThreshold`、`TargetImageJson.Threshold`、`TargetDefaultsJson.Threshold` 及 loader/`MatchFirst` 覆盖逻辑),运行阈值**只保留唯一来源 `EngineOptions.Match.Threshold`(运行页「匹配阈值」),对所有图统一生效**;匹配器(matcher/Hint)每图覆盖保留。旧 target.json 残留的 `threshold` 字段反序列化时被静默忽略(向后兼容)。新增 `MatcherExtensionsTests` 锁死回归,**全解决方案 75 单测全绿**。
- **P8 打包进行中(2026-07-23)——提权 manifest + 自包含单文件发布**:
  - **`app.manifest`(`requestedExecutionLevel = requireAdministrator`)**:发行版启动即触发 UAC 提权,从根上解决 UIPI 静默丢弃合成输入的问题(见本节 P4 后根因)。**仅 Release 声明**(`csproj` 里 `ApplicationManifest` 条件 `Configuration == Release`),**Debug 保持不提权**——让 headless `serve`+curl / `list` / `match` 等"不触及游戏"的开发/验证命令免 UAC(对齐 CLAUDE.md);Debug 下跑触及游戏的命令用 `sudo <命令>` 临时提权。
  - **DPI 不写进 manifest**:仍由 `Program.cs` 启动时 `DpiAwareness.EnablePerMonitorV2()` 统一设置(P1 唯一事实源),避免双份声明;`EnablePerMonitorV2` 不检查返回值,即便 manifest 曾声明也不冲突。manifest 只加 `supportedOS`(Win10/11)。
  - **自包含单文件发布**(用户选定路线):发布配置 `Properties/PublishProfiles/win-x64.pubxml`——`SelfContained` + `PublishSingleFile` + `RuntimeIdentifier=win-x64` + `IncludeNativeLibrariesForSelfExtract`(OpenCvSharp `OpenCvSharpExtern`、WebView2 loader 等原生 dll 塞进 exe,启动自解压)+ `IncludeAllContentForSelfExtract`(`wwwroot` 随 exe 走,真·单文件分发)+ `EnableCompressionInSingleFile`;`AllowedReferenceRelatedFileExtensions=.dll` 去掉引用项目 pdb,发布目录只剩一个 exe。用法:`dotnet publish src/SmartOnmyoji.Host -p:PublishProfile=win-x64`。
  - **`img/` 根目录解析改造**(发行阻塞点):旧 `TargetSetCatalog.ImgRoot` 从 bin 回溯 6 层取仓库 `img/`,单文件装到干净机器后 `AppContext.BaseDirectory` 变成自解压临时目录、回溯必然找不到。改为三级 `ResolveImgRoot()`:① 环境变量 `SMARTONMYOJI_IMG_ROOT` 覆盖 → ② 开发期仓库 `img/`(存在即用,**开发流不变**)→ ③ 发行期用 `Environment.ProcessPath` 定位**真实 exe 同级 `img/`**(便携、可写,不落进临时目录)。
  - **无控制台黑框**(用户反馈):`OutputType` 从 `Exe`(控制台子系统)改 `WinExe`(GUI 子系统)——发行版跑 `ui` 不再弹黑色控制台窗口。控制台冒烟命令经 `dotnet run` 跑时 dotnet 重定向子进程 stdout 转发到终端,与子系统无关、输出照常(实测 `list` 正常;发布 exe 的 PE Subsystem 已为 2=GUI)。副作用:GUI 子系统下启动失败无控制台可打印,靠 `MainForm` 的 WebView2 失败弹窗兜底,后续可加致命错误 MessageBox。
  - **运行参数持久化到 exe 同级 `config.json`**(用户要求):`Ui/OptionsStore`(读缺失/损坏回落默认、写失败静默;路径用 `Environment.ProcessPath` 取真实 exe 目录)+ `GET/PUT /api/options`(保留 `/api/options/default` 供"恢复默认")。前端启动读 `/api/options` 记住上次设置,控制卡片改动**防抖 600ms** 写回;`EngineManager.Start` 校验通过后也存一次兜底。旧版只有硬编码默认、不记忆,此为补齐设计 §9.1 未落地的持久化。
  - **打包自动带 `img/`**(用户要求):csproj `CopyImgToPublish` 目标(`AfterTargets=Publish`)把仓库根 `img/`(目标集数据,已 gitignore)拷到 `$(PublishDir)img/`,与发行版 exe 的"同级 img/"解析对齐 → `dotnet publish` 产物 `exe + img/` **开箱即用**,无需手动拷。img/ 是数据非二进制,不进单文件 exe。
  - **实测**:① `dotnet build` 全绿(0 警告);Release exe 内含 `requireAdministrator`、PE Subsystem=GUI,Debug exe 为默认 `asInvoker`(grep + PE 头确认)。② `dotnet publish` 产出 `bin/publish/win-x64/` = **单个 ~97MB exe + img/**(img 由 `CopyImgToPublish` 自动拷,含全部目标集与 target.json/img_pos.json)。③ 用**同配置 Debug 单文件**(免 UAC)`serve` + curl 实证运行时:原生库自解压、秒启动;内嵌 `index.html`/`tailwind.css`/`app.js` 均 200;仓库外裸 exe 无 img → `/api/targets` 空,exe 同级建 `img/testset` → 立即识别(证 `Environment.ProcessPath` 解析生效);`config.json` 往返(默认 → PUT → 读回改后值,落盘 exe 同级)。④ **全解决方案 72 单测仍全绿**。
- **下一步**:**P8 余下**——评估 Trimming(OpenCvSharp/WinForms 大量反射,裁错风险高,当前 `PublishTrimmed=false`)与 NativeAOT(已知未验证);Release 单文件真机提权跑一次(本机非交互环境跑 UAC 会卡,已用 Debug 单文件代验机制);评估安装包 / 自动更新;并行 P7 打磨(按真机使用反馈)。诱饵点击/时段警告(`DecoyClick`/`PlaytimeWarning`)仍留作可选后续补。
- **P7 打磨(2026-07-24)——目标管理:打开文件夹 + 优先级自动化**:
  - **打开文件夹**:`TargetSetCatalog.OpenFolder(name)` 用 `Process.Start(UseShellExecute=true)` 在资源管理器打开该目标集在磁盘上的 `img/<name>/` 目录;`ApiEndpoints` 新增 `POST /api/targets/{name}/open-folder`;WebUI「编辑目标集」卡片加同名按钮(未选目标集时禁用)。
  - **优先级改为按列表顺序自动设置**:去掉图片列表里手填的优先级数字框,优先级 = 图片在列表中的先后顺序,每次渲染重算(`recomputePriorities()`);列表项加拖拽手柄(仅按住手柄才把该行 `draggable` 置真,避免误触下拉框/按钮触发拖拽),原生 HTML5 drag-and-drop 调整顺序即改优先级,仍需手动点「保存 target.json」才落盘。
  - **加表头 + 踩坑记录**:图片列表上方加「图片名/标记/优先级」说明行。首版对不齐——每行(含表头)是各自独立的 CSS Grid,末尾按钮列原用 `auto` 宽度:数据行里按钮文字撑开了列宽,表头对应格子是空的,`auto` 便退化成 0,导致表头「图片名」那一列(`1fr`)在表头行里被拉宽、后面的列整体右移错位。改成固定 px 宽度(`92px 56px 44px`)后,表头与数据行的列宽不再依赖各行自身内容,才真正对齐。
  - 样式经 `tools/tailwindcss.exe` 重新生成并提交 `wwwroot/tailwind.css`;不涉及 Core,全解决方案编译 0 警告 0 错误,单测数不变。
- **P7 打磨(2026-07-24)——目标管理:截图/框选/点选放大为弹窗编辑器**:
  - **问题**:左侧「截图取模板」卡片把截图画布嵌在 400px 窄栏里(`#captureImg` 实际只能撑到约 360px 宽),1200×600 的游戏截图被缩得很小,框选模板 / 点选偏移点都难以精确操作(用户反馈)。
  - **改法**:复用运行页「点击位置分析」同款遮罩弹窗模式,新增 `#captureModal`(`min(1400px, 94vw)`)。左侧卡片只留窗口筛选/选择/截图按钮;点「截图」成功、或在右侧图片行点「偏移点」时打开弹窗。原截图区的全部 DOM(`captureStage`/`captureImg`/`selRect`/`pointLayer`)与保存控制原样搬进弹窗,框选拖拽/归一化点选的坐标换算逻辑不变(仍按 `imgRect()` 相对定位),只是 `captureImg` 从撑满容器改成 `max-width/max-height` 居中 letterbox 显示;新增 `.capture-canvas` 包裹层,确保 letterbox 留白下 `selRect`/`pointLayer` 的绝对定位仍与图片精确对齐(而非相对外层弹窗偏移)。若在右侧点「偏移点」但本次会话还没截图,不再打开空弹窗死路,改为 toast 提示先去左侧截图。
  - **实测**:临时 Playwright 脚本跑 `serve` headless,连上本机真实运行的阴阳师窗口截图成功,弹窗按预期打开、拖拽框选的选区与鼠标轨迹像素级对齐(误差 <1px)、关闭按钮正常隐藏、全程无 JS 控制台报错。
  - **弹窗内加「重新截图」**(用户反馈):原先弹窗打开后要重截图必须先关闭弹窗回到左侧卡片点「截图」,再重新打开弹窗,来回丢工作流。弹窗头部工具栏新增 `#mgRecapture` 按钮,直接复用 `mgCapture()`(读左侧仍在 DOM 中的 `mgWindowSelect` 当前选中句柄)重新拉一帧,弹窗保持打开、`captureImg` 原地刷新。Playwright 实测:点「重新截图」后 `src` 变为新 blob(游戏画面确已推进到下一帧)、弹窗未关闭、无控制台报错。
  - 样式经 `tools/tailwindcss.exe` 重新生成并提交 `wwwroot/tailwind.css`;不涉及 Core,全解决方案编译 0 警告 0 错误,单测数不变。
- **P7 打磨(2026-07-24)——运行自然结束弹系统通知**(用户要求:跑完指定回合/时长不用一直守着):
  - **落点**:`Host/Ui/EngineManager.OnEngineEvent` 收到 `EngineStopped` 时,若 `Reason != Cancelled`(即到达回合/时长上限的 `Completed`、命中终止图的 `StopFlag`、卡死保护的 `RepeatedSameTarget`)才通知;用户手动点「停止」不弹——那种情况本来就在看着界面。
  - **`Host/Ui/CompletionNotifier`**:独立 STA 后台线程起临时 `NotifyIcon` 显气泡通知(6s)+ `SystemSounds.Asterisk` 提示音,不依赖 `ui`(WebView2 消息循环)或 `serve`(无消息循环)哪种宿主模式在跑——两边都能弹,失败静默不影响引擎主流程。放 Host 层(不进 Core):Core 引擎不知道 UI/系统通知存在,`EngineManager` 仍是唯一把事件接到宿主能力的地方,对齐 §10。
  - 全解决方案编译 0 警告 0 错误,75 单测全绿(不涉及 Core,无新增单测)。
- **模板跨分辨率复用(2026-07-26)——`baseSize` + 缩放模板匹配**(用户确认:痛点就是"模板在 A 分辨率截的想在 B 分辨率跑,不用多次截同样的图"):
  - **背景**:先分析了旧版第二条匹配路径 SIFT 特征匹配(`GetPosBySiftMatch`),结论是**不移植**——UI 图标低纹理正是 SIFT 弱项、8 自由度单应对 1 自由度问题过剩、每轮整帧特征提取贵一个量级、且给不出与模板匹配可比的分数。改用**记基准尺寸 + 缩放模板**直击痛点。完整取舍与实测见 §9.4。
  - **Core**:`TargetImage.BaseSize` / `TargetImageJson.baseSize`(`{width,height}`)+ `TargetSetLoader` 映射(非正尺寸当未记录);新增 `Core/Matching/TemplateScale.cs` 纯函数裁决缩放比(等比取均值、不等比取较小、比例离谱或缺失一律退回 1.0 不缩放)。
  - **Vision**:`TemplateMatcher` 按缩放比重采样模板再匹配,按(路径+目标像素尺寸)缓存缩放结果;与压缩复合时**模板总缩放 = 适配比 × 压缩比,命中坐标只按压缩比还原**。`ImageOps.ResizeTo`(缩小 Area / 放大 Cubic)。
  - **Host/WebUI**:存模板时前端带整帧尺寸 → `SaveImage` 立即把 `baseSize` 写进 `target.json`(此值只有截图那刻知道,不等用户点保存);图片列表每行显示「基准 1200×600」/「基准未记录 · 不缩放」;引擎启动时若当前客户区与模板基准不同,日志报一条 `按 ×0.83 缩放后匹配`。
  - **新增 `scalematch` 离线冒烟命令**:目录内按 `X_full.jpg`(整帧)↔ `X.jpg`(从中裁的模板)配对,把整帧缩放到各比例后,**对照**打印"记了 baseSize" vs "旧行为"的分数与坐标误差。不需要游戏窗口,纯文件输入,可反复回归。
  - **实测**(素材 `img/test/`):×0.75/×0.90/×1.25/×1.50 四档下,记了 `baseSize` 的分数 **0.914~0.996 全部命中、坐标误差 ≤1px**;旧行为 0.281~0.691 **全部低于 0.80 阈值**、坐标偏离最多 667px。写侧经 headless `serve` + REST 实证:存图落 `baseSize` → `/edit` 往返 → `PUT` 整份配置后仍在 → DTO 透出。**全解决方案 98 单测全绿**(新增 `TemplateScaleTests` 14 项 + schema 往返 2 项)。
  - **顺带修**:冒烟命令的中文输出在 Windows 控制台默认代码页下一直是乱码,`Program.cs` 启动时显式设 `Console.OutputEncoding = UTF8`(无控制台时静默忽略)。

---

## 12. 测试策略

- **Core 单测**(xUnit,无平台依赖):
  - `RoundStateMachine`:每条 flag 分支、连续同名终止、mark/skip 抑制。
  - `OffsetSampler`:统计断言(越界率、偏向内侧、分布形状)。
  - `WaitPolicy` / `FrequencyCap`:用可注入时钟(`TimeProvider`)验证触发时机。
  - `MatchFirst` 优先级选择:用假 `IMatcher`。
- **Vision 集成测试**:固定样例截图 + 目标图,断言坐标落在期望框内。
- **Windows 层**:手动/半自动冒烟(截图存盘核对、真机点击核对),难自动化的部分列为人工检查清单。

---

## 13. 风险与验证点

| 风险 | 验证/缓解 |
|---|---|
| **OpenCvSharp 在 NativeAOT 下的原生依赖** | P2 就做一次 AOT 试打包验证;不行则退回单文件+Trimming |
| **PrintWindow 对部分窗口/渲染方式失效** | P1 保留 BitBlt 回退;必要时预研 `Windows.Graphics.Capture` |
| **DPI / 客户区坐标换算** | app.manifest 声明 Per-Monitor-V2;P1 用存盘帧核对尺寸与坐标 |
| ~~**后台点击被游戏忽略**~~(已定位:**UIPI/权限**,非反外挂) | **已解决**:阴阳师进程以管理员运行,未提权进程的合成输入(`SendMessage`/`mouse_event`)被 UIPI 静默丢弃。**必须以管理员运行**。旧版 py 一直以管理员跑故一直可用。`ForegroundInputSender` 前台方案保留为备选(兼容模式) |
| **拟人化改写后手感/防检测效果变化** | 用统计断言 + 与旧模型采样分布对比;参数可调 |
| **多开并行下的句柄失效/竞态** | 每窗口独立状态;失效即跳过并告警,不影响其它窗口 |

---

## 附录 A:旧目标 flag 语义 → 新枚举映射

| img_pos.json `flag` | 语义 | 新 `TargetFlag` |
|---|---|---|
| `""` | 普通图,匹配即点 | `Normal` |
| `start` | 回合起点,复位 mark,回合数 +1 | `RoundStart` |
| `mark` | 本回合只点一次 | `Once` |
| `skip` | 匹配但不点,并抑制本回合 mark | `Skip` |
| `stop` | 触发即终止脚本 | `Stop` |

## 附录 B:坐标系约定

全链路统一使用**目标窗口客户区坐标系**(原点 = 客户区左上角,单位 = 物理像素)。截图捕获客户区、匹配在客户区图上出点、点击用客户区坐标 `SendMessage`,不再出现 `-40` 之类跨坐标系的硬编码修正。
