using System.Diagnostics;
using SmartOnmyoji.Core;
using SmartOnmyoji.Core.Targets;
// Host 是 WinForms 项目,全局 using 了 System.Drawing —— 显式指名用领域里的 Size(客户区像素尺寸)。
using Size = SmartOnmyoji.Core.Size;

namespace SmartOnmyoji.Host.Ui;

/// <summary>
/// 目标集目录(img/ 下每个子目录 = 一个玩法目标集)的只读浏览与运行期构建。
/// 集中「三级加载优先」(P6):① target.json → ② 旧 img_pos.json 内存导入 → ③ 目录扫描全 Normal 兜底。
/// Host 冒烟命令(run/import)与 WebUI(/api/targets、/api/engine/start)共用此处,避免逻辑漂移。
/// </summary>
public static class TargetSetCatalog
{
    /// <summary>
    /// 目标集根目录 img/。三级解析,兼顾开发与发行(P8 单文件):
    /// ① 环境变量 <c>SMARTONMYOJI_IMG_ROOT</c> 显式覆盖(测试/部署用);
    /// ② 开发期:从 bin 输出回溯 5 层到仓库根的 img/(存在即用,保持既有开发流不变);
    /// ③ 发行期:真实 exe 同级的 img/(便携、可写)。单文件自解压下 <see cref="AppContext.BaseDirectory"/>
    ///    会指向临时解压目录,故用 <see cref="Environment.ProcessPath"/> 定位真实 exe,不会写进临时目录。
    /// </summary>
    public static string ImgRoot { get; } = ResolveImgRoot();

