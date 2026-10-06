using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusTool.Win.Models;
using FocusTool.Win.Native;
using FocusTool.Win.Services;
using System.Windows.Controls;
using WpfTextBox = System.Windows.Controls.TextBox;
using Screen = System.Windows.Forms.Screen;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace FocusTool.Win.Overlay;

internal sealed class OverlayWindow : Window
{
    private readonly Screen _screen;
    private readonly IOverlayInputHandler _inputHandler;
    private readonly Func<RectOverlayVisual?> _rectOverlayProvider;
    private readonly OverlaySurface _surface;
    private readonly Canvas _editorLayer = new();
    private WpfTextBox? _textEditor;
    private QuickEditStroke? _editingCallout;
    private HwndSource? _source;
    private bool _annotateInputEnabled;
    private bool _applyingBounds;
    private bool _nativeMouseCaptured;
    private bool _sourceReady;
    private ScreenPoint? _quickEditHoverPoint;

    public OverlayWindow(
        Screen screen,
        TrailModel trailModel,
        AnnotationDocument annotations,
        Func<AppSettings> settingsProvider,
        Func<InteractionMode> modeProvider,
        Func<double> clockProvider,
        Func<ScreenPoint?> spotlightProvider,
        Func<CursorHighlightFrame> cursorHighlightProvider,
        Func<ScreenBoardFrame?> screenBoardProvider,
        Func<RectOverlayVisual?> rectOverlayProvider,
        Func<IReadOnlyList<RegionMask>> regionMaskProvider,
        Func<int> regionMaskSelectionProvider,
        Func<IReadOnlyList<ScreenRect>> spotlightRegionProvider,
        Func<int> spotlightRegionSelectionProvider,
        Func<LiveAdjustmentHudFrame?> liveAdjustmentHudProvider,
        IOverlayInputHandler inputHandler)
    {
        _screen = screen;
        _inputHandler = inputHandler;
        _rectOverlayProvider = rectOverlayProvider;
        var bounds = screen.Bounds;
        _surface = new OverlaySurface(
            trailModel,
            annotations,
            settingsProvider,
            modeProvider,
            clockProvider,
            spotlightProvider,
            cursorHighlightProvider,
            screenBoardProvider,
            rectOverlayProvider,
            regionMaskProvider,
            regionMaskSelectionProvider,
            spotlightRegionProvider,
            spotlightRegionSelectionProvider,
            liveAdjustmentHudProvider,
            new ScreenRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));

        Title = "FocusTool";
        var content = new Grid();
        content.Children.Add(_surface);
        content.Children.Add(_editorLayer);
        Content = content;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;

        Left = _screen.Bounds.Left;
        Top = _screen.Bounds.Top;
        Width = _screen.Bounds.Width;
        Height = _screen.Bounds.Height;

        PreviewKeyDown += OnPreviewKeyDown;
        TextInput += OnTextInput;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _sourceReady = true;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _source?.AddHook(WndProc);
        SetInteractionMode(_inputHandler.Mode);
        PositionOverScreen();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        PositionOverScreen();
        SyncTextEditor();

        // WPF can rebuild the layered surface on a DPI change and drop the manually
        // applied click-through styles, which would silently make an annotation
        // overlay click-through (or vice versa). Re-assert them for the current mode.
        if (_sourceReady)
        {
            ApplyWindowStyles(_annotateInputEnabled);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_nativeMouseCaptured)
        {
            _nativeMouseCaptured = false;
            NativeMethods.ReleaseCapture();
            _inputHandler.HandleOverlayCaptureLost();
        }

