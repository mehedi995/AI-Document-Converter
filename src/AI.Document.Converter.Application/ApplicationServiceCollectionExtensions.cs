using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Document.Converter.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<SettingsService>();
        services.AddSingleton<IImportService, ImportService>();
        services.AddSingleton<IDocumentProcessorResolver, DocumentProcessorResolver>();
        services.AddSingleton<IMarkdownGenerator, MarkdownGenerator>();

        // IOutputPathResolver is deliberately NOT registered here - it is
        // stateful per batch (docs/15-IMPLEMENTATION-PLAN.md Section 4), so
        // whatever orchestrates a batch (Phase 8) constructs one instance per
        // batch run rather than resolving a container-managed lifetime.

        return services;
    }
}
