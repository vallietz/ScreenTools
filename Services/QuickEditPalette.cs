using FocusTool.Win.Overlay;
using Forms = System.Windows.Forms;

namespace FocusTool.Win.Services;

internal enum QuickEditPaletteAction { Pencil, Line, Arrow, Rectangle, Ellipse, Text, Mosaic, Highlighter, StepBadge, Copy, Save, Cancel }

internal static class QuickEditPalette
{
    public const double Width = 528;
    public const double Height = 32;
    public const double Gap = 8;
    public const int ItemCount = 12;

    public static ScreenRect Bounds(ScreenRect frame)
    {
        var desktop = Forms.SystemInformation.VirtualScreen;
        var left = Math.Clamp(frame.Right - Width, desktop.Left, Math.Max(desktop.Left, desktop.Right - Width));
        var top = frame.Bottom + Gap;
        if (top + Height > desktop.Bottom) top = frame.Top - Height - Gap;
        top = Math.Clamp(top, desktop.Top, Math.Max(desktop.Top, desktop.Bottom - Height));
        return new ScreenRect(left, top, left + Width, top + Height);
    }

    public static ScreenRect ItemBounds(ScreenRect frame, int index)
    {
        var bounds = Bounds(frame);
        var itemWidth = Width / ItemCount;
        return new ScreenRect(bounds.Left + index * itemWidth, bounds.Top,
            bounds.Left + (index + 1) * itemWidth, bounds.Bottom);
    }

    public static bool TryHit(ScreenRect frame, ScreenPoint point, out QuickEditPaletteAction action)
    {
        var bounds = Bounds(frame);
        if (!bounds.Contains(point))
        {
            action = default;
            return false;
        }

        var index = Math.Min(ItemCount - 1, (int)((point.X - bounds.Left) / (Width / ItemCount)));
        action = (QuickEditPaletteAction)index;
        return true;
    }

    public static QuickEditTool ToTool(QuickEditPaletteAction action) => action switch
    {
        QuickEditPaletteAction.Line => QuickEditTool.Line,
        QuickEditPaletteAction.Arrow => QuickEditTool.Arrow,
        QuickEditPaletteAction.Rectangle => QuickEditTool.Rectangle,
        QuickEditPaletteAction.Ellipse => QuickEditTool.Ellipse,
        QuickEditPaletteAction.Text => QuickEditTool.Text,
        QuickEditPaletteAction.Mosaic => QuickEditTool.Mosaic,
        QuickEditPaletteAction.Highlighter => QuickEditTool.Highlighter,
        QuickEditPaletteAction.StepBadge => QuickEditTool.StepBadge,
        _ => QuickEditTool.Pencil,
    };
}
