using System.Windows.Input;
using FocusTool.Win.Models;
using FocusTool.Win.Overlay;
using System.Windows.Media.Imaging;
using Shortcut = FocusTool.Win.Native.Shortcut;

namespace FocusTool.Win.Services;

internal sealed class RectToolsInputController
{
    private const string ExitVisualShortcut = "Esc";

    private readonly RectSelectionController _selection;
    private readonly RegionMaskController _masks;
    private readonly RegionSpotlightController _spotlights;
    private readonly Func<InteractionMode> _modeProvider;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Action<InteractionMode> _setMode;
    private readonly Action _invalidateOverlay;
    private readonly Action _notifyStateChanged;
    private readonly Action _registerHotKeys;
    private readonly Action<ScreenRect> _openPinnedLens;
    private readonly Action<ScreenRect> _openStaticPin;
    private readonly Action _completeStaticPinSelection;
    private readonly Action _showToolbar;
    private readonly Func<ScreenRect, Task<BitmapSource?>> _captureQuickEditSource;
    private readonly Func<BitmapSource, Task> _copyQuickEditImage;
    private readonly Func<BitmapSource, Task> _saveQuickEditImage;
    private readonly Action<ScreenPoint, int> _showMaskContextMenu;
    private bool _quickEditDrawing;
    private bool _quickEditExporting;
    private Task? _quickEditSourceTask;

    public RectToolsInputController(
        RectSelectionController selection,
        RegionMaskController masks,
        RegionSpotlightController spotlights,
        Func<InteractionMode> modeProvider,
        Func<AppSettings> settingsProvider,
        Action<InteractionMode> setMode,
        Action invalidateOverlay,
        Action notifyStateChanged,
        Action registerHotKeys,
        Action<ScreenRect> openPinnedLens,
        Action<ScreenRect> openStaticPin,
        Action completeStaticPinSelection,
        Action showToolbar,
        Func<ScreenRect, Task<BitmapSource?>> captureQuickEditSource,
        Func<BitmapSource, Task> copyQuickEditImage,
        Func<BitmapSource, Task> saveQuickEditImage,
        Action<ScreenPoint, int> showMaskContextMenu)
    {
        _selection = selection;
        _masks = masks;
        _spotlights = spotlights;
        _modeProvider = modeProvider;
        _settingsProvider = settingsProvider;
        _setMode = setMode;
        _invalidateOverlay = invalidateOverlay;
        _notifyStateChanged = notifyStateChanged;
        _registerHotKeys = registerHotKeys;
        _openPinnedLens = openPinnedLens;
        _openStaticPin = openStaticPin;
        _completeStaticPinSelection = completeStaticPinSelection;
        _showToolbar = showToolbar;
        _captureQuickEditSource = captureQuickEditSource;
        _copyQuickEditImage = copyQuickEditImage;
        _saveQuickEditImage = saveQuickEditImage;
        _showMaskContextMenu = showMaskContextMenu;
    }

    public void HandleMouseDown(ScreenPoint point, MouseButton button)
    {
        switch (_modeProvider())
        {
            case InteractionMode.PinnedLensSelect:
                HandlePinnedLensMouseDown(point, button);
                break;
            case InteractionMode.StaticPinSelect:
                HandlePinnedLensMouseDown(point, button);
                break;
            case InteractionMode.ScreenshotRegionSelect:
                HandleScreenshotRegionMouseDown(point, button);
                break;
            case InteractionMode.RegionSpotlightSelect:
                HandleRegionSpotlightMouseDown(point, button);
                break;
            case InteractionMode.RegionMaskSelect:
                HandleRegionMaskMouseDown(point, button);
                break;
        }
    }

