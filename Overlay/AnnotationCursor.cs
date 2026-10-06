using FocusTool.Win.Models;
using FocusTool.Win.Services;
using System.IO;
using WpfCursor = System.Windows.Input.Cursor;
using WpfCursors = System.Windows.Input.Cursors;
using FormsCursor = System.Windows.Forms.Cursor;
using FormsCursors = System.Windows.Forms.Cursors;

namespace FocusTool.Win.Overlay;

internal static class AnnotationCursor
{
    // FastStone Capture's embedded CRMYERASER cursor, extracted as a standalone .cur.
    private const string FastStoneEraserCursorBase64 = "AAACAAEAIABAAAoAFAAwAQAAFgAAACgAAAAgAAAAQAAAAAEAAQAAAAAAAAEAAAAAAAAAAAAAAgAAAAAAAAAAAAAA////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMAAAAEgAAAC0AAABegAAAvkAAAL0gAABakAAAJUgAABKkAAAJUgAABKoAAAJUAAABKAAAAJAAAABgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA//////////////////////////////////////////////////P////h////wP///4B///8AP///AB///4AP///AB///4AP///AB///4Af///AP///4H////D////5////////////////////////////////////////////8=";
    // FastStone Capture's embedded CRMYPEN cursor, extracted as a standalone .cur.
    private const string FastStonePenCursorBase64 = "AAACAAEAIABAAAwAFwAwAQAAFgAAACgAAAAgAAAAQAAAAAEAAQAAAAAAAAEAAAAAAAAAAAAAAgAAAAAAAAAAAAAA////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABgAAAAUAAAAEgAAABEAAAAQgAAAEkAAABdAAAALIAAAC6AAAAWQAAAF0AAAAogAAAIoAAABdAAAASQAAACIAAAAcAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA////////////////////////////////////////////5////+P////h////4P///+B////gP///4D////Af///wH///+A////gP///8B////Af///4D///+A////wf///+P//////////////////////////////////////8=";
    // FastStone Capture's embedded CRMYCLOSEHAND cursor, extracted as a standalone .cur.
    private const string FastStoneClosedHandCursorBase64 = "AAACAAEAIABAAA8ADwAwAQAAFgAAACgAAAAgAAAAQAAAAAEAAQAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAA////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD8AAAA/gAAAf8AAAP/AAAH/wAAB/+AAAf/gAAE/4AAAP6AAAH2AAABtgAAAJAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA//////////////////////////////////////////////////gH///wA///8AH//+AB///AAf//wAD//8AA///AAP//4AD///AA///wA///8Af///k///////////////////////////////////////////////////////8=";
    // FastStone Capture's embedded CRMYOPENHAND cursor, extracted as a standalone .cur.
    private const string FastStoneOpenHandCursorBase64 = "AAACAAEAIABAAA8ADwAwAQAAFgAAACgAAAAgAAAAQAAAAAEAAQAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAA////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAPwAAAH8AAAD/gAAA/4AAAf/AAAP/wAAHf8AABn/gAAB/YAAA22AAANsgAAGbAAABmwAAABsAAAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA////////////////////////////////////////////+Af///AH///gA///4AP//8AB//+AAf//AAH//wAA//+QAP//4AD//+AA///ABf//wAf//+QH///8D////n////////////////////////////////////////////8=";
    private static readonly Lazy<WpfCursor> FastStoneEraserForOverlay = new(CreateOverlayEraser);
    private static readonly Lazy<FormsCursor> FastStoneEraserForPin = new(CreatePinEraser);
    private static readonly Lazy<WpfCursor> FastStonePenForOverlay = new(CreateOverlayPen);
    private static readonly Lazy<FormsCursor> FastStonePenForPin = new(CreatePinPen);
    private static readonly Lazy<WpfCursor> FastStoneClosedHandForOverlay = new(CreateOverlayClosedHand);
    private static readonly Lazy<FormsCursor> FastStoneClosedHandForPin = new(CreatePinClosedHand);
    private static readonly Lazy<WpfCursor> FastStoneOpenHandForOverlay = new(CreateOverlayOpenHand);

    public static FormsCursor DraggingHandForPin => FastStoneClosedHandForPin.Value;

