using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SmartOnmyoji.Core;
using SmartOnmyoji.Core.Abstractions;
using SmartOnmyoji.Core.Config;
using SmartOnmyoji.Core.Engine;
using SmartOnmyoji.Core.Events;
using SmartOnmyoji.Core.Humanize;
using SmartOnmyoji.Core.Matching;
using SmartOnmyoji.Core.Targets;
using SmartOnmyoji.Host.Ui;
using SmartOnmyoji.Vision;
using SmartOnmyoji.Windows;
// WinForms(WebView2 外壳)引入 System.Drawing 后,Point/Size 与 Core 同名类型冲突,显式取 Core 版。
using Point = SmartOnmyoji.Core.Point;
using Size = SmartOnmyoji.Core.Size;

// 冒烟命令输出大量中文,而 Windows 控制台默认代码页(GBK/CP437)会把它显示成乱码。
try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* 无控制台(WebView2 外壳)时忽略 */ }

// 全项目任何窗口/截图操作前,先声明 DPI 感知
DpiAwareness.EnablePerMonitorV2();

// 默认进 P7 的 WebUI;冒烟命令(list/capture/match/click/run/import…)仍可显式调用。
var command = args.Length > 0 ? args[0].ToLowerInvariant() : "ui";

switch (command)
{
    case "ui":
        await WebUiApp.RunAsync(shell: true);
        return;
    case "serve":
        // Headless / 浏览器模式:只起 Kestrel,不开 WebView2 窗口。可指定端口(默认 8770)。
        await WebUiApp.RunAsync(
            shell: false,
            port: args.Length > 1 && int.TryParse(args[1], out var p) ? p : 8770);
        return;
    case "demo":
        await RunFakeDemoAsync();
        return;
    case "list":
        ListWindows();
        return;
    case "capture":
        CaptureWindow(args.Length > 1 ? args[1] : null);
        return;
    case "match":
        MatchWindow(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : "huntu");
        return;
    case "scalematch":
        ScaleMatch(args.Length > 1 ? args[1] : "test", args.Skip(2).ToArray());
        return;
    case "click":
        ClickWindow(
            args.Length > 1 ? args[1] : null,
            args.Length > 2 ? args[2] : null,
            args.Length > 3 ? args[3] : null);
        return;
    case "fgclick":
        ClickWindow(
            args.Length > 1 ? args[1] : null,
            args.Length > 2 ? args[2] : null,
            args.Length > 3 ? args[3] : null,
            foreground: true);
        return;
    case "children":
        ListChildren(args.Length > 1 ? args[1] : null);
        return;
    case "geom":
        ShowGeometry(args.Length > 1 ? args[1] : null);
        return;
    case "import":
        ImportTargets(args.Length > 1 ? args[1] : null);
        return;
    case "run":
        await RunEngineAsync(
            args.Length > 1 ? args[1] : null,
            args.Length > 2 ? args[2] : null,
            args.Length > 3 && double.TryParse(args[3], out var mins) ? mins : 1.0);
        return;
    default:
        await RunFakeDemoAsync();
        return;
}

static void ListWindows()
{
    var service = new WindowService();
    var windows = service.Enumerate();
    Console.WriteLine($"可见窗口 {windows.Count} 个:");
    foreach (var window in windows.OrderBy(w => w.Title, StringComparer.OrdinalIgnoreCase))
    {
        var size = "?x?";
        try
        {
            var client = service.GetClientArea(window.Handle);
            size = $"{client.Width}x{client.Height}";
        }
        catch
        {
            // 忽略取尺寸失败的窗口
        }
        Console.WriteLine($"  0x{window.Handle:X}  pid={window.ProcessId,-6}  {size,-11}  {window.Title}");
    }
}

