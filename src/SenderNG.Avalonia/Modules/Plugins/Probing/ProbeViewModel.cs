using System.Collections.ObjectModel;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Fixed;
using SenderNG.Avalonia.ViewModels;
using SenderNG.Core.Commands;
using SenderNG.Core.Modules.Probing;

namespace SenderNG.Avalonia.Modules.Plugins;

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
    private readonly AppSettings _appSettings;
    private readonly IServiceProvider? _services;
    private readonly ProbingProfileStore _store;
    private readonly ProbeRunner _runner;
    private readonly Dictionary<string, bool> _enabledTools;
    private CancellationTokenSource? _cts;

    public ProbeViewModel(IGrblService grbl, SettingsService settings, IServiceProvider? services = null)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _settingsService = settings ?? throw new ArgumentNullException(nameof(settings));
        _services = services;
        _appSettings = _settingsService.Load();

        // Module settings hold only the profiles file path + enabled tool checkboxes.
        var filePath = ReadString(ProbingModule.ProfilesFileKey) ?? Path.Combine(_settingsService.SettingsDir, "probing.json");
        _enabledTools = ReadEnabledTools();
        _store = new ProbingProfileStore(filePath);
        _runner = new ProbeRunner(grbl);

        foreach (var p in _store.Load().Profiles)
            Profiles.Add(p);
        SelectedProfile = Profiles.FirstOrDefault();

        WireCommands();
        ApplyToolInstructions();
    }

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
        HeightMapApplyCommand = new DelegateCommand(_ => ApplyHeightMapToProgram());
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
    public ProbingType SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (SetField(ref _selectedTool, value))
            {
                OnPropertyChanged(nameof(IsToolLength));
                OnPropertyChanged(nameof(IsEdgeExternal));
                OnPropertyChanged(nameof(IsEdgeInternal));
                OnPropertyChanged(nameof(IsCenter));
                OnPropertyChanged(nameof(IsRotation));
                OnPropertyChanged(nameof(IsHeightMap));
                OnPropertyChanged(nameof(IsEdgeFinder));
                OnPropertyChanged(nameof(IsEdgeSelectorVisible));
            }
        }
    }

    public bool IsToolLength => SelectedTool == ProbingType.ToolLength;
    public bool IsEdgeExternal => SelectedTool == ProbingType.EdgeFinderExternal;
    public bool IsEdgeInternal => SelectedTool == ProbingType.EdgeFinderInternal;
    public bool IsCenter => SelectedTool == ProbingType.CenterFinder;
    public bool IsRotation => SelectedTool == ProbingType.Rotation;
    public bool IsHeightMap => SelectedTool == ProbingType.HeightMap;
    public bool IsEdgeFinder => IsEdgeExternal || IsEdgeInternal;
    public bool IsEdgeSelectorVisible => IsEdgeFinder || IsRotation;

    public DelegateCommand SelectToolCommand { get; private set; } = null!;

    public bool IsToolEnabled(ProbingType tool) => tool switch
    {
        ProbingType.ToolLength => _enabledTools.GetValueOrDefault("tool-length", true),
        ProbingType.EdgeFinderExternal => _enabledTools.GetValueOrDefault("edge-external", true),
        ProbingType.EdgeFinderInternal => _enabledTools.GetValueOrDefault("edge-internal", true),
        ProbingType.Rotation => _enabledTools.GetValueOrDefault("rotation", true),
        ProbingType.CenterFinder => _enabledTools.GetValueOrDefault("center", true),
        ProbingType.HeightMap => _enabledTools.GetValueOrDefault("heightmap", true),
        _ => false,
    };

    public bool ToolLengthEnabled => IsToolEnabled(ProbingType.ToolLength);
    public bool EdgeExternalEnabled => IsToolEnabled(ProbingType.EdgeFinderExternal);
    public bool EdgeInternalEnabled => IsToolEnabled(ProbingType.EdgeFinderInternal);
    public bool CenterEnabled => IsToolEnabled(ProbingType.CenterFinder);
    public bool RotationEnabled => IsToolEnabled(ProbingType.Rotation);
    public bool HeightMapEnabled => IsToolEnabled(ProbingType.HeightMap);

    // ------------------------------------------------------ tool-specific input

    private Edge _probeEdge;
    public Edge ProbeEdge { get => _probeEdge; set => SetField(ref _probeEdge, value); }
    public DelegateCommand SelectEdgeCommand { get; private set; } = null!;

    private bool _probeZ;
    public bool ProbeZ { get => _probeZ; set => SetField(ref _probeZ, value); }

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
    public bool AddAction { get => _addAction; set => SetField(ref _addAction, value); }

    private bool _referenceToolOffset = true;
    public bool ReferenceToolOffset { get => _referenceToolOffset; set => SetField(ref _referenceToolOffset, value); }

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
    public bool HasHeightMap
    {
        get => HeightMapData is not null;
        private set => OnPropertyChanged(nameof(HasHeightMap));
    }

    // Coordinate writing: G10 (with system) / G92 / Measure.
    private CoordinateMode _coordMode = CoordinateMode.G10;
    public CoordinateMode CoordMode { get => _coordMode; set => SetField(ref _coordMode, value); }
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
    public DelegateCommand RotationApplyCommand { get; private set; } = null!;

    private void ApplyToolInstructions() => Instructions = SelectedTool switch
    {
        ProbingType.ToolLength => "Probe the touch plate/fixture to set the tool length offset.",
        ProbingType.EdgeFinderExternal or ProbingType.EdgeFinderInternal =>
            "Click an edge, corner or Z in the diagram to select the probing action.\nMove the probe above the indicated position before Start.",
        ProbingType.CenterFinder =>
            "Place the probe above the approximate center of the workpiece before Start.\nSelect inside or outside probing first.",
        ProbingType.Rotation =>
            "Select an edge to probe two points along, then Start to measure the angle.",
        ProbingType.HeightMap =>
            "Set the map area and grid size, then Start to probe the grid.",
        _ => string.Empty,
    };

    // -------------------------------------------------------------- execution

    public async Task StartAsync()
    {
        if (IsRunning)
            return;

        IsConnected = _grbl.IsConnected;
        if (!IsConnected)
        {
            LastResult = "Not connected - connect first (or use Demo mode).";
            return;
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;
        LastResult = string.Empty;

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
            LastResult = "Probing cancelled";
        }
        catch (Exception ex)
        {
            LastResult = $"Probing failed: {ex.Message}";
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

    private async Task<ProbeRunResult> RunAsync(ProbeProgramBuilder builder)
    {
        var result = await _runner.ExecuteAsync(builder.Lines, _cts!.Token);
        if (!result.Success)
            LastResult = $"Probing failed: {result.Error}";
        return result;
    }

    private async Task RunToolLengthAsync(ProbeProgramBuilder builder, Pos start)
    {
        builder.BuildToolLength(probeFixture: ProbeFixture);
        var result = await RunAsync(builder);
        if (!result.Success || result.Positions.Count != 1)
        {
            LastResult = "Probing failed - no probe position captured";
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
                LastResult = $"Tool length offset set: {cmd}";
            }
            else
            {
                LastResult = $"Measured Z: {ProbeMath.Fmt(touch.Z)}";
            }
        }
        else
        {
            var cmd = $"G43.1Z{ProbeMath.Fmt(touch.Z)}";
            await _grbl.SendAsync(cmd);
            LastResult = $"Tool length offset set: {cmd}";
        }
    }

    private async Task RunEdgeFinderAsync(ProbeProgramBuilder builder, Pos start, bool internalFinder)
    {
        if (ProbeEdge == Edge.None)
        {
            LastResult = "Select an edge or corner first";
            return;
        }

        builder.BuildEdgeFinder(ProbeEdge, start, internalFinder);
        var result = await RunAsync(builder);
        if (!result.Success || result.Positions.Count == 0)
        {
            LastResult = "Probing failed - no probe position captured";
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

        ApplyEdgeOffset(contact, internalFinder);
    }

    private void ApplyEdgeOffset(Pos contact, bool internalFinder)
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
            LastResult = $"Measured: X{ProbeMath.Fmt(contact.X)} Y{ProbeMath.Fmt(contact.Y)} Z{ProbeMath.Fmt(contact.Z)}";
            return;
        }

        _ = _grbl.SendAsync(cmd);
        LastResult = $"Probing completed: {cmd}";
    }

    private async Task RunCenterFinderAsync(ProbeProgramBuilder builder, Pos start)
    {
        if (ProbeCenter == Center.None)
        {
            LastResult = "Select inside or outside probing first";
            return;
        }
        if (WorkpieceSizeX <= 0 && WorkpieceSizeY <= 0)
        {
            LastResult = "Workpiece size must be set";
            return;
        }

        var current = start;
        for (var pass = 0; pass < Passes; pass++)
        {
            builder.BuildCenterFinder(ProbeCenter, current, WorkpieceSizeX, WorkpieceSizeY);
            var result = await RunAsync(builder);
            if (!result.Success)
                return;

            var expected = (WorkpieceSizeY > 0 && WorkpieceSizeX > 0) ? 4 : 2;
            if (result.Positions.Count < expected)
            {
                LastResult = "Probing failed - not enough probe positions";
                return;
            }

            var probes = result.Positions.Select(Pos.From).ToArray();
            var (center, xd, yd) = ProbeMath.ComputeCenter(ProbeCenter, current, probes, ProbeDiameter, WorkpieceSizeX, WorkpieceSizeY);
            current = center;

            if (pass == Passes - 1)
            {
                // Move to center, then write the offset.
                await _grbl.SendAsync($"G53G0X{ProbeMath.Fmt(center.X)}Y{ProbeMath.Fmt(center.Y)}");
                ApplyCenterOffset(center, xd, yd);
            }
        }
    }

    private void ApplyCenterOffset(Pos center, double xd, double yd)
    {
        if (CoordMode == CoordinateMode.Measure)
        {
            LastResult = $"Probing completed: X distance {ProbeMath.Fmt(xd)}, Y distance {ProbeMath.Fmt(yd)}";
            return;
        }

        var axes = WorkpieceSizeY > 0 && WorkpieceSizeX > 0 ? "XY" : (WorkpieceSizeX > 0 ? "Y" : "X");
        var pos = CoordMode == CoordinateMode.G92
            ? new Pos(ProbeOffsetX, ProbeOffsetY, 0)
            : new Pos(center.X + ProbeOffsetX, center.Y + ProbeOffsetY, 0);

        var cmd = ProbeMath.OffsetCommand(CoordMode, CoordinateSystem, pos, axes);
        if (!string.IsNullOrEmpty(cmd))
        {
            _ = _grbl.SendAsync(cmd);
            LastResult = $"Probing completed: X distance {ProbeMath.Fmt(xd)}, Y distance {ProbeMath.Fmt(yd)} -> {cmd}";
        }
    }

    private async Task RunRotationAsync(ProbeProgramBuilder builder, Pos start)
    {
        if (ProbeEdge is not (Edge.AD or Edge.AB or Edge.CB or Edge.CD))
        {
            LastResult = "Select an edge for rotation probing";
            return;
        }

        builder.BuildRotation(ProbeEdge, start);
        var result = await RunAsync(builder);
        if (!result.Success || result.Positions.Count < 2)
        {
            LastResult = "Probing failed - two probe positions required";
            return;
        }

        var p1 = Pos.From(result.Positions[^2]);
        var p2 = Pos.From(result.Positions[^1]);
        var angle = ProbeMath.ComputeRotationAngle(ProbeEdge, p1, p2);
        _probedAngle = angle;
        LastResult = $"Probed angle: {ProbeMath.ToDegrees(angle)}°";
    }

    private double _probedAngle;

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
            LastResult = $"Probing failed - expected {map.TotalPoints} positions, got {result.Positions.Count}";
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
        LastResult = $"Probing completed: Z range {ProbeMath.Fmt(zMin)} to {ProbeMath.Fmt(zMax)}";
    }

    // ------------------------------------------------------ height map file io

    private async Task SaveHeightMapAsync()
    {
        if (HeightMapData is null)
        {
            LastResult = "No height map to save";
            return;
        }

        // Prefer a native save dialog; fall back to the settings dir when no UI is available.
        var dialogs = _services?.GetService(typeof(IFileDialogService)) as IFileDialogService;
        var path = dialogs is null
            ? Path.Combine(SettingsService.DefaultDir(), "heightmap.map")
            : await dialogs.SaveFileAsync("Save height map", "heightmap.map", "Height map (*.map)|*.map");
        if (string.IsNullOrEmpty(path))
            return; // cancelled

        try
        {
            HeightMapData.Save(path);
            LastResult = $"Height map saved to {path}";
        }
        catch (Exception ex)
        {
            LastResult = $"Save failed: {ex.Message}";
        }
    }

    private async Task LoadHeightMapAsync()
    {
        // Prefer a native open dialog; fall back to the settings dir when no UI is available.
        var dialogs = _services?.GetService(typeof(IFileDialogService)) as IFileDialogService;
        var path = dialogs is null
            ? Path.Combine(SettingsService.DefaultDir(), "heightmap.map")
            : await dialogs.OpenFileAsync("Load height map", "Height map (*.map)|*.map");
        if (string.IsNullOrEmpty(path))
            return; // cancelled

        try
        {
            if (!File.Exists(path))
            {
                LastResult = $"No height map at {path}";
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
            LastResult = $"Height map loaded from {path}";
        }
        catch (Exception ex)
        {
            LastResult = $"Load failed: {ex.Message}";
        }
    }

    private void ApplyHeightMapToProgram()
    {
        if (HeightMapData is null || _services is not { } services)
        {
            LastResult = "Probe or load a height map first";
            return;
        }
        var program = services.GetService(typeof(ProgramViewModel)) as ProgramViewModel;
        if (program is null || string.IsNullOrEmpty(program.LoadedText))
        {
            LastResult = "No program loaded";
            return;
        }

        var transformed = ProgramTransform.ApplyHeightMap(program.LoadedText, HeightMapData);
        program.LoadGCode(transformed);
        LastResult = "Height map applied to program";
    }

    private void ApplyRotationToProgram()
    {
        if (Math.Abs(_probedAngle) < 0.0001 || _services is not { } services)
        {
            LastResult = "Probe an edge angle first";
            return;
        }
        var program = services.GetService(typeof(ProgramViewModel)) as ProgramViewModel;
        if (program is null || string.IsNullOrEmpty(program.LoadedText))
        {
            LastResult = "No program loaded";
            return;
        }

        var transformed = ProgramTransform.Rotate(program.LoadedText, _probedAngle, 0, 0);
        program.LoadGCode(transformed);
        LastResult = "Program rotated";
    }

    // ----------------------------------------------------------------- helpers

    private string? ReadString(string key)
    {
        if (_appSettings.Modules.TryGetValue("Probing", out var raw) && raw is Dictionary<string, object> section &&
            section.TryGetValue(key, out var value) && value is string s)
            return s;
        return null;
    }

    private Dictionary<string, bool> ReadEnabledTools()
    {
        var result = ProbingModule.ToolKeys.ToDictionary(k => k, _ => true);
        if (_appSettings.Modules.TryGetValue("Probing", out var raw) && raw is Dictionary<string, object> section &&
            section.TryGetValue(ProbingModule.EnabledToolsKey, out var rawTools) && rawTools is Dictionary<string, object> dict)
        {
            foreach (var key in ProbingModule.ToolKeys)
                if (dict.TryGetValue(key, out var v) && v is bool b)
                    result[key] = b;
        }
        return result;
    }
}
