using System.Reflection;
using SenderNG.Localization;

namespace SenderNG.ViewModels;

public class AboutViewModel : ViewModelBase
{
    public string Title => LocText.Resolve("about.title", "About SenderNG");
    public string AboutTab => LocText.Resolve("about.tab.about", "About");
    public string LibrariesTab => LocText.Resolve("about.tab.libraries", "Libraries");
    public string AppName => LocText.Resolve("about.name", "SenderNG");
    public string Version => LocText.Format("about.version", "Version: {0}", AssemblyVersion);
    public string Copyright => LocText.Resolve("about.copyright",
        "Copyright (c) 2024-2026 Shkolik · 2019-2023 Io Engineering");
    public string License => LocText.Resolve("about.license", "BSD 3-Clause License");
    public string RepoUrl => LocText.Resolve("about.repo", "https://github.com/Shkolik/SenderNG");
    public IReadOnlyList<ThirdPartyLibrary> Libraries => ThirdPartyLibraries.List;

    private static string AssemblyVersion =>
        typeof(AboutViewModel).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
