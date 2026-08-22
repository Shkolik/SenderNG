using SenderNG.Avalonia.Modules.Fixed;

namespace SenderNG.Core.Tests;

public class GrblSettingsViewModelTests
{
    [Fact]
    public void Parse_splits_key_value_and_comment()
    {
        var row = GrblSettingsViewModel.Parse("$110=800.000 (x mm/min)");

        Assert.NotNull(row);
        Assert.Equal("110", row.Key);
        Assert.Equal("800.000", row.Value);
        Assert.Equal("x mm/min", row.Comment);
    }

    [Fact]
    public void Parse_handles_line_without_comment()
    {
        var row = GrblSettingsViewModel.Parse("$110=800.000");

        Assert.NotNull(row);
        Assert.Equal("110", row.Key);
        Assert.Equal("800.000", row.Value);
        Assert.Equal(string.Empty, row.Comment);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ok")]
    [InlineData("<Idle|WPos:1.0,2.0,3.0>")]
    public void Parse_returns_null_for_non_setting_lines(string line)
    {
        Assert.Null(GrblSettingsViewModel.Parse(line));
    }

    [Fact]
    public async Task LoadCommand_fills_list_from_provider()
    {
        IEnumerable<string> lines = ["$110=800.000 (x mm/min)", "$111=1000.000 (y mm/min)", "ok"];
        var vm = new GrblSettingsViewModel(() => Task.FromResult(lines));

        await vm.LoadAsync();

        Assert.Equal(2, vm.Settings.Count);
        Assert.Equal("110", vm.Settings[0].Key);
        Assert.Equal("1000.000", vm.Settings[1].Value);

        vm.LoadCommand.Execute(null);
        Assert.Equal(2, vm.Settings.Count);
    }
}
