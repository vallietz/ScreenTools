using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

internal static class StaticPinGeometry
{
    private const double SegmentEpsilon = 1e-9;

    /// <summary>
    /// Splits a segment at the edges of <paramref name="bounds"/>. Fragments are returned
    /// in start-to-end order and marked by whether their interior belongs to the bounds.
    /// </summary>
    public static IReadOnlyList<StaticPinStrokeFragment> SplitSegment(
        ScreenPoint start,
        ScreenPoint end,
        ScreenRect bounds)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (Math.Abs(dx) <= SegmentEpsilon && Math.Abs(dy) <= SegmentEpsilon)
        {
            return [new StaticPinStrokeFragment(start, end, bounds.Contains(start))];
        }

        var enter = 0.0;
        var exit = 1.0;
        if (!Clip(-dx, start.X - bounds.Left, ref enter, ref exit)
            || !Clip(dx, bounds.Right - start.X, ref enter, ref exit)
            || !Clip(-dy, start.Y - bounds.Top, ref enter, ref exit)
            || !Clip(dy, bounds.Bottom - start.Y, ref enter, ref exit))
        {
            return [new StaticPinStrokeFragment(start, end, false)];
        }

        var cuts = new List<double>(4) { 0, 1 };
        if (enter > SegmentEpsilon && enter < 1 - SegmentEpsilon) cuts.Add(enter);
        if (exit > SegmentEpsilon && exit < 1 - SegmentEpsilon) cuts.Add(exit);
        cuts.Sort();

        var fragments = new List<StaticPinStrokeFragment>(cuts.Count - 1);
        for (var index = 0; index < cuts.Count - 1; index++)
        {
            var from = cuts[index];
            var to = cuts[index + 1];
            if (to - from <= SegmentEpsilon)
            {
                continue;
            }

            var fragmentStart = At(start, dx, dy, from);
            var fragmentEnd = At(start, dx, dy, to);
            var midpoint = At(start, dx, dy, (from + to) / 2);
            fragments.Add(new StaticPinStrokeFragment(fragmentStart, fragmentEnd, bounds.Contains(midpoint)));
        }

        return fragments;
    }

    private static bool Clip(double p, double q, ref double enter, ref double exit)
    {
        if (Math.Abs(p) <= SegmentEpsilon)
        {
            return q >= -SegmentEpsilon;
        }

        var ratio = q / p;
        if (p < 0)
        {
            if (ratio > exit + SegmentEpsilon) return false;
            if (ratio > enter) enter = ratio;
        }
        else
        {
            if (ratio < enter - SegmentEpsilon) return false;
            if (ratio < exit) exit = ratio;
        }

        return true;
    }

    private static ScreenPoint At(ScreenPoint start, double dx, double dy, double t)
        => new(start.X + dx * t, start.Y + dy * t);

    public static (int Width, int Height) ScaleToMinimum(int sourceWidth, int sourceHeight, int minimumDimension)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || minimumDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Image dimensions and minimum size must be positive.");
        }

        var scale = Math.Max(1.0, (double)minimumDimension / Math.Min(sourceWidth, sourceHeight));
        return (
            Math.Max(1, (int)Math.Round(sourceWidth * scale)),
            Math.Max(1, (int)Math.Round(sourceHeight * scale)));
    }

    public static ScreenRect Resize(ScreenRect bounds, double dx, double dy, StaticPinResizeHandle handle, double minimumWidth)
    {
        var aspect = bounds.Height > 0 ? bounds.Width / bounds.Height : 1;
        var minimumHeight = minimumWidth / aspect;
        var left = bounds.Left;
        var top = bounds.Top;
        double width;
        double height;

        if (handle is StaticPinResizeHandle.Left or StaticPinResizeHandle.Right)
        {
            width = Math.Max(minimumWidth, bounds.Width + (handle == StaticPinResizeHandle.Right ? dx : -dx));
            height = width / aspect;
            top -= (height - bounds.Height) / 2;
            if (handle == StaticPinResizeHandle.Left) left = bounds.Right - width;
        }
        else if (handle is StaticPinResizeHandle.Top or StaticPinResizeHandle.Bottom)
        {
            height = Math.Max(minimumHeight, bounds.Height + (handle == StaticPinResizeHandle.Bottom ? dy : -dy));
            width = height * aspect;
            left -= (width - bounds.Width) / 2;
            if (handle == StaticPinResizeHandle.Top) top = bounds.Bottom - height;
        }
        else
        {
            var growX = handle is StaticPinResizeHandle.TopLeft or StaticPinResizeHandle.BottomLeft ? -dx : dx;
            var growY = handle is StaticPinResizeHandle.TopLeft or StaticPinResizeHandle.TopRight ? -dy : dy;
            width = Math.Max(minimumWidth, (bounds.Width + growX + (bounds.Height + growY) * aspect) / 2);
            height = width / aspect;
            if (handle is StaticPinResizeHandle.TopLeft or StaticPinResizeHandle.BottomLeft) left = bounds.Right - width;
            if (handle is StaticPinResizeHandle.TopLeft or StaticPinResizeHandle.TopRight) top = bounds.Bottom - height;
        }

        return new ScreenRect(left, top, left + width, top + height);
    }

    public static ScreenRect ResizeFromBottomRight(ScreenRect bounds, ScreenPoint pointer, double minimumWidth)
    {
        var aspectRatio = bounds.Height > 0 ? bounds.Width / bounds.Height : 1;
        var minimumHeight = minimumWidth / aspectRatio;
        var widthFromPointer = Math.Max(0, pointer.X - bounds.Left);
        var heightFromPointer = Math.Max(0, pointer.Y - bounds.Top);
        var width = Math.Max(minimumWidth, (widthFromPointer + heightFromPointer * aspectRatio) / 2);
        var height = width / aspectRatio;
        return new ScreenRect(bounds.Left, bounds.Top, bounds.Left + width, bounds.Top + Math.Max(minimumHeight, height));
    }
}

internal readonly record struct StaticPinStrokeFragment(ScreenPoint Start, ScreenPoint End, bool Inside);

internal enum StaticPinResizeHandle
{
    Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight
}
