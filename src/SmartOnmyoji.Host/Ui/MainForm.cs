using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SmartOnmyoji.Host.Ui;

/// <summary>
/// WebView2 桌面外壳:一个铺满窗体的 WebView2 控件指向本地 Kestrel(设计文档 §3 主推路线)。
/// 缺少 WebView2 运行时时回退为「用系统浏览器打开」,不至于白屏。
/// </summary>
public sealed class MainForm : Form
{
    private readonly string _url;
    private readonly WebView2 _web;

    public MainForm(string url)
    {
        _url = url;
        Text = "SmartOnmyoji · 御魂助手";
        Width = 1600;
        Height = 1200;
        MinimumSize = new System.Drawing.Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;

        _web = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_web);
        Load += OnLoad;
    }

    private async void OnLoad(object? sender, EventArgs e)
    {
        try
        {
            // 用户数据目录放 LocalAppData,避免写程序目录(可能无写权限 / 只读发布)
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmartOnmyoji", "WebView2");
            Directory.CreateDirectory(userData);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Navigate(_url);
        }
        catch (Exception ex)
        {
            var choice = MessageBox.Show(
                $"未能加载内嵌 WebView2(可能缺少 WebView2 运行时)。\n\n{ex.Message}\n\n是否改用系统浏览器打开?\n{_url}",
                "SmartOnmyoji", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (choice == DialogResult.Yes)
                Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true });
        }
    }
}
