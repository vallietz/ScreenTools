using FocusTool.Win.Overlay;
using FocusTool.Win.Models;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FocusTool.Win.Services;

/// <summary>Ephemeral ink owned by the current Quick edit session only.</summary>
internal static class QuickEditInkStore
{
    public const double StepBadgeRadius = 16;
    public static List<QuickEditStroke> Strokes { get; } = [];
    public static QuickEditTool Tool { get; set; } = QuickEditTool.Pencil;
    public static QuickEditStroke? Draft { get; private set; }
    public static QuickEditStroke? SelectedArrow { get; private set; }
    public static QuickEditStroke? SelectedStroke { get; private set; }
    public static QuickEditStroke? ActiveText { get; private set; }
    private static QuickEditStroke? _textBeforeEdit;
    public static QuickEditStroke? SelectedCallout { get; private set; }
    public static bool IsCalloutPointerDragging => _draggedCallout is not null;
    private static QuickEditStroke? _draggedCallout;
    private static int _draggedTailIndex;
    private static int _draggedCalloutEndpoint = -1;
    private static bool _calloutDragSaved;
    public static bool IsArrowControlDragging => _draggedArrow is not null;
    private static QuickEditStroke? _draggedArrow;
    private static int _draggedControl;
    private static QuickEditStroke? _draggedStroke;
    private static bool _dragHistorySaved;
    private static ScreenPoint _dragOrigin;
    private static List<QuickEditStroke>? _dragOriginal;
    private static QuickEditStroke? _resizedStroke;
    private static int _resizedHandle;
    private static bool _resizeHistorySaved;
    public static bool IsResizeDragging => _resizedStroke is not null;
    private static readonly Stack<List<QuickEditStroke>> UndoStack = new();
    private static readonly Stack<List<QuickEditStroke>> RedoStack = new();
    public static bool IsStrokeDragging => _draggedStroke is not null;
    public static BitmapSource? Source { get; private set; }
    public static BitmapSource? PixelatedSource { get; private set; }
    public static ScreenRect SourceFrame { get; private set; }

    public static void Reset() { Strokes.Clear(); UndoStack.Clear(); RedoStack.Clear(); Draft = null; SelectedStroke = null; SelectedArrow = null; ActiveText = null; SelectedCallout = null; _draggedCallout = null; _draggedTailIndex = 0; _draggedArrow = null; _draggedStroke = null; _draggedControl = 0; Tool = QuickEditTool.Pencil; Source = null; PixelatedSource = null; SourceFrame = default; }
    public static void SetSource(ScreenRect frame, BitmapSource source)
    {
        SourceFrame = frame;
        Source = source;
        PixelatedSource = CreatePixelated(source);
    }
    public static void Begin(ScreenPoint point) { Select(null); Draft = new QuickEditStroke(Tool, [point]); }
    public static void Add(ScreenPoint point)
    {
        if (Draft is null) return;
        if (Draft.Tool is QuickEditTool.Pencil or QuickEditTool.Highlighter || Draft.Points.Count == 1)
        {
            Draft.Points.Add(point);
        }
        else
        {
            Draft.Points[^1] = point;
        }
    }
    public static void AddText(ScreenPoint point, string text) { SaveHistory(); Strokes.Add(new QuickEditStroke(QuickEditTool.Text, [point]) { Text = text }); }
    public static void PlaceStepBadge(ScreenPoint point)
    {
        SaveHistory();
        var badge = new QuickEditStroke(QuickEditTool.StepBadge, [point]);
        Strokes.Add(badge);
        Select(badge);
    }

