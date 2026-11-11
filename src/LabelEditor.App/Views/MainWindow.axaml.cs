using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using LabelEditor.App.Controls;
using LabelEditor.Application.Abstractions;
using LabelEditor.Presentation.ViewModels;

namespace LabelEditor.App.Views;

/// <summary>Main window. Code-behind only bridges view events the view model cannot express in bindings.</summary>
public sealed partial class MainWindow : Window
{
    private bool _closeConfirmed;
    private Point? _fieldPressPoint;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    /// <summary>Fields in bold, categories normal.</summary>
    public static FuncValueConverter<bool, FontWeight> FieldWeight { get; } = new(isField => isField ? FontWeight.SemiBold : FontWeight.Normal);

    /// <summary>Captions of LEdit's filler character options.</summary>
    public static FuncValueConverter<Domain.Text.FillCharacterMode, string> FillModeCaption { get; } = new(mode => mode switch
    {
        Domain.Text.FillCharacterMode.None => "None",
        Domain.Text.FillCharacterMode.LeadingZeros => "Leading zeros",
        Domain.Text.FillCharacterMode.LeadingBlanks => "Leading blanks",
        Domain.Text.FillCharacterMode.TrailingBlanks => "Trailing blanks",
        _ => "(not set)",
    });

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || ViewModel is not { } vm)
        {
            return;
        }

        // Ask asynchronously, then close again for real.
        e.Cancel = true;
        if (await vm.CanShutdownAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: DocumentViewModel document } && ViewModel is { } vm)
        {
            vm.CloseDocumentCommand.Execute(document);
        }
    }

    private void OnLibraryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: LabelLibraryItem item } && ViewModel is { } vm)
        {
            vm.OpenLibraryItemCommand.Execute(item);
        }
    }

    private void OnFieldDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is { } vm && vm.FieldPicker.SelectedNode is { IsField: true } node)
        {
            vm.InsertFieldCommand.Execute(node);
        }
    }

    private void OnFieldPointerPressed(object? sender, PointerPressedEventArgs e) =>
        _fieldPressPoint = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ? e.GetPosition(this) : null;

    /// <summary>Starts dragging a field once the pointer has moved a little, so clicks and double-clicks still work.</summary>
    private async void OnFieldPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_fieldPressPoint is not { } start || sender is not Control { DataContext: FieldNodeViewModel { FieldId: { } fieldId } })
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _fieldPressPoint = null;
            return;
        }

        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 6)
        {
            return;
        }

        _fieldPressPoint = null;

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(LabelCanvas.FieldDragFormat, fieldId.ToString(CultureInfo.InvariantCulture)));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
    }
}
