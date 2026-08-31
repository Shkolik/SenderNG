using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SenderNG.Views;

public partial class AboutView : Window
{
    public AboutView()
    {
        InitializeComponent();
    }

    /// <summary>The dialog's Close button</summary>
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