    public void HandleMouseMove(ScreenPoint point)
    {
        switch (_modeProvider())
        {
            case InteractionMode.PinnedLensSelect:
                InvalidateIf(_selection.UpdateDraft(point));
                break;
            case InteractionMode.StaticPinSelect:
                InvalidateIf(_selection.UpdateDraft(point));
                break;
            case InteractionMode.ScreenshotRegionSelect:
                if (QuickEditInkStore.IsResizeDragging)
                {
                    QuickEditInkStore.UpdateResizeDrag(point);
                    _invalidateOverlay();
                    return;
                }
                if (QuickEditInkStore.IsStrokeDragging)
                {
                    QuickEditInkStore.UpdateStrokeDrag(point);
                    _invalidateOverlay();
                    return;
                }
                if (QuickEditInkStore.IsCalloutPointerDragging)
                {
                    QuickEditInkStore.UpdateCalloutPointerDrag(point);
                    _invalidateOverlay();
                    return;
                }
                if (QuickEditInkStore.IsArrowControlDragging)
                {
                    QuickEditInkStore.UpdateArrowControlDrag(point);
                    _invalidateOverlay();
                    return;
                }
                if (_quickEditDrawing)
                {
                    QuickEditInkStore.Add(point);
                    _invalidateOverlay();
                    return;
                }
                if (_selection.UpdateScreenshotEdit(point))
                {
                    _invalidateOverlay();
                    return;
                }

                InvalidateIf(_selection.UpdateDraft(point));
                break;
            case InteractionMode.RegionSpotlightSelect:
                if (_spotlights.UpdateEdit(point))
                {
                    _invalidateOverlay();
                    return;
                }

                InvalidateIf(_selection.UpdateDraft(point));
                break;
            case InteractionMode.RegionMaskSelect:
                if (_masks.UpdateEdit(point))
                {
                    _invalidateOverlay();
                    return;
                }

                InvalidateIf(_selection.UpdateDraft(point));
                break;
        }
    }

    public void HandleMouseUp(ScreenPoint point, MouseButton button)
    {
        switch (_modeProvider())
        {
            case InteractionMode.PinnedLensSelect:
                HandlePinnedLensMouseUp(point, button);
                break;
            case InteractionMode.StaticPinSelect:
                HandleStaticPinMouseUp(point, button);
                break;
            case InteractionMode.ScreenshotRegionSelect:
                HandleScreenshotRegionMouseUp(point, button);
                break;
            case InteractionMode.RegionSpotlightSelect:
                HandleRegionSpotlightMouseUp(point, button);
                break;
            case InteractionMode.RegionMaskSelect:
                HandleRegionMaskMouseUp(point, button);
                break;
        }
    }

    public bool HandleMouseWheel(ScreenPoint point, int delta, ModifierKeys modifiers)
    {
        return false;
    }

    public void HandleCaptureLost()
    {
        switch (_modeProvider())
        {
            case InteractionMode.PinnedLensSelect:
                _setMode(InteractionMode.Passthrough);
                break;
            case InteractionMode.StaticPinSelect:
                _selection.CancelDraft();
                _setMode(InteractionMode.Passthrough);
                break;
            case InteractionMode.ScreenshotRegionSelect:
                QuickEditInkStore.EndCalloutPointerDrag();
                QuickEditInkStore.EndArrowControlDrag();
                QuickEditInkStore.EndStrokeDrag();
                QuickEditInkStore.EndResizeDrag();
                _selection.CancelScreenshotPointerState();
                _invalidateOverlay();
                break;
            case InteractionMode.RegionSpotlightSelect:
                _spotlights.CancelEdit();
                if (_selection.IsDraftActive)
                {
                    _selection.CancelDraft();
                }

                _invalidateOverlay();
                break;
            case InteractionMode.RegionMaskSelect:
                if (_masks.IsMoving || _masks.IsResizing)
                {
                    _masks.CancelEdit();
                    _invalidateOverlay();
                }

                if (_selection.IsDraftActive)
                {
                    _selection.CancelDraft();
                    _invalidateOverlay();
                }

                break;
        }
    }