static void ShowGeometry(string? titleQuery)
{
    var service = new WindowService();
    var target = ResolveTarget(service, titleQuery);
    if (target is null) { Console.WriteLine("未找到目标窗口。"); return; }

    var g = service.DescribeGeometry(target.Handle);
    string Awareness(int a) => a switch { 0 => "Unaware", 1 => "System", 2 => "PerMonitor", _ => $"?({a})" };

    Console.WriteLine($"目标窗口 : {target.Title}  (0x{target.Handle:X})");
    Console.WriteLine($"WindowRect: {g.WindowRect.Left},{g.WindowRect.Top} ~ {g.WindowRect.Right},{g.WindowRect.Bottom}  ({g.WindowRect.Width}x{g.WindowRect.Height})");
    Console.WriteLine($"客户区尺寸: {g.ClientWidth}x{g.ClientHeight}");
    Console.WriteLine($"客户区原点(屏幕): {g.ClientOrigin.X},{g.ClientOrigin.Y}");
    Console.WriteLine($"左边框    : {g.LeftBorder}px    上边(标题栏): {g.TopBar}px  ← 旧版就是硬编码 -40 这个");
    Console.WriteLine($"窗口 DPI  : {g.WindowDpi}  (缩放 {g.WindowDpi / 96.0:P0})   系统 DPI: {g.SystemDpi} (缩放 {g.SystemDpi / 96.0:P0})");
    Console.WriteLine($"窗口 DPI 感知: {Awareness(g.WindowAwareness)}    我们进程: {Awareness(g.ProcessAwareness)}");
}

static void ListChildren(string? titleQuery)
{
    var service = new WindowService();
    var target = ResolveTarget(service, titleQuery);
    if (target is null) { Console.WriteLine("未找到目标窗口。"); return; }

    var children = service.EnumerateChildren(target.Handle);
    Console.WriteLine($"目标窗口 : {target.Title}  (0x{target.Handle:X})");
    Console.WriteLine($"子窗口 {children.Count} 个:");
    foreach (var c in children)
        Console.WriteLine($"  0x{c.Handle:X}  {(c.Visible ? "可见" : "隐藏")}  {c.ClientWidth}x{c.ClientHeight}  类名[{c.ClassName}]");
}

static WindowInfo? ResolveTarget(WindowService service, string? titleQuery)
{
    if (string.IsNullOrWhiteSpace(titleQuery))
    {
        Console.WriteLine("未指定标题,使用当前前台窗口。");
        return service.GetForeground();
    }

    // 按句柄精确选窗(如 0x1480B88),解决多开同名窗口取窗不确定
    if (titleQuery.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
        nint handle;
        try { handle = (nint)Convert.ToInt64(titleQuery[2..], 16); }
        catch { Console.WriteLine($"句柄格式非法: {titleQuery}"); return null; }
        var byHandle = service.Enumerate().FirstOrDefault(w => w.Handle == handle);
        if (byHandle is null) Console.WriteLine($"找不到句柄 {titleQuery} 的窗口。用 `list` 查看全部。");
        return byHandle;
    }

    var matches = service.Enumerate()
        .Where(w => w.Title.Contains(titleQuery, StringComparison.OrdinalIgnoreCase))
        .ToList();
    if (matches.Count == 0)
    {
        Console.WriteLine($"找不到标题包含 \"{titleQuery}\" 的窗口。用 `list` 查看全部。");
        return null;
    }
    if (matches.Count > 1)
    {
        Console.WriteLine($"匹配到 {matches.Count} 个窗口,取第一个:");
        foreach (var m in matches)
            Console.WriteLine($"  0x{m.Handle:X}  {m.Title}");
    }
    return matches[0];
}

static void CaptureWindow(string? titleQuery)
{
    var service = new WindowService();
    var target = ResolveTarget(service, titleQuery);
    if (target is null) { Console.WriteLine("未找到目标窗口。"); return; }

    var capturer = new GdiWindowCapturer();
    var (width, height, bgra) = capturer.CaptureBgra(target.Handle);

    var ratio = (double)CountNonBlack(bgra) / (width * height);
    var outputPath = ArtifactPath($"capture_{Sanitize(target.Title)}.png");
    DebugImage.SavePng(outputPath, width, height, bgra);

    Console.WriteLine($"目标窗口 : {target.Title}  (0x{target.Handle:X})");
    Console.WriteLine($"客户区   : {width}x{height}");
    Console.WriteLine($"非黑像素 : {ratio:P1}");
    Console.WriteLine($"已保存   : {outputPath}");
}

