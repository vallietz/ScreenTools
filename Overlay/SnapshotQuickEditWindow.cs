using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;
using WpfPoint = System.Windows.Point;
using WpfButton = System.Windows.Controls.Button;
using WpfPanel = System.Windows.Controls.Panel;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using Cursors = System.Windows.Input.Cursors;
using Size = System.Windows.Size;
using Rectangle = System.Windows.Shapes.Rectangle;
using TextBox = System.Windows.Controls.TextBox;
using PrintDialog = System.Windows.Controls.PrintDialog;

namespace FocusTool.Win.Overlay;

// A transient Lightshot-like overlay. Only _content is rasterized for export.
internal sealed class SnapshotQuickEditWindow : Window
{
    private readonly BitmapSource _source;
    private readonly Func<BitmapSource, Task> _copy;
    private readonly Func<BitmapSource, Task> _save;
    private readonly Canvas _root = new();
    private readonly Canvas _content = new() { ClipToBounds = true };
    private readonly Canvas _ink = new() { Background = Brushes.Transparent };
    private readonly Path _shade = new() { Fill = new SolidColorBrush(Color.FromArgb(155, 0, 0, 0)), IsHitTestVisible = false };
    private ScreenRect _frame;
    private WpfPoint? _start;
    private Shape? _draft;
    private Tool _tool = Tool.Pencil;
    private bool _exporting;

    public SnapshotQuickEditWindow(ScreenRect frame, BitmapSource source, Func<BitmapSource, Task> copy, Func<BitmapSource, Task> save)
    {
        _frame = frame; _source = source; _copy = copy; _save = save;
        var desktop = Forms.SystemInformation.VirtualScreen;
        Left = desktop.Left; Top = desktop.Top; Width = desktop.Width; Height = desktop.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false;
        Content = _root;
        Loaded += (_, _) => Build();
        PreviewKeyDown += OnKeyDown;
    }

    private void Build()
    {
        _root.Children.Add(_shade);
        _root.Children.Add(_content);
        _content.Children.Add(new Image { Source = _source, Stretch = Stretch.Fill, IsHitTestVisible = false });
        _content.Children.Add(_ink);
        _content.MouseLeftButtonDown += StartDraw;
        _content.MouseMove += Draw;
        _content.MouseLeftButtonUp += EndDraw;
        _root.Children.Add(CreatePanel());
        _root.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == _root) Close(); };
        LayoutFrame(); Activate();
    }

    private Border CreatePanel()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3) };
        AddTool(bar, "✎", "Карандаш", Tool.Pencil);
        AddTool(bar, "╱", "Линия", Tool.Line);
        AddTool(bar, "➜", "Стрелка", Tool.Arrow);
        AddTool(bar, "T", "Текст с выноской", Tool.Text);
        AddTool(bar, "▦", "Мозаика", Tool.Mosaic);
        bar.Children.Add(Button("Copy", "Копировать", async () => await ExportAsync(_copy)));
        bar.Children.Add(Button("PNG", "Сохранить PNG", async () => await ExportAsync(_save)));
        bar.Children.Add(Button("Print", "Печать", Print));
        bar.Children.Add(Button("×", "Отмена", Close));
        var panel = new Border { Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = bar };
        panel.MouseLeftButtonDown += (_, e) => e.Handled = true;
        return panel;
    }

    private static WpfButton Button(string text, string tip, Action action)
    {
        var result = new WpfButton { Content = text, ToolTip = tip, Margin = new Thickness(2), Padding = new Thickness(6, 2, 6, 2), MinWidth = 28 };
        result.Click += (_, _) => action(); return result;
    }

    private void AddTool(WpfPanel host, string text, string tip, Tool tool)
    {
        var button = Button(text, tip, () => { _tool = tool; _content.Cursor = Cursors.Cross; });
        button.FontSize = 15; host.Children.Add(button);
    }

    private void LayoutFrame()
    {
        var local = new Rect(_frame.Left - Left, _frame.Top - Top, Math.Max(12, _frame.Width), Math.Max(12, _frame.Height));
        _shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)), new RectangleGeometry(local));
        Canvas.SetLeft(_content, local.Left); Canvas.SetTop(_content, local.Top);
        _content.Width = _ink.Width = local.Width; _content.Height = _ink.Height = local.Height;
        if (_content.Children[0] is Image image) { image.Width = local.Width; image.Height = local.Height; }
        if (_root.Children[^1] is Border panel)
        {
            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(panel, Math.Min(ActualWidth - panel.DesiredSize.Width - 4, local.Right - panel.DesiredSize.Width));
            Canvas.SetTop(panel, Math.Min(ActualHeight - panel.DesiredSize.Height - 4, local.Bottom + 7));
        }
    }

    private void StartDraw(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(_ink);
        if (_tool == Tool.Text) { AddText(_start.Value); return; }
        _draft = MakeShape(_start.Value); _ink.Children.Add(_draft); _content.CaptureMouse(); e.Handled = true;
    }
    private void Draw(object sender, WpfMouseEventArgs e)
    {
        if (_start is not { } start || _draft is null || e.LeftButton != MouseButtonState.Pressed) return;
        var end = e.GetPosition(_ink);
        if (_draft is Line line) { line.X2 = end.X; line.Y2 = end.Y; }
        else if (_draft is Rectangle box) { Canvas.SetLeft(box, Math.Min(start.X, end.X)); Canvas.SetTop(box, Math.Min(start.Y, end.Y)); box.Width = Math.Abs(end.X - start.X); box.Height = Math.Abs(end.Y - start.Y); }
    }
    private void EndDraw(object sender, MouseButtonEventArgs e) { _start = null; _draft = null; _content.ReleaseMouseCapture(); }

    private Shape MakeShape(WpfPoint start)
    {
        if (_tool == Tool.Mosaic) return new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(205, 85, 85, 85)), Stroke = Brushes.Transparent };
        var brush = _tool == Tool.Pencil ? Brushes.Red : Brushes.OrangeRed;
        return new Line { X1 = start.X, Y1 = start.Y, X2 = start.X, Y2 = start.Y, Stroke = brush, StrokeThickness = _tool == Tool.Pencil ? 3 : 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    }
    private void AddText(WpfPoint point)
    {
        var box = new TextBox { Text = "Текст", Background = Brushes.LightYellow, Foreground = Brushes.Black, BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1), MinWidth = 100, Padding = new Thickness(5) };
        Canvas.SetLeft(box, point.X); Canvas.SetTop(box, point.Y); _ink.Children.Add(box); box.Focus(); box.SelectAll();
    }
    private async void OnKeyDown(object sender, WpfKeyEventArgs e) { if (e.Key == Key.Escape) Close(); else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; await ExportAsync(_copy); } }
    private async Task ExportAsync(Func<BitmapSource, Task> action)
    {
        if (_exporting) return; _exporting = true;
        try { var bitmap = new RenderTargetBitmap(Math.Max(1, (int)_content.ActualWidth), Math.Max(1, (int)_content.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(_content); bitmap.Freeze(); await action(bitmap); Close(); }
        finally { _exporting = false; }
    }
    private void Print()
    {
        var printer = new PrintDialog();
        if (printer.ShowDialog() != true) return;
        printer.PrintVisual(_content, "FocusTool quick edit");
    }
    private enum Tool { Pencil, Line, Arrow, Text, Mosaic }
}
