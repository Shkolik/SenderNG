
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
using System.ComponentModel;
using Avalonia.Controls;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using CNC.GCode;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Modules.Probing.Localization;
using SenderNG.Modules.Probing.ViewModels;
using SenderNG.Core.Commands;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Modules.Probing.Core;

namespace SenderNG.Modules.Plugins;

/// <summary>
/// Probing view model ported from ioSender's CNC.Controls.Probing: named
/// profiles persisted in a separate probing.json, shared probe settings in the
/// left column, and six tools (tool length, edge finders, center finder,
/// rotation, height map) selected via the tool strip. Program generation and
/// execution live in SenderNG.Core (<see cref="ProbeProgramBuilder"/>,
/// <see cref="ProbeRunner"/>, <see cref="ProbeMath"/>).
/// </summary>
public class ProbeViewModel : ViewModelBase
{
    private readonly IGrblService _grbl;
    private readonly SettingsService _settingsService;
    private MachineProfile _profile;
    private readonly IServiceProvider? _services;
    private ProbingProfileStore _store;
    private readonly ProbeRunner _runner;
    private Dictionary<string, bool> _enabledTools;
    private CancellationTokenSource? _cts;

    public ProbeViewModel(IGrblService grbl, SettingsService settings, IServiceProvider? services = null)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _settingsService = settings ?? throw new ArgumentNullException(nameof(settings));
        _services = services;
        _profile = _settingsService.Load();

        // Module settings hold only the enabled-tool checkboxes; the per-profile
        // companion path (profiles/<id>.probing.json) derives from the profile id.
        var filePath = ProfileSettingsPaths.CompanionPath(_settingsService.SettingsDir, _profile.Id, ProbingProfileStore.CompanionSuffix);
        _enabledTools = ReadEnabledTools();
        _store = new ProbingProfileStore(filePath);
        _runner = new ProbeRunner(grbl);

        foreach (var p in _store.Load().Profiles)
            Profiles.Add(p);
        SelectedProfile = Profiles.FirstOrDefault();

        WireCommands();
        ApplyToolInstructions();
        ToolTabs = BuildToolTabs();

        // RotationOriginNames is resolved live (like the {loc:Loc} markup
        // extension): re-raise it when the active locale switches so the
        // selector's items follow without a restart. LocalizationService.
        // Changed is only raised by SetLocale (language picker, UI thread),
        // so the update is safe synchronously. Design mode has no
        // localization service — the English fallback is used there.
        var loc = LocalizationService.Current;
        if (loc is not null)
            loc.Changed += (_, _) => OnPropertyChanged(nameof(RotationOriginNames));