static void MatchWindow(string? titleQuery, string folder)
{
    var service = new WindowService();
    var target = ResolveTarget(service, titleQuery);
    if (target is null) { Console.WriteLine("未找到目标窗口。"); return; }

    // 一次截图,gray 与 bgra 出自同一帧,保证自裁剪 sanity 的确定性
    var capturer = new GdiWindowCapturer();
    var (width, height, bgra) = capturer.CaptureBgra(target.Handle);
    var frame = new CaptureFrame(width, height, GrayFromBgra(width, height, bgra));
    Console.WriteLine($"目标窗口 : {target.Title}  客户区 {width}x{height}\n");

    using var matcher = new TemplateMatcher();

    // ---- 1) 自裁剪回配:从本帧裁一块当模板,匹配回去应命中原位、分数≈1.0(确定性证明坐标数学正确)----
    int cw = 140, ch = 90, cx = width / 2 - cw / 2, cy = height / 2 - ch / 2;
    var cropBgra = CropBgra(bgra, width, cx, cy, cw, ch);
    var selfTemplatePath = ArtifactPath("_selftest_template.png");
    DebugImage.SavePng(selfTemplatePath, cw, ch, cropBgra);
    var selfTarget = new TargetImage { Name = "_selftest", FilePath = selfTemplatePath };
    var selfResult = matcher.Match(frame, selfTarget, new MatchOptions(MatchMethod.Template, 0.8, 1.0));
    Console.WriteLine("[自裁剪回配] 期望中心 " +
        $"({cx + cw / 2},{cy + ch / 2}) 分数≈1.00  ->  " +
        (selfResult is null
            ? "未命中(异常!)"
            : $"命中 ({selfResult.Center.X},{selfResult.Center.Y}) 分数 {selfResult.Score:0.000}"));

    // ---- 2) 真实模板:对 img/<folder> 里现成模板逐一匹配,阈值设 0 以显示所有分数 ----
    var folderPath = ImgFolderPath(folder);
    Console.WriteLine($"\n[真实模板] 目录 {folderPath}");
    if (!Directory.Exists(folderPath))
    {
        Console.WriteLine("目录不存在。");
        return;
    }

    var images = Directory.EnumerateFiles(folderPath)
        .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                 || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        .OrderBy(f => f)
        .Select((f, i) => new TargetImage { Name = Path.GetFileNameWithoutExtension(f), FilePath = f, Priority = i })
        .ToList();

    const double hitThreshold = 0.80;
    var scan = new MatchOptions(MatchMethod.Template, 0.0, 1.0); // 阈值 0 → 总返回分数
    var sw = Stopwatch.StartNew();
    var results = images
        .Select(img => (img.Name, Result: matcher.Match(frame, img, scan)))
        .OrderByDescending(x => x.Result?.Score ?? -1)
        .ToList();
    sw.Stop();

    foreach (var (name, result) in results)
    {
        if (result is null) { Console.WriteLine($"  {name,-18}  (模板大于截图,跳过)"); continue; }
        var hit = result.Score >= hitThreshold ? "✔ 命中" : "      ";
        Console.WriteLine($"  {name,-18}  分数 {result.Score:0.000}  @({result.Center.X},{result.Center.Y})  {hit}");
    }

    Console.WriteLine($"\n匹配 {images.Count} 个模板(全分辨率、未压缩)耗时 {sw.ElapsedMilliseconds} ms" +
        $"(平均 {(images.Count == 0 ? 0 : sw.ElapsedMilliseconds / (double)images.Count):0.0} ms/张)。");
    Console.WriteLine("→ 若耗时够低即可默认不压缩,省掉旧版整套压缩坐标还原。");
}

