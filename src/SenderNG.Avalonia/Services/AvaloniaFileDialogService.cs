using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CNC.Core.Abstractions;

namespace SenderNG.Avalonia.Services;

/// <summary>
/// Avalonia implementation of <see cref="IFileDialogService"/> via StorageProvider
/// and a minimal inline message box window.
/// </summary>
public class AvaloniaFileDialogService : IFileDialogService
{
    public async Task<string?> OpenFileAsync(string title, params string[] filters)
    {
        var provider = GetStorageProvider();
        if (provider is null)
            return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        };

        var fileTypes = new List<FilePickerFileType>();
        foreach (var filter in filters)
        {
            // Filter format: "Name|*.a;*.b" (one '|' separator)
            var parts = filter.Split('|', 2);
            if (parts.Length != 2) continue;
            fileTypes.Add(new FilePickerFileType(parts[0])
            {
                Patterns = parts[1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            });
        }
        if (fileTypes.Count > 0)
            options.FileTypeFilter = fileTypes;

        var files = await provider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public Task ShowErrorAsync(string title, string message)
    {
        var tcs = new TaskCompletionSource();
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
        };

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 80,
            Height = 28,
            MinHeight = 28,
            CornerRadius = new CornerRadius(0),
            Background = new SolidColorBrush(Color.Parse("#eee")),
            BorderBrush = new SolidColorBrush(Color.Parse("#999")),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        okButton.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                okButton,
            },
        };

        dialog.Closed += (_, _) => tcs.SetResult();

        var owner = GetMainWindow();
        if (owner is not null)
            dialog.Show(owner);
        else
            dialog.Show();

        return tcs.Task;
    }

    private static Window? GetMainWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private static IStorageProvider? GetStorageProvider() => GetMainWindow()?.StorageProvider;
}
