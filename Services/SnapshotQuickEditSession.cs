using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

/// <summary>
/// Interaction state for one temporary screenshot selection. It deliberately
/// owns no window and no image so the UI may be replaced without changing the
/// selection/move/resize rules.
/// </summary>
internal sealed class SnapshotQuickEditSession
{
    private readonly RectEditSession _edit = new();

    public SnapshotQuickEditSession(ScreenRect frame)
    {
        Frame = frame;
    }

    public ScreenRect Frame { get; private set; }
    public bool IsEditingFrame => _edit.IsMoving || _edit.IsResizing;

    public bool TryBeginFrameEdit(ScreenPoint point)
    {
        if (RectGeometry.TryHitResizeHandle(Frame, point, out var handle))
        {
            _edit.BeginResize(Frame, handle);
            return true;
        }

        if (!Frame.Contains(point))
        {
            return false;
        }

        _edit.BeginMove(point);
        return true;
    }

    public void UpdateFrameEdit(ScreenPoint point)
    {
        if (_edit.IsMoving)
        {
            Frame = _edit.Move(Frame, point);
        }
        else if (_edit.IsResizing)
        {
            Frame = _edit.Resize(point);
        }
    }

    public void EndFrameEdit() => _edit.EndPointerAction();
}
