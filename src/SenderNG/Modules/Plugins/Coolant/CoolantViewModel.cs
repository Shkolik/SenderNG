
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

using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.ViewModels;

namespace SenderNG.Modules.Plugins;

public class CoolantViewModel : ViewModelBase
{
    private readonly IGrblService _grbl;

    private bool _isFlood;
    public bool IsFlood
    {
        get => _isFlood;
        private set => SetField(ref _isFlood, value);
    }

    private bool _isMist;
    public bool IsMist
    {
        get => _isMist;
        private set => SetField(ref _isMist, value);
    }

    // Capability-driven visibility (B.3): the toggle is hidden when the connected
    // firmware doesn't support the coolant mode. Unknown/defaults report available.
    public bool HasFlood => _grbl.Capabilities.CoolantFlood;
    public bool HasMist => _grbl.Capabilities.CoolantMist;

    public DelegateCommand ToggleFloodCommand { get; }
    public DelegateCommand ToggleMistCommand { get; }

    public CoolantViewModel(IGrblService grbl)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        ToggleFloodCommand = new DelegateCommand(_ => Toggle(GrblRealtime.CoolantFloodToggle));
        ToggleMistCommand = new DelegateCommand(_ => Toggle(GrblRealtime.CoolantMistToggle));

        // State mirrors the machine (override toggles are echoed back via State).
        grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));
    }

    private void Toggle(byte realtimeCode)
    {
        try
        {
            _grbl.SendRealtime(realtimeCode);
        }
        catch (Exception ex)
        {
            // Not connected etc. - state stays as reported by the machine.
            System.Diagnostics.Debug.WriteLine($"Coolant toggle failed: {ex.Message}");
        }
    }

    private void ApplyState(MachineState state)
    {
        if (state is null)
            return;
        IsFlood = state.Flood;
        IsMist = state.Mist;
    }
}
