using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FocusTool.Win.Overlay;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;
using FlowDirection = System.Windows.FlowDirection;

namespace FocusTool.Win.Services;

internal static class QuickEditCalloutRenderer
{
    private static readonly Pen Border = new(Brushes.Gray, 1.5);
    private static readonly System.Windows.Media.Brush Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 184));

    public static void Draw(DrawingContext context, QuickEditStroke callout,
        Func<ScreenPoint, Point> toLocal, bool editing)
    {
        var origin = toLocal(callout.Points[0]);
        var opposite = toLocal(callout.Points[0].Offset(callout.CalloutWidth, callout.CalloutHeight));
        var box = new Rect(origin, opposite);
        Geometry silhouette = new RectangleGeometry(box, 8, 8);
        for (var index = 0; index < 2; index++)
        {
            var target = index == 0 ? callout.CalloutTarget : callout.CalloutTarget2;
            if (target is not { } tip) continue;
            var source = QuickEditCalloutGeometry.TailTriangle(callout, index, tip);
            var triangle = new StreamGeometry();
            using (var path = triangle.Open())
            {
                path.BeginFigure(toLocal(source.BaseLeft), true, true);
                path.LineTo(toLocal(source.Tip), true, true);
                path.LineTo(toLocal(source.BaseRight), true, true);
            }
            triangle.Freeze();
            silhouette = new CombinedGeometry(GeometryCombineMode.Union, silhouette, triangle);
        }
        context.DrawGeometry(Fill, Border, silhouette);

        if (editing) return; // A real TextBox renders and edits the text above this silhouette.
        var text = new FormattedText(callout.Text ?? string.Empty,
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            18, Brushes.Black, 1)
        {
            MaxTextWidth = Math.Max(1, box.Width - 20),
            TextAlignment = TextAlignment.Center,
        };
        context.DrawText(text, new Point(origin.X + 10, origin.Y + (box.Height - text.Height) / 2));
    }
}
