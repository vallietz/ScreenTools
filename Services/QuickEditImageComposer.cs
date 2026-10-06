using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusTool.Win.Overlay;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;

namespace FocusTool.Win.Services;

internal static class QuickEditImageComposer
{
    public static BitmapSource Compose(BitmapSource source, ScreenRect frame,
        IReadOnlyList<QuickEditStroke> strokes, BitmapSource? pixelatedSource)
    {
        var width = Math.Max(1, source.PixelWidth);
        var height = Math.Max(1, source.PixelHeight);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(width / Math.Max(1, frame.Width), height / Math.Max(1, frame.Height)));
            var imageRect = new Rect(0, 0, frame.Width, frame.Height);
            context.DrawImage(source, imageRect);
            context.PushClip(new RectangleGeometry(imageRect));
            foreach (var stroke in strokes)
            {
                var stepNumber = stroke.Tool == QuickEditTool.StepBadge
                    ? QuickEditInkStore.StepNumber(strokes, stroke) : 0;
                DrawStroke(context, frame, stroke, imageRect, pixelatedSource, stepNumber);
            }
            context.Pop();
            context.Pop();
        }

        var output = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        output.Render(visual);
        output.Freeze();
        return output;
    }

    private static void DrawStroke(DrawingContext context, ScreenRect frame, QuickEditStroke stroke,
        Rect imageRect, BitmapSource? pixelatedSource, int stepNumber)
    {
        if (stroke.Points.Count == 0) return;
        Point Local(ScreenPoint point) => new(point.X - frame.Left, point.Y - frame.Top);
        var start = Local(stroke.Points[0]);
        var end = Local(stroke.Points[^1]);
        var redPen = new Pen(Brushes.Red, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        redPen.Freeze();

        switch (stroke.Tool)
        {
            case QuickEditTool.StepBadge:
                QuickEditStepBadgeRenderer.Draw(context, start, stepNumber);
                return;
            case QuickEditTool.Text:
                if (string.IsNullOrWhiteSpace(stroke.Text)) return;
                var text = new FormattedText(stroke.Text, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 18, Brushes.Black, 1);
                var handle = Local(stroke.CalloutHandle);
                if (stroke.CalloutTarget is { } target)
                {
                    var tip = Local(target);
                    context.DrawLine(redPen, handle, tip);
                    var direction = tip - handle;
                    if (direction.Length >= 0.01)
                    {
                        direction.Normalize();
                        var back = tip - direction * 11;
                        var wing = new Vector(-direction.Y, direction.X) * 5;
                        context.DrawLine(redPen, tip, back + wing);
                        context.DrawLine(redPen, tip, back - wing);
                    }
                }
                context.DrawRectangle(Brushes.LightYellow, redPen,
                    new Rect(start, new Size((handle.X - start.X) * 2, handle.Y - start.Y)));
                context.DrawText(text, start + new Vector(8, 6));
                return;
            case QuickEditTool.Mosaic:
                if (pixelatedSource is null) return;
                context.PushClip(new RectangleGeometry(new Rect(start, end)));
                context.DrawImage(pixelatedSource, imageRect);
                context.Pop();
                return;
            case QuickEditTool.Rectangle:
                context.DrawRectangle(null, redPen, new Rect(start, end));
                return;
            case QuickEditTool.Ellipse:
                var ellipse = new Rect(start, end);
                context.DrawEllipse(null, redPen,
                    new Point(ellipse.Left + ellipse.Width / 2, ellipse.Top + ellipse.Height / 2),
                    ellipse.Width / 2, ellipse.Height / 2);
                return;
            case QuickEditTool.Line:
                context.DrawLine(redPen, start, end);
                return;
            case QuickEditTool.Arrow:
                var (first, second) = stroke.GetArrowControls();
                var curve = new StreamGeometry();
                using (var path = curve.Open())
                {
                    path.BeginFigure(start, false, false);
                    path.BezierTo(Local(first), Local(second), end, true, true);
                }
                curve.Freeze();
                context.DrawGeometry(null, redPen, curve);
                var tangent = end - Local(second);
                if (tangent.Length < 0.01) tangent = end - start;
                if (tangent.Length >= 0.01)
                {
                    tangent.Normalize();
                    var basePoint = end - tangent * 14;
                    var wing = new Vector(-tangent.Y, tangent.X) * 6;
                    context.DrawLine(redPen, end, basePoint + wing);
                    context.DrawLine(redPen, end, basePoint - wing);
                }
                return;
            default:
                if (stroke.Points.Count < 2) return;
                var polyline = new StreamGeometry();
                using (var path = polyline.Open())
                {
                    path.BeginFigure(start, false, false);
                    path.PolyLineTo(stroke.Points.Skip(1).Select(Local).ToArray(), true, true);
                }
                polyline.Freeze();
                var highlighter = stroke.Tool == QuickEditTool.Highlighter;
                var pen = highlighter
                    ? new Pen(new SolidColorBrush(Color.FromArgb(97, 255, 255, 0)), 18)
                    : redPen;
                context.DrawGeometry(null, pen, polyline);
                return;
        }
    }
}
