using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AI.Document.Converter.Wpf.ViewModels;

// Deliberately hand-rolled rather than pulled from a third-party MVVM toolkit
// (docs/11-CODING-STANDARDS.md: prefer explicit code a mid-level developer can
// read line-by-line over an added abstraction).
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