    public static int StepNumber(IReadOnlyList<QuickEditStroke> strokes, QuickEditStroke badge)
    {
        var number = 0;
        foreach (var stroke in strokes)
        {
            if (stroke.Tool == QuickEditTool.StepBadge) number++;
            if (ReferenceEquals(stroke, badge)) return number;
        }
        throw new ArgumentException("Badge is not in the current stroke list.", nameof(badge));
    }
    public static void BeginText(ScreenPoint point)
    {
        CommitText();
        ActiveText = new QuickEditStroke(QuickEditTool.Text, [point]) { Text = string.Empty };
        _textBeforeEdit = null;
        SelectedCallout = null;
    }
    public static bool BeginEditingSelectedText()
    {
        if (SelectedStroke is not { Tool: QuickEditTool.Text } text) return false;
        _textBeforeEdit = Clone(text);
        ActiveText = text;
        return true;
    }
    public static bool TryBeginEditingTextAt(ScreenPoint point)
    {
        foreach (var text in Strokes.AsEnumerable().Reverse())
        {
            if (text.Tool != QuickEditTool.Text || !Hit(text, point)) continue;
            if (text.GetResizeHandles().Any(handle => handle.DistanceTo(point) <= 10)) return false;
            for (var tail = 0; tail < 2; tail++)
            {
                var tip = tail == 0 ? text.CalloutTarget : text.CalloutTarget2;
                if (tip?.DistanceTo(point) <= 10) return false;
                if (tip is null && text.GetCalloutBase(tail).DistanceTo(point) <= 10) return false;
                if (tip is not null)
                {
                    var (first, second) = QuickEditCalloutGeometry.TailBaseEndpoints(text, tail);
                    if (first.DistanceTo(point) <= 10 || second.DistanceTo(point) <= 10) return false;
                }
            }
            Select(text);
            return BeginEditingSelectedText();
        }
        return false;
    }
    public static void AppendText(string text)
    {
        if (ActiveText is not null) ActiveText.Text += text;
    }
    public static void ReplaceActiveText(string text)
    {
        if (ActiveText is not null) ActiveText.Text = text;
    }
    public static void BackspaceText()
    {
        if (ActiveText?.Text is { Length: > 0 } text) ActiveText.Text = text[..^1];
    }
    public static void CommitText()
    {
        if (ActiveText is { } text && !string.IsNullOrWhiteSpace(text.Text))
        {
            if (_textBeforeEdit is not null)
            {
                var index = Strokes.IndexOf(text);
                var current = Snapshot();
                current[index] = Clone(_textBeforeEdit);
                UndoStack.Push(current);
                RedoStack.Clear();
            }
            else
            {
                SaveHistory();
                Strokes.Add(text);
            }
            Select(text);
        }
        ActiveText = null;
        _textBeforeEdit = null;
    }
    public static void CancelText()
    {
        if (_textBeforeEdit is not null && ActiveText is not null)
        {
            ActiveText.Text = _textBeforeEdit.Text;
            ActiveText.CalloutTarget = _textBeforeEdit.CalloutTarget;
            ActiveText.CalloutTarget2 = _textBeforeEdit.CalloutTarget2;
            ActiveText.CalloutAnchor1 = _textBeforeEdit.CalloutAnchor1;
            ActiveText.CalloutAnchor2 = _textBeforeEdit.CalloutAnchor2;
            ActiveText.CalloutBaseStart1 = _textBeforeEdit.CalloutBaseStart1;
            ActiveText.CalloutBaseEnd1 = _textBeforeEdit.CalloutBaseEnd1;
            ActiveText.CalloutBaseStart2 = _textBeforeEdit.CalloutBaseStart2;
            ActiveText.CalloutBaseEnd2 = _textBeforeEdit.CalloutBaseEnd2;
            ActiveText.CalloutBoxWidth = _textBeforeEdit.CalloutBoxWidth;
            ActiveText.CalloutBoxHeight = _textBeforeEdit.CalloutBoxHeight;
        }
        ActiveText = null;
        _textBeforeEdit = null;
    }

