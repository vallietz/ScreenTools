using System.Windows.Media.Imaging;
using FocusTool.Win.Models;
using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

internal sealed class StaticPinController : IDisposable
{
    private readonly List<StaticPinWindow> _pins = [];
    private readonly ScreenshotService _screenshots = new();
    private readonly Func<bool> _isDisposed;
    private readonly Action _reassertLensTopmost;
    private readonly Action _reassertLensMenus;
    private readonly Action _stateChanged;
    private readonly Func<bool> _isAnnotationInputActive;
    private readonly Func<AnnotationTool> _annotationToolProvider;
    private readonly Func<ScreenPoint, bool> _annotationMouseDown;
    private readonly Action<ScreenPoint> _annotationMouseMove;
    private readonly Action<ScreenPoint> _annotationMouseUp;
    private readonly Action _annotationCaptureLost;
    private StaticPinWindow[] _selectionHiddenPins = [];
    private StaticPinWindow? _inkEraseTarget;
    private ScreenPoint _inkEraseLastPoint;
    private bool _selectionCommitted;

    public StaticPinController(
        Func<bool> isDisposed,
        Action reassertLensTopmost,
        Action reassertLensMenus,
        Action stateChanged,
        Func<bool> isAnnotationInputActive,
        Func<AnnotationTool> annotationToolProvider,
        Func<ScreenPoint, bool> annotationMouseDown,
        Action<ScreenPoint> annotationMouseMove,
        Action<ScreenPoint> annotationMouseUp,
        Action annotationCaptureLost)
    {
        _isDisposed = isDisposed;
        _reassertLensTopmost = reassertLensTopmost;
        _reassertLensMenus = reassertLensMenus;
        _stateChanged = stateChanged;
        _isAnnotationInputActive = isAnnotationInputActive;
        _annotationToolProvider = annotationToolProvider;
        _annotationMouseDown = annotationMouseDown;
        _annotationMouseMove = annotationMouseMove;
        _annotationMouseUp = annotationMouseUp;
        _annotationCaptureLost = annotationCaptureLost;
    }

    public int Count => _pins.Count;

    public void BeginSelection()
    {
        CancelSelection();
        _selectionHiddenPins = _pins.Where(pin => pin.Bounds != Rectangle.Empty).ToArray();
        foreach (var pin in _selectionHiddenPins) pin.HideForCapture();
    }

    public void CompleteSelection() => _selectionCommitted = true;

    public bool ConsumeSelectionCommit()
    {
        var committed = _selectionCommitted;
        _selectionCommitted = false;
        return committed;
    }

    public void CancelSelection()
    {
        _selectionCommitted = false;
        foreach (var pin in _selectionHiddenPins.Where(_pins.Contains)) pin.RestoreAfterCapture();
        _selectionHiddenPins = [];
    }

    public async Task OpenAsync(ScreenRect rect)
    {
        var previouslyVisible = _pins.Where(pin => pin.Bounds != Rectangle.Empty).ToArray();
        foreach (var pin in previouslyVisible) pin.HideForCapture();
        try
        {
            await CaptureController.WaitForScreenRefreshAsync();
            if (_isDisposed()) return;
            var image = await _screenshots.CaptureRegionImageAsync(rect, copyToClipboard: true);
            var pin = new StaticPinWindow(
                image,
                Close,
                _isAnnotationInputActive,
                _annotationToolProvider,
                _annotationMouseDown,
                _annotationMouseMove,
                _annotationMouseUp,
                _annotationCaptureLost,
                new Point((int)Math.Floor(rect.Left), (int)Math.Floor(rect.Top)));
            pin.Closed += OnPinClosed;
            _pins.Add(pin);
            pin.Show();
            _stateChanged();
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not create a static pin.", ex);
        }
        finally
        {
            if (!_isDisposed())
            {
                foreach (var pin in previouslyVisible.Where(_pins.Contains)) pin.RestoreAfterCapture();
                _selectionHiddenPins = [];
                ReassertTopmost();
            }
        }
    }

    public void CloseAll()
    {
        foreach (var pin in _pins.ToArray()) Remove(pin);
        _stateChanged();
    }

    public bool BeginEraseInkAt(ScreenPoint point, double thickness)
    {
        _inkEraseTarget = FindAt(point);
        if (_inkEraseTarget is null) return false;
        _inkEraseTarget.EraseInk(point, point, thickness);
        _inkEraseLastPoint = point;
        return true;
    }

    public void ContinueEraseInkAt(ScreenPoint point, double thickness)
    {
        if (_inkEraseTarget is null) return;
        _inkEraseTarget.EraseInk(_inkEraseLastPoint, point, thickness);
        _inkEraseLastPoint = point;
    }

    public void EndEraseInk() => _inkEraseTarget = null;

    public void ClearInk()
    {
        foreach (var pin in _pins) pin.ClearInk();
    }

    public void RefreshAnnotationCursors()
    {
        foreach (var pin in _pins) pin.RefreshAnnotationCursor();
    }

    public StaticPinWindow? FindAt(ScreenPoint point)
        => _pins.LastOrDefault(pin => pin.Contains(new Point((int)point.X, (int)point.Y)));

    /// <summary>Returns a stable z-order snapshot; later-created pins are visually on top.</summary>
    public IReadOnlyList<StaticPinWindow> GetPinsTopmostFirst() => _pins.AsEnumerable().Reverse().ToArray();

    public void ReconcileToWorkingArea() { foreach (var pin in _pins) pin.ReconcileToWorkingArea(); }

    public void ReassertTopmost()
    {
        _reassertLensTopmost();
        _reassertLensMenus();
        foreach (var pin in _pins) pin.ReassertTopmost();
    }

    public void Dispose() => CloseAll();

    private void Close(StaticPinWindow pin)
    {
        if (!_pins.Contains(pin)) return;
        Remove(pin);
        _stateChanged();
    }

    private void OnPinClosed(object? sender, EventArgs e)
    {
        if (sender is StaticPinWindow pin) Close(pin);
    }

    private void Remove(StaticPinWindow pin)
    {
        if (!_pins.Remove(pin)) return;
        pin.Closed -= OnPinClosed;
        pin.Dispose();
    }
}
