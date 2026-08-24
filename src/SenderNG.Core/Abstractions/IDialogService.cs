namespace CNC.Core.Abstractions;

public interface IDialogService
{
    /// <summary>preferredSize: explicit window size -> manually resizable; null -> size to content.</summary>
    Task<bool?> ShowDialogAsync(string title, object viewModel, (double Width, double Height)? preferredSize = null);
}

/// <summary>
/// Implemented by dialog view models so a generic host can close them with a result.
/// </summary>
public interface IClosableDialog
{
    event EventHandler<bool>? CloseRequested;
}
