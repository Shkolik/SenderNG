namespace CNC.Core.Abstractions;

public interface IDialogService
{
    Task<bool?> ShowDialogAsync(string title, object viewModel);
}

/// <summary>
/// Implemented by dialog view models so a generic host can close them with a result.
/// </summary>
public interface IClosableDialog
{
    event EventHandler<bool>? CloseRequested;
}
