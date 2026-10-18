/*
 * FluentBridgeTests.cs - pins the bridged Fluent chrome keys for both theme
 * variants (dictionary level and rendered-control level)
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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SenderNG.Themes;
using Xunit;

namespace SenderNG.Tests;

[Collection("LocalizationState")] // merges into Application.Current.Resources and switches RequestedThemeVariant
public class FluentBridgeTests
{
    private static readonly string[] BridgedKeys =
    [
        // TextControl
        "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
        "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused", "TextControlForegroundDisabled",
        "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushDisabled",
        // ComboBox
        "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed", "ComboBoxBackgroundDisabled",
        "ComboBoxForeground", "ComboBoxForegroundDisabled",
        "ComboBoxBorderBrush", "ComboBoxBorderBrushPointerOver", "ComboBoxBorderBrushDisabled",
        "ComboBoxDropDownBackground", "ComboBoxDropDownBorderBrush",
        // Menu / flyout / popup
        "MenuFlyoutPresenterBackground", "MenuFlyoutPresenterBorderBrush", "FlyoutPresenterBackground",
        // Expander (Color-typed values: Fluent's template consumes them via DynamicResource)
        "ExpanderHeaderBackground", "ExpanderHeaderBackgroundPointerOver",
        "ExpanderHeaderForeground", "ExpanderHeaderForegroundPointerOver", "ExpanderContentBackground",
        // ToggleButton
        "ToggleButtonBackground", "ToggleButtonBackgroundPointerOver",
        "ToggleButtonForeground", "ToggleButtonBorderBrush",
        // ToolTip
        "ToolTipBackground", "ToolTipForeground", "ToolTipBorderBrush",
        // ScrollBar
        "ScrollBarThumbFillPointerOver", "ScrollBarThumbFillPressed",
    ];

    // Light values are frozen from FluentTheme's resolved brushes: Color and Opacity
    // are both pinned so that drift (e.g. a lost opacity) cannot pass the existence test.
    private static readonly (string Key, string Color, double Opacity)[] FrozenLightBrushValues =
    [
        // TextControl
        ("TextControlBackground", "#66ffffff", 1),
        ("TextControlBackgroundPointerOver", "#99ffffff", 1),
        ("TextControlBackgroundFocused", "#ffffffff", 1),
        ("TextControlBackgroundDisabled", "#33000000", 1),
        ("TextControlForeground", "#ff000000", 1),
        ("TextControlForegroundPointerOver", "#ff000000", 1),
        ("TextControlForegroundFocused", "#ff000000", 1),
        ("TextControlForegroundDisabled", "#ff7a7a7a", 1),
        ("TextControlBorderBrush", "#99000000", 1),
        ("TextControlBorderBrushPointerOver", "#cc000000", 1),
        ("TextControlBorderBrushDisabled", "#33000000", 1),
        // ComboBox
        ("ComboBoxBackground", "#66ffffff", 1),
        ("ComboBoxBackgroundPointerOver", "#99ffffff", 1),
        ("ComboBoxBackgroundPressed", "#33000000", 1),
        ("ComboBoxBackgroundDisabled", "#33000000", 1),
        ("ComboBoxForeground", "#ff000000", 1),
        ("ComboBoxForegroundDisabled", "#66000000", 1),
        ("ComboBoxBorderBrush", "#99000000", 1),
        ("ComboBoxBorderBrushPointerOver", "#cc000000", 1),
        ("ComboBoxBorderBrushDisabled", "#33000000", 1),
        ("ComboBoxDropDownBackground", "#fff2f2f2", 1),
        ("ComboBoxDropDownBorderBrush", "#ff000000", 0.14),
        // Menu / flyout / popup
        ("MenuFlyoutPresenterBackground", "#fff2f2f2", 1),
        ("MenuFlyoutPresenterBorderBrush", "#ff000000", 0.14),
        ("FlyoutPresenterBackground", "#fff2f2f2", 1),
        // ToggleButton
        ("ToggleButtonBackground", "#33000000", 1),
        ("ToggleButtonBackgroundPointerOver", "#ff000000", 0.1),
        ("ToggleButtonForeground", "#ff000000", 1),
        ("ToggleButtonBorderBrush", "#00ffffff", 1),
        // ToolTip
        ("ToolTipBackground", "#fff2f2f2", 1),
        ("ToolTipForeground", "#ff000000", 1),
        ("ToolTipBorderBrush", "#ff000000", 0.14),
        // ScrollBar
        ("ScrollBarThumbFillPointerOver", "#66000000", 1),
        ("ScrollBarThumbFillPressed", "#99000000", 1),
    ];

    private static readonly (string Key, string Color)[] FrozenLightColorValues =
    [
        ("ExpanderHeaderBackground", "#ccffffff"),
        ("ExpanderHeaderBackgroundPointerOver", "#ccffffff"),
        ("ExpanderHeaderForeground", "#ff000000"),
        ("ExpanderHeaderForegroundPointerOver", "#ff000000"),
        ("ExpanderContentBackground", "#fff2f2f2"),
    ];

    // Accent-derived states — checked/selected/focused — are deliberately not
    // bridged: they follow the system accent in both themes.
    private static readonly string[] UnbridgedAccentKeys =
    [
        "TextControlBorderBrushFocused",
        "TextControlSelectionHighlightColor",
        "ToggleButtonBackgroundChecked",
        "ToggleButtonBackgroundCheckedPointerOver",
        "ToggleButtonForegroundChecked",
        "ToggleButtonBorderBrushChecked",
    ];

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Accent_derived_states_are_not_bridged(string variantName)
    {
        var variant = ThemeVariants.ToVariant(variantName);
        var theme = ThemeTestHost.LoadSenderTheme();
        var dictionary = ThemeTestHost.DictionaryFor(theme, variant);
        Assert.All(UnbridgedAccentKeys, key =>
            Assert.False(dictionary.ContainsKey(key),
                $"{key} must follow the system accent and must not be defined in the {variantName} dictionary"));
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Every_bridged_key_resolves_from_the_theme_in_both_variants(string variantName)
    {
        var variant = ThemeVariants.ToVariant(variantName);
        var theme = ThemeTestHost.LoadSenderTheme();
        Assert.All(BridgedKeys, key =>
        {
            var value = ThemeTestHost.DictionaryFor(theme, variant)[key];
            Assert.NotNull(value);
            Assert.True(value is ISolidColorBrush or Avalonia.Media.Color,
                $"{key} must resolve to a brush or a color, was {value.GetType().Name}");
        });
    }

    [AvaloniaFact]
    public void Frozen_light_values_keep_the_dumped_color_and_opacity()
    {
        var theme = ThemeTestHost.LoadSenderTheme();
        Assert.All(FrozenLightBrushValues, entry =>
        {
            var brush = ThemeTestHost.Brush(theme, ThemeVariant.Light, entry.Key);
            Assert.Equal(Avalonia.Media.Color.Parse(entry.Color), brush.Color);
            Assert.Equal(entry.Opacity, brush.Opacity);
        });
        Assert.All(FrozenLightColorValues, entry =>
            Assert.Equal(Avalonia.Media.Color.Parse(entry.Color),
                Assert.IsType<Avalonia.Media.Color>(ThemeTestHost.DictionaryFor(theme, ThemeVariant.Light)[entry.Key])));
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task Input_controls_take_their_chrome_from_the_bridge(string variantName)
    {
        var variant = ThemeVariants.ToVariant(variantName);
        ThemeTestHost.EnsureMerged();
        var previous = Application.Current!.RequestedThemeVariant;
        Application.Current.RequestedThemeVariant = variant;
        var theme = ThemeTestHost.LoadSenderTheme();
        var host = new Window { Width = 500, Height = 400, Content = new StackPanel
        {
            Children =
            {
                new TextBox(),
                new ComboBox { ItemsSource = new[] { "a", "b" }, SelectedIndex = 0 },
                new ToggleButton { Content = "t" },
            },
        } };
        try
        {
            host.Show();
            for (var i = 0; i < 3; i++)
                await Pump();

            var textBox = host.GetVisualDescendants().OfType<TextBox>().First();
            Assert.Equal(ThemeTestHost.ColorOf(theme, variant, "TextControlBackground"),
                Color(textBox.Background));

            var combo = host.GetVisualDescendants().OfType<ComboBox>().First();
            Assert.Equal(ThemeTestHost.ColorOf(theme, variant, "ComboBoxBackground"),
                Color(combo.Background));

            // No ListBox assertion: Avalonia 12 Fluent has no ListBoxBackground resource
            // (ListBox takes the internal SystemControlBackgroundChromeMediumLowBrush),
            // so there is no bridge key to pin the control against.

            var toggle = host.GetVisualDescendants().OfType<ToggleButton>().First();
            Assert.Equal(ThemeTestHost.ColorOf(theme, variant, "ToggleButtonBackground"),
                Color(toggle.Background));

            host.Close();
        }
        finally
        {
            Application.Current.RequestedThemeVariant = previous;
        }
    }

    private static Color Color(IBrush? brush) =>
        ((ISolidColorBrush)brush!).Color;

    private static async Task Pump()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
    }
}
