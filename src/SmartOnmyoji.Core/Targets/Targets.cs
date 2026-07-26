namespace SmartOnmyoji.Core.Targets;

/// <summary>目标图片的行为标记。对应旧 img_pos.json 的 flag 字段。</summary>
public enum TargetFlag
{
    /// <summary>普通图,匹配即点。(旧 "")</summary>
    Normal,

    /// <summary>回合起点:计回合数并复位 <see cref="Once"/>。(旧 "start")</summary>
    RoundStart,

    /// <summary>本回合只点一次。(旧 "mark")</summary>
    Once,

    /// <summary>匹配但不点,并抑制本回合的 <see cref="Once"/>。(旧 "skip")</summary>
    Skip,

    /// <summary>匹配即终止脚本。(旧 "stop")</summary>
    Stop,
}

public enum MatchHint { Auto, Template, Feature }

/// <summary>
/// 关键图的偏移点击:命中此图时不点它本身,而在这些点里随机取一个点。
/// 点用<b>客户区归一化坐标(0~1)</b>表达,天生分辨率无关——
/// 取代旧 img_pos.json 的绝对像素 click_pos + real_pos + scal_rate 缩放换算。
/// </summary>
public sealed record ClickSpec(IReadOnlyList<NormalizedPoint> Points);

public sealed record TargetImage
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }

    /// <summary>匹配优先级,升序检查(数值小者先匹配)。取代旧代码"文件名排序隐式决定优先级"。</summary>
    public int Priority { get; init; }

    public TargetFlag Flag { get; init; } = TargetFlag.Normal;

    /// <summary>关键图的偏移点击;为 null 时点击匹配中心点。</summary>
    public ClickSpec? Click { get; init; }

    /// <summary>
    /// 截取此模板时的客户区尺寸(物理像素)。运行时客户区若是别的尺寸,匹配器按比例缩放模板再匹配——
    /// <b>同一套模板可跨分辨率复用,不必换个分辨率就重截一遍图</b>。
    /// null = 未记录(旧目标集/导入数据),此时不缩放,行为与记录前完全一致。
    /// </summary>
    public Size? BaseSize { get; init; }

    /// <summary>本图的匹配器覆盖;Auto 时用全局设置。</summary>
    public MatchHint Hint { get; init; } = MatchHint.Auto;
}

/// <summary>一个目标集(如"御魂"),图片按 <see cref="TargetImage.Priority"/> 升序排列。</summary>
public sealed class TargetSet
{
    public string Name { get; }
    public IReadOnlyList<TargetImage> Images { get; }

    public TargetSet(string name, IEnumerable<TargetImage> images)
    {
        Name = name;
        Images = images.OrderBy(i => i.Priority).ToArray();
    }
}
