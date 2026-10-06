using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Forms;
using FocusTool.Win.Models;
using FocusTool.Win.Native;
using FocusTool.Win.Services;

namespace FocusTool.Win.Overlay;

internal sealed class StaticPinWindow : IDisposable
{
    private static readonly Color IdleBorderColor = Color.FromArgb(255, 128, 128, 128);
    private static readonly Color ActiveBorderColor = Color.FromArgb(255, 35, 211, 200);
    private readonly Bitmap _image;
    private readonly Bitmap _ink;
    private readonly List<StaticPinArrow> _arrows = [];
    private StaticPinArrow? _draftArrow;
    private StaticPinArrow? _selectedArrow;
    private StaticPinArrow? _editingArrow;
    private int _editingArrowControl;
    private readonly StaticPinForm _form;
    private Rectangle _bounds;
    private bool _disposed;
    private bool _hovered;
    private bool _dragging;
    private bool _drawing;
    private bool _startingNativeDrag;
    private bool _resizing;
    private StaticPinResizeHandle _resizeHandle;
    private Point _resizeOrigin;
    private Rectangle _resizeBounds;
    private readonly List<System.Windows.Forms.Timer> _topmostTimers = [];
    private readonly Func<bool> _isAnnotationInputActive;
    private readonly Func<AnnotationTool> _annotationToolProvider;
    private readonly Func<ScreenPoint, bool> _annotationMouseDown;
    private readonly Action<ScreenPoint> _annotationMouseMove;
    private readonly Action<ScreenPoint> _annotationMouseUp;
    private readonly Action _annotationCaptureLost;

    public StaticPinWindow(
        BitmapSource image,
        Action<StaticPinWindow> close,
        Func<bool> isAnnotationInputActive,
        Func<AnnotationTool> annotationToolProvider,
        Func<ScreenPoint, bool> annotationMouseDown,
        Action<ScreenPoint> annotationMouseMove,
        Action<ScreenPoint> annotationMouseUp,
        Action annotationCaptureLost,
        Point location)
    {
        CloseRequested = close;
        _isAnnotationInputActive = isAnnotationInputActive;
        _annotationToolProvider = annotationToolProvider;
        _annotationMouseDown = annotationMouseDown;
        _annotationMouseMove = annotationMouseMove;
        _annotationMouseUp = annotationMouseUp;
        _annotationCaptureLost = annotationCaptureLost;
        _image = ToBitmap(image);
        _ink = new Bitmap(_image.Width, _image.Height, PixelFormat.Format32bppPArgb);
        var size = StaticPinGeometry.ScaleToMinimum(_image.Width, _image.Height, 48);
        _bounds = new Rectangle(location.X, location.Y, size.Width, size.Height);
        _form = new StaticPinForm(this)
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            TopMost = true,
            AutoScaleMode = AutoScaleMode.None,
            Bounds = _bounds,
        };
        _form.FormClosed += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Closed;
    public Action<StaticPinWindow> CloseRequested { get; }
    public IntPtr Handle => _form.IsHandleCreated ? _form.Handle : IntPtr.Zero;
    public Rectangle Bounds => _bounds;

    public void Show()
    {
        if (_disposed) return;
        _form.Show();
        ReassertTopmost();
    }

    public void HideForCapture() { if (!_disposed && _form.Visible) _form.Hide(); }
    public void RestoreAfterCapture() { if (!_disposed && !_form.Visible) { _form.Show(); ReassertTopmost(); } }

