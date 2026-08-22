using CNC.Core.Abstractions;
using SenderNG.Core.Commands;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Avalonia.Modules.Plugins;

public class CuttingViewModel : ViewModelBase
{
    private const int Min = 10;
    private const int Max = 200;

    private readonly IGrblService _grbl;

    private int _feedOverride = 100;
    public int FeedOverride
    {
        get => _feedOverride;
        set => SetField(ref _feedOverride, Math.Clamp(value, Min, Max));
    }

    private int _spindleOverride = 100;
    public int SpindleOverride
    {
        get => _spindleOverride;
        set => SetField(ref _spindleOverride, Math.Clamp(value, Min, Max));
    }

    public DelegateCommand FeedUpCommand { get; }
    public DelegateCommand FeedDownCommand { get; }
    public DelegateCommand FeedUp10Command { get; }
    public DelegateCommand FeedDown10Command { get; }
    public DelegateCommand SpindleUpCommand { get; }
    public DelegateCommand SpindleDownCommand { get; }
    public DelegateCommand SpindleUp10Command { get; }
    public DelegateCommand SpindleDown10Command { get; }

    public CuttingViewModel(IGrblService grbl)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));

        FeedUpCommand = new DelegateCommand(_ => _ = AdjustFeedAsync(+1));
        FeedDownCommand = new DelegateCommand(_ => _ = AdjustFeedAsync(-1));
        FeedUp10Command = new DelegateCommand(_ => _ = AdjustFeedAsync(+10));
        FeedDown10Command = new DelegateCommand(_ => _ = AdjustFeedAsync(-10));
        SpindleUpCommand = new DelegateCommand(_ => _ = AdjustSpindleAsync(+1));
        SpindleDownCommand = new DelegateCommand(_ => _ = AdjustSpindleAsync(-1));
        SpindleUp10Command = new DelegateCommand(_ => _ = AdjustSpindleAsync(+10));
        SpindleDown10Command = new DelegateCommand(_ => _ = AdjustSpindleAsync(-10));
    }

    public async Task AdjustFeedAsync(int delta)
    {
        FeedOverride += delta;
        await _grbl.SendAsync($"$FV={FeedOverride}");
    }

    public async Task AdjustSpindleAsync(int delta)
    {
        SpindleOverride += delta;
        await _grbl.SendAsync($"$SV={SpindleOverride}");
    }
}
