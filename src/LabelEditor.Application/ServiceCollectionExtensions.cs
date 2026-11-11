using LabelEditor.Application.Documents;
using LabelEditor.Application.Editing;
using Microsoft.Extensions.DependencyInjection;

namespace LabelEditor.Application;

/// <summary>Registers application services.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLabelEditorApplication(this IServiceCollection services)
    {
        services.AddSingleton<DocumentService>();
        services.AddSingleton<ReferenceDataService>();
        services.AddSingleton<EntryClipboard>();
        return services;
    }
}
