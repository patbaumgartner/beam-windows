using Beam.Core.Display;

namespace Beam.App.Tests;

public class ExtendPresentationTests
{
    [Fact]
    public async Task Extend_DpiCaptureApplyStatusAndRestore_AllTargetExternalSource()
    {
        using var h = new PresentationHarness(
            Topologies.MirroredWithMismatch(), // internal source 0 is deliberately first
            DpiScale.From(-4, 0, 4));
        h.Manager.ProjectorTopology = PresentationTopology.Extend;
        h.Mode.OnApply = () => h.Topology.Topology = new DisplayTopology(
            new[]
            {
                Topologies.Path(0, 1, "Laptop", isInternal: true, active: true, 3840, 2400, 3840, 2400),
                Topologies.Path(1, 2, "DELL", isInternal: false, active: true, 1920, 1080, 1920, 1080),
            },
            DisplayTopologyId.Extend);

        await h.Manager.ApplyNowAsync();

        Assert.Equal(1, h.Mode.ExtendCalls);
        Assert.Equal(1u, h.Dpi.LastReadPath?.SourceId);
        Assert.Equal(1u, h.Dpi.LastSetPath?.SourceId);
        Assert.False(h.Dpi.LastSetPath!.Target.IsInternal);
        Assert.Equal(1u, Assert.Single(h.Journal.LastDpi!).SourceId);
        Assert.Equal("1920x1080@60", h.Manager.CurrentStatus.AppliedSourceMode);
        Assert.Equal(new[] { 100 }, h.Dpi.RequestedPercents);

        h.Dpi.RequestedPercents.Clear();
        await h.Manager.RestoreNowAsync();

        Assert.Equal(new[] { 200 }, h.Dpi.RequestedPercents);
        Assert.Equal(1u, h.Dpi.LastSetPath?.SourceId);
    }
}