    public static bool TryBeginCalloutPointerDrag(ScreenPoint point)
    {
        foreach (var callout in Strokes.AsEnumerable().Reverse())
        {
            if (callout.Tool != QuickEditTool.Text) continue;
            var index = -1;
            var endpoint = -1;
            for (var tail = 0; tail < 2 && index < 0; tail++)
            {
                var target = tail == 0 ? callout.CalloutTarget : callout.CalloutTarget2;
                if (target?.DistanceTo(point) <= 10) { index = tail; break; }
                if (target is null)
                {
                    if (callout.GetCalloutBase(tail).DistanceTo(point) <= 10) index = tail;
                    continue;
                }
                var (first, second) = QuickEditCalloutGeometry.TailBaseEndpoints(callout, tail);
                if (first.DistanceTo(point) <= 10) { index = tail; endpoint = 0; }
                else if (second.DistanceTo(point) <= 10) { index = tail; endpoint = 1; }
            }
            if (index < 0) continue;
            Select(callout);
            _draggedCallout = callout;
            _draggedTailIndex = index;
            _draggedCalloutEndpoint = endpoint;
            _calloutDragSaved = false;
            return true;
        }
        return false;
    }
    public static void UpdateCalloutPointerDrag(ScreenPoint point)
    {
        if (_draggedCallout is null) return;
        if (_draggedCalloutEndpoint >= 0)
        {
            var anchor = QuickEditCalloutGeometry.ProjectToContour(_draggedCallout, point);
            if (_draggedCallout.GetCalloutBaseEndpointAnchor(_draggedTailIndex, _draggedCalloutEndpoint) == anchor) return;
            if (!_calloutDragSaved) { SaveHistory(); _calloutDragSaved = true; }
            _draggedCallout.SetCalloutBaseEndpointAnchor(_draggedTailIndex, _draggedCalloutEndpoint, anchor);
            return;
        }
        var target = point.DistanceTo(_draggedCallout.GetCalloutBase(_draggedTailIndex)) <= 6 ? (ScreenPoint?)null : point;
        var current = _draggedTailIndex == 0 ? _draggedCallout.CalloutTarget : _draggedCallout.CalloutTarget2;
        if (current == target) return;
        if (!_calloutDragSaved) { SaveHistory(); _calloutDragSaved = true; }
        if (_draggedTailIndex == 0) _draggedCallout.CalloutTarget = target;
        else _draggedCallout.CalloutTarget2 = target;
    }
    public static void EndCalloutPointerDrag() { _draggedCallout = null; _draggedTailIndex = 0; _draggedCalloutEndpoint = -1; }
    public static void Commit()
    {
        if (Draft is { Points.Count: > 1 } draft)
        {
            SaveHistory();
            Strokes.Add(draft);
            Select(draft);
        }
        Draft = null;
    }
    public static void Cancel() => Draft = null;

    public static bool TryBeginArrowControlDrag(ScreenPoint point)
    {
        const double hitRadius = 10;
        foreach (var stroke in Strokes.AsEnumerable().Reverse())
        {
            if (stroke.Tool != QuickEditTool.Arrow) continue;
            var (first, second) = stroke.GetArrowControls();
            var control = stroke.Points[0].DistanceTo(point) <= hitRadius ? 3
                : stroke.Points[^1].DistanceTo(point) <= hitRadius ? 4
                : first.DistanceTo(point) <= hitRadius ? 1
                : second.DistanceTo(point) <= hitRadius ? 2 : 0;
            if (control == 0) continue;
            SaveHistory();
            Select(stroke);
            _draggedArrow = stroke;
            _draggedControl = control;
            return true;
        }
        return false;
    }

    public static void UpdateArrowControlDrag(ScreenPoint point) => _draggedArrow?.SetArrowControl(_draggedControl, point);
    public static void EndArrowControlDrag() { _draggedArrow = null; _draggedControl = 0; }

    public static bool TrySelectArrowAt(ScreenPoint point)
    {
        foreach (var stroke in Strokes.AsEnumerable().Reverse())
        {
            if (stroke.Tool != QuickEditTool.Arrow) continue;
            var (first, second) = stroke.GetArrowControls();
            var samples = CurvedArrowGeometry.Sample(stroke.Points[0], first, second, stroke.Points[^1], 48);
            for (var index = 1; index < samples.Count; index++)
            {
                if (DistanceToSegment(point, samples[index - 1], samples[index]) > 7) continue;
                Select(stroke);
                return true;
            }
        }
        return false;
    }

    private static double DistanceToSegment(ScreenPoint point, ScreenPoint start, ScreenPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared < 0.0001 ? 0 : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
        return point.DistanceTo(new ScreenPoint(start.X + dx * t, start.Y + dy * t));
    }

