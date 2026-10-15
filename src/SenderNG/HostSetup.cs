
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
using SenderNG.Localization;
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

        // SettingsService reports any settings file it could not load; route that
        // to the status bar. Registered as a factory because StatusSink is
        // registered further down — resolution is lazy, so order is fine.
        // `services: sp` is load-bearing too: the parameterless registration this
        // replaced let MS DI inject the provider into that parameter on its own,
        // and DeleteProfile resolves IProfileCompanionSuffixes through it to strip
        // a deleted profile's per-module companion files. A factory that omits it
        // leaves it null and silently stops that cleanup.
        builder.Services.AddSingleton(sp => new SettingsService(
            services: sp,
            onFallback: f => sp.GetRequiredService<StatusSink>().Set(DescribeSettingsFallback(f))));
        builder.Services.AddSingleton<SenderNG.Core.Services.HotkeyService>();
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
        // Status-bar sink + per-module error reporters: ModuleCatalog binds one
        // IErrorReporter per discovered module (keyed by module id) via this
        // factory; Status(text) reaches the status bar through the sink, which
        // MainViewModel wires at construction.
        builder.Services.AddSingleton<StatusSink>();
        builder.Services.AddSingleton<IErrorReporterFactory, AvaloniaErrorReporterFactory>();
        // View factory for dialog hosting: the app seeds its own VM→view pairs;
        // 3rd-party modules register theirs at runtime (idempotent), so a plugin
        // can show a dialog without the app knowing its view types.
        var viewFactory = new AvaloniaViewFactory();
        viewFactory.Register<CNC.Core.ViewModels.ConnectViewModel, Views.ConnectView>();
        viewFactory.Register<CNC.Core.ViewModels.AppSettingsViewModel, Modules.Fixed.AppSettingsView>();
        viewFactory.Register<CNC.Core.ViewModels.RotateProgramViewModel, Views.RotateProgramView>();
        viewFactory.Register<CNC.Core.ViewModels.ArcsToLinesProgramViewModel, Views.ArcsToLinesProgramView>();
        viewFactory.Register<CNC.Core.ViewModels.WrapProgramViewModel, Views.WrapProgramView>();
        viewFactory.Register<Modules.Fixed.GrblSettingsViewModel, Modules.Fixed.GrblSettingsView>();
        viewFactory.Register<Modules.Plugins.StepperCalibrationViewModel, Modules.Plugins.StepperCalibrationView>();
        builder.Services.AddSingleton<IViewFactory>(viewFactory);
        builder.Services.AddSingleton<DemoGrblService>();
        builder.Services.AddSingleton<RealGrblService>();
        // Per-connection controller identity/capability state: the one instance
        // every consumer shares. RealGrblService writes it per connect; the static
        // GrblInfo facade is bound to it below so the parser/expressions see the
        // board. Tests never touch it (they construct isolated instances).
        builder.Services.AddSingleton<CNC.Core.GrblInfoState>();
        // The proxy is THE IGrblService singleton: consumers subscribe once and
        // survive demo/real/network swaps via Inner reassignment. The app starts
        // on the real (serial) service, disconnected — demo mode is only entered
        // explicitly through the connect dialog, never at startup.
        builder.Services.AddSingleton<IGrblService>(sp =>
            new GrblServiceProxy(sp.GetRequiredService<RealGrblService>()));
        // Settings route through the proxy to whichever inner service is active.
        builder.Services.AddSingleton<IGrblSettingsProvider>(sp =>
            (IGrblSettingsProvider)sp.GetRequiredService<IGrblService>());
        // The one kinematics seam: modules resolve this instead of each re-resolving
        // the machine-type preset at their own trigger point. It merges the active
        // profile with the live controller (letters/count from [AXS:]), so it must be
        // a singleton — it subscribes to connect and profile-switch once and every
        // consumer reads the same resolved object.
        builder.Services.AddSingleton<IMachineKinematicsProvider>(sp =>
        {
            var provider = new MachineKinematicsProvider(
                sp.GetRequiredService<SettingsService>(),
                sp.GetRequiredService<IGrblService>());

            // The parser reads its CAM aliases from the same ambient state it reads
            // the board's letters from, so keep that in step with the resolved
            // kinematics: a program whose axis naming differs from the board's (the
            // "my CAM writes U" case) resolves through the profile's
            // MachineAxis.Alias entries. Current merges stored profile state with
            // live controller state, so this must follow every change to it.
            void PushAliases() =>
                CNC.Core.GrblInfo.Current.AxisAliases = provider.Current.AliasMap;

            provider.Changed += (_, _) => PushAliases();
            PushAliases();
            return provider;
        });

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

        // Plugin modules registered in code (jog / cutting / coolant / macro / mpos /
        // outline / goto / stepper calibration) + whatever ModuleCatalog.ScanDirectory
        // discovers in OutputDir/Modules.
        RegisterModule(builder.Services, new Modules.Plugins.GotoModule());
        RegisterModule(builder.Services, new Modules.Plugins.JogModule());
        RegisterModule(builder.Services, new Modules.Plugins.CuttingModule());
        RegisterModule(builder.Services, new Modules.Plugins.CoolantModule());
        RegisterModule(builder.Services, new Modules.Plugins.MacroModule());
        RegisterModule(builder.Services, new Modules.Plugins.MposModule());
        RegisterModule(builder.Services, new Modules.Plugins.OutlineModule());
        RegisterModule(builder.Services, new Modules.Plugins.StepperCalibrationModule());

        // Plugin modules (converters, …): every *.dll in OutputDir/Modules
        // is scanned at startup — drop a DLL in and it loads, remove it and the
        // app has no idea it ever existed. SenderNG.csproj stages the built-in
        // plugin assemblies there; real third-party plugins are dropped in.
        ModuleCatalog.ScanDirectory(builder.Services, Path.Combine(AppContext.BaseDirectory, "Modules"));

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
                viewFactory: sp.GetRequiredService<Func<IModule, object>>(),
                statusSink: sp.GetRequiredService<StatusSink>(),
                info: sp.GetRequiredService<CNC.Core.GrblInfoState>()));

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

        // Bind the static GrblInfo facade to the DI singleton so legacy readers
        // (gcode parser, expressions) see the board the connect just described.
        // Must happen before any VM/factory is constructed.
        CNC.Core.GrblInfo.Current = host.Services.GetRequiredService<CNC.Core.GrblInfoState>();

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

    /// <summary>
    /// Composes the one user-facing string for a settings file that could not be
    /// loaded. Lives here, not in Core, because the app.msg.* resources are the
    /// host's. One key per (reason x Quarantined) cell: the two axes
    /// are independent facts, and collapsing either one makes the message untrue —
    /// an unreadable file is not a damaged one, and a file that was not moved is
    /// not preserved.
    /// </summary>
    private static string DescribeSettingsFallback(SettingsFallback fallback)
    {
        var name = Path.GetFileName(fallback.Path);
        var moved = fallback.Quarantined;
        var key = (fallback.Reason, moved) switch
        {
            (SettingsFallbackReason.Corrupt, true) => "app.msg.settingsCorruptMoved",
            (SettingsFallbackReason.Corrupt, false) => "app.msg.settingsCorruptKept",
            (SettingsFallbackReason.Unreadable, true) => "app.msg.settingsUnreadableMoved",
            (SettingsFallbackReason.Unreadable, false) => "app.msg.settingsUnreadableKept",
            // The explicit discard keeps this switch exhaustive at compile time.
            // Throwing is deliberate over a neutral catch-all: a reason nobody wrote
            // a sentence for must not reach the user as vague text. Unreachable today
            // because every SettingsFallback is built at ProfileSettingsStore.cs:197,
            // :201, :297 and :301, each with reason taken from the same expression,
            // `ex is JsonException ? Corrupt : Unreadable` — so only those two members
            // can arrive. If a third reason is ever added this arm is where the
            // developer finds out a sentence is still missing.
            _ => throw new ArgumentOutOfRangeException(
                nameof(fallback), fallback.Reason, "No settings-fallback message for this reason"),
        };
        return LocText.Format(key, $"Settings file {name} could not be used; defaults were loaded instead", name);
    }

    private static void RegisterModule(IServiceCollection services, IModule module)
    {
        // NOTE: do not call AddSingleton(module) here - 'module' is statically typed
        // IModule, so generic inference would register IModule a second time.
        services.AddSingleton<IModule>(module);
        module.RegisterServices(services);
    }
}
