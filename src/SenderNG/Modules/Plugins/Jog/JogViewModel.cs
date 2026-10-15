
/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

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

using System.Collections.ObjectModel;
using System.Globalization;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Localization;
using SenderNG.ViewModels;

namespace SenderNG.Modules.Plugins;

public sealed class LinearAxisRow
{
    public LinearAxisRow(string letter) => Letter = letter;
    public string Letter { get; }
    public string PlusTag => Letter + "+";
    public string MinusTag => Letter + "-";
}

public class JogViewModel : ViewModelBase
{
    private static readonly string[] Letters = "XYZABCUVW".Select(c => c.ToString()).ToArray();

    private readonly IGrblService _grbl;
    private readonly SettingsService _settingsService;
    private readonly CNC.Core.GrblInfoState _info;
    private MachineProfile _profile;
    private int _numAxes = 2;
    private string _letters = string.Empty;
    // The resolved preset ⊕ profile ⊕ board layout the cross and the linear rows
    // are derived from. Re-resolved whenever the profile, the letters or the
    // reported axis count change (see ResolveKinematics).
    private MachineKinematics _kinematics = null!;
    private bool _suppressReload;
    private SenderNG.Core.Services.HotkeyService? _subscribedHotkeys;

    public JogViewModel(IGrblService grbl, SettingsService settingsService, CNC.Core.GrblInfoState? info = null)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _info = info ?? CNC.Core.GrblInfo.Current;
        _profile = _settingsService.Load();

        Config = JogConfigStore.Load(_profile);
        Config.PropertyChanged += OnConfigPropertyChanged;
        _letters = _info.AxisLetters;
        ResolveKinematics();
        RefreshGeometry();
        RefreshLinearAxes();

        JogCommand = new DelegateCommand(p => _ = JogAsync(p as string, Together));
        // Jog cancel (realtime 0x85) is the firmware's dedicated stop-jog command on
        // classic grbl 1.1, the mega5x 1.2i fork and grblHAL: it decelerates, flushes
        // remaining jog blocks and returns to IDLE. A feed hold ('!') would leave the
        // machine in HOLD needing a cycle start — wrong for the jog panel.
        StopCommand = new DelegateCommand(_ =>
        {
            if (_continuousStarting)
                _continuousCancelRequested = true;
            CancelJog();
        });

