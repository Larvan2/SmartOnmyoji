using System.Text.Json;

namespace SmartOnmyoji.Host.Ui;

/// <summary>窗口大小/位置/最大化状态,记录关闭前的最后一次布局。</summary>
public sealed record WindowSettingsDto(int X, int Y, int Width, int Height, bool Maximized);

/// <summary>
/// <see cref="WindowSettingsDto"/> 持久化到 <b>exe 同级 window.json</b>,与 <see cref="OptionsStore"/> 同一约定
/// (路径解析、读损坏回落、写失败不崩)。
/// </summary>
public static class WindowSettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string ConfigPath { get; } = ResolvePath();

    private static string ResolvePath()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        return Path.Combine(dir, "window.json");
    }

    /// <summary>读已保存的窗口布局;缺失或损坏则返回 null(调用方回落默认布局)。</summary>
    public static WindowSettingsDto? Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<WindowSettingsDto>(File.ReadAllText(ConfigPath), JsonOpts);
        }
        catch
        {
            // 损坏/不可读 → 回落默认
        }
        return null;
    }

    /// <summary>写窗口布局到 window.json;写失败(如只读目录)静默忽略。</summary>
    public static void Save(WindowSettingsDto dto)
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(dto, JsonOpts));
        }
        catch
        {
            // 只读目录等 → 忽略,不影响关闭流程
        }
    }
}