static void ClickWindow(string? titleQuery, string? folder, string? targetName, bool foreground = false)
{
    var service = new WindowService();
    var target = ResolveTarget(service, titleQuery);
    if (target is null) { Console.WriteLine("未找到目标窗口。"); return; }

    var capturer = new GdiWindowCapturer();
    var sampler = new OffsetSampler();
    IInputSender clicker = foreground ? new ForegroundInputSender() : new SendMessageInput();
    Console.WriteLine($"点击方式 : {(foreground ? "前台(真实光标+mouse_event)" : "后台(SendMessage)")}");

    // 点击前截图
    var (width, height, beforeBgra) = capturer.CaptureBgra(target.Handle);
    var beforePath = ArtifactPath("click_before.png");
    DebugImage.SavePng(beforePath, width, height, beforeBgra);
    Console.WriteLine($"目标窗口 : {target.Title}  客户区 {width}x{height}");

    // 决定点击基准点:给了 目录+目标 就匹配它,否则点客户区中心
    Point basePoint;
    if (folder is not null && targetName is not null)
    {
        var frame = new CaptureFrame(width, height, GrayFromBgra(width, height, beforeBgra));
        using var matcher = new TemplateMatcher();
        var tgt = new TargetImage
        {
            Name = targetName,
            FilePath = Path.Combine(ImgFolderPath(folder), targetName + ".jpg"),
        };
        if (!File.Exists(tgt.FilePath))
            tgt = tgt with { FilePath = Path.Combine(ImgFolderPath(folder), targetName + ".png") };
        var hit = matcher.Match(frame, tgt, new MatchOptions(MatchMethod.Template, 0.80, 1.0));
        if (hit is null) { Console.WriteLine($"未匹配到 {folder}/{targetName},取消点击。"); return; }
        basePoint = hit.Center;
        Console.WriteLine($"匹配目标 : {targetName} 分数 {hit.Score:0.000} @({basePoint.X},{basePoint.Y})");
    }
    else
    {
        basePoint = new Point(width / 2, height / 2);
        Console.WriteLine("未指定目标,点击客户区中心。");
    }

    var clickPoint = sampler.Sample(basePoint, width, height, 25);
    Console.WriteLine($"基准点   : ({basePoint.X},{basePoint.Y})  拟人化落点: ({clickPoint.X},{clickPoint.Y})");

    var outcome = clicker.Click(target.Handle, clickPoint);
    Console.WriteLine($"点击结果 : {(outcome.Success ? "已发送" : "失败")}");

    Thread.Sleep(900); // 等游戏响应

    // 点击后截图
    var (aw, ah, afterBgra) = capturer.CaptureBgra(target.Handle);
    var afterPath = ArtifactPath("click_after.png");
    DebugImage.SavePng(afterPath, aw, ah, afterBgra);

    var changed = ChangedRatio(beforeBgra, afterBgra);
    Console.WriteLine($"前后画面差异: {changed:P1}(差异明显即说明点击已被游戏接收)");
    Console.WriteLine($"点击前 : {beforePath}");
    Console.WriteLine($"点击后 : {afterPath}");
}

static double ChangedRatio(byte[] a, byte[] b)
{
    if (a.Length != b.Length || a.Length == 0) return 1.0;
    long changed = 0;
    var pixels = a.Length / 4;
    for (var i = 0; i < a.Length; i += 4)
    {
        var d = Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]);
        if (d > 30) changed++; // 阈值,忽略细微噪声
    }
    return (double)changed / pixels;
}

// P4 真引擎冒烟:对真实窗口跑 截图→匹配→点击 循环。目录内图片暂按 Normal 处理
// (flag 语义待 P6 的 target.json 导入器);按分钟数运行,Ctrl+C 提前停止。⚠ 会实际点击游戏。
static async Task RunEngineAsync(string? titleQuery, string? folder, double minutes)
{
    if (string.IsNullOrWhiteSpace(folder))
    {
        Console.WriteLine("用法: run <窗口标题> <img子目录> [分钟数,默认 1]");
        return;
    }

    var service = new WindowService();
    var target = ResolveTarget(service, titleQuery);
    if (target is null) { Console.WriteLine("未找到目标窗口。"); return; }

    var built = BuildTargetSet(folder);
    if (built is not { Set: { Images.Count: > 0 } set })
    {
        Console.WriteLine($"目录 img/{folder} 下没有可用模板(jpg/png)。");
        return;
    }

    Console.WriteLine($"目标窗口 : {target.Title}  (0x{target.Handle:X})");
    Console.WriteLine($"目标集   : {set.Name} · {set.Images.Count} 图(来源:{built.Value.Source})");
    Console.WriteLine($"运行     : {minutes} 分钟,匹配间隔 2~4s。⚠ 将实际点击游戏,Ctrl+C 提前停止。\n");

    using var matcher = new TemplateMatcher();
    var engine = new AutomationEngine(
        service, new GdiWindowCapturer(), matcher, new SendMessageInput(),
        new[] { target }, set);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    var options = new EngineOptions
    {
        Mode = RunMode.ByMinutes,
        Duration = TimeSpan.FromMinutes(minutes),
        Interval = new IntervalRange(2.0, 4.0),
        SetPriority = null, // 冒烟不改进程优先级,免管理员
    };

    // P6:类型化配置校验 —— 非法参数(阈值/间隔/频次等)启动即拦下。
    var configErrors = EngineOptionsValidation.Validate(options);
    if (configErrors.Count > 0)
    {
        Console.WriteLine("配置校验失败:");
        foreach (var err in configErrors) Console.WriteLine($"  - {err}");
        return;
    }

    var consumer = Task.Run(async () =>
    {
        await foreach (var evt in engine.Events.ReadAllAsync())
            Console.WriteLine(Render(evt));
    });

    await engine.RunAsync(options, cts.Token);
    await consumer;
    Console.WriteLine("=== 运行结束 ===");
}