    public void ReassertTopmost()
    {
        if (Handle != IntPtr.Zero)
            NativeMethods.SetWindowPos(Handle, NativeMethods.HwndTopmost, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
    }

    public void ReconcileToWorkingArea()
    {
        if (_disposed || !_form.IsHandleCreated) return;
        var screen = Screen.FromRectangle(_bounds);
        var area = screen.WorkingArea;
        var left = Math.Clamp(_bounds.Left, area.Left, Math.Max(area.Left, area.Right - _bounds.Width));
        var top = Math.Clamp(_bounds.Top, area.Top, Math.Max(area.Top, area.Bottom - _bounds.Height));
        SetBounds(new Rectangle(left, top, _bounds.Width, _bounds.Height));
    }

    public bool Contains(Point point) => _bounds.Contains(point);
    public void SetHover(bool hovered) { if (_hovered != hovered) { _hovered = hovered; _form.Invalidate(); } }
    public void RefreshAnnotationCursor() => _form.Cursor = AnnotationCursor.ForPin(_isAnnotationInputActive(), _annotationToolProvider());
    /// <summary>
    /// Sets whether an annotation gesture is currently being drawn on this pin.
    /// The annotation integration is responsible for calling this at gesture start and end.
    /// </summary>
    public void SetDrawing(bool drawing) { if (_drawing != drawing) { _drawing = drawing; _form.Invalidate(); } }

    internal static StaticPinBorderStyle ResolveBorderStyle(bool hovered, bool dragging, bool resizing, bool drawing)
        => hovered || dragging || resizing || drawing
            ? new StaticPinBorderStyle(ActiveBorderColor, 2)
            : new StaticPinBorderStyle(IdleBorderColor, 1);

    public void Paint(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.FromArgb(255, 28, 28, 28));
        var borderStyle = ResolveBorderStyle(_hovered, _dragging, _resizing, _drawing);
        var imageRect = ContentRect(borderStyle);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(_image, imageRect);
        graphics.DrawImage(_ink, imageRect);
        DrawArrows(graphics, imageRect);
        using var border = new Pen(borderStyle.Color, borderStyle.Width) { Alignment = PenAlignment.Inset };
        graphics.DrawRectangle(border, 0, 0, Math.Max(0, _form.ClientSize.Width - 1), Math.Max(0, _form.ClientSize.Height - 1));
        if (_hovered)
        {
            var close = CloseBounds(_form.ClientSize.Width);
            using var fill = new SolidBrush(Color.FromArgb(230, 35, 211, 200));
            using var cross = new Pen(Color.White, 2);
            graphics.FillEllipse(fill, close);
            graphics.DrawLine(cross, close.Left + 6, close.Top + 6, close.Right - 6, close.Bottom - 6);
            graphics.DrawLine(cross, close.Right - 6, close.Top + 6, close.Left + 6, close.Bottom - 6);
        }
    }

    public void BeginResize(Point screenPoint, StaticPinResizeHandle handle)
    {
        _resizing = true;
        _resizeHandle = handle;
        _resizeOrigin = screenPoint;
        _resizeBounds = _bounds;
        _form.Invalidate();
    }

    public void Resize(Point screenPoint)
    {
        if (!_resizing) return;
        var rect = StaticPinGeometry.Resize(
            new ScreenRect(0, 0, _resizeBounds.Width, _resizeBounds.Height),
            screenPoint.X - _resizeOrigin.X,
            screenPoint.Y - _resizeOrigin.Y,
            _resizeHandle,
            80);
        SetBounds(new Rectangle(
            _resizeBounds.Left + (int)Math.Round(rect.Left),
            _resizeBounds.Top + (int)Math.Round(rect.Top),
            (int)Math.Round(rect.Width),
            (int)Math.Round(rect.Height)));
    }

    public void EndResize()
    {
        if (!_resizing) return;
        _resizing = false;
        _form.Invalidate();
    }

    public void BeginDrag()
    {
        if (_dragging) return;
        _dragging = true;
        _form.Cursor = AnnotationCursor.DraggingHandForPin;
        _form.Invalidate();
    }

