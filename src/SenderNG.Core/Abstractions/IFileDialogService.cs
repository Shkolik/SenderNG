namespace CNC.Core.Abstractions;

/// <summary>
/// Abstraction over native file dialogs and message boxes (MVVM-friendly).
/// </summary>
public interface IFileDialogService
{
    /// <summary>Returns the selected file path, or null if cancelled.</summary>
    Task<string?> OpenFileAsync(string title, params string[] filters);

    /// <summary>Returns the chosen save path, or null if cancelled.</summary>
    Task<string?> SaveFileAsync(string title, string suggestedFileName, params string[] filters);

    /// <summary>Shows a modal error dialog.</summary>
    Task ShowErrorAsync(string title, string message);
}
