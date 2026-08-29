namespace SenderNG.Core.Modules;

/// <summary>
/// Optional interface localized modules implement so App Settings (and the locale
/// generator) can enumerate the module's UI strings. Localization stays data-driven:
/// each key maps to its English fallback here and is translated in the Locale
/// {&lt;lang&gt;}.json files; the App Settings tree resolves keys through
/// <see cref="SenderNG.Core.Localization.LocalizationService"/> and views resolve them
/// via the <c>{Loc key}</c> markup extension. Modules that don't implement this keep
/// their literal strings untouched.
/// </summary>
public interface ILocalizedModule : IModule
{
    /// <summary>All user-visible strings this module produces (settings-view texts,
    /// code-generated messages), keyed by localization key with the English fallback.</summary>
    IReadOnlyDictionary<string, string> LocalizationKeys { get; }
}