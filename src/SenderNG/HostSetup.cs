
/*

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

· Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Services;
using SenderNG.ViewModels;
using SenderNG.Core.Modules;

namespace SenderNG;

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
        // Shared active-profile graph: one instance for every consumer (VMs, services).
        builder.Services.AddSingleton(sp => sp.GetRequiredService<SettingsService>().Load());
        // Localization: locale data lives in the UI's OutputDir/Locale (copied on
        // build). Core owns only the mechanism — discover the requested locale and
        // pass its stream into LocalizationService. Locale resolution: explicit
        // --locale arg wins, else the persisted global Language, else English.
        var localeDir = Path.Combine(AppContext.BaseDirectory, "Locale");
        var cliLocale = ResolveLocaleCode(args);
        builder.Services.AddSingleton(sp =>
        {
            var code = cliLocale;
            if (string.Equals(code, "en", System.StringComparison.OrdinalIgnoreCase))
            {
                var savedLanguage = sp.GetRequiredService<SettingsService>().Global.Language;
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
                CNC.Core.ViewModels.RotateProgramViewModel v => new Views.RotateProgramView { DataContext = v },
                CNC.Core.ViewModels.ArcsToLinesProgramViewModel v => new Views.ArcsToLinesProgramView { DataContext = v },
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

        var localization = host.Services
            .GetRequiredService<SenderNG.Core.Localization.LocalizationService>();
        // 3rd-party modules may serve their own locale data under
        // OutputDir/Modules/<id>/Locale/*.json; register them so their namespaced
        // keys (module.<id>.*) resolve through the active locale. Base catalogs win.
        foreach (var module in host.Services.GetServices<IModule>())
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Modules", module.Id, "Locale");
            if (Directory.Exists(dir))
                localization.RegisterModuleLocales(dir);
        }

        // Expose the localized instance for {Loc key} markup extensions (views
        // can't reach DI). Must be set before any module view is realized.
        SenderNG.Core.Localization.LocalizationService.Current = localization;

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
