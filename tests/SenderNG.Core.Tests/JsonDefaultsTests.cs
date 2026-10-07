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

using System.Text.Json;
using CNC.Core.Services;
using Xunit;

namespace SenderNG.Core.Tests;

public class JsonDefaultsTests
{
    private sealed class Widget
    {
        public int Size { get; set; }
        public string Name { get; set; } = "";
    }

    private interface IWidget
    {
        int Size { get; }
    }

    [Fact]
    public void TryDeserialize_reads_valid_json()
    {
        var w = JsonDefaults.TryDeserialize<Widget>("{\"Size\":3,\"Name\":\"a\"}");
        Assert.NotNull(w);
        Assert.Equal(3, w!.Size);
        Assert.Equal("a", w.Name);
    }

    [Fact]
    public void TryDeserialize_returns_null_for_malformed_json()
    {
        // The old code caught only JsonException, so a payload that made the
        // serializer throw NotSupportedException or InvalidOperationException
        // escaped and took profile loading down with it.
        Assert.Null(JsonDefaults.TryDeserialize<Widget>("{ not json"));
        Assert.Null(JsonDefaults.TryDeserialize<Widget>(""));
        Assert.Null(JsonDefaults.TryDeserialize<Widget>(null));
    }

    [Fact]
    public void TryDeserialize_returns_null_when_the_type_cannot_be_read()
    {
        // An interface has no deserializable shape: System.Text.Json throws
        // NotSupportedException, not JsonException. This is the widened case
        // the audit §7.3 finding was actually about. It must be a NON-collection
        // interface: STJ materialises well-known collection interfaces
        // (IList<T>, IEnumerable<T>, ...) into List<T>, so those return a value
        // instead of throwing and would make this assertion vacuous.
        Assert.Null(JsonDefaults.TryDeserialize<IWidget>("{\"Size\":3}"));
    }
}
