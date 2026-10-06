using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusTool.Win.Native;
using FocusTool.Win.Overlay;
using FocusTool.Win.Models;
using FocusTool.Win.Services;

namespace FocusTool.Verification;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--benchmark-highlighter", StringComparer.OrdinalIgnoreCase))
            {
                RunHighlighterBenchmark();
                return 0;
            }
            if (args.Contains("--render-callout-reference", StringComparer.OrdinalIgnoreCase))
            {
                RenderCalloutReference();
                return 0;
            }

            VerifyHalfTransparentPrivacyPixel();
            VerifyOpaquePrivacyPixel();
            VerifyMismatchedDimensionsFail();
            VerifyOverlaySegmentsArePlacedInSourceCoordinates();
            VerifyLegacyDefaultShortcutsMigrate();
            VerifyCustomizedLegacyShortcutsArePreserved();
            VerifyCustomizedLegacyHoldShortcutIsPreserved();
            VerifyEraserShortcutMigrationAvoidsConflict();
            VerifyObjectEraserGestureUndoRedo();
            VerifyPushToAnnotateUsesLatchedShortcutComponents();
            VerifyStrokeSmoothingPreservesEndpointsAndCorners();
            VerifyHighlighterUsesFixedRectangularNib();
            VerifyHighlighterDrawAndHoldLocksAndTracksEndpoint();
            VerifyStaticPinAspectRatioSizing();
            VerifyStaticPinStrokeSegmentsSplitAtBounds();
            VerifyStaticPinStrokeSegmentsEnterAndExitBounds();
            VerifyStaticPinRoutingAcrossMultiplePins();
            VerifyStaticPinLiveDraftPromotesArrowAtGestureEnd();
            VerifyStaticPinInkEraserPreservesSnapshotLayer();
            VerifyStaticPinBorderStyles();
            VerifyStaticPinShortcutDefaultAndClone();
            VerifySnapshotQuickEditFrameGeometry();
            VerifyQuickEditDraftSelectionMaskEligibility();
            VerifyQuickEditSelectionCursor();
            VerifyQuickEditPaletteAndGestureTools();
            VerifyCurvedArrowGeometry();
            VerifyEditableCurvedAnnotationArrow();
            VerifyEditableQuickEditArrow();
            VerifyQuickEditExportIncludesArrow();
            VerifyStaticPinArrowControls();
            VerifyFineQuickEditMosaic();
            VerifyQuickEditTextCallout();
            VerifyQuickEditTwoTailCallout();
            VerifyQuickEditTwoTailExport();
            VerifyQuickEditCalloutTailGeometry();
            VerifyQuickEditCalloutReferenceGeometry();
            VerifyQuickEditCalloutSeamlessExport();
            VerifyQuickEditCalloutProjection();
            VerifyQuickEditCalloutBaseEndpoints();
            VerifyQuickEditDoubleClickReopensText();
            VerifyQuickEditTextEditorSynchronization();
            VerifyQuickEditObjectEditingAndHistory();
            VerifyQuickEditEllipseHandlesOnContour();
            VerifyQuickEditStepBadges();
            VerifyQuickEditStepBadgeExport();
            Console.WriteLine("FocusTool verification checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void VerifyQuickEditObjectEditingAndHistory()
    {
        foreach (var tool in new[] { QuickEditTool.Line, QuickEditTool.Arrow, QuickEditTool.Rectangle,
            QuickEditTool.Ellipse, QuickEditTool.Highlighter, QuickEditTool.Pencil })
        {
            QuickEditInkStore.Reset();
            QuickEditInkStore.Tool = tool;
            QuickEditInkStore.Begin(new ScreenPoint(10, 10));
            QuickEditInkStore.Add(new ScreenPoint(60, 50));
            QuickEditInkStore.Commit();
            var grab = tool switch
            {
                QuickEditTool.Rectangle => new ScreenPoint(35, 10),
                QuickEditTool.Ellipse => new ScreenPoint(35, 10),
                _ => new ScreenPoint(35, 30),
            };
            if (!QuickEditInkStore.TryBeginStrokeDrag(grab))
                throw new InvalidOperationException($"Quick Edit {tool} cannot be selected and moved.");
            QuickEditInkStore.UpdateStrokeDrag(grab.Offset(10, 10));
            QuickEditInkStore.EndStrokeDrag();
            if (QuickEditInkStore.Strokes.Single().Points[0] != new ScreenPoint(20, 20))
                throw new InvalidOperationException($"Quick Edit {tool} did not move.");
        }
        QuickEditInkStore.Reset();
        QuickEditInkStore.Tool = QuickEditTool.Rectangle;
        QuickEditInkStore.Begin(new ScreenPoint(10, 10));
        QuickEditInkStore.Add(new ScreenPoint(60, 50));
        QuickEditInkStore.Commit();
        if (!QuickEditInkStore.TryBeginStrokeDrag(new ScreenPoint(35, 10)))
            throw new InvalidOperationException("Quick Edit rectangle cannot be reselected.");
        QuickEditInkStore.UpdateStrokeDrag(new ScreenPoint(45, 20));
        QuickEditInkStore.EndStrokeDrag();
        if (QuickEditInkStore.Strokes.Single().Points[0] != new ScreenPoint(20, 20))
            throw new InvalidOperationException("Quick Edit rectangle did not move.");
        if (!QuickEditInkStore.Undo() || QuickEditInkStore.Strokes.Single().Points[0] != new ScreenPoint(10, 10))
            throw new InvalidOperationException("Ctrl+Z history did not restore moved rectangle.");
        if (!QuickEditInkStore.Redo() || QuickEditInkStore.Strokes.Single().Points[0] != new ScreenPoint(20, 20))
            throw new InvalidOperationException("Redo did not restore moved rectangle.");
        if (!QuickEditInkStore.TryBeginStrokeDrag(new ScreenPoint(45, 20)))
            throw new InvalidOperationException("Quick Edit rectangle cannot be selected after undo/redo.");
        QuickEditInkStore.EndStrokeDrag();
        if (!QuickEditInkStore.DeleteSelected() || QuickEditInkStore.Strokes.Count != 0)
            throw new InvalidOperationException("Delete must remove the selected object.");
        if (!QuickEditInkStore.Undo() || QuickEditInkStore.Strokes.Count != 1)
            throw new InvalidOperationException("Deleting an object must be undoable.");

        QuickEditInkStore.Reset();
        QuickEditInkStore.AddText(new ScreenPoint(20, 20), "callout");
        var callout = QuickEditInkStore.Strokes.Single();
        if (!QuickEditInkStore.TryBeginCalloutPointerDrag(callout.CalloutHandle))
            throw new InvalidOperationException("Text pointer handle cannot start a drag.");
        QuickEditInkStore.UpdateCalloutPointerDrag(new ScreenPoint(150, 150));
        QuickEditInkStore.EndCalloutPointerDrag();
        if (callout.CalloutTarget != new ScreenPoint(150, 150))
            throw new InvalidOperationException("Text pointer handle did not extend the pointer.");
    }

    private static void VerifyQuickEditEllipseHandlesOnContour()
    {
        QuickEditInkStore.Reset();
        QuickEditInkStore.Tool = QuickEditTool.Ellipse;
        QuickEditInkStore.Begin(new ScreenPoint(10, 10));
        QuickEditInkStore.Add(new ScreenPoint(60, 50));
        QuickEditInkStore.Commit();
        var handles = QuickEditInkStore.Strokes.Single().GetResizeHandles();
        if (!handles.SequenceEqual(new[] { new ScreenPoint(35, 10), new ScreenPoint(60, 30),
            new ScreenPoint(35, 50), new ScreenPoint(10, 30) }))
            throw new InvalidOperationException("All ellipse handles must lie on its contour.");
        if (!QuickEditInkStore.TryBeginResizeDrag(new ScreenPoint(35, 10)))
            throw new InvalidOperationException("Ellipse top contour handle cannot be dragged.");
        QuickEditInkStore.UpdateResizeDrag(new ScreenPoint(35, 0));
        QuickEditInkStore.EndResizeDrag();
        if (QuickEditInkStore.Strokes.Single().Points[0] != new ScreenPoint(10, 0))
            throw new InvalidOperationException("Top handle must resize the ellipse vertically.");
        if (!QuickEditInkStore.TryBeginResizeDrag(new ScreenPoint(60, 25)))
            throw new InvalidOperationException("Ellipse right contour handle cannot be dragged.");
        QuickEditInkStore.UpdateResizeDrag(new ScreenPoint(80, 25));
        QuickEditInkStore.EndResizeDrag();
        if (QuickEditInkStore.Strokes.Single().Points[^1] != new ScreenPoint(80, 50))
            throw new InvalidOperationException("Right handle must resize the ellipse horizontally.");
    }

    private static void VerifyQuickEditStepBadges()
    {
        var paletteFrame = new ScreenRect(10, 10, 310, 210);
        var badgeButton = QuickEditPalette.ItemBounds(paletteFrame, 8);
        var badgeButtonCenter = new ScreenPoint((badgeButton.Left + badgeButton.Right) / 2, (badgeButton.Top + badgeButton.Bottom) / 2);
        if (!QuickEditPalette.TryHit(paletteFrame, badgeButtonCenter, out var badgeAction)
            || badgeAction != QuickEditPaletteAction.StepBadge
            || QuickEditPalette.ToTool(badgeAction) != QuickEditTool.StepBadge)
            throw new InvalidOperationException("Quick Edit palette must expose the step badge tool.");
        QuickEditInkStore.Reset();
        QuickEditInkStore.Tool = QuickEditTool.StepBadge;
        QuickEditInkStore.PlaceStepBadge(new ScreenPoint(30, 30));
        QuickEditInkStore.PlaceStepBadge(new ScreenPoint(80, 30));
        QuickEditInkStore.PlaceStepBadge(new ScreenPoint(130, 30));
        var badges = QuickEditInkStore.Strokes;
        if (badges.Count != 3 || badges.Select(b => QuickEditInkStore.StepNumber(badges, b)).SequenceEqual(new[] { 1, 2, 3 }) == false)
            throw new InvalidOperationException("Quick Edit badges must be numbered by placement order.");
        if (!QuickEditInkStore.TryBeginStrokeDrag(new ScreenPoint(80, 30)))
            throw new InvalidOperationException("A step badge must be selectable.");
        QuickEditInkStore.UpdateStrokeDrag(new ScreenPoint(95, 40));
        QuickEditInkStore.EndStrokeDrag();
        if (badges[1].Points[0] != new ScreenPoint(95, 40) || QuickEditInkStore.StepNumber(badges, badges[1]) != 2)
            throw new InvalidOperationException("Moving a badge must preserve its number.");
        if (!QuickEditInkStore.DeleteSelected() || badges.Count != 2 || QuickEditInkStore.StepNumber(badges, badges[1]) != 2)
            throw new InvalidOperationException("Deleting a badge must close the numbering gap.");
        if (!QuickEditInkStore.Undo() || badges.Count != 3 || QuickEditInkStore.StepNumber(badges, badges[1]) != 2)
            throw new InvalidOperationException("Undo must restore the deleted numbered badge.");
    }

    private static void VerifyQuickEditStepBadgeExport()
    {
        var source = CreateBitmap(64, 64, Enumerable.Repeat((byte)255, 64 * 64 * 4).ToArray());
        var badge = new QuickEditStroke(QuickEditTool.StepBadge, [new ScreenPoint(32, 32)]);
        var output = QuickEditImageComposer.Compose(source, new ScreenRect(0, 0, 64, 64), [badge], null);
        var pixels = new byte[64 * 64 * 4];
        output.CopyPixels(pixels, 64 * 4, 0);
        var redPixels = 0;
        var whiteNumberPixels = 0;
        for (var y = 18; y <= 46; y++)
        for (var x = 18; x <= 46; x++)
        {
            var offset = (y * 64 + x) * 4;
            if (pixels[offset + 2] > 180 && pixels[offset + 1] < 80 && pixels[offset] < 80) redPixels++;
            if (x is >= 27 and <= 37 && y is >= 24 and <= 40
                && pixels[offset] > 200 && pixels[offset + 1] > 200 && pixels[offset + 2] > 200) whiteNumberPixels++;
        }
        if (redPixels < 200 || whiteNumberPixels == 0)
            throw new InvalidOperationException("Exported step badge must contain a red circle and white number.");
    }

    private static void RunHighlighterBenchmark()
    {
        const int sampleCount = 800;
        var raw = new List<ScreenPoint>(sampleCount);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var generatedSweepPoints = 0L;
        for (var index = 0; index < sampleCount; index++)
        {
            raw.Add(new ScreenPoint(
                index * 4.5,
                300 + Math.Sin(index * 0.08) * 80 + (index % 2 == 0 ? 0.8 : -0.8)));
            if (raw.Count < 3)
            {
                continue;
            }

            var smoothed = AnnotationStrokeGeometry.Smooth(raw, StrokeSmoothingLevel.Strong, finalize: false);
            var geometry = AnnotationStrokeGeometry.BuildFixedNibGeometry(smoothed, 4, 24);
            generatedSweepPoints += geometry.Points.Count;
        }

        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            $"Highlighter live replay: {sampleCount} samples, {stopwatch.ElapsedMilliseconds} ms, "
            + $"{allocated / (1024.0 * 1024.0):0.0} MiB allocated, {generatedSweepPoints} generated vertices.");

        const int repeatCount = 100;
        var phaseWatch = System.Diagnostics.Stopwatch.StartNew();
        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        IReadOnlyList<ScreenPoint> finalSmoothed = raw;
        for (var index = 0; index < repeatCount; index++)
        {
            finalSmoothed = AnnotationStrokeGeometry.Smooth(raw, StrokeSmoothingLevel.Strong, finalize: false);
        }

        phaseWatch.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            $"  smoothing x{repeatCount}: {phaseWatch.ElapsedMilliseconds} ms, {allocated / (1024.0 * 1024.0):0.0} MiB");

        phaseWatch.Restart();
        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < repeatCount; index++)
        {
            _ = AnnotationStrokeGeometry.BuildFixedNibGeometry(finalSmoothed, 4, 24);
        }

        phaseWatch.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            $"  nib geometry x{repeatCount}: {phaseWatch.ElapsedMilliseconds} ms, {allocated / (1024.0 * 1024.0):0.0} MiB");
    }

    private static void VerifyHalfTransparentPrivacyPixel()
    {
        var background = CreateBitmap(2, 1,
        [
            100, 100, 100, 255,
            90, 80, 70, 255
        ]);
        var privacyLayer = CreateBitmap(2, 1,
        [
            0, 0, 128, 128,
            0, 0, 0, 0
        ]);

        var result = ScreenBoardCompositor.CompositePrivacyLayer(background, privacyLayer);
        AssertPixels(result,
        [
            50, 50, 178, 255,
            90, 80, 70, 255
        ], "Half-transparent privacy layer");
    }

    private static void VerifyOpaquePrivacyPixel()
    {
        var background = CreateBitmap(1, 1, [100, 110, 120, 255]);
        var privacyLayer = CreateBitmap(1, 1, [20, 30, 40, 255]);
        var result = ScreenBoardCompositor.CompositePrivacyLayer(background, privacyLayer);
        AssertPixels(result, [20, 30, 40, 255], "Opaque privacy layer");
    }

    private static void VerifyMismatchedDimensionsFail()
    {
        var background = CreateBitmap(1, 1, [0, 0, 0, 255]);
        var privacyLayer = CreateBitmap(2, 1, [0, 0, 0, 0, 0, 0, 0, 0]);
        try
        {
            _ = ScreenBoardCompositor.CompositePrivacyLayer(background, privacyLayer);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException("Mismatched compositor dimensions were accepted.");
    }

    private static void VerifyOverlaySegmentsArePlacedInSourceCoordinates()
    {
        var destination = new byte[4 * 2 * 4];
        var first = CreateBitmap(2, 1, [1, 2, 3, 4, 5, 6, 7, 8]);
        var second = CreateBitmap(2, 1, [9, 10, 11, 12, 13, 14, 15, 16]);

        OverlayLayerComposer.CopyInto(first, destination, 4, 2, 0, 0);
        OverlayLayerComposer.CopyInto(second, destination, 4, 2, 2, 1);

        var expected = new byte[]
        {
            1, 2, 3, 4, 5, 6, 7, 8, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 9, 10, 11, 12, 13, 14, 15, 16,
        };
        if (!destination.SequenceEqual(expected))
        {
            throw new InvalidOperationException("Multi-monitor overlay segments were placed incorrectly.");
        }
    }

    private static void VerifyLegacyDefaultShortcutsMigrate()
    {
        var shortcuts = CreateLegacyShortcuts();
        shortcuts.Normalize();

        if (shortcuts.LayoutVersion != ShortcutSettings.CurrentLayoutVersion
            || shortcuts.ToggleAnnotate != "Ctrl+Alt+A"
            || shortcuts.ToggleClickPulse != "Ctrl+Alt+C"
            || shortcuts.HoldSpotlight != "Alt+S"
            || shortcuts.ToolPencil != "W"
            || shortcuts.Redo != "Ctrl+Shift+Z")
        {
            throw new InvalidOperationException("Legacy default shortcuts were not migrated to the left-hand layout.");
        }
    }

    private static void VerifyCustomizedLegacyShortcutsArePreserved()
    {
        var shortcuts = CreateLegacyShortcuts();
        shortcuts.ToggleAnnotate = "Ctrl+Shift+A";
        shortcuts.Normalize();

        if (shortcuts.ToggleAnnotate != "Ctrl+Shift+A"
            || shortcuts.ToggleLaserActivation != "Ctrl+Alt+L"
            || shortcuts.ToggleClickPulse != ShortcutSettings.DisabledShortcut
            || shortcuts.HoldSpotlight != ShortcutSettings.DisabledShortcut)
        {
            throw new InvalidOperationException("Customized legacy shortcuts were overwritten during migration.");
        }
    }

    private static void VerifyCustomizedLegacyHoldShortcutIsPreserved()
    {
        var settings = new AppSettings
        {
            LaserHoldShortcut = "Mouse4",
            Shortcuts = CreateLegacyShortcuts(),
        };
        settings.Normalize();

        if (settings.LaserHoldShortcut != "Mouse4"
            || settings.Shortcuts.ToggleLaserActivation != "Ctrl+Alt+L"
            || settings.Shortcuts.HoldSpotlight != ShortcutSettings.DisabledShortcut)
        {
            throw new InvalidOperationException("A customized legacy hold shortcut did not prevent automatic layout migration.");
        }
    }

    private static void VerifyEraserShortcutMigrationAvoidsConflict()
    {
        var available = new ShortcutSettings { LayoutVersion = 1 };
        available.Normalize();
        if (available.ToolEraser != "E")
        {
            throw new InvalidOperationException("Eraser did not receive the available E shortcut.");
        }

        var occupied = new ShortcutSettings
        {
            LayoutVersion = 1,
            ClearAlternate = "E"
        };
        occupied.Normalize();
        if (occupied.ToolEraser != ShortcutSettings.DisabledShortcut)
        {
            throw new InvalidOperationException("Eraser migration introduced a shortcut conflict.");
        }
    }

    private static void VerifyObjectEraserGestureUndoRedo()
    {
        var document = new AnnotationDocument(() => 1000);
        var settings = new AppSettings();
        AddLine(document, settings, 20, "#FFFF0000");
        AddLine(document, settings, 60, "#FF00FF00");
        AddLine(document, settings, 100, "#FF0000FF");

        document.BeginEraseGesture(new ScreenPoint(20, 50));
        document.ContinueEraseGesture(new ScreenPoint(100, 50));
        document.EndEraseGesture(new ScreenPoint(100, 50));
        if (document.Shapes.Count != 0)
        {
            throw new InvalidOperationException("Eraser drag did not remove each crossed object.");
        }

        document.Undo();
        if (document.Shapes.Count != 3
            || document.Shapes[0].Color != "#FFFF0000"
            || document.Shapes[2].Color != "#FF0000FF")
        {
            throw new InvalidOperationException("One eraser gesture was not restored as one ordered undo operation.");
        }

        document.Redo();
        if (document.Shapes.Count != 0)
        {
            throw new InvalidOperationException("Redo did not repeat the eraser gesture.");
        }

        var overlap = new AnnotationDocument(() => 1000);
        AddLine(overlap, settings, 40, "#FFFF0000");
        AddLine(overlap, settings, 40, "#FF0000FF");
        overlap.BeginEraseGesture(new ScreenPoint(40, 50));
        overlap.EndEraseGesture(new ScreenPoint(40, 50));
        if (overlap.Shapes.Count != 1 || overlap.Shapes[0].Color != "#FFFF0000")
        {
            throw new InvalidOperationException("Eraser click did not remove only the topmost object.");
        }

        var imageOverlap = new AnnotationDocument(() => 1000);
        AddLine(imageOverlap, settings, 40, "#FFFF0000");
        var image = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            new byte[] { 255, 255, 255, 255 },
            4);
        imageOverlap.AddPastedImage(image, new ScreenRect(0, 0, 80, 100));
        imageOverlap.BeginEraseGesture(new ScreenPoint(40, 50));
        imageOverlap.EndEraseGesture(new ScreenPoint(40, 50));
        if (imageOverlap.Shapes.Count != 1 || imageOverlap.Shapes[0].Tool != AnnotationTool.Image)
        {
            throw new InvalidOperationException("Image blocked erasing an annotation beneath it or was erased itself.");
        }

        imageOverlap.BeginEraseGesture(new ScreenPoint(40, 50));
        imageOverlap.EndEraseGesture(new ScreenPoint(40, 50));
        if (imageOverlap.Shapes.Count != 1
            || imageOverlap.Shapes[0].Tool != AnnotationTool.Image
            || imageOverlap.EraserHoverShape is not null)
        {
            throw new InvalidOperationException("Eraser treated a pasted image as an erasable target.");
        }
    }

    private static void VerifyPushToAnnotateUsesLatchedShortcutComponents()
    {
        VerifyPushToAnnotateShortcutScenario("Alt+A", "A", AnnotationTool.Arrow);
        VerifyPushToAnnotateShortcutScenario("Ctrl+Shift+W", "W", AnnotationTool.Pencil);
        VerifyPushToAnnotateShortcutScenario("F", "F", AnnotationTool.Highlighter);

        Shortcut.TryParse("Ctrl+Shift+W", out var shortcut);
        var chordPressed = false;
        var anyComponentPressed = true;
        var holdSession = new HoldShortcutSession(_ => chordPressed, _ => anyComponentPressed);
        holdSession.Begin(shortcut, HoldShortcutReleasePolicy.ChordBreak);
        if (holdSession.ShouldRemainActive())
        {
            throw new InvalidOperationException("ChordBreak hold policy ignored a broken shortcut chord.");
        }

        holdSession.Begin(shortcut, HoldShortcutReleasePolicy.AllComponentsReleased);
        if (!holdSession.ShouldRemainActive())
        {
            throw new InvalidOperationException("AllComponentsReleased hold policy exited while a shortcut component was still held.");
        }
    }

    private static void VerifyPushToAnnotateShortcutScenario(
        string pushShortcut,
        string conflictingToolShortcut,
        AnnotationTool expectedTool)
    {
        var settings = new AppSettings();
        settings.Shortcuts.PushToAnnotate = pushShortcut;
        settings.Shortcuts.ToolArrow = expectedTool == AnnotationTool.Arrow ? conflictingToolShortcut : "A";
        settings.Shortcuts.ToolPencil = expectedTool == AnnotationTool.Pencil ? conflictingToolShortcut : "W";
        settings.Shortcuts.ToolHighlighter = expectedTool == AnnotationTool.Highlighter ? conflictingToolShortcut : "F";

        var mode = InteractionMode.Passthrough;
        var pressedShortcuts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { conflictingToolShortcut };
        var anyTriggerComponentPressed = true;
        var hasTextInput = false;
        var selectedTools = new List<AnnotationTool>();
        var controller = new PushToAnnotateController(
            () => settings,
            () => mode,
            next => mode = next,
            _ => { },
            () => hasTextInput,
            TimeSpan.FromMilliseconds(8),
            () => { },
            selectedTools.Add,
            () => { },
            _ => { },
            shortcut => pressedShortcuts.Contains(shortcut.DisplayText),
            _ => anyTriggerComponentPressed);

        controller.ConfigureShortcut();
        controller.Start(disposed: false);
        controller.Update(canExit: true);
        if (mode != InteractionMode.Annotate || selectedTools.Count != 0)
        {
            throw new InvalidOperationException($"Push-to-annotate {pushShortcut} activated its conflicting tool on entry.");
        }

        hasTextInput = true;
        controller.Update(canExit: false);
        hasTextInput = false;
        controller.Update(canExit: true);
        if (selectedTools.Count != 0)
        {
            throw new InvalidOperationException($"Push-to-annotate {pushShortcut} lost its shortcut latch during text input.");
        }

        pressedShortcuts.Remove(conflictingToolShortcut);
        controller.Update(canExit: true);
        if (!controller.Active)
        {
            throw new InvalidOperationException($"Push-to-annotate {pushShortcut} exited before all shortcut components were released.");
        }

        pressedShortcuts.Add(conflictingToolShortcut);
        controller.Update(canExit: true);
        if (selectedTools.Count != 1 || selectedTools[0] != expectedTool)
        {
            throw new InvalidOperationException($"Push-to-annotate {pushShortcut} did not accept a fresh conflicting tool press.");
        }

        pressedShortcuts.Clear();
        anyTriggerComponentPressed = false;
        controller.Update(canExit: true);
        if (controller.Active || mode != InteractionMode.Passthrough)
        {
            throw new InvalidOperationException($"Push-to-annotate {pushShortcut} did not exit after all shortcut components were released.");
        }
    }

    private static void VerifyStrokeSmoothingPreservesEndpointsAndCorners()
    {
        var raw = new[]
        {
            new ScreenPoint(0, 0),
            new ScreenPoint(20, 1),
            new ScreenPoint(40, 0),
            new ScreenPoint(40, 30),
            new ScreenPoint(40, 60)
        };
        var smoothed = AnnotationStrokeGeometry.Smooth(raw, StrokeSmoothingLevel.Strong, finalize: true);
        if (smoothed[0] != raw[0]
            || smoothed[^1] != raw[^1]
            || smoothed.Min(point => point.DistanceTo(raw[2])) > 2)
        {
            throw new InvalidOperationException("Stroke smoothing changed an endpoint or rounded away a sharp corner.");
        }

        var noisy = Enumerable.Range(0, 31)
            .Select(index => new ScreenPoint(index * 4, index is 0 or 30 ? 0 : index % 2 == 0 ? 2 : -2))
            .ToArray();
        var stabilized = AnnotationStrokeGeometry.Smooth(noisy, StrokeSmoothingLevel.Strong, finalize: true);
        var rawNoise = noisy.Skip(2).SkipLast(2).Average(point => Math.Abs(point.Y));
        var stabilizedNoise = stabilized.Skip(4).SkipLast(4).Average(point => Math.Abs(point.Y));
        if (stabilizedNoise >= rawNoise * 0.4)
        {
            throw new InvalidOperationException("Strong final smoothing did not suppress high-frequency pointer noise.");
        }
    }

    private static void VerifyHighlighterUsesFixedRectangularNib()
    {
        var horizontal = AnnotationStrokeGeometry.BuildFixedNibGeometry(
            [new ScreenPoint(10, 20), new ScreenPoint(110, 20)],
            4,
            24);
        var vertical = AnnotationStrokeGeometry.BuildFixedNibGeometry(
            [new ScreenPoint(10, 20), new ScreenPoint(10, 120)],
            4,
            24);
        var horizontalThickness = horizontal.Points.Max(point => point.Y) - horizontal.Points.Min(point => point.Y);
        var verticalThickness = vertical.Points.Max(point => point.X) - vertical.Points.Min(point => point.X);
        if (Math.Abs(horizontalThickness - 24) > 0.001
            || Math.Abs(verticalThickness - 4) > 0.001)
        {
            throw new InvalidOperationException("Highlighter tip rotated with the stroke instead of staying fixed.");
        }

        var corner = AnnotationStrokeGeometry.BuildFixedNibGeometry(
            [new ScreenPoint(10, 20), new ScreenPoint(110, 20), new ScreenPoint(110, 120)],
            4,
            24);
        if (corner.FigureEnds.Count != 2
            || corner.FigureEnds[0] < 4
            || corner.FigureEnds[1] - corner.FigureEnds[0] < 4)
        {
            throw new InvalidOperationException("Fixed highlighter sweeps left a disconnected corner.");
        }
    }

    private static void VerifyHighlighterDrawAndHoldLocksAndTracksEndpoint()
    {
        var nowMs = 0.0;
        var document = new AnnotationDocument(() => nowMs);
        var settings = new AppSettings();
        document.BeginStroke(AnnotationTool.Highlighter, new ScreenPoint(10, 10), settings);
        document.UpdateStroke(new ScreenPoint(90, 30), shift: false);
        nowMs = 479;
        if (document.TryLockHighlighterHold(nowMs))
        {
            throw new InvalidOperationException("Highlighter hold locked before the threshold.");
        }

        nowMs = 480;
        if (!document.TryLockHighlighterHold(nowMs) || document.Draft?.HighlighterStraightened != true)
        {
            throw new InvalidOperationException("Highlighter hold did not lock at the threshold.");
        }

        document.UpdateStroke(new ScreenPoint(120, 45), shift: false);
        if (document.Draft?.End != new ScreenPoint(120, 45))
        {
            throw new InvalidOperationException("Locked highlighter endpoint could not be adjusted before release.");
        }
    }

    private static void VerifyStaticPinAspectRatioSizing()
    {
        var initial = new ScreenRect(100, 100, 500, 300);
        var resized = StaticPinGeometry.ResizeFromBottomRight(initial, new ScreenPoint(700, 400), 48);
        if (Math.Abs(resized.Width / resized.Height - 2) > 0.001
            || Math.Abs(resized.Right - 700) > 0.001
            || resized.Left != initial.Left
            || resized.Top != initial.Top)
        {
            throw new InvalidOperationException("Static pin resize did not preserve its source aspect ratio and fixed origin.");
        }

        var minimum = StaticPinGeometry.ResizeFromBottomRight(initial, new ScreenPoint(110, 110), 48);
        if (minimum.Width < 48 || minimum.Height < 24 || Math.Abs(minimum.Width / minimum.Height - 2) > 0.001)
        {
            throw new InvalidOperationException("Static pin resize crossed its minimum size or broke its aspect ratio.");
        }

        var left = StaticPinGeometry.Resize(new ScreenRect(100, 100, 500, 300), 50, 0, StaticPinResizeHandle.Left, 80);
        if (Math.Abs(left.Width / left.Height - 2) > 0.001 || Math.Abs(left.Right - 500) > 0.001 || left.Left <= 100)
        {
            throw new InvalidOperationException("Static pin left-edge resize did not preserve its ratio and opposite edge.");
        }

        var top = StaticPinGeometry.Resize(new ScreenRect(100, 100, 500, 300), 0, 50, StaticPinResizeHandle.Top, 80);
        if (Math.Abs(top.Width / top.Height - 2) > 0.001 || Math.Abs(top.Bottom - 300) > 0.001 || top.Top <= 100)
        {
            throw new InvalidOperationException("Static pin top-edge resize did not preserve its ratio and opposite edge.");
        }

        var minimumCard = StaticPinGeometry.ScaleToMinimum(16, 100, 48);
        if (minimumCard.Width != 48 || minimumCard.Height != 300 || Math.Abs(minimumCard.Width / (double)minimumCard.Height - 0.16) > 0.001)
        {
            throw new InvalidOperationException("Small static pin display scaling did not preserve the captured image aspect ratio.");
        }
    }

    private static void VerifyStaticPinShortcutDefaultAndClone()
    {
        var shortcuts = new ShortcutSettings();
        if (shortcuts.NewStaticPin != "Alt+Shift+W")
        {
            throw new InvalidOperationException("Static Pin shortcut default was not initialized.");
        }

        shortcuts.NewStaticPin = "Ctrl+Shift+P";
        var copy = shortcuts.Clone();
        copy.Normalize();
        if (copy.NewStaticPin != "Ctrl+Shift+P")
        {
            throw new InvalidOperationException("A customized Static Pin shortcut was not preserved when cloning settings.");
        }
    }

    private static void VerifyStaticPinStrokeSegmentsSplitAtBounds()
    {
        var fragments = StaticPinGeometry.SplitSegment(
            new ScreenPoint(0, 50),
            new ScreenPoint(200, 50),
            new ScreenRect(50, 0, 150, 100));

        AssertStrokeFragments(
            fragments,
            (new ScreenPoint(0, 50), new ScreenPoint(50, 50), false),
            (new ScreenPoint(50, 50), new ScreenPoint(150, 50), true),
            (new ScreenPoint(150, 50), new ScreenPoint(200, 50), false));
    }

    private static void VerifyStaticPinStrokeSegmentsEnterAndExitBounds()
    {
        var bounds = new ScreenRect(50, 0, 150, 100);
        AssertStrokeFragments(
            StaticPinAnnotationRouter.Route(AnnotationTool.Pencil, new ScreenPoint(0, 50), new ScreenPoint(100, 50), bounds),
            (new ScreenPoint(0, 50), new ScreenPoint(50, 50), false),
            (new ScreenPoint(50, 50), new ScreenPoint(100, 50), true));
        AssertStrokeFragments(
            StaticPinAnnotationRouter.Route(AnnotationTool.Arrow, new ScreenPoint(100, 50), new ScreenPoint(200, 50), bounds),
            (new ScreenPoint(100, 50), new ScreenPoint(150, 50), true),
            (new ScreenPoint(150, 50), new ScreenPoint(200, 50), false));
        AssertStrokeFragments(
            StaticPinAnnotationRouter.Route(AnnotationTool.Line, new ScreenPoint(0, 50), new ScreenPoint(200, 50), bounds),
            (new ScreenPoint(0, 50), new ScreenPoint(50, 50), false),
            (new ScreenPoint(50, 50), new ScreenPoint(150, 50), true),
            (new ScreenPoint(150, 50), new ScreenPoint(200, 50), false));

        try
        {
            _ = StaticPinAnnotationRouter.Route(AnnotationTool.Rectangle, new ScreenPoint(0, 50), new ScreenPoint(200, 50), bounds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        throw new InvalidOperationException("Static pin router accepted a tool that is not represented by a segment.");
    }

    private static void VerifyStaticPinRoutingAcrossMultiplePins()
    {
        var routed = StaticPinAnnotationRouter.RouteAcrossPins(
            AnnotationTool.Arrow,
            new ScreenPoint(0, 50),
            new ScreenPoint(200, 50),
            [new ScreenRect(125, 0, 175, 100), new ScreenRect(50, 0, 100, 100)]);

        if (routed.Count != 5
            || routed[0].PinIndex is not null
            || routed[1].PinIndex != 1
            || routed[2].PinIndex is not null
            || routed[3].PinIndex != 0
            || routed[4].PinIndex is not null
            || routed[1].Fragment.Start != new ScreenPoint(50, 50)
            || routed[1].Fragment.End != new ScreenPoint(100, 50)
            || routed[3].Fragment.Start != new ScreenPoint(125, 50)
            || routed[3].Fragment.End != new ScreenPoint(175, 50))
        {
            throw new InvalidOperationException("Static pin routing did not split an outside-to-outside segment across every pin in order.");
        }
    }

    private static void VerifyStaticPinLiveDraftPromotesArrowAtGestureEnd()
    {
        var document = new AnnotationDocument(() => 1000);
        var settings = new AppSettings();
        var start = new ScreenPoint(10, 10);
        var end = new ScreenPoint(120, 40);

        // A Line draft is the live preview while the pointer moves. It must be
        // visible before mouse-up, then become the single final Arrow only when
        // the gesture ends outside a pin.
        document.BeginStroke(AnnotationTool.Line, start, settings);
        document.UpdateStroke(end, shift: false);
        if (document.Draft is not { Tool: AnnotationTool.Line, End: var draftEnd } || draftEnd != end)
        {
            throw new InvalidOperationException("Static pin external stroke was not visible as a live draft.");
        }

        document.CommitDraftAs(AnnotationTool.Arrow);
        if (document.Shapes.Count != 1 || document.Shapes[0].Tool != AnnotationTool.Arrow)
        {
            throw new InvalidOperationException("Static pin final external stroke was not promoted to one arrow.");
        }
    }

    private static void VerifyStaticPinInkEraserPreservesSnapshotLayer()
    {
        using var ink = new System.Drawing.Bitmap(48, 24, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(ink))
        {
            graphics.Clear(System.Drawing.Color.Transparent);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.Red, 4);
            graphics.DrawLine(pen, 4, 7, 42, 7);
            graphics.DrawLine(pen, 4, 17, 42, 17);
        }

        if (!StaticPinInkEraser.EraseConnectedComponent(ink, new System.Drawing.PointF(20, 7), 4)
            || ink.GetPixel(20, 7).A != 0
            || ink.GetPixel(20, 17).A == 0)
        {
            throw new InvalidOperationException("Static pin eraser did not remove exactly the touched ink stroke.");
        }

        StaticPinInkEraser.Clear(ink);
        if (ink.GetPixel(20, 17).A != 0)
        {
            throw new InvalidOperationException("Static pin global clear did not remove its remaining ink.");
        }
    }

    private static void VerifySnapshotQuickEditFrameGeometry()
    {
        var initialFrame = new ScreenRect(100, 100, 500, 400);
        var movedFrame = SnapshotQuickEditGeometry.MoveFrame(
            initialFrame,
            new ScreenPoint(150, 150),
            new ScreenPoint(210, 185));
        if (movedFrame != new ScreenRect(160, 135, 560, 435))
        {
            throw new InvalidOperationException("Quick-edit snapshot frame did not move with the pointer.");
        }

        var panel = SnapshotQuickEditGeometry.GetDefaultPanelBounds(
            initialFrame,
            panelWidth: 360,
            panelHeight: 34,
            workArea: new ScreenRect(0, 0, 800, 600));
        if (panel != new ScreenRect(100, 408, 460, 442))
        {
            throw new InvalidOperationException("Quick-edit toolbar was not positioned directly below its snapshot frame.");
        }

        var resizedFrame = SnapshotQuickEditGeometry.ResizeFrame(
            initialFrame,
            RectResizeHandle.BottomRight,
            new ScreenPoint(640, 520));
        if (resizedFrame != new ScreenRect(100, 100, 640, 520))
        {
            throw new InvalidOperationException("Quick-edit snapshot frame did not resize from its selected handle.");
        }

        var session = new SnapshotQuickEditSession(initialFrame);
        if (!session.TryBeginFrameEdit(new ScreenPoint(102, 102)))
        {
            throw new InvalidOperationException("Quick-edit session did not start a resize from the frame corner.");
        }

        session.UpdateFrameEdit(new ScreenPoint(80, 70));
        session.EndFrameEdit();
        if (session.Frame != new ScreenRect(80, 70, 500, 400))
        {
            throw new InvalidOperationException("Quick-edit session did not retain the resized frame.");
        }

        if (!session.TryBeginFrameEdit(new ScreenPoint(250, 200)))
        {
            throw new InvalidOperationException("Quick-edit session did not start a frame move from its body.");
        }

        session.UpdateFrameEdit(new ScreenPoint(300, 250));
        session.EndFrameEdit();
        if (session.Frame != new ScreenRect(130, 120, 550, 450))
        {
            throw new InvalidOperationException("Quick-edit session did not retain the moved frame.");
        }
    }

    private static void VerifyQuickEditDraftSelectionMaskEligibility()
    {
        var draft = new RectOverlayVisual(
            new ScreenRect(100, 100, 500, 400),
            IsDraft: true,
            ShowHandles: false,
            ShowReadout: false);

        var draftOptions = OverlaySurface.GetQuickEditRenderOptions(InteractionMode.ScreenshotRegionSelect, draft);
        if (!draftOptions.DrawOuterDim || draftOptions.ShowPaletteAndInk || draftOptions.ShowSelectionFill)
        {
            throw new InvalidOperationException("Quick Edit draft did not keep the selected area free of visual effects while a selection was being dragged.");
        }

        var committed = draft with { IsDraft = false };
        var committedOptions = OverlaySurface.GetQuickEditRenderOptions(InteractionMode.ScreenshotRegionSelect, committed);
        if (!committedOptions.DrawOuterDim || !committedOptions.ShowPaletteAndInk || committedOptions.ShowSelectionFill)
        {
            throw new InvalidOperationException("Quick Edit completed selection did not keep its selected area free of the blue fill.");
        }

        var nonQuickEditOptions = OverlaySurface.GetQuickEditRenderOptions(InteractionMode.RegionMaskSelect, draft);
        if (nonQuickEditOptions.DrawOuterDim || nonQuickEditOptions.ShowPaletteAndInk || !nonQuickEditOptions.ShowSelectionFill)
        {
            throw new InvalidOperationException("A non-Quick-Edit rectangle selection incorrectly applied Quick Edit rendering.");
        }
    }

    private static void VerifyQuickEditTextCallout()
    {
        QuickEditInkStore.Reset();
        QuickEditInkStore.BeginText(new ScreenPoint(20, 30));
        QuickEditInkStore.AppendText("Привет");
        QuickEditInkStore.CommitText();
        var callout = QuickEditInkStore.Strokes.Single();
        if (callout.Text != "Привет" || !QuickEditInkStore.TryBeginCalloutPointerDrag(callout.CalloutHandle))
            throw new InvalidOperationException("Inline Quick Edit text must expose a draggable callout pointer.");
        QuickEditInkStore.UpdateCalloutPointerDrag(new ScreenPoint(120, 140));
        QuickEditInkStore.EndCalloutPointerDrag();
        if (callout.CalloutTarget != new ScreenPoint(120, 140))
            throw new InvalidOperationException("The callout pointer must follow its drag target.");
        QuickEditInkStore.Reset();
    }

    private static void VerifyQuickEditTwoTailCallout()
    {
        QuickEditInkStore.Reset();
        QuickEditInkStore.AddText(new ScreenPoint(20, 30), "Текст");
        var callout = QuickEditInkStore.Strokes.Single();
        var firstBase = callout.GetCalloutBase(0);
        var secondBase = callout.GetCalloutBase(1);
        if (firstBase.Y != secondBase.Y || firstBase.X >= secondBase.X)
            throw new InvalidOperationException("Both green callout handles must begin on the lower edge.");
        if (!QuickEditInkStore.TryBeginCalloutPointerDrag(firstBase))
            throw new InvalidOperationException("First tail handle cannot be dragged.");
        QuickEditInkStore.UpdateCalloutPointerDrag(new ScreenPoint(55, 125));
        QuickEditInkStore.EndCalloutPointerDrag();
        if (callout.CalloutTarget != new ScreenPoint(55, 125) || callout.CalloutTarget2 is not null)
            throw new InvalidOperationException("Dragging the first handle must create only the first tail.");
        if (!QuickEditInkStore.TryBeginCalloutPointerDrag(secondBase))
            throw new InvalidOperationException("Second tail handle cannot be dragged.");
        QuickEditInkStore.UpdateCalloutPointerDrag(new ScreenPoint(135, 125));
        QuickEditInkStore.EndCalloutPointerDrag();
        if (callout.CalloutTarget != new ScreenPoint(55, 125) || callout.CalloutTarget2 != new ScreenPoint(135, 125))
            throw new InvalidOperationException("Second tail must not move the first tail.");
        if (!QuickEditInkStore.Undo() || QuickEditInkStore.Strokes.Single().CalloutTarget2 is not null)
            throw new InvalidOperationException("Undo must remove only the second tail.");
        if (!QuickEditInkStore.Redo())
            throw new InvalidOperationException("Redo must restore the second tail.");
        var restored = QuickEditInkStore.Strokes.Single();
        if (!QuickEditInkStore.TryBeginStrokeDrag(new ScreenPoint(25, 35)))
            throw new InvalidOperationException("Callout cannot be moved after editing tails.");
        QuickEditInkStore.UpdateStrokeDrag(new ScreenPoint(35, 45));
        QuickEditInkStore.EndStrokeDrag();
        if (restored.CalloutTarget != new ScreenPoint(55, 125) || restored.CalloutTarget2 != new ScreenPoint(135, 125))
            throw new InvalidOperationException("Moving the callout must keep both tail tips fixed on the image.");
    }

    private static void VerifyQuickEditTwoTailExport()
    {
        var white = Enumerable.Repeat((byte)255, 180 * 180 * 4).ToArray();
        var source = BitmapSource.Create(180, 180, 96, 96, PixelFormats.Bgra32, null, white, 720);
        var callout = new QuickEditStroke(QuickEditTool.Text, [new ScreenPoint(20, 20)])
        {
            Text = "Text",
            CalloutTarget = new ScreenPoint(50, 130),
            CalloutTarget2 = new ScreenPoint(130, 130),
        };
        var image = QuickEditImageComposer.Compose(source, new ScreenRect(0, 0, 180, 180), [callout], null);
        var pixels = new byte[white.Length];
        image.CopyPixels(pixels, 720, 0);
        var leftYellow = 0;
        var rightYellow = 0;
        var red = 0;
        var green = 0;
        for (var y = 70; y <= 125; y++)
        for (var x = 25; x <= 145; x++)
        {
            var offset = (y * 180 + x) * 4;
            var b = pixels[offset]; var g = pixels[offset + 1]; var r = pixels[offset + 2];
            if (r > 220 && g > 220 && b < 245)
            {
                if (x < 90) leftYellow++; else rightYellow++;
            }
            if (r > 180 && g < 80 && b < 80) red++;
            if (g > 140 && r < 100 && b < 100) green++;
        }
        if (leftYellow < 20 || rightYellow < 20 || red != 0 || green != 0)
            throw new InvalidOperationException("Export must contain two yellow triangular tails, no red arrow or green edit handles.");
    }

    private static void VerifyQuickEditCalloutTailGeometry()
    {
        var callout = new QuickEditStroke(QuickEditTool.Text, [new ScreenPoint(30, 20)]) { Text = "Text" };
        var tip = new ScreenPoint(65, 120);
        var triangle = QuickEditCalloutGeometry.TailTriangle(callout, 0, tip);
        if (triangle.BaseLeft.Y != 20 + callout.CalloutHeight || triangle.BaseRight.Y != 20 + callout.CalloutHeight
            || triangle.BaseRight.X - triangle.BaseLeft.X != 20
            || triangle.Tip != tip)
            throw new InvalidOperationException("A callout tail must be a wide triangle rooted on the lower edge.");
        callout.CalloutBoxWidth = 540;
        var expanded = QuickEditCalloutGeometry.TailTriangle(callout, 0, tip);
        if (expanded.BaseRight.X - expanded.BaseLeft.X != 60 || expanded.Tip != tip)
            throw new InvalidOperationException("Widening the callout must widen the tail base proportionally without moving its tip.");
    }

    private static void VerifyQuickEditCalloutBaseEndpoints()
    {
        QuickEditInkStore.Reset();
        QuickEditInkStore.AddText(new ScreenPoint(30, 30), "Text");
        var callout = QuickEditInkStore.Strokes.Single();
        if (!QuickEditInkStore.TryBeginCalloutPointerDrag(callout.GetCalloutBase(0)))
            throw new InvalidOperationException("Cannot create a tail from its initial green point.");
        var tip = new ScreenPoint(130, 160);
        QuickEditInkStore.UpdateCalloutPointerDrag(tip);
        QuickEditInkStore.EndCalloutPointerDrag();
        var original = QuickEditCalloutGeometry.TailTriangle(callout, 0, tip);
        if (!QuickEditInkStore.TryBeginCalloutPointerDrag(original.BaseLeft))
            throw new InvalidOperationException("The first base endpoint must be draggable.");
        QuickEditInkStore.UpdateCalloutPointerDrag(original.BaseLeft.Offset(-30, 0));
        QuickEditInkStore.EndCalloutPointerDrag();
        var widened = QuickEditCalloutGeometry.TailTriangle(callout, 0, tip);
        if (widened.BaseLeft.X != original.BaseLeft.X - 30 || widened.BaseRight != original.BaseRight
            || callout.CalloutTarget != tip)
            throw new InvalidOperationException("Dragging one green base endpoint must change only that side of the tail.");
        if (!QuickEditInkStore.TryBeginCalloutPointerDrag(widened.BaseRight))
            throw new InvalidOperationException("The second base endpoint must be draggable.");
        QuickEditInkStore.UpdateCalloutPointerDrag(new ScreenPoint(120, 30));
        QuickEditInkStore.EndCalloutPointerDrag();
        var movedToTop = QuickEditCalloutGeometry.TailTriangle(callout, 0, tip);
        if (movedToTop.BaseLeft != widened.BaseLeft || movedToTop.BaseRight != new ScreenPoint(120, 30)
            || callout.CalloutTarget != tip)
            throw new InvalidOperationException("Moving the second endpoint to the top edge must preserve the first endpoint and tip.");
        if (!QuickEditInkStore.Undo())
            throw new InvalidOperationException("Moving the second endpoint must be undoable.");
        var afterOneUndo = QuickEditInkStore.Strokes.Single();
        var priorShape = QuickEditCalloutGeometry.TailTriangle(afterOneUndo, 0, tip);
        if (priorShape.BaseLeft != widened.BaseLeft || priorShape.BaseRight != original.BaseRight)
            throw new InvalidOperationException("Undo must restore only the second endpoint.");
        if (!QuickEditInkStore.Undo())
            throw new InvalidOperationException("Tail-base width change must be undoable.");
        var restored = QuickEditInkStore.Strokes.Single();
        var oldShape = QuickEditCalloutGeometry.TailTriangle(restored, 0, tip);
        if (oldShape.BaseLeft != original.BaseLeft || oldShape.BaseRight != original.BaseRight)
            throw new InvalidOperationException("Undo must restore both base endpoints.");
        QuickEditInkStore.Reset();
    }

    private static void VerifyQuickEditDoubleClickReopensText()
    {
        QuickEditInkStore.Reset();
        var selection = new RectSelectionController(() => InteractionMode.ScreenshotRegionSelect,
            () => false, () => false, () => { }, () => { });
        selection.SetPendingScreenshotRegion(new ScreenRect(0, 0, 500, 500));
        var controller = new RectToolsInputController(selection, new RegionMaskController(),
            new RegionSpotlightController(), () => InteractionMode.ScreenshotRegionSelect,
            () => new AppSettings(), _ => { }, () => { }, () => { }, () => { },
            _ => { }, _ => { }, () => { }, () => { },
            _ => Task.FromResult<BitmapSource?>(null), _ => Task.CompletedTask,
            _ => Task.CompletedTask, (_, _) => { });
        QuickEditInkStore.AddText(new ScreenPoint(30, 30), "Old");
        var point = new ScreenPoint(100, 55);
        controller.HandleMouseDown(point, System.Windows.Input.MouseButton.Left);
        controller.HandleMouseUp(point, System.Windows.Input.MouseButton.Left);
        controller.HandleMouseDown(point, System.Windows.Input.MouseButton.Left);
        controller.HandleMouseUp(point, System.Windows.Input.MouseButton.Left);
        if (QuickEditInkStore.ActiveText?.Text != "Old")
            throw new InvalidOperationException("Double-clicking an existing callout must reopen its text at the end.");
        controller.HandleTextInput("!");
        QuickEditInkStore.CommitText();
        if (QuickEditInkStore.Strokes.Single().Text != "Old!")
            throw new InvalidOperationException("Typing after double-click must append to the existing text.");
        QuickEditInkStore.Reset();
    }

    private static void VerifyQuickEditTextEditorSynchronization()
    {
        QuickEditInkStore.Reset();
        QuickEditInkStore.AddText(new ScreenPoint(30, 30), "Long original text");
        var callout = QuickEditInkStore.Strokes.Single();
        callout.Text = new string('Ж', 200);
        if (callout.CalloutWidth != callout.CalloutBoxWidth)
            throw new InvalidOperationException("Long text must wrap or scroll inside the box, not push the caret off screen.");
        callout.Text = "Long original text";
        QuickEditInkStore.Select(QuickEditInkStore.Strokes.Single());
        if (!QuickEditInkStore.BeginEditingSelectedText())
            throw new InvalidOperationException("Existing callout must enter text editing.");
        QuickEditInkStore.ReplaceActiveText("Selected replacement");
        QuickEditInkStore.CommitText();
        if (QuickEditInkStore.Strokes.Single().Text != "Selected replacement")
            throw new InvalidOperationException("TextBox edits must synchronize with callout text.");
        if (!QuickEditInkStore.Undo() || QuickEditInkStore.Strokes.Single().Text != "Long original text")
            throw new InvalidOperationException("TextBox edits must remain undoable.");
        QuickEditInkStore.Reset();
    }

    private static void VerifyQuickEditCalloutReferenceGeometry()
    {
        QuickEditInkStore.Reset();
        QuickEditInkStore.AddText(new ScreenPoint(30, 30), "Text");
        var callout = QuickEditInkStore.Strokes.Single();
        if (callout.CalloutWidth < 180 || callout.CalloutHeight < 50)
            throw new InvalidOperationException("Reference callout box must start near 180x50.");
        if (callout.GetResizeHandles().Length != 8)
            throw new InvalidOperationException("Reference callout must expose eight box resize handles.");
        var tip = new ScreenPoint(100, 180);
        if (!QuickEditInkStore.TryBeginCalloutPointerDrag(callout.GetCalloutBase(0)))
            throw new InvalidOperationException("First tail cannot be created.");
        QuickEditInkStore.UpdateCalloutPointerDrag(tip);
        QuickEditInkStore.EndCalloutPointerDrag();
        if (!QuickEditInkStore.TryBeginStrokeDrag(new ScreenPoint(100, 55)))
            throw new InvalidOperationException("Callout box cannot be moved.");
        QuickEditInkStore.UpdateStrokeDrag(new ScreenPoint(120, 65));
        QuickEditInkStore.EndStrokeDrag();
        if (callout.CalloutTarget != tip)
            throw new InvalidOperationException("Tail tip must stay at its image coordinate when box moves.");
        var firstBase = QuickEditCalloutGeometry.TailBaseEndpoints(callout, 0).First;
        if (!QuickEditInkStore.TryBeginCalloutPointerDrag(firstBase))
            throw new InvalidOperationException("Existing tail base must be draggable.");
        QuickEditInkStore.UpdateCalloutPointerDrag(new ScreenPoint(50, 55));
        QuickEditInkStore.EndCalloutPointerDrag();
        if (callout.GetCalloutBaseEndpointAnchor(0, 0)?.Edge != CalloutEdge.Left
            || QuickEditCalloutGeometry.TailBaseEndpoints(callout, 0).First.X != 50 || callout.CalloutTarget != tip)
            throw new InvalidOperationException("Tail base must move onto the box contour without moving its tip.");
        var rightHandle = callout.GetResizeHandles()[3];
        if (!QuickEditInkStore.TryBeginResizeDrag(rightHandle))
            throw new InvalidOperationException("Callout right size handle cannot be dragged.");
        QuickEditInkStore.UpdateResizeDrag(rightHandle.Offset(40, 0));
        QuickEditInkStore.EndResizeDrag();
        if (callout.CalloutWidth != 220 || callout.CalloutTarget != tip
            || QuickEditCalloutGeometry.TailBaseEndpoints(callout, 0).First.X != 50)
            throw new InvalidOperationException("Resizing callout must preserve contour anchor and image tip.");
        if (!QuickEditInkStore.Undo() || QuickEditInkStore.Strokes.Single().CalloutWidth != 180)
            throw new InvalidOperationException("Callout resize must be undoable.");
        QuickEditInkStore.Reset();
    }

    private static void VerifyQuickEditCalloutSeamlessExport()
    {
        var white = Enumerable.Repeat((byte)255, 300 * 240 * 4).ToArray();
        var source = BitmapSource.Create(300, 240, 96, 96, PixelFormats.Bgra32, null, white, 300 * 4);
        var callout = new QuickEditStroke(QuickEditTool.Text, [new ScreenPoint(30, 30)])
        {
            Text = "Text",
            CalloutTarget = new ScreenPoint(90, 200),
        };
        var image = QuickEditImageComposer.Compose(source, new ScreenRect(0, 0, 300, 240), [callout], null);
        var pixels = new byte[white.Length];
        image.CopyPixels(pixels, 300 * 4, 0);
        var baseOffset = (80 * 300 + 90) * 4;
        if (pixels[baseOffset] < 100 || pixels[baseOffset + 1] < 220 || pixels[baseOffset + 2] < 220)
            throw new InvalidOperationException("Tail base must be yellow, without a gray border seam.");
    }

    private static void VerifyQuickEditCalloutProjection()
    {
        var callout = new QuickEditStroke(QuickEditTool.Text, [new ScreenPoint(100, 100)]) { Text = "Text" };
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, 300, 180));
            QuickEditCalloutRenderer.Draw(context, callout,
                point => new System.Windows.Point(point.X * 0.8, point.Y * 0.8), editing: false);
        }
        var image = new RenderTargetBitmap(300, 180, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var pixels = new byte[300 * 180 * 4];
        image.CopyPixels(pixels, 300 * 4, 0);
        var inside = (100 * 300 + 215) * 4;
        var outside = (100 * 300 + 230) * 4;
        if (pixels[inside] > 210 || pixels[outside] < 245)
            throw new InvalidOperationException("Callout box must use the same screen-to-local projection as its handles.");
    }

    private static void RenderCalloutReference()
    {
        const int width = 560;
        const int height = 300;
        var dark = new byte[width * height * 4];
        for (var i = 0; i < dark.Length; i += 4)
        {
            dark[i] = 24; dark[i + 1] = 24; dark[i + 2] = 24; dark[i + 3] = 255;
        }
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, dark, width * 4);
        var first = new QuickEditStroke(QuickEditTool.Text, [new ScreenPoint(35, 55)])
        {
            Text = "sdfsdfsdf",
            CalloutTarget2 = new ScreenPoint(260, 245),
        };
        var second = new QuickEditStroke(QuickEditTool.Text, [new ScreenPoint(320, 100)])
        {
            Text = "sdfsdfsdf",
            CalloutTarget = new ScreenPoint(270, 155),
            CalloutTarget2 = new ScreenPoint(375, 25),
            CalloutAnchor1 = new CalloutAnchor(CalloutEdge.Left, 0.6),
            CalloutAnchor2 = new CalloutAnchor(CalloutEdge.Top, 0.25),
            CalloutBaseStart2 = new CalloutAnchor(CalloutEdge.Top, 0.15),
            CalloutBaseEnd2 = new CalloutAnchor(CalloutEdge.Top, 0.55),
            CalloutBoxHeight = 120,
        };
        var image = QuickEditImageComposer.Compose(source, new ScreenRect(0, 0, width, height), [first, second], null);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var output = Path.GetFullPath("Verification/artifacts/callout-reference.png");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var file = File.Create(output);
        encoder.Save(file);
        Console.WriteLine(output);
    }

    private static void VerifyFineQuickEditMosaic()
    {
        var pixels = new byte[12 * 12 * 4];
        for (var y = 0; y < 12; y++)
        for (var x = 0; x < 12; x++)
        {
            var offset = (y * 12 + x) * 4;
            var value = (byte)(x < 6 ? 0 : 255);
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
        var source = BitmapSource.Create(12, 12, 96, 96, PixelFormats.Bgra32, null, pixels, 48);
        QuickEditInkStore.SetSource(new ScreenRect(0, 0, 12, 12), source);
        var mosaic = new byte[pixels.Length];
        QuickEditInkStore.PixelatedSource!.CopyPixels(mosaic, 48, 0);
        if (mosaic[(4 * 12 + 2) * 4] != 0 || mosaic[(4 * 12 + 8) * 4] != 255)
            throw new InvalidOperationException("Quick Edit mosaic blocks must preserve detail at a six-pixel scale.");
        QuickEditInkStore.Reset();
    }

    private static void VerifyStaticPinArrowControls()
    {
        var arrow = new StaticPinArrow(new ScreenPoint(10, 20), new ScreenPoint(100, 20), "#FF0000", 3);
        if (!arrow.TryHitControl(new ScreenPoint(40, 20), out var index) || index != 1)
        {
            throw new InvalidOperationException("A Static Pin arrow must expose its first Bézier control in source coordinates.");
        }
        arrow.SetControl(index, new ScreenPoint(40, 50));
        if (arrow.GetControls().First != new ScreenPoint(40, 50)
            || arrow.GetControls().Second != new ScreenPoint(70, 20))
        {
            throw new InvalidOperationException("Editing one Static Pin control must preserve the other.");
        }
        if (!arrow.TryHitControl(new ScreenPoint(100, 20), out var endHandle) || endHandle != 4)
            throw new InvalidOperationException("The Static Pin arrow endpoint must be draggable.");
        arrow.SetControl(endHandle, new ScreenPoint(110, 30));
        if (arrow.End != new ScreenPoint(110, 30))
            throw new InvalidOperationException("Dragging the Static Pin arrow endpoint must update its geometry.");
    }

    private static void VerifyQuickEditExportIncludesArrow()
    {
        var pixels = Enumerable.Repeat((byte)255, 100 * 100 * 4).ToArray();
        var source = BitmapSource.Create(100, 100, 96, 96, PixelFormats.Bgra32, null, pixels, 400);
        source.Freeze();
        var frame = new ScreenRect(0, 0, 100, 100);
        var arrow = new QuickEditStroke(QuickEditTool.Arrow,
            [new ScreenPoint(10, 50), new ScreenPoint(90, 50)]);
        arrow.SetArrowControl(1, new ScreenPoint(35, 10));
        arrow.SetArrowControl(2, new ScreenPoint(65, 10));
        var exported = QuickEditImageComposer.Compose(source, frame, [arrow], null);
        var output = new byte[100 * 100 * 4];
        exported.CopyPixels(output, 400, 0);
        var curvePixel = (20 * 100 + 50) * 4;
        if (output[curvePixel + 2] <= output[curvePixel + 1]
            || output[curvePixel + 2] <= output[curvePixel])
        {
            throw new InvalidOperationException("A copied Quick Edit image must contain its curved arrow.");
        }
    }

    private static void VerifyEditableQuickEditArrow()
    {
        QuickEditInkStore.Reset();
        QuickEditInkStore.Tool = QuickEditTool.Arrow;
        QuickEditInkStore.Begin(new ScreenPoint(10, 10));
        QuickEditInkStore.Add(new ScreenPoint(100, 10));
        QuickEditInkStore.Commit();
        var arrow = QuickEditInkStore.Strokes.Single();
        var (first, second) = arrow.GetArrowControls();
        if (first != new ScreenPoint(40, 10) || second != new ScreenPoint(70, 10)
            || !QuickEditInkStore.TryBeginArrowControlDrag(first))
        {
            throw new InvalidOperationException("A Quick Edit arrow must expose two draggable controls.");
        }
        QuickEditInkStore.UpdateArrowControlDrag(new ScreenPoint(40, 40));
        QuickEditInkStore.EndArrowControlDrag();
        if (arrow.GetArrowControls().First != new ScreenPoint(40, 40)
            || arrow.GetArrowControls().Second != second)
        {
            throw new InvalidOperationException("Dragging the first Quick Edit arrow control must not move the second.");
        }
        if (!QuickEditInkStore.TryBeginArrowControlDrag(new ScreenPoint(100, 10)))
            throw new InvalidOperationException("The Quick Edit arrow endpoint must be draggable.");
        QuickEditInkStore.UpdateArrowControlDrag(new ScreenPoint(110, 20));
        QuickEditInkStore.EndArrowControlDrag();
        if (arrow.Points[^1] != new ScreenPoint(110, 20))
            throw new InvalidOperationException("Dragging the Quick Edit arrow endpoint must update its geometry.");
        QuickEditInkStore.Tool = QuickEditTool.Line;
        if (!QuickEditInkStore.TrySelectArrowAt(new ScreenPoint(75, 17))
            || !ReferenceEquals(QuickEditInkStore.SelectedArrow, arrow))
        {
            throw new InvalidOperationException("An existing Quick Edit arrow must be selectable after switching tools.");
        }
        QuickEditInkStore.Reset();
    }

    private static void VerifyEditableCurvedAnnotationArrow()
    {
        var arrow = new AnnotationShape
        {
            Tool = AnnotationTool.Arrow,
            Start = new ScreenPoint(0, 0),
            End = new ScreenPoint(90, 0)
        };
        var (first, second) = arrow.GetArrowControls();
        if (!AnnotationHitTesting.TryHitEditHandle(arrow, first, out var handle)
            || handle != AnnotationEditHandle.Control1)
        {
            throw new InvalidOperationException("The first curved-arrow control must have an editable hit target.");
        }
        AnnotationGeometry.ResizeShape(arrow, handle, new ScreenPoint(30, 30), shift: false);
        var moved = arrow.Clone();
        moved.Offset(10, 20);
        var (movedFirst, movedSecond) = moved.GetArrowControls();
        if (movedFirst != new ScreenPoint(40, 50) || movedSecond != new ScreenPoint(70, 20)
            || moved.Start != new ScreenPoint(10, 20) || moved.End != new ScreenPoint(100, 20))
        {
            throw new InvalidOperationException("Cloning and moving an arrow must preserve its bend.");
        }
        arrow.SetArrowControl(1, new ScreenPoint(30, 80));
        arrow.SetArrowControl(2, new ScreenPoint(60, 80));
        if (!AnnotationHitTesting.TryFindShapeAt([arrow], new ScreenPoint(45, 60), out _)
            || AnnotationHitTesting.TryFindShapeAt([arrow], new ScreenPoint(45, 0), out _))
        {
            throw new InvalidOperationException("Arrow hit testing must follow the curve, not the original chord.");
        }
        var document = new AnnotationDocument(() => 0);
        document.BeginStroke(AnnotationTool.Arrow, new ScreenPoint(0, 0), new AppSettings());
        document.UpdateStroke(new ScreenPoint(90, 0), shift: false);
        document.CommitStroke();
        if (document.ObjectEditShape?.Tool != AnnotationTool.Arrow)
        {
            throw new InvalidOperationException("A newly drawn arrow must expose its control handles immediately.");
        }
    }

    private static void VerifyCurvedArrowGeometry()
    {
        var start = new ScreenPoint(0, 0);
        var end = new ScreenPoint(90, 0);
        var (first, second) = CurvedArrowGeometry.DefaultControls(start, end);
        if (first != new ScreenPoint(30, 0) || second != new ScreenPoint(60, 0)
            || CurvedArrowGeometry.PointAt(start, first, second, end, 0.5) != new ScreenPoint(45, 0)
            || CurvedArrowGeometry.TangentAt(start, first, second, end, 1) != new ScreenPoint(90, 0))
        {
            throw new InvalidOperationException("A new arrow must be straight with a tangent aligned to its endpoint.");
        }

        var curvedMiddle = CurvedArrowGeometry.PointAt(start, new ScreenPoint(30, 30), second, end, 0.5);
        if (curvedMiddle.Y <= 0 || CurvedArrowGeometry.PointAt(start, new ScreenPoint(30, 30), second, end, 1) != end)
        {
            throw new InvalidOperationException("Moving a control point must bend the shaft without moving the arrow endpoint.");
        }
    }

    private static void VerifyQuickEditSelectionCursor()
    {
        var draft = new RectOverlayVisual(new ScreenRect(100, 100, 500, 400),
            IsDraft: true, ShowHandles: false, ShowReadout: true);
        if (AnnotationCursor.ForQuickEditSelection(null) != System.Windows.Input.Cursors.Cross
            || AnnotationCursor.ForQuickEditSelection(draft) != System.Windows.Input.Cursors.Cross
            || AnnotationCursor.ForQuickEditSelection(draft with { IsDraft = false }) == System.Windows.Input.Cursors.Arrow)
        {
            throw new InvalidOperationException("Quick Edit must use a crosshair while selecting and a tool cursor after selection.");
        }
        QuickEditInkStore.Reset();
        QuickEditInkStore.AddText(new ScreenPoint(120, 120), "Text");
        QuickEditInkStore.Select(QuickEditInkStore.Strokes.Single());
        var selected = draft with { IsDraft = false };
        if (AnnotationCursor.ForQuickEditSelection(selected, new ScreenPoint(200, 145)) != System.Windows.Input.Cursors.IBeam
            || AnnotationCursor.ForQuickEditSelection(selected, new ScreenPoint(120, 120)) != System.Windows.Input.Cursors.SizeNWSE
            || AnnotationCursor.ForQuickEditSelection(selected, QuickEditInkStore.SelectedCallout!.GetCalloutBase(0)) != System.Windows.Input.Cursors.SizeAll)
            throw new InvalidOperationException("Text, resize, and pointer handles need distinct hover cursors.");
        QuickEditInkStore.Reset();
    }

    private static void VerifyQuickEditPaletteAndGestureTools()
    {
        var frame = new ScreenRect(100, 100, 500, 400);
        for (var index = 0; index < QuickEditPalette.ItemCount; index++)
        {
            var item = QuickEditPalette.ItemBounds(frame, index);
            var center = new ScreenPoint((item.Left + item.Right) / 2, (item.Top + item.Bottom) / 2);
            if (!QuickEditPalette.TryHit(frame, center, out var action) || (int)action != index)
            {
                throw new InvalidOperationException($"Quick Edit palette item {index} does not match its click target.");
            }
        }

        foreach (var tool in new[] { QuickEditTool.Line, QuickEditTool.Arrow, QuickEditTool.Rectangle, QuickEditTool.Ellipse, QuickEditTool.Mosaic })
        {
            QuickEditInkStore.Reset();
            QuickEditInkStore.Tool = tool;
            QuickEditInkStore.Begin(new ScreenPoint(10, 10));
            QuickEditInkStore.Add(new ScreenPoint(40, 20));
            QuickEditInkStore.Add(new ScreenPoint(80, 50));
            QuickEditInkStore.Commit();
            if (QuickEditInkStore.Strokes.Single().Points.Count != 2
                || QuickEditInkStore.Strokes.Single().Points[^1] != new ScreenPoint(80, 50))
            {
                throw new InvalidOperationException($"Quick Edit {tool} retained a pencil path instead of start/end geometry.");
            }
        }

        QuickEditInkStore.Reset();
        QuickEditInkStore.AddText(new ScreenPoint(20, 30), "Текст");
        if (QuickEditInkStore.Strokes.Single().Text != "Текст")
        {
            throw new InvalidOperationException("Quick Edit text tool did not retain entered text.");
        }
        QuickEditInkStore.Reset();
    }

    private static void AssertStrokeFragments(
        IReadOnlyList<StaticPinStrokeFragment> actual,
        params (ScreenPoint Start, ScreenPoint End, bool Inside)[] expected)
    {
        if (actual.Count != expected.Length)
        {
            throw new InvalidOperationException($"Expected {expected.Length} pin stroke fragments but got {actual.Count}.");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (actual[index].Start != expected[index].Start
                || actual[index].End != expected[index].End
                || actual[index].Inside != expected[index].Inside)
            {
                throw new InvalidOperationException($"Pin stroke fragment {index} did not match the expected segment.");
            }
        }
    }

    private static void VerifyStaticPinBorderStyles()
    {
        var idle = StaticPinWindow.ResolveBorderStyle(hovered: false, dragging: false, resizing: false, drawing: false);
        if (idle.Width != 1 || idle.Color != System.Drawing.Color.FromArgb(255, 128, 128, 128))
        {
            throw new InvalidOperationException("Idle static pin did not use a neutral 1px border.");
        }

        foreach (var style in new[]
        {
            StaticPinWindow.ResolveBorderStyle(hovered: true, dragging: false, resizing: false, drawing: false),
            StaticPinWindow.ResolveBorderStyle(hovered: false, dragging: true, resizing: false, drawing: false),
            StaticPinWindow.ResolveBorderStyle(hovered: false, dragging: false, resizing: true, drawing: false),
            StaticPinWindow.ResolveBorderStyle(hovered: false, dragging: false, resizing: false, drawing: true),
        })
        {
            if (style.Width != 2 || style.Color != System.Drawing.Color.FromArgb(255, 35, 211, 200))
            {
                throw new InvalidOperationException("An active static pin state did not use a cyan 2px border.");
            }
        }
    }

    private static void AddLine(AnnotationDocument document, AppSettings settings, double x, string color)
    {
        settings.AnnotationColor = color;
        document.BeginStroke(AnnotationTool.Line, new ScreenPoint(x, 10), settings);
        document.UpdateStroke(new ScreenPoint(x, 90), shift: false);
        document.CommitStroke();
    }

    private static ShortcutSettings CreateLegacyShortcuts()
    {
        return new ShortcutSettings
        {
            LayoutVersion = 0,
            ToggleLaserActivation = "Ctrl+Alt+L",
            ToggleAnnotate = "Ctrl+Alt+D",
            PushToAnnotate = "Alt+A",
            ToggleCursorHighlight = "Ctrl+Alt+U",
            ToggleSpotlight = "Ctrl+Alt+S",
            ToggleMagnifier = "Ctrl+Alt+M",
            TogglePinnedLens = "Ctrl+Alt+P",
            ToggleRegionMask = "Ctrl+Alt+H",
            ClearRegionMasks = "Ctrl+Alt+Shift+H",
            ToggleRegionSpotlight = "Ctrl+Alt+Shift+S",
            ClearRegionSpotlights = "Ctrl+Alt+Shift+X",
            ToggleFadingAnnotations = "Ctrl+Alt+F",
            ToggleTimer = "Ctrl+Alt+N",
            ToggleToolbar = "Ctrl+Alt+T",
            TakeScreenshot = "Ctrl+Alt+C",
            TakeRegionScreenshot = "Ctrl+Alt+Shift+C",
            ToggleScreenBoard = "Ctrl+Alt+G",
            ToggleBlackScreen = "Ctrl+Alt+B",
            ToggleWhiteScreen = "Ctrl+Alt+W",
            ExitApp = "Ctrl+Alt+Q",
            ToolArrow = "A",
            ToolRectangle = "R",
            ToolEllipse = "C",
            ToolLine = "L",
            ToolPencil = "P",
            ToolHighlighter = "H",
            ToolText = "T",
            ToolMove = "M",
            ToolStep = "N",
            Color1 = "1",
            Color2 = "2",
            Color3 = "3",
            Color4 = "4",
            Color5 = "5",
            ThicknessDown = "[",
            ThicknessUp = "]",
            Undo = "Ctrl+Z",
            Redo = "Ctrl+Y",
            DeleteSelection = "Backspace",
            Clear = "Delete",
            ClearAlternate = "E",
            ExitAnnotate = "Esc",
        };
    }

    private static BitmapSource CreateBitmap(int width, int height, byte[] pixels)
    {
        var stride = width * 4;
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32,
            palette: null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static void AssertPixels(BitmapSource bitmap, byte[] expected, string scenario)
    {
        var actual = new byte[expected.Length];
        bitmap.CopyPixels(actual, bitmap.PixelWidth * 4, 0);
        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"{scenario} failed. Expected [{string.Join(", ", expected)}], " +
                $"actual [{string.Join(", ", actual)}].");
        }
    }
}
