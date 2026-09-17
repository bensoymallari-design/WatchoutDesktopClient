using Watchout.Core.Models;
using Watchout.Core.Stage;
using Xunit;

namespace Watchout.Core.Tests;

public class StageGeometryTests
{
    static Display Display(string id = "d1", double x = 0, double y = 0, double w = 1920, double h = 1080) => new()
    {
        Id = id, Name = id, X = x, Y = y, Width = w, Height = h, OutputType = OutputType.GPU,
        Channel = 1, NodeId = "local", Enabled = true, BlendWidth = 128,
    };

    [Fact]
    public void FitCoverMaps4kMediaOnto4kDisplayAt1To1()
    {
        var uhd = Display(w: 3840, h: 2160);
        var fit = StageGeometry.FitTransform(new Asset { Width = 3840, Height = 2160 }, uhd, "cover");
        Assert.Equal(100, fit.Scale.X);
        Assert.Equal(100, fit.Scale.Y);
    }

    [Fact]
    public void FitCoverStretchesOtherAspects()
    {
        var fit = StageGeometry.FitTransform(new Asset { Width = 1280, Height = 720 }, Display(), "cover");
        Assert.Equal(0, fit.Position.X);
        Assert.Equal(0, fit.Position.Y);
        Assert.Equal(150, fit.Scale.X);
        Assert.Equal(150, fit.Scale.Y);
    }

    [Fact]
    public void FitContainLetterboxes()
    {
        var fit = StageGeometry.FitTransform(new Asset { Width = 1920, Height = 1920 }, Display(), "contain");
        Assert.Equal(56.25, fit.Scale.X);
        Assert.Equal(56.25, fit.Scale.Y);
        Assert.Equal(420, fit.Position.X);
        Assert.Equal(0, fit.Position.Y);
    }

    [Fact]
    public void SnapMediaLeftEdgeToDisplayEdge()
    {
        var snapped = StageGeometry.SnapRect(new StageRect(8, 3, 1920, 1080), [0, 1920], [0, 1080], 12);
        Assert.Equal(0, snapped.X);
        Assert.Equal(0, snapped.Y);
    }

    [Fact]
    public void SnapValuePicksNearestGuide()
    {
        Assert.Equal(0, StageGeometry.SnapValue(5, [0d, 1920], 8));
        Assert.Equal(40, StageGeometry.SnapValue(40, [0d, 1920], 8));
    }

    [Fact]
    public void HitDisplayPrefersTopmost()
    {
        var d1 = Display();
        var d2 = Display("d2", 100, 100);
        var hit = StageGeometry.HitDisplay([d1, d2], (150, 150));
        Assert.Equal("d2", hit?.Id);
    }

    [Fact]
    public void DropSnapsOntoNearestDisplayWhenJustOutside()
    {
        var wall = Display("wall", 0, 0, 6720, 1344);
        Assert.Equal("wall", StageGeometry.HitDisplay([wall], (100, 100))?.Id);
        Assert.Null(StageGeometry.HitDisplay([wall], (-40, 80)));
        Assert.Equal("wall", StageGeometry.DropTarget([wall], (-40, 80), snap: true, snapDist: 120)?.Id);
        Assert.Null(StageGeometry.DropTarget([wall], (-4000, 80), snap: true, snapDist: 120));
        Assert.Null(StageGeometry.DropTarget([wall], (-40, 80), snap: false, snapDist: 120));
    }

    [Fact]
    public void FitCameraCentersAndZoomsTheWallIntoTheView()
    {
        var wall = new StageRect(0, 0, 1920, 1080);
        var cam = StageGeometry.FitCamera(wall, 800, 450, 48);
        Assert.Equal(960, cam.X);
        Assert.Equal(540, cam.Y);
        Assert.InRange(cam.Zoom, 0.25, 0.4);
        var display = StageGeometry.FitCamera(wall, 1920, 1080, 0);
        Assert.Equal(1, display.Zoom, 3);
        Assert.True(StageGeometry.SnapThreshold(0.18) >= 200);
    }

    [Fact]
    public void DisplayForCueUsesDisplayUnderOrigin()
    {
        var right = Display("right", 1920);
        Assert.Equal("right", StageGeometry.DisplayForCue([Display(), right], new Cue { Position = new Vec3 { X = 1920 } }).Id);
    }

