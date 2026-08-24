using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.Services;
using SenderNG.Avalonia.ViewModels;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia;

/// <summary>
/// Builds the application host: settings, GRBL services (Demo as default), and
/// module discovery via <see cref="ModuleCatalog"/>.
/// </summary>
public static class HostSetup
{
    public static IHost BuildHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddSingleton<SettingsService>();
        // Shared AppSettings graph: one instance for every consumer (VMs, services).
        builder.Services.AddSingleton(sp => sp.GetRequiredService<SettingsService>().Load());
        builder.Services.AddSingleton<IComPortService, SerialComPortService>();
        builder.Services.AddSingleton<IFileDialogService, AvaloniaFileDialogService>();
        builder.Services.AddSingleton<IDialogService, AvaloniaDialogService>();
        builder.Services.AddSingleton<Func<object, object>>(sp =>
            vm => vm switch
            {
                CNC.Core.ViewModels.DemoMachineSettingsViewModel v => new Views.DemoMachineSettingsView { DataContext = v },
                CNC.Core.ViewModels.AppSettingsViewModel v => new Modules.Fixed.AppSettingsView { DataContext = v },
                Modules.Fixed.GrblSettingsViewModel v => new Modules.Fixed.GrblSettingsView { DataContext = v },
                _ => throw new NotSupportedException($"No view registered for {vm.GetType().Name}")
            });
        builder.Services.AddSingleton<DemoGrblService>();
        builder.Services.AddSingleton<RealGrblService>();
        // The proxy is THE IGrblService singleton: consumers subscribe once and
        // survive demo/real swaps via Inner reassignment.
        builder.Services.AddSingleton<IGrblService>(sp =>
            new GrblServiceProxy(sp.GetRequiredService<DemoGrblService>()));
        // Settings route through the proxy to whichever inner service is active.
        builder.Services.AddSingleton<IGrblSettingsProvider>(sp =>
            (IGrblSettingsProvider)sp.GetRequiredService<IGrblService>());

        // Built-in fixed modules (no assembly scan).
        RegisterModule(builder.Services, new Modules.Fixed.DroModule());
        RegisterModule(builder.Services, new Modules.Fixed.OffsetsModule());
        RegisterModule(builder.Services, new Modules.Fixed.ProgramModule());
        RegisterModule(builder.Services, new Modules.Fixed.ConsoleModule());
        RegisterModule(builder.Services, new Modules.Fixed.GrblSettingsModule());
        RegisterModule(builder.Services, new Modules.Fixed.AppSettingsModule());

        // Plugin modules (jog / cutting / coolant / macro).
        RegisterModule(builder.Services, new Modules.Plugins.JogModule());
        RegisterModule(builder.Services, new Modules.Plugins.CuttingModule());
        RegisterModule(builder.Services, new Modules.Plugins.CoolantModule());
        RegisterModule(builder.Services, new Modules.Plugins.MacroModule());
        RegisterModule(builder.Services, new Modules.Plugins.ProbingModule());

        // External module assemblies: discovered via ModuleCatalog assembly scan
        // (proves the plugin discovery path — Dummy lives in its own project).
        ModuleCatalog.Scan(builder.Services, typeof(SenderNG.Modules.Dummy.DummyModule).Assembly);

        // Avalonia-side view factory: module -> view model + view as controls.
        builder.Services.AddSingleton<Func<IModule, object>>(sp =>
            module => module.CreateView(module.CreateViewModel(sp)));

        builder.Services.AddSingleton(sp =>
            new MainViewModel(
                sp.GetRequiredService<IGrblService>(),
                sp.GetRequiredService<SettingsService>(),
                sp,
                comPorts: sp.GetRequiredService<IComPortService>(),
                fileDialogs: sp.GetRequiredService<IFileDialogService>(),
                dialogs: sp.GetRequiredService<IDialogService>(),
                modules: sp.GetServices<IModule>(),
                viewFactory: sp.GetRequiredService<Func<IModule, object>>()));

        var host = builder.Build();

        // Settings fields may be read before a module's UI is ever opened.
        foreach (var module in host.Services.GetServices<IModule>())
            if (module is IModuleSettingsProvider provider)
                provider.AttachServices(host.Services);

        return host;
    }

    private static void RegisterModule(IServiceCollection services, IModule module)
    {
        // NOTE: do not call AddSingleton(module) here - 'module' is statically typed
        // IModule, so generic inference would register IModule a second time.
        services.AddSingleton<IModule>(module);
        module.RegisterServices(services);
    }
}
