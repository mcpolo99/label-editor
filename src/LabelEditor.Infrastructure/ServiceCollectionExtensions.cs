using LabelEditor.Application.Abstractions;
using LabelEditor.Infrastructure.Ledit;
using LabelEditor.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabelEditor.Infrastructure;

/// <summary>Registers infrastructure adapters for the application ports.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLabelEditorInfrastructure(this IServiceCollection services, AppPaths paths, StorageOptions storage, LeditXmlStyle xmlStyle = LeditXmlStyle.Native)
    {
        services.AddSingleton(paths);
        services.AddSingleton(storage);
        services.AddSingleton<IStorageLocations, StorageLocations>();
        services.AddSingleton<ILabelLayoutFormat>(new LeditLabelFormat(xmlStyle));
        services.AddSingleton(sp => new FileLabelDocumentStore(sp.GetRequiredService<ILabelLayoutFormat>()));
        services.AddSingleton<ILabelDocumentStore>(sp => sp.GetRequiredService<FileLabelDocumentStore>());
        services.AddSingleton<ILabelVersionHistory>(sp => sp.GetRequiredService<FileLabelDocumentStore>());
        services.AddSingleton<ILabelLibrary, FileSystemLabelLibrary>();
        services.AddSingleton<IFieldCatalogSource, LeditFieldCatalogSource>();
        services.AddSingleton<ILanguageSource, LeditLanguageSource>();
        services.AddSingleton<Application.Preview.IPreviewDataSource, Preview.CutListPreviewDataSource>();
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(paths.SettingsFile, sp.GetRequiredService<ILogger<JsonSettingsStore>>()));
        return services;
    }
}
