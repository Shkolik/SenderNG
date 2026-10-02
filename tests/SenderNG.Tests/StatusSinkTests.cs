
/*

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

· Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using SenderNG.Services;
using Xunit;

namespace SenderNG.Tests;

public class StatusSinkTests
{
    [Fact]
    public void A_message_set_before_any_sink_is_replayed_on_attach()
    {
        // Settings can load before MainViewModel constructs (HostSetup registers
        // Load() as a singleton factory), and today that message is lost.
        var sink = new StatusSink();
        sink.Set("settings were reset");

        var received = new List<string>();
        sink.Sink = received.Add;

        Assert.Equal(new[] { "settings were reset" }, received);
    }

    [Fact]
    public void Only_the_last_pending_message_is_delivered()
    {
        var sink = new StatusSink();
        sink.Set("first");
        sink.Set("second");
        sink.Set("third");

        var received = new List<string>();
        sink.Sink = received.Add;

        Assert.Equal(new[] { "third" }, received);
    }

    [Fact]
    public void A_pending_message_is_delivered_exactly_once()
    {
        var sink = new StatusSink();
        sink.Set("once");

        var received = new List<string>();
        sink.Sink = received.Add;

        Assert.Single(received);
    }

    [Fact]
    public void Messages_after_attach_go_straight_through()
    {
        var sink = new StatusSink();
        var received = new List<string>();
        sink.Sink = received.Add;

        sink.Set("live");

        Assert.Equal(new[] { "live" }, received);
    }

[Fact]
    public void A_sink_that_reenters_Set_during_replay_does_not_replay_twice()
    {
        var sink = new StatusSink();
        sink.Set("pending");

        var received = new List<string>();
        sink.Sink = message =>
        {
            received.Add(message);
            if (message == "pending")
            {
                sink.Set("from sink");
            }
        };

        Assert.Single(received, m => m == "pending");
        Assert.Equal(new[] { "pending", "from sink" }, received);
    }

    [Fact]
    public void Set_with_no_sink_ever_attached_is_harmless()
    {
        var sink = new StatusSink();

        sink.Set("nobody home");   // must not throw

        Assert.Null(sink.Sink);
    }
}