        _source?.RemoveHook(WndProc);
        _source = null;
        _surface.Detach();
        base.OnClosed(e);
    }

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Continues an annotation gesture that started in a Pin window.</summary>
    public bool TryCaptureExternalAnnotationGesture()
    {
        if (!_annotateInputEnabled || Handle == IntPtr.Zero) return false;
        _nativeMouseCaptured = true;
        NativeMethods.SetCapture(Handle);
        return true;
    }

    public bool Contains(ScreenPoint point)
    {
        var bounds = _screen.Bounds;
        return point.X >= bounds.Left
            && point.X < bounds.Right
            && point.Y >= bounds.Top
            && point.Y < bounds.Bottom;
    }

    public bool Intersects(ScreenRect rect)
    {
        var bounds = _screen.Bounds;
        return new ScreenRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom).Intersects(rect);
    }

    public ScreenRect ScreenBounds
    {
        get
        {
            var bounds = _screen.Bounds;
            return new ScreenRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        }
    }

    public double DistanceSquaredTo(ScreenPoint point)
    {
        var bounds = _screen.Bounds;
        var clampedX = Math.Clamp(point.X, bounds.Left, bounds.Right);
        var clampedY = Math.Clamp(point.Y, bounds.Top, bounds.Bottom);
        var dx = point.X - clampedX;
        var dy = point.Y - clampedY;
        return dx * dx + dy * dy;
    }

    public double DpiScaleX => VisualTreeHelper.GetDpi(this).DpiScaleX;

    public void PositionOverScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var bounds = _screen.Bounds;
        _applyingBounds = true;
        try
        {
            // Positioning one window per monitor must not also choose the
            // keyboard target. OverlayManager activates one explicit window.
            var flags = NativeMethods.SwpShowWindow | NativeMethods.SwpNoActivate;

            NativeMethods.SetWindowPos(
                handle,
                NativeMethods.HwndTopmost,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                flags | NativeMethods.SwpNoOwnerZOrder);
        }
        finally
        {
            _applyingBounds = false;
        }
    }

    public void ReassertTopmost()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
    }

    public void SetInteractionMode(InteractionMode mode)
    {
        _annotateInputEnabled = IsOverlayInputMode(mode);
        if (!_annotateInputEnabled && _nativeMouseCaptured)
        {
            _nativeMouseCaptured = false;
            NativeMethods.ReleaseCapture();
        }

        Focusable = _annotateInputEnabled;
        IsHitTestVisible = _annotateInputEnabled;
        RefreshAnnotationCursor();
        _surface.SetAnnotationInputEnabled(_annotateInputEnabled);
        SyncTextEditor();

        if (_sourceReady)
        {
            ApplyWindowStyles(_annotateInputEnabled);
        }

        _surface.InvalidateVisual();
    }

    public void RefreshAnnotationCursor()
    {
        if (_inputHandler.Mode == InteractionMode.ScreenshotRegionSelect)
        {
            Cursor = AnnotationCursor.ForQuickEditSelection(_rectOverlayProvider(), _quickEditHoverPoint);
            return;
        }

        Cursor = AnnotationCursor.ForOverlay(_inputHandler.Mode, _annotateInputEnabled, _inputHandler.CurrentTool, _inputHandler.IsMoveDragging, _inputHandler.HasMoveSelection);
    }

    public void ActivateKeyboardInput()
    {
        if (!_sourceReady || !_annotateInputEnabled || !IsVisible)
        {
            return;
        }

        FocusKeyboardInputCore();
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            FocusKeyboardInputCore);
    }

    private void FocusKeyboardInputCore()
    {
        if (!_sourceReady || !_annotateInputEnabled || !IsVisible)
        {
            return;
        }

        Activate();
        if (_textEditor is { Visibility: Visibility.Visible })
        {
            Keyboard.Focus(_textEditor);
            return;
        }
        Focus();
        _surface.Focus();
        Keyboard.Focus(_surface);
    }

    public void Refresh()
    {
        SyncTextEditor();
        _surface.InvalidateVisual();
    }

    private void SyncTextEditor()
    {
        var active = _inputHandler.Mode == InteractionMode.ScreenshotRegionSelect
            ? QuickEditInkStore.ActiveText : null;
        if (active is null || active.Points.Count == 0)
        {
            if (_textEditor is not null) _textEditor.Visibility = Visibility.Collapsed;
            _editingCallout = null;
            return;
        }

        var topLeft = _surface.PointFromScreen(new System.Windows.Point(active.Points[0].X, active.Points[0].Y));
        var bottomRight = _surface.PointFromScreen(new System.Windows.Point(
            active.Points[0].X + active.CalloutWidth, active.Points[0].Y + active.CalloutHeight));
        var visible = bottomRight.X > 0 && bottomRight.Y > 0
            && topLeft.X < _surface.ActualWidth && topLeft.Y < _surface.ActualHeight;
        if (!visible)
        {
            if (_textEditor is not null) _textEditor.Visibility = Visibility.Collapsed;
            return;
        }

        if (_textEditor is null)
        {
            _textEditor = new WpfTextBox
            {
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 18,
                FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.Black,
                Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0),
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true, VerticalContentAlignment = VerticalAlignment.Center,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(0), Cursor = WpfCursors.IBeam,
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(_textEditor, "QuickEditCalloutTextEditor");
            _textEditor.TextChanged += (_, _) =>
            {
                QuickEditInkStore.ReplaceActiveText(_textEditor.Text);
                SyncTextEditor();
                _surface.InvalidateVisual();
            };
            _editorLayer.Children.Add(_textEditor);
        }
        var firstActivation = !ReferenceEquals(_editingCallout, active);
        _editingCallout = active;
        _textEditor.Visibility = Visibility.Visible;
        Canvas.SetLeft(_textEditor, topLeft.X + 10);
        Canvas.SetTop(_textEditor, topLeft.Y + 5);
        _textEditor.Width = Math.Max(1, bottomRight.X - topLeft.X - 20);
        _textEditor.Height = Math.Max(1, bottomRight.Y - topLeft.Y - 10);
        if (firstActivation)
        {
            _textEditor.Text = active.Text ?? string.Empty;
            _textEditor.CaretIndex = _textEditor.Text.Length;
            _textEditor.ScrollToEnd();
            Activate();
            Keyboard.Focus(_textEditor);
        }
    }

    public BitmapSource? CaptureSurface()
    {
        if (_surface.ActualWidth <= 1 || _surface.ActualHeight <= 1)
        {
            return null;
        }

        _surface.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(_surface);
        var pixelWidth = Math.Max(1, (int)Math.Round(_surface.ActualWidth * dpi.DpiScaleX));
        var pixelHeight = Math.Max(1, (int)Math.Round(_surface.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var target = new System.Windows.Rect(0, 0, _surface.ActualWidth, _surface.ActualHeight);
            dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, target);
            _surface.RenderSnapshot(dc, OverlayRenderOptions.CaptureStage);
        }

        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // Like CaptureSurface but cropped to a screen rect (physical px), so the Capture
    // Stage snapshots only the source window's area instead of the whole monitor
    // (far less per-frame work and GC pressure).
    public BitmapSource? CaptureSurfaceRegion(ScreenRect sourceRect, OverlayRenderOptions options)
    {
        return CaptureSurfaceRegionCore(sourceRect, dc =>
        {
            _surface.RenderSnapshot(dc, options);
            return true;
        });
    }

    public BitmapSource? CaptureRegionMaskSurfaceRegion(ScreenRect sourceRect)
    {
        return CaptureSurfaceRegionCore(sourceRect, _surface.RenderRegionMaskSnapshot);
    }

    private BitmapSource? CaptureSurfaceRegionCore(
        ScreenRect sourceRect,
        Func<DrawingContext, bool> renderContent)
    {
        if (_surface.ActualWidth <= 1 || _surface.ActualHeight <= 1)
        {
            return null;
        }

        _surface.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(_surface);
        var bounds = _screen.Bounds;
        var widthPx = (int)Math.Round(sourceRect.Right - sourceRect.Left);
        var heightPx = (int)Math.Round(sourceRect.Bottom - sourceRect.Top);
        if (widthPx <= 0 || heightPx <= 0)
        {
            return null;
        }

        var viewLeftDip = (sourceRect.Left - bounds.Left) / dpi.DpiScaleX;
        var viewTopDip = (sourceRect.Top - bounds.Top) / dpi.DpiScaleY;
        var viewWidthDip = widthPx / dpi.DpiScaleX;
        var viewHeightDip = heightPx / dpi.DpiScaleY;
        var bitmap = new RenderTargetBitmap(
            widthPx,
            heightPx,
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        bool rendered;
        using (var dc = visual.RenderOpen())
        {
            var target = new System.Windows.Rect(0, 0, viewWidthDip, viewHeightDip);
            dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, target);
            dc.PushClip(new RectangleGeometry(target));
            dc.PushTransform(new TranslateTransform(-viewLeftDip, -viewTopDip));
            rendered = renderContent(dc);
            dc.Pop();
            dc.Pop();
        }

        if (!rendered)
        {
            return null;
        }

        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void ApplyWindowStyles(bool annotate)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();

        style |= NativeMethods.WsExLayered;
        style |= NativeMethods.WsExToolWindow;
        style &= ~NativeMethods.WsExAppWindow;

        if (annotate)
        {
            style &= ~NativeMethods.WsExTransparent;
            style &= ~NativeMethods.WsExNoActivate;
        }
        else
        {
            style |= NativeMethods.WsExTransparent;
            style |= NativeMethods.WsExNoActivate;
        }

        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new IntPtr(style));
        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpFrameChanged | NativeMethods.SwpNoActivate);
    }

    private static bool IsOverlayInputMode(InteractionMode mode)
    {
        return mode is InteractionMode.Annotate
            or InteractionMode.PinnedLensSelect
            or InteractionMode.StaticPinSelect
            or InteractionMode.RegionMaskSelect
            or InteractionMode.ScreenshotRegionSelect
            or InteractionMode.RegionSpotlightSelect
            or InteractionMode.ScreenBoard
            or InteractionMode.BlackScreen
            or InteractionMode.WhiteScreen;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionOverScreen();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_applyingBounds || !_sourceReady)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var expectedWidth = _screen.Bounds.Width / Math.Max(0.1, dpi.DpiScaleX);
        var expectedHeight = _screen.Bounds.Height / Math.Max(0.1, dpi.DpiScaleY);

        if (e.NewSize.Width < expectedWidth * 0.5 || e.NewSize.Height < expectedHeight * 0.5)
        {
            PositionOverScreen();
        }
    }

    private void OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (!_annotateInputEnabled)
        {
            return;
        }

        var key = e.Key == Key.System
            ? e.SystemKey
            : e.Key == Key.ImeProcessed
                ? e.ImeProcessedKey
                : e.Key;

        if (_textEditor is { IsKeyboardFocusWithin: true } && QuickEditInkStore.ActiveText is not null)
        {
            if (key == Key.Escape)
            {
                QuickEditInkStore.CancelText();
                Refresh();
                FocusKeyboardInputCore();
                e.Handled = true;
            }
            return;
        }

        e.Handled = _inputHandler.HandleOverlayKeyDown(key, Keyboard.Modifiers);
    }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!_annotateInputEnabled)
        {
            return;
        }

        if (_textEditor is { IsKeyboardFocusWithin: true } && QuickEditInkStore.ActiveText is not null)
            return;

        _inputHandler.HandleOverlayTextInput(e.Text);
        e.Handled = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_annotateInputEnabled)
        {
            return IntPtr.Zero;
        }

        // Let WPF's TextBox own mouse selection and caret positioning within its bounds.
        if (!_nativeMouseCaptured
            && msg is (NativeMethods.WmLButtonDown or NativeMethods.WmLButtonDblClk
                or NativeMethods.WmLButtonUp or NativeMethods.WmMouseMove)
            && _textEditor is { Visibility: Visibility.Visible }
            && QuickEditInkStore.ActiveText is not null
            && IsOverTextEditor(ToScreenPoint(hwnd, lParam)))
            return IntPtr.Zero;

        switch (msg)
        {
            case NativeMethods.WmLButtonDown:
            case NativeMethods.WmLButtonDblClk:
                _nativeMouseCaptured = true;
                NativeMethods.SetCapture(hwnd);
                _inputHandler.HandleOverlayMouseDown(ToScreenPoint(hwnd, lParam), MouseButton.Left, Keyboard.Modifiers);
                handled = true;
                break;
            case NativeMethods.WmMouseMove:
                if (_inputHandler.Mode == InteractionMode.ScreenshotRegionSelect)
                {
                    _quickEditHoverPoint = ToScreenPoint(hwnd, lParam);
                    RefreshAnnotationCursor();
                }
                if (_nativeMouseCaptured)
                {
                    _inputHandler.HandleOverlayMouseMove(ToScreenPoint(hwnd, lParam), Keyboard.Modifiers);
                    handled = true;
                }

                break;
            case NativeMethods.WmLButtonUp:
                if (_nativeMouseCaptured)
                {
                    _nativeMouseCaptured = false;
                    NativeMethods.ReleaseCapture();
                    _inputHandler.HandleOverlayMouseUp(ToScreenPoint(hwnd, lParam), MouseButton.Left, Keyboard.Modifiers);
                    handled = true;
                }

                break;
            case NativeMethods.WmRButtonDown:
                _inputHandler.HandleOverlayMouseDown(ToScreenPoint(hwnd, lParam), MouseButton.Right, Keyboard.Modifiers);
                handled = true;
                break;
            case NativeMethods.WmRButtonUp:
                _inputHandler.HandleOverlayMouseUp(ToScreenPoint(hwnd, lParam), MouseButton.Right, Keyboard.Modifiers);
                handled = true;
                break;
            case NativeMethods.WmMouseWheel:
                handled = _inputHandler.HandleOverlayMouseWheel(ToScreenPoint(lParam), GetMouseWheelDelta(wParam), Keyboard.Modifiers);
                break;
            case NativeMethods.WmCancelMode:
            case NativeMethods.WmCaptureChanged:
                if (_nativeMouseCaptured
                    && (msg != NativeMethods.WmCaptureChanged || lParam != hwnd))
                {
                    _nativeMouseCaptured = false;
                    _inputHandler.HandleOverlayCaptureLost();
                    handled = true;
                }

                break;
        }

        return IntPtr.Zero;
    }

    private bool IsOverTextEditor(ScreenPoint point)
    {
        if (_textEditor is null) return false;
        var local = _surface.PointFromScreen(new System.Windows.Point(point.X, point.Y));
        return local.X >= Canvas.GetLeft(_textEditor) && local.X <= Canvas.GetLeft(_textEditor) + _textEditor.Width
            && local.Y >= Canvas.GetTop(_textEditor) && local.Y <= Canvas.GetTop(_textEditor) + _textEditor.Height;
    }

    private static int GetMouseWheelDelta(IntPtr wParam)
    {
        return unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
    }

    private static ScreenPoint ToScreenPoint(IntPtr hwnd, IntPtr lParam)
    {
        var raw = lParam.ToInt64();
        var point = new NativeMethods.Point
        {
            X = unchecked((short)(raw & 0xFFFF)),
            Y = unchecked((short)((raw >> 16) & 0xFFFF))
        };

        NativeMethods.ClientToScreen(hwnd, ref point);
        return new ScreenPoint(point.X, point.Y);
    }

    private static ScreenPoint ToScreenPoint(IntPtr lParam)
    {
        var raw = lParam.ToInt64();
        return new ScreenPoint(
            unchecked((short)(raw & 0xFFFF)),
            unchecked((short)((raw >> 16) & 0xFFFF)));
    }
}
