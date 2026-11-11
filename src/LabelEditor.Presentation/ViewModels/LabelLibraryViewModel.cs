using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelEditor.Application.Abstractions;

namespace LabelEditor.Presentation.ViewModels;

/// <summary>A group of labels from one source folder.</summary>
public sealed record LabelLibraryGroup(string Title, string Folder, IReadOnlyList<LabelLibraryItem> Items);

/// <summary>Browses labels in the working folder and the installed LEdit.</summary>
public sealed partial class LabelLibraryViewModel(ILabelLibrary library, IStorageLocations locations) : ObservableObject
{
    public ObservableCollection<LabelLibraryGroup> Groups { get; } = [];

    public string DataSource => locations.IsMachineInstallation
        ? $"LEdit installation: {locations.DataRoot}"
        : "No LEdit installation found: the field tree is empty. Use File ▸ Choose LEdit folder… to point the editor at one.";

    [ObservableProperty]
    private bool _isLoading;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var items = await library.GetItemsAsync();
            Groups.Clear();
            foreach (var folder in locations.LabelFolders)
            {
                var title = folder.Kind switch
                {
                    LabelSourceKind.Working => "My labels",
                    LabelSourceKind.Machine => folder.IsReadOnly ? "Machine (read-only)" : "Machine",
                    _ => "Templates",
                };
                Groups.Add(new LabelLibraryGroup(title, folder.Path, items.Where(i => i.Source == folder.Kind).ToList()));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}
