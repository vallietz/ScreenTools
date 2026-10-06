using FocusTool.Win.Overlay;

namespace FocusTool.Win.Models;

internal static class CurvedArrowGeometry
{
    public static (ScreenPoint First, ScreenPoint Second) DefaultControls(ScreenPoint start, ScreenPoint end)
        => (Lerp(start, end, 1.0 / 3), Lerp(start, end, 2.0 / 3));

    public static ScreenPoint PointAt(ScreenPoint start, ScreenPoint first, ScreenPoint second, ScreenPoint end, double t)
    {
        t = Math.Clamp(t, 0, 1);
        var inverse = 1 - t;
        return new ScreenPoint(
            inverse * inverse * inverse * start.X + 3 * inverse * inverse * t * first.X
                + 3 * inverse * t * t * second.X + t * t * t * end.X,
            inverse * inverse * inverse * start.Y + 3 * inverse * inverse * t * first.Y
                + 3 * inverse * t * t * second.Y + t * t * t * end.Y);
    }

    public static ScreenPoint TangentAt(ScreenPoint start, ScreenPoint first, ScreenPoint second, ScreenPoint end, double t)
    {
        t = Math.Clamp(t, 0, 1);
        var inverse = 1 - t;
        return new ScreenPoint(
            3 * inverse * inverse * (first.X - start.X) + 6 * inverse * t * (second.X - first.X) + 3 * t * t * (end.X - second.X),
            3 * inverse * inverse * (first.Y - start.Y) + 6 * inverse * t * (second.Y - first.Y) + 3 * t * t * (end.Y - second.Y));
    }

    public static IReadOnlyList<ScreenPoint> Sample(ScreenPoint start, ScreenPoint first, ScreenPoint second, ScreenPoint end, int segments = 32)
    {
        segments = Math.Max(1, segments);
        return Enumerable.Range(0, segments + 1)
            .Select(index => PointAt(start, first, second, end, index / (double)segments))
            .ToArray();
    }

    private static ScreenPoint Lerp(ScreenPoint start, ScreenPoint end, double amount)
        => new(start.X + (end.X - start.X) * amount, start.Y + (end.Y - start.Y) * amount);
}
