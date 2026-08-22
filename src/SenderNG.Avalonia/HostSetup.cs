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
        builder.Services.AddSingleton<DemoGrblService>();
        builder.Services.AddSingleton<RealGrblService>();
        builder.Services.AddSingleton<IGrblService>(sp => sp.GetRequiredService<DemoGrblService>());

        // Built-in fixed modules (no assembly scan).
        RegisterModule(builder.Services, new Modules.Fixed.DroModule());

        // External module assemblies are added here as they appear.
        ModuleCatalog.Scan(builder.Services);

        builder.Services.AddSingleton<MainViewModel>();

        return builder.Build();
    }

    private static void RegisterModule(IServiceCollection services, IModule module)
    {
        services.AddSingleton(module);
        services.AddSingleton<IModule>(module);
        module.RegisterServices(services);
    }
}
