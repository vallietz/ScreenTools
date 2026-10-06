using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using FlowDirection = System.Windows.FlowDirection;

namespace FocusTool.Win.Services;

internal static class QuickEditStepBadgeRenderer
{
    private static readonly Brush Fill = Brushes.Red;
    private static readonly Pen Border = new(Brushes.Black, 1.5);
    private static readonly Typeface NumberTypeface = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public static void Draw(DrawingContext context, Point center, int number)
    {
        context.DrawEllipse(Fill, Border, center, QuickEditInkStore.StepBadgeRadius, QuickEditInkStore.StepBadgeRadius);

        var digits = Math.Max(1, number).ToString(CultureInfo.InvariantCulture);
        var fontSize = digits.Length switch { 1 => 19d, 2 => 15d, 3 => 12d, _ => 10d };
        FormattedText text;
        do
        {
            text = new FormattedText(digits, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                NumberTypeface, fontSize, Brushes.White, 1);
            if (text.WidthIncludingTrailingWhitespace <= 25 && text.Height <= 25) break;
            fontSize -= 0.5;
        } while (fontSize >= 7);

        context.DrawText(text, new Point(
            center.X - text.WidthIncludingTrailingWhitespace / 2,
            center.Y - text.Height / 2));
    }
}
