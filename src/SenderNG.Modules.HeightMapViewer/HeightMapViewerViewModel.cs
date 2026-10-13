using System.ComponentModel;
using System.Runtime.CompilerServices;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;

namespace SenderNG.Modules.HeightMapViewer;

/// <summary>
/// Backs the 3D height-map dialog. Subscribes <see cref="IHeightMapSource.Snapshot"/>
/// and surfaces the latest layer plus an <see cref="IsEmpty"/> flag so the view can
/// swap in the localized empty state. The heavy GL surface lands in a later task;
/// this view model stays headless and dependency-light on purpose.
/// </summary>
public sealed class HeightMapViewerViewModel : INotifyPropertyChanged, IDisposable
{
    private const string EmptyTextKey = "module.heightmap-viewer.empty";
    private const string EmptyTextFallback = "No height map loaded. Probe or load a map first.";

    private readonly LocalizationService? _localization;
    private readonly IDisposable? _subscription;
    private HeightMapLayer? _layer;

    public HeightMapViewerViewModel(IHeightMapSource? source, LocalizationService? localization)
    {
        _localization = localization;
        _subscription = source?.Snapshot.Subscribe(OnSnapshot);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>True while no layer has been published (or the source cleared it).</summary>
    public bool IsEmpty => _layer is null;

    /// <summary>The latest published height map, or null when cleared/not probed.</summary>
    public HeightMapLayer? Layer => _layer;

    /// <summary>Localized empty-state message the placeholder view binds.</summary>
    public string EmptyText =>
        _localization?.Resolve(EmptyTextKey, EmptyTextFallback) ?? EmptyTextFallback;

    private void OnSnapshot(HeightMapLayer? layer)
    {
        _layer = layer;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Layer));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose() => _subscription?.Dispose();
}
