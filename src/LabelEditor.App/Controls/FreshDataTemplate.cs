using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.Templates;

namespace LabelEditor.App.Controls;

/// <summary>
/// A <see cref="DataTemplate"/> that builds new controls for every item instead of reusing the previous item's.
/// </summary>
/// <remarks>
/// Editing panels need this: when Avalonia reuses the controls for the next item, two-way bound inputs (radio
/// buttons, combo boxes, number boxes) briefly hold the previous item's values while they re-bind and write them
/// into the new item. That showed up as edits nobody made and as the field dialog opening by itself.
/// </remarks>
public sealed class FreshDataTemplate : DataTemplate, IRecyclingDataTemplate
{
    /// <inheritdoc />
    public new Control? Build(object? data, Control? existing) => base.Build(data, existing: null);
}
