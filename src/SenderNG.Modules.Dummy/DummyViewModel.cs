using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SenderNG.Modules.Dummy;

/// <summary>
/// UI-agnostic-ish view model: implements INotifyPropertyChanged directly so the
/// module project needs no dependency beyond Avalonia (required for CreateView).
/// </summary>
public class DummyViewModel : INotifyPropertyChanged
{
    private string _message = "Dummy plugin works!";
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
