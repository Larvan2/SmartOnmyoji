using System.Text.Json;
using SmartOnmyoji.Core.Engine;

namespace SmartOnmyoji.Host.Ui;

/// <summary>
/// 运行参数(<see cref="OptionsDto"/>)持久化到 <b>exe 同级 config.json</b>。
/// <para>读:文件缺失/损坏 → 回落硬编码默认值(<c>new EngineOptions()</c>),不抛。</para>
/// <para>写:目录只读等写失败只返回 false、不崩(单文件发行版提权运行,exe 目录通常可写)。</para>
/// <para>路径用 <see cref="Environment.ProcessPath"/> 取真实 exe 目录——单文件自解压下
/// <see cref="AppContext.BaseDirectory"/> 指向临时目录,不能用它。开发期(dotnet run)落在 bin 输出目录旁,同样随进程持久。</para>
/// </summary>
public static class OptionsStore
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string ConfigPath { get; } = ResolvePath();

    private static string ResolvePath()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        return Path.Combine(dir, "config.json");
    }

    /// <summary>读已保存的选项;缺失或损坏则回落默认值。</summary>
    public static OptionsDto Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var dto = JsonSerializer.Deserialize<OptionsDto>(File.ReadAllText(ConfigPath), JsonOpts);
                if (dto is not null) return dto;
            }
        }
        catch
        {
            // 损坏/不可读 → 回落默认
        }
        return OptionsMapping.ToDto(new EngineOptions());
    }

    /// <summary>写选项到 config.json;写失败(如只读目录)返回 false,不抛。</summary>
    public static bool Save(OptionsDto dto)
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(dto, JsonOpts));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