    public void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        RefreshAnnotationCursor();
        _form.Invalidate();
    }

    private void EndInteractionAfterCaptureLoss()
    {
        if (_startingNativeDrag || (!_resizing && !_dragging)) return;
        EndResize();
        EndDrag();
        _bounds = _form.Bounds;
    }

    public void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        _form.Bounds = bounds;
        _form.Invalidate();
    }

    /// <summary>Draws ink in source-image coordinates, so it stays registered when the pin moves or resizes.</summary>
    public void DrawAnnotation(AnnotationTool tool, ScreenPoint start, ScreenPoint end, string color, double thickness, bool drawArrowHead)
    {
        if (_disposed) return;
        var localStart = ToSourcePoint(start);
        var localEnd = ToSourcePoint(end);
        using var graphics = Graphics.FromImage(_ink);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingMode = CompositingMode.SourceOver;
        using var pen = new Pen(ColorTranslator.FromHtml(color), (float)Math.Max(1, thickness * _image.Width / Math.Max(1, _bounds.Width)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawLine(pen, localStart, localEnd);
        if (tool == AnnotationTool.Arrow && drawArrowHead)
        {
            var angle = Math.Atan2(localEnd.Y - localStart.Y, localEnd.X - localStart.X);
            var length = Math.Max(8, pen.Width * 3);
            var left = new PointF((float)(localEnd.X - length * Math.Cos(angle - Math.PI / 6)), (float)(localEnd.Y - length * Math.Sin(angle - Math.PI / 6)));
            var right = new PointF((float)(localEnd.X - length * Math.Cos(angle + Math.PI / 6)), (float)(localEnd.Y - length * Math.Sin(angle + Math.PI / 6)));
            graphics.DrawLine(pen, localEnd, left);
            graphics.DrawLine(pen, localEnd, right);
        }
        _form.Invalidate();
    }

    public void BeginArrow(ScreenPoint point, string color, double thickness)
    {
        var source = ToSourcePoint(point);
        var content = ContentRect(ResolveBorderStyle(_hovered, _dragging, _resizing, _drawing));
        _draftArrow = new StaticPinArrow(new ScreenPoint(source.X, source.Y), new ScreenPoint(source.X, source.Y),
            color, thickness * _image.Width / Math.Max(1, content.Width));
        _selectedArrow = null;
        _form.Invalidate();
    }

    public void UpdateArrow(ScreenPoint point)
    {
        if (_draftArrow is null) return;
        var source = ToSourcePoint(point);
        _draftArrow.End = new ScreenPoint(source.X, source.Y);
        _form.Invalidate();
    }

    public void CommitArrow(ScreenPoint point)
    {
        if (_draftArrow is null) return;
        UpdateArrow(point);
        if (_draftArrow.Start.DistanceTo(_draftArrow.End) >= 2)
        {
            _arrows.Add(_draftArrow);
            _selectedArrow = _draftArrow;
        }
        _draftArrow = null;
        _form.Invalidate();
    }

    public void CancelArrow() { _draftArrow = null; _form.Invalidate(); }

    public bool TryBeginArrowEdit(ScreenPoint point)
    {
        var source = ToSourcePoint(point);
        var content = ContentRect(ResolveBorderStyle(_hovered, _dragging, _resizing, _drawing));
        var tolerance = 10 * _image.Width / Math.Max(1, content.Width);
        foreach (var arrow in _arrows.AsEnumerable().Reverse())
        {
            if (!arrow.TryHitControl(new ScreenPoint(source.X, source.Y), out var control, tolerance)) continue;
            _selectedArrow = _editingArrow = arrow;
            _editingArrowControl = control;
            _form.Invalidate();
            return true;
        }
        return false;
    }

    public bool IsEditingArrow => _editingArrow is not null;

    public void UpdateArrowEdit(ScreenPoint point)
    {
        if (_editingArrow is null) return;
        var source = ToSourcePoint(point);
        _editingArrow.SetControl(_editingArrowControl, new ScreenPoint(source.X, source.Y));
        _form.Invalidate();
    }

    public void EndArrowEdit() { _editingArrow = null; _editingArrowControl = 0; _form.Invalidate(); }

    private void DrawArrows(Graphics graphics, Rectangle imageRect)
    {
        var state = graphics.Save();
        graphics.SetClip(imageRect);
        graphics.TranslateTransform(imageRect.Left, imageRect.Top);
        graphics.ScaleTransform(imageRect.Width / (float)_image.Width, imageRect.Height / (float)_image.Height);
        foreach (var arrow in _arrows) DrawArrow(graphics, arrow, showControls: ReferenceEquals(arrow, _selectedArrow));
        if (_draftArrow is { } draft) DrawArrow(graphics, draft, showControls: false);
        graphics.Restore(state);
    }

    private void DrawArrow(Graphics graphics, StaticPinArrow arrow, bool showControls)
    {
        var (first, second) = arrow.GetControls();
        using var pen = new Pen(ColorTranslator.FromHtml(arrow.Color), (float)Math.Max(1, arrow.Thickness))
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        graphics.DrawBezier(pen, ToPointF(arrow.Start), ToPointF(first), ToPointF(second), ToPointF(arrow.End));
        var tangent = CurvedArrowGeometry.TangentAt(arrow.Start, first, second, arrow.End, 1);
        var length = Math.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y);
        if (length >= 0.01)
        {
            var ux = tangent.X / length;
            var uy = tangent.Y / length;
            var headLength = Math.Max(8, arrow.Thickness * 3);
            var wing = headLength * 0.5;
            var end = ToPointF(arrow.End);
            graphics.DrawLine(pen, end, new PointF((float)(end.X - ux * headLength - uy * wing), (float)(end.Y - uy * headLength + ux * wing)));
            graphics.DrawLine(pen, end, new PointF((float)(end.X - ux * headLength + uy * wing), (float)(end.Y - uy * headLength - ux * wing)));
        }
        if (!showControls) return;
        var radius = Math.Max(2, 4 * _image.Width / Math.Max(1, _bounds.Width));
        foreach (var control in new[] { arrow.Start, first, second, arrow.End })
        {
            using var fill = new SolidBrush(Color.White);
            using var outline = new Pen(Color.Black, 1);
            graphics.FillEllipse(fill, (float)(control.X - radius), (float)(control.Y - radius), (float)(radius * 2), (float)(radius * 2));
            graphics.DrawEllipse(outline, (float)(control.X - radius), (float)(control.Y - radius), (float)(radius * 2), (float)(radius * 2));
        }
    }

    private static PointF ToPointF(ScreenPoint point) => new((float)point.X, (float)point.Y);

    public void EraseInk(ScreenPoint start, ScreenPoint end, double thickness)
    {
        if (_disposed) return;
        var content = ContentRect(ResolveBorderStyle(_hovered, _dragging, _resizing, _drawing));
        var sourceWidth = (float)Math.Max(8, thickness * 4 * _image.Width / Math.Max(1, content.Width));
        var sourceStart = ToSourcePoint(start);
        var sourceEnd = ToSourcePoint(end);
        var distance = Math.Sqrt(Math.Pow(sourceEnd.X - sourceStart.X, 2) + Math.Pow(sourceEnd.Y - sourceStart.Y, 2));
        var samples = Math.Max(1, (int)Math.Ceiling(distance / Math.Max(1, sourceWidth / 2)));
        for (var index = 0; index <= samples; index++)
        {
            var progress = index / (float)samples;
            StaticPinInkEraser.EraseConnectedComponent(
                _ink,
                new PointF(sourceStart.X + (sourceEnd.X - sourceStart.X) * progress, sourceStart.Y + (sourceEnd.Y - sourceStart.Y) * progress),
                sourceWidth / 2);
        }
        _form.Invalidate();
    }

    public void ClearInk()
    {
        if (_disposed) return;
        StaticPinInkEraser.Clear(_ink);
        _form.Invalidate();
    }

    private PointF ToSourcePoint(ScreenPoint point)
    {
        var content = ContentRect(ResolveBorderStyle(_hovered, _dragging, _resizing, _drawing));
        var x = (point.X - _bounds.Left - content.Left) * _image.Width / Math.Max(1, content.Width);
        var y = (point.Y - _bounds.Top - content.Top) * _image.Height / Math.Max(1, content.Height);
        return new PointF((float)x, (float)y);
    }

    private Rectangle ContentRect(StaticPinBorderStyle borderStyle) => new(
        borderStyle.Width,
        borderStyle.Width,
        Math.Max(1, _form.ClientSize.Width - borderStyle.Width * 2),
        Math.Max(1, _form.ClientSize.Height - borderStyle.Width * 2));

    public static Rectangle CloseBounds(int width) => new(Math.Max(0, width - 32), 4, 28, 28);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TopmostContextMenuHelper.DisposeTimers(_topmostTimers);
        _form.Dispose();
        _ink.Dispose();
        _image.Dispose();
    }

    private static Bitmap ToBitmap(BitmapSource source)
    {
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        using var decoded = new Bitmap(stream);
        return new Bitmap(decoded);
    }

    private sealed class StaticPinForm : Form
    {
        private readonly StaticPinWindow _pin;
        private readonly ContextMenuStrip _menu = new();
        private bool _annotationGestureActive;

        public StaticPinForm(StaticPinWindow pin)
        {
            _pin = pin;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            _menu.Items.Add(new ToolStripMenuItem("Удалить", null, (_, _) => _pin.CloseRequested(_pin)));
            ContextMenuStrip = _menu;
            TopmostContextMenuHelper.Attach(_menu, _pin._topmostTimers);
        }

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate; cp.ExStyle &= ~NativeMethods.WsExAppWindow; cp.ClassStyle |= 0x00020000; return cp; }
        }
        protected override void OnPaint(PaintEventArgs e) => _pin.Paint(e.Graphics);
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _pin.SetHover(true); _pin.RefreshAnnotationCursor(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _pin.SetHover(false); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var screen = PointToScreen(e.Location);
            if (e.Button == MouseButtons.Left && CloseBounds(ClientSize.Width).Contains(e.Location)) { _pin.CloseRequested(_pin); return; }
            // Border handles take precedence over annotation input so a Pin stays resizable
            // while the global drawing overlay is active.
            if (e.Button == MouseButtons.Left && TryGetResizeHandle(e.Location, out var handle)) { _pin.BeginResize(screen, handle); Capture = true; return; }
            if (e.Button == MouseButtons.Left && _pin._isAnnotationInputActive())
            {
                // The overlay receives the rest of the gesture through native capture.
                // If it is unavailable, retain the old WinForms capture as a fallback.
                _annotationGestureActive = !_pin._annotationMouseDown(ToScreenPoint(e.Location));
                Capture = _annotationGestureActive;
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                _pin.BeginDrag();
                _pin._startingNativeDrag = true;
                try
                {
                    NativeMethods.ReleaseCapture();
                    NativeMethods.SendMessage(Handle, NativeMethods.WmNcLButtonDown, NativeMethods.HtCaption, IntPtr.Zero);
                }
                finally
                {
                    _pin._startingNativeDrag = false;
                    _pin.EndDrag();
                    _pin._bounds = Bounds;
                }
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_annotationGestureActive) { _pin._annotationMouseMove(ToScreenPoint(e.Location)); return; }
            if (_pin._resizing)
            {
                _pin.Resize(PointToScreen(e.Location));
                return;
            }
            Cursor = TryGetResizeHandle(e.Location, out var handle)
                ? ResizeCursor(handle)
                : AnnotationCursor.ForPin(_pin._isAnnotationInputActive(), _pin._annotationToolProvider());
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && _annotationGestureActive)
            {
                _pin._annotationMouseUp(ToScreenPoint(e.Location));
                _annotationGestureActive = false;
                Capture = false;
                return;
            }
            if (_pin._resizing) { _pin.EndResize(); Capture = false; }
            _pin.EndDrag();
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) _pin.EndInteractionAfterCaptureLoss();
            if (!Capture && _annotationGestureActive)
            {
                _annotationGestureActive = false;
                _pin._annotationCaptureLost();
            }
        }
        private ScreenPoint ToScreenPoint(Point point)
        {
            var screen = PointToScreen(point);
            return new ScreenPoint(screen.X, screen.Y);
        }
        protected override void OnMove(EventArgs e) { base.OnMove(e); if (!_pin._resizing) _pin._bounds = Bounds; }

        private bool TryGetResizeHandle(Point point, out StaticPinResizeHandle handle)
        {
            const int grip = 10;
            var left = point.X <= grip;
            var right = point.X >= ClientSize.Width - grip;
            var top = point.Y <= grip;
            var bottom = point.Y >= ClientSize.Height - grip;
            handle = (left, right, top, bottom) switch
            {
                (true, _, true, _) => StaticPinResizeHandle.TopLeft,
                (_, true, true, _) => StaticPinResizeHandle.TopRight,
                (true, _, _, true) => StaticPinResizeHandle.BottomLeft,
                (_, true, _, true) => StaticPinResizeHandle.BottomRight,
                (true, _, _, _) => StaticPinResizeHandle.Left,
                (_, true, _, _) => StaticPinResizeHandle.Right,
                (_, _, true, _) => StaticPinResizeHandle.Top,
                (_, _, _, true) => StaticPinResizeHandle.Bottom,
                _ => default
            };
            return left || right || top || bottom;
        }

        private static Cursor ResizeCursor(StaticPinResizeHandle handle)
            => handle is StaticPinResizeHandle.Left or StaticPinResizeHandle.Right ? Cursors.SizeWE
                : handle is StaticPinResizeHandle.Top or StaticPinResizeHandle.Bottom ? Cursors.SizeNS
                : handle is StaticPinResizeHandle.TopLeft or StaticPinResizeHandle.BottomRight ? Cursors.SizeNWSE
                : Cursors.SizeNESW;
        protected override void Dispose(bool disposing) { if (disposing) _menu.Dispose(); base.Dispose(disposing); }
    }
}

internal readonly record struct StaticPinBorderStyle(Color Color, int Width);