// P6:目标集加载优先级 —— ① target.json(显式、含 flag/偏移点击)→ ② 旧 img_pos.json 内存导入
// (补齐 flag 语义,不落盘)→ ③ 纯目录扫描(全 Normal 兜底)。
static (TargetSet? Set, string Source)? BuildTargetSet(string folder)
{
    var folderPath = ImgFolderPath(folder);
    if (!Directory.Exists(folderPath)) return null;

    if (TargetSetLoader.Exists(folderPath))
        return (TargetSetLoader.Load(folderPath), "target.json");

    if (File.Exists(Path.Combine(folderPath, "img_pos.json")))
    {
        var result = ImgPosImporter.ImportFolder(folderPath, folder);
        foreach (var w in result.Warnings)
            Console.WriteLine($"  [导入告警] {w}");
        return (TargetSetLoader.FromJson(result.TargetSet, folderPath), "img_pos.json(内存导入)");
    }

    var images = Directory.EnumerateFiles(folderPath)
        .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                 || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        .OrderBy(f => f)
        .Select((f, i) => new TargetImage
        {
            Name = Path.GetFileNameWithoutExtension(f),
            FilePath = f,
            Priority = i,
        })
        .ToList();

    return (new TargetSet(folder, images), "目录扫描(全 Normal)");
}

// P6 冒烟:把 img/<folder>/img_pos.json 的 flag + 优先级 导入为同目录的 target.json。
// 旧 click_pos/real_pos 坐标不导入(几乎全错);需偏移点击的图后续在 target.json 手动补归一化坐标。
static void ImportTargets(string? folder)
{
    if (string.IsNullOrWhiteSpace(folder))
    {
        Console.WriteLine("用法: import <img子目录>");
        return;
    }

    var folderPath = ImgFolderPath(folder);
    if (!Directory.Exists(folderPath))
    {
        Console.WriteLine($"目录不存在: {folderPath}");
        return;
    }

    var result = ImgPosImporter.ImportFolder(folderPath, folder);

    var outPath = Path.Combine(folderPath, TargetSetLoader.FileName);
    File.WriteAllText(outPath, TargetSetSerializer.Serialize(result.TargetSet));

    Console.WriteLine($"目标集   : {result.TargetSet.Name} · {result.TargetSet.Images.Count} 图");
    Console.WriteLine("图片(优先级 / flag / 文件):");
    foreach (var img in result.TargetSet.Images)
        Console.WriteLine($"  {img.Priority,4}  {img.Flag,-10}  {img.File}");
    if (result.Warnings.Count > 0)
    {
        Console.WriteLine("\n告警:");
        foreach (var w in result.Warnings)
            Console.WriteLine($"  - {w}");
    }
    Console.WriteLine($"\n已写入: {outPath}");
}

static async Task RunFakeDemoAsync()
{
    var services = new ServiceCollection();
    services.AddSingleton<IAutomationEngine>(_ => new FakeAutomationEngine(TimeSpan.FromMilliseconds(120)));
    using var provider = services.BuildServiceProvider();
    var engine = provider.GetRequiredService<IAutomationEngine>();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var options = new EngineOptions { Mode = RunMode.ByRounds, Rounds = 3 };
    var consumer = Task.Run(async () =>
    {
        await foreach (var evt in engine.Events.ReadAllAsync())
            Console.WriteLine(Render(evt));
    });

    Console.WriteLine("=== SmartOnmyoji (C# 重写) P0 竖切演示 · 假引擎 ===");
    await engine.RunAsync(options, cts.Token);
    await consumer;
    Console.WriteLine("=== 演示结束 ===");
}