    public static void Select(QuickEditStroke? stroke)
    {
        SelectedStroke = stroke;
        SelectedArrow = stroke?.Tool == QuickEditTool.Arrow ? stroke : null;
        SelectedCallout = stroke?.Tool == QuickEditTool.Text ? stroke : null;
    }

    public static bool TryBeginStrokeDrag(ScreenPoint point)
    {
        foreach (var stroke in Strokes.AsEnumerable().Reverse())
        {
            if (!Hit(stroke, point)) continue;
            Select(stroke);
            _draggedStroke = stroke;
            _dragHistorySaved = false;
            _dragOrigin = point;
            _dragOriginal = [Clone(stroke)];
            return true;
        }
        Select(null);
        return false;
    }

    public static void UpdateStrokeDrag(ScreenPoint point)
    {
        if (_draggedStroke is null || _dragOriginal is null) return;
        var original = _dragOriginal[0];
        var dx = point.X - _dragOrigin.X;
        var dy = point.Y - _dragOrigin.Y;
        if (dx == 0 && dy == 0) return;
        if (!_dragHistorySaved) { SaveHistory(); _dragHistorySaved = true; }
        _draggedStroke.Points.Clear();
        _draggedStroke.Points.AddRange(original.Points.Select(p => p.Offset(dx, dy)));
        if (_draggedStroke.Tool != QuickEditTool.Text)
        {
            _draggedStroke.CalloutTarget = original.CalloutTarget?.Offset(dx, dy);
            _draggedStroke.CalloutTarget2 = original.CalloutTarget2?.Offset(dx, dy);
        }
        _draggedStroke.SetArrowControlsFrom(original, dx, dy);
    }

    public static void EndStrokeDrag() { _draggedStroke = null; _dragOriginal = null; }

    public static bool TryBeginResizeDrag(ScreenPoint point)
    {
        var stroke = SelectedStroke;
        if (stroke is null || (stroke.Points.Count < 2 && stroke.Tool != QuickEditTool.Text)
            || stroke.Tool is not (QuickEditTool.Line or QuickEditTool.Rectangle or QuickEditTool.Ellipse or QuickEditTool.Mosaic or QuickEditTool.Text)) return false;
        var handle = Array.FindIndex(stroke.GetResizeHandles(), candidate => candidate.DistanceTo(point) <= 10);
        if (handle < 0) return false;
        _resizedStroke = stroke;
        _resizedHandle = handle;
        _resizeHistorySaved = false;
        return true;
    }
    public static void UpdateResizeDrag(ScreenPoint point)
    {
        if (_resizedStroke is null) return;
        if (_resizedStroke.Tool == QuickEditTool.Text)
        {
            var stroke = _resizedStroke;
            var origin = stroke.Points[0];
            var left = origin.X;
            var top = origin.Y;
            var right = left + stroke.CalloutWidth;
            var bottom = top + stroke.CalloutHeight;
            var minWidth = stroke.CalloutMinimumWidth;
            var minHeight = stroke.CalloutMinimumHeight;
            switch (_resizedHandle)
            {
                case 0: left = Math.Min(point.X, right - minWidth); top = Math.Min(point.Y, bottom - minHeight); break;
                case 1: top = Math.Min(point.Y, bottom - minHeight); break;
                case 2: right = Math.Max(point.X, left + minWidth); top = Math.Min(point.Y, bottom - minHeight); break;
                case 3: right = Math.Max(point.X, left + minWidth); break;
                case 4: right = Math.Max(point.X, left + minWidth); bottom = Math.Max(point.Y, top + minHeight); break;
                case 5: bottom = Math.Max(point.Y, top + minHeight); break;
                case 6: left = Math.Min(point.X, right - minWidth); bottom = Math.Max(point.Y, top + minHeight); break;
                case 7: left = Math.Min(point.X, right - minWidth); break;
            }
            if (origin == new ScreenPoint(left, top) && stroke.CalloutWidth == right - left && stroke.CalloutHeight == bottom - top) return;
            if (!_resizeHistorySaved) { SaveHistory(); _resizeHistorySaved = true; }
            stroke.Points[0] = new ScreenPoint(left, top);
            stroke.CalloutBoxWidth = right - left;
            stroke.CalloutBoxHeight = bottom - top;
            return;
        }
        if (_resizedStroke.Tool == QuickEditTool.Ellipse)
        {
            var first = _resizedStroke.Points[0];
            var last = _resizedStroke.Points[^1];
            var left = Math.Min(first.X, last.X);
            var right = Math.Max(first.X, last.X);
            var top = Math.Min(first.Y, last.Y);
            var bottom = Math.Max(first.Y, last.Y);
            switch (_resizedHandle)
            {
                case 0: top = Math.Min(point.Y, bottom - 1); break;
                case 1: right = Math.Max(point.X, left + 1); break;
                case 2: bottom = Math.Max(point.Y, top + 1); break;
                case 3: left = Math.Min(point.X, right - 1); break;
            }
            if (first == new ScreenPoint(left, top) && last == new ScreenPoint(right, bottom)) return;
            if (!_resizeHistorySaved) { SaveHistory(); _resizeHistorySaved = true; }
            _resizedStroke.Points[0] = new ScreenPoint(left, top);
            _resizedStroke.Points[^1] = new ScreenPoint(right, bottom);
            return;
        }
        var index = _resizedHandle == 0 ? 0 : _resizedStroke.Points.Count - 1;
        if (_resizedStroke.Points[index] == point) return;
        if (!_resizeHistorySaved) { SaveHistory(); _resizeHistorySaved = true; }
        _resizedStroke.Points[index] = point;
    }
    public static void EndResizeDrag() => _resizedStroke = null;

