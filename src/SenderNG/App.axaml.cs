using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SenderNG.ViewModels;

namespace SenderNG;

public partial class App : Application
{
    public static IHost? Host { get; private set; }

    /// <summary>Command-line args from <see cref="Program.Main"/> (Avalonia's lifetime
    /// doesn't forward them), read when building the host in <see cref="OnFrameworkInitializationCompleted"/>.</summary>
    public static string[] StartupArgs { get; set; } = [];

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
            Host = HostSetup.BuildHost(StartupArgs);
            var vm = Host.Services.GetRequiredService<MainViewModel>();

            desktop.MainWindow = new MainWindow { DataContext = vm };
            // The app starts disconnected; open the connect dialog right after
            // the window is shown so the user picks Serial / Network / Demo first.
            desktop.MainWindow.Opened += (_, _) => _ = vm.ShowConnectDialogAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
