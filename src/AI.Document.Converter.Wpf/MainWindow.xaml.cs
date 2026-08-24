using System.Windows;
using AI.Document.Converter.Wpf.Views;

namespace AI.Document.Converter.Wpf;

public partial class MainWindow : Window
{
    public MainWindow(DashboardView dashboardView, SettingsView settingsView)
    {
        InitializeComponent();
        DashboardHost.Content = dashboardView;
        SettingsHost.Content = settingsView;
    }
}