static long CountNonBlack(byte[] bgra)
{
    long count = 0;
    for (var i = 0; i < bgra.Length; i += 4)
        if (bgra[i] != 0 || bgra[i + 1] != 0 || bgra[i + 2] != 0)
            count++;
    return count;
}

/// <summary>
/// 离线验证「模板跨分辨率复用」——不需要游戏窗口,纯文件输入,可反复回归。
/// 目录里按 <c>X_full.jpg</c>(整帧截图)↔ <c>X.jpg</c>(从该帧裁出的模板)配对;
/// 把整帧缩放到别的分辨率后,分别用<b>记了 baseSize</b> 与<b>没记 baseSize</b>(旧行为)的模板匹配,
/// 对照打印分数和坐标误差,直接看出「模板在 A 分辨率截、拿到 B 分辨率跑」是否成立。
/// </summary>
static void ScaleMatch(string folder, string[] scaleArgs)
{
    var folderPath = ImgFolderPath(folder);
    if (!Directory.Exists(folderPath)) { Console.WriteLine($"目录不存在:{folderPath}"); return; }

    var scales = scaleArgs
        .Select(a => double.TryParse(a, out var v) ? v : 0)
        .Where(v => v > 0)
        .ToArray();
    if (scales.Length == 0) scales = [0.75, 0.9, 1.25, 1.5];

    var pairs = Directory.EnumerateFiles(folderPath)
        .Where(f => Path.GetFileNameWithoutExtension(f).EndsWith("_full", StringComparison.OrdinalIgnoreCase))
        .OrderBy(f => f, StringComparer.Ordinal)
        .Select(full =>
        {
            var stem = Path.GetFileNameWithoutExtension(full);
            stem = stem[..^"_full".Length];
            var template = Directory.EnumerateFiles(folderPath)
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(stem, StringComparison.OrdinalIgnoreCase));
            return (Full: full, Template: template);
        })
        .Where(p => p.Template is not null)
        .ToList();

    if (pairs.Count == 0)
    {
        Console.WriteLine($"{folderPath} 里没有 X_full.* ↔ X.* 配对(整帧截图 + 从中裁出的模板)。");
        return;
    }

    using var matcher = new TemplateMatcher();
    var scan = new MatchOptions(MatchMethod.Template, 0.0, 1.0); // 阈值 0 → 总返回分数,便于对照
    const double hitThreshold = 0.80;

    foreach (var (fullPath, templatePath) in pairs)
    {
        var name = Path.GetFileNameWithoutExtension(templatePath!);
        var (bw, bh, baseGray) = LoadGrayScaled(fullPath, 1.0);
        var baseFrame = new CaptureFrame(bw, bh, baseGray);
        var baseSize = new Size(bw, bh);

        var baseline = matcher.Match(
            baseFrame,
            new TargetImage { Name = name, FilePath = templatePath!, BaseSize = baseSize },
            scan);

        Console.WriteLine($"\n=== {Path.GetFileName(templatePath)} @ 基准帧 {Path.GetFileName(fullPath)} {bw}x{bh} ===");
        if (baseline is null) { Console.WriteLine("  基准帧就没匹配上,素材有问题。"); continue; }
        Console.WriteLine($"  基准 ×1.00     分数 {baseline.Score:0.000}  @({baseline.Center.X},{baseline.Center.Y})");

        foreach (var s in scales)
        {
            var (fw, fh, gray) = LoadGrayScaled(fullPath, s);
            var frame = new CaptureFrame(fw, fh, gray);

            var withBase = matcher.Match(
                frame, new TargetImage { Name = name, FilePath = templatePath!, BaseSize = baseSize }, scan);
            var legacy = matcher.Match(
                frame, new TargetImage { Name = name, FilePath = templatePath! }, scan);  // 没记 baseSize = 旧行为

            double expectX = baseline.Center.X * s, expectY = baseline.Center.Y * s;
            Console.WriteLine($"  ×{s:0.00}  帧 {fw}x{fh}   期望中心 ≈({expectX:0},{expectY:0})");
            Console.WriteLine($"    记了 baseSize   {Describe(withBase, expectX, expectY, hitThreshold)}");
            Console.WriteLine($"    旧行为(不缩放)  {Describe(legacy, expectX, expectY, hitThreshold)}");
        }
    }

    Console.WriteLine("\n→ 「记了 baseSize」这行命中、且坐标误差在几个像素内,即说明同一套模板可跨分辨率复用。");

    static string Describe(MatchResult? r, double expectX, double expectY, double hitThreshold)
    {
        if (r is null) return "模板大于截图,跳过";
        var dx = r.Center.X - expectX;
        var dy = r.Center.Y - expectY;
        var mark = r.Score >= hitThreshold ? "✔ 命中" : "✘ 未达阈值";
        return $"分数 {r.Score:0.000}  @({r.Center.X},{r.Center.Y})  误差({dx:+0;-0;0},{dy:+0;-0;0})px  {mark}";
    }
}

