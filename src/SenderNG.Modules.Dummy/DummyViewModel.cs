using System.ComponentModel;
using System.Runtime.CompilerServices;
using SenderNG.Core.Localization;

namespace SenderNG.Modules.Dummy;

/// <summary>
/// UI-agnostic-ish view model: implements INotifyPropertyChanged directly so the
/// module project needs no dependency beyond Avalonia (required for CreateView).
/// Strings resolve through the app's <see cref="LocalizationService"/> from the
/// plugin's OWN Locale catalog (registered under <c>Modules/dummy/Locale</c>), and
/// re-render live on locale change.
/// </summary>
public class DummyViewModel : INotifyPropertyChanged
{
    private readonly LocalizationService _localization;
    private string _message;

    public DummyViewModel(LocalizationService localization)
    {
        _localization = localization;
        _message = localization.T("module.dummy.tagline");
        localization.Changed += (_, _) =>
        {
            Message = localization.T("module.dummy.tagline");
        };
    }

    public string Message
    {
        get => _message;
        set
        {
            if (_message != value)
            {
                _message = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
