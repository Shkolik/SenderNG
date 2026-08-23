using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using CNC.Core.Abstractions;

namespace SenderNG.Avalonia.Services;

public class AvaloniaDialogService : IDialogService
{
    private readonly Func<object, object> _viewResolver;

    public AvaloniaDialogService(Func<object, object> viewResolver)
    {
        _viewResolver = viewResolver;
    }

    public async Task<bool?> ShowDialogAsync(string title, object viewModel)
    {
        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#f6f6f6")),
            Content = _viewResolver(viewModel),
        };

        bool? result = null;
        if (viewModel is IClosableDialog closable)
            closable.CloseRequested += (_, r) =>
            {
                result = r;
                window.Close(r);
            };

        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null)
        {
            window.Show();
            return result;
        }

        await window.ShowDialog(owner);
        return result;
    }
}
