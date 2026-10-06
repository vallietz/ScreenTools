using FocusTool.Win.Models;
using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

/// <summary>
/// Routes annotation tools that are represented by a straight pointer segment through a static pin.
/// This is deliberately independent of WPF windows and input controllers.
/// </summary>
internal static class StaticPinAnnotationRouter
{
    public static bool CanRoute(AnnotationTool tool)
        => tool is AnnotationTool.Pencil or AnnotationTool.Line or AnnotationTool.Arrow;

    public static IReadOnlyList<StaticPinStrokeFragment> Route(
        AnnotationTool tool,
        ScreenPoint start,
        ScreenPoint end,
        ScreenRect pinBounds)
    {
        if (!CanRoute(tool))
        {
            throw new ArgumentOutOfRangeException(nameof(tool), tool, "Only Pencil, Line, and Arrow segments can be routed through a static pin.");
        }

        return StaticPinGeometry.SplitSegment(start, end, pinBounds);
    }

    /// <summary>
    /// Routes a segment through every pin in visual z-order. Bounds must be supplied
    /// from topmost to backmost: an already claimed fragment is never painted into a
    /// pin underneath it.
    /// </summary>
    public static IReadOnlyList<StaticPinRoutedFragment> RouteAcrossPins(
        AnnotationTool tool,
        ScreenPoint start,
        ScreenPoint end,
        IReadOnlyList<ScreenRect> pinBoundsTopmostFirst)
    {
        if (!CanRoute(tool))
        {
            throw new ArgumentOutOfRangeException(nameof(tool), tool, "Only Pencil, Line, and Arrow segments can be routed through static pins.");
        }

        var pending = new List<StaticPinRoutedFragment>
        {
            new(new StaticPinStrokeFragment(start, end, Inside: false), null)
        };
        for (var index = 0; index < pinBoundsTopmostFirst.Count; index++)
        {
            var next = new List<StaticPinRoutedFragment>();
            foreach (var routed in pending)
            {
                if (routed.PinIndex is not null)
                {
                    next.Add(routed);
                    continue;
                }

                foreach (var fragment in Route(tool, routed.Fragment.Start, routed.Fragment.End, pinBoundsTopmostFirst[index]))
                {
                    next.Add(new StaticPinRoutedFragment(fragment, fragment.Inside ? index : null));
                }
            }

            pending = next;
        }

        return pending;
    }
}

internal readonly record struct StaticPinRoutedFragment(StaticPinStrokeFragment Fragment, int? PinIndex);
