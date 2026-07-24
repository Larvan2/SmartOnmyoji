using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartOnmyoji.Core.Abstractions;
using SmartOnmyoji.Windows;

namespace SmartOnmyoji.Host.Ui;

/// <summary>
/// P7 组合根:进程内起 Kestrel(仅 127.0.0.1)提供静态 WebUI + REST + SSE。
/// <para><see cref="RunAsync"/> 的 <c>shell=true</c>:再用 WebView2 桌面窗口指向它(主推路线),窗口关闭即停机。</para>
/// <para><c>shell=false</c>(headless / 浏览器模式):只起服务、打印地址、挂到 Ctrl+C——无需 WebView2 运行时,便于验证。</para>
/// </summary>
public static class WebUiApp
{
    public static async Task RunAsync(bool shell, int port = 0)
    {
        var chosen = port != 0 ? port : FreeLoopbackPort();
        var url = $"http://127.0.0.1:{chosen}/";

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // ContentRoot 固定到 bin 输出目录,wwwroot 已随构建拷来,避免受启动工作目录影响
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = "wwwroot",
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, chosen));

        builder.Services.AddSingleton<IWindowService, WindowService>();
        builder.Services.AddSingleton<EngineManager>();

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        ApiEndpoints.MapApi(app);

        await app.StartAsync();
        Console.WriteLine($"WebUI listening: {url}");

        if (shell)
            RunWebViewShell(url);
        else
            await WaitForCtrlCAsync(url);

        await app.StopAsync();
    }

    /// <summary>WebView2 + WinForms 外壳必须在 STA 线程上跑消息循环;阻塞至窗口关闭。</summary>
    private static void RunWebViewShell(string url)
    {
        var uiThread = new Thread(() =>
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var form = new MainForm(url);
            Application.Run(form);
        })
        {
            Name = "WebUI-STA",
            IsBackground = false,
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        uiThread.Join();
    }

    private static async Task WaitForCtrlCAsync(string url)
    {
        Console.WriteLine($"Headless 模式:用浏览器打开 {url} —— Ctrl+C 退出。");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        try { await Task.Delay(Timeout.Infinite, cts.Token); }
        catch (OperationCanceledException) { /* 正常退出 */ }
    }

    /// <summary>抢一个空闲的 loopback 端口(URL 需在启动前已知,供 WebView2 导航)。</summary>
    private static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var chosen = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return chosen;
    }
}
