
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

using System.Reactive.Subjects;
using CNC.Core.Abstractions;

namespace CNC.Core.Services;

/// <summary>
/// Stable IGrblService singleton that forwards to a swappable inner service.
/// Consumers subscribe once and keep receiving state across demo/real swaps;
/// the proxy re-subscribes its relay subject whenever <see cref="Inner"/> changes.
/// </summary>
public class GrblServiceProxy : IGrblService, IGrblSettingsProvider
{
    private IGrblService _inner;
    private IDisposable? _subscription;
    private IDisposable? _linesSubscription;
    private EventHandler? _innerConnectionHandler;
    private EventHandler? _connectionChanged;
    private readonly BehaviorSubject<MachineState> _subject = new(new MachineState());
    // Bounded replay so the console realizes late (center-panel tabs are built
    // lazily by Avalonia from a deferred enumeration) still sees the connect
    // burst — the $I identity/options lines and recent status — instead of an
    // empty log. The demo service publishes no raw lines, so the buffer is empty
    // in demo mode (the console stays silent as intended).
    private readonly ReplaySubject<string> _receivedLines = new(bufferSize: 50);
    private readonly BehaviorSubject<WorkParameters> _workParameters =
        new(WorkParameters.CreateDefault(3));
    private IDisposable? _workParametersSubscription;

    public GrblServiceProxy(IGrblService inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        AttachInner(inner);
    }

    /// <summary>
    /// Forwards the inner service's connection events and also fires on every
    /// inner swap (the new inner may already be connected). Subscribers survive
    /// demo/real/network transitions, like the state relay.
    /// </summary>
    public event EventHandler? ConnectionChanged
    {
        add => _connectionChanged += value;
        remove => _connectionChanged -= value;
    }

    public IGrblService Inner
    {
        get => _inner;
        set
        {
            // Repoint BEFORE relaying: AttachInner subscribes to the new inner's
            // subjects, and a BehaviorSubject replays its current value
            // synchronously — consumers reading ConnectionId/ControllerKey (e.g.
            // the offsets editor scoping tool names per controller) must see the
            // NEW controller on that first snapshot, not the stale one.
            _inner = value;
            AttachInner(value);
        }
    }

    public bool IsConnected => _inner.IsConnected;

    public string ConnectionId => _inner.ConnectionId;

    public string ControllerKey => _inner.ControllerKey;

    public ControllerCapabilities Capabilities => _inner.Capabilities;

    public WorkParameters WorkParameters => _workParameters.Value;

    public IObservable<WorkParameters> WorkParametersChanges => _workParameters;

    // BehaviorSubject: replays the current machine state, so subscribers that
    // attach after construction (widgets, MainViewModel) immediately see the
    // inner service's latest state instead of waiting for the next emission.
    // The relay subscription in SetInner feeds the inner's replayed value
    // through here on attach and on every demo/real swap.
    public IObservable<MachineState> State => _subject;

    // Raw controller output is relayed the same way as state, so console
    // subscribers keep receiving lines across demo/real/network inner swaps.
    public IObservable<string> ReceivedLines => _receivedLines;

    public Task ConnectAsync(string port) => _inner.ConnectAsync(port);

    public void Disconnect() => _inner.Disconnect();

    public Task RefreshWorkParametersAsync() => _inner.RefreshWorkParametersAsync();

    public Task SendAsync(string line) => _inner.SendAsync(line);

    public void SendRealtime(byte code) => _inner.SendRealtime(code);

    public Task<IReadOnlyList<string>> GetSettingLinesAsync() =>
        _inner is IGrblSettingsProvider provider
            ? provider.GetSettingLinesAsync()
            : throw new NotSupportedException("The active machine does not support settings.");

    public Task<IReadOnlyList<string>> GetSettingDetailLinesAsync() =>
        _inner is IGrblSettingsProvider p ? p.GetSettingDetailLinesAsync() : EmptyLines;

    public Task<IReadOnlyList<string>> GetSettingGroupLinesAsync() =>
        _inner is IGrblSettingsProvider p ? p.GetSettingGroupLinesAsync() : EmptyLines;

    public Task<string?> GetSettingDescriptionAsync(int id) =>
        _inner is IGrblSettingsProvider p ? p.GetSettingDescriptionAsync(id) : Task.FromResult<string?>(null);

    private static readonly Task<IReadOnlyList<string>> EmptyLines =
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    private void AttachInner(IGrblService value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _subscription?.Dispose();
        _subscription = value.State.Subscribe(_subject.OnNext);
        _linesSubscription?.Dispose();
        _linesSubscription = value.ReceivedLines.Subscribe(_receivedLines.OnNext);

        _workParametersSubscription?.Dispose();
        _workParametersSubscription = value.WorkParametersChanges.Subscribe(
            _workParameters.OnNext);

        if (_innerConnectionHandler is not null)
            _inner.ConnectionChanged -= _innerConnectionHandler;
        _innerConnectionHandler = (_, _) => _connectionChanged?.Invoke(this, EventArgs.Empty);
        value.ConnectionChanged += _innerConnectionHandler;

        // The new inner may already be connected/disconnected; tell subscribers
        // so e.g. the state indicator updates without waiting for a state event.
        _connectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
