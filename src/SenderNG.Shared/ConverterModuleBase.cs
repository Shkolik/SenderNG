/*
 * ConverterModuleBase.cs - shared plumbing for file-handler converter modules
 */

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

using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;

namespace SenderNG.Modules.Shared;

/// <summary>
/// Base class for file-format → g-code converter modules (Excellon, HPGL, …).
/// Wires the full plugin contract: <see cref="IModule"/> (no flyout UI —
/// invisible by default), <see cref="IModuleSettingsProvider"/> (per-profile
/// parameters + ShowDialog leaf in App Settings), <see cref="ILocalizedModule"/>
/// (own locale catalog), and <see cref="IGCodeFileHandler"/> (the Open-flow
/// dispatch the main app sees). Subclasses supply the identity, locale keys and
/// the actual <see cref="Convert"/> logic.
/// </summary>
public abstract class ConverterModuleBase : IModule, IModuleSettingsProvider, ILocalizedModule, IGCodeFileHandler
{
    private IServiceProvider? _services;
    private bool _dialogViewRegistered;

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract int Order { get; }

    /// <summary>No UI at all — these are open-flow plugins, not panels.</summary>
    public PanelHint Panel => PanelHint.None;
    public bool IsVisibleByDefault => false;
    public bool IsEnabledByDefault => true;
    public bool CanHide => false;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    /// <summary>All user-visible strings this module produces (settings leaf,
    /// dialog, filter label), keyed by localization key with English fallback.
    /// The host copies them into the module's own <c>Locale/*.json</c> catalogs.</summary>
    public abstract IReadOnlyDictionary<string, string> LocalizationKeys { get; }

    // ---- IModuleSettingsProvider: parameters + ShowDialog live in the profile ----

    public SettingsScope Scope => SettingsScope.Profile;

    public void AttachServices(IServiceProvider services) => _services = services;

    public object? CreateSettingsView() =>
        _services is null
            ? null
            : new ConverterSettingsView
            {
                DataContext = new ConverterSettingsViewModel(_services, Id, Name, ShowCutMode),
            };

    // ---- IModule: no flyout UI ----

    public void RegisterServices(IServiceCollection services) { }

    public object CreateViewModel(IServiceProvider serviceProvider) => new object();

    public object CreateView(object viewModel) => new Avalonia.Controls.UserControl();

    // ---- IGCodeFileHandler ----

    public abstract string FileType { get; }

    public abstract string FileExtensions { get; }

    public virtual string? FileTypeKey => $"module.{Id}.fileType";

    /// <summary>Whether the parameters dialog shows the cut/laser selector
    /// (HPGL yes — laser preamble vs spindle cut; Excellon no).</summary>
    public virtual bool ShowCutMode => false;

    /// <summary>
    /// The open flow: load the profile's stored parameters; show the parameters
    /// dialog when enabled (<see cref="ConverterSettings.ShowDialog"/>) or when
    /// nothing was ever stored; convert with the accepted (or stored) values.
    /// The dialog is the module's own view, registered into the app's
    /// <see cref="IViewFactory"/> on first use. Returns null on cancel/failure.
    /// </summary>
    public async Task<string?> ConvertAsync(IServiceProvider services, string sourceName, string sourceText)
    {
        var settingsService = services.GetRequiredService<SettingsService>();
        var profile = settingsService.Load();
        var stored = ConverterSettingsStore.Load(profile, Id);

        if (stored.ShowDialog || !stored.HasValues)
        {
            EnsureDialogViewRegistered(services);

            var dialog = services.GetRequiredService<IDialogService>();
            var vm = new ConverterParametersViewModel(
                stored,
                ShowCutMode,
                services.GetService<LocalizationService>());
            var title = Localized(services, "module.converters.parametersTitle", "Converter parameters");
            var accepted = await dialog.ShowDialogAsync(title, vm);
            if (accepted != true)
                return null; // cancelled — nothing loaded

            stored = vm.ToSettings();
            ConverterSettingsStore.Save(profile, Id, stored);
            settingsService.Save(profile);
        }

        try
        {
            return Convert(sourceText, stored.ToParameters(sourceName));
        }
        catch (Exception ex)
        {
            // Core-owned error surfacing (docs/plugin-development.md §3.4): report
            // through the per-module IErrorReporter so the host shows the common
            // "[module name] : [text]" dialog — never throw into the host.
            services.GetKeyedService<IErrorReporter>(Id)?.Error(ex.Message);
            return null;
        }
    }

    /// <summary>The actual conversion logic (pure text-in/text-out). Returns null
    /// when the input cannot be converted; exceptions are caught by the base
    /// class and reported through the module's <see cref="IErrorReporter"/>.</summary>
    protected abstract string? Convert(string sourceText, ConverterParameters parameters);

    /// <summary>Resolve a module key through the locale, English fallback.</summary>
    protected static string Localized(IServiceProvider services, string key, string fallback) =>
        services.GetService<LocalizationService>()?.Resolve(key, fallback) ?? fallback;

    private void EnsureDialogViewRegistered(IServiceProvider services)
    {
        if (_dialogViewRegistered)
            return;
        services.GetRequiredService<IViewFactory>()
            .Register<ConverterParametersViewModel, ConverterParametersView>();
        _dialogViewRegistered = true;
    }
}