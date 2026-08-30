using SenderNG.Core.Localization;

namespace SenderNG.Core.Modules;

/// <summary>
/// Resolves a module's display name through <see cref="LocalizationService"/>. For a
/// 3rd-party module the key <c>module.&lt;id&gt;.name</c> exists only in the module's OWN
/// registered locale catalog, so it resolves plugin-first; in-app modules resolve from
/// the base catalog. Falls back to <see cref="IModule.Name"/> when no catalog has the key.
/// </summary>
public static class ModuleNames
{
    public static string Localized(IModule module, LocalizationService loc) =>
        loc.Resolve($"module.{module.Id}.name", module.Name);
}