    public bool HandleKeyDown(Key key, ModifierKeys modifiers)
    {
        var mode = _modeProvider();
        if (mode == InteractionMode.ScreenshotRegionSelect && QuickEditInkStore.ActiveText is not null)
        {
            if (key == Key.Escape)
            {
                QuickEditInkStore.CancelText();
                _invalidateOverlay();
                return true;
            }
            if (key == Key.Back)
            {
                QuickEditInkStore.BackspaceText();
                _invalidateOverlay();
                return true;
            }
            if (key == Key.Enter)
            {
                QuickEditInkStore.AppendText("\n");
                _invalidateOverlay();
                return true;
            }
        }
        if (Matches(key, modifiers, ExitVisualShortcut)
            || Matches(key, modifiers, _settingsProvider().Shortcuts.ExitAnnotate))
        {
            _setMode(InteractionMode.Passthrough);
            return true;
        }

        if (mode == InteractionMode.RegionMaskSelect)
        {
            if ((key == Key.Back || key == Key.Delete) && modifiers == ModifierKeys.None)
            {
                DeleteSelectedRegionMask();
                return true;
            }

            return false;
        }

        if (mode == InteractionMode.ScreenshotRegionSelect)
        {
            if (key == Key.F2 && modifiers == ModifierKeys.None && QuickEditInkStore.BeginEditingSelectedText())
            {
                _invalidateOverlay();
                return true;
            }
            if (key == Key.Z && modifiers == ModifierKeys.Control)
            {
                if (QuickEditInkStore.Undo()) _invalidateOverlay();
                return true;
            }
            if ((key == Key.Y && modifiers == ModifierKeys.Control)
                || (key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
            {
                if (QuickEditInkStore.Redo()) _invalidateOverlay();
                return true;
            }
            if (key == Key.C && modifiers == ModifierKeys.Control)
            {
                _ = CommitPendingScreenshotRegionAsync(save: false);
                return true;
            }
            if ((key == Key.Back || key == Key.Delete) && modifiers == ModifierKeys.None)
            {
                if (QuickEditInkStore.DeleteSelected())
                {
                    _invalidateOverlay();
                    return true;
                }
                DeletePendingScreenshotRegion();
                return true;
            }

            return TryNudgeScreenshotRegion(key, modifiers);
        }

        if (mode == InteractionMode.RegionSpotlightSelect)
        {
            if ((key == Key.Back || key == Key.Delete) && modifiers == ModifierKeys.None)
            {
                DeleteSelectedSpotlightRegion();
                return true;
            }

            if (key == Key.Enter && modifiers == ModifierKeys.None)
            {
                _setMode(InteractionMode.Passthrough);
                return true;
            }

            return TryNudgeSelectedSpotlightRegion(key, modifiers);
        }

        return false;
    }

    public void HandleTextInput(string text)
    {
        if (_modeProvider() != InteractionMode.ScreenshotRegionSelect || QuickEditInkStore.ActiveText is null) return;
        QuickEditInkStore.AppendText(text);
        _invalidateOverlay();
    }

    private void HandlePinnedLensMouseDown(ScreenPoint point, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        _selection.BeginDraft(point);
        _invalidateOverlay();
    }

    private void HandleScreenshotRegionMouseDown(ScreenPoint point, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        if (QuickEditInkStore.ActiveText is not null)
        {
            QuickEditInkStore.CommitText();
            _invalidateOverlay();
            if (_selection.PendingScreenshotRegion is not { } textFrame
                || !QuickEditPalette.TryHit(textFrame, point, out _)) return;
        }

        if (_selection.PendingScreenshotRegion is { } frame
            && QuickEditPalette.TryHit(frame, point, out var action))
        {
            if (action is QuickEditPaletteAction.Copy or QuickEditPaletteAction.Save)
            {
                _ = CommitPendingScreenshotRegionAsync(save: action == QuickEditPaletteAction.Save);
            }
            else if (action == QuickEditPaletteAction.Cancel)
            {
                DeletePendingScreenshotRegion();
                _setMode(InteractionMode.Passthrough);
            }
            else
            {
                QuickEditInkStore.Tool = QuickEditPalette.ToTool(action);
                _invalidateOverlay();
            }

            return;
        }

        if (_selection.PendingScreenshotRegion is not null && QuickEditInkStore.TryBeginCalloutPointerDrag(point))
        {
            _invalidateOverlay();
            return;
        }

        if (_selection.PendingScreenshotRegion is { } selected && selected.Contains(point))
        {
            if (QuickEditInkStore.TryBeginResizeDrag(point))
            {
                _invalidateOverlay();
                return;
            }
            if (QuickEditInkStore.TryBeginArrowControlDrag(point))
            {
                _invalidateOverlay();
                return;
            }
            if (QuickEditInkStore.TryBeginStrokeDrag(point))
            {
                _invalidateOverlay();
                return;
            }
            if (QuickEditInkStore.Tool == QuickEditTool.Text)
            {
                QuickEditInkStore.BeginText(point);
                _invalidateOverlay();
                return;
            }
            if (QuickEditInkStore.Tool == QuickEditTool.StepBadge)
            {
                QuickEditInkStore.PlaceStepBadge(point);
                _invalidateOverlay();
                return;
            }

            QuickEditInkStore.Begin(point);
            _quickEditDrawing = true;
            return;
        }

        if (_selection.PendingScreenshotRegion is not null && _selection.TryBeginScreenshotEdit(point))
        {
            return;
        }

        _selection.BeginDraft(point);
        _invalidateOverlay();
    }

    private void HandleRegionSpotlightMouseDown(ScreenPoint point, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        if (_spotlights.TryHitResizeHandle(point, out var resizeIndex, out var resizeHandle))
        {
            _spotlights.BeginResize(resizeIndex, resizeHandle);
            _selection.CancelDraft();
            _invalidateOverlay();
            return;
        }

        if (_spotlights.TryHit(point, out var moveIndex))
        {
            _spotlights.BeginMove(moveIndex, point);
            _selection.CancelDraft();
            _invalidateOverlay();
            return;
        }

        _spotlights.ClearSelection();
        _selection.BeginDraft(point);
        _invalidateOverlay();
    }

    private void HandleRegionMaskMouseDown(ScreenPoint point, MouseButton button)
    {
        if (button == MouseButton.Right)
        {
            if (_masks.TryHit(point, out var mask))
            {
                _masks.Select(mask.Id);
                _invalidateOverlay();
                _showMaskContextMenu(point, mask.Id);
            }

            return;
        }

        if (button != MouseButton.Left)
        {
            return;
        }

        if (_masks.TryHitResizeHandle(point, out var resizeMask, out var resizeHandle))
        {
            _masks.BeginResize(resizeMask, resizeHandle);
            _selection.CancelDraft();
            _invalidateOverlay();
            return;
        }

        if (_masks.TryHit(point, out var existingMask))
        {
            _masks.BeginMove(existingMask, point);
            _selection.CancelDraft();
            _invalidateOverlay();
            return;
        }

        _masks.ClearSelection();
        _masks.CancelEdit();
        _selection.BeginDraft(point);
        _invalidateOverlay();
    }

    private void HandlePinnedLensMouseUp(ScreenPoint point, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        var sourceRect = _selection.CompleteDraft(point);
        if (sourceRect is null)
        {
            return;
        }

        var completedSourceRect = sourceRect.Value;
        _setMode(InteractionMode.Passthrough);
        if (completedSourceRect.Width >= 16 && completedSourceRect.Height >= 16)
        {
            _openPinnedLens(completedSourceRect);
        }
    }

    private void HandleStaticPinMouseUp(ScreenPoint point, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        var sourceRect = _selection.CompleteDraft(point);
        if (sourceRect is null)
        {
            return;
        }

        var completedSourceRect = sourceRect.Value;
        _setMode(InteractionMode.Passthrough);
        if (RectGeometry.IsLargeEnough(completedSourceRect))
        {
            _completeStaticPinSelection();
            _openStaticPin(completedSourceRect);
        }
    }

    private void HandleScreenshotRegionMouseUp(ScreenPoint point, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        if (QuickEditInkStore.IsResizeDragging)
        {
            QuickEditInkStore.UpdateResizeDrag(point);
            QuickEditInkStore.EndResizeDrag();
            _invalidateOverlay();
            return;
        }

        if (QuickEditInkStore.IsStrokeDragging)
        {
            QuickEditInkStore.UpdateStrokeDrag(point);
            QuickEditInkStore.EndStrokeDrag();
            _invalidateOverlay();
            return;
        }

        if (QuickEditInkStore.IsCalloutPointerDragging)
        {
            QuickEditInkStore.UpdateCalloutPointerDrag(point);
            QuickEditInkStore.EndCalloutPointerDrag();
            _invalidateOverlay();
            return;
        }

        if (QuickEditInkStore.IsArrowControlDragging)
        {
            QuickEditInkStore.UpdateArrowControlDrag(point);
            QuickEditInkStore.EndArrowControlDrag();
            _invalidateOverlay();
            return;
        }

        if (_quickEditDrawing)
        {
            QuickEditInkStore.Add(point);
            QuickEditInkStore.Commit();
            _quickEditDrawing = false;
            _invalidateOverlay();
            return;
        }

        if (_selection.IsScreenshotRegionResizing)
        {
            _selection.EndScreenshotPointerAction();
            if (_selection.PendingScreenshotRegion is { } resized) _quickEditSourceTask = RefreshQuickEditSourceAsync(resized);
            _invalidateOverlay();
            return;
        }

        if (_selection.IsScreenshotRegionMoving)
        {
            _selection.EndScreenshotPointerAction();
            if (_selection.PendingScreenshotRegion is { } moved) _quickEditSourceTask = RefreshQuickEditSourceAsync(moved);
            _invalidateOverlay();
            return;
        }

        var sourceRect = _selection.CompleteDraft(point);
        if (sourceRect is null)
        {
            return;
        }

        var completedSourceRect = sourceRect.Value;
        if (RectGeometry.IsLargeEnough(completedSourceRect))
        {
            _selection.SetPendingScreenshotRegion(completedSourceRect);
            _notifyStateChanged();
            _quickEditSourceTask = RefreshQuickEditSourceAsync(completedSourceRect);
        }

        _invalidateOverlay();
    }

    private async Task RefreshQuickEditSourceAsync(ScreenRect frame)
    {
        var source = await _captureQuickEditSource(frame);
        if (source is not null && _selection.PendingScreenshotRegion == frame
            && _modeProvider() == InteractionMode.ScreenshotRegionSelect)
        {
            QuickEditInkStore.SetSource(frame, source);
            _invalidateOverlay();
        }
    }

    private void HandleRegionSpotlightMouseUp(ScreenPoint point, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        if (_spotlights.IsResizing)
        {
            _spotlights.EndPointerAction();
            _invalidateOverlay();
            return;
        }

        if (_spotlights.IsMoving)
        {
            _spotlights.EndPointerAction();
            _invalidateOverlay();
            return;
        }

        var sourceRect = _selection.CompleteDraft(point);
        if (sourceRect is null)
        {
            return;
        }

        var completedSourceRect = sourceRect.Value;
        if (RectGeometry.IsLargeEnough(completedSourceRect))
        {
            var hadSpotlightRegions = _spotlights.HasRegions;
            _spotlights.Add(completedSourceRect);
            if (!hadSpotlightRegions)
            {
                _registerHotKeys();
            }
        }

        _selection.RestoreToolbarAfterRectSelection();
        _invalidateOverlay();
        _notifyStateChanged();
    }

    private void HandleRegionMaskMouseUp(ScreenPoint point, MouseButton button)
    {
        if (_masks.IsResizing && button == MouseButton.Left)
        {
            _masks.EndPointerAction();
            _invalidateOverlay();
            return;
        }

        if (_masks.IsMoving && button == MouseButton.Left)
        {
            _masks.EndPointerAction();
            _invalidateOverlay();
            return;
        }

        if (button != MouseButton.Left)
        {
            return;
        }

        var maskRect = _selection.CompleteDraft(point);
        if (maskRect is null)
        {
            return;
        }

        var completedMaskRect = maskRect.Value;
        if (RectGeometry.IsLargeEnough(completedMaskRect))
        {
            _masks.Add(completedMaskRect, _settingsProvider());
        }

        _selection.RestoreToolbarAfterRectSelection();
        _invalidateOverlay();
        _notifyStateChanged();
    }

    private void DeleteSelectedRegionMask()
    {
        if (!_masks.DeleteSelected())
        {
            return;
        }

        _selection.CancelDraft();
        _invalidateOverlay();
        _notifyStateChanged();
    }

    private void DeleteSelectedSpotlightRegion()
    {
        var hadRegions = _spotlights.HasRegions;
        if (!_spotlights.DeleteSelected())
        {
            return;
        }

        if (hadRegions && !_spotlights.HasRegions)
        {
            _registerHotKeys();
        }

        _invalidateOverlay();
        _notifyStateChanged();
    }

    private void DeletePendingScreenshotRegion()
    {
        if (!_selection.DeletePendingScreenshotRegion())
        {
            return;
        }

        _selection.CancelDraft();
        _invalidateOverlay();
        _notifyStateChanged();
    }

    private async Task CommitPendingScreenshotRegionAsync(bool save)
    {
        if (_quickEditExporting || _selection.PendingScreenshotRegion is not { } selected) return;
        _quickEditExporting = true;
        var restoreToolbarAfterExport = false;
        try
        {
            if (_quickEditSourceTask is not null) await _quickEditSourceTask;
            QuickEditInkStore.CommitText();
            var source = QuickEditInkStore.SourceFrame == selected ? QuickEditInkStore.Source : null;
            var pixelated = QuickEditInkStore.PixelatedSource;
            var strokes = QuickEditInkStore.Strokes.ToArray();
            if (source is null)
            {
                source = await _captureQuickEditSource(selected);
                if (source is null) return;
                QuickEditInkStore.SetSource(selected, source);
                pixelated = QuickEditInkStore.PixelatedSource;
            }

            if (_selection.PendingScreenshotRegion != selected
                || !_selection.TryTakePendingScreenshotRegion(out var rect, out var restoreToolbar)) return;
            restoreToolbarAfterExport = restoreToolbar;
            _setMode(InteractionMode.Passthrough);
            var image = QuickEditImageComposer.Compose(source, rect, strokes, pixelated);
            if (save) await _saveQuickEditImage(image);
            else await _copyQuickEditImage(image);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not export Quick Edit image.", ex);
        }
        finally
        {
            if (restoreToolbarAfterExport) _showToolbar();
            _quickEditExporting = false;
        }
    }

    private bool TryNudgeScreenshotRegion(Key key, ModifierKeys modifiers)
    {
        if (!TryGetNudgeDelta(key, modifiers, out var dx, out var dy)
            || !_selection.TryNudgePendingScreenshotRegion(dx, dy))
        {
            return false;
        }

        _invalidateOverlay();
        return true;
    }

    private bool TryNudgeSelectedSpotlightRegion(Key key, ModifierKeys modifiers)
    {
        if (!TryGetNudgeDelta(key, modifiers, out var dx, out var dy)
            || !_spotlights.NudgeSelected(dx, dy))
        {
            return false;
        }

        _invalidateOverlay();
        return true;
    }

    private void InvalidateIf(bool shouldInvalidate)
    {
        if (shouldInvalidate)
        {
            _invalidateOverlay();
        }
    }

    private static bool TryGetNudgeDelta(Key key, ModifierKeys modifiers, out double dx, out double dy)
    {
        dx = 0;
        dy = 0;

        if ((modifiers & ~ModifierKeys.Shift) != 0)
        {
            return false;
        }

        var step = (modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        switch (key)
        {
            case Key.Left:
                dx = -step;
                return true;
            case Key.Right:
                dx = step;
                return true;
            case Key.Up:
                dy = -step;
                return true;
            case Key.Down:
                dy = step;
                return true;
            default:
                return false;
        }
    }

    private static bool Matches(Key key, ModifierKeys modifiers, string shortcutText)
    {
        return Shortcut.TryParse(shortcutText, out var shortcut) && shortcut.Matches(key, modifiers);
    }

}
