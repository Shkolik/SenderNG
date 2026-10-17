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
using SenderNG.Core.Localization;

namespace CNC.Core.Services;

/// <summary>Built-in machine-type preset ids. The catalog grows with plugin
/// machine types later; the migration and the profile manager only need ids.</summary>
public static class MachineTypes
{
    public const string Mill = "Mill";
    public const string Foam = "Foam";

    /// <summary>The built-ins in offer order — what the profile manager lists today.
    /// Plugin-provided types join the catalog later; every list that offers types
    /// reads this, so adding one is a single-place change.</summary>
    public static IReadOnlyList<string> BuiltIn { get; } = [Mill, Foam];

    /// <summary>The localized display name of a type id — the profile badge, both
    /// type pickers and the machine card all wear it. The id stays the stored truth;
    /// the English fallbacks keep an unknown (plugin) id readable — as its own id
    /// when even the built-ins' naming rules do not apply.</summary>
    public static string DisplayName(string id)
    {
        var fallback = id switch
        {
            Mill => "Mill/Router",
            Foam => "Foam cutter/EDM",
            _ => id,
        };
        return LocalizationService.Current?.Resolve($"appSettings.mode.{id.ToLowerInvariant()}", fallback)
            ?? fallback;
    }
}

/// <summary>
/// One machine's persisted settings — the per-profile graph that replaces the
/// legacy <c>AppSettings</c> + per-mode <c>ModeSettings</c> pair. Stored one
/// file per profile (<c>profiles/&lt;id&gt;.json</c>).
/// <para>
/// <c>Id</c> is the stable slug used for the file name and as the active
/// profile pointer in <see cref="GlobalSettings"/> — renaming the display
/// <c>Name</c> never changes the id. <c>MachineTypeId</c> references the
/// machine-type preset (<see cref="MachineTypes"/> for built-ins; plugin ids
/// later) that supplies tool points and conventions.
/// </para>
/// </summary>
public class MachineProfile
{
    public const string DefaultId = "default";

    /// <summary>Stable slug; file name base (<c>profiles/&lt;id&gt;.json</c>); never changes on rename.</summary>
    public string Id { get; set; } = DefaultId;

    /// <summary>Display name shown in the profile manager (registry: id → name).</summary>
    public string Name { get; set; } = "Default";

    /// <summary>Reference to the machine-type preset (built-in Mill/Foam, later plugin ids).</summary>
    public string MachineTypeId { get; set; } = MachineTypes.Mill;

    /// <summary>Connection prefill + last-successful state for this machine.</summary>
    public MachineConnection Connection { get; set; } = new();

    /// <summary>Axis count the Demo controller simulates for this machine (was the per-mode DemoAxes).
    /// Also the lever for simulating a reduced board (invariant 10's superset tolerance).</summary>
    public int DemoAxes { get; set; } = 3;

    private List<ProfilePlane> _planes = [];
    private List<MachineAxis> _optionalAxes = [];

    /// <summary>
    /// This machine's planes, seeded from the preset's <c>BasePlanes</c> with the
    /// default conventional indices. Plane order is the machine's plane order and an
    /// axis's slot in a plane is its role (horizontal, vertical, optional depth); the
    /// axis <b>index</b> is the stored truth, so a plane's indices may be
    /// non-monotonic. Empty means "not migrated/seeded yet" — the resolver seeds it
    /// from the preset, so a profile file written before this field existed still
    /// resolves. Never null (a hand-edited <c>"Planes": null</c> reads as empty).
    /// </summary>
    public List<ProfilePlane> Planes
    {
        get => _planes;
        set => _planes = value ?? [];
    }

    /// <summary>Extra axes outside every plane — rotary axes always live here, plus any
    /// additional linear axis the machine carries. Never in a plane (invariants 3, 5).</summary>
    public List<MachineAxis> OptionalAxes
    {
        get => _optionalAxes;
        set => _optionalAxes = value ?? [];
    }

    /// <summary>Plane-to-plane distance in mm for a multi-plane (foam) machine — the one
    /// degree of freedom two parallel planes leave (their shared unspanned direction).
    /// Seeded from the preset's <c>DefaultSeparation</c>; single-plane machines ignore it.</summary>
    public double Separation { get; set; }

    /// <summary>Tool slots the Offsets/WorkParameters/Probe grids show (was the per-mode VisibleTools).</summary>
    public int VisibleTools { get; set; } = 8;

    /// <summary>Module visibility for this machine (was the per-mode ModulesVisibility).</summary>
    public Dictionary<string, bool> Modules { get; set; } = new();

    /// <summary>Per-profile module sections (was the shared AppSettings.Modules bag).</summary>
    public Dictionary<string, object> ModuleSettings { get; set; } = new();
}

/// <summary>Machine-coupled connection state (all profile tier — the old global
/// "last successful" fields were machine properties and move here).</summary>
public class MachineConnection
{
    /// <summary>Preferred port ("auto" = first usable) — legacy mode Port → here.</summary>
    public string SerialPort { get; set; } = "auto";

    public int SerialBaudRate { get; set; } = 115200;

    /// <summary>Serial "On connect" reset action (Arduino DTR vs ESP32 RTS) — board behavior.</summary>
    public ConnectResetMode SerialResetMode { get; set; }

    /// <summary>Last *successful* serial port, written only after a connect succeeds (prefill).</summary>
    public string LastSerialPort { get; set; } = string.Empty;

    public int LastSerialBaudRate { get; set; }

    /// <summary>Last *successful* grblHAL TCP endpoint (prefill).</summary>
    public string LastNetworkEndpoint { get; set; } = string.Empty;
}
