using SenderNG.Avalonia.Localization;
using SenderNG.Core.Localization;

namespace SenderNG.Core.Tests;

public class LocTextTests
{
    private static readonly LocalizationService Locale = TestLocalization.Locale;

    [Fact]
    public void Resolve_returns_english_when_no_service_current()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = null;
            Assert.Equal("Hello", LocText.Resolve("app.hello", "Hello"));
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [Fact]
    public void Resolve_returns_localized_when_service_current()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Locale;
            Assert.Equal("GRBL settings", LocText.Resolve("grblSettings.title", "GRBL settings"));
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [Fact]
    public void Resolve_returns_english_when_key_missing()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Locale;
            Assert.Equal("Fallback", LocText.Resolve("module.nope.missing", "Fallback"));
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [Fact]
    public void Format_uses_localized_template_and_arguments()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Locale;
            // en.json: "module.goto.commandFailed": "{0} failed: {1}"
            Assert.Equal("G1 failed: boom", LocText.Format("module.goto.commandFailed", "{0} failed: {1}", "G1", "boom"));
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [Fact]
    public void Format_falls_back_to_english_on_format_error()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Locale;
            // Key missing -> english fallback "{bad" is a malformed format string that
            // throws FormatException, exercising the catch branch.
            Assert.Equal("{bad", LocText.Format("module.nonexistent", "{bad", 5));
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }
}