    public static bool DeleteSelected()
    {
        if (SelectedStroke is null) return false;
        SaveHistory();
        Strokes.Remove(SelectedStroke);
        Select(null);
        return true;
    }

    public static bool Undo()
    {
        if (UndoStack.Count == 0) return false;
        RedoStack.Push(Snapshot());
        Restore(UndoStack.Pop());
        return true;
    }

    public static bool Redo()
    {
        if (RedoStack.Count == 0) return false;
        UndoStack.Push(Snapshot());
        Restore(RedoStack.Pop());
        return true;
    }

    private static void SaveHistory() { UndoStack.Push(Snapshot()); RedoStack.Clear(); }
    private static List<QuickEditStroke> Snapshot() => Strokes.Select(Clone).ToList();
    private static QuickEditStroke Clone(QuickEditStroke stroke)
    {
        var copy = new QuickEditStroke(stroke.Tool, [.. stroke.Points]) { Text = stroke.Text,
            CalloutTarget = stroke.CalloutTarget, CalloutTarget2 = stroke.CalloutTarget2,
            CalloutAnchor1 = stroke.CalloutAnchor1, CalloutAnchor2 = stroke.CalloutAnchor2,
            CalloutBaseStart1 = stroke.CalloutBaseStart1, CalloutBaseEnd1 = stroke.CalloutBaseEnd1,
            CalloutBaseStart2 = stroke.CalloutBaseStart2, CalloutBaseEnd2 = stroke.CalloutBaseEnd2,
            CalloutBoxWidth = stroke.CalloutBoxWidth, CalloutBoxHeight = stroke.CalloutBoxHeight };
        copy.SetArrowControlsFrom(stroke, 0, 0);
        return copy;
    }
    private static void Restore(List<QuickEditStroke> strokes)
    {
        Strokes.Clear(); Strokes.AddRange(strokes.Select(Clone));
        Select(null); Draft = null; ActiveText = null;
        _draggedStroke = null; _draggedArrow = null; _draggedCallout = null;
    }

