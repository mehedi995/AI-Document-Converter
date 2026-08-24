using System.Windows;
using AI.Document.Converter.Application;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Infrastructure;
using AI.Document.Converter.Infrastructure.Configuration;
using AI.Document.Converter.Infrastructure.Logging;
using AI.Document.Converter.Wpf.ViewModels;
using AI.Document.Converter.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AI.Document.Converter.Wpf;

// Fully qualified because this file also imports the AI.Document.Converter.Application
// namespace (for AddApplication()), which would otherwise shadow System.Windows.Application.
public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Bootstrap order matters: the settings file must exist before Serilog
        // reads a log directory from it, and Serilog must be configured before
        // the DI container (whose services log through it) is built.
        AppSettingsBootstrapper.EnsureSettingsFileExists();
        SerilogConfigurator.Configure(AppSettingsBootstrapper.ReadLogDirectoryForBootstrap());

        Log.Information("Application starting");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(dispose: true));
        services.AddInfrastructure();
        services.AddApplication();
        RegisterPresentation(services);

        _serviceProvider = services.BuildServiceProvider();

        var startedCleanly = await RunStartupHealthCheckAsync(_serviceProvider);
        if (!startedCleanly)
        {
            Shutdown();
            return;
        }

        Log.Information("Application started");

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application shutting down");
        _serviceProvider?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void RegisterPresentation(IServiceCollection services)
    {
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsView>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<DashboardView>();
        services.AddTransient<MainWindow>();
    }

    // FR-038: verified once, before any window that could let the user attempt an
    // import is shown.
    private static async Task<bool> RunStartupHealthCheckAsync(IServiceProvider serviceProvider)
    {
        var pythonEngineClient = serviceProvider.GetRequiredService<IPythonEngineClient>();
        var logger = serviceProvider.GetRequiredService<ILogger<App>>();

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var isHealthy = await pythonEngineClient.CheckHealthAsync(timeoutCts.Token);

            if (isHealthy)
            {
                return true;
            }

            logger.LogError("Python engine health check reported an unhealthy engine at startup");
            ShowStartupError(
                "The document-processing engine could not be verified. " +
                "Check the Python engine path in Settings, then restart the application.");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Python engine health check threw an exception at startup");
            ShowStartupError(
                "The document-processing engine could not be started. " +
                "Check the Python engine path in Settings, then restart the application.");
            return false;
        }
    }

    private static void ShowStartupError(string message)
    {
        var window = new StartupErrorWindow(message);
        window.ShowDialog();
    }
}
