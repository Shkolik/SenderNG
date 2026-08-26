using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class StatusReportParserTests
{
    private static MachineState NewState() => new(3) { Status = "Idle" };

    [Fact]
    public void Empty_or_null_input_is_ignored()
    {
        Assert.False(StatusReportParser.Parse("", NewState()));
        Assert.False(StatusReportParser.Parse(null!, NewState()));
    }

    [Fact]
    public void Garbage_line_is_ignored()
    {
        var state = NewState();
        Assert.False(StatusReportParser.Parse("this is not a report", state));
        Assert.Equal("Idle", state.Status);
    }

    [Fact]
    public void Status_field_updates_machine_status()
    {
        var state = NewState();
        Assert.True(StatusReportParser.Parse("<Run|WPos:1,2,3>", state));
        Assert.Equal("Run", state.Status);
    }

    [Theory]
    [InlineData("<Idle|WPos:1.0,2.0,5.5>", 0, 1.0)]
    [InlineData("<Idle|WPos:10,20,30>", 1, 20.0)]
    [InlineData("<Idle|WPos:-3.5,0,2>", 2, 2.0)]
    public void WPos_sets_work_positions(string report, int axis, double expected)
    {
        var state = NewState();
        StatusReportParser.Parse(report, state);
        Assert.Equal(expected, state.Positions[axis]);
    }

    [Fact]
    public void MPos_sets_machine_positions()
    {
        var state = NewState();
        StatusReportParser.Parse("<Idle|MPos:4,5,6>", state);
        Assert.Equal([4.0, 5.0, 6.0], state.MachinePositions!);
    }

    [Fact]
    public void FS_sets_feed_and_spindle_speed()
    {
        var state = NewState();
        StatusReportParser.Parse("<Run|FS:120.5,8000|Ov:100,100,100>", state);
        Assert.Equal(120.5, state.Feed);
        Assert.Equal(8000.0, state.Speed);
    }

    [Fact]
    public void Ov_sets_all_three_override_percentages()
    {
        var state = NewState();
        StatusReportParser.Parse("<Run|Ov:90,50,120>", state);
        Assert.Equal(90, state.FeedOvr);
        Assert.Equal(50, state.RapidOvr);
        Assert.Equal(120, state.SpindleOvr);
    }

    [Fact]
    public void Pn_probe_pin_sets_probe_signals()
    {
        var state = NewState();
        StatusReportParser.Parse("<Idle|Pn:P>", state);
        Assert.True((state.Signals & CNC.Core.Signals.Probe) != 0);

        var disconnected = NewState();
        StatusReportParser.Parse("<Idle|Pn:O>", disconnected);
        Assert.True((disconnected.Signals & CNC.Core.Signals.ProbeDisconnected) != 0);
    }

    [Fact]
    public void PRB_report_updates_probe_position_and_success()
    {
        var state = NewState();
        Assert.True(StatusReportParser.Parse("[PRB:1.5,2.5,3.5:1]", state));
        Assert.True(state.ProbeSuccess);
        Assert.Equal([1.5, 2.5, 3.5], state.ProbePosition!);
    }

    [Fact]
    public void PRB_report_with_zero_sets_unsuccessful_probe()
    {
        var state = NewState();
        StatusReportParser.Parse("[PRB:0,0,0:0]", state);
        Assert.False(state.ProbeSuccess);
        Assert.Equal([0.0, 0.0, 0.0], state.ProbePosition!);
    }

    [Fact]
    public void Accessory_field_A_sets_flood_mist_and_spindle()
    {
        var state = NewState();
        StatusReportParser.Parse("<Idle|A:FMS>", state);
        Assert.True(state.Flood);
        Assert.True(state.Mist);
        Assert.Equal(SpindleDirection.Forward, state.SpindleState);
    }

    [Fact]
    public void Accessory_field_M_T_S_sets_mist_toolchange_and_spindle_forward()
    {
        var state = NewState();
        StatusReportParser.Parse("<Idle|A:MTS>", state);
        Assert.True(state.Mist);
        Assert.True(state.IsToolChanging);
        Assert.Equal(SpindleDirection.Forward, state.SpindleState);
    }

    [Fact]
    public void Accessory_field_C_sets_spindle_reverse()
    {
        var state = NewState();
        StatusReportParser.Parse("<Idle|A:C>", state);
        Assert.Equal(SpindleDirection.Reverse, state.SpindleState);
    }

    [Fact]
    public void Accessory_field_empty_clears_flood_mist_and_spindle()
    {
        var state = NewState();
        state.Flood = true;
        state.Mist = true;
        StatusReportParser.Parse("<Idle|A:>", state);
        Assert.False(state.Flood);
        Assert.False(state.Mist);
        Assert.Equal(SpindleDirection.Off, state.SpindleState);
    }

    [Fact]
    public void No_accessory_field_leaves_flood_mist_untouched()
    {
        var state = NewState();
        state.Flood = true;
        state.Mist = true;
        StatusReportParser.Parse("<Idle|WPos:1,2,3>", state);
        Assert.True(state.Flood);
        Assert.True(state.Mist);
    }
}