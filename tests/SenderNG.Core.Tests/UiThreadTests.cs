using SenderNG.Avalonia;
using Xunit;

namespace SenderNG.Core.Tests;

public class UiThreadTests
{
    [Fact]
    public void Post_runs_inline_when_no_ui_dispatcher_initialized()
    {
        // Headless/unit-test hosts never call UiThread.Initialize(), so callbacks
        // must run inline on the calling thread (keeps sync assertions working).
        var callerThread = Environment.CurrentManagedThreadId;
        var executedOn = -1;
        var executed = 0;

        UiThread.Post(() =>
        {
            executed++;
            executedOn = Environment.CurrentManagedThreadId;
        });

        Assert.Equal(1, executed);
        Assert.Equal(callerThread, executedOn);
    }
}
