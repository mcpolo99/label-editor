using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LabelEditor.App.Rendering;
using LabelEditor.Application.Documents;
using SelectionMode = LabelEditor.Application.Documents.SelectionMode;
using LabelEditor.Application.Editing;
using LabelEditor.Domain.Editing;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;
using LabelEditor.Presentation.ViewModels;
using LabelEditor.Rendering.Scene;

namespace LabelEditor.App.Controls;

/// <summary>
/// The design surface: paints the label as it will print and turns mouse/keyboard input into session edits.
/// All edits go through the session (undoable); a drag is one interactive edit committed on release.
/// </summary>
public sealed class LabelCanvas : Control
{
    /// <summary>Drag-and-drop format carrying a field id from the field tree.</summary>
    public const string FieldDragFormat = "application/x-labeleditor-field";

    public static readonly StyledProperty<DocumentViewModel?> DocumentProperty =
        AvaloniaProperty.Register<LabelCanvas, DocumentViewModel?>(nameof(Document));

    private const double CanvasMargin = 24;
    private const double HandleSize = 7;
    private const double DragThreshold = 3;

    private static readonly IBrush Desk = new SolidColorBrush(Color.FromRgb(225, 228, 232));
    private static readonly IBrush Shadow = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0));
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0, 120, 215));
    private static readonly IPen SelectionPen = new Pen(Accent, 1, DashStyle.Dash);
    private static readonly IPen HandlePen = new Pen(Accent, 1);
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 90, 200)), 1);
    private static readonly IPen LabelPen = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 1);
    private static readonly IBrush MarqueeFill = new SolidColorBrush(Color.FromArgb(30, 0, 120, 215));

    private readonly ScenePainter _painter = new();
    private (LabelLayout Layout, object? Record, object Resolver, LabelScene Scene)? _sceneCache;

    private Mode _mode;
    private Point _pressView;
    private LabelPoint _pressLayout;
    private LabelPoint _current;
    private SelectionDrag? _drag;
    private EntryResize? _resize;
    private LabelPoint _resizeAnchor;
    private LabelRect _dragOrigin;

    static LabelCanvas()
    {
        AffectsMeasure<LabelCanvas>(DocumentProperty);
        AffectsRender<LabelCanvas>(DocumentProperty);
        FocusableProperty.OverrideDefaultValue<LabelCanvas>(true);
    }

    public LabelCanvas()
    {
        ClipToBounds = true;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private enum Mode
    {
        None,
        PendingMove,
        Moving,
        Resizing,
        Marquee,
        Creating,
    }

    public DocumentViewModel? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    private EditorSession? Session => Document?.Session;

    private EditorContext? Context => Document?.Context;

    private LabelLayout? Layout => Document?.Layout;

    private ViewTransform View
    {
        get
        {
            var layout = Layout;
            var scale = ViewTransform.ScaleFor(Document?.Zoom ?? 1, layout?.Header.Unit ?? MeasurementUnit.Metric);
            return new ViewTransform(scale, new Point(CanvasMargin, CanvasMargin));
        }
    }

    // ------------------------------------------------------------------ layout & rendering

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty)
        {
            if (change.OldValue is DocumentViewModel old)
            {
                old.RenderInvalidated -= OnRenderInvalidated;
            }

            if (change.NewValue is DocumentViewModel document)
            {
                document.RenderInvalidated += OnRenderInvalidated;
            }

            CancelInteraction();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelInteraction();
        if (Document is { } document)
        {
            document.RenderInvalidated -= OnRenderInvalidated;
        }

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Document is { } document)
        {
            document.RenderInvalidated -= OnRenderInvalidated;
            document.RenderInvalidated += OnRenderInvalidated;
        }
    }

    private void OnRenderInvalidated(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Layout is not { } layout)
        {
            return default;
        }

        var view = View;
        return new Size((layout.Header.Shape.Width * view.Scale) + (2 * CanvasMargin), (layout.Header.Shape.Height * view.Scale) + (2 * CanvasMargin));
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Desk, new Rect(Bounds.Size));
        if (Layout is not { } layout || Document is not { } document)
        {
            return;
        }

        var view = View;
        var label = view.ToView(layout.Header.Shape.Bounds);
        context.FillRectangle(Shadow, label.Translate(new Vector(3, 3)));
        context.FillRectangle(Brushes.White, label);

        if (document.Context.ShowGrid)
        {
            DrawGrid(context, layout, view, label);
        }

        using (context.PushClip(label.Inflate(CanvasMargin / 2)))
        {
            _painter.Paint(context, BuildScene(document), view);
        }

        context.DrawRectangle(null, LabelPen, label);
        DrawSelection(context, document, view);
        DrawInteraction(context, view);
    }

    private LabelScene BuildScene(DocumentViewModel document)
    {
        var record = document.Context.PreviewRecord;
        var resolver = document.Context.Resolver;
        if (_sceneCache is { } cache && ReferenceEquals(cache.Layout, document.Layout) && ReferenceEquals(cache.Record, record) && ReferenceEquals(cache.Resolver, resolver))
        {
            return cache.Scene;
        }

        var scene = new LabelSceneBuilder(resolver).Build(document.Layout, record);
        _sceneCache = (document.Layout, record, resolver, scene);
        return scene;
    }

    private static void DrawGrid(DrawingContext context, LabelLayout layout, ViewTransform view, Rect label)
    {
        var grid = layout.Header.GridSize;
        if (grid <= 0)
        {
            return;
        }

        var step = grid;
        while (step * view.Scale < 6)
        {
            step *= 2;
        }

        for (var x = step; x < layout.Header.Shape.Width; x += step)
        {
            var px = Math.Round(view.ToView(x, 0).X) + 0.5;
            context.DrawLine(GridPen, new Point(px, label.Top), new Point(px, label.Bottom));
        }

        for (var y = step; y < layout.Header.Shape.Height; y += step)
        {
            var py = Math.Round(view.ToView(0, y).Y) + 0.5;
            context.DrawLine(GridPen, new Point(label.Left, py), new Point(label.Right, py));
        }
    }

    private static void DrawSelection(DrawingContext context, DocumentViewModel document, ViewTransform view)
    {
        var selection = document.Session.Selection;
        foreach (var id in selection.Ids)
        {
            if (document.Layout.Find(id) is not { } entry)
            {
                continue;
            }

            var dominant = id == selection.Dominant && selection.Count > 1;
            if (entry.IsSegment)
            {
                context.DrawLine(SelectionPen, view.ToView(entry.Bounds.Start), view.ToView(entry.Bounds.End));
            }
            else
            {
                context.DrawRectangle(null, SelectionPen, view.ToView(entry.Bounds).Inflate(1));
            }

            foreach (var (_, point) in Handles(entry, view))
            {
                var handle = new Rect(point.X - (HandleSize / 2), point.Y - (HandleSize / 2), HandleSize, HandleSize);
                context.DrawRectangle(dominant || selection.Count == 1 ? Accent : Brushes.White, HandlePen, handle);
            }
        }
    }

    private void DrawInteraction(DrawingContext context, ViewTransform view)
    {
        switch (_mode)
        {
            case Mode.Marquee:
                var marquee = view.ToView(LabelRect.FromPoints(_pressLayout, _current));
                context.DrawRectangle(MarqueeFill, SelectionPen, marquee);
                break;
            case Mode.Creating when Context is { } ctx:
                if (ctx.ActiveTool == EditorTool.Line)
                {
                    context.DrawLine(new Pen(Accent, 1.5), view.ToView(_pressLayout), view.ToView(_current));
                }
                else
                {
                    context.DrawRectangle(MarqueeFill, new Pen(Accent, 1), view.ToView(LabelRect.FromPoints(_pressLayout, _current)));
                }

                break;
        }
    }

    private static IEnumerable<(ResizeHandle Handle, Point Point)> Handles(LabelEntry entry, ViewTransform view)
    {
        if (entry.IsSizeLocked)
        {
            yield break;
        }

        if (entry.IsSegment)
        {
            yield return (ResizeHandle.TopLeft, view.ToView(entry.Bounds.Start));
            yield return (ResizeHandle.BottomRight, view.ToView(entry.Bounds.End));
            yield break;
        }

        var r = view.ToView(entry.Bounds);
        yield return (ResizeHandle.TopLeft, r.TopLeft);
        yield return (ResizeHandle.Top, new Point(r.Center.X, r.Top));
        yield return (ResizeHandle.TopRight, r.TopRight);
        yield return (ResizeHandle.Right, new Point(r.Right, r.Center.Y));
        yield return (ResizeHandle.BottomRight, r.BottomRight);
        yield return (ResizeHandle.Bottom, new Point(r.Center.X, r.Bottom));
        yield return (ResizeHandle.BottomLeft, r.BottomLeft);
        yield return (ResizeHandle.Left, new Point(r.Left, r.Center.Y));
    }

    // ------------------------------------------------------------------ pointer input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (Session is not { } session || Context is not { } context || session.IsInteracting || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var view = View;
        _pressView = e.GetPosition(this);
        _pressLayout = view.ToLayout(_pressView);
        _current = _pressLayout;
        e.Pointer.Capture(this);
        e.Handled = true;

        if (context.ActiveTool != EditorTool.Select)
        {
            _pressLayout = SnapPoint(_pressLayout, e.KeyModifiers);
            _current = _pressLayout;
            _mode = Mode.Creating;
            return;
        }

        if (HandleAt(_pressView) is { } hit)
        {
            _resizeAnchor = HandlePoint(hit.Entry, hit.Handle);
            _resize = session.BeginResize(hit.Entry.Id, hit.Handle);
            _mode = Mode.Resizing;
            return;
        }

        var entry = HitTesting.EntryAt(session.Layout, _pressLayout, (int)(4 / view.Scale));
        var toggle = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var add = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (entry is null)
        {
            if (!toggle && !add)
            {
                session.Selection.Clear();
            }

            _mode = Mode.Marquee;
            return;
        }

        if (toggle)
        {
            session.Selection.Select(entry.Id, SelectionMode.Toggle);
            _mode = Mode.None;
            return;
        }

        if (!session.Selection.Contains(entry.Id))
        {
            session.Selection.Select(entry.Id, add ? SelectionMode.Add : SelectionMode.Replace);
        }

        _dragOrigin = session.Layout.Find(session.Selection.Dominant ?? entry.Id)?.Bounds.Normalize() ?? entry.Bounds.Normalize();
        _mode = Mode.PendingMove;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Document is not { } document)
        {
            return;
        }

        var position = e.GetPosition(this);
        var layoutPoint = View.ToLayout(position);
        document.CursorPosition = string.Create(CultureInfo.CurrentCulture, $"{layoutPoint.X / 1000.0:0.0}, {layoutPoint.Y / 1000.0:0.0}");

        switch (_mode)
        {
            case Mode.None:
                UpdateHoverCursor(position);
                break;
            case Mode.PendingMove when Distance(position, _pressView) > DragThreshold:
                _drag = document.Session.BeginMove();
                _mode = Mode.Moving;
                goto case Mode.Moving;
            case Mode.Moving:
            {
                var target = SnapPoint(new LabelPoint(_dragOrigin.Left + layoutPoint.X - _pressLayout.X, _dragOrigin.Top + layoutPoint.Y - _pressLayout.Y), e.KeyModifiers);
                _drag?.Update(target.X - _dragOrigin.Left, target.Y - _dragOrigin.Top);
                break;
            }

            case Mode.Resizing:
            {
                var target = SnapPoint(new LabelPoint(_resizeAnchor.X + layoutPoint.X - _pressLayout.X, _resizeAnchor.Y + layoutPoint.Y - _pressLayout.Y), e.KeyModifiers);
                _resize?.Update(target.X - _resizeAnchor.X, target.Y - _resizeAnchor.Y, minimumSize: 200);
                break;
            }

            case Mode.Marquee:
                _current = layoutPoint;
                InvalidateVisual();
                break;
            case Mode.Creating:
                _current = Constrain(_pressLayout, SnapPoint(layoutPoint, e.KeyModifiers), e.KeyModifiers);
                InvalidateVisual();
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (Session is not { } session || Context is not { } context)
        {
            return;
        }

        switch (_mode)
        {
            case Mode.Moving:
                _drag?.Commit();
                break;
            case Mode.Resizing:
                _resize?.Commit();
                break;
            case Mode.Marquee:
            {
                var area = LabelRect.FromPoints(_pressLayout, _current);
                if (area.Width > 0 || area.Height > 0)
                {
                    var mode = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? SelectionMode.Add : SelectionMode.Replace;
                    session.Selection.Select(HitTesting.EntriesIn(session.Layout, area).Select(x => x.Id), mode);
                }

                break;
            }

            case Mode.Creating:
                CreateEntry(session, context);
                break;
        }

        ResetInteraction();
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelInteraction();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && Document is { } document)
        {
            document.Zoom = Math.Clamp(document.Zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), 0.25, 20);
            e.Handled = true;
            return;
        }

        base.OnPointerWheelChanged(e);
    }

    private void CreateEntry(EditorSession session, EditorContext context)
    {
        var tool = context.ActiveTool;
        var rect = tool == EditorTool.Line
            ? new LabelRect(_pressLayout.X, _pressLayout.Y, _current.X - _pressLayout.X, _current.Y - _pressLayout.Y)
            : LabelRect.FromPoints(_pressLayout, _current);

        // A click without a meaningful drag places an entry of the default size.
        var tiny = Math.Abs(rect.Width) < EntryFactory.MinimumSize && Math.Abs(rect.Height) < EntryFactory.MinimumSize;
        if (tiny)
        {
            var (w, h) = EntryFactory.DefaultSize(tool);
            rect = new LabelRect(_pressLayout.X, _pressLayout.Y, w, h);
        }

        session.AddEntry(EntryFactory.Create(tool, rect));
        context.ActiveTool = EditorTool.Select;
    }

    private void UpdateHoverCursor(Point position)
    {
        if (Session is not { } session || Context is not { } context)
        {
            return;
        }

        if (context.ActiveTool != EditorTool.Select)
        {
            Cursor = new Cursor(StandardCursorType.Cross);
            return;
        }

        if (HandleAt(position) is { } hit)
        {
            Cursor = new Cursor(hit.Entry.IsSegment ? StandardCursorType.Hand : hit.Handle switch
            {
                ResizeHandle.TopLeft or ResizeHandle.BottomRight => StandardCursorType.TopLeftCorner,
                ResizeHandle.TopRight or ResizeHandle.BottomLeft => StandardCursorType.TopRightCorner,
                ResizeHandle.Left or ResizeHandle.Right => StandardCursorType.SizeWestEast,
                _ => StandardCursorType.SizeNorthSouth,
            });
            return;
        }

        var view = View;
        Cursor = HitTesting.EntryAt(session.Layout, view.ToLayout(position), (int)(4 / view.Scale)) is null
            ? Cursor.Default
            : new Cursor(StandardCursorType.SizeAll);
    }

    /// <summary>Resize handles only exist when exactly one entry is selected.</summary>
    private (LabelEntry Entry, ResizeHandle Handle)? HandleAt(Point position)
    {
        if (Session is not { Selection.Count: 1 } session || session.Layout.Find(session.Selection.Ids[0]) is not { } entry)
        {
            return null;
        }

        foreach (var (handle, point) in Handles(entry, View))
        {
            if (Math.Abs(point.X - position.X) <= HandleSize && Math.Abs(point.Y - position.Y) <= HandleSize)
            {
                return (entry, handle);
            }
        }

        return null;
    }

    private static LabelPoint HandlePoint(LabelEntry entry, ResizeHandle handle)
    {
        if (entry.IsSegment)
        {
            return handle == ResizeHandle.TopLeft ? entry.Bounds.Start : entry.Bounds.End;
        }

        var b = entry.Bounds;
        return handle switch
        {
            ResizeHandle.TopLeft => new(b.Left, b.Top),
            ResizeHandle.Top => new(b.CenterX, b.Top),
            ResizeHandle.TopRight => new(b.Right, b.Top),
            ResizeHandle.Right => new(b.Right, b.CenterY),
            ResizeHandle.BottomRight => new(b.Right, b.Bottom),
            ResizeHandle.Bottom => new(b.CenterX, b.Bottom),
            ResizeHandle.BottomLeft => new(b.Left, b.Bottom),
            _ => new(b.Left, b.CenterY),
        };
    }

    /// <summary>Snaps to the label grid when snapping is on; holding Alt bypasses it.</summary>
    private LabelPoint SnapPoint(LabelPoint point, KeyModifiers modifiers) =>
        Context is { SnapToGrid: true } && !modifiers.HasFlag(KeyModifiers.Alt) && Layout is { } layout
            ? LayoutUnits.Snap(point, layout.Header.GridSize)
            : point;

    /// <summary>Shift: circles for ellipses, horizontal/vertical lines, squares for boxes.</summary>
    private LabelPoint Constrain(LabelPoint start, LabelPoint end, KeyModifiers modifiers)
    {
        if (!modifiers.HasFlag(KeyModifiers.Shift))
        {
            return end;
        }

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (Context?.ActiveTool == EditorTool.Line)
        {
            return Math.Abs(dx) >= Math.Abs(dy) ? new LabelPoint(end.X, start.Y) : new LabelPoint(start.X, end.Y);
        }

        var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new LabelPoint(start.X + (Math.Sign(dx == 0 ? 1 : dx) * side), start.Y + (Math.Sign(dy == 0 ? 1 : dy) * side));
    }

    // ------------------------------------------------------------------ keyboard

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Session is not { } session || Context is not { } context)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            CancelInteraction();
            context.ActiveTool = EditorTool.Select;
            session.Selection.Clear();
            e.Handled = true;
            return;
        }

        if (session.IsInteracting)
        {
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var grid = session.Layout.Header.GridSize > 0 ? session.Layout.Header.GridSize : 1000;
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 100 : grid;
        e.Handled = (e.Key, ctrl) switch
        {
            (Key.Delete or Key.Back, false) => session.DeleteSelection() || true,
            (Key.Left, false) => session.MoveSelection(-step, 0) || true,
            (Key.Right, false) => session.MoveSelection(step, 0) || true,
            (Key.Up, false) => session.MoveSelection(0, -step) || true,
            (Key.Down, false) => session.MoveSelection(0, step) || true,
            (Key.Z, true) => (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? session.Redo() : session.Undo()) || true,
            (Key.Y, true) => session.Redo() || true,
            (Key.C, true) => Do(() => session.CopySelection(context.Clipboard)),
            (Key.X, true) => session.CutSelection(context.Clipboard) || true,
            (Key.V, true) => session.Paste(context.Clipboard, grid) || true,
            (Key.D, true) => session.DuplicateSelection(grid) || true,
            (Key.A, true) => Do(session.SelectAll),
            _ => false,
        };
    }

    private static bool Do(Action action)
    {
        action();
        return true;
    }

    // ------------------------------------------------------------------ drag and drop of fields

    // TODO: migrate to Avalonia's DataTransfer API (the IDataObject API is obsolete since 11.3 but still supported).
#pragma warning disable CS0618
    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.Data.Contains(FieldDragFormat) && Document is not null ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (Document is not { } document || e.Data.Get(FieldDragFormat) is not string text || !int.TryParse(text, CultureInfo.InvariantCulture, out var fieldId))
        {
            return;
        }

        document.InsertField(fieldId, View.ToLayout(e.GetPosition(this)));
        Focus();
        e.Handled = true;
    }
#pragma warning restore CS0618

    // ------------------------------------------------------------------ helpers

    private void CancelInteraction()
    {
        _drag?.Dispose();
        _resize?.Dispose();
        ResetInteraction();
        InvalidateVisual();
    }

    private void ResetInteraction()
    {
        _drag = null;
        _resize = null;
        _mode = Mode.None;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
}