    private static bool Hit(QuickEditStroke stroke, ScreenPoint point)
    {
        var points = stroke.Points;
        if (points.Count == 0) return false;
        if (stroke.Tool == QuickEditTool.StepBadge)
            return points[0].DistanceTo(point) <= StepBadgeRadius + 4;
        if (stroke.Tool == QuickEditTool.Text)
        {
            var width = stroke.CalloutWidth;
            return point.X >= points[0].X - 5 && point.X <= points[0].X + width + 5
                && point.Y >= points[0].Y - 5 && point.Y <= points[0].Y + stroke.CalloutHeight + 5;
        }
        if (points.Count < 2) return false;
        if (stroke.Tool is QuickEditTool.Rectangle or QuickEditTool.Mosaic or QuickEditTool.Ellipse)
        {
            var a = points[0]; var b = points[^1];
            var left = Math.Min(a.X, b.X); var right = Math.Max(a.X, b.X);
            var top = Math.Min(a.Y, b.Y); var bottom = Math.Max(a.Y, b.Y);
            if (stroke.Tool == QuickEditTool.Mosaic) return point.X >= left && point.X <= right && point.Y >= top && point.Y <= bottom;
            if (stroke.Tool == QuickEditTool.Rectangle)
                return ((Math.Abs(point.X - left) <= 8 || Math.Abs(point.X - right) <= 8) && point.Y >= top - 8 && point.Y <= bottom + 8)
                    || ((Math.Abs(point.Y - top) <= 8 || Math.Abs(point.Y - bottom) <= 8) && point.X >= left - 8 && point.X <= right + 8);
            var rx = Math.Max(1, (right - left) / 2); var ry = Math.Max(1, (bottom - top) / 2);
            var normalized = Math.Sqrt(Math.Pow((point.X - (left + right) / 2) / rx, 2) + Math.Pow((point.Y - (top + bottom) / 2) / ry, 2));
            return Math.Abs(normalized - 1) <= 8 / Math.Min(rx, ry);
        }
        var path = stroke.Tool == QuickEditTool.Arrow
            ? CurvedArrowGeometry.Sample(points[0], stroke.GetArrowControls().First, stroke.GetArrowControls().Second, points[^1], 48)
            : points;
        for (var i = 1; i < path.Count; i++)
            if (DistanceToSegment(point, path[i - 1], path[i]) <= (stroke.Tool == QuickEditTool.Highlighter ? 12 : 8)) return true;
        return false;
    }

    private static BitmapSource CreatePixelated(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);
        const int block = 6;
        for (var top = 0; top < height; top += block)
        for (var left = 0; left < width; left += block)
        {
            var right = Math.Min(width, left + block);
            var bottom = Math.Min(height, top + block);
            long blue = 0, green = 0, red = 0;
            var count = (right - left) * (bottom - top);
            for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var offset = y * stride + x * 4;
                blue += pixels[offset]; green += pixels[offset + 1]; red += pixels[offset + 2];
            }
            var b = (byte)(blue / count);
            var g = (byte)(green / count);
            var r = (byte)(red / count);
            for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var offset = y * stride + x * 4;
                pixels[offset] = b; pixels[offset + 1] = g; pixels[offset + 2] = r;
            }
        }
        var result = BitmapSource.Create(width, height, source.DpiX, source.DpiY,
            PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}

