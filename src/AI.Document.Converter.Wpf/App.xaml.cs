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

        // FR-031 last-resort safety net: every failure point ConversionService/
        // BatchService/PythonEngineClient know about already returns a safe,
        // categorized message (FR-029) rather than throwing - these three
        // handlers only catch a genuine, unanticipated bug elsewhere (a XAML
        // binding, an import-path edge case, ...) so one never reaches the
        // user as a raw stack trace or crash dialog.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

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

    // FR-031: the full exception (with stack trace) goes to the log only -
    // the MessageBox text is always this one fixed, generic sentence, never
    // ex.Message/ex.ToString(), so an exception message that happens to
    // contain a file path or library internals can't leak into the UI here.
    private const string UnexpectedErrorMessage =
        "An unexpected error occurred. You can keep working, but if this keeps happening, " +
        "please restart the application and check the log files.";

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled exception on the UI thread");
        MessageBox.Show(UnexpectedErrorMessage, "AI Document Converter", MessageBoxButton.OK, MessageBoxImage.Error);

        // The exception interrupted one command/event handler, not global
        // application state (BR-006's "one failure doesn't stop everything"
        // spirit) - marking it handled keeps the rest of the session usable
        // instead of force-closing and losing the user's imported batch.
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Raised off the UI thread and always fatal (the CLR is already
        // terminating the process by the time this fires) - log only, no
        // MessageBox: WPF's dispatcher isn't guaranteed to still be pumping
        // messages at this point.
        Log.Error(e.ExceptionObject as Exception, "Unhandled exception outside the UI thread");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved exception from a background Task");
        e.SetObserved();
    }
}
