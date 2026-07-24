using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartOnmyoji.Core.Targets;

/// <summary>
/// 旧 <c>img_pos.json</c> → 新 <c>target.json</c> 的一次性导入器(设计文档 §9.2)。
///
/// <para>核心转换 <see cref="Import"/> 是<b>纯函数</b>(不碰磁盘),可离线单测:</para>
/// <list type="bullet">
///   <item>flag 空串→Normal,start→RoundStart,mark→Once,skip→Skip,stop→Stop;未知 flag→Normal + 告警。</item>
///   <item>优先级按<b>文件名排序</b>赋值(step 10),忠实还原旧版"文件夹遍历顺序 + 早退"的匹配次序
///     ——旧目标集正是靠 <c>00_marked</c>/<c>0_end</c> 这类前导数字文件名把 skip/stop 排到最前。</item>
///   <item>目录里有图但 json 没配 → 按 Normal 收进来;json 配了但目录没图 → 告警并跳过。</item>
/// </list>
///
/// <para><b>不导入旧 <c>click_pos</c>/<c>real_pos</c> 坐标</b>:经确认旧坐标几乎全是错的(且标注在整窗坐标系、
/// 与新客户区归一化坐标系不兼容)。导入后 <c>click</c> 一律留空 → 引擎点匹配中心(反而正确);
/// 确需偏移点击的图,后续在 <c>target.json</c> 里用客户区归一化坐标(0~1)手动补 <c>click.clickPos</c>。</para>
/// </summary>
public static class ImgPosImporter
{
    /// <summary>导入参数。</summary>
    public sealed record Options(int PriorityStep = 10);

    /// <summary>导入结果:产出的 <see cref="TargetSetJson"/> 与人类可读告警列表。</summary>
    public sealed record ImportResult(TargetSetJson TargetSet, IReadOnlyList<string> Warnings);

    /// <summary>旧 <c>img_pos.json</c> 单条目(snake_case 字段)。</summary>
    public sealed class ImgPosEntry
    {
        public string Name { get; set; } = "";

        [JsonPropertyName("real_pos")]
        public List<double>? RealPos { get; set; }

        [JsonPropertyName("click_pos")]
        public List<List<double>>? ClickPos { get; set; }

        public string? Flag { get; set; }
    }

    private static readonly JsonSerializerOptions ImgPosJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>解析旧 <c>img_pos.json</c> 文本为条目列表(空文本 → 空列表)。</summary>
    public static IReadOnlyList<ImgPosEntry> ParseImgPos(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<ImgPosEntry>();
        return JsonSerializer.Deserialize<List<ImgPosEntry>>(json, ImgPosJsonOptions)
            ?? new List<ImgPosEntry>();
    }

    /// <summary>
    /// 从目录读 <c>img_pos.json</c> + 枚举 jpg/png,产出 <see cref="ImportResult"/>(便捷入口,含磁盘 IO)。
    /// 纯转换逻辑在 <see cref="Import"/>。
    /// </summary>
    public static ImportResult ImportFolder(string folderPath, string? setName = null, Options? options = null)
    {
        var imgPosPath = Path.Combine(folderPath, "img_pos.json");
        var entries = File.Exists(imgPosPath)
            ? ParseImgPos(File.ReadAllText(imgPosPath))
            : Array.Empty<ImgPosEntry>();

        var imageFiles = Directory.EnumerateFiles(folderPath)
            .Where(IsImageFile)
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .Select(f => f!)
            .ToList();

        var name = setName ?? new DirectoryInfo(folderPath).Name;
        return Import(name, entries, imageFiles, options);
    }

    /// <summary>
    /// 纯转换:旧条目 + 目录里的图片文件名 → <see cref="TargetSetJson"/>。不碰磁盘,可离线单测。
    /// </summary>
    public static ImportResult Import(
        string setName,
        IReadOnlyList<ImgPosEntry> oldEntries,
        IReadOnlyList<string> imageFileNames,
        Options? options = null)
    {
        options ??= new Options();
        var warnings = new List<string>();

        // 旧条目按 name 建索引(去重,后者告警)。
        var byName = new Dictionary<string, ImgPosEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in oldEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name)) continue;
            if (!byName.TryAdd(entry.Name, entry))
                warnings.Add($"img_pos.json 有重名条目「{entry.Name}」,后者忽略。");
        }

        // 图片按文件名排序 → 优先级(还原旧版遍历顺序:00_/0_ 前缀排最前 → 先匹配)。
        var sorted = imageFileNames
            .Where(IsImageFile)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var images = new List<TargetImageJson>(sorted.Count);
        var priority = options.PriorityStep;

        foreach (var file in sorted)
        {
            var baseName = Path.GetFileNameWithoutExtension(file);
            var img = new TargetImageJson { File = file, Priority = priority };
            priority += options.PriorityStep;

            if (byName.TryGetValue(baseName, out var entry))
            {
                used.Add(baseName);
                img.Flag = MapFlag(entry.Flag, out var unknown);
                if (unknown)
                    warnings.Add($"图片「{baseName}」的 flag「{entry.Flag}」无法识别,按 Normal 处理。");
                // 旧 click_pos/real_pos 坐标不导入(几乎全错、坐标系不兼容);click 留空 → 点匹配中心。
            }

            images.Add(img);
        }

        // json 配了但目录里没有对应图片 → 告警(旧配置遗留、改名或删图)。
        foreach (var entry in oldEntries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Name) && !used.Contains(entry.Name))
                warnings.Add($"img_pos.json 条目「{entry.Name}」在目录里找不到对应图片,已跳过。");
        }

        var set = new TargetSetJson
        {
            Name = setName,
            Defaults = new TargetDefaultsJson { Matcher = MatchHint.Template },
            Images = images,
        };
        return new ImportResult(set, warnings);
    }

    /// <summary>旧 flag 字符串 → 新枚举(见设计文档附录 A)。未知值 → Normal 且 <paramref name="unknown"/>=true。</summary>
    public static TargetFlag MapFlag(string? flag, out bool unknown)
    {
        unknown = false;
        switch ((flag ?? "").Trim().ToLowerInvariant())
        {
            case "": return TargetFlag.Normal;
            case "start": return TargetFlag.RoundStart;
            case "mark": return TargetFlag.Once;
            case "skip": return TargetFlag.Skip;
            case "stop": return TargetFlag.Stop;
            default: unknown = true; return TargetFlag.Normal;
        }
    }

    private static bool IsImageFile(string path) =>
        path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
}
