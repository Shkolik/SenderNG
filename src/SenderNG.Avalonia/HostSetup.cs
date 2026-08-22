using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CNC.Core.Abstractions;
using CNC.Core.Services;
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
        builder.Services.AddSingleton<DemoGrblService>();
        builder.Services.AddSingleton<RealGrblService>();
        // The proxy is THE IGrblService singleton: consumers subscribe once and
        // survive demo/real swaps via Inner reassignment.
        builder.Services.AddSingleton<IGrblService>(sp =>
            new GrblServiceProxy(sp.GetRequiredService<DemoGrblService>()));

        // Built-in fixed modules (no assembly scan).
        RegisterModule(builder.Services, new Modules.Fixed.DroModule());
        RegisterModule(builder.Services, new Modules.Fixed.OffsetsModule());
        RegisterModule(builder.Services, new Modules.Fixed.GCodeConsoleModule());
        RegisterModule(builder.Services, new Modules.Fixed.GrblSettingsModule());

        // Plugin modules (jog / cutting / coolant / macro).
        RegisterModule(builder.Services, new Modules.Plugins.JogModule());
        RegisterModule(builder.Services, new Modules.Plugins.CuttingModule());
        RegisterModule(builder.Services, new Modules.Plugins.CoolantModule());
        RegisterModule(builder.Services, new Modules.Plugins.MacroModule());
        RegisterModule(builder.Services, new Modules.Plugins.ProbingModule());

        // External module assemblies are added here as they appear.
        ModuleCatalog.Scan(builder.Services);

        // Avalonia-side view factory: module -> view model + view as controls.
        builder.Services.AddSingleton<Func<IModule, object>>(sp =>
            module => module.CreateView(module.CreateViewModel(sp)));

        builder.Services.AddSingleton(sp =>
            new MainViewModel(
                sp.GetRequiredService<IGrblService>(),
                sp.GetRequiredService<SettingsService>(),
                sp,
                comPorts: sp.GetRequiredService<IComPortService>(),
                modules: sp.GetServices<IModule>(),
                viewFactory: sp.GetRequiredService<Func<IModule, object>>()));

        return builder.Build();
    }

    private static void RegisterModule(IServiceCollection services, IModule module)
    {
        services.AddSingleton(module);
        services.AddSingleton<IModule>(module);
        module.RegisterServices(services);
    }
}