/// <summary>
/// 读 PNG/JPG 并(可选)等比缩放为灰度帧,模拟"同一画面在另一分辨率下的截图"。
/// 诊断专用,用 System.Drawing 就地做掉,不把这类图像 IO 塞进 Vision 层。
/// </summary>
static (int Width, int Height, byte[] Gray) LoadGrayScaled(string path, double scale)
{
    using var src = new Bitmap(path);
    var w = Math.Max(1, (int)Math.Round(src.Width * scale, MidpointRounding.AwayFromZero));
    var h = Math.Max(1, (int)Math.Round(src.Height * scale, MidpointRounding.AwayFromZero));

    using var dst = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(dst))
    {
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.DrawImage(src, 0, 0, w, h);
    }

    var data = dst.LockBits(
        new Rectangle(0, 0, w, h),
        System.Drawing.Imaging.ImageLockMode.ReadOnly,
        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    var bgra = new byte[w * h * 4];
    try
    {
        for (var y = 0; y < h; y++)   // 按行拷贝:Stride 未必等于 w*4
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, bgra, y * w * 4, w * 4);
    }
    finally { dst.UnlockBits(data); }

    return (w, h, GrayFromBgra(w, h, bgra));
}

static byte[] GrayFromBgra(int width, int height, byte[] bgra)
{
    var gray = new byte[width * height];
    for (int i = 0, j = 0; i < gray.Length; i++, j += 4)
        gray[i] = (byte)((bgra[j + 2] * 299 + bgra[j + 1] * 587 + bgra[j] * 114) / 1000);
    return gray;
}

static byte[] CropBgra(byte[] src, int srcWidth, int x, int y, int cropWidth, int cropHeight)
{
    var dst = new byte[cropWidth * cropHeight * 4];
    for (var row = 0; row < cropHeight; row++)
    {
        var srcOffset = ((y + row) * srcWidth + x) * 4;
        var dstOffset = row * cropWidth * 4;
        Array.Copy(src, srcOffset, dst, dstOffset, cropWidth * 4);
    }
    return dst;
}

static string ArtifactPath(string fileName) =>
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".artifacts", fileName));

static string ImgFolderPath(string folder) =>
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "img", folder));

static string Sanitize(string name)
{
    foreach (var c in Path.GetInvalidFileNameChars())
        name = name.Replace(c, '_');
    return name.Length > 40 ? name[..40] : name;
}

static string Render(EngineEvent e) => e switch
{
    RoundStarted r => $"[回合] 第 {r.Round} 轮开始",
    TargetMatched m => $"[匹配] {m.Name} @({m.Position.X},{m.Position.Y}) 分数 {m.Score:0.00}",
    Clicked c => $"[点击] {c.Name} -> {string.Join(", ", c.Points.Select(p => $"({p.X},{p.Y})"))}",
    ProgressChanged p => $"[进度] {p.Percent}%",
    Waiting w => $"[等待] {w.Duration.TotalSeconds:0.#}s ({w.Reason})",
    EngineStopped s => $"[停止] {s.Reason}",
    EngineError err => $"[错误] {err.Message}",
    LogMessage l => $"[{l.Level}] {l.Text}",
    _ => e.ToString() ?? string.Empty,
};
