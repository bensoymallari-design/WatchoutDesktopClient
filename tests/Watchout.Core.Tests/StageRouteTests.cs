using Watchout.Core;
using Watchout.Core.Media;
using Watchout.Core.Stage;
using Xunit;

namespace Watchout.Core.Tests;

public class StageRouteTests
{
    [Fact]
    public void ColumnsFollowStageLeftToRight()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.AddDisplay();
        var left = session.Show!.Displays[0];
        var right = session.Show.Displays[1];
        right.X = left.X + left.Width;
        var columns = StageRoute.Columns(session.Show);
        Assert.Equal(2, columns.Count);
        Assert.Equal(left.Id, columns[0].Id);
        Assert.Equal(right.Id, columns[1].Id);
        Assert.Equal(left.Name, StageRoute.ColumnLabel(left, 0));
        Assert.Equal($"{right.Width:0}×{right.Height:0}", StageRoute.ColumnHint(right));
    }

    [Fact]
    public void AssignNdiFitsTheChosenStageColumn()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.AddDisplay();
        var left = session.Show!.Displays[0];
        var right = session.Show.Displays[1];
        right.X = 1920;
        right.Width = 1920;
        right.Height = 1080;

        session.AssignNdiToDisplay("SHOW-PC (Resolume)", right.Id);

        var asset = LiveSources.NdiAssetOnShow(session.Show, "SHOW-PC (Resolume)");
        Assert.NotNull(asset);
        var cue = LiveSources.NdiCue(session.Show, "SHOW-PC (Resolume)");
        Assert.NotNull(cue);
        Assert.Equal(right.X, cue!.Position.X);
        Assert.Equal(right.Y, cue.Position.Y);
        Assert.Equal(right.Id, LiveSources.NdiDisplayKey(session.Show, "SHOW-PC (Resolume)"));
        Assert.Equal(right.Id, StageRoute.AssignedDisplayId(session.Show, StageRoute.Ndi, "SHOW-PC (Resolume)"));
        var rect = StageGeometry.CueRect(cue, asset);
        Assert.Equal(right.Width, rect.W);
        Assert.Equal(right.Height, rect.H);

        session.AssignNdiToDisplay("SHOW-PC (Resolume)", left.Id);
        cue = LiveSources.NdiCue(session.Show, "SHOW-PC (Resolume)");
        Assert.Equal(left.X, cue!.Position.X);
        Assert.Equal(left.Id, LiveSources.NdiDisplayKey(session.Show, "SHOW-PC (Resolume)"));
    }

    [Fact]
    public void RowsAreCaptureThenNdiIncoming()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.AddDisplay();
        var left = session.Show!.Displays[0];
        session.AssignCaptureToDisplay("elgato-1", left.Id, "Capture 1");
        session.AssignNdiToDisplay("Arena", session.Show.Displays[1].Id);

        var rows = StageRoute.Rows(session.Show,
        [
            new StageRoute.Incoming(StageRoute.Capture, "elgato-1", "Capture 1"),
            new StageRoute.Incoming(StageRoute.Capture, "elgato-2", "Capture 2"),
            new StageRoute.Incoming(StageRoute.Ndi, "Arena", "Arena"),
        ]);

        Assert.Equal(3, rows.Count);
        Assert.Equal("elgato-1", rows[0].Key);
        Assert.Equal(left.Id, rows[0].DisplayId);
        Assert.Equal("elgato-2", rows[1].Key);
        Assert.Null(rows[1].DisplayId);
        Assert.Equal(StageRoute.Ndi, rows[2].Kind);
        Assert.Equal(session.Show.Displays[1].Id, rows[2].DisplayId);
        Assert.DoesNotContain(rows, r => r.Key == "WatchMe-CAPTURE");
    }

    [Fact]
    public void SwitchingCaptureAutoFitsTheNewColumn()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.AddDisplay();
        var left = session.Show!.Displays[0];
        var right = session.Show.Displays[1];
        right.X = 3840;
        session.AssignCaptureToDisplay("card-a", left.Id, "Card A");
        session.AssignCaptureToDisplay("card-a", right.Id, "Card A");
        var cue = LiveSources.CaptureCue(session.Show, "card-a");
        Assert.Equal(right.X, cue!.Position.X);
        Assert.Equal(right.Id, LiveSources.CaptureDisplayKey(session.Show, "card-a"));
    }
}
