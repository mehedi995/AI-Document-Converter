using System.Windows.Controls;
using AI.Document.Converter.Wpf.ViewModels;

namespace AI.Document.Converter.Wpf.Views;

public partial class DashboardView : UserControl
{
    public DashboardView(DashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
