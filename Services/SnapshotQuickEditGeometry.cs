using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

/// <summary>
/// Pure geometry for the temporary screenshot quick-edit session. Keeping this
/// separate from the overlay makes frame and toolbar behavior deterministic and testable.
/// </summary>
internal static class SnapshotQuickEditGeometry
{
    public const double ToolbarGapPixels = 8;

    public static ScreenRect MoveFrame(ScreenRect frame, ScreenPoint start, ScreenPoint current)
        => frame.Offset(current.X - start.X, current.Y - start.Y);

    public static ScreenRect ResizeFrame(ScreenRect frame, RectResizeHandle handle, ScreenPoint pointer)
    {
        if (handle == RectResizeHandle.None)
        {
            return frame;
        }

        return RectGeometry.CreateResizeRect(RectGeometry.GetResizeAnchor(frame, handle), pointer);
    }

    public static ScreenRect GetDefaultPanelBounds(
        ScreenRect frame,
        double panelWidth,
        double panelHeight,
        ScreenRect workArea)
    {
        if (panelWidth <= 0 || panelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(panelWidth), "Toolbar dimensions must be positive.");
        }

        var left = Math.Clamp(frame.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - panelWidth));
        var top = frame.Bottom + ToolbarGapPixels;
        if (top + panelHeight > workArea.Bottom)
        {
            top = Math.Max(workArea.Top, frame.Top - ToolbarGapPixels - panelHeight);
        }

        return new ScreenRect(left, top, left + panelWidth, top + panelHeight);
    }
}