    private static string ResolveImgRoot()
    {
        var env = Environment.GetEnvironmentVariable("SMARTONMYOJI_IMG_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);

        var repo = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "img"));
        if (Directory.Exists(repo))
            return repo;

        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        return Path.Combine(exeDir, "img");
    }

    public static string FolderPath(string name) => Path.Combine(ImgRoot, name);

    /// <summary>
    /// 枚举所有目标集:含至少一张 jpg/png 的子目录,外加<b>软件托管建出的空集</b>(有 target.json、还没截模板)。
    /// 空集必须列出——目标管理要能选中它往里加图,否则「新建完就消失」;运行页自行按 imageCount 屏蔽不可跑的空集。
    /// </summary>
    public static IReadOnlyList<TargetSetSummaryDto> List()
    {
        if (!Directory.Exists(ImgRoot)) return Array.Empty<TargetSetSummaryDto>();

        var sets = new List<TargetSetSummaryDto>();
        foreach (var dir in Directory.EnumerateDirectories(ImgRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var images = ImageFiles(dir).Count;
            if (images == 0 && !TargetSetLoader.Exists(dir)) continue;
            sets.Add(new TargetSetSummaryDto(Path.GetFileName(dir), images, SourceOf(dir)));
        }
        return sets;
    }

    /// <summary>某目标集的详情:逐图 flag/优先级/是否偏移点击 + 加载告警。</summary>
    public static TargetSetDetailDto? Describe(string name)
    {
        var folder = FolderPath(name);
        if (!Directory.Exists(folder)) return null;

        var built = Build(name);
        if (built is null) return null;

        var images = built.Value.Set.Images
            .Select(i => new TargetImageDto(
                i.Name,
                Path.GetFileName(i.FilePath),
                i.Priority,
                i.Flag.ToString(),
                i.Click is { Points.Count: > 0 },
                i.BaseSize))
            .ToList();

        return new TargetSetDetailDto(name, built.Value.Source, images, built.Value.Warnings);
    }

    /// <summary>构建运行期 <see cref="TargetSet"/>,返回来源与告警。目录无图返回 null。</summary>
    public static (TargetSet Set, string Source, IReadOnlyList<string> Warnings)? Build(string name)
    {
        var folder = FolderPath(name);
        if (!Directory.Exists(folder)) return null;

        if (TargetSetLoader.Exists(folder))
        {
            var set = TargetSetLoader.Load(folder);
            // 空集(刚建出、还没截模板)不可跑:与"目录无图"同样返回 null,调用方给「没有可用模板」提示。
            if (set.Images.Count == 0) return null;
            return (set, "target.json", Array.Empty<string>());
        }

        if (File.Exists(Path.Combine(folder, "img_pos.json")))
        {
            var result = ImgPosImporter.ImportFolder(folder, name);
            return (TargetSetLoader.FromJson(result.TargetSet, folder), "img_pos.json(内存导入)", result.Warnings);
        }

        var scanned = ImageFiles(folder)
            .Select((f, i) => new TargetImage
            {
                Name = Path.GetFileNameWithoutExtension(f),
                FilePath = f,
                Priority = i,
            })
            .ToList();

        if (scanned.Count == 0) return null;
        return (new TargetSet(name, scanned), "目录扫描(全 Normal)", Array.Empty<string>());
    }

    /// <summary>解析目标集里某图片文件的绝对路径(用于 UI 缩略图);越界/不存在返回 null。</summary>
    public static string? ResolveImage(string name, string file)
    {
        var folder = FolderPath(name);
        if (!Directory.Exists(folder)) return null;

        // 只允许目录内的文件名,拒绝路径穿越
        var safe = Path.GetFileName(file);
        if (string.IsNullOrEmpty(safe) || !string.Equals(safe, file, StringComparison.Ordinal)) return null;

        var full = Path.GetFullPath(Path.Combine(folder, safe));
        if (!full.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)) return null;
        return File.Exists(full) ? full : null;
    }

    // ---- 写侧(P7 目标管理:建集 / 存模板 / 编辑 flag/优先级/偏移点击 / 删图) ----

    /// <summary>玩法名是否可安全用作目录名(无路径分隔符/非法字符,非 . / ..,≤40)。</summary>
    public static bool IsSafeSetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name is "." or "..") return false;
        if (name != name.Trim()) return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        if (name.Contains('/') || name.Contains('\\')) return false;
        return name.Length <= 40;
    }

    /// <summary>新建目标集:建目录 + 写空 target.json。名称非法或已存在则失败。</summary>
    public static (bool Ok, string? Error) CreateSet(string name)
    {
        if (!IsSafeSetName(name))
            return (false, "名称非法(不能含路径分隔符/特殊字符,≤40 字符)。");

        var folder = FolderPath(name);
        if (Directory.Exists(folder))
            return (false, $"目标集「{name}」已存在。");

        Directory.CreateDirectory(folder);
        var dto = new TargetSetJson { Name = name, Images = new List<TargetImageJson>() };
        File.WriteAllText(Path.Combine(folder, TargetSetLoader.FileName), TargetSetSerializer.Serialize(dto));
        return (true, null);
    }

    /// <summary>
    /// 目标管理编辑模型:把 <c>target.json</c>(若有)与目录里实际的图片文件<b>对账</b>后返回 target.json 形状。
    /// 磁盘上有、json 里没有的图片(如刚截取的新模板)→ 补为 Normal + 递增优先级,便于用户接着设 flag;
    /// json 里有、磁盘上没有的条目 → 丢弃(无法编辑不存在的图)。返回值与 <c>PUT</c> 入参同形状,天然可往返。
    /// </summary>
    public static TargetSetJson? EditModel(string name)
    {
        var folder = FolderPath(name);
        if (!Directory.Exists(folder)) return null;

        var dto = TargetSetLoader.Exists(folder)
            ? TargetSetSerializer.Deserialize(File.ReadAllText(Path.Combine(folder, TargetSetLoader.FileName)))
            : new TargetSetJson { Name = name };

        var byFile = dto.Images
            .Where(i => !string.IsNullOrEmpty(i.File))
            .GroupBy(i => i.File, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var next = byFile.Count == 0 ? 10 : byFile.Values.Max(i => i.Priority) + 10;

        var images = new List<TargetImageJson>();
        foreach (var path in ImageFiles(folder))
        {
            var file = Path.GetFileName(path);
            if (byFile.TryGetValue(file, out var existing))
                images.Add(existing);
            else
            {
                images.Add(new TargetImageJson { File = file, Priority = next, Flag = TargetFlag.Normal });
                next += 10;
            }
        }

        dto.Name = string.IsNullOrWhiteSpace(dto.Name) ? name : dto.Name;
        dto.Images = images.OrderBy(i => i.Priority).ToList();
        return dto;
    }

    /// <summary>写回 target.json(由 UI 托管);校验每个 image.file 都是目录内的裸文件名。</summary>
    public static (bool Ok, string? Error) SaveJson(string name, TargetSetJson dto)
    {
        var folder = FolderPath(name);
        if (!Directory.Exists(folder))
            return (false, $"目标集「{name}」不存在。");

        foreach (var img in dto.Images)
        {
            var safe = Path.GetFileName(img.File);
            if (string.IsNullOrEmpty(safe) || safe != img.File)
                return (false, $"非法文件名:{img.File}");
        }

        dto.Name = string.IsNullOrWhiteSpace(dto.Name) ? name : dto.Name;
        File.WriteAllText(Path.Combine(folder, TargetSetLoader.FileName), TargetSetSerializer.Serialize(dto));
        return (true, null);
    }

    /// <summary>
    /// 把一张截图裁剪(PNG/JPG 字节)存为该目标集的模板文件。返回落盘后的裸文件名。
    /// <paramref name="baseSize"/> 是截图时的客户区尺寸,会一并记进 target.json,
    /// 供匹配器把模板缩放到其它分辨率复用(见 <c>TemplateScale</c>)。
    /// <para>
    /// 重名<b>不静默覆盖</b>:已有同名模板时返回 <c>Conflict</c>=冲突的文件名、不落盘,
    /// 由 UI 弹「是否覆盖」,用户确认后带 <paramref name="overwrite"/>=true 重来——
    /// 截图取模板极易手滑撞名,悄悄盖掉一张已调好的模板事后无从发现。
    /// </para>
    /// <para>
    /// 「同名」按<b>去扩展名</b>比较:模板的逻辑名就是不含扩展名的文件名(见 <c>TargetImage.Name</c>),
    /// 放任 <c>win.jpg</c> / <c>win.png</c> 并存等于同一个目标被匹配两次。
    /// 确认覆盖时,同名的其它扩展名旧文件一并删除并从 target.json 摘掉。
    /// </para>
    /// </summary>
    public static (bool Ok, string? Error, string? File, string? Conflict) SaveImage(
        string name, string fileName, byte[] bytes, Size? baseSize = null, bool overwrite = false)
    {
        var folder = FolderPath(name);
        if (!Directory.Exists(folder))
            return (false, $"目标集「{name}」不存在。", null, null);

        var safe = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safe) || safe != fileName)
            return (false, "非法文件名。", null, null);

        var ext = Path.GetExtension(safe).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg"))
            return (false, "仅支持 .png / .jpg。", null, null);
        if (bytes.Length == 0)
            return (false, "空图片数据。", null, null);

        var clashes = SameNameFiles(folder, safe);
        if (clashes.Count > 0 && !overwrite)
            return (false, null, null, Path.GetFileName(clashes[0]));

        // 同名但扩展名不同的旧文件:覆盖 = 换掉这个模板,留着它只会两张图匹配同一个目标。
        var removed = clashes
            .Select(Path.GetFileName)
            .Where(f => !string.Equals(f, safe, StringComparison.OrdinalIgnoreCase))
            .ToList()!;
        foreach (var old in removed)
            File.Delete(Path.Combine(folder, old!));

        File.WriteAllBytes(Path.Combine(folder, safe), bytes);
        SyncJson(folder, safe, baseSize, removed!);
        return (true, null, safe, null);
    }

    /// <summary>目录里与 <paramref name="fileName"/> 去扩展名同名的已有图片(含它自己),按文件名排序。</summary>
    private static List<string> SameNameFiles(string folder, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return ImageFiles(folder)
            .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), stem, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 存图后同步 target.json:①把「截图时的客户区尺寸」记进对应条目;②摘掉覆盖时删掉的同名旧文件条目。
    /// baseSize 这个值<b>只有截图这一刻知道</b>,漏记就再也补不回来,所以存图即落盘,不等用户点「保存 target.json」。
    /// 目录还没有 target.json(旧目标集 / 纯目录扫描来源)时不在此新建——免得悄悄改变来源判定、
    /// 把 img_pos.json 的 flag 语义甩掉;那种情况由前端 PUT 整份 json 时带上 baseSize。
    /// </summary>
    private static void SyncJson(string folder, string file, Size? baseSize, IReadOnlyList<string> removed)
    {
        var hasBase = baseSize is { Width: > 0, Height: > 0 };
        if (!hasBase && removed.Count == 0) return;
        if (!TargetSetLoader.Exists(folder)) return;

        var path = Path.Combine(folder, TargetSetLoader.FileName);
        TargetSetJson dto;
        try { dto = TargetSetSerializer.Deserialize(File.ReadAllText(path)); }
        catch { return; }  // 手工改坏的 json 不在"存图"这条路上纠正,留给编辑页报错

        if (removed.Count > 0)
            dto.Images.RemoveAll(i => removed.Contains(i.File, StringComparer.OrdinalIgnoreCase));

        if (hasBase)
        {
            var entry = dto.Images.FirstOrDefault(i => string.Equals(i.File, file, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                entry = new TargetImageJson
                {
                    File = file,
                    Flag = TargetFlag.Normal,
                    Priority = dto.Images.Count == 0 ? 10 : dto.Images.Max(i => i.Priority) + 10,
                };
                dto.Images.Add(entry);
            }

            entry.BaseSize = baseSize;
        }

        File.WriteAllText(path, TargetSetSerializer.Serialize(dto));
    }

    /// <summary>删除目标集里的一张模板文件(拦路径穿越)。</summary>
    public static (bool Ok, string? Error) DeleteImage(string name, string file)
    {
        var path = ResolveImage(name, file);
        if (path is null) return (false, "文件不存在或非法。");
        File.Delete(path);
        return (true, null);
    }

    /// <summary>用系统资源管理器打开该目标集在磁盘上的文件夹(供 UI「打开文件夹」按钮使用)。</summary>
    public static (bool Ok, string? Error) OpenFolder(string name)
    {
        var folder = FolderPath(name);
        if (!Directory.Exists(folder))
            return (false, $"目标集「{name}」不存在。");
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        return (true, null);
    }

    private static string SourceOf(string folder) =>
        TargetSetLoader.Exists(folder) ? "target.json"
        : File.Exists(Path.Combine(folder, "img_pos.json")) ? "img_pos.json"
        : "目录扫描";

    private static List<string> ImageFiles(string folder) =>
        Directory.EnumerateFiles(folder)
            .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
}