    public static WpfCursor ForQuickEditSelection(RectOverlayVisual? selection)
        => selection is not { IsDraft: false } ? WpfCursors.Cross
            : QuickEditInkStore.IsStrokeDragging ? FastStoneClosedHandForOverlay.Value
            : QuickEditInkStore.IsArrowControlDragging || QuickEditInkStore.IsResizeDragging || QuickEditInkStore.IsCalloutPointerDragging ? WpfCursors.SizeAll
            : QuickEditInkStore.Tool is QuickEditTool.Pencil or QuickEditTool.Highlighter ? FastStonePenForOverlay.Value
            : QuickEditInkStore.Tool == QuickEditTool.StepBadge ? WpfCursors.Hand
            : QuickEditInkStore.Tool is QuickEditTool.Line or QuickEditTool.Arrow or QuickEditTool.Rectangle or QuickEditTool.Ellipse or QuickEditTool.Mosaic ? WpfCursors.Cross
            : WpfCursors.IBeam;

    public static WpfCursor ForOverlay(InteractionMode mode, bool annotationInputEnabled, AnnotationTool tool, bool isMoveDragging, bool hasMoveSelection)
        => mode == InteractionMode.StaticPinSelect ? WpfCursors.Cross
            : !annotationInputEnabled ? WpfCursors.Arrow
            : tool == AnnotationTool.Eraser ? FastStoneEraserForOverlay.Value
            : tool is AnnotationTool.Pencil or AnnotationTool.Highlighter ? FastStonePenForOverlay.Value
            : tool is AnnotationTool.Line or AnnotationTool.Arrow or AnnotationTool.Rectangle or AnnotationTool.Ellipse ? WpfCursors.Cross
            : tool is AnnotationTool.StepOval or AnnotationTool.StepRect ? WpfCursors.Hand
            : tool == AnnotationTool.Move ? isMoveDragging ? FastStoneClosedHandForOverlay.Value : hasMoveSelection ? FastStoneOpenHandForOverlay.Value : WpfCursors.Cross
            : WpfCursors.Arrow;

    // WinForms exposes no stock Pen cursor; Cross is the standard nearest equivalent.
    public static FormsCursor ForPin(bool annotationInputEnabled, AnnotationTool tool)
        => !annotationInputEnabled ? FormsCursors.Default
            : tool == AnnotationTool.Eraser ? FastStoneEraserForPin.Value
            : tool is AnnotationTool.Pencil or AnnotationTool.Highlighter ? FastStonePenForPin.Value
            : tool is AnnotationTool.Line or AnnotationTool.Arrow or AnnotationTool.Rectangle or AnnotationTool.Ellipse ? FormsCursors.Cross
            : tool is AnnotationTool.StepOval or AnnotationTool.StepRect ? FormsCursors.Hand
            : tool == AnnotationTool.Move ? FastStoneClosedHandForPin.Value
            : FormsCursors.Default;

    private static WpfCursor CreateOverlayEraser()
    {
        using var stream = new MemoryStream(Convert.FromBase64String(FastStoneEraserCursorBase64));
        return new WpfCursor(stream);
    }

    private static FormsCursor CreatePinEraser()
    {
        using var stream = new MemoryStream(Convert.FromBase64String(FastStoneEraserCursorBase64));
        return new FormsCursor(stream);
    }

    private static WpfCursor CreateOverlayPen()
    {
        using var stream = new MemoryStream(Convert.FromBase64String(FastStonePenCursorBase64));
        return new WpfCursor(stream);
    }

    private static FormsCursor CreatePinPen()
    {
        using var stream = new MemoryStream(Convert.FromBase64String(FastStonePenCursorBase64));
        return new FormsCursor(stream);
    }

    private static WpfCursor CreateOverlayClosedHand()
    {
        using var stream = new MemoryStream(Convert.FromBase64String(FastStoneClosedHandCursorBase64));
        return new WpfCursor(stream);
    }

    private static FormsCursor CreatePinClosedHand()
    {
        using var stream = new MemoryStream(Convert.FromBase64String(FastStoneClosedHandCursorBase64));
        return new FormsCursor(stream);
    }

    private static WpfCursor CreateOverlayOpenHand()
    {
        using var stream = new MemoryStream(Convert.FromBase64String(FastStoneOpenHandCursorBase64));
        return new WpfCursor(stream);
    }
}
