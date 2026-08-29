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
        // Localization: locale data lives in the UI's OutputDir/Locale (copied on
        // build). Core owns only the mechanism — discover the requested locale and
        // pass its stream into LocalizationService. Locale resolution: explicit
        // --locale arg wins, else the persisted AppSettings.Language, else English.
        var localeDir = Path.Combine(AppContext.BaseDirectory, "Locale");
        var cliLocale = ResolveLocaleCode(args);
        builder.Services.AddSingleton(sp =>
        {
            var code = cliLocale;
            if (string.Equals(code, "en", System.StringComparison.OrdinalIgnoreCase))
            {
                var savedLanguage = sp.GetRequiredService<AppSettings>().Language;
                if (!string.IsNullOrWhiteSpace(savedLanguage))
                    code = savedLanguage;
            }
            return SenderNG.Core.Localization.LocalizationService.LoadFromDirectory(localeDir, code);
        });
        builder.Services.AddSingleton<IComPortService, SerialComPortService>();
        builder.Services.AddSingleton<IFileDialogService, AvaloniaFileDialogService>();
        builder.Services.AddSingleton<IDialogService, AvaloniaDialogService>();
        builder.Services.AddSingleton<Func<object, object>>(sp =>
            vm => vm switch
            {
                CNC.Core.ViewModels.ConnectViewModel v => new Views.ConnectView { DataContext = v },
                CNC.Core.ViewModels.DemoMachineSettingsViewModel v => new Views.DemoMachineSettingsView { DataContext = v },
                CNC.Core.ViewModels.AppSettingsViewModel v => new Modules.Fixed.AppSettingsView { DataContext = v },
                Modules.Fixed.GrblSettingsViewModel v => new Modules.Fixed.GrblSettingsView { DataContext = v },
                _ => throw new NotSupportedException($"No view registered for {vm.GetType().Name}")
            });
        builder.Services.AddSingleton<DemoGrblService>();
        builder.Services.AddSingleton<RealGrblService>();
        // The proxy is THE IGrblService singleton: consumers subscribe once and
        // survive demo/real/network swaps via Inner reassignment. The app starts
        // on the real (serial) service, disconnected — demo mode is only entered
        // explicitly through the connect dialog, never at startup.
        builder.Services.AddSingleton<IGrblService>(sp =>
            new GrblServiceProxy(sp.GetRequiredService<RealGrblService>()));
        // Settings route through the proxy to whichever inner service is active.
        builder.Services.AddSingleton<IGrblSettingsProvider>(sp =>
            (IGrblSettingsProvider)sp.GetRequiredService<IGrblService>());

        // Built-in fixed modules (no assembly scan).
        RegisterModule(builder.Services, new Modules.Fixed.DroModule());
        // The compact selector is WorkParameters; the editable offsets/tool table
        // is a separate flyout, matching ioSender's two distinct widgets.
        RegisterModule(builder.Services, new Modules.Fixed.OffsetsModule());
        RegisterModule(builder.Services, new Modules.Fixed.ProgramModule());
        RegisterModule(builder.Services, new Modules.Fixed.ConsoleModule());
        RegisterModule(builder.Services, new Modules.Fixed.GrblSettingsModule());
        RegisterModule(builder.Services, new Modules.Fixed.AppSettingsModule());
        RegisterModule(builder.Services, new Modules.Fixed.WorkParametersModule());

        // Plugin modules (jog / cutting / coolant / macro / mpos / outline / goto).
        RegisterModule(builder.Services, new Modules.Plugins.GotoModule());
        RegisterModule(builder.Services, new Modules.Plugins.JogModule());
        RegisterModule(builder.Services, new Modules.Plugins.CuttingModule());
        RegisterModule(builder.Services, new Modules.Plugins.CoolantModule());
        RegisterModule(builder.Services, new Modules.Plugins.MacroModule());
        RegisterModule(builder.Services, new Modules.Plugins.ProbingModule());
        RegisterModule(builder.Services, new Modules.Plugins.MposModule());
        RegisterModule(builder.Services, new Modules.Plugins.OutlineModule());

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

        // Expose the localized instance for {Loc key} markup extensions (views
        // can't reach DI). Must be set before any module view is realized.
        SenderNG.Core.Localization.LocalizationService.Current =
            host.Services.GetRequiredService<SenderNG.Core.Localization.LocalizationService>();

        // Settings fields may be read before a module's UI is ever opened.
        foreach (var module in host.Services.GetServices<IModule>())
            if (module is IModuleSettingsProvider provider)
                provider.AttachServices(host.Services);

        return host;
    }

    /// <summary>Resolve the requested locale from args (--locale &lt;code&gt;) or default to English.</summary>
    private static string ResolveLocaleCode(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], "--locale", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return "en";
    }

    private static void RegisterModule(IServiceCollection services, IModule module)
    {
        // NOTE: do not call AddSingleton(module) here - 'module' is statically typed
        // IModule, so generic inference would register IModule a second time.
        services.AddSingleton<IModule>(module);
        module.RegisterServices(services);
    }
}
