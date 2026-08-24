using System.Windows;
using AI.Document.Converter.Wpf.Views;

namespace AI.Document.Converter.Wpf;

public partial class MainWindow : Window
{
    public MainWindow(SettingsView settingsView)
    {
        InitializeComponent();
        RootContent.Children.Add(settingsView);
    }
}
