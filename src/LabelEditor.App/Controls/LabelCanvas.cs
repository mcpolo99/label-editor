using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
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
    /// <summary>Drag-and-drop format carrying a field id (invariant digits) from the field tree.</summary>
    public static readonly DataFormat<string> FieldDragFormat = DataFormat.CreateStringApplicationFormat("LabelEditor.Field");

    public static readonly StyledProperty<DocumentViewModel?> DocumentProperty =
        AvaloniaProperty.Register<LabelCanvas, DocumentViewModel?>(nameof(Document));

    /// <summary>Space around the label, in DIPs (the rulers line up with it).</summary>
    internal const double CanvasMargin = 24;

    /// <summary>How close (in DIPs) an edge must come to another entry's to snap to it.</summary>
    private const double SnapDistance = 5;
    private const double HandleSize = 7;
    private const double DragThreshold = 3;

    private static readonly IBrush Desk = new SolidColorBrush(Color.FromRgb(225, 228, 232));
    private static readonly IBrush DarkDesk = new SolidColorBrush(Color.FromRgb(30, 31, 34));
    private static readonly IBrush DimPaper = new SolidColorBrush(Color.FromRgb(200, 201, 204));
    private static readonly IBrush Shadow = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0));
    private static readonly IBrush DarkShadow = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
    private static readonly IPen DarkLabelPen = new Pen(new SolidColorBrush(Color.FromRgb(90, 93, 99)), 1);
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0, 120, 215));
    private static readonly IPen SelectionPen = new Pen(Accent, 1, DashStyle.Dash);
    private static readonly IPen HandlePen = new Pen(Accent, 1);
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 90, 200)), 1);
    private static readonly IPen LabelPen = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 1);
    private static readonly IBrush MarqueeFill = new SolidColorBrush(Color.FromArgb(30, 0, 120, 215));
    private static readonly IPen HighlightPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 140, 0)), 2.5);
    private static readonly IPen GuidePen = new Pen(new SolidColorBrush(Color.FromRgb(230, 0, 160)), 1);

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
    private (ResizeHandle Handle, bool Segment) _resizeHandle;

    /// <summary>While dragging: what the dragged entries can line up with, the dragged box (all selected) at the start, and the guides to draw.</summary>
    private IReadOnlyList<LabelRect> _snapTargets = [];
    private LabelRect _dragUnion;
    private IReadOnlyList<SnapLine> _guides = [];

    /// <summary>Entries stacked under the pointer at the last plain click; two or more open the pick list on release.</summary>
    private IReadOnlyList<EntryListItem> _pressStack = [];

    /// <summary>How an entry picked from the list joins the selection (plain click replaces, Shift adds, Ctrl toggles).</summary>
    private SelectionMode _pickMode = SelectionMode.Replace;

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

        /// <summary>Shift/Ctrl+click on overlapping entries: the pick list opens on release; nothing moves.</summary>
        PendingPick,
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
        else if (change.Property == ThemeVariantScope.ActualThemeVariantProperty)
        {
            InvalidateVisual();
        }
    }

    private bool IsDark => ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>The label paper: white, or soft grey in the dark theme (unless the user turned dimming off).</summary>
    private IBrush Paper => IsDark && Context?.DimLabelInDarkTheme != false ? DimPaper : Brushes.White;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelInteraction();
        if (Document is { } document)
        {
            document.RenderInvalidated -= OnRenderInvalidated;
        }

        if (_window is not null)
        {
            _window.Deactivated -= OnWindowDeactivated;
            _window = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private Window? _window;

    /// <summary>A dialog or another window took over mid-drag: never leave the entry attached to the cursor.</summary>
    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (_mode != Mode.None)
        {
            CancelInteraction();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            _window = window;
            window.Deactivated -= OnWindowDeactivated;
            window.Deactivated += OnWindowDeactivated;
        }

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
        var dark = IsDark;
        context.FillRectangle(dark ? DarkDesk : Desk, new Rect(Bounds.Size));
        if (Layout is not { } layout || Document is not { } document)
        {
            return;
        }

        var view = View;
        var label = view.ToView(layout.Header.Shape.Bounds);
        var paper = Paper;
        context.FillRectangle(dark ? DarkShadow : Shadow, label.Translate(new Vector(3, 3)));
        context.FillRectangle(paper, label);
        _painter.Paper = paper;

        if (document.Context.ShowGrid)
        {
            DrawGrid(context, layout, view, label);
        }

        using (context.PushClip(label.Inflate(CanvasMargin / 2)))
        {
            _painter.Paint(context, BuildScene(document), view);
        }

        context.DrawRectangle(null, dark ? DarkLabelPen : LabelPen, label);
        DrawSelection(context, document, view);
        if (document.HighlightedEntry is { } highlighted && document.Layout.Find(highlighted) is { } hover)
        {
            if (hover.IsSegment)
            {
                context.DrawLine(HighlightPen, view.ToView(hover.Bounds.Start), view.ToView(hover.Bounds.End));
            }
            else
            {
                context.DrawRectangle(null, HighlightPen, view.ToView(hover.Bounds).Inflate(2));
            }
        }
        DrawInteraction(context, view);
        DrawGuides(context, view);
    }

    private void DrawGuides(DrawingContext context, ViewTransform view)
    {
        foreach (var line in _guides)
        {
            var (from, to) = line.IsVertical
                ? (view.ToView(line.Position, line.From), view.ToView(line.Position, line.To))
                : (view.ToView(line.From, line.Position), view.ToView(line.To, line.Position));
            var crisp = new Vector(line.IsVertical ? 0.5 : 0, line.IsVertical ? 0 : 0.5);
            context.DrawLine(GuidePen, new Point(Math.Round(from.X), Math.Round(from.Y)) + crisp, new Point(Math.Round(to.X), Math.Round(to.Y)) + crisp);
        }
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

        // A position-locked entry keeps its top-left corner (a line its start point): only the far edges have handles.
        var locked = entry.IsPositionLocked;
        if (entry.IsSegment)
        {
            if (!locked)
            {
                yield return (ResizeHandle.TopLeft, view.ToView(entry.Bounds.Start));
            }

            yield return (ResizeHandle.BottomRight, view.ToView(entry.Bounds.End));
            yield break;
        }

        var r = view.ToView(entry.Bounds);
        if (!locked)
        {
            yield return (ResizeHandle.TopLeft, r.TopLeft);
            yield return (ResizeHandle.Top, new Point(r.Center.X, r.Top));
            yield return (ResizeHandle.TopRight, r.TopRight);
        }

        yield return (ResizeHandle.Right, new Point(r.Right, r.Center.Y));
        yield return (ResizeHandle.BottomRight, r.BottomRight);
        yield return (ResizeHandle.Bottom, new Point(r.Center.X, r.Bottom));
        if (!locked)
        {
            yield return (ResizeHandle.BottomLeft, r.BottomLeft);
            yield return (ResizeHandle.Left, new Point(r.Left, r.Center.Y));
        }
    }

    // ------------------------------------------------------------------ pointer input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (Session is not { } session || Context is not { } context || session.IsInteracting)
        {
            return;
        }

        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            // The context menu acts on the selection: right-clicking an entry outside it selects that entry first.
            var probe = View.ToLayout(e.GetPosition(this));
            Document?.SelectForContextMenu(HitTesting.EntryAt(session.Layout, probe, (int)(4 / View.Scale))?.Id);
            FillPickMenu(probe);
            return;
        }

        if (!properties.IsLeftButtonPressed)
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
            _resizeHandle = (hit.Handle, hit.Entry.IsSegment);
            _snapTargets = SnapGuides.TargetsFor(session.Layout, session.Selection.Ids.ToHashSet());
            _resize = session.BeginResize(hit.Entry.Id, hit.Handle);
            _mode = Mode.Resizing;
            return;
        }

        var toggle = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var add = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var tolerance = (int)(4 / view.Scale);

        // Alt+click: select the next entry behind the current one at this point ("select behind"), wrapping around.
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !toggle && !add;
        LabelEntry? entry;
        if (alt)
        {
            entry = HitTesting.NextEntryAt(session.Layout, _pressLayout, tolerance, session.Selection.Count == 1 ? session.Selection.Dominant : null);
            if (entry is not null)
            {
                session.Selection.Select(entry.Id);
            }
        }
        else
        {
            var stack = HitTesting.EntriesAt(session.Layout, _pressLayout, tolerance);

            // Shift/Ctrl on overlapping entries: let the user choose which one to add/toggle before touching the selection.
            if ((toggle || add) && stack.Count > 1)
            {
                _pickMode = toggle ? SelectionMode.Toggle : SelectionMode.Add;
                _pressStack = Document!.EntriesAt(_pressLayout, tolerance);
                _mode = Mode.PendingPick;
                return;
            }

            _pickMode = SelectionMode.Replace;

            // A plain click on an already selected entry keeps it, even when it lies behind others: that is how an
            // entry picked from the overlap list can then be dragged.
            entry = (!toggle && !add ? stack.FirstOrDefault(x => session.Selection.Contains(x.Id)) : null) ?? (stack.Count > 0 ? stack[0] : null);
            _pressStack = !toggle && !add && stack.Count > 1 ? Document!.EntriesAt(_pressLayout, tolerance) : [];
        }
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

        // The button was released somewhere we never heard about (a dialog or another window took the release):
        // drop the interaction instead of leaving the entry stuck to the cursor.
        if (_mode != Mode.None && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            CancelInteraction();
            e.Pointer.Capture(null);
        }

        var position = e.GetPosition(this);
        var layoutPoint = View.ToLayout(position);
        document.PointerPosition = layoutPoint;
        var stacked = _mode == Mode.None ? HitTesting.EntriesAt(document.Layout, layoutPoint, (int)(4 / View.Scale)).Count : 0;
        document.CursorPosition = string.Create(CultureInfo.CurrentCulture, $"{layoutPoint.X / 1000.0:0.0}, {layoutPoint.Y / 1000.0:0.0}")
            + (stacked > 1 ? $"   ·   {stacked} entries overlap here: click to choose (Shift adds, Ctrl toggles)" : string.Empty);

        switch (_mode)
        {
            case Mode.None:
                UpdateHoverCursor(position);
                break;
            case Mode.PendingMove when Distance(position, _pressView) > DragThreshold:
                StartMove(document);
                goto case Mode.Moving;
            case Mode.Moving:
            {
                var (dx, dy) = (layoutPoint.X - _pressLayout.X, layoutPoint.Y - _pressLayout.Y);
                var target = SnapPoint(new LabelPoint(_dragOrigin.Left + dx, _dragOrigin.Top + dy), e.KeyModifiers);
                var guides = SnapsToEntries(e.KeyModifiers) ? SnapGuides.ForMove(_dragUnion.Offset(dx, dy), _snapTargets, SnapTolerance) : SnapResult.None;
                _guides = guides.Lines;

                // An edge lining up with another entry wins over the grid on that axis.
                _drag?.Update(guides.SnappedX ? dx + guides.Dx : target.X - _dragOrigin.Left, guides.SnappedY ? dy + guides.Dy : target.Y - _dragOrigin.Top);
                InvalidateVisual();
                break;
            }

            case Mode.Resizing:
            {
                var raw = new LabelPoint(_resizeAnchor.X + layoutPoint.X - _pressLayout.X, _resizeAnchor.Y + layoutPoint.Y - _pressLayout.Y);
                var target = SnapPoint(raw, e.KeyModifiers);
                var (handle, segment) = _resizeHandle;
                var movesX = segment || handle is not (ResizeHandle.Top or ResizeHandle.Bottom);
                var movesY = segment || handle is not (ResizeHandle.Left or ResizeHandle.Right);
                var guides = SnapsToEntries(e.KeyModifiers) ? SnapGuides.ForPoint(raw, movesX, movesY, _snapTargets, SnapTolerance) : SnapResult.None;
                _guides = guides.Lines;
                var x = guides.SnappedX ? raw.X + guides.Dx : target.X;
                var y = guides.SnappedY ? raw.Y + guides.Dy : target.Y;
                _resize?.Update(x - _resizeAnchor.X, y - _resizeAnchor.Y, minimumSize: 200);
                InvalidateVisual();
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
            case Mode.PendingMove or Mode.PendingPick when _pressStack.Count > 1:
                // A click (no drag) where several entries overlap: let the user choose which one.
                ShowPickFlyout(_pressStack, _pickMode);
                break;
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

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (Document is { } document && _mode == Mode.None)
        {
            document.PointerPosition = null;
        }
    }

    private void StartMove(DocumentViewModel document)
    {
        var session = document.Session;
        var moved = session.Selection.Ids.Select(session.Layout.Find).OfType<LabelEntry>().Where(x => !x.IsPositionLocked).Select(x => x.Bounds.Normalize()).ToList();
        _dragUnion = moved.Count > 0 ? moved.Aggregate((a, b) => a.Union(b)) : _dragOrigin;
        _snapTargets = SnapGuides.TargetsFor(session.Layout, session.Selection.Ids.ToHashSet());
        _drag = session.BeginMove();
        _mode = Mode.Moving;
    }

    /// <summary>Snapping to other entries is on and Alt (move freely) is not held.</summary>
    private bool SnapsToEntries(KeyModifiers modifiers) => Context is { SnapToEntries: true } && !modifiers.HasFlag(KeyModifiers.Alt);

    private int SnapTolerance => (int)Math.Ceiling(SnapDistance / View.Scale);

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

    /// <summary>
    /// The resize handle under the pointer, on any selected entry (the dominant one first). With several entries
    /// selected, dragging it resizes all of them (see <see cref="EditingCommands.BeginResize"/>).
    /// </summary>
    private (LabelEntry Entry, ResizeHandle Handle)? HandleAt(Point position)
    {
        if (Session is not { } session || session.Selection.IsEmpty)
        {
            return null;
        }

        var ids = session.Selection.Ids.OrderBy(id => id == session.Selection.Dominant ? 0 : 1);
        foreach (var entry in ids.Select(session.Layout.Find).OfType<LabelEntry>())
        {
            foreach (var (handle, point) in Handles(entry, View))
            {
                if (Math.Abs(point.X - position.X) <= HandleSize && Math.Abs(point.Y - position.Y) <= HandleSize)
                {
                    return (entry, handle);
                }
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

    // ------------------------------------------------------------------ "Select overlapping" list in the context menu

    /// <summary>Tag of the context menu item (and its separator) that lists the entries stacked under the pointer.</summary>
    public const string PickMenuTag = "pick-overlapping";

    private void FillPickMenu(LabelPoint point)
    {
        if (Document is not { } document || ContextMenu is not { } menu)
        {
            return;
        }

        var stack = document.EntriesAt(point, (int)(4 / View.Scale));
        var parts = menu.Items.OfType<Control>().Where(c => Equals(c.Tag, PickMenuTag)).ToList();
        foreach (var part in parts)
        {
            part.IsVisible = stack.Count > 1;
        }

        if (parts.OfType<MenuItem>().FirstOrDefault() is not { } pick)
        {
            return;
        }

        pick.Items.Clear();
        foreach (var option in CreatePickItems(document, stack, SelectionMode.Replace))
        {
            pick.Items.Add(option);
        }

        menu.Closed -= OnContextMenuClosed;
        menu.Closed += OnContextMenuClosed;
    }

    /// <summary>True while the overlap pick list is shown (lets UI tests observe it).</summary>
    internal bool IsPickListOpen { get; private set; }

    /// <summary>Small list at the cursor with the entries stacked at the clicked point; hovering one outlines it.</summary>
    private void ShowPickFlyout(IReadOnlyList<EntryListItem> stack, SelectionMode mode)
    {
        if (Document is not { } document)
        {
            return;
        }

        var title = mode switch
        {
            SelectionMode.Add => $"{stack.Count} entries here: add to selection",
            SelectionMode.Toggle => $"{stack.Count} entries here: add or remove",
            _ => $"{stack.Count} entries here",
        };
        var flyout = new MenuFlyout { Placement = PlacementMode.Pointer };
        flyout.Items.Add(new MenuItem { Header = title, IsEnabled = false, FontWeight = FontWeight.SemiBold });
        flyout.Items.Add(new Separator());
        foreach (var option in CreatePickItems(document, stack, mode))
        {
            flyout.Items.Add(option);
        }

        flyout.Closed += (_, _) =>
        {
            document.HighlightedEntry = null;
            IsPickListOpen = false;
        };
        IsPickListOpen = true;
        flyout.ShowAt(this, showAtPointer: true);
    }

    /// <summary>One menu item per stacked entry, topmost first: click selects, hover outlines, the selected one is ticked.</summary>
    private static IEnumerable<MenuItem> CreatePickItems(DocumentViewModel document, IReadOnlyList<EntryListItem> stack, SelectionMode mode)
    {
        foreach (var item in stack)
        {
            var id = item.Id;
            var option = new MenuItem
            {
                // Position (and size) tell identical kinds apart, e.g. two ellipses.
                Header = (string.IsNullOrEmpty(item.Description) ? item.Kind : $"{item.Kind}   {item.Description}") + $"   ·  at {item.Position}" + (document.Layout.Find(id) is { IsSegment: false } box ? string.Create(System.Globalization.CultureInfo.CurrentCulture, $"  ·  {box.Bounds.Width / 1000.0:0.#}×{box.Bounds.Height / 1000.0:0.#} mm") : string.Empty),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = document.Session.Selection.Contains(id),
            };
            option.Click += (_, _) => document.SelectEntry(id, mode);
            option.PointerEntered += (_, _) => document.HighlightedEntry = id;
            option.PointerExited += (_, _) => document.HighlightedEntry = null;
            yield return option;
        }
    }

    private void OnContextMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (Document is { } document)
        {
            document.HighlightedEntry = null;
        }
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

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(FieldDragFormat) && Document is not null ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (Document is not { } document || e.DataTransfer.TryGetValue(FieldDragFormat) is not { } text || !int.TryParse(text, CultureInfo.InvariantCulture, out var fieldId))
        {
            return;
        }

        document.InsertField(fieldId, View.ToLayout(e.GetPosition(this)));
        Focus();
        e.Handled = true;
    }

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
        _pressStack = [];
        _guides = [];
        _snapTargets = [];
        _drag = null;
        _resize = null;
        _mode = Mode.None;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
}
