using SmartOnmyoji.Core.Targets;
using Xunit;
using static SmartOnmyoji.Core.Targets.ImgPosImporter;

namespace SmartOnmyoji.Core.Tests;

public class ImgPosImporterTests
{
    private static ImgPosEntry Entry(string name, string? flag = null, params double[][] clickPos) =>
        new() { Name = name, Flag = flag, ClickPos = clickPos.Length == 0 ? null : clickPos.Select(p => p.ToList()).ToList() };

    private static TargetImageJson? Find(ImportResult r, string baseName) =>
        r.TargetSet.Images.FirstOrDefault(i => Path.GetFileNameWithoutExtension(i.File) == baseName);

    [Theory]
    [InlineData(null, TargetFlag.Normal)]
    [InlineData("", TargetFlag.Normal)]
    [InlineData("start", TargetFlag.RoundStart)]
    [InlineData("mark", TargetFlag.Once)]
    [InlineData("skip", TargetFlag.Skip)]
    [InlineData("stop", TargetFlag.Stop)]
    [InlineData("STOP", TargetFlag.Stop)]     // 大小写不敏感
    [InlineData("  skip ", TargetFlag.Skip)]  // 去空白
    public void MapFlag_maps_old_flags(string? flag, TargetFlag expected)
    {
        Assert.Equal(expected, MapFlag(flag, out var unknown));
        Assert.False(unknown);
    }

    [Fact]
    public void MapFlag_unknown_falls_back_to_normal_and_flags_unknown()
    {
        Assert.Equal(TargetFlag.Normal, MapFlag("weird", out var unknown));
        Assert.True(unknown);
    }

    [Fact]
    public void Import_maps_flag_from_matching_entry()
    {
        var entries = new[] { Entry("start", "start"), Entry("0_end", "stop"), Entry("00_marked", "skip") };
        var files = new[] { "start.jpg", "0_end.jpg", "00_marked.png", "reward.jpg" };

        var r = Import("御魂", entries, files);

        Assert.Equal(TargetFlag.RoundStart, Find(r, "start")!.Flag);
        Assert.Equal(TargetFlag.Stop, Find(r, "0_end")!.Flag);
        Assert.Equal(TargetFlag.Skip, Find(r, "00_marked")!.Flag);
        // 目录里有但 json 没配 → Normal
        Assert.Equal(TargetFlag.Normal, Find(r, "reward")!.Flag);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Import_assigns_priority_by_filename_sort_step10()
    {
        // 乱序传入,期望按文件名排序赋优先级(00_ / 0_ 前缀排最前 → 先匹配)
        var files = new[] { "reward.jpg", "0_end.jpg", "00_marked.png", "start.jpg" };
        var r = Import("s", Array.Empty<ImgPosEntry>(), files);

        var byPriority = r.TargetSet.Images.OrderBy(i => i.Priority).Select(i => i.File).ToArray();
        Assert.Equal(new[] { "00_marked.png", "0_end.jpg", "reward.jpg", "start.jpg" }, byPriority);
        Assert.Equal(new[] { 10, 20, 30, 40 }, r.TargetSet.Images.OrderBy(i => i.Priority).Select(i => i.Priority).ToArray());
    }

    [Fact]
    public void Import_never_imports_old_clickpos_coordinates()
    {
        // 旧坐标几乎全错、坐标系不兼容 → 一律不导入,click 留空(引擎点匹配中心)。
        var entries = new[] { Entry("reward", "", new double[] { 900, 600 }, new double[] { 600, 300 }) };
        var r = Import("s", entries, new[] { "reward.jpg" });
        Assert.Null(Find(r, "reward")!.Click);
    }

    [Fact]
    public void Import_warns_on_entry_without_image()
    {
        var entries = new[] { Entry("ghost", "skip") };  // 目录里没有 ghost.*
        var r = Import("s", entries, new[] { "reward.jpg" });

        Assert.DoesNotContain(r.TargetSet.Images, i => Path.GetFileNameWithoutExtension(i.File) == "ghost");
        Assert.Contains(r.Warnings, w => w.Contains("ghost"));
    }

    [Fact]
    public void Import_warns_on_unknown_flag_but_still_imports_as_normal()
    {
        var entries = new[] { Entry("reward", "bogus") };
        var r = Import("s", entries, new[] { "reward.jpg" });

        Assert.Equal(TargetFlag.Normal, Find(r, "reward")!.Flag);
        Assert.Contains(r.Warnings, w => w.Contains("bogus"));
    }

    [Fact]
    public void Import_sets_default_matcher()
    {
        var r = Import("s", Array.Empty<ImgPosEntry>(), new[] { "reward.jpg" });
        Assert.NotNull(r.TargetSet.Defaults);
        Assert.Equal(MatchHint.Template, r.TargetSet.Defaults!.Matcher);
    }

    [Fact]
    public void ParseImgPos_reads_real_world_shape()
    {
        // 对齐真实 img/yuling/img_pos.json 的字段形状(snake_case、空数组、含注释鲁棒)
        const string json = """
        [
          { "name":"reward", "real_pos":[840,330], "click_pos":[[900,620],[850,600]], "flag": "" },
          { "name":"00_marked", "real_pos": [], "click_pos":[], "flag": "skip" },
          { "name":"start", "real_pos": [], "click_pos":[], "flag": "start" },
          { "name":"0_end", "real_pos": [], "click_pos":[], "flag": "stop" }
        ]
        """;
        var entries = ParseImgPos(json);

        Assert.Equal(4, entries.Count);
        Assert.Equal("reward", entries[0].Name);
        Assert.Equal(2, entries[0].ClickPos!.Count);
        Assert.Equal("skip", entries[1].Flag);
        Assert.Empty(entries[1].ClickPos!);
    }

    [Fact]
    public void ParseImgPos_empty_text_yields_empty_list()
    {
        Assert.Empty(ParseImgPos(""));
        Assert.Empty(ParseImgPos("  "));
    }

    [Fact]
    public void End_to_end_import_then_load_produces_usable_targetset()
    {
        // 导入 → 序列化 → 反序列化 → 加载为运行期 TargetSet,验证 flag/优先级全程贯通。
        var entries = new[]
        {
            Entry("start", "start"),
            Entry("0_end", "stop"),
            Entry("reward", "", new double[] { 900, 600 }),  // 旧坐标不导入
        };
        var files = new[] { "start.jpg", "0_end.jpg", "reward.jpg" };

        var imported = Import("御魂", entries, files);
        var json = TargetSetSerializer.Serialize(imported.TargetSet);
        var roundTripped = TargetSetSerializer.Deserialize(json);
        var set = TargetSetLoader.FromJson(roundTripped, @"C:\img\yuling");

        Assert.Equal("御魂", set.Name);
        var start = set.Images.Single(i => i.Name == "start");
        var end = set.Images.Single(i => i.Name == "0_end");
        var reward = set.Images.Single(i => i.Name == "reward");

        Assert.Equal(TargetFlag.RoundStart, start.Flag);
        Assert.Equal(TargetFlag.Stop, end.Flag);
        Assert.Equal(TargetFlag.Normal, reward.Flag);
        Assert.Equal(Path.Combine(@"C:\img\yuling", "reward.jpg"), reward.FilePath);
        Assert.Null(reward.Click);  // 旧坐标不导入 → 点匹配中心
        Assert.Equal(MatchHint.Template, reward.Hint);
    }
}
