using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.ValueObjects;
using AI.Document.Converter.Infrastructure.Configuration;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Docx;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Excel;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Pdf;
using AI.Document.Converter.Infrastructure.DocumentProcessing.PowerPoint;
using AI.Document.Converter.Infrastructure.DocumentProcessing.Text;
using AI.Document.Converter.Infrastructure.FileSystem;
using AI.Document.Converter.Infrastructure.Python;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Infrastructure;

// Single composition point for everything this layer provides, so App.xaml.cs
// (docs/11-CODING-STANDARDS.md Section 9) never registers an Infrastructure type
// directly.
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        AppSettingsBootstrapper.EnsureSettingsFileExists();

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(AppPaths.SettingsFilePath, optional: true, reloadOnChange: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<AppSettings>(configuration);

        services.AddSingleton<IPathValidator, PathValidator>();
        services.AddSingleton<IFileSizeReader, FileSizeReader>();
        services.AddSingleton<IPythonPathValidator, PythonPathValidator>();
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(
            AppPaths.SettingsFilePath,
            sp.GetRequiredService<IOptionsMonitor<AppSettings>>(),
            sp.GetRequiredService<ILogger<JsonSettingsStore>>()));
        services.AddSingleton<IPythonEngineClient, PythonEngineClient>();

        // ADR-002: all five registered under the same interface; the resolver
        // (Application layer) picks the first one whose CanProcess matches.
        services.AddSingleton<IDocumentProcessor, TextDocumentProcessor>();
        services.AddSingleton<IDocumentProcessor, PdfDocumentProcessor>();
        services.AddSingleton<IDocumentProcessor, DocxDocumentProcessor>();
        services.AddSingleton<IDocumentProcessor, ExcelDocumentProcessor>();
        services.AddSingleton<IDocumentProcessor, PowerPointDocumentProcessor>();

        services.AddSingleton<ITokenEstimator, TokenEstimator>();
        services.AddSingleton<IMarkdownFileWriter, MarkdownFileWriter>();

        return services;
    }
}
