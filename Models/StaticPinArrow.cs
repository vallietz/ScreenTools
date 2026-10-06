using FocusTool.Win.Overlay;

namespace FocusTool.Win.Models;

/// <summary>An editable arrow stored in pin-image coordinates, independent of pin screen position and scale.</summary>
internal sealed class StaticPinArrow(ScreenPoint start, ScreenPoint end, string color, double thickness)
{
    public ScreenPoint Start { get; set; } = start;
    public ScreenPoint End { get; set; } = end;
    public ScreenPoint? Control1 { get; private set; }
    public ScreenPoint? Control2 { get; private set; }
    public string Color { get; } = color;
    public double Thickness { get; } = thickness;

    public (ScreenPoint First, ScreenPoint Second) GetControls()
    {
        var (first, second) = CurvedArrowGeometry.DefaultControls(Start, End);
        return (Control1 ?? first, Control2 ?? second);
    }

    public bool TryHitControl(ScreenPoint point, out int index, double tolerance = 10)
    {
        var (first, second) = GetControls();
        index = Start.DistanceTo(point) <= tolerance ? 3
            : End.DistanceTo(point) <= tolerance ? 4
            : first.DistanceTo(point) <= tolerance ? 1
            : second.DistanceTo(point) <= tolerance ? 2 : 0;
        return index != 0;
    }

    public void SetControl(int index, ScreenPoint point)
    {
        if (index == 1) Control1 = point;
        else if (index == 2) Control2 = point;
        else if (index == 3) Start = point;
        else if (index == 4) End = point;
        else throw new ArgumentOutOfRangeException(nameof(index));
    }
}
