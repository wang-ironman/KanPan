using KanPan.Core.Import;
using KanPan.Core.Models;

namespace KanPan.Tests;

public class HoldingParserTests
{
    [Fact]
    public void Parses_mixed_separators_and_optional_cost()
    {
        var result = HoldingParser.Parse("600519,100\n000001 2000\n300750\t500\t180.5\n510300，1000，3.852");

        Assert.False(result.HasErrors);
        Assert.Equal(
            [
                new Holding(SecurityId.Parse("600519"), 100, null),
                new Holding(SecurityId.Parse("000001"), 2000, null),
                new Holding(SecurityId.Parse("300750"), 500, 180.5m),
                new Holding(SecurityId.Parse("510300"), 1000, 3.852m),
            ],
            result.Holdings);
    }

    [Fact]
    public void Skips_blank_lines_comments_and_header()
    {
        var result = HoldingParser.Parse("\uFEFF代码,股数,成本\r\n\r\n# 我的持仓\r\n600519,100\r\n");
        Assert.Single(result.Lines);
        Assert.Single(result.Holdings);
    }

    [Fact]
    public void Header_is_only_detected_on_first_content_line()
    {
        var result = HoldingParser.Parse("600519,100\ncode,shares");
        Assert.True(result.HasErrors);
    }

    [Theory]
    [InlineData("600519", "缺少持仓股数")]
    [InlineData("abc,100", "无法识别")]
    [InlineData("600519,0", "正整数")]
    [InlineData("600519,-5", "正整数")]
    [InlineData("600519,1.5", "正整数")]
    [InlineData("600519,100,abc", "成本价")]
    public void Reports_line_errors(string line, string messagePart)
    {
        var result = HoldingParser.Parse(line);
        var parsed = Assert.Single(result.Lines);
        Assert.Equal(ParseStatus.Error, parsed.Status);
        Assert.Contains(messagePart, parsed.Message);
        Assert.Empty(result.Holdings);
    }

    [Theory]
    [InlineData("600519,100,0", "0")]
    [InlineData("600519,100,-12.5", "-12.5")]
    public void Accepts_zero_or_negative_diluted_cost(string line, string cost) =>
        Assert.Equal(decimal.Parse(cost), Assert.Single(HoldingParser.Parse(line).Holdings).CostPrice);

    [Fact]
    public void Errors_do_not_block_other_lines()
    {
        var result = HoldingParser.Parse("600519,100\nbad line\n000001,200");
        Assert.True(result.HasErrors);
        Assert.Equal(2, result.Holdings.Count);
        Assert.Equal(2, result.Lines[1].LineNumber);
    }

    [Fact]
    public void Merges_duplicates_with_weighted_cost()
    {
        var result = HoldingParser.Parse("600519,100,1500\nsh600519,300,1700");

        var h = Assert.Single(result.Holdings);
        Assert.Equal(400, h.Shares);
        Assert.Equal(1650m, h.CostPrice);
        Assert.Equal(ParseStatus.Merged, result.Lines[1].Status);
        Assert.Contains("第 1 行", result.Lines[1].Message);
    }

    [Fact]
    public void Merge_drops_cost_when_any_line_lacks_it()
    {
        var result = HoldingParser.Parse("600519,100,1500\n600519,300");
        var h = Assert.Single(result.Holdings);
        Assert.Equal(400, h.Shares);
        Assert.Null(h.CostPrice);
    }

    [Fact]
    public void Format_round_trips()
    {
        var holdings = HoldingParser.Parse("600519,100,1550.5\n000001,2000").Holdings;
        Assert.Equal(holdings, HoldingParser.Parse(HoldingParser.Format(holdings)).Holdings);
    }
}
