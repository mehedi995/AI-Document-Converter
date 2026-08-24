using System.Windows;

namespace AI.Document.Converter.Wpf.Views;

// FR-038: shown once at startup, before any import is possible, instead of every
// subsequent file failing individually with a confusing per-file error.
public partial class StartupErrorWindow : Window
{
    public string Message { get; }

    public StartupErrorWindow(string message)
    {
        Message = message;
        InitializeComponent();
        DataContext = this;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