        // Live probe-program preview: regenerate whenever any input changes
        // (PreviewText itself is exempt — its own change must not re-trigger a
        // rebuild). Building a probe program is microseconds, so the preview is
        // always on, no checkbox.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(PreviewText))
                RefreshPreview();
        };

        // Seed the box with the current tool's program (defaults) right away.
        RefreshPreview();

        // Names are editor-local metadata saved by the Offsets editor: re-build
        // the choice list (labels) whenever the settings graph changes.
        _settingsService.Changed += () => UiThread.Post(() =>
        {
            // A profile switch swaps the shared graph: re-init the companion
            // store and the profile list for the new machine.
            var active = _settingsService.Load();
            if (!ReferenceEquals(active, _profile))
            {
                _profile = active;
                _store = new ProbingProfileStore(
                    ProfileSettingsPaths.CompanionPath(_settingsService.SettingsDir, active.Id, ProbingProfileStore.CompanionSuffix));
                Profiles.Clear();
                foreach (var p in _store.Load().Profiles)
                    Profiles.Add(p);
                SelectedProfile = Profiles.FirstOrDefault();
                ApplyToolInstructions();
            }

            // Enabled-tool checkboxes live in the shared profile's module
            // settings: re-read on every save (not just profile switches) so
            // App Settings toggles apply to the open tab immediately.
            _enabledTools = ReadEnabledTools();
            // Push the new enabled flags into the existing tabs instead of
            // rebuilding ToolTabs: a rebuild replaces the tab objects, and once
            // a panel has been materialized its RadioButtons re-register the
            // same GroupNames with Avalonia's per-root RadioButtonGroupManager
            // ("same key already added").
            ApplyEnabledTools();
            OnPropertyChanged(nameof(ToolChoices));
            OnPropertyChanged(nameof(SelectedToolChoice));

            // A module-visibility toggle (App Settings → Modules) can enable or
            // disable the renderer: refresh the "3D View" gate live.
            OnPropertyChanged(nameof(Has3DRenderer));
            OnPropertyChanged(nameof(IsPreview3DVisible));
            (Preview3DCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        });
    }

    /// <summary>
    /// Design-time instance for the Avalonia previewer (no services wired):
    /// enough state to render ProbeView realistically — the tool pinned via
    /// <see cref="ProbeDesignState.PinnedTool"/> selects the visible panel,
    /// sample values feed the numeric fields, and the probe-program preview
    /// and instructions are generated for the pinned tool. Never used at
    /// runtime.
    /// </summary>
    private ProbeViewModel(ProbingType pinnedTool)
    {
        _grbl = null!;
        _settingsService = null!;
        _profile = null!;
        _store = null!;
        _runner = null!;
        _enabledTools = new Dictionary<string, bool>();
        NewProfileName = "Default";
        Profiles.Add(ProbingProfile.CreateDefault());
        WireCommands();
        ProbeEdge = Edge.AD;
        ProbeCenter = Center.XInside;
        SelectedTool = pinnedTool;
        ApplyToolInstructions();
        ToolTabs = BuildToolTabs();

        // Live preview in the designer too: re-seed whenever any input changes
        // (same subscription as the runtime ctor), so toggling modes in the
        // previewer updates the shown program.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(PreviewText))
                RefreshPreview();
        };

        RefreshPreview();
    }

    /// <summary>Designer-only factory: a populated view model pinned to
    /// <paramref name="pinnedTool"/> so exactly one tool panel is visible.</summary>
    public static ProbeViewModel CreateDesignInstance(ProbingType pinnedTool) => new(pinnedTool);

    private void WireCommands()
    {
        StartCommand = new DelegateCommand(_ => _ = StartAsync(), _ => !IsRunning);
        StopCommand = new DelegateCommand(_ => Stop(), _ => IsRunning);
        SelectToolCommand = new DelegateCommand(p =>
        {
            if (p is ProbingType t && IsToolEnabled(t))
            {
                SelectedTool = t;
                ApplyToolInstructions();
            }
        });
        SelectEdgeCommand = new DelegateCommand(p =>
        {
            if (p is Edge e)
                ProbeEdge = e;
        });
        ProfileAddCommand = new DelegateCommand(_ => AddProfile());
        ProfileUpdateCommand = new DelegateCommand(_ => UpdateProfile());
        ProfileDeleteCommand = new DelegateCommand(_ => DeleteProfile());
        HeightMapSaveCommand = new DelegateCommand(_ => _ = SaveHeightMapAsync());
        HeightMapLoadCommand = new DelegateCommand(_ => _ = LoadHeightMapAsync());
        HeightMapApplyCommand = new DelegateCommand(_ => ApplyHeightMapToProgram(), _ => HasHeightMap);
        Preview3DCommand = new DelegateCommand(_ => OnPreview3D(), _ => Has3DRenderer && HasHeightMap);
        RotationApplyCommand = new DelegateCommand(_ => ApplyRotationToProgram());
    }

    // ---------------------------------------------------------------- profiles

    public ObservableCollection<ProbingProfile> Profiles { get; } = new();

    private ProbingProfile? _selectedProfile;
    public ProbingProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            // The editable ComboBox nulls the selection while a typed name does
            // not match a profile; keep the previous selection so Add/Update
            // still know which profile is being edited.
            if (value is null || value == _selectedProfile)
                return;
            if (SetField(ref _selectedProfile, value))
            {
                ProbeDiameter = value.ProbeDiameter;
                TouchPlateHeight = value.TouchPlateHeight;
                FixtureHeight = value.FixtureHeight;
                RapidsFeedRate = value.RapidsFeedRate;
                ProbeFeedRate = value.ProbeFeedRate;
                LatchFeedRate = value.LatchFeedRate;
                ProbeDistance = value.ProbeDistance;
                LatchDistance = value.LatchDistance;
                XYClearance = value.XYClearance;
                Offset = value.Offset;
                Depth = value.Depth;
                ProbeOffsetX = value.ProbeOffsetX;
                ProbeOffsetY = value.ProbeOffsetY;
                TouchPlateIsXY = value.TouchPlateIsXY;
                NewProfileName = value.Name;
            }
        }
    }

    private string _newProfileName = "<Default>";
    public string NewProfileName
    {
        get => _newProfileName;
        set
        {
            // Avalonia's editable ComboBox writes the selected item's ToString()
            // (the full type name) into Text; translate that back to the profile name.
            if ((value == typeof(ProbingProfile).FullName || value == nameof(ProbingProfile)) && _selectedProfile is not null)
                value = _selectedProfile.Name;
            SetField(ref _newProfileName, value);
        }
    }

    public DelegateCommand ProfileAddCommand { get; private set; } = null!;
    public DelegateCommand ProfileUpdateCommand { get; private set; } = null!;
    public DelegateCommand ProfileDeleteCommand { get; private set; } = null!;

    private ProbingProfile Snapshot()
    {
        var p = ProbingProfile.CreateDefault();
        p.Name = NewProfileName;
        p.ProbeDiameter = ProbeDiameter;
        p.TouchPlateHeight = TouchPlateHeight;
        p.FixtureHeight = FixtureHeight;
        p.RapidsFeedRate = RapidsFeedRate;
        p.ProbeFeedRate = ProbeFeedRate;
        p.LatchFeedRate = LatchFeedRate;
        p.ProbeDistance = ProbeDistance;
        p.LatchDistance = LatchDistance;
        p.XYClearance = XYClearance;
        p.Offset = Offset;
        p.Depth = Depth;
        p.ProbeOffsetX = ProbeOffsetX;
        p.ProbeOffsetY = ProbeOffsetY;
        p.TouchPlateIsXY = TouchPlateIsXY;
        return p;
    }

    private void AddProfile()
    {
        var profile = new ProbingProfile();
        CopyInto(Snapshot(), profile);
        profile.Name = string.IsNullOrWhiteSpace(NewProfileName) ? "Profile" : NewProfileName;
        Profiles.Add(profile);
        SelectedProfile = profile;
        SaveProfiles();
    }

    private void UpdateProfile()
    {
        if (_selectedProfile is null)
            return;
        CopyInto(Snapshot(), _selectedProfile);
        _selectedProfile.Name = string.IsNullOrWhiteSpace(NewProfileName) ? _selectedProfile.Name : NewProfileName;
        OnPropertyChanged(nameof(Profiles));
        SaveProfiles();
    }

    private void DeleteProfile()
    {
        if (_selectedProfile is null || Profiles.Count <= 1)
            return;
        var index = Profiles.IndexOf(_selectedProfile);
        Profiles.Remove(_selectedProfile);
        SelectedProfile = Profiles[Math.Max(0, index - 1)];
        SaveProfiles();
    }

    private void SaveProfiles()
    {
        var profiles = new ProbingProfiles();
        foreach (var p in Profiles)
            profiles.Profiles.Add(p);
        _store.Save(profiles);
    }

    private static void CopyInto(ProbingProfile from, ProbingProfile to)
    {
        to.ProbeDiameter = from.ProbeDiameter;
        to.TouchPlateHeight = from.TouchPlateHeight;
        to.FixtureHeight = from.FixtureHeight;
        to.RapidsFeedRate = from.RapidsFeedRate;
        to.ProbeFeedRate = from.ProbeFeedRate;
        to.LatchFeedRate = from.LatchFeedRate;
        to.ProbeDistance = from.ProbeDistance;
        to.LatchDistance = from.LatchDistance;
        to.XYClearance = from.XYClearance;
        to.Offset = from.Offset;
        to.Depth = from.Depth;
        to.ProbeOffsetX = from.ProbeOffsetX;
        to.ProbeOffsetY = from.ProbeOffsetY;
        to.TouchPlateIsXY = from.TouchPlateIsXY;
    }

    // --------------------------------------------------- shared probe settings

    public double ProbeDiameter { get => _probeDiameter; set => SetField(ref _probeDiameter, value); }
    public double TouchPlateHeight { get => _touchPlateHeight; set => SetField(ref _touchPlateHeight, value); }
    public double FixtureHeight { get => _fixtureHeight; set => SetField(ref _fixtureHeight, value); }
    public double RapidsFeedRate { get => _rapidsFeedRate; set => SetField(ref _rapidsFeedRate, value); }
    public double ProbeFeedRate { get => _probeFeedRate; set => SetField(ref _probeFeedRate, value); }
    public double LatchFeedRate { get => _latchFeedRate; set => SetField(ref _latchFeedRate, value); }
    public double ProbeDistance { get => _probeDistance; set => SetField(ref _probeDistance, value); }
    public double LatchDistance { get => _latchDistance; set => SetField(ref _latchDistance, value); }
    public double XYClearance { get => _xyClearance; set => SetField(ref _xyClearance, value); }
    public double Offset { get => _offset; set => SetField(ref _offset, value); }
    public double Depth { get => _depth; set => SetField(ref _depth, value); }
    public double ProbeOffsetX { get => _probeOffsetX; set => SetField(ref _probeOffsetX, value); }
    public double ProbeOffsetY { get => _probeOffsetY; set => SetField(ref _probeOffsetY, value); }
    public bool TouchPlateIsXY { get => _touchPlateIsXY; set => SetField(ref _touchPlateIsXY, value); }

    private double _probeDiameter = 2d, _touchPlateHeight = 1d, _fixtureHeight = 1d, _rapidsFeedRate;
    private double _probeFeedRate = 100d, _latchFeedRate = 25d, _probeDistance = 10d, _latchDistance = 0.5d;
    private double _xyClearance = 5d, _offset = 5d, _depth = 3d, _probeOffsetX, _probeOffsetY;
    private bool _touchPlateIsXY;

    // ------------------------------------------------------------- tool strip

    private ProbingType _selectedTool = ProbingType.ToolLength;

    /// <summary>Last selected mode of the merged Edge finder tab. The tab
    /// itself is one tool (EdgeFinderExternal/EdgeFinderInternal both map to
    /// its index), so the radio choice must be remembered here: switching
    /// away to another tab overwrites <see cref="SelectedTool"/> and the mode
    /// would otherwise reset to External on return.</summary>
    private bool _edgeFinderInternal;

    public ProbingType SelectedTool
    {
        get => _selectedTool;
        set
        {
            // Remember the merged edge-finder tab's mode so re-entering the
            // tab (SelectedToolIndex setter) restores it.
            if (value == ProbingType.EdgeFinderInternal)
            {
                _edgeFinderInternal = true;
                // Internal probing measures a pocket: there is no top surface
                // at its center to probe, so the Z cell doesn't exist in this
                // mode. Move off Z (the ProbeEdge setter then restores the
                // auto-forced ProbeZ flag).
                if (ProbeEdge == Edge.Z)
                    ProbeEdge = Edge.AD;
            }
            else if (value == ProbingType.EdgeFinderExternal)
                _edgeFinderInternal = false;

            if (SetField(ref _selectedTool, value))
            {
                // The result is tool-local: showing the previous tool's
                // message on another tab would be misleading.
                LastResult = string.Empty;
                // The renderer snapshot follows the tab: cleared away from the
                // height-map tab, and the loaded map re-published on return so
                // a round trip doesn't leave the preview blank.
                RaiseHeightMapChanged(value == ProbingType.HeightMap ? HeightMapData : null);
                Preview3DCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(IsToolLength));
                OnPropertyChanged(nameof(IsEdgeExternal));
                OnPropertyChanged(nameof(IsEdgeInternal));
                OnPropertyChanged(nameof(IsCenter));
                OnPropertyChanged(nameof(IsRotation));
                OnPropertyChanged(nameof(IsHeightMap));
                OnPropertyChanged(nameof(IsPreview3DVisible));
                OnPropertyChanged(nameof(IsEdgeFinder));
                OnPropertyChanged(nameof(IsEdgeSelectorVisible));
                OnPropertyChanged(nameof(RotationOriginEnabled));
                OnPropertyChanged(nameof(SelectedToolIndex));
                ApplyToolInstructions();
            }
        }
    }

    public bool IsToolLength => SelectedTool == ProbingType.ToolLength;
    public bool IsEdgeExternal => SelectedTool == ProbingType.EdgeFinderExternal;
    public bool IsEdgeInternal => SelectedTool == ProbingType.EdgeFinderInternal;
    public bool IsCenter => SelectedTool == ProbingType.CenterFinder;
    public bool IsRotation => SelectedTool == ProbingType.Rotation;
    public bool IsHeightMap => SelectedTool == ProbingType.HeightMap;

    /// <summary>True while a height-map renderer module is registered AND enabled
    /// for the active profile: its per-profile Modules visibility flag (App
    /// Settings → Modules) when set, else the module's own
    /// <see cref="IModule.IsVisibleByDefault"/>. Gates the "3D View" action.</summary>
    public bool Has3DRenderer => IsRendererEnabled();

    private bool IsRendererEnabled()
    {
        if (_services?.GetService<IHeightMap3DRenderer>() is not { } renderer)
            return false;
        // A renderer plugin is a module; honour its visibility toggle. A renderer
        // that is not a module has no toggle to consult — treat it as enabled.
        if (renderer is not IModule module)
            return true;
        var modules = _settingsService.Load().Modules;
        return modules.TryGetValue(module.Id, out var visible) ? visible : module.IsVisibleByDefault;
    }

    /// <summary>Renders the "Preview in 3D" button only on the height-map tab
    /// while a renderer module is registered.</summary>
    public bool IsPreview3DVisible => IsHeightMap && Has3DRenderer;

    /// <summary>Zero-based index of <see cref="SelectedTool"/> in ProbeView's
    /// tool tabs (Tool length, Edge finder, Rotation, Center, Height map);
    /// two-way bound to the TabControl's SelectedIndex so tab clicks switch
    /// the active tool and vice versa. Both edge-finder modes (external /
    /// internal, chosen by the radios inside the merged tab) map to index 1.</summary>
    public int SelectedToolIndex
    {
        get => SelectedTool switch
        {
            ProbingType.EdgeFinderExternal or ProbingType.EdgeFinderInternal => 1,
            ProbingType.Rotation => 2,
            ProbingType.CenterFinder => 3,
            ProbingType.HeightMap => 4,
            _ => 0,
        };
        set => SelectedTool = value switch
        {
            // Restore the mode the radios last selected (the internal/external
            // choice is not a tab identity, see _edgeFinderInternal); entering
            // the tab for the first time defaults to external.
            1 => _edgeFinderInternal ? ProbingType.EdgeFinderInternal : ProbingType.EdgeFinderExternal,
            2 => ProbingType.Rotation,
            3 => ProbingType.CenterFinder,
            4 => ProbingType.HeightMap,
            _ => ProbingType.ToolLength,
        };
    }

    /// <summary>The five tool tabs of ProbeView's TabControl (Tool length …
    /// Height map; Edge external and Edge internal share one "Edge finder"
    /// tab whose radios pick the mode). Each tab carries the localized header,
    /// its tool panel (DataContext pinned to this view model) and the enabled
    /// state read from the machine profile; rebuilt on profile switches.
    /// The TabControl's ItemTemplate renders the header, the ContentTemplate
    /// shows the instructions/warning banner above the panel.</summary>
    public IReadOnlyList<ProbeToolTab> ToolTabs { get; private set; } = Array.Empty<ProbeToolTab>();

    private IReadOnlyList<ProbeToolTab> BuildToolTabs() => new ProbeToolTab[]
    {
        new("module.probing.tipTool", "Tool length", MakePanel<ToolLengthPanel>(), ToolLengthEnabled),
        new("module.probing.edge-finder", "Edge finder", MakePanel<EdgeFinderPanel>(), EdgeFinderEnabled),
        new("module.probing.rotation", "Rotation", MakePanel<RotationPanel>(), RotationEnabled),
        new("module.probing.tabCenter", "Center", MakePanel<CenterFinderPanel>(), CenterEnabled),
        new("module.probing.heightmap", "Height map", MakePanel<HeightMapPanel>(), HeightMapEnabled),
    };

    /// <summary>
    /// Defers the panel construction to the first <see cref="ProbeToolTab.View"/>
    /// access instead of building it here.
    /// </summary>
    /// <remarks>
    /// Avalonia controls are owned by the thread that creates them, and a view
    /// model must stay constructible off the UI thread: the constructor runs in
    /// unit tests and in the probe hotkey/dispatcher paths, none of which are on
    /// the UI thread. Building the panels eagerly made every ProbeViewModel own
    /// five UserControls, so a later property change (for example the
    /// <see cref="LocalizationService.Changed"/> re-raise below) updated a
    /// binding on a control owned by another thread and threw
    /// <c>InvalidOperationException: The calling thread cannot access this
    /// object because a different thread owns it</c>. The view is a UI-thread
    /// object; let the UI thread create it.
    /// </remarks>
    private Func<UserControl> MakePanel<T>() where T : UserControl, new() =>
        () => new T { DataContext = this };

    /// <summary>The rotation pivot combo stays usable unless the
    /// "Determine X0,Y0" extra (which forces the (0,0) pivot) is active.</summary>
    public bool RotationOriginEnabled => !(AddAction && IsRotation);

    // ------------------------------------------------------------ probe preview

    /// <summary>
    /// Shows the G-code the current tool would run (built live from the
    /// settings, never executed). Always on — regenerates on any input change
    /// (ioSender gates this behind a Preview checkbox and only refreshes on
    /// tab switch; building a probe program is microseconds, so live refresh
    /// is free).
    /// </summary>
    private string _previewText = string.Empty;
    public string PreviewText
    {
        get => _previewText;
        private set => SetField(ref _previewText, value);
    }

    private void RefreshPreview()
    {
        // ioSender zeroes the start position in preview mode: the shown program
        // is deterministic and independent of the current DRO position.
        var start = new Pos(0d, 0d, 0d);
        try
        {
            var builder = new ProbeProgramBuilder(Snapshot());
            switch (SelectedTool)
            {
                case ProbingType.ToolLength:
                    builder.BuildToolLength(probeFixture: ProbeFixture);
                    break;

                case ProbingType.EdgeFinderExternal:
                case ProbingType.EdgeFinderInternal:
                    if (ProbeEdge != Edge.None)
                        builder.BuildEdgeFinder(ProbeEdge, start, SelectedTool == ProbingType.EdgeFinderInternal);
                    break;

                case ProbingType.CenterFinder:
                    if (ProbeCenter != Center.None)
                        // One pass: the follow-up passes start from the measured
                        // center, which a preview cannot know (ioSender: preview = 1).
                        builder.BuildCenterFinder(ProbeCenter, start, WorkpieceSizeX, WorkpieceSizeY);
                    break;

                case ProbingType.Rotation:
                    if (ProbeEdge is Edge.AD or Edge.AB or Edge.CB or Edge.CD)
                        builder.BuildRotation(ProbeEdge, start, AddAction && ProbeEdge == Edge.AB);
                    break;

                case ProbingType.HeightMap:
                    var map = new HeightMap(HmGridSizeX, HmGridSizeY, new Vec2(HmMinX, HmMinY), new Vec2(HmMaxX, HmMaxY));
                    builder.BuildHeightMap(map, start, HmAddPause);
                    break;
            }

            // Optional Z probe phase of the edge finder (ioSender's
            // "; Post XY probe" preview section): the contact point is only
            // known after the XY probe, so the preview substitutes the start.
            if (SelectedTool is ProbingType.EdgeFinderExternal or ProbingType.EdgeFinderInternal
                && ProbeEdge != Edge.None && ProbeZ && ProbeEdge != Edge.Z)
            {
                var zBuilder = new ProbeProgramBuilder(Snapshot());
                zBuilder.BuildEdgeFinder(Edge.Z, start, SelectedTool == ProbingType.EdgeFinderInternal);
                PreviewText = string.Join("\n", builder.Lines.Append("; Post XY probe").Concat(zBuilder.Lines));
                return;
            }

            PreviewText = string.Join("\n", builder.Lines);
        }
        catch (Exception ex)
        {
            // Invalid height-map grid etc.: surface the reason in the preview box.
            PreviewText = ex.Message;
        }
    }
    public bool IsEdgeFinder => IsEdgeExternal || IsEdgeInternal;
    public bool IsEdgeSelectorVisible => IsEdgeFinder || IsRotation;

    public DelegateCommand SelectToolCommand { get; private set; } = null!;

    public bool IsToolEnabled(ProbingType tool) => tool switch
    {
        ProbingType.ToolLength => _enabledTools.GetValueOrDefault("tool-length", true),
        // The merged Edge finder tab covers both modes (see ProbingModule.ToolKeys).
        ProbingType.EdgeFinderExternal or ProbingType.EdgeFinderInternal =>
            _enabledTools.GetValueOrDefault("edge-finder", true),
        ProbingType.Rotation => _enabledTools.GetValueOrDefault("rotation", true),
        ProbingType.CenterFinder => _enabledTools.GetValueOrDefault("center", true),
        ProbingType.HeightMap => _enabledTools.GetValueOrDefault("heightmap", true),
        _ => false,
    };

    public bool ToolLengthEnabled => IsToolEnabled(ProbingType.ToolLength);
    public bool EdgeFinderEnabled => IsToolEnabled(ProbingType.EdgeFinderExternal);
    public bool CenterEnabled => IsToolEnabled(ProbingType.CenterFinder);
    public bool RotationEnabled => IsToolEnabled(ProbingType.Rotation);
    public bool HeightMapEnabled => IsToolEnabled(ProbingType.HeightMap);

    /// <summary>Copy the current profile's enabled flags into the existing
    /// tabs. Tab order must match <see cref="BuildToolTabs"/> (Tool length,
    /// Edge finder, Rotation, Center, Height map). See the profile-switch
    /// handler in the constructor for why panels are never recreated.</summary>
    private void ApplyEnabledTools()
    {
        if (ToolTabs.Count != 5)
            return;
        ToolTabs[0].IsEnabled = ToolLengthEnabled;
        ToolTabs[1].IsEnabled = EdgeFinderEnabled;
        ToolTabs[2].IsEnabled = RotationEnabled;
        ToolTabs[3].IsEnabled = CenterEnabled;
        ToolTabs[4].IsEnabled = HeightMapEnabled;
    }

    // ------------------------------------------------------ tool-specific input

    private Edge _probeEdge;
    private bool _zForcedProbeZ; // ProbeZ was auto-checked by selecting Z; restore on leaving
    public Edge ProbeEdge
    {
        get => _probeEdge;
        set
        {
            if (_probeEdge == value)
                return;
            // "Determine X0,Y0" only makes sense on the bottom edge (AB):
            // picking any other edge drops the extra (ioSender behavior).
            if (IsRotation && AddAction && value != Edge.AB)
                AddAction = false;
            // Selecting the Z cell means the top surface is measured too: force
            // ProbeZ on (and remember it was forced) so the height probe always
            // runs; leaving Z restores the user's previous choice (ioSender
            // behavior). The checkbox is locked while Z is selected.
            if (value == Edge.Z)
            {
                if (!_probeZ)
                {
                    _zForcedProbeZ = true;
                    ProbeZ = true;
                }
            }
            else if (_probeEdge == Edge.Z && _zForcedProbeZ)
            {
                _zForcedProbeZ = false;
                ProbeZ = false;
            }
            SetField(ref _probeEdge, value);
            OnPropertyChanged(nameof(IsProbeZEditable));
        }
    }
    public DelegateCommand SelectEdgeCommand { get; private set; } = null!;

    private bool _probeZ;
    public bool ProbeZ { get => _probeZ; set => SetField(ref _probeZ, value); }

    /// <summary>The "Probe Z" checkbox is locked on while the Z cell is
    /// selected — the Z probe is implied by the Z action and can't be turned
    /// off (ioSender behavior).</summary>
    public bool IsProbeZEditable => _probeEdge != Edge.Z;

    private double _workpieceHeight;
    public double WorkpieceHeight { get => _workpieceHeight; set => SetField(ref _workpieceHeight, value); }

    private double _workpieceXYEdgeOffset;
    public double WorkpieceXYEdgeOffset { get => _workpieceXYEdgeOffset; set => SetField(ref _workpieceXYEdgeOffset, value); }

    private bool _workpieceLockXY = true;
    public bool WorkpieceLockXY { get => _workpieceLockXY; set => SetField(ref _workpieceLockXY, value); }

    private double _workpieceSizeX = 40d;
    public double WorkpieceSizeX
    {
        get => _workpieceSizeX;
        set
        {
            if (SetField(ref _workpieceSizeX, value) && WorkpieceLockXY)
                SetField(ref _workpieceSizeY, value, nameof(WorkpieceSizeY));
        }
    }

    private double _workpieceSizeY = 40d;
    public double WorkpieceSizeY
    {
        get => _workpieceSizeY;
        set
        {
            if (SetField(ref _workpieceSizeY, value) && WorkpieceLockXY)
                SetField(ref _workpieceSizeX, value, nameof(WorkpieceSizeX));
        }
    }

    private int _passes = 1;
    public int Passes { get => _passes; set => SetField(ref _passes, Math.Max(1, value)); }

    private Center _probeCenter;
    public Center ProbeCenter { get => _probeCenter; set => SetField(ref _probeCenter, value); }

    private bool _probeFixture;
    public bool ProbeFixture { get => _probeFixture; set => SetField(ref _probeFixture, value); }

    private bool _addAction;
    public bool AddAction
    {
        get => _addAction;
        set
        {
            if (SetField(ref _addAction, value))
            {
                OnPropertyChanged(nameof(RotationOriginEnabled));
                // On the rotation tool the extra forces the AB edge and the
                // (0,0) pivot: after zeroing, work (0,0) IS the probed corner,
                // so the rotation apply rotates about the corner. The shared
                // AddAction property is also the tool-length panel's "set
                // coordinate system" checkbox — its side effects stay out of
                // the way by applying only while the rotation tool is active.
                if (value && IsRotation)
                {
                    ProbeEdge = Edge.AB;
                    RotationOrigin = RotationOrigin.None;
                }
            }
        }
    }

    private bool _referenceToolOffset = true;
    public bool ReferenceToolOffset { get => _referenceToolOffset; set => SetField(ref _referenceToolOffset, value); }

    /// <summary>True when the connected controller can persist a tool table
    /// (G10 L1 — grblHAL-only; classic grbl 1.1 / 1.2i forks reject it).</summary>
    public bool HasToolTable =>
        _grbl is not null && _grbl.Capabilities.IsGrblHAL && _grbl.Capabilities.NumTools > 0;

    /// <summary>Tool-table slots to store a probed length into (0..N, matching
    /// the Offsets editor — slot 0 renders as "None", apply a dynamic G43.1
    /// offset instead; numbered slots as T{n} with the editor-local name
    /// appended in parentheses when one is saved for this controller). The
    /// slot count follows the mode's "Visible tools" App-Settings value
    /// (clamped to the controller's slots — same rule as the Offsets editor
    /// grid), so the picker never lists all 32 firmware slots when the editor
    /// is configured for 8.</summary>
    public IReadOnlyList<ToolSelectorItem> ToolChoices
    {
        get
        {
            var tools = new List<ToolSelectorItem>();
            var max = EffectiveToolCount();
            var names = _settingsService is null
                ? new Dictionary<int, string>()
                : OffsetsConfigStore.ToolNamesFor(_settingsService.Load(), _grbl.ControllerKey);
            for (var i = 0; i <= max; i++)
                tools.Add(new ToolSelectorItem(i, names.GetValueOrDefault(i, string.Empty)));
            return tools;
        }
    }

    /// <summary>Tool slots offered by the pickers: min(mode's VisibleTools,
    /// controller's NumTools). 0 (or an unknown/classic controller) yields
    /// just the "None" option.</summary>
    private int EffectiveToolCount()
    {
        // The design-time instance has no services: report no tool-table slots.
        if (_grbl is null || _profile is null)
            return 0;
        var controllerTools = _grbl.Capabilities.IsKnown && _grbl.Capabilities.NumTools > 0
            ? _grbl.Capabilities.NumTools : 0;
        return Math.Min(Math.Clamp(_profile.VisibleTools, 0, 64), controllerTools);
    }

    /// <summary>Selected choice (TwoWay ComboBox anchor, re-matched by value after
    /// an ItemsSource rebuild). None (slot 0) is the default — probing without a
    /// table write.</summary>
    public ToolSelectorItem? SelectedToolChoice
    {
        get => ToolChoices.FirstOrDefault(t => t.Id == SelectedToolNumber);
        set => SelectedToolNumber = value?.Id ?? 0;
    }

    private int _selectedToolNumber;
    public int SelectedToolNumber
    {
        get => _selectedToolNumber;
        set
        {
            if (SetField(ref _selectedToolNumber, Math.Clamp(value, 0, EffectiveToolCount())))
            {
                OnPropertyChanged(nameof(ProbeToolTable));
                OnPropertyChanged(nameof(SelectedToolChoice));
            }
        }
    }

    /// <summary>True when the probe run should store the length into the tool
    /// table rather than apply a dynamic G43.1 offset.</summary>
    public bool ProbeToolTable => SelectedToolNumber > 0 && HasToolTable;

    // Height map geometry (ioSender HeightMapViewModel defaults).
    private double _hmMinX, _hmMinY, _hmMaxX = 50, _hmMaxY = 50, _hmGridSizeX = 5, _hmGridSizeY = 5;
    private bool _hmLockXY = true, _hmAddPause, _hmSetToolOffset;
    public double HmMinX { get => _hmMinX; set => SetField(ref _hmMinX, value); }
    public double HmMinY { get => _hmMinY; set => SetField(ref _hmMinY, value); }
    public double HmMaxX { get => _hmMaxX; set => SetField(ref _hmMaxX, value); }
    public double HmMaxY { get => _hmMaxY; set => SetField(ref _hmMaxY, value); }
    public double HmGridSizeX
    {
        get => _hmGridSizeX;
        set
        {
            if (SetField(ref _hmGridSizeX, Math.Max(0.1, value)) && HmLockXY)
                SetField(ref _hmGridSizeY, _hmGridSizeX, nameof(HmGridSizeY));
        }
    }
    public double HmGridSizeY
    {
        get => _hmGridSizeY;
        set
        {
            if (SetField(ref _hmGridSizeY, Math.Max(0.1, value)) && HmLockXY)
                SetField(ref _hmGridSizeX, _hmGridSizeY, nameof(HmGridSizeX));
        }
    }
    public bool HmLockXY { get => _hmLockXY; set => SetField(ref _hmLockXY, value); }
    public bool HmAddPause { get => _hmAddPause; set => SetField(ref _hmAddPause, value); }
    public bool HmSetToolOffset { get => _hmSetToolOffset; set => SetField(ref _hmSetToolOffset, value); }

    public HeightMap? HeightMapData { get; private set; }

    /// <summary>Raised when the height map changes: the layer (or null when the
    /// map is cleared) for the probing module to broadcast to renderers. The
    /// module owns the VM's lifecycle via DI and subscribes in its
    /// <c>AttachServices</c>; there is no unsubscribe path.</summary>
    public event Action<HeightMapLayer?>? HeightMapChanged;

    private void RaiseHeightMapChanged(HeightMap? map)
        => HeightMapChanged?.Invoke(map is null ? null : ProbingModule.ToHeightMapLayer(map));

    private bool _hasHeightMap;
    public bool HasHeightMap
    {
        get => _hasHeightMap;
        private set
        {
            if (SetField(ref _hasHeightMap, value))
            {
                (HeightMapApplyCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                Preview3DCommand.RaiseCanExecuteChanged();
            }
        }
    }

    // Coordinate writing: G10 (with system) / G92 / Measure.
    private CoordinateMode _coordMode = CoordinateMode.G10;
    public CoordinateMode CoordMode
    {
        get => _coordMode;
        set
        {
            if (SetField(ref _coordMode, value))
            {
                // The radio IsChecked bindings read these computed flags; without
                // the notification the second radio would stay visually checked
                // until some unrelated change re-rendered the group.
                OnPropertyChanged(nameof(IsCoordG10));
                OnPropertyChanged(nameof(IsCoordG92));
                OnPropertyChanged(nameof(IsCoordMeasure));
            }
        }
    }
    public bool IsCoordG10 { get => CoordMode == CoordinateMode.G10; set { if (value) CoordMode = CoordinateMode.G10; } }
    public bool IsCoordG92 { get => CoordMode == CoordinateMode.G92; set { if (value) CoordMode = CoordinateMode.G92; } }
    public bool IsCoordMeasure { get => CoordMode == CoordinateMode.Measure; set { if (value) CoordMode = CoordinateMode.Measure; } }

    public IReadOnlyList<string> CoordinateSystemNames { get; } = ["G54", "G55", "G56", "G57", "G58", "G59"];

    private CoordinateSystem _coordinateSystem = CoordinateSystem.G54;
    public CoordinateSystem CoordinateSystem
    {
        get => _coordinateSystem;
        set => SetField(ref _coordinateSystem, value);
    }
    public int CoordinateSystemIndex
    {
        get => (int)CoordinateSystem - 1;
        set => CoordinateSystem = (CoordinateSystem)Math.Clamp(value + 1, 1, 6);
    }

    // -------------------------------------------------------------- output/run

    private string _instructions = string.Empty;
    public string Instructions { get => _instructions; set => SetField(ref _instructions, value); }

    private string _lastResult = string.Empty;
    public string LastResult { get => _lastResult; set => SetField(ref _lastResult, value); }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
            {
                (StartCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                (StopCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _isConnected;
    public bool IsConnected { get => _isConnected; private set => SetField(ref _isConnected, value); }

    public DelegateCommand StartCommand { get; private set; } = null!;
    public DelegateCommand StopCommand { get; private set; } = null!;
    public DelegateCommand HeightMapSaveCommand { get; private set; } = null!;
    public DelegateCommand HeightMapLoadCommand { get; private set; } = null!;
    public DelegateCommand HeightMapApplyCommand { get; private set; } = null!;
    public DelegateCommand Preview3DCommand { get; private set; } = null!;
    public DelegateCommand RotationApplyCommand { get; private set; } = null!;

    private void ApplyToolInstructions() => Instructions = SelectedTool switch
    {
        ProbingType.ToolLength => LocText.Resolve("module.probing.instr.toolLength", "Probe the touch plate/fixture to set the tool length offset."),
        ProbingType.EdgeFinderExternal or ProbingType.EdgeFinderInternal =>
            LocText.Resolve("module.probing.instr.edge", "Click an edge, corner or Z in the diagram to select the probing action.\nMove the probe above the indicated position before Start."),
        ProbingType.CenterFinder =>
            LocText.Resolve("module.probing.instr.center", "Place the probe above the approximate center of the workpiece before Start.\nSelect inside or outside probing first."),
        ProbingType.Rotation =>
            LocText.Resolve("module.probing.instr.rotation", "Select an edge to probe two points along, then Start to measure the angle."),
        ProbingType.HeightMap =>
            LocText.Resolve("module.probing.instr.heightmap", "Set the map area and grid size, then Start to probe the grid."),
        _ => string.Empty,
    };

    // -------------------------------------------------------------- execution

    public async Task StartAsync()
    {
        if (IsRunning)
            return;

        // Design-time instance (CreateDesignInstance) wires commands so the
        // action bar renders enabled, but must never reach the controller.
        if (_grbl is null)
            return;

        IsConnected = _grbl.IsConnected;
        if (!IsConnected)
        {
            LastResult = LocText.Resolve("module.probing.notConnected", "Not connected - connect first (or use Demo mode).");
            return;
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;
        LastResult = string.Empty;
        // A new probe session invalidates the previous height map: clear it so
        // the preview gate (the HasHeightMap setter raises CanExecute) and the
        // renderer snapshot agree that nothing is loaded mid-run.
        HeightMapData = null;
        OnPropertyChanged(nameof(HeightMapData));
        HasHeightMap = false;
        RaiseHeightMapChanged(null);

        var start = CurrentPosition();
        var profile = Snapshot();
        var builder = new ProbeProgramBuilder(profile);

        try
        {
            switch (SelectedTool)
            {
                case ProbingType.ToolLength:
                    await RunToolLengthAsync(builder, start);
                    break;
                case ProbingType.EdgeFinderExternal:
                    await RunEdgeFinderAsync(builder, start, internalFinder: false);
                    break;
                case ProbingType.EdgeFinderInternal:
                    await RunEdgeFinderAsync(builder, start, internalFinder: true);
                    break;
                case ProbingType.CenterFinder:
                    await RunCenterFinderAsync(builder, start);
                    break;
                case ProbingType.Rotation:
                    await RunRotationAsync(builder, start);
                    break;
                case ProbingType.HeightMap:
                    await RunHeightMapAsync(builder, start);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            LastResult = LocText.Resolve("module.probing.cancelled", "Probing cancelled");
        }
        catch (Exception ex)
        {
            LastResult = LocText.Format("module.probing.failed", "Probing failed: {0}", ex.Message);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Stop() => _cts?.Cancel();

    private Pos CurrentPosition()
    {
        var state = LatestState();
        return state is null ? new Pos(0, 0, 0) : Pos.From(state.Positions);
    }

    private MachineState? LatestState()
    {
        MachineState? state = null;
        using var sub = _grbl.State.Subscribe(s => state = s);
        return state;
    }

    /// <summary>One-shot $# re-read so the Offsets tool table / Work Parameters
    /// widgets immediately reflect a change the controller published, e.g. a
    /// tool-table row written by the tool-length probe. Best-effort: a real
    /// controller re-reads it; the demo is already live and no-ops.</summary>
    private async Task RefreshWorkParametersAsync()
    {
        try
        {
            await _grbl.RefreshWorkParametersAsync();
        }
        catch
        {
            // Non-fatal: a failed re-read keeps the last published snapshot.
        }
    }

    private async Task<ProbeRunResult> RunAsync(ProbeProgramBuilder builder)
    {
        var result = await _runner.ExecuteAsync(builder.Lines, _cts!.Token);
        if (!result.Success)
            LastResult = LocText.Format("module.probing.failed", "Probing failed: {0}", result.Error);
        return result;
    }

    private async Task RunToolLengthAsync(ProbeProgramBuilder builder, Pos start)
    {
        builder.BuildToolLength(probeFixture: ProbeFixture);
        var result = await RunAsync(builder);
        if (!result.Success || result.Positions.Count != 1)
        {
            LastResult = LocText.Resolve("module.probing.noPosition", "Probing failed - no probe position captured");
            return;
        }

        var touch = Pos.From(result.Positions[0]);
        if (ProbeFixture)
            touch = new Pos(touch.X, touch.Y, touch.Z - FixtureHeight);

        if (AddAction)
        {
            var z = ProbeFixture ? FixtureHeight : WorkpieceHeight + TouchPlateHeight;
            var cmd = ProbeMath.OffsetCommand(CoordMode, CoordinateSystem, new Pos(0, 0, z), "Z");
            if (!string.IsNullOrEmpty(cmd))
            {
                await _grbl.SendAsync(cmd);
                LastResult = LocText.Format("module.probing.toolLengthSet", "Tool length offset set: {0}", cmd);
            }
            else
            {
                LastResult = LocText.Format("module.probing.measuredZ", "Measured Z: {0}", ProbeMath.Fmt(touch.Z));
            }
        }
        else if (ProbeToolTable)
        {
            // Store the probed length into the selected tool-table slot (G10 L1 Pn
            // Z), then apply it with G43 Hn so the current job uses it. ioSender
            // scaffolded exactly this but shipped it commented out; on a grblHAL
            // board this persists the length so the Offsets tool table and probing
            // share one source of truth.
            var table = ProbeMath.ToolLengthTableCommand(SelectedToolNumber, touch.Z);
            var apply = $"G43H{SelectedToolNumber}";
            await _grbl.SendAsync(table);
            await _grbl.SendAsync(apply);
            // Re-read $# so the Offsets tool table and Work Parameters widgets
            // immediately reflect the new stored length and the active TLO
            // (the stored table row only arrives via a $# dump).
            await RefreshWorkParametersAsync();
            LastResult = LocText.Format("module.probing.toolLengthStored", "Tool length offset stored to T{0}: {1}", SelectedToolNumber, table);
        }
        else
        {
            var cmd = $"G43.1Z{ProbeMath.Fmt(touch.Z)}";
            await _grbl.SendAsync(cmd);
            LastResult = LocText.Format("module.probing.toolLengthSet", "Tool length offset set: {0}", cmd);
        }
    }

    private async Task RunEdgeFinderAsync(ProbeProgramBuilder builder, Pos start, bool internalFinder)
    {
        if (ProbeEdge == Edge.None)
        {
            LastResult = LocText.Resolve("module.probing.selectEdgeFirst", "Select an edge or corner first");
            return;
        }

        builder.BuildEdgeFinder(ProbeEdge, start, internalFinder);
        var result = await RunAsync(builder);
        if (!result.Success || result.Positions.Count == 0)
        {
            LastResult = LocText.Resolve("module.probing.noPosition", "Probing failed - no probe position captured");
            return;
        }

        var probes = result.Positions.Select(Pos.From).ToArray();
        var contact = ProbeMath.ComputeEdgePoint(ProbeEdge, probes, ProbeDiameter, internalFinder);

        // Optional Z probe at the computed contact point (move there first).
        if (ProbeZ && ProbeEdge != Edge.Z)
        {
            await _grbl.SendAsync($"G53G0X{ProbeMath.Fmt(contact.X)}Y{ProbeMath.Fmt(contact.Y)}");
            var zProbe = new ProbeProgramBuilder(Snapshot());
            zProbe.BuildEdgeFinder(Edge.Z, contact, internalFinder);
            var zResult = await RunAsync(zProbe);
            if (zResult.Success && zResult.Positions.Count > 0)
                contact = new Pos(contact.X, contact.Y, Pos.From(zResult.Positions[^1]).Z);
        }

        await ApplyEdgeOffsetAsync(contact, internalFinder);
    }

    private async Task ApplyEdgeOffsetAsync(Pos contact, bool internalFinder)
    {
        // Touch-plate offset sign follows the edge (ioSender IsProbeEdgeNegativeX/Y).
        var negX = ProbeEdge is Edge.B or Edge.C or Edge.CB;
        var negY = ProbeEdge is Edge.C or Edge.D or Edge.CD;
        var tpX = TouchPlateIsXY && negX ? -ProbeOffsetX : ProbeOffsetX;
        var tpY = TouchPlateIsXY && negY ? -ProbeOffsetY : ProbeOffsetY;

        string axes;
        Pos pos;
        if (CoordMode == CoordinateMode.G92)
        {
            axes = ProbeEdge == Edge.Z ? "Z" : ProbeMath.ProbedAxes(ProbeEdge);
            pos = internalFinder
                ? new Pos(ProbeOffsetX, ProbeOffsetY, WorkpieceHeight + TouchPlateHeight)
                : new Pos(-tpX, -tpY, WorkpieceHeight + TouchPlateHeight);
        }
        else
        {
            axes = ProbeMath.ProbedAxes(ProbeEdge);
            pos = new Pos(contact.X + tpX, contact.Y + tpY, contact.Z - WorkpieceHeight - TouchPlateHeight);
        }

        var cmd = ProbeMath.OffsetCommand(CoordMode, CoordinateSystem, pos, axes);
        if (string.IsNullOrEmpty(cmd))
        {
            LastResult = LocText.Format("module.probing.measured", "Measured: X{0} Y{1} Z{2}",
                ProbeMath.Fmt(contact.X), ProbeMath.Fmt(contact.Y), ProbeMath.Fmt(contact.Z));
            return;
        }

        try
        {
            await _grbl.SendAsync(cmd);
            LastResult = LocText.Format("module.probing.probingCompleted", "Probing completed: {0}", cmd);
        }
        catch (Exception ex)
        {
            LastResult = LocText.Format("module.probing.failed", "Probing failed: {0}", ex.Message);
        }
    }

    private async Task RunCenterFinderAsync(ProbeProgramBuilder builder, Pos start)
    {
        if (ProbeCenter == Center.None)
        {
            LastResult = LocText.Resolve("module.probing.selectCenterFirst", "Select a center probing mode first");
            return;
        }

        var needX = ProbeCenter is Center.XInside or Center.XOutside;
        var needY = ProbeCenter is Center.YInside or Center.YOutside;
        if (needX ? WorkpieceSizeX <= 0 : needY ? WorkpieceSizeY <= 0 : WorkpieceSizeX <= 0 && WorkpieceSizeY <= 0)
        {
            LastResult = LocText.Resolve("module.probing.sizeMustBeSet", "Workpiece size must be set");
            return;
        }

        var current = start;
        for (var pass = 0; pass < Passes; pass++)
        {
            builder.BuildCenterFinder(ProbeCenter, current, WorkpieceSizeX, WorkpieceSizeY);
            var result = await RunAsync(builder);
            if (!result.Success)
                return;

            var expected = needX || needY ? 2 : (WorkpieceSizeY > 0 && WorkpieceSizeX > 0) ? 4 : 2;
            if (result.Positions.Count < expected)
            {
                LastResult = LocText.Resolve("module.probing.notEnoughPos", "Probing failed - not enough probe positions");
                return;
            }

            var probes = result.Positions.Select(Pos.From).ToArray();
            var (center, xd, yd) = ProbeMath.ComputeCenter(ProbeCenter, current, probes, ProbeDiameter, WorkpieceSizeX, WorkpieceSizeY);
            current = center;

            if (pass == Passes - 1)
            {
                // Move to center, then write the offset.
                await _grbl.SendAsync($"G53G0X{ProbeMath.Fmt(center.X)}Y{ProbeMath.Fmt(center.Y)}");
                await ApplyCenterOffsetAsync(center, xd, yd);
            }
        }
    }

    private async Task ApplyCenterOffsetAsync(Pos center, double xd, double yd)
    {
        var singleAxis = ProbeCenter switch
        {
            Center.XInside or Center.XOutside => "X",
            Center.YInside or Center.YOutside => "Y",
            _ => null,
        };

        if (CoordMode == CoordinateMode.Measure)
        {
            LastResult = singleAxis != null
                ? LocText.Format("module.probing.axisDistance", "Probing completed: {0} distance {1}", singleAxis, ProbeMath.Fmt(singleAxis == "X" ? xd : yd))
                : LocText.Format("module.probing.xDistance", "Probing completed: X distance {0}, Y distance {1}", ProbeMath.Fmt(xd), ProbeMath.Fmt(yd));
            return;
        }

        var axes = singleAxis ?? (WorkpieceSizeY > 0 && WorkpieceSizeX > 0 ? "XY" : (WorkpieceSizeX > 0 ? "Y" : "X"));
        var pos = CoordMode == CoordinateMode.G92
            ? new Pos(ProbeOffsetX, ProbeOffsetY, 0)
            : singleAxis == "X"
                ? new Pos(center.X + ProbeOffsetX, 0, 0)
                : singleAxis == "Y"
                    ? new Pos(0, center.Y + ProbeOffsetY, 0)
                    : new Pos(center.X + ProbeOffsetX, center.Y + ProbeOffsetY, 0);

        var cmd = ProbeMath.OffsetCommand(CoordMode, CoordinateSystem, pos, axes);
        if (string.IsNullOrEmpty(cmd))
            return;

        try
        {
            await _grbl.SendAsync(cmd);
            LastResult = singleAxis != null
                ? LocText.Format("module.probing.axisDistanceArrow", "Probing completed: {0} distance {1} -> {2}", singleAxis, ProbeMath.Fmt(singleAxis == "X" ? xd : yd), cmd)
                : LocText.Format("module.probing.xDistanceArrow", "Probing completed: X distance {0}, Y distance {1} -> {2}", ProbeMath.Fmt(xd), ProbeMath.Fmt(yd), cmd);
        }
        catch (Exception ex)
        {
            LastResult = LocText.Format("module.probing.failed", "Probing failed: {0}", ex.Message);
        }
    }

    private async Task RunRotationAsync(ProbeProgramBuilder builder, Pos start)
    {
        if (ProbeEdge is not (Edge.AD or Edge.AB or Edge.CB or Edge.CD))
        {
            LastResult = LocText.Resolve("module.probing.selectRotationEdge", "Select an edge for rotation probing");
            return;
        }

        var determineX0Y0 = AddAction && ProbeEdge == Edge.AB;
        builder.BuildRotation(ProbeEdge, start, determineX0Y0);
        var result = await RunAsync(builder);
        var expected = determineX0Y0 ? 3 : 2;
        if (!result.Success || result.Positions.Count < expected)
        {
            LastResult = LocText.Resolve(determineX0Y0 ? "module.probing.threePosRequired" : "module.probing.twoPosRequired",
                determineX0Y0 ? "Probing failed - three probe positions required" : "Probing failed - two probe positions required");
            return;
        }

        // Positions arrive in program order: p1/p2 on the probed edge, then
        // (with the extra) p3 on the left edge.
        var p1 = Pos.From(result.Positions[0]);
        var p2 = Pos.From(result.Positions[1]);
        var angle = ProbeMath.ComputeRotationRadians(ProbeEdge, p1, p2);
        _probedAngleRadians = angle;

        if (determineX0Y0)
        {
            var p3 = Pos.From(result.Positions[2]);
            var corner = ProbeMath.ComputeCorner(p1, p2, p3, ProbeMath.ComputeRotationAngle(ProbeEdge, p1, p2),
                ProbeOffsetX, ProbeOffsetY, ProbeDiameter / 2d, Offset);

            // Ride the spindle to the corner (machine coords), then write the
            // work offset so the corner reads as X0,Y0: G92 zeroes the current
            // position, G10 stores the machine position into the system.
            await _grbl.SendAsync($"G53G0X{ProbeMath.Fmt(corner.X)}Y{ProbeMath.Fmt(corner.Y)}");
            var pos = CoordMode == CoordinateMode.G92 ? new Pos(0d, 0d, 0d) : corner;
            var cmd = ProbeMath.OffsetCommand(CoordMode, CoordinateSystem, pos, "XY");
            if (string.IsNullOrEmpty(cmd))
            {
                LastResult = LocText.Format("module.probing.cornerMeasured", "Probed corner: X{0} Y{1}",
                    ProbeMath.Fmt(corner.X), ProbeMath.Fmt(corner.Y));
                return;
            }

            await _grbl.SendAsync(cmd);
            LastResult = LocText.Format("module.probing.cornerZeroed", "X0,Y0 set at the probed corner: {0}", cmd);
            return;
        }

        LastResult = LocText.Format("module.probing.probedAngle", "Probed angle: {0}°", ProbeMath.ToDegrees(angle));
    }

    private double _probedAngleRadians;

    private async Task RunHeightMapAsync(ProbeProgramBuilder builder, Pos start)
    {
        HeightMap map;
        try
        {
            map = new HeightMap(HmGridSizeX, HmGridSizeY, new Vec2(HmMinX, HmMinY), new Vec2(HmMaxX, HmMaxY));
        }
        catch (Exception ex)
        {
            LastResult = ex.Message;
            return;
        }

        builder.BuildHeightMap(map, start, HmAddPause);
        var result = await RunAsync(builder);
        if (!result.Success || result.Positions.Count != map.TotalPoints)
        {
            LastResult = LocText.Format("module.probing.expectedFail", "Probing failed - expected {0} positions, got {1}", map.TotalPoints, result.Positions.Count);
            return;
        }

        // Fill the map in the same snake order the program probes.
        var z0 = result.Positions[0][2];
        var zMin = 0d; var zMax = 0d;
        var i = 0;
        for (var x = 0; x < map.SizeX; x++)
        {
            for (var y = 0; y < map.SizeY; y++)
            {
                var delta = result.Positions[i++][2] - z0;
                zMin = Math.Min(zMin, delta);
                zMax = Math.Max(zMax, delta);
                map.AddPoint(x, y, Math.Round(delta, 3));
            }
            if (++x < map.SizeX)
            {
                for (var y = map.SizeY - 1; y >= 0; y--)
                {
                    var delta = result.Positions[i++][2] - z0;
                    zMin = Math.Min(zMin, delta);
                    zMax = Math.Max(zMax, delta);
                    map.AddPoint(x, y, Math.Round(delta, 3));
                }
            }
        }

        if (HmSetToolOffset)
        {
            var zProbe = new ProbeProgramBuilder(Snapshot());
            zProbe.BuildToolLength(probeFixture: false);
            var zResult = await RunAsync(zProbe);
            if (zResult.Success && zResult.Positions.Count > 0)
            {
                map.ZOffset = Math.Round(z0 - zResult.Positions[0][2], 3);
                await _grbl.SendAsync($"G10L2P{(int)CoordinateSystem}Z{ProbeMath.Fmt(zResult.Positions[0][2])}");
            }
        }

        HeightMapData = map;
        OnPropertyChanged(nameof(HeightMapData));
        HasHeightMap = true;
        RaiseHeightMapChanged(map);
        LastResult = LocText.Format("module.probing.zRange", "Probing completed: Z range {0} to {1}", ProbeMath.Fmt(zMin), ProbeMath.Fmt(zMax));
    }

    // ------------------------------------------------------ height map file io

    private async Task SaveHeightMapAsync()
    {
        if (HeightMapData is null)
        {
            LastResult = LocText.Resolve("module.probing.noHeightMap", "No height map to save");
            return;
        }

        // Prefer a native save dialog; fall back to the settings dir when no UI is available.
        var dialogs = _services?.GetService(typeof(IFileDialogService)) as IFileDialogService;
        var path = dialogs is null
            ? Path.Combine(SettingsService.DefaultDir(), "heightmap.hmap.json")
            : await dialogs.SaveFileAsync(LocText.Resolve("module.probing.saveMap", "Save map"), "heightmap.hmap.json", "Height map (*.hmap.json)|*.hmap.json");
        if (string.IsNullOrEmpty(path))
            return; // cancelled

        try
        {
            HeightMapData.Save(path);
            LastResult = LocText.Format("module.probing.heightMapSaved", "Height map saved to {0}", path);
        }
        catch (Exception ex)
        {
            LastResult = LocText.Format("module.probing.saveFailed", "Save failed: {0}", ex.Message);
        }
    }

    private async Task LoadHeightMapAsync()
    {
        // Prefer a native open dialog; fall back to the settings dir when no UI is available.
        var dialogs = _services?.GetService(typeof(IFileDialogService)) as IFileDialogService;
        var path = dialogs is null
            ? Path.Combine(SettingsService.DefaultDir(), "heightmap.hmap.json")
            : await dialogs.OpenFileAsync(LocText.Resolve("module.probing.loadMap", "Load map"), "Height map (*.hmap.json)|*.hmap.json");
        if (string.IsNullOrEmpty(path))
            return; // cancelled

        try
        {
            if (!File.Exists(path))
            {
                LastResult = LocText.Format("module.probing.noHeightMapAt", "No height map at {0}", path);
                return;
            }
            var map = HeightMap.Load(path);
            HeightMapData = map;
            HmMinX = map.Min.X;
            HmMinY = map.Min.Y;
            HmMaxX = map.Max.X;
            HmMaxY = map.Max.Y;
            HmGridSizeX = map.GridX;
            HmGridSizeY = map.GridY;
            HasHeightMap = true;
            RaiseHeightMapChanged(map);
            LastResult = LocText.Format("module.probing.heightMapLoaded", "Height map loaded from {0}", path);
        }
        catch (Exception ex)
        {
            LastResult = LocText.Format("module.probing.loadFailed", "Load failed: {0}", ex.Message);
        }
    }

    // ------------------------------------------------------- rotation origin

    /// <summary>Combo labels for <see cref="RotationOrigin"/> (order matches
    /// the enum). Rebuilt on every locale change — a live property, not a
    /// snapshot — so the selector follows a language switch without restart.
    /// Names are positional only (no A/B/C/D letters): the rotation diagram
    /// uses its own letter convention, so "Edge AB" would be actively
    /// misleading (AB is the bottom edge here, ioSender's letters were
    /// flipped).</summary>
    public IReadOnlyList<string> RotationOriginNames => BuildRotationOriginNames();

    private static IReadOnlyList<string> BuildRotationOriginNames() =>
    [
        LocText.Resolve("module.probing.origin.none", "0, 0"),
        LocText.Resolve("module.probing.origin.a", "Bottom-left corner"),
        LocText.Resolve("module.probing.origin.b", "Bottom-right corner"),
        LocText.Resolve("module.probing.origin.c", "Top-right corner"),
        LocText.Resolve("module.probing.origin.d", "Top-left corner"),
        LocText.Resolve("module.probing.origin.center", "Center"),
        LocText.Resolve("module.probing.origin.ab", "Bottom edge"),
        LocText.Resolve("module.probing.origin.ad", "Left edge"),
        LocText.Resolve("module.probing.origin.cb", "Right edge"),
        LocText.Resolve("module.probing.origin.cd", "Top edge"),
        LocText.Resolve("module.probing.origin.current", "Current position"),
    ];

    private RotationOrigin _rotationOrigin;
    public RotationOrigin RotationOrigin
    {
        get => _rotationOrigin;
        set => SetField(ref _rotationOrigin, value);
    }

    /// <summary>ComboBox anchor for <see cref="RotationOrigin"/> (order must
    /// match <see cref="RotationOriginNames"/>).</summary>
    public int RotationOriginIndex
    {
        get => (int)RotationOrigin;
        set => RotationOrigin = (RotationOrigin)Math.Clamp(value, 0, (int)RotationOrigin.CurrentPos);
    }

    /// <summary>Rotation pivot coordinates for the selected origin — resolved
    /// at apply time from the loaded program's limits (ioSender's
    /// <c>apply_Click</c> shape) or the current DRO position.</summary>
    private (double X, double Y) ResolveRotationOrigin(IProgramTransformTarget program)
    {
        var minX = 0d; var minY = 0d; var maxX = 0d; var maxY = 0d;
        foreach (var row in program.Limits)
        {
            switch (row.Letter)
            {
                case 'X': minX = row.Min; maxX = row.Max; break;
                case 'Y': minY = row.Min; maxY = row.Max; break;
            }
        }
        var centerX = (minX + maxX) / 2d;
        var centerY = (minY + maxY) / 2d;
        return RotationOrigin switch
        {
            RotationOrigin.A => (minX, minY),
            RotationOrigin.B => (maxX, minY),
            RotationOrigin.C => (maxX, maxY),
            RotationOrigin.D => (minX, maxY),
            RotationOrigin.Center => (centerX, centerY),
            RotationOrigin.AB => (centerX, minY),
            RotationOrigin.AD => (minX, centerY),
            RotationOrigin.CB => (maxX, centerY),
            RotationOrigin.CD => (centerX, maxY),
            RotationOrigin.CurrentPos => CurrentDro(),
            _ => (0d, 0d),
        };
    }

    private (double X, double Y) CurrentDro()
    {
        var state = LatestState();
        if (state is null)
            return (0d, 0d);
        var pos = Pos.From(state.Positions);
        return (pos.X, pos.Y);
    }

    /// <summary>Loads a transform result through the same engine gate as
    /// File → Transform (parse → check errors → transform → re-parse → check
    /// errors → load): a transformed program that would fail while sending is
    /// never loaded.</summary>
    private bool LoadTransformedProgram(IProgramTransformTarget program, string sourceText,
        Func<GCodeProgram, List<string>> transform, out string error)
    {
        var parsed = GCodeProgram.Parse(sourceText);
        if (parsed.HasErrors)
        {
            error = string.Join(Environment.NewLine, parsed.Errors);
            return false;
        }

        var resultText = string.Join("\n", transform(parsed));
        var result = GCodeProgram.Parse(resultText);
        if (result.HasErrors)
        {
            error = string.Join(Environment.NewLine, result.Errors);
            return false;
        }

        program.LoadProgram(result, resultText);
        error = string.Empty;
        return true;
    }

    private async void OnPreview3D()
    {
        if (_services is not { } services ||
            services.GetService(typeof(SenderNG.Core.Modules.IHeightMap3DRenderer))
                is not SenderNG.Core.Modules.IHeightMap3DRenderer renderer)
            return;

        try
        {
            // Awaited, not fire-and-forget: the renderer's failure often surfaces
            // after its first await, which a discarded Task would swallow.
            await renderer.ShowAsync();
        }
        catch (Exception ex)
        {
            // Error surfacing is host-owned: report through the module's keyed
            // IErrorReporter (the container keys it by module id, so the unkeyed
            // lookup is dead), otherwise fall back to the in-panel result line
            // (tests / design mode have no reporter).
            if (services.GetKeyedService<IErrorReporter>("probing") is IErrorReporter reporter)
                reporter.Status(ex.Message);
            else
                LastResult = ex.Message;
        }
    }

    private void ApplyHeightMapToProgram()
    {
        if (HeightMapData is null || _services is not { } services)
        {
            LastResult = LocText.Resolve("module.probing.probeOrLoadFirst", "Probe or load a height map first");
            return;
        }
        var program = services.GetService(typeof(IProgramTransformTarget)) as IProgramTransformTarget;
        if (program is null || string.IsNullOrEmpty(program.LoadedText))
        {
            LastResult = LocText.Resolve("module.probing.noProgramLoaded", "No program loaded");
            return;
        }

        try
        {
            var segmentLength = Math.Min(HeightMapData.GridX, HeightMapData.GridY);
            if (!LoadTransformedProgram(program, program.LoadedText,
                    p => GCodeTransformers.ApplyHeightMap(p.Tokens,
                        (x, y) => HeightMapData.InterpolateZ(x, y), segmentLength, p.Decimals),
                    out var error))
            {
                LastResult = LocText.Format("module.probing.heightMapApplyFailed", "Height map apply failed: {0}", error);
                return;
            }
            LastResult = LocText.Resolve("module.probing.heightMapApplied", "Height map applied to program");
        }
        catch (Exception ex)
        {
            LastResult = LocText.Format("module.probing.heightMapApplyFailed", "Height map apply failed: {0}", ex.Message);
        }
    }

    private void ApplyRotationToProgram()
    {
        if (Math.Abs(_probedAngleRadians) < 0.0001 || _services is not { } services)
        {
            LastResult = LocText.Resolve("module.probing.probeAngleFirst", "Probe an edge angle first");
            return;
        }
        var program = services.GetService(typeof(IProgramTransformTarget)) as IProgramTransformTarget;
        if (program is null || string.IsNullOrEmpty(program.LoadedText))
        {
            LastResult = LocText.Resolve("module.probing.noProgramLoaded", "No program loaded");
            return;
        }

        try
        {
            var (originX, originY) = ResolveRotationOrigin(program);
            if (!LoadTransformedProgram(program, program.LoadedText,
                    p => GCodeTransformers.Rotate(p.Tokens, _probedAngleRadians, originX, originY, p.Decimals),
                    out var error))
            {
                LastResult = LocText.Format("module.probing.rotationApplyFailed", "Rotation failed: {0}", error);
                return;
            }
            LastResult = LocText.Resolve("module.probing.programRotated", "Program rotated");
        }
        catch (Exception ex)
        {
            LastResult = LocText.Format("module.probing.rotationApplyFailed", "Rotation failed: {0}", ex.Message);
        }
    }

    // ----------------------------------------------------------------- helpers

    private Dictionary<string, bool> ReadEnabledTools() => ProbingModule.ReadEnabledTools(_profile);
}

/// <summary>One tab of ProbeView's tool TabControl (see
/// <see cref="ProbeViewModel.ToolTabs"/>): the localized header, the lazily
/// built tool panel view, and whether the tool is enabled for the machine. The
/// header re-resolves on <see cref="LocalizationService.Changed"/> so tab names
/// follow a live locale switch, same as the {loc:Loc} markup extension;
/// <see cref="IsEnabled"/> is updated in place on profile switches so the
/// panels are never recreated. (Design mode has no localization service: the
/// English fallback is used.)</summary>
public sealed class ProbeToolTab : INotifyPropertyChanged
{
    private readonly string _key;
    private readonly string _fallback;
    private readonly Func<UserControl> _viewFactory;
    private UserControl? _view;
    private string _name;
    private bool _isEnabled;

    public ProbeToolTab(string key, string fallback, Func<UserControl> viewFactory, bool isEnabled)
    {
        _key = key;
        _fallback = fallback;
        _name = LocText.Resolve(key, fallback);
        _viewFactory = viewFactory;
        _isEnabled = isEnabled;

        var loc = LocalizationService.Current;
        if (loc is not null)
            loc.Changed += (_, _) => UiThread.Post(() => Name = LocText.Resolve(_key, _fallback));
    }

    /// <summary>The localized tab header; raises <see cref="PropertyChanged"/>
    /// when the active locale changes.</summary>
    public string Name
    {
        get => _name;
        private set
        {
            if (_name == value)
                return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    /// <summary>The tool panel, built on first access by the thread that asks
    /// for it — the UI thread, when the TabControl binds it.</summary>
    public UserControl View => _view ??= _viewFactory();

    /// <summary>Whether the machine profile enables the tool. Set in place on
    /// profile switches (see <see cref="ProbeViewModel.ApplyEnabledTools"/>).</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        internal set
        {
            if (_isEnabled == value)
                return;
            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
