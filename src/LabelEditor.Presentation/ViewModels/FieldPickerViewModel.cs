using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LabelEditor.Application.Preview;
using LabelEditor.Domain.Fields;

namespace LabelEditor.Presentation.ViewModels;

/// <summary>A category or field in the field tree.</summary>
public sealed class FieldNodeViewModel
{
    public FieldNodeViewModel(string name, string? caption, int? fieldId, IReadOnlyList<FieldNodeViewModel> children)
    {
        Name = name;
        Caption = caption;
        FieldId = fieldId;
        Children = children;
    }

    /// <summary>Element name (e.g. QMin), or the category caption.</summary>
    public string Name { get; }

    /// <summary>Translated caption, shown as secondary text / tooltip.</summary>
    public string? Caption { get; }

    public int? FieldId { get; }

    public bool IsField => FieldId is not null;

    public IReadOnlyList<FieldNodeViewModel> Children { get; }

    public string ToolTip => FieldId is { } id ? $"{Name} (ID {id}){(Caption is null ? string.Empty : " – " + Caption)}" : Name;
}

/// <summary>The LEdit field tree with a filter box.</summary>
public sealed partial class FieldPickerViewModel : ObservableObject
{
    private FieldTextResolver? _resolver;

    public ObservableCollection<FieldNodeViewModel> Nodes { get; } = [];

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private FieldNodeViewModel? _selectedNode;

    public void Load(FieldTextResolver resolver)
    {
        _resolver = resolver;
        Rebuild();
    }

    partial void OnFilterChanged(string value) => Rebuild();

    private void Rebuild()
    {
        Nodes.Clear();
        if (_resolver is null)
        {
            return;
        }

        foreach (var category in _resolver.Catalog.Root.Children)
        {
            if (Build(category, _resolver, Filter.Trim()) is { } node)
            {
                Nodes.Add(node);
            }
        }
    }

    private static FieldNodeViewModel? Build(FieldCategory category, FieldTextResolver resolver, string filter)
    {
        var children = new List<FieldNodeViewModel>();
        children.AddRange(category.Children.Select(c => Build(c, resolver, filter)).OfType<FieldNodeViewModel>());
        foreach (var field in category.Fields)
        {
            var caption = resolver.Texts.Resolve(field.Caption);
            if (filter.Length == 0 || Matches(field.Name, filter) || Matches(caption, filter) || field.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) == filter)
            {
                children.Add(new FieldNodeViewModel(field.Name, caption, field.Id, []));
            }
        }

        if (children.Count == 0)
        {
            return null;
        }

        return new FieldNodeViewModel(resolver.Texts.Resolve(category.Caption) is { Length: > 0 } c ? c : category.Key, null, null, children);
    }

    private static bool Matches(string text, string filter) => text.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
}