        _grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));
        _grbl.ConnectionChanged += OnConnectionChanged;
        _settingsService.Changed += OnSettingsChanged;
        // The plane-2 hotkey labels carry the live letter, so a language switch or a
        // hotkey remap must re-read them (the tooltip also carries the gesture).
        if (SenderNG.Core.Localization.LocalizationService.Current is { } localization)
            localization.Changed += OnLocaleChanged;
        AppHotkeys.Changed += OnAppHotkeysChanged;
        SubscribeToHotkeys(AppHotkeys.Current);
    }

    public JogConfig Config { get; private set; }

    // Single persistence point: ANY config edit (settings card, widget selectors)
    // flows through here. The settings card binds Config directly.
    // "Distances"/"Speeds" array-instance swaps come from Load/Normalize itself
    // (not user edits) and must not re-persist.
    private bool _saving;
    private void OnConfigPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_saving || e.PropertyName is nameof(JogConfig.Distances) or nameof(JogConfig.Speeds))
            return;

        _saving = true;
        try
        {
            Save();
        }
        finally
        {
            _saving = false;
        }

        switch (e.PropertyName)
        {
            case nameof(JogConfig.DistanceIndex):
                RaiseDistanceFlags();
                OnPropertyChanged(nameof(SelectedDistanceIndex));
                OnPropertyChanged(nameof(SelectedDistance));
                break;
            case nameof(JogConfig.SpeedIndex):
                RaiseSpeedFlags();
                OnPropertyChanged(nameof(SelectedSpeedIndex));
                OnPropertyChanged(nameof(SelectedSpeed));
                break;
            case nameof(JogConfig.Diagonals):
                OnPropertyChanged(nameof(ShowDiagonals));
                break;
        }
    }

    public ObservableCollection<LinearAxisRow> LinearAxisLetters { get; } = new();

    // ---- Machine geometry ----
    // The layout comes from the resolved MachineKinematics (preset ⊕ profile ⊕
    // board), never from a slot position: a plane's slot 0/1 are ROLES, and the
    // axis index behind each role is what the profile stores. A plane bound to
    // slots [3, 2] — the bug-report foam firmware's A-horizontal/Z-vertical tower —
    // therefore labels and drives A/Z, where a positional cross drove Z/A.

    /// <summary>Re-resolve the kinematics from what jog tracks live: the active
    /// profile, the controller's letters and the polled axis count. Mirrors the
    /// resolver the Core provider runs; jog keeps its own state-driven inputs so a
    /// state emission (the demo's SetNumAxes path) re-lays the cross immediately.</summary>
    private void ResolveKinematics() =>
        _kinematics = MachineKinematicsResolver.Resolve(_profile, _letters, _numAxes);

    /// <summary>Multi-plane machine (foam): the resolver's plane count.</summary>
    public bool IsMultiPlane => _kinematics.IsMultiPlane;

    /// <summary>Plane 2 (second carriage) is drivable: both of its in-plane slots
    /// are reported. Fit is by presence of each slot's index (never by a running
    /// count), so a plane whose indices are non-monotonic — [3, 2] — needs the
    /// highest one. Under-axed boards hide the plane and show the hint.</summary>
    public bool Plane2Visible => PlaneVisible(1);

    /// <summary>Shown when a multi-plane machine meets fewer axes than its planes need.</summary>
    public bool Plane2HiddenHintVisible => IsMultiPlane && !Plane2Visible;

    /// <summary>The Together padlock only means something when two visible planes
    /// share their in-plane direction pair — that parallel pair is the wire the
    /// coupling mirrors, so a non-parallel second plane must not offer it.</summary>
    public bool TogetherVisible
    {
        get
        {
            if (!PlaneVisible(0) || _kinematics.Planes.Count < 2)
                return false;

            var reference = InPlanePair(0);
            var coupled = 0;
            for (var plane = 1; plane < _kinematics.Planes.Count; plane++)
            {
                if (!PlaneVisible(plane))
                    continue;
                if (!InPlanePair(plane).SequenceEqual(reference))
                    return false;
                coupled++;
            }
            return coupled > 0;
        }
    }

    /// <summary>A plane renders as a cross iff its horizontal and vertical slots
    /// both exist and the controller reports both axes.</summary>
    private bool PlaneVisible(int plane)
    {
        var horizontal = _kinematics.IndexAt(plane, 0);
        var vertical = _kinematics.IndexAt(plane, 1);
        return _kinematics.IsPresent(horizontal) && _kinematics.IsPresent(vertical);
    }

    /// <summary>A plane's in-plane direction pair (horizontal, vertical) — the pair
    /// two parallel planes of a coupled machine share.</summary>
    private AxisDirection[] InPlanePair(int plane) =>
        _kinematics.Planes[plane].Axes.Take(2).Select(a => a.Direction).ToArray();

    // Plane letters: the axis index in the plane's slot, through the controller's
    // [AXS:] letters (the conventional order when it reported none). A plane that
    // does not exist has no slots, so the two properties that would label its
    // hidden cross fall back to the extra-axis rows — which is what those controls
    // have always driven on a single-plane machine (PageUp/PageDown = the mill's
    // depth, Home/End = the next axis).
    public string P1H => PlaneLetter(0, 0);
    public string P1V => PlaneLetter(0, 1);
    public string P2H => PlaneLetter(1, 0);
    public string P2V => PlaneLetter(1, 1);

    private string PlaneLetter(int plane, int slot)
    {
        var index = _kinematics.IndexAt(plane, slot);
        if (index >= 0)
            return _kinematics.LetterFor(index).ToString();

        var extras = ExtraAxes;
        return slot < extras.Count ? _kinematics.LetterFor(extras[slot]).ToString() : "?";
    }

    public string P1HPlus => P1H + "+";
    public string P1HMinus => P1H + "-";
    public string P1VPlus => P1V + "+";
    public string P1VMinus => P1V + "-";
    public string P2HPlus => P2H + "+";
    public string P2HMinus => P2H + "-";
    public string P2VPlus => P2V + "+";
    public string P2VMinus => P2V + "-";

    // Plane-1 cross tags: part 1 = plane-1 words, part 2 (after '|') = the
    // coupled plane-2 words the Together padlock adds ("X+|Z+" = both
    // horizontals, "Y+|A+" = both verticals — the mirrored-gantry semantics).
    // No coupled second plane (mill, clamped foam, non-parallel tower) -> single part.
    public string UpLeftTag => CrossTag($"{P1H}-,{P1V}+", $"{P2H}-,{P2V}+");
    public string UpTag => CrossTag($"{P1V}+", $"{P2V}+");
    public string UpRightTag => CrossTag($"{P1H}+,{P1V}+", $"{P2H}+,{P2V}+");
    public string LeftTag => CrossTag($"{P1H}-", $"{P2H}-");
    public string RightTag => CrossTag($"{P1H}+", $"{P2H}+");
    public string DownLeftTag => CrossTag($"{P1H}-,{P1V}-", $"{P2H}-,{P2V}-");
    public string DownTag => CrossTag($"{P1V}-", $"{P2V}-");
    public string DownRightTag => CrossTag($"{P1H}+,{P1V}-", $"{P2H}+,{P2V}-");

    // Plane-2 cross tags: single-plane moves (that cross jogs tower 2 alone).
    public string SecondUpLeftTag => $"{P2H}-,{P2V}+";
    public string SecondUpTag => $"{P2V}+";
    public string SecondUpRightTag => $"{P2H}+,{P2V}+";
    public string SecondLeftTag => $"{P2H}-";
    public string SecondRightTag => $"{P2H}+";
    public string SecondDownLeftTag => $"{P2H}-,{P2V}-";
    public string SecondDownTag => $"{P2V}-";
    public string SecondDownRightTag => $"{P2H}+,{P2V}-";

    // The coupled '|' part is the Together payload, so it exists only where the
    // padlock does: two visible planes sharing their direction pair (a mill, a
    // clamped foam machine and a non-parallel second plane all tag single-part).
    private string CrossTag(string plane1, string plane2) => TogetherVisible ? $"{plane1}|{plane2}" : plane1;

    // ---- Plane-2 direction hotkey labels ----
    // The action IDs are frozen (saved keymaps key on them), but the LETTERS are
    // not machine-invariant: with plane 2's slots bound to [3, 2] the "horizontal"
    // key jogs A and the "vertical" one Z, on conventional foam [2, 3] they jog Z/A,
    // and on a mill the same two keys jog the depth and the next reported axis. No
    // static catalog value can be right for every machine, so both the App Settings
    // row and the pad tooltip format the letter the key ACTUALLY jogs right now.
    private static readonly Dictionary<string, string> Plane2HotkeyKeys = new(StringComparer.Ordinal)
    {
        ["jog.z.plus"] = "module.jog.hotkey.jog.z.plus",
        ["jog.z.minus"] = "module.jog.hotkey.jog.z.minus",
        ["jog.a.plus"] = "module.jog.hotkey.jog.a.plus",
        ["jog.a.minus"] = "module.jog.hotkey.jog.a.minus",
    };

    /// <summary>Live, localized label for a plane-2 direction action on this machine
    /// ("Jog A+"); the id itself for an action this VM does not own.</summary>
    public string HotkeyLabel(string actionId)
        => Plane2HotkeyKeys.TryGetValue(actionId, out var key)
            ? LocText.Format(key, Plane2English(actionId), Plane2Letter(actionId))
            : actionId;

    /// <summary>The same label plus the gesture currently bound to the action
    /// ("Jog A+ [PageUp]") — the pad tooltip's text.</summary>
    public string HotkeyTip(string actionId)
        => HotkeyTipSource.Compose(HotkeyLabel(actionId), AppHotkeys.Current?.GestureFor(actionId));

    public string HotkeyTipPlane2Right => HotkeyTip("jog.z.plus");
    public string HotkeyTipPlane2Left => HotkeyTip("jog.z.minus");
    public string HotkeyTipPlane2Up => HotkeyTip("jog.a.plus");
    public string HotkeyTipPlane2Down => HotkeyTip("jog.a.minus");

    private static string Plane2English(string actionId)
        => actionId.EndsWith(".minus", StringComparison.Ordinal) ? "Jog {0}-" : "Jog {0}+";

    // The horizontal keys drive the plane's slot 0, the vertical keys slot 1 — the
    // same P2H/P2V the on-screen cross shows and the tags it sends, so the label and
    // the motion can never disagree.
    private string Plane2Letter(string actionId)
        => actionId.Contains(".z.", StringComparison.Ordinal) ? P2H : P2V;

    private void RefreshHotkeyTips()
    {
        OnPropertyChanged(nameof(HotkeyTipPlane2Right));
        OnPropertyChanged(nameof(HotkeyTipPlane2Left));
        OnPropertyChanged(nameof(HotkeyTipPlane2Up));
        OnPropertyChanged(nameof(HotkeyTipPlane2Down));
    }

    private void OnLocaleChanged(object? sender, EventArgs e) => UiThread.Post(RefreshHotkeyTips);
    private void OnHotkeysChanged(object? sender, EventArgs e) => UiThread.Post(RefreshHotkeyTips);

    /// <summary>Owns the single <see cref="SenderNG.Core.Services.HotkeyService"/>
    /// subscription, moving it to <paramref name="service"/> and releasing the previous
    /// one (the reference check keeps it idempotent).</summary>
    private void SubscribeToHotkeys(SenderNG.Core.Services.HotkeyService? service)
    {
        if (ReferenceEquals(_subscribedHotkeys, service))
            return;
        if (_subscribedHotkeys is not null)
            _subscribedHotkeys.Changed -= OnHotkeysChanged;
        _subscribedHotkeys = service;
        if (service is not null)
            service.Changed += OnHotkeysChanged;
    }

    private void OnAppHotkeysChanged(object? sender, EventArgs e)
    {
        SubscribeToHotkeys(AppHotkeys.Current);
        RefreshHotkeyTips();
    }

    private void RefreshGeometry()
    {
        OnPropertyChanged(nameof(IsMultiPlane));
        OnPropertyChanged(nameof(Plane2Visible));
        OnPropertyChanged(nameof(Plane2HiddenHintVisible));
        OnPropertyChanged(nameof(TogetherVisible));
        foreach (var property in new[]
                 {
                     nameof(P1H), nameof(P1V), nameof(P2H), nameof(P2V),
                     nameof(P1HPlus), nameof(P1HMinus), nameof(P1VPlus), nameof(P1VMinus),
                     nameof(P2HPlus), nameof(P2HMinus), nameof(P2VPlus), nameof(P2VMinus),
                     nameof(UpLeftTag), nameof(UpTag), nameof(UpRightTag), nameof(LeftTag),
                     nameof(RightTag), nameof(DownLeftTag), nameof(DownTag), nameof(DownRightTag),
                     nameof(SecondUpLeftTag), nameof(SecondUpTag), nameof(SecondUpRightTag),
                     nameof(SecondLeftTag), nameof(SecondRightTag), nameof(SecondDownLeftTag),
                     nameof(SecondDownTag), nameof(SecondDownRightTag),
                 })
            OnPropertyChanged(property);
        RefreshHotkeyTips();   // the plane-2 labels carry the letters re-laid here
    }

    public bool ShowDiagonals => Config.Diagonals;

    // Session-only padlock: while engaged the XY cross jogs both gantries mirrored.
    private bool _together;
    public bool Together
    {
        get => _together;
        set
        {
            if (SetField(ref _together, value))
                OnPropertyChanged(nameof(ZaCrossEnabled));
        }
    }

    public bool ZaCrossEnabled => !Together;

    public DelegateCommand JogCommand { get; }
    public DelegateCommand StopCommand { get; }

    private string? _continuousTag;   // tag held down, e.g. "X-" / "X-,Y+|Z-,A+"

    private string? _status = string.Empty;
    public string? Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    private bool _continuousStarting;
    private bool _continuousCancelRequested;

    public bool IsContinuousStep => SelectedDistance == JogConfig.ContinuousDistance;
    public bool ContinuousActive => _continuousTag is not null;
    public bool ContinuousStarting => _continuousStarting;

    /// <summary>Continuous jog needs the board's per-axis max travel ($130+), which only
    /// exists after the connect handshake's $$ fetch. Gates the Continuous toggle
    /// (IsEnabled in the view) — "wait till connection established before allow jogging".
    /// Refreshed on every state emission (see ApplyState); poll ticks keep flowing while
    /// connected, so availability appears within one poll interval of the handshake.</summary>
    public bool ContinuousAvailable =>
        _grbl.IsConnected && _grbl.Capabilities.MaxTravel.Any(t => t > 0);

    public void StartContinuousJog(string? tag, bool allGantries = false)
        => _ = StartContinuousJogAsync(tag, allGantries);

    private async Task StartContinuousJogAsync(string? tag, bool allGantries)
    {
        var mergeBoth = allGantries || Together;   // explicit hotkey flag or the engaged padlock
        if (!IsContinuousStep || string.IsNullOrWhiteSpace(tag) || _continuousTag is not null
            || _continuousStarting || !_grbl.IsConnected || !ContinuousTravelKnown(tag, mergeBoth))
            return;                                   // step mode, empty tag, held, in flight, or no travel

        _continuousStarting = true;
        _continuousCancelRequested = false;
        try
        {
            await _grbl.SendAsync(BuildContinuousJogCommand(tag, mergeBoth));
            if (_continuousCancelRequested)
            {
                CancelJog();                          // release beat the send: cancel, do not commit
            }
            else
            {
                _continuousTag = tag;
                Status = LocText.Resolve("module.jog.continuousStarted", "Continuous jog started");
            }
        }
        catch (InvalidOperationException ex)
        {
            // Not connected: DemoGrblService throws synchronously; leave no stale tag.
            Status = LocText.Format("module.jog.sendFailed", "{0} failed: {1}", tag, ex.Message);
        }
        catch (Exception ex)
        {
            Status = LocText.Format("module.jog.sendFailed", "{0} failed: {1}", tag, ex.Message);
        }
        finally
        {
            _continuousStarting = false;
            _continuousCancelRequested = false;
        }
    }

    public void StopContinuousJog()
    {
        if (_continuousTag is null)
        {
            if (_continuousStarting)
                _continuousCancelRequested = true;    // release arrived before the start landed
            return;
        }
        CancelJog();
    }

    private void CancelJog()
    {
        _continuousTag = null;
        Status = string.Empty;                          // the run is over: drop any jog message
        try { _grbl.SendRealtime(GrblRealtime.JogCancel); }   // 0x85 — decelerate, flush, Idle
        catch (InvalidOperationException) { }                  // disconnected: nothing to cancel
    }

    private bool ContinuousTravelKnown(string tag, bool allGantries)
    {
        var travel = _grbl.Capabilities.MaxTravel;
        if (travel.Length == 0)
            return false;                              // handshake not completed / $$ not answered
        var parts = tag.Split('|');
        var effective = allGantries && parts.Length == 2 ? parts : [parts[0]];
        foreach (var part in effective)
            foreach (var raw in part.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.Trim();
                var index = token.Length > 0 ? _info.AxisLetters.IndexOf(token[0]) : -1;
                if (index < 0 || index >= travel.Length || travel[index] <= 0)
                    return false;                      // axis letter unknown or $130+ unset/zero
            }
        return true;
    }

    public double SelectedDistance =>
        Config.Distances[Math.Clamp(Config.DistanceIndex, 0, Config.Distances.Length - 1)];

    public double SelectedSpeed =>
        Config.Speeds[Math.Clamp(Config.SpeedIndex, 0, Config.Speeds.Length - 1)];

    public int SelectedDistanceIndex
    {
        get => Config.DistanceIndex;
        set
        {
            // Config.PropertyChanged persists + raises flags/derived values.
            Config.DistanceIndex = Math.Clamp(value, 0, JogConfigStore.DistanceStepCount - 1);
        }
    }

    public int SelectedSpeedIndex
    {
        get => Config.SpeedIndex;
        set
        {
            Config.SpeedIndex = Math.Clamp(value, 0, Config.Speeds.Length - 1);
        }
    }

    private bool GetDistanceFlag(int i) => Config.DistanceIndex == i;
    private void SetDistanceFlag(int i, bool value)
    {
        if (value)
            SelectedDistanceIndex = i;
        else
            OnPropertyChanged($"IsDistance{i}");
    }

    private bool GetSpeedFlag(int i) => Config.SpeedIndex == i;
    private void SetSpeedFlag(int i, bool value)
    {
        if (value)
            SelectedSpeedIndex = i;
        else
            OnPropertyChanged($"IsSpeed{i}");
    }

    public bool IsDistance0 { get => GetDistanceFlag(0); set => SetDistanceFlag(0, value); }
    public bool IsDistance1 { get => GetDistanceFlag(1); set => SetDistanceFlag(1, value); }
    public bool IsDistance2 { get => GetDistanceFlag(2); set => SetDistanceFlag(2, value); }
    public bool IsDistance3 { get => GetDistanceFlag(3); set => SetDistanceFlag(3, value); }
    public bool IsSpeed0 { get => GetSpeedFlag(0); set => SetSpeedFlag(0, value); }
    public bool IsSpeed1 { get => GetSpeedFlag(1); set => SetSpeedFlag(1, value); }
    public bool IsSpeed2 { get => GetSpeedFlag(2); set => SetSpeedFlag(2, value); }

    private void RaiseDistanceFlags()
    {
        for (var i = 0; i < JogConfigStore.DistanceStepCount; i++)
            OnPropertyChanged($"IsDistance{i}");
    }

    private void RaiseSpeedFlags()
    {
        for (var i = 0; i < JogConfigStore.SpeedStepCount; i++)
            OnPropertyChanged($"IsSpeed{i}");
    }

    private string BuildJogWords(string tag, Func<string, double> distanceFor, bool allGantries)
    {
        var parts = tag.Split('|');
        IEnumerable<string> SelectWords(string part) => part
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Select(t => ToWord(t, distanceFor(t)));
        return allGantries && parts.Length == 2
            ? string.Join(" ", parts.SelectMany(SelectWords))   // mirrored gantries, one command
            : string.Join(" ", SelectWords(parts[0]));
    }

    private static string ToWord(string token, double distance)
    {
        if (token.Length >= 2 && token[^1] is '+' or '-')
        {
            var sign = token[^1] == '+' ? 1 : -1;
            return $"{token[..^1]}{Fmt(sign * distance)}";
        }
        return token;
    }

    private async Task JogAsync(string? tag, bool allGantries)
    {
        if (string.IsNullOrWhiteSpace(tag) || _continuousTag is not null   // no step jog while holding
            || !_grbl.IsConnected)                                          // wait till connected
            return;
        try
        {
            await _grbl.SendAsync($"$J=G91 {BuildJogWords(tag, _ => SelectedDistance, allGantries)} F{Fmt(SelectedSpeed)}");
            Status = string.Empty;
        }
        catch (Exception ex)
        {
            Status = LocText.Format("module.jog.sendFailed", "{0} failed: {1}", tag, ex.Message);
        }
    }

    /// <summary>Step jog of BOTH gantries (the "Together" merge) regardless of
    /// the padlock state — the Alt+arrow hotkeys' entry point.</summary>
    public void JogTogether(string? tag) => _ = JogAsync(tag, allGantries: true);

    /// <summary>Per-axis continuous distance: the board's $130+ max travel (ioSender's
    /// MaxTravel.Values[axis]). StartContinuousJog's gate guarantees a positive value for
    /// every axis in the tag before this runs; the 0 return is unreachable defense.</summary>
    private double ContinuousDistanceFor(string token)
    {
        var index = _info.AxisLetters.IndexOf(token.Length > 0 ? token[0] : '\0');
        var travel = _grbl.Capabilities.MaxTravel;
        return index >= 0 && index < travel.Length ? travel[index] : 0;
    }

    private string BuildContinuousJogCommand(string tag, bool allGantries) =>
        $"$J=G91 {BuildJogWords(tag, ContinuousDistanceFor, allGantries)} F{Fmt(SelectedSpeed)}";

    public void Save()
    {
        _suppressReload = true;
        try
        {
            JogConfigStore.Save(_profile, Config);
            _settingsService.Save(_profile);
        }
        catch
        {
            // Best-effort persistence; settings still apply for the session.
        }
        finally
        {
            _suppressReload = false;
        }
    }

    private void OnSettingsChanged()
    {
        if (_suppressReload)
            return;

        // A profile switch swaps the shared graph: re-point so saves keep
        // writing to the ACTIVE machine's file, then reload its jog section.
        var active = _settingsService.Load();
        if (!ReferenceEquals(active, _profile))
            _profile = active;

        Config = JogConfigStore.Load(_profile);
        Config.PropertyChanged += OnConfigPropertyChanged;
        OnPropertyChanged(nameof(Config));
        ResolveKinematics();
        RefreshGeometry();
        OnPropertyChanged(nameof(ShowDiagonals));
        OnPropertyChanged(nameof(SelectedDistanceIndex));
        OnPropertyChanged(nameof(SelectedSpeedIndex));
        OnPropertyChanged(nameof(SelectedDistance));
        OnPropertyChanged(nameof(SelectedSpeed));
        RaiseDistanceFlags();
        RaiseSpeedFlags();
        RefreshLinearAxes();
    }

    // The poll stops on disconnect, so ApplyState stops raising ContinuousAvailable —
    // refresh it (and clear any held jog) the moment the connection itself changes.
    private void OnConnectionChanged(object? sender, EventArgs e)
        => UiThread.Post(() =>
        {
            if (!_grbl.IsConnected)
                CancelJog();   // disconnect mid-hold: no board to cancel, but clear the stale tag
            OnPropertyChanged(nameof(ContinuousAvailable));
        });

    private void ApplyState(MachineState state)
    {
        if (state is null)
            return;

        OnPropertyChanged(nameof(ContinuousAvailable));   // before the early-return below
        // Letters ride along with the count: the handshake pushes the [AXS:]
        // letters right before the first state report, and a reconnect to a
        // different board can change them without changing the axis count.
        var letters = _info.AxisLetters;
        var n = Math.Clamp(state.NumAxes, 2, Letters.Length);
        if (n == _numAxes && letters == _letters)
            return;

        _numAxes = n;
        _letters = letters;
        ResolveKinematics();
        RefreshGeometry();
        RefreshLinearAxes();
    }

    // Extra axis rows: every reported axis no VISIBLE plane's in-plane pair
    // consumes, ascending — the mill's depth, any optional axis (rotary axes
    // included; a rotary row jogs in the step distance's units) and any axis the
    // controller reports that the profile does not declare. This is a set test,
    // not a running count, so a plane's non-monotonic indices cannot smuggle an
    // already-crossed axis into a duplicate row.
    private IReadOnlyList<int> ExtraAxes
    {
        get
        {
            var crossed = new HashSet<int>();
            for (var plane = 0; plane < _kinematics.Planes.Count; plane++)
            {
                if (!PlaneVisible(plane))
                    continue;
                crossed.Add(_kinematics.IndexAt(plane, 0));
                crossed.Add(_kinematics.IndexAt(plane, 1));
            }

            var extras = new List<int>();
            for (var index = 0; index < _numAxes; index++)
                if (!crossed.Contains(index))
                    extras.Add(index);
            return extras;
        }
    }

    private void RefreshLinearAxes()
    {
        // Rebuild (not just resize): the crossed-axis set changes with the machine
        // type / clamping, so existing rows must be re-lettered.
        LinearAxisLetters.Clear();
        foreach (var index in ExtraAxes)
            LinearAxisLetters.Add(new LinearAxisRow(_kinematics.LetterFor(index).ToString()));
    }

    private static string Fmt(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
