namespace FocusTool.Win.Overlay;

internal readonly record struct BurnTrailFrame(double Opacity);

internal sealed class TrailModel
{
    internal const double BurnFadeDurationMs = 7_000;
    private readonly List<TrailPoint> _points = [];
    private double _burnFinishedAtMs = -1;

    public IReadOnlyList<TrailPoint> Points => _points;
    public double LastMovementMs { get; private set; } = -1;
    public bool IsCapturingBurn { get; private set; }
    public bool HasBurnTrail => IsCapturingBurn || _burnFinishedAtMs >= 0;

    public void AddPoint(ScreenPoint point, double nowMs)
    {
        _points.Add(new TrailPoint(point.X, point.Y, nowMs));
        LastMovementMs = nowMs;
    }

    public void TouchLastPoint(ScreenPoint point, double nowMs)
    {
        if (_points.Count == 0)
        {
            AddPoint(point, nowMs);
            return;
        }

        _points[^1] = new TrailPoint(point.X, point.Y, nowMs);
        LastMovementMs = nowMs;
    }

    public void Clear()
    {
        _points.Clear();
        LastMovementMs = -1;
        IsCapturingBurn = false;
        _burnFinishedAtMs = -1;
    }

    public void TrimWhileMoving(double nowMs, int trailLengthMs)
    {
        if (IsCapturingBurn)
        {
            return;
        }

        RemoveOlderThan(nowMs - trailLengthMs);
    }

    public void BeginBurn()
    {
        IsCapturingBurn = true;
        _burnFinishedAtMs = -1;
    }

    public void EndBurn(double nowMs)
    {
        if (!IsCapturingBurn)
        {
            return;
        }

        IsCapturingBurn = false;
        _burnFinishedAtMs = nowMs;
        for (var index = 0; index < _points.Count; index++)
        {
            var point = _points[index];
            _points[index] = new TrailPoint(point.X, point.Y, nowMs);
        }

        LastMovementMs = nowMs;
    }

    public bool TryGetBurnFrame(double nowMs, out BurnTrailFrame frame)
    {
        if (IsCapturingBurn)
        {
            frame = new BurnTrailFrame(0.55);
            return true;
        }

        if (_burnFinishedAtMs < 0)
        {
            frame = default;
            return false;
        }

        var opacity = 0.55 * (1 - (nowMs - _burnFinishedAtMs) / BurnFadeDurationMs);
        if (opacity <= 0)
        {
            frame = default;
            return false;
        }

        frame = new BurnTrailFrame(opacity);
        return true;
    }

    public void TrimWhileStationary(int trailLengthMs)
    {
        if (LastMovementMs < 0)
        {
            _points.Clear();
            return;
        }

        RemoveOlderThan(LastMovementMs - trailLengthMs);
    }

    private void RemoveOlderThan(double cutoffMs)
    {
        var firstVisible = 0;
        while (firstVisible < _points.Count && _points[firstVisible].TimeMs < cutoffMs)
        {
            firstVisible++;
        }

        if (firstVisible > 0)
        {
            _points.RemoveRange(0, firstVisible);
        }
    }
}
