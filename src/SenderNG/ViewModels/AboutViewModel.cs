using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SenderNG.Core.Commands;
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

        CloseCmd = new DelegateCommand(_ => RequestClose(true));
        CopyInfoCmd = new DelegateCommand(_ => CopyInfo());
        CopyButtonText = LocText.Resolve("common.copy_info", "Copy info");
    }

    public string Title => LocText.Resolve("about.title", "About SenderNG");
    public string AboutTab => LocText.Resolve("about.tab.about", "About");
    public string LibrariesTab => LocText.Resolve("about.tab.libraries", "Libraries");
    public string ModulesTab => LocText.Resolve("about.tab.modules", "Modules");
    public string AppName => LocText.Resolve("about.name", "SenderNG");
    public string Version => LocText.Format("about.version", "Version: {0}", AssemblyVersion);
    public string Copyright => LocText.Resolve("about.copyright",
        "Copyright (c) 2024-2026 Andrew Shkolik · 2019-2023 Io Engineering");
    public string License => LocText.Resolve("about.license", "BSD 3-Clause License");
    public string RepoUrl => LocText.Resolve("about.repo", "https://github.com/Shkolik/SenderNG");
    public IReadOnlyList<ThirdPartyLibrary> Libraries => ThirdPartyLibraries.List;
    public IReadOnlyList<ModuleInfoItem> Modules { get; }

    private static string AssemblyVersion =>
        typeof(AboutViewModel).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private static readonly TimeSpan CopyFeedbackDuration = TimeSpan.FromMilliseconds(2000);
    private int _copyGen;

    public event EventHandler<bool>? CloseRequested;
    public event EventHandler<string>? CopyRequested;

    public DelegateCommand CloseCmd { get; }
    public DelegateCommand CopyInfoCmd { get; }

    private string _copyButtonText = string.Empty;
    public string CopyButtonText
    {
        get => _copyButtonText;
        private set => SetField(ref _copyButtonText, value);
    }

    /// <summary>
    /// Called by the view after it writes the copied text to the clipboard. On
    /// success the button label flips to "Copied" for ~2 s; on failure it stays put.
    /// </summary>
    public void SetCopyResult(bool success)
    {
        if (!success)
            return;
        var gen = ++_copyGen;
        CopyButtonText = LocText.Resolve("common.copied", "Copied");
        _ = RevertCopyLabelAsync(gen);
    }

    private async Task RevertCopyLabelAsync(int gen)
    {
        await Task.Delay(CopyFeedbackDuration);
        if (gen == _copyGen)
            CopyButtonText = LocText.Resolve("common.copy_info", "Copy info");
    }

    private void RequestClose(bool result) => CloseRequested?.Invoke(this, result);

    private void CopyInfo() => CopyRequested?.Invoke(this, BuildCopyText());

    /// <summary>
    /// The full clipboard payload: header line, then Libraries / Plugins / System
    /// sections. Fixed English headings; the header carries the active language.
    /// </summary>
    public string BuildCopyText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AppName} Version: {AssemblyVersion} ({LocText.GetLanguage()})");
        sb.AppendLine();

        sb.AppendLine("Libraries:");
        foreach (var lib in Libraries)
            sb.AppendLine($"{lib.Name} {lib.VersionLicense}");
        sb.AppendLine();

        sb.AppendLine("Plugins:");
        foreach (var m in Modules)
        {
            sb.AppendLine($"{m.Name} v.{m.Version} — {m.AuthorLicense}");
            if (m.HasDependencies)
                foreach (var d in m.Dependencies!)
                    sb.AppendLine($"    {d.OneLine}");
        }
        sb.AppendLine();

        sb.AppendLine("System:");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        sb.AppendLine($"OS architecture: {RuntimeInformation.OSArchitecture}");
        sb.AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($".NET runtime: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"UI culture: {CultureInfo.CurrentUICulture.Name}");
        sb.AppendLine($"Processors: {Environment.ProcessorCount}");
        return sb.ToString().Replace("\r\n", "\n");
    }
}

/// <summary>One loaded plugin row on the About → Modules tab (IPluginInfo metadata + optional IPluginDependencies).</summary>
public sealed record ModuleInfoItem(
    string Name, string Version, string Author, string License,
    IReadOnlyList<PluginDependency>? Dependencies = null)
{
    /// <summary>Whether the plugin declared dependencies (drives expander visibility).</summary>
    public bool HasDependencies { get; } = Dependencies is { Count: > 0 };

    /// <summary>Card line 2: "Author · License".</summary>
    public string AuthorLicense { get; } = $"{Author} · {License}";
}