internal sealed record QuickEditStroke(QuickEditTool Tool, List<ScreenPoint> Points)
{
    public string? Text { get; set; }
    public ScreenPoint? CalloutTarget { get; set; }
    public ScreenPoint? CalloutTarget2 { get; set; }
    public CalloutAnchor CalloutAnchor1 { get; set; } = new(CalloutEdge.Bottom, 1.0 / 3);
    public CalloutAnchor CalloutAnchor2 { get; set; } = new(CalloutEdge.Bottom, 2.0 / 3);
    public CalloutAnchor? CalloutBaseStart1 { get; set; }
    public CalloutAnchor? CalloutBaseEnd1 { get; set; }
    public CalloutAnchor? CalloutBaseStart2 { get; set; }
    public CalloutAnchor? CalloutBaseEnd2 { get; set; }
    public double CalloutBoxWidth { get; set; } = 180;
    public double CalloutBoxHeight { get; set; } = 50;
    public double CalloutMinimumWidth => 100;
    public double CalloutMinimumHeight => Math.Max(42, (Text ?? string.Empty).Replace("\r\n", "\n").Split('\n').Length * 23 + 16);
    public double CalloutWidth => Math.Max(CalloutBoxWidth, CalloutMinimumWidth);
    public double CalloutHeight => Math.Max(CalloutBoxHeight, CalloutMinimumHeight);
    public CalloutAnchor GetCalloutAnchor(int index) => index switch
    {
        0 => CalloutAnchor1,
        1 => CalloutAnchor2,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
    public void SetCalloutAnchor(int index, CalloutAnchor anchor)
    {
        if (index == 0) CalloutAnchor1 = anchor;
        else if (index == 1) CalloutAnchor2 = anchor;
        else throw new ArgumentOutOfRangeException(nameof(index));
    }
    public CalloutAnchor? GetCalloutBaseEndpointAnchor(int index, int endpoint) => (index, endpoint) switch
    {
        (0, 0) => CalloutBaseStart1,
        (0, 1) => CalloutBaseEnd1,
        (1, 0) => CalloutBaseStart2,
        (1, 1) => CalloutBaseEnd2,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
    public void SetCalloutBaseEndpointAnchor(int index, int endpoint, CalloutAnchor anchor)
    {
        switch (index, endpoint)
        {
            case (0, 0): CalloutBaseStart1 = anchor; break;
            case (0, 1): CalloutBaseEnd1 = anchor; break;
            case (1, 0): CalloutBaseStart2 = anchor; break;
            case (1, 1): CalloutBaseEnd2 = anchor; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
    public ScreenPoint GetCalloutBase(int index)
    {
        return QuickEditCalloutGeometry.PointOnContour(this, GetCalloutAnchor(index));
    }
    public ScreenPoint CalloutHandle => GetCalloutBase(0);
    public ScreenPoint? Control1 { get; private set; }
    public ScreenPoint? Control2 { get; private set; }

    public ScreenPoint[] GetResizeHandles()
    {
        if (Tool == QuickEditTool.Text)
        {
            var origin = Points[0];
            var middleX = origin.X + CalloutWidth / 2;
            var middleY = origin.Y + CalloutHeight / 2;
            var boxRight = origin.X + CalloutWidth;
            var boxBottom = origin.Y + CalloutHeight;
            return [origin, new ScreenPoint(middleX, origin.Y), new ScreenPoint(boxRight, origin.Y),
                new ScreenPoint(boxRight, middleY), new ScreenPoint(boxRight, boxBottom),
                new ScreenPoint(middleX, boxBottom), new ScreenPoint(origin.X, boxBottom),
                new ScreenPoint(origin.X, middleY)];
        }
        if (Points.Count < 2) return [];
        if (Tool != QuickEditTool.Ellipse) return [Points[0], Points[^1]];
        var left = Math.Min(Points[0].X, Points[^1].X);
        var right = Math.Max(Points[0].X, Points[^1].X);
        var top = Math.Min(Points[0].Y, Points[^1].Y);
        var bottom = Math.Max(Points[0].Y, Points[^1].Y);
        return [new ScreenPoint((left + right) / 2, top),
            new ScreenPoint(right, (top + bottom) / 2),
            new ScreenPoint((left + right) / 2, bottom),
            new ScreenPoint(left, (top + bottom) / 2)];
    }

    public (ScreenPoint First, ScreenPoint Second) GetArrowControls()
    {
        var (first, second) = CurvedArrowGeometry.DefaultControls(Points[0], Points[^1]);
        return (Control1 ?? first, Control2 ?? second);
    }

    public void SetArrowControl(int index, ScreenPoint point)
    {
        if (index == 1) Control1 = point;
        else if (index == 2) Control2 = point;
        else if (index == 3) Points[0] = point;
        else if (index == 4) Points[^1] = point;
        else throw new ArgumentOutOfRangeException(nameof(index));
    }

    public void SetArrowControlsFrom(QuickEditStroke source, double dx, double dy)
    {
        Control1 = source.Control1?.Offset(dx, dy);
        Control2 = source.Control2?.Offset(dx, dy);
    }
}
internal enum QuickEditTool { Pencil, Line, Arrow, Rectangle, Ellipse, Text, Mosaic, Highlighter, StepBadge }
