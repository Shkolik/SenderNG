using CNC.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;

namespace SenderNG.Modules.HeightMapViewer;

public sealed class HeightMapViewerModule : IModule, IHeightMap3DRenderer,
    ILocalizedModule, IPluginInfo, IPluginDependencies, IModuleSettingsProvider
{
    private IServiceProvider? _services;

    public string Id => "heightmap-viewer";
    public string Name => "Height map 3D";
    public string Description => "3D view of the probed height map.";
    public int Order => 51;
    public PanelHint Panel => PanelHint.Dialog;
    public bool IsVisibleByDefault => false;
    public bool IsEnabledByDefault => true;
    public bool CanHide => true;
    public IReadOnlyList<HotKey> HotKeys => [];

    public string Version => "0.1.0";
    public string Author => "Andrew Shkolik";
    public string License => "BSD-3-Clause";
    public IReadOnlyList<PluginDependency> Dependencies =>
        [new("Silk.NET.OpenGL", "2.23.0", "MIT")];

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.heightmap-viewer.name"] = "Height map 3D",
            ["module.heightmap-viewer.description"] = "3D view of the probed height map.",
            ["module.heightmap-viewer.empty"] = "No height map loaded. Probe or load a map first.",
            ["module.heightmap-viewer.title"] = "Height map 3D",
            ["module.heightmap-viewer.reset"] = "Reset view",
        };

    public void RegisterServices(IServiceCollection services)
        => services.AddSingleton<IHeightMap3DRenderer>(this);

    public object CreateViewModel(IServiceProvider sp)
        => new HeightMapViewerViewModel(
            sp.GetService<IHeightMapSource>(),
            sp.GetService<LocalizationService>());

    public object CreateView(object vm) => new HeightMapViewerView { DataContext = vm };

    public (double, double)? DialogPreferredSize => (900, 640);

    public SettingsScope Scope => SettingsScope.Profile;
    public object? CreateSettingsView() => null;
    public void AttachServices(IServiceProvider services) => _services = services;

    public Task ShowAsync()
    {
        var dialogs = _services?.GetService<IDialogService>();
        var source = _services?.GetService<IHeightMapSource>();
        if (dialogs is null) return Task.CompletedTask;
        var vm = new HeightMapViewerViewModel(source, _services?.GetService<LocalizationService>());
        return dialogs.ShowDialogAsync(
            _services?.GetService<LocalizationService>()?.Resolve("module.heightmap-viewer.title", "Height map 3D")
                ?? "Height map 3D",
            vm, (900, 640));
    }
}
