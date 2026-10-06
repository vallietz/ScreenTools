using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

internal readonly record struct CalloutTailTriangle(ScreenPoint BaseLeft, ScreenPoint BaseRight, ScreenPoint Tip);
internal enum CalloutEdge { Top, Right, Bottom, Left }
internal readonly record struct CalloutAnchor(CalloutEdge Edge, double Fraction);

internal static class QuickEditCalloutGeometry
{
    public const double MinimumTailHalfBase = 10;

    public static CalloutAnchor ProjectToContour(QuickEditStroke callout, ScreenPoint point)
    {
        var origin = callout.Points[0];
        var left = origin.X;
        var top = origin.Y;
        var right = left + callout.CalloutWidth;
        var bottom = top + callout.CalloutHeight;
        var distances = new[] { Math.Abs(point.Y - top), Math.Abs(point.X - right),
            Math.Abs(point.Y - bottom), Math.Abs(point.X - left) };
        var edge = Array.IndexOf(distances, distances.Min());
        return edge switch
        {
            0 => new CalloutAnchor(CalloutEdge.Top, Math.Clamp((point.X - left) / callout.CalloutWidth, 0.08, 0.92)),
            1 => new CalloutAnchor(CalloutEdge.Right, Math.Clamp((point.Y - top) / callout.CalloutHeight, 0.16, 0.84)),
            2 => new CalloutAnchor(CalloutEdge.Bottom, Math.Clamp((point.X - left) / callout.CalloutWidth, 0.08, 0.92)),
            _ => new CalloutAnchor(CalloutEdge.Left, Math.Clamp((point.Y - top) / callout.CalloutHeight, 0.16, 0.84)),
        };
    }

    public static CalloutTailTriangle TailTriangle(QuickEditStroke callout, int index, ScreenPoint tip)
    {
        var (first, second) = TailBaseEndpoints(callout, index);
        return new CalloutTailTriangle(first, second, tip);
    }

    public static (ScreenPoint First, ScreenPoint Second) TailBaseEndpoints(QuickEditStroke callout, int index)
    {
        var center = callout.GetCalloutBase(index);
        var edge = callout.GetCalloutAnchor(index).Edge;
        var horizontal = edge is CalloutEdge.Top or CalloutEdge.Bottom;
        var sideLength = horizontal ? callout.CalloutWidth : callout.CalloutHeight;
        var distanceFromStart = horizontal ? center.X - callout.Points[0].X : center.Y - callout.Points[0].Y;
        var halfBase = Math.Min(Math.Max(MinimumTailHalfBase, sideLength / 18),
            Math.Max(1, Math.Min(distanceFromStart, sideLength - distanceFromStart) - 2));
        var defaultFirst = center.Offset(horizontal ? -halfBase : 0, horizontal ? 0 : -halfBase);
        var defaultSecond = center.Offset(horizontal ? halfBase : 0, horizontal ? 0 : halfBase);
        var firstAnchor = callout.GetCalloutBaseEndpointAnchor(index, 0);
        var secondAnchor = callout.GetCalloutBaseEndpointAnchor(index, 1);
        return (firstAnchor is { } first ? PointOnContour(callout, first) : defaultFirst,
            secondAnchor is { } second ? PointOnContour(callout, second) : defaultSecond);
    }

    public static ScreenPoint PointOnContour(QuickEditStroke callout, CalloutAnchor anchor)
    {
        var origin = callout.Points[0];
        return anchor.Edge switch
        {
            CalloutEdge.Top => origin.Offset(callout.CalloutWidth * anchor.Fraction, 0),
            CalloutEdge.Right => origin.Offset(callout.CalloutWidth, callout.CalloutHeight * anchor.Fraction),
            CalloutEdge.Bottom => origin.Offset(callout.CalloutWidth * anchor.Fraction, callout.CalloutHeight),
            CalloutEdge.Left => origin.Offset(0, callout.CalloutHeight * anchor.Fraction),
            _ => throw new ArgumentOutOfRangeException(nameof(anchor)),
        };
    }
}
