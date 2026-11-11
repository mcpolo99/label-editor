using CommunityToolkit.Mvvm.ComponentModel;
using LabelEditor.Application.Documents;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;

namespace LabelEditor.Presentation.ViewModels.Properties;

/// <summary>Label size, unit and grid; shown when nothing is selected.</summary>
public sealed partial class LabelSettingsViewModel : PropertiesViewModel
{
    public LabelSettingsViewModel(EditorSession session)
        : base(session) => Refresh(session.Layout);

    public override string Title => "Label";

    public IReadOnlyList<MeasurementUnit> Units { get; } = Enum.GetValues<MeasurementUnit>();

    [ObservableProperty]
    private decimal? _labelWidth;

    [ObservableProperty]
    private decimal? _labelHeight;

    [ObservableProperty]
    private decimal? _gridSize;

    [ObservableProperty]
    private MeasurementUnit _unit;

    [ObservableProperty]
    private string _formatVersion = string.Empty;

    [ObservableProperty]
    private int _entryCount;

    public void Refresh(LabelLayout layout) => RunRefresh(() =>
    {
        LabelWidth = ToDisplay(layout.Header.Shape.Width);
        LabelHeight = ToDisplay(layout.Header.Shape.Height);
        GridSize = ToDisplay(layout.Header.GridSize);
        Unit = layout.Header.Unit;
        FormatVersion = layout.Header.Version.ToString();
        EntryCount = layout.VisualEntries.Count();
    });

    partial void OnLabelWidthChanged(decimal? value) => Apply("Label size", l => l.WithLabelSize(FromDisplay(value), l.Header.Shape.Height));

    partial void OnLabelHeightChanged(decimal? value) => Apply("Label size", l => l.WithLabelSize(l.Header.Shape.Width, FromDisplay(value)));

    partial void OnGridSizeChanged(decimal? value) => Apply("Grid", l => l.WithHeader(h => h with { GridSize = Math.Max(0, FromDisplay(value)) }));

    partial void OnUnitChanged(MeasurementUnit value) => Apply("Unit", l => l.WithHeader(h => h with { Unit = value }));

    private void Apply(string description, Func<LabelLayout, LabelLayout> edit)
    {
        if (!IsRefreshing && !Session.IsInteracting)
        {
            Session.Execute(description, edit, mergeKey: "label:" + description);
        }
    }
}
