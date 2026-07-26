using SmartOnmyoji.Core.Targets;
using Xunit;

namespace SmartOnmyoji.Core.Tests;

public class TargetSetSerializerTests
{
    [Fact]
    public void Deserialize_reads_design_doc_shape()
    {
        // 对齐设计文档 §9.2 示例:camelCase、字符串枚举 flag、defaults、归一化 clickPos。
        // 额外验证:旧 target.json 残留的 threshold 字段(defaults/每图)被静默忽略、不报错
        // ——阈值已不是 target.json 的字段,统一走运行级 EngineOptions.Match.Threshold。
        const string json = """
        {
          "name": "御魂",
          "defaults": { "matcher": "Template", "threshold": 0.80 },
          "images": [
            { "file": "tiaozhan.jpg",  "priority": 10, "flag": "RoundStart" },
            { "file": "win_jiangli.jpg", "priority": 20, "flag": "Normal",
              "click": { "clickPos": [[0.75, 0.92], [0.71, 0.89]] } },
            { "file": "00_marked.png", "priority": 30, "flag": "Skip" },
            { "file": "0_end.jpg", "priority": 5, "flag": "Stop", "threshold": 0.9 }
          ]
        }
        """;

        var dto = TargetSetSerializer.Deserialize(json);

        Assert.Equal("御魂", dto.Name);
        Assert.Equal(MatchHint.Template, dto.Defaults!.Matcher);
        Assert.Equal(4, dto.Images.Count);

        var jiangli = dto.Images.Single(i => i.File == "win_jiangli.jpg");
        Assert.Equal(TargetFlag.Normal, jiangli.Flag);
        Assert.Equal(2, jiangli.Click!.ClickPos.Count);
        Assert.Equal(new[] { 0.75, 0.92 }, jiangli.Click.ClickPos[0]);

        var end = dto.Images.Single(i => i.File == "0_end.jpg");
        Assert.Equal(TargetFlag.Stop, end.Flag);
    }

    [Fact]
    public void Serialize_uses_camelCase_and_string_enums()
    {
        var set = new TargetSetJson
        {
            Name = "s",
            Defaults = new TargetDefaultsJson { Matcher = MatchHint.Template },
            Images = { new TargetImageJson { File = "a.jpg", Priority = 10, Flag = TargetFlag.RoundStart } },
        };

        var json = TargetSetSerializer.Serialize(set);

        Assert.Contains("\"images\"", json);          // camelCase
        Assert.Contains("\"RoundStart\"", json);       // 字符串枚举而非数字
        Assert.DoesNotContain("\"click\"", json);      // null 不写(WhenWritingNull)
    }

    [Fact]
    public void Roundtrip_preserves_content()
    {
        var original = new TargetSetJson
        {
            Name = "御魂",
            Defaults = new TargetDefaultsJson { Matcher = MatchHint.Feature },
            Images =
            {
                new TargetImageJson { File = "start.jpg", Priority = 10, Flag = TargetFlag.RoundStart },
                new TargetImageJson
                {
                    File = "reward.jpg", Priority = 20, Flag = TargetFlag.Normal,
                    Matcher = MatchHint.Template,
                    Click = new ClickSpecJson { ClickPos = { new[] { 0.5, 0.9 } } },
                },
            },
        };

        var back = TargetSetSerializer.Deserialize(TargetSetSerializer.Serialize(original));

        Assert.Equal(original.Name, back.Name);
        Assert.Equal(original.Defaults!.Matcher, back.Defaults!.Matcher);
        Assert.Equal(2, back.Images.Count);
        var reward = back.Images.Single(i => i.File == "reward.jpg");
        Assert.Equal(MatchHint.Template, reward.Matcher);
        Assert.Equal(new[] { 0.5, 0.9 }, reward.Click!.ClickPos[0]);
    }

    [Fact]
    public void BaseSize_roundtrips_and_reaches_domain_model()
    {
        // baseSize = 截取模板时的客户区尺寸,是模板跨分辨率复用的唯一依据,必须能完整往返到运行期模型。
        const string json = """
        {
          "name": "御魂",
          "images": [
            { "file": "start.png", "priority": 10, "baseSize": { "width": 1200, "height": 600 } },
            { "file": "old.png",   "priority": 20 }
          ]
        }
        """;

        var dto = TargetSetSerializer.Deserialize(json);
        Assert.Equal(new Size(1200, 600), dto.Images.Single(i => i.File == "start.png").BaseSize);
        Assert.Null(dto.Images.Single(i => i.File == "old.png").BaseSize);

        var back = TargetSetSerializer.Deserialize(TargetSetSerializer.Serialize(dto));
        Assert.Equal(new Size(1200, 600), back.Images.Single(i => i.File == "start.png").BaseSize);

        var set = TargetSetLoader.FromJson(back, @"C:\img\yuhun");
        Assert.Equal(new Size(1200, 600), set.Images.Single(i => i.Name == "start").BaseSize);
        Assert.Null(set.Images.Single(i => i.Name == "old").BaseSize);   // 没记的老模板 → 不缩放
    }

    [Fact]
    public void FromJson_drops_non_positive_base_size()
    {
        // 手工改坏/占位的尺寸不能进运行期模型,否则会算出 0 或负的缩放比。
        var dto = new TargetSetJson
        {
            Images =
            {
                new TargetImageJson { File = "a.png", BaseSize = new Size(0, 600) },
                new TargetImageJson { File = "b.png", BaseSize = new Size(1200, -1) },
            },
        };

        var set = TargetSetLoader.FromJson(dto, @"C:\img\yuhun");

        Assert.All(set.Images, i => Assert.Null(i.BaseSize));
    }

    [Fact]
    public void FromJson_applies_defaults_and_per_image_matcher_override()
    {
        var dto = new TargetSetJson
        {
            Name = "",  // 空 → 回退文件夹名
            Defaults = new TargetDefaultsJson { Matcher = MatchHint.Template },
            Images =
            {
                new TargetImageJson { File = "a.jpg", Priority = 20 },  // 用 defaults
                new TargetImageJson { File = "b.jpg", Priority = 10, Matcher = MatchHint.Feature },
            },
        };

        var set = TargetSetLoader.FromJson(dto, @"C:\img\yuhun");

        Assert.Equal("yuhun", set.Name);                        // 回退文件夹名
        Assert.Equal("b", set.Images[0].Name);                  // 按优先级升序:b(10) 在前

        var a = set.Images.Single(i => i.Name == "a");
        Assert.Equal(MatchHint.Template, a.Hint);               // 来自 defaults

        var b = set.Images.Single(i => i.Name == "b");
        Assert.Equal(MatchHint.Feature, b.Hint);                // 每图覆盖
        Assert.Equal(Path.Combine(@"C:\img\yuhun", "b.jpg"), b.FilePath);
    }
}
