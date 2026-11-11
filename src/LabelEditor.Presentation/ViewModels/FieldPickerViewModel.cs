using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LabelEditor.Application.Preview;
using LabelEditor.Domain.Fields;

namespace LabelEditor.Presentation.ViewModels;

/// <summary>A category or field in the field tree.</summary>
public sealed partial class FieldNodeViewModel : ObservableObject
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

    [ObservableProperty]
    private bool _isExpanded;
}

/// <summary>The LEdit field tree with a filter box. Used by the Fields panel and the field selection dialog.</summary>
public sealed partial class FieldPickerViewModel : ObservableObject
{
    private FieldTextResolver? _resolver;

    public ObservableCollection<FieldNodeViewModel> Nodes { get; } = [];

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedFieldId), nameof(HasSelectedField))]
    private FieldNodeViewModel? _selectedNode;

    /// <summary>Id of the selected node when it is a field (not a category).</summary>
    public int? SelectedFieldId => SelectedNode?.FieldId;

    public bool HasSelectedField => SelectedFieldId is not null;

    public void Load(FieldTextResolver resolver)
    {
        _resolver = resolver;
        Rebuild();
    }

    /// <summary>Selects the first node for <paramref name="fieldId"/> and expands its categories.</summary>
    /// <returns>False when the field is not in the tree.</returns>
    public bool Reveal(int fieldId)
    {
        foreach (var node in Nodes)
        {
            if (Reveal(node, fieldId))
            {
                return true;
            }
        }

        return false;
    }

    private bool Reveal(FieldNodeViewModel node, int fieldId)
    {
        if (node.FieldId == fieldId)
        {
            SelectedNode = node;
            return true;
        }

        foreach (var child in node.Children)
        {
            if (Reveal(child, fieldId))
            {
                node.IsExpanded = true;
                return true;
            }
        }

        return false;
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

        // While filtering, show the matches without making the user open every category.
        return new FieldNodeViewModel(resolver.Texts.Resolve(category.Caption) is { Length: > 0 } c ? c : category.Key, null, null, children)
        {
            IsExpanded = filter.Length > 0,
        };
    }

    private static bool Matches(string text, string filter) => text.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
}
