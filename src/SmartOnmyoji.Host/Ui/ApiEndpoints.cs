using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SmartOnmyoji.Core;
using SmartOnmyoji.Core.Abstractions;
using SmartOnmyoji.Core.Engine;
using SmartOnmyoji.Core.Targets;
using SmartOnmyoji.Windows;
// Host 是 WinForms 项目,全局 using 了 System.Drawing —— 显式指名用领域里的 Size(客户区像素尺寸)。
using Size = SmartOnmyoji.Core.Size;

namespace SmartOnmyoji.Host.Ui;

/// <summary>
/// WebUI 的 HTTP 契约:命令(前端→后端)走 REST;事件(后端→前端)走 <c>GET /api/events</c> 的 SSE 推送。
/// (设计文档 §10 原定 SignalR;原生前端 + WebView2 离线场景改用浏览器原生 EventSource,零客户端依赖、同一意图。)
/// </summary>
public static class ApiEndpoints
{
    public static void MapApi(WebApplication app)
    {
        app.MapGet("/api/status", (EngineManager mgr) => Results.Json(mgr.Status()));

        app.MapGet("/api/windows", (IWindowService ws) =>
        {
            var list = ws.Enumerate()
                .Select(w =>
                {
                    int width = 0, height = 0;
                    try { var c = ws.GetClientArea(w.Handle); width = c.Width; height = c.Height; }
                    catch { /* 取尺寸失败的窗口仍列出,尺寸为 0 */ }
                    return new WindowDto($"0x{w.Handle:X}", w.Title, w.ProcessId, width, height);
                })
                .OrderBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Results.Json(list);
        });

        // 倒计时点选:延迟若干秒后取前台窗口(对齐旧版「倒计时内切到游戏窗口」)。
        app.MapPost("/api/windows/pick", async (PickRequest? req, IWindowService ws) =>
        {
            var seconds = Math.Clamp(req?.Seconds ?? 3, 0, 10);
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            var w = ws.GetForeground();
            if (w is null) return Results.Json<WindowDto?>(null);
            int width = 0, height = 0;
            try { var c = ws.GetClientArea(w.Handle); width = c.Width; height = c.Height; }
            catch { /* 忽略 */ }
            return Results.Json<WindowDto?>(new WindowDto($"0x{w.Handle:X}", w.Title, w.ProcessId, width, height));
        });

        app.MapGet("/api/targets", () => Results.Json(TargetSetCatalog.List()));

        app.MapGet("/api/targets/{name}", (string name) =>
        {
            var detail = TargetSetCatalog.Describe(name);
            return detail is null ? Results.NotFound() : Results.Json(detail);
        });

        // 目标管理编辑模型(target.json ∪ 磁盘图片,对账后 target.json 形状,可直接 PUT 回)。
        app.MapGet("/api/targets/{name}/edit", (string name) =>
        {
            var model = TargetSetCatalog.EditModel(name);
            return model is null
                ? Results.NotFound()
                : Results.Text(TargetSetSerializer.Serialize(model), "application/json");
        });

        app.MapGet("/api/targets/{name}/images/{file}", (string name, string file) =>
        {
            var path = TargetSetCatalog.ResolveImage(name, file);
            if (path is null) return Results.NotFound();
            var mime = file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            return Results.File(File.ReadAllBytes(path), mime);
        });

        // ---- 目标管理写侧(P7 §9.3):建集 / 截图取模板 / 存模板 / 编辑 target.json / 删图 ----

        app.MapPost("/api/targets/create", (CreateSetRequest req) =>
        {
            var (ok, err) = TargetSetCatalog.CreateSet(req.Name ?? "");
            return ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = err });
        });

        // 后台截取窗口客户区,返回 PNG(供前端 canvas 框选取模板)。坐标系 = 客户区物理像素。
        app.MapPost("/api/capture", (CaptureRequest req, IWindowService ws) =>
        {
            if (!TryParseHandle(req.WindowHandle, out var h)) return Results.BadRequest(new { error = "句柄非法。" });
            if (!ws.IsAlive(h)) return Results.BadRequest(new { error = "窗口不存在或已关闭。" });
            var (w, height, bgra) = new GdiWindowCapturer().CaptureBgra(h);
            return Results.File(DebugImage.EncodePng(w, height, bgra), "image/png");
        });

        // 前端 canvas 裁好的模板(dataURL)落盘为目标集里的图片文件。
        // 同时记下截图时的客户区尺寸(baseWidth/baseHeight)——模板跨分辨率复用的依据,只有此刻知道。
        app.MapPost("/api/targets/{name}/images", (string name, SaveImageRequest req) =>
        {
            var bytes = DecodeDataUrl(req.DataUrl);
            if (bytes is null) return Results.BadRequest(new { ok = false, error = "图片数据非法。" });

            Size? baseSize = req.BaseWidth > 0 && req.BaseHeight > 0
                ? new Size(req.BaseWidth, req.BaseHeight)
                : null;

            var (ok, err, file) = TargetSetCatalog.SaveImage(name, req.File ?? "", bytes, baseSize);
            return ok
                ? Results.Ok(new { ok = true, file, baseSize })
                : Results.BadRequest(new { ok = false, error = err });
        });

        app.MapDelete("/api/targets/{name}/images/{file}", (string name, string file) =>
        {
            var (ok, err) = TargetSetCatalog.DeleteImage(name, file);
            return ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = err });
        });

        // 用系统资源管理器打开该目标集的磁盘文件夹(便于手动整理模板图)。
        app.MapPost("/api/targets/{name}/open-folder", (string name) =>
        {
            var (ok, err) = TargetSetCatalog.OpenFolder(name);
            return ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = err });
        });

        // 写回整份 target.json(裸 body,用 Core 的 schema 反序列化以支持字符串枚举 flag)。
        app.MapPut("/api/targets/{name}", async (string name, HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var json = await reader.ReadToEndAsync();
            TargetSetJson dto;
            try { dto = TargetSetSerializer.Deserialize(json); }
            catch (Exception ex) { return Results.BadRequest(new { ok = false, error = "target.json 解析失败:" + ex.Message }); }
            var (ok, err) = TargetSetCatalog.SaveJson(name, dto);
            return ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = err });
        });

        // 硬编码默认值(供前端"恢复默认")。
        app.MapGet("/api/options/default", () => Results.Json(OptionsMapping.ToDto(new EngineOptions())));

        // 已保存的运行参数(缺失/损坏回落默认)——前端启动时读它,记住上次设置。存 exe 同级 config.json。
        app.MapGet("/api/options", () => Results.Json(OptionsStore.Load()));

        // 保存运行参数到 config.json(前端改动即防抖写回)。写失败(只读目录等)不视为错误、只回 ok=false。
        app.MapPut("/api/options", (OptionsDto dto) =>
        {
            var ok = OptionsStore.Save(dto);
            return Results.Ok(new { ok, path = OptionsStore.ConfigPath });
        });

        app.MapPost("/api/engine/start", (StartRequest req, EngineManager mgr) =>
        {
            var r = mgr.Start(req);
            return r.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = r.Error });
        });

        app.MapPost("/api/engine/stop", (EngineManager mgr) =>
            Results.Ok(new { stopped = mgr.Stop() }));

        app.MapPost("/api/engine/pause", (EngineManager mgr) =>
            Results.Ok(new { ok = mgr.Pause() }));

        app.MapPost("/api/engine/resume", (EngineManager mgr) =>
            Results.Ok(new { ok = mgr.Resume() }));

        // SSE 事件流:一直挂着,把 EngineManager 扇出的 JSON 行逐条推给浏览器 EventSource。
        app.MapGet("/api/events", async (HttpContext ctx, EngineManager mgr) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            var (reader, sub) = mgr.Subscribe();
            using var _ = sub;
            var ct = ctx.RequestAborted;
            try
            {
                await ctx.Response.WriteAsync(": connected\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
                await foreach (var msg in reader.ReadAllAsync(ct))
                {
                    await ctx.Response.WriteAsync($"data: {msg}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 客户端断开,正常退出
            }
        });
    }

    private static bool TryParseHandle(string? s, out nint handle)
    {
        handle = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var hex = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        try
        {
            handle = (nint)Convert.ToInt64(hex ? s[2..] : s, hex ? 16 : 10);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>从 dataURL(<c>data:image/png;base64,…</c>)或裸 base64 解出字节;非法返回 null。</summary>
    private static byte[]? DecodeDataUrl(string? dataUrl)
    {
        if (string.IsNullOrEmpty(dataUrl)) return null;
        var idx = dataUrl.IndexOf("base64,", StringComparison.Ordinal);
        var b64 = idx >= 0 ? dataUrl[(idx + 7)..] : dataUrl;
        try { return Convert.FromBase64String(b64); }
        catch { return null; }
    }
}

public sealed record PickRequest
{
    public int Seconds { get; init; } = 3;
}

public sealed record CreateSetRequest
{
    public string? Name { get; init; }
}

public sealed record CaptureRequest
{
    public string? WindowHandle { get; init; }
}

public sealed record SaveImageRequest
{
    public string? File { get; init; }
    public string? DataUrl { get; init; }

    /// <summary>截图时的客户区尺寸(物理像素,= 前端那张整图的原始宽高);0 表示前端没给,按未记录处理。</summary>
    public int BaseWidth { get; init; }
    public int BaseHeight { get; init; }
}
