using System.Media;
using System.Windows.Forms;

namespace SmartOnmyoji.Host.Ui;

/// <summary>
/// 引擎自然结束(跑完指定回合/时长、命中终止图、卡死保护)时弹 Windows 系统通知 + 提示音,
/// 免得用户一直守着界面。独立 STA 线程起临时托盘图标显气泡,不依赖 <c>ui</c>/<c>serve</c>
/// 哪种宿主模式在跑消息循环——两边都能用。用户主动点「停止」(<see cref="Engine.StopReason.Cancelled"/>)
/// 不通知,那种情况用户本来就在看着。
/// </summary>
internal static class CompletionNotifier
{
    public static void Notify(string title, string message)
    {
        try { SystemSounds.Asterisk.Play(); } catch { /* 提示音失败不影响主流程 */ }

        try
        {
            var thread = new Thread(() =>
            {
                using var icon = new NotifyIcon
                {
                    Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)
                        ?? System.Drawing.SystemIcons.Information,
                    Visible = true,
                };
                icon.ShowBalloonTip(6000, title, message, ToolTipIcon.Info);
                Thread.Sleep(6200);
            })
            {
                IsBackground = true,
                Name = "CompletionNotifier",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        catch { /* 通知失败不影响主流程 */ }
    }
}
