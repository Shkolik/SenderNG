using System.Reflection;
using SenderNG.Core.Modules;
using SenderNG.Localization;

namespace SenderNG.ViewModels;

public class AboutViewModel : ViewModelBase
{
    public AboutViewModel(IEnumerable<IModule>? modules = null)
    {
        // Loaded plugins carrying IPluginInfo metadata — the About → Modules tab.
        // Names resolve through the locale (module.<id>.name, plugin-first),
        // falling back to the module's literal Name.
        Modules = (modules ?? Enumerable.Empty<IModule>())
            .Select(m => (Module: m, Info: m as IPluginInfo, Deps: m as IPluginDependencies))
            .Where(t => t.Info is not null)
            .Select(t => new ModuleInfoItem(
                LocText.Resolve($"module.{t.Module.Id}.name", t.Module.Name),
                t.Info!.Version,
                t.Info.Author,
                t.Info.License,
                t.Deps?.Dependencies))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string Title => LocText.Resolve("about.title", "About SenderNG");
    public string AboutTab => LocText.Resolve("about.tab.about", "About");
    public string LibrariesTab => LocText.Resolve("about.tab.libraries", "Libraries");
    public string ModulesTab => LocText.Resolve("about.tab.modules", "Modules");
    public string AppName => LocText.Resolve("about.name", "SenderNG");
    public string Version => LocText.Format("about.version", "Version: {0}", AssemblyVersion);
    public string Copyright => LocText.Resolve("about.copyright",
        "Copyright (c) 2024-2026 Shkolik · 2019-2023 Io Engineering");
    public string License => LocText.Resolve("about.license", "BSD 3-Clause License");
    public string RepoUrl => LocText.Resolve("about.repo", "https://github.com/Shkolik/SenderNG");
    public IReadOnlyList<ThirdPartyLibrary> Libraries => ThirdPartyLibraries.List;
    public IReadOnlyList<ModuleInfoItem> Modules { get; }

    private static string AssemblyVersion =>
        typeof(AboutViewModel).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}

/// <summary>One loaded plugin row on the About → Modules tab (IPluginInfo metadata + optional IPluginDependencies).</summary>
public sealed record ModuleInfoItem(
    string Name, string Version, string Author, string License,
    IReadOnlyList<PluginDependency>? Dependencies = null)
{
    /// <summary>Whether the plugin declared dependencies (drives expander visibility).</summary>
    public bool HasDependencies { get; } = Dependencies is { Count: > 0 };
}