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

using System.Text;
using CNC.Core.Abstractions;

namespace CNC.Core.Services;

/// <summary>
/// The one place the app resolves machine kinematics. Modules (jog, plotter,
/// outline, DRO/limits) read <see cref="Current"/> instead of each calling
/// <see cref="MachineTypeCatalog.Resolve"/> at their own trigger point — which is
/// also what makes a connect's <c>[AXS:]</c> letters and reported axis count reach
/// every consumer at once.
/// </summary>
/// <remarks>
/// <see cref="Current"/> merges <b>stored</b> profile state with <b>live</b>
/// controller state, so it is re-resolved on connect / disconnect and on a profile
/// switch. <see cref="Refresh"/> is the explicit hook for a caller that changed the
/// active profile's axis layout (the profile editor's commit path). Register as a
/// singleton and resolve it from DI.
/// </remarks>
public interface IMachineKinematicsProvider
{
    /// <summary>The resolved kinematics for the active profile and the current
    /// connection. Never null; resolves on first access.</summary>
    MachineKinematics Current { get; }

    /// <summary>Raised whenever <see cref="Current"/> was replaced — a profile switch,
    /// a connect/disconnect that changed the letters or count, or a
    /// <see cref="Refresh"/>. Raised on the thread that triggered it: a UI consumer
    /// must marshal to the UI thread.</summary>
    event EventHandler? Changed;

    /// <summary>Re-resolve <see cref="Current"/> from the active profile and the
    /// controller now. Call it after committing axis edits to the active profile; it
    /// raises <see cref="Changed"/> only when something actually changed, so it is
    /// cheap to call from the settings editor.</summary>
    void Refresh();
}

/// <summary>
/// Default <see cref="IMachineKinematicsProvider"/>: resolves through
/// <see cref="MachineKinematicsResolver"/> from <see cref="SettingsService"/>'s
/// active profile plus the live <see cref="IGrblService"/> capabilities.
/// <para>
/// It subscribes to the events that already exist rather than inventing a bus:
/// <see cref="IGrblService.ConnectionChanged"/> (the <c>[AXS:]</c> letters and the
/// reported count land on connect / disconnect / an inner swap),
/// <see cref="SettingsService.ProfileSwitched"/> (the active machine changed) and
/// <see cref="SettingsService.Changed"/> (a save may have committed an axis edit or a
/// machine-type change). Re-resolving is a pure in-memory pass over a handful of
/// axes, and a re-resolve that yields the same shape keeps the existing instance and
/// raises nothing, so an unrelated settings save costs a comparison and no repaint.
/// </para>
/// </summary>
public sealed class MachineKinematicsProvider : IMachineKinematicsProvider, IDisposable
{
    private readonly SettingsService _settings;
    private readonly IGrblService _grbl;
    private readonly object _gate = new();
    private MachineKinematics? _current;
    private string? _signature;
    private bool _disposed;

    public MachineKinematicsProvider(SettingsService settings, IGrblService grbl)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _grbl.ConnectionChanged += OnConnectionChanged;
        _settings.ProfileSwitched += OnProfileSwitched;
        _settings.Changed += OnSettingsChanged;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public MachineKinematics Current
    {
        get
        {
            lock (_gate)
            {
                if (_current is null)
                {
                    _current = Resolve();
                    _signature = SignatureOf(_current);
                }
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public void Refresh() => Republish();

    private void OnConnectionChanged(object? sender, EventArgs e) => Republish();

    private void OnProfileSwitched(string profileId) => Republish();

    private void OnSettingsChanged() => Republish();

    /// <summary>Re-resolve; publish (and raise) only when the resolved shape differs
    /// from what consumers already hold.</summary>
    private void Republish()
    {
        MachineKinematics resolved;
        lock (_gate)
        {
            if (_disposed)
                return;
            resolved = Resolve();
            var signature = SignatureOf(resolved);
            if (_current is not null && signature == _signature)
                return;
            _current = resolved;
            _signature = signature;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private MachineKinematics Resolve()
    {
        var profile = _settings.Load();
        var capabilities = _grbl.Capabilities;
        // 0 = no controller: an unconnected machine reports nothing, so consumers
        // treat every declared axis as absent (invariant 10).
        var reportedAxes = _grbl.IsConnected ? capabilities.NumAxes : 0;
        return MachineKinematicsResolver.Resolve(profile, capabilities.AxisLetters, reportedAxes);
    }

    /// <summary>Cheap structural fingerprint of a resolution: the profile identity
    /// (a switch must publish even between two identically-shaped machines), the
    /// preset, the board letters, the reported count and every axis binding. Numbers
    /// are formatted invariantly: the app can switch locale at runtime and a culture
    /// change must not look like a geometry change.</summary>
    private static string SignatureOf(MachineKinematics kinematics)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.Append(kinematics.ProfileId).Append('|')
            .Append(kinematics.MachineTypeId).Append('|')
            .Append(kinematics.BoardLetters).Append('|')
            .Append(kinematics.ReportedAxes.ToString(culture)).Append('|');

        foreach (var plane in kinematics.Planes)
        {
            builder.Append('[');
            foreach (var axis in plane.Axes)
                AppendAxis(builder, axis, culture);
            builder.Append("]@").Append(plane.Separation.ToString(culture)).Append(';');
        }

        builder.Append('/');
        foreach (var axis in kinematics.OptionalAxes)
            AppendAxis(builder, axis, culture);

        return builder.ToString();
    }

    private static void AppendAxis(StringBuilder builder, MachineAxis axis,
        System.Globalization.CultureInfo culture) =>
        builder.Append(axis.Index.ToString(culture)).Append(':')
            .Append(axis.Kind).Append(':')
            .Append(axis.Direction).Append(':')
            .Append(axis.Sign.ToString(culture)).Append(':')
            .Append(axis.Alias?.ToString() ?? "-").Append(':')
            .Append(axis.RotationOrigin is null
                ? "-"
                : string.Join(',', axis.RotationOrigin.Select(v => v.ToString(culture))))
            .Append(',');

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _grbl.ConnectionChanged -= OnConnectionChanged;
        _settings.ProfileSwitched -= OnProfileSwitched;
        _settings.Changed -= OnSettingsChanged;
    }
}
