using Avalonia.Threading;

namespace SenderNG.Avalonia;

/// <summary>
/// Marshals callbacks to the Avalonia UI thread. Machine-state emissions can
/// arrive on background threads (demo poll timer, future serial-port reads),
/// but ViewModels touch Avalonia-owned objects (brushes, bound collections)
/// which must be updated on the UI thread.
/// <para>
/// The dispatcher is captured at app startup via <see cref="Initialize"/>;
/// headless/unit-test hosts never initialize it, so callbacks run inline on
/// the calling thread - keeping synchronous test assertions deterministic.
/// </para>
/// </summary>
public static class UiThread
{
    private static Dispatcher? _uiDispatcher;

    /// <summary>Bind to the app's UI dispatcher. Call once from the UI thread (App startup).</summary>
    public static void Initialize() => _uiDispatcher = Dispatcher.UIThread;

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread, or inline when already
    /// there (or when no dispatcher was initialized, e.g. unit tests).
    /// </summary>
    public static void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var dispatcher = _uiDispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Post(action);
    }
}