    [Fact]
    public void DraggingDisplaySnapsFlushToNeighbor()
    {
        var left = Display("left");
        var moving = Display("right", 1908, 6);
        var guides = StageGeometry.DisplayMoveGuides([left, moving], moving.Id);
        var snapped = StageGeometry.SnapRect(new StageRect(moving.X, moving.Y, moving.Width, moving.Height), guides.X, guides.Y, 16);
        Assert.Equal(1920, snapped.X);
        Assert.Equal(0, snapped.Y);
    }

    [Fact]
    public void WallIsBoundingBox()
    {
        var row = Enumerable.Range(0, 4).Select(i => Display($"d{i}", i * 1920)).ToList();
        var wall = StageGeometry.WallRect(row) ?? throw new InvalidOperationException("wall");
        Assert.Equal(0, wall.X);
        Assert.Equal(0, wall.Y);
        Assert.Equal(7680, wall.W);
        Assert.Equal(1080, wall.H);

        var grid = new[]
        {
            Display("a"), Display("b", 1920), Display("c", 0, 1080), Display("d", 1920, 1080),
        };
        var g = StageGeometry.WallRect(grid) ?? throw new InvalidOperationException("grid");
        Assert.Equal(3840, g.W);
        Assert.Equal(2160, g.H);
    }

    [Fact]
    public void FitCoverMapsClipAcrossFourWideWall()
    {
        var fit = StageGeometry.FitTransform(1920, 1080, 0, 0, 7680, 1080);
        Assert.Equal(0, fit.Position.X);
        Assert.Equal(400, fit.Scale.X);
        Assert.Equal(100, fit.Scale.Y);
    }

    [Fact]
    public void FitCoverMapsClipAcross2x2Wall()
    {
        var fit = StageGeometry.FitTransform(1920, 1080, 0, 0, 3840, 2160);
        Assert.Equal(200, fit.Scale.X);
        Assert.Equal(200, fit.Scale.Y);
    }

    [Fact]
    public void ResizeSnapDoesNotGlueAFitCueToTheDisplay()
    {
        var zoom = 0.18;
        Assert.True(StageGeometry.SnapThreshold(zoom) >= 200);
        Assert.True(StageGeometry.EditSnapThreshold(zoom) < 80);
        Assert.True(StageGeometry.EditSnapThreshold(zoom) < StageGeometry.SnapThreshold(zoom));
        var grown = StageGeometry.ResizeRect(new StageRect(0, 0, 1920, 1080), "e", 80, 0);
        Assert.Equal(2000, grown.W);
        var edit = StageGeometry.SnapResizeRect(grown, "e", [0, 1920], [0, 1080], StageGeometry.EditSnapThreshold(zoom));
        Assert.Equal(2000, edit.W);
        var glued = StageGeometry.SnapResizeRect(grown, "e", [0, 1920], [0, 1080], StageGeometry.SnapThreshold(zoom));
        Assert.Equal(1920, glued.W);
    }

    [Fact]
    public void EditCueRectsKeepATimelineSelectionWhenPlayheadIsOffTheClip()
    {
        var cue = new Cue
        {
            Id = "c",
            Type = CueType.Media,
            AssetId = "a",
            Start = 10_000,
            Duration = 5_000,
            Position = new Vec3 { X = 40, Y = 80 },
            Scale = new Vec2 { X = 50, Y = 50 },
        };
        var asset = new Asset { Id = "a", Width = 1920, Height = 1080 };
        var selected = new Selection { Kind = SelectionKind.Cue, Ids = ["c"] };
        var rects = StageGeometry.EditCueRects([], [asset], [cue], selected);
        Assert.Single(rects);
        Assert.Equal("c", rects[0].Cue.Id);
        Assert.Equal(40, rects[0].Rect.X);
        Assert.Equal(960, rects[0].Rect.W);
        var handle = StageGeometry.HitEditTarget(StageEditMode.Cues, [Display()], rects, selected, (40, 80), 1);
        Assert.Equal(StageHitKind.CueHandle, handle.Kind);
        Assert.Equal("nw", handle.Handle);
    }

    [Fact]
    public void EdgeDragGrowsRightSide()
    {
        var grown = StageGeometry.ResizeRect(new StageRect(0, 0, 1920, 1080), "e", 80, 0);
        Assert.Equal(2000, grown.W);
        Assert.Equal(0, grown.X);
        Assert.Equal("e", StageGeometry.HitResizeHandle(new StageRect(0, 0, 1920, 1080), (1920, 540), 1));
        var snapped = StageGeometry.SnapResizeRect(new StageRect(0, 0, 1908, 1080), "e", [0, 1920], [0, 1080], 16);
        Assert.Equal(1920, snapped.W);
        var transform = StageGeometry.RectToCueTransform(new StageRect(10, 20, 3840, 1080), new Asset { Width = 1920, Height = 1080 });
        Assert.Equal(10, transform.Position.X);
        Assert.Equal(200, transform.Scale.X);
        Assert.Equal(100, transform.Scale.Y);
    }

