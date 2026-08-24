using System.Windows.Controls;
using AI.Document.Converter.Wpf.ViewModels;

namespace AI.Document.Converter.Wpf.Views;

// No business logic here (docs/50 MVVM rule) - construction just wires the
// ViewModel and kicks off the initial load.
public partial class SettingsView : UserControl
{
    public SettingsView(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }
}
