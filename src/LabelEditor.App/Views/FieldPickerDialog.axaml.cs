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

    private void OnOk(object? sender, RoutedEventArgs e) => Close(Picker?.SelectedFieldId);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Picker?.SelectedFieldId is { } id)
        {
            Close(id);
        }
    }
}
