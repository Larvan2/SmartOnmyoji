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
