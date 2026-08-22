using CNC.Core.Abstractions;
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
        set => SetField(ref _isFlood, value);
    }

    private bool _isMist;
    public bool IsMist
    {
        get => _isMist;
        set => SetField(ref _isMist, value);
    }

    public DelegateCommand FloodCommand { get; }
    public DelegateCommand MistCommand { get; }

    public CoolantViewModel(IGrblService grbl)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        FloodCommand = new DelegateCommand(_ => _ = ToggleFloodAsync());
        MistCommand = new DelegateCommand(_ => _ = ToggleMistAsync());
    }

    public async Task ToggleFloodAsync()
    {
        IsFlood = !IsFlood;
        await _grbl.SendAsync(IsFlood ? "M8" : "M9");
    }

    public async Task ToggleMistAsync()
    {
        IsMist = !IsMist;
        await _grbl.SendAsync(IsMist ? "M7" : "M9");
    }
}
