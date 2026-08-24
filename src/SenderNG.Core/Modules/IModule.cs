using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace SenderNG.Core.Modules;

public enum PanelHint { Left, Center, Right, Flyout, Menu, Bottom }

/// <summary>
/// Contract for a pluggable sender module. Core stays UI-agnostic: view/view-model
/// creation returns <see cref="object"/> and the host (Avalonia) casts them.
/// </summary>
public interface IModule
{
    /// <summary>Unique, stable identifier used for settings and routing.</summary>
    string Id { get; }

    /// <summary>Human-readable display name.</summary>
    string Name { get; }

    /// <summary>Sort order in which the module's UI is presented.</summary>
    int Order { get; }

    /// <summary>Preferred panel where the module's UI should be hosted.</summary>
    PanelHint Panel => PanelHint.Center;

    /// <summary>Whether the module is visible by default (overridable via AppSettings.ModulesVisibility).</summary>
    bool IsVisibleByDefault => true;

    /// <summary>Whether the user may hide this module. Fixed layout modules (DRO, Program, Console, Jog) cannot.</summary>
    bool CanHide => true;

    /// <summary>Global hot keys provided by this module.</summary>
    IReadOnlyList<HotKey> HotKeys { get; }

    /// <summary>Registers module-provided services into the shared DI container.</summary>
    void RegisterServices(IServiceCollection services);

    /// <summary>Loads/initializes module settings from the shared application settings.</summary>
    void RegisterSettings(AppSettings settings);

    /// <summary>Creates the module's view model. UI-agnostic (returns object).</summary>
    object CreateViewModel(IServiceProvider serviceProvider);

    /// <summary>Creates the module's view bound to the given view model. UI-agnostic.</summary>
    object CreateView(object viewModel);

    /// <summary>Preferred (width, height) when the module opens as a dialog; null = size to content.</summary>
    (double Width, double Height)? DialogPreferredSize => null;
}