    [Fact]
    public void WestEdgeDragMovesXAndShrinksWidth()
    {
        var shrunk = StageGeometry.ResizeRect(new StageRect(80, 0, 1920, 1080), "w", -80, 0);
        Assert.Equal(0, shrunk.X);
        Assert.Equal(2000, shrunk.W);
        var snapped = StageGeometry.SnapResizeRect(new StageRect(12, 0, 1908, 1080), "w", [0, 1920], [0, 1080], 16);
        Assert.Equal(0, snapped.X);
        Assert.Equal(1920, snapped.W);
    }

    [Fact]
    public void ClickDisplayCanvasHitsDisplayEvenUnderMedia()
    {
        var display = Display();
        var cue = new Cue
        {
            Id = "c",
            Type = CueType.Media,
            AssetId = "a",
            Position = new Vec3(),
            Scale = new Vec2 { X = 100, Y = 100 },
        };
        var asset = new Asset { Id = "a", Width = 1920, Height = 1080 };
        var rects = StageGeometry.CueRects([cue], [asset]);
        var none = new Selection();
        var overMedia = StageGeometry.HitEditTarget(StageEditMode.Cues, [display], rects, none, (200, 200), 1);
        Assert.Equal(StageHitKind.Cue, overMedia.Kind);
        Assert.Equal("c", overMedia.Id);

        var canvas = StageGeometry.HitEditTarget(StageEditMode.Displays, [display], rects, none, (200, 200), 1);
        Assert.Equal(StageHitKind.Display, canvas.Kind);
        Assert.Equal("d1", canvas.Id);

        var alt = StageGeometry.HitEditTarget(StageEditMode.Cues, [display], rects, none, (200, 200), 1, preferDisplay: true);
        Assert.Equal(StageHitKind.Display, alt.Kind);

        var chrome = StageGeometry.HitEditTarget(StageEditMode.Cues, [display], rects, none, (40, 8), 1);
        Assert.Equal(StageHitKind.Display, chrome.Kind);

        var selected = new Selection { Kind = SelectionKind.Display, Ids = ["d1"] };
        var handle = StageGeometry.HitEditTarget(StageEditMode.Cues, [display], rects, selected, (1920, 1080), 1);
        Assert.Equal(StageHitKind.DisplayHandle, handle.Kind);
        Assert.Equal("se", handle.Handle);
    }

    [Fact]
    public void SelectedOverlayStaysMovableWhileEditingDisplays()
    {
        var display = Display();
        var bg = new Cue
        {
            Id = "bg",
            Type = CueType.Media,
            AssetId = "a",
            Position = new Vec3(),
            Scale = new Vec2 { X = 100, Y = 100 },
        };
        var ndi = new Cue
        {
            Id = "ndi",
            Type = CueType.Media,
            AssetId = "n",
            Position = new Vec3 { X = 400, Y = 200 },
            Scale = new Vec2 { X = 40, Y = 40 },
        };
        var assets = new List<Asset>
        {
            new() { Id = "a", Width = 1920, Height = 1080 },
            new() { Id = "n", Width = 1920, Height = 1080 },
        };
        var rects = StageGeometry.CueRects([bg, ndi], assets);
        var selected = new Selection { Kind = SelectionKind.Cue, Ids = ["ndi"] };

        var overNdi = StageGeometry.HitEditTarget(StageEditMode.Displays, [display], rects, selected, (500, 300), 1);
        Assert.Equal(StageHitKind.Cue, overNdi.Kind);
        Assert.Equal("ndi", overNdi.Id);

        var corner = StageGeometry.HitEditTarget(StageEditMode.Displays, [display], rects, selected, (400, 200), 1);
        Assert.Equal(StageHitKind.CueHandle, corner.Kind);
        Assert.Equal("nw", corner.Handle);

        var wall = StageGeometry.HitEditTarget(StageEditMode.Displays, [display], rects, selected, (50, 50), 1);
        Assert.Equal(StageHitKind.Display, wall.Kind);
    }
}
