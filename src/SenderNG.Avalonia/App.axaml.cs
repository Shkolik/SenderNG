using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Avalonia;

public partial class App : Application
{
    public static IHost? Host { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // State emissions can arrive on background threads (demo poll timer,
        // serial reads); capture the UI dispatcher so UiThread can marshal them.
        UiThread.Initialize();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Host = HostSetup.BuildHost([]);
            var vm = Host.Services.GetRequiredService<MainViewModel>();

            desktop.MainWindow = new MainWindow { DataContext = vm };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
