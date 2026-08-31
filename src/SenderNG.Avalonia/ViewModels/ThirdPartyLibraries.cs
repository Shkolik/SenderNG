namespace SenderNG.Avalonia.ViewModels;

public sealed record ThirdPartyLibrary(string Name, string Version, string License)
{
    /// <summary>Second list row: "v.{version}  license: {license}".</summary>
    public string VersionLicense => $"v.{Version}  license: {License}";
}

/// <summary>Static list of the third-party libraries shown in the About dialog's Libraries tab.</summary>
public static class ThirdPartyLibraries
{
    public static IReadOnlyList<ThirdPartyLibrary> List { get; } =
    [
        new("Avalonia", "12.1.1", "MIT"),
        new("SkiaSharp", "2.x", "MIT"),
        new("System.Reactive", "6.x", "MIT"),
        new("System.IO.Ports", "8.x", "MIT"),
        new("Microsoft.Extensions.Hosting", "8.x", "MIT"),
        new("Microsoft.Extensions.DependencyInjection", "8.x", "MIT"),
    ];
}
