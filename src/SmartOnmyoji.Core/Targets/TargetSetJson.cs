using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartOnmyoji.Core.Targets;

/// <summary>
/// <c>target.json</c> 的序列化形状(对齐设计文档 §9.2)。这是新的<b>显式、可迁移</b>目标定义格式,
/// 取代旧 <c>img_pos.json</c> 靠文件名字符串关联行为、绝对像素 + real_pos + scal_rate 缩放换算那一套。
/// 这些 DTO 只做磁盘契约;运行期用的是 <see cref="TargetSet"/>/<see cref="TargetImage"/>
/// (由 <see cref="TargetSetLoader"/> 把这里的 DTO 解析、排序、解析路径后产出)。
/// </summary>
public sealed class TargetSetJson
{
    /// <summary>目标集名称(如「御魂」)。为空时 <see cref="TargetSetLoader"/> 回退用文件夹名。</summary>
    public string Name { get; set; } = "";

    /// <summary>整个目标集的默认匹配器;每图可覆盖(匹配阈值不在此,统一走运行级设置)。</summary>
    public TargetDefaultsJson? Defaults { get; set; }

    public List<TargetImageJson> Images { get; set; } = new();
}

public sealed class TargetDefaultsJson
{
    /// <summary>默认匹配器。Template / Feature / Auto。</summary>
    public MatchHint Matcher { get; set; } = MatchHint.Template;
}

public sealed class TargetImageJson
{
    /// <summary>模板文件名(含扩展名,相对目标集目录),如 <c>win_jiangli.jpg</c>。</summary>
    public string File { get; set; } = "";

    /// <summary>匹配优先级,升序检查(小者先匹配)。取代旧代码文件名排序隐式决定优先级。</summary>
    public int Priority { get; set; }

    public TargetFlag Flag { get; set; } = TargetFlag.Normal;

    /// <summary>关键图的偏移点击(客户区归一化坐标);为 null 时点匹配中心。</summary>
    public ClickSpecJson? Click { get; set; }

    /// <summary>本图匹配器覆盖;为 null 时用 <see cref="TargetDefaultsJson.Matcher"/>。</summary>
    public MatchHint? Matcher { get; set; }

    /// <summary>
    /// 截取此模板时的客户区尺寸(物理像素),形如 <c>"baseSize": { "width": 1200, "height": 600 }</c>。
    /// 由目标管理截图取模板时自动记录,用户不必手填;运行时据此把模板缩放到当前分辨率再匹配。
    /// 缺省(旧目标集)= 不缩放。
    /// </summary>
    public Size? BaseSize { get; set; }
}

/// <summary>偏移点击定义:<c>clickPos</c> 是客户区归一化坐标 [[x,y],…],x/y ∈ [0,1]。</summary>
public sealed class ClickSpecJson
{
    public List<double[]> ClickPos { get; set; } = new();
}

/// <summary><c>target.json</c> 的读写(System.Text.Json,camelCase + 字符串枚举 + 缩进)。</summary>
public static class TargetSetSerializer
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(TargetSetJson set) =>
        JsonSerializer.Serialize(set, JsonOptions);

    public static TargetSetJson Deserialize(string json) =>
        JsonSerializer.Deserialize<TargetSetJson>(json, JsonOptions)
        ?? throw new InvalidDataException("target.json 反序列化为空。");
}
