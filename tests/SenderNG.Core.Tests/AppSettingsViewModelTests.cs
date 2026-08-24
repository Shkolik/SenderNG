using CNC.Core.Services;
using CNC.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using Xunit;

namespace SenderNG.Core.Tests;

public class AppSettingsViewModelTests
{
    private sealed class StubModule : IModule
    {
        public StubModule(string id, string name, int order, bool visibleByDefault = true, bool canHide = true)
        {
            Id = id;
            Name = name;
            Order = order;
            IsVisibleByDefault = visibleByDefault;
            CanHide = canHide;
        }
        public string Id { get; }
        public string Name { get; }
        public int Order { get; }
        public bool IsVisibleByDefault { get; }
        public bool CanHide { get; }
        public IReadOnlyList<HotKey> HotKeys => [];
        public void RegisterServices(IServiceCollection services) { }
        public void RegisterSettings(AppSettings settings) { }
        public object CreateViewModel(IServiceProvider sp) => new object();
        public object CreateView(object vm) => new object();
    }

    private static (string Dir, SettingsService Service) MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-appsettings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (dir, new SettingsService(dir));
    }

    private static readonly IModule[] Modules =
    [
        new StubModule("dro", "DRO", 10),
        new StubModule("jog", "Jog", 20),
        new StubModule("console", "Console", 30, visibleByDefault: false),
    ];

    [Fact]
    public void Toggling_visibility_persists_to_current_mode()
    {
        var (dir, settings) = MakeSettings();
        var vm = new AppSettingsViewModel(settings, Modules);

        vm.ModuleSections.First(e => e.Id == "jog").IsVisible = false;
        vm.ModuleSections.First(e => e.Id == "console").IsVisible = true;

        // Fresh service instance proves values hit disk.
        var reloaded = new SettingsService(dir).Load();
        Assert.False(reloaded.Current.ModulesVisibility["jog"]);
        Assert.True(reloaded.Current.ModulesVisibility["console"]);
    }

    [Fact]
    public void Mode_switch_reflects_that_mode_visibility()
    {
        var (_, settings) = MakeSettings();
        settings.Load().Modes["Mill"] = new ModeSettings { ModulesVisibility = new Dictionary<string, bool> { ["jog"] = true } };
        settings.Load().Modes["Foam"] = new ModeSettings { ModulesVisibility = new Dictionary<string, bool> { ["jog"] = false } };

        var vm = new AppSettingsViewModel(settings, Modules);

        Assert.True(vm.IsMill);
        Assert.True(vm.ModuleSections.First(e => e.Id == "jog").IsVisible);

        vm.IsFoam = true;

        Assert.Equal("Foam", settings.Load().Mode);
        Assert.False(vm.IsMill);
        Assert.True(vm.IsFoam);
        Assert.False(vm.ModuleSections.First(e => e.Id == "jog").IsVisible);

        vm.IsMill = true;

        Assert.True(vm.ModuleSections.First(e => e.Id == "jog").IsVisible);
    }

    [Fact]
    public void Close_command_requests_close_with_true()
    {
        var (_, settings) = MakeSettings();
        var vm = new AppSettingsViewModel(settings, Modules);
        bool? result = null;
        vm.CloseRequested += (_, r) => result = r;

        vm.CloseCommand.Execute(null);

        Assert.True(result ?? false);
    }

    [Fact]
    public void Module_sections_ordered_by_module_order()
    {
        var (_, settings) = MakeSettings();
        var vm = new AppSettingsViewModel(settings, Modules);

        Assert.Equal(new[] { "dro", "jog", "console" }, vm.ModuleSections.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Fixed_modules_without_settings_are_excluded()
    {
        var (_, settings) = MakeSettings();
        var modules = new IModule[]
        {
            new StubModule("dro", "DRO", 10, canHide: false),
            new StubModule("jog", "Jog", 20),
        };

        var vm = new AppSettingsViewModel(settings, modules);

        Assert.Equal(new[] { "jog" }, vm.ModuleSections.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Legacy_flat_settings_json_migrates_to_mill_mode()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"),
            """
            {
              "Mode": "Mill",
              "DemoAxes": 5,
              "Port": "COM7",
              "ModulesVisibility": { "jog": false }
            }
            """);

        var loaded = new SettingsService(dir).Load();

        Assert.Equal(2, loaded.Modes.Count);
        Assert.Contains(AppSettings.MillMode, loaded.Modes.Keys);
        Assert.Contains(AppSettings.FoamMode, loaded.Modes.Keys);
        var mill = loaded.Modes[AppSettings.MillMode];
        Assert.Equal(5, mill.DemoAxes);
        Assert.Equal("COM7", mill.Port);
        Assert.False(mill.ModulesVisibility["jog"]);
    }
}
