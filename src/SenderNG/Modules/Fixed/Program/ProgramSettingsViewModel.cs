/*
 * ProgramSettingsViewModel.cs - Program module settings (App Settings leaf)
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

using CNC.Core.Services;
using CNC.GCode;
using SenderNG.ViewModels;

namespace SenderNG.Modules.Fixed;

/// <summary>A compress comment-mode choice, with a locale-aware display name.
/// INPC so a locale switch can rename the item in place without touching the
/// selection (a rebuilt list would re-push SelectedIndex through the ComboBox).</summary>
public sealed class CommentModeOption : ViewModelBase
{
    public CommentModeOption(GCodeCommentMode mode, string localeKey, string localeFallback)
    {
        Mode = mode;
        LocaleKey = localeKey;
        LocaleFallback = localeFallback;
    }

    public GCodeCommentMode Mode { get; }

    internal string LocaleKey { get; }
    internal string LocaleFallback { get; }

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }
}

/// <summary>
/// Program settings leaf: how the Compress transform treats comment tokens.
/// Profile-scoped (ProgramConfigStore section), persisted on selection and
/// re-synced when the profile changes while the dialog is open. Option names
/// re-resolve when the active locale changes, so the dropdown stays live.
/// </summary>
public class ProgramSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly SettingsService _settings;
    private readonly MachineProfile _profile;
    private readonly SenderNG.Core.Localization.LocalizationService? _loc;
    private int _selectedIndex;

    public ProgramSettingsViewModel(SettingsService settings, SenderNG.Core.Localization.LocalizationService? loc = null)
    {
        _settings = settings;
        _profile = settings.Load();
        _loc = loc;

        // Two meaningful modes: ';' remark lines never tokenize, so they are
        // dropped by ANY token-level transform regardless — the choice is only
        // whether (...) CAM comments survive.
        Options =
        [
            new CommentModeOption(GCodeCommentMode.Keep,
                "module.program.compressComments.keep", "Keep comments"),
            new CommentModeOption(GCodeCommentMode.StripAll,
                "module.program.compressComments.stripAll", "Strip all comments"),
        ];
        Relocalize();

        var saved = ProgramConfigStore.Load(_profile).CompressCommentMode;
        var index = IndexOf(saved);
        _selectedIndex = index >= 0 ? index : 0;

        _settings.Changed += OnSettingsChanged;
        if (loc is not null)
            loc.Changed += OnLocalizationChanged;
    }

    public IReadOnlyList<CommentModeOption> Options { get; }

    /// <summary>Re-resolve the option display names in the active locale (in place).</summary>
    public void Relocalize()
    {
        foreach (var option in Options)
            option.Name = _loc?.Resolve(option.LocaleKey, option.LocaleFallback) ?? option.LocaleFallback;
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (SetField(ref _selectedIndex, value))
                Save();
        }
    }

    private void OnSettingsChanged()
    {
        // Profile may have switched while the dialog was open — re-sync.
        var mode = ProgramConfigStore.Load(_settings.Load()).CompressCommentMode;
        var index = IndexOf(mode);
        _selectedIndex = index >= 0 ? index : 0;
        OnPropertyChanged(nameof(SelectedIndex));
    }

    private bool _disposed;

    private void OnLocalizationChanged(object? sender, EventArgs e) => Relocalize();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        if (_loc is not null)
            _loc.Changed -= OnLocalizationChanged;
    }

    private int IndexOf(GCodeCommentMode mode)
    {
        for (var i = 0; i < Options.Count; i++)
            if (Options[i].Mode == mode)
                return i;
        return -1;
    }

    private void Save()
    {
        var config = ProgramConfigStore.Load(_profile);
        config.CompressCommentMode = Options[SelectedIndex].Mode;
        ProgramConfigStore.Save(_profile, config);
        _settings.Save(_profile);
    }
}