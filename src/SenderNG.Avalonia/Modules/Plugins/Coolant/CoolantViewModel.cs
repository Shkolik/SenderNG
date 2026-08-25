using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Avalonia.Modules.Plugins;

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

    public DelegateCommand ToggleFloodCommand { get; }
    public DelegateCommand ToggleMistCommand { get; }

    public CoolantViewModel(IGrblService grbl)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        ToggleFloodCommand = new DelegateCommand(_ => Toggle(GrblRealtime.CoolantFloodToggle));
        ToggleMistCommand = new DelegateCommand(_ => Toggle(GrblRealtime.CoolantMistToggle));

        // State mirrors the machine (override toggles are echoed back via State).
        grbl.State.Subscribe(ApplyState);
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
