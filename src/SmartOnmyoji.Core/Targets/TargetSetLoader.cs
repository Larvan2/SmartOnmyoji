namespace SmartOnmyoji.Core.Targets;

/// <summary>
/// 把 <c>target.json</c>(<see cref="TargetSetJson"/> DTO)解析为运行期 <see cref="TargetSet"/>:
/// 解析图片路径、套用 defaults、把 <c>clickPos</c> 归一化坐标转成 <see cref="ClickSpec"/>。
/// 是引擎加载目标集的入口(取代旧 <c>ModuleGetTargetInfo</c> 的文件夹遍历 + 隐式优先级)。
/// </summary>
public static class TargetSetLoader
{
    public const string FileName = "target.json";

    /// <summary>从目录读 <c>target.json</c> 并解析为 <see cref="TargetSet"/>。</summary>
    public static TargetSet Load(string folderPath)
    {
        var jsonPath = Path.Combine(folderPath, FileName);
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"目录下没有 {FileName}", jsonPath);

        var dto = TargetSetSerializer.Deserialize(File.ReadAllText(jsonPath));
        return FromJson(dto, folderPath);
    }

    /// <summary>目录下是否存在 <c>target.json</c>。</summary>
    public static bool Exists(string folderPath) =>
        File.Exists(Path.Combine(folderPath, FileName));

    /// <summary>
    /// 把 DTO 解析为 <see cref="TargetSet"/>(纯转换,不碰磁盘)。
    /// 图片路径 = <paramref name="folderPath"/> + 文件名;匹配器缺省回退 defaults。
    /// 匹配阈值不在此层——统一由运行级 <c>EngineOptions.Match.Threshold</c>(运行页设置)决定,不按图覆盖。
    /// </summary>
    public static TargetSet FromJson(TargetSetJson dto, string folderPath)
    {
        var defaults = dto.Defaults ?? new TargetDefaultsJson();
        var name = string.IsNullOrWhiteSpace(dto.Name)
            ? new DirectoryInfo(folderPath).Name
            : dto.Name;

        var images = dto.Images.Select(img => new TargetImage
        {
            Name = Path.GetFileNameWithoutExtension(img.File),
            FilePath = Path.Combine(folderPath, img.File),
            Priority = img.Priority,
            Flag = img.Flag,
            Hint = img.Matcher ?? defaults.Matcher,
            Click = ToClickSpec(img.Click),
            BaseSize = ToBaseSize(img.BaseSize),
        });

        return new TargetSet(name, images);
    }

    /// <summary>非正的尺寸(手写错/占位)一律当"未记录"处理,避免算出 0 或负的缩放比。</summary>
    private static Size? ToBaseSize(Size? size) =>
        size is { Width: > 0, Height: > 0 } ? size : null;

    private static ClickSpec? ToClickSpec(ClickSpecJson? click)
    {
        if (click is null || click.ClickPos.Count == 0)
            return null;

        var points = click.ClickPos
            .Where(p => p.Length >= 2)
            .Select(p => new NormalizedPoint(p[0], p[1]))
            .ToArray();

        return points.Length == 0 ? null : new ClickSpec(points);
    }
}
