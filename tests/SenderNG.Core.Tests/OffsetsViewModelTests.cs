using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Fixed;

namespace SenderNG.Core.Tests;

public class OffsetsViewModelTests
{
    private static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        return new SettingsService(dir);
    }

    [Fact]
    public void Rows_contain_G54_through_G59()
    {
        var vm = new OffsetsViewModel(MakeSettings());

        Assert.Equal(6, vm.Rows.Count);
        Assert.Equal(["G54", "G55", "G56", "G57", "G58", "G59"],
            vm.Rows.Select(r => r.Name));
    }

    [Fact]
    public void SaveCommand_persists_edits_and_reload_roundtrips()
    {
        var settings = MakeSettings();
        var vm = new OffsetsViewModel(settings);
        vm.Rows[0].X = 1.5;
        vm.Rows[0].Y = -2.25;
        vm.Rows[3].Z = 10;

        vm.SaveCommand.Execute(null);

        var reloaded = new OffsetsViewModel(settings);
        Assert.Equal(1.5, reloaded.Rows[0].X);
        Assert.Equal(-2.25, reloaded.Rows[0].Y);
        Assert.Equal(10, reloaded.Rows[3].Z);
    }

    [Fact]
    void Constructor_loads_existing_values_from_settings_file()
    {
        var settings = MakeSettings();
        var first = new OffsetsViewModel(settings);
        first.Rows[2].X = 42;
        first.SaveCommand.Execute(null);

        var second = new OffsetsViewModel(settings);

        Assert.Equal(42, second.Rows[2].X);
    }
}
