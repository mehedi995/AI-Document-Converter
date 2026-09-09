using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Docx;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Excel;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Pdf;
using AI.Document.Converter.Infrastructure.DocumentProcessing.PowerPoint;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Text;
using AI.Document.Converter.Infrastructure.Python;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Jobs;
using AI.Document.Converter.Persistence.Retention;
using AI.Document.Converter.Persistence.Storage;
using AI.Document.Converter.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Same secret store as the web host, so local development needs one connection
// string rather than two that can drift apart.
builder.Configuration.AddUserSecrets<Program>(optional: true);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "No 'ConnectionStrings:Default' configured. See docs/saas/03-DEVELOPMENT-SETUP.md.");

builder.Services.AddDbContext<ConverterDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.Configure<LocalFileSystemObjectStorageOptions>(
    builder.Configuration.GetSection("ObjectStorage"));
builder.Services.AddSingleton<IObjectStorage, LocalFileSystemObjectStorage>();

builder.Services.Configure<ConversionWorkerOptions>(builder.Configuration.GetSection("Worker"));

// The engine path is SERVER configuration, never a customer setting.
//
// Audit A-04: on the desktop, AppSettings.PythonExecutablePath is user-editable,
// which is harmless for an app running as that user and is arbitrary-code
// execution on a shared host. The desktop's PythonEngineClient is reused for its
// tested behaviour - no shell, no arguments, deadlock-safe concurrent stream
// reads - but it is fed a fixed options value from server configuration rather
// than from anything a tenant can influence.
builder.Services.AddSingleton<IOptionsMonitor<AppSettings>>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var enginePath = configuration["Engine:ExecutablePath"]
        ?? throw new InvalidOperationException(
            "No 'Engine:ExecutablePath' configured. The worker cannot convert anything without "
            + "the extraction engine. See docs/saas/03-DEVELOPMENT-SETUP.md.");

    var timeoutSeconds = configuration.GetValue("Engine:TimeoutSeconds", 120);

    return new FixedOptionsMonitor<AppSettings>(new AppSettings
    {
        PythonExecutablePath = enginePath,
        PythonEngineTimeoutSeconds = timeoutSeconds
    });
});

builder.Services.AddSingleton<IPythonEngineClient, PythonEngineClient>();

// The real extraction pipeline, shared with the desktop application. Registered
// individually rather than via AddApplication(), which also pulls in
// SettingsService and the desktop's JSON settings file - neither belongs on a
// server.
builder.Services.AddSingleton<IDocumentProcessor, PdfDocumentProcessor>();
builder.Services.AddSingleton<IDocumentProcessor, DocxDocumentProcessor>();
builder.Services.AddSingleton<IDocumentProcessor, ExcelDocumentProcessor>();
builder.Services.AddSingleton<IDocumentProcessor, PowerPointDocumentProcessor>();
builder.Services.AddSingleton<IDocumentProcessor, TextDocumentProcessor>();
builder.Services.AddSingleton<IDocumentProcessorResolver, DocumentProcessorResolver>();
builder.Services.AddSingleton<IMarkdownGenerator, MarkdownGenerator>();

builder.Services.Configure<RetentionPolicy>(builder.Configuration.GetSection("Retention"));
builder.Services.AddScoped<RetentionService>();
builder.Services.AddScoped<JobLifecycleService>();
builder.Services.AddHostedService<RetentionSweepService>();

builder.Services.AddScoped<JobClaimer>();
builder.Services.AddScoped<JobProcessor>();
builder.Services.AddHostedService<ConversionWorkerService>();

var host = builder.Build();
await host.RunAsync();

// The desktop uses IOptionsMonitor so a user can change settings while the app
// runs. Server configuration does not change under us, so this just satisfies
// the interface with a value fixed at startup.
internal sealed class FixedOptionsMonitor<T> : IOptionsMonitor<T>
{
    public FixedOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
