using System.Drawing;

namespace FocusTool.Win.Services;

/// <summary>Erases only transparent Pin ink, never the captured screenshot bitmap.</summary>
internal static class StaticPinInkEraser
{
    public static void Clear(Bitmap ink)
    {
        using var graphics = Graphics.FromImage(ink);
        graphics.Clear(Color.Transparent);
    }

    /// <summary>Deletes the entire connected ink stroke touched by the eraser.</summary>
    public static bool EraseConnectedComponent(Bitmap ink, PointF point, float hitRadius)
    {
        var radius = Math.Max(1, hitRadius);
        var radiusSquared = radius * radius;
        var minX = Math.Max(0, (int)Math.Floor(point.X - radius));
        var maxX = Math.Min(ink.Width - 1, (int)Math.Ceiling(point.X + radius));
        var minY = Math.Max(0, (int)Math.Floor(point.Y - radius));
        var maxY = Math.Min(ink.Height - 1, (int)Math.Ceiling(point.Y + radius));

        Point? seed = null;
        var closestDistanceSquared = float.MaxValue;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var distanceX = x - point.X;
                var distanceY = y - point.Y;
                var distanceSquared = distanceX * distanceX + distanceY * distanceY;
                if (distanceSquared <= radiusSquared && distanceSquared < closestDistanceSquared && HasInk(ink, x, y))
                {
                    closestDistanceSquared = distanceSquared;
                    seed = new Point(x, y);
                }
            }
        }

        if (seed is null) return false;

        var pending = new Queue<Point>();
        var visited = new bool[ink.Width, ink.Height];
        pending.Enqueue(seed.Value);
        visited[seed.Value.X, seed.Value.Y] = true;
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!HasInk(ink, current.X, current.Y)) continue;

            ink.SetPixel(current.X, current.Y, Color.Transparent);
            for (var offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (var offsetX = -1; offsetX <= 1; offsetX++)
                {
                    if (offsetX == 0 && offsetY == 0) continue;
                    var nextX = current.X + offsetX;
                    var nextY = current.Y + offsetY;
                    if (nextX >= 0 && nextX < ink.Width && nextY >= 0 && nextY < ink.Height
                        && !visited[nextX, nextY] && HasInk(ink, nextX, nextY))
                    {
                        visited[nextX, nextY] = true;
                        pending.Enqueue(new Point(nextX, nextY));
                    }
                }
            }
        }

        return true;
    }

    private static bool HasInk(Bitmap ink, int x, int y) => ink.GetPixel(x, y).A != 0;
}
