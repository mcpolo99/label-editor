using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LabelEditor.Presentation.ViewModels;

namespace LabelEditor.App.Views;

/// <summary>Modal field selection with the structured field tree. Result: the chosen field id, or null.</summary>
public sealed partial class FieldPickerDialog : Window
{
    public FieldPickerDialog()
    {
        InitializeComponent();

        // Bring the preselected field into view once the tree has been laid out.
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (Picker?.SelectedNode is { } node && Tree.ContainerFromItem(node) is Control container)
            {
                container.BringIntoView();
            }

            FilterBox.Focus();
        }, DispatcherPriority.Loaded);
    }

    private FieldPickerViewModel? Picker => DataContext as FieldPickerViewModel;

    /// <summary>Allow choosing several fields at once (Ctrl/Shift-click).</summary>
    public bool AllowMultiple
    {
        get => Tree.SelectionMode.HasFlag(SelectionMode.Multiple);
        init
        {
            Tree.SelectionMode = value ? SelectionMode.Multiple : SelectionMode.Single;
            MultiHint.IsVisible = value;
            Title = value ? "Add fields" : "Choose field";
        }
    }

    /// <summary>Chosen field ids in the order the user selected them (categories are ignored).</summary>
    private IReadOnlyList<int> ChosenFields()
    {
        var ids = (Tree.SelectedItems?.OfType<FieldNodeViewModel>() ?? [])
            .Select(n => n.FieldId).OfType<int>().Distinct().ToList();
        if (ids.Count == 0 && Picker?.SelectedFieldId is { } single)
        {
            ids.Add(single);
        }

        return ids;
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(ChosenFields());

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Picker?.SelectedFieldId is not null)
        {
            Close(ChosenFields());
        }
    }
}
