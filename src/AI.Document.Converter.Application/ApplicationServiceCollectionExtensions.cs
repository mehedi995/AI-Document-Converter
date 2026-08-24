using AI.Document.Converter.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Document.Converter.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<SettingsService>();

        return services;
    }
}
