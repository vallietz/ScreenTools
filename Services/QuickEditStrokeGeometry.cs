using System.Windows.Media;
using FocusTool.Win.Models;
using FocusTool.Win.Overlay;
using WpfPoint = System.Windows.Point;

namespace FocusTool.Win.Services;

internal static class QuickEditStrokeGeometry
{
    public static IReadOnlyList<ScreenPoint> Smooth(IReadOnlyList<ScreenPoint> points)
    {
        return AnnotationStrokeGeometry.Smooth(points, StrokeSmoothingLevel.Balanced, finalize: true);
    }

    public static Geometry BuildSmoothGeometry(
        IReadOnlyList<ScreenPoint> points,
        Func<ScreenPoint, WpfPoint> toLocal)
    {
        var smoothed = Smooth(points);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(toLocal(smoothed[0]), isFilled: false, isClosed: false);
            if (smoothed.Count == 2)
            {
                context.LineTo(toLocal(smoothed[1]), isStroked: true, isSmoothJoin: true);
            }
            else
            {
                for (var index = 1; index < smoothed.Count - 1; index++)
                {
                    var midpoint = new ScreenPoint(
                        (smoothed[index].X + smoothed[index + 1].X) / 2,
                        (smoothed[index].Y + smoothed[index + 1].Y) / 2);
                    context.QuadraticBezierTo(
                        toLocal(smoothed[index]),
                        toLocal(midpoint),
                        isStroked: true,
                        isSmoothJoin: true);
                }

                context.LineTo(toLocal(smoothed[^1]), isStroked: true, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        return geometry;
    }
}
