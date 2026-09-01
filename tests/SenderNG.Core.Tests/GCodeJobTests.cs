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

using CNC.Core;
using CNC.GCode;
using System.IO;
using Xunit;

namespace SenderNG.Core.Tests;

public class GCodeJobTests
{
    private static string WriteFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "sng-gcodejob-" + Guid.NewGuid().ToString("N") + ".nc");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Without_handler_load_aborts_on_first_parse_error()
    {
        var path = WriteFile("G21\nG5 X10\nG1 X20 F100\n");
        try
        {
            var job = new GCodeJob();
            job.Parser.Dialect = Dialect.Grbl;

            Assert.False(job.LoadFile(path));
            Assert.False(job.Loaded); // abort clears the rows

            var clean = WriteFile("G21\nG1 X20 F100\n");
            try
            {
                Assert.True(job.LoadFile(clean)); // job is reusable after an aborted load
            }
            finally
            {
                File.Delete(clean);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Handler_can_skip_the_offending_line_and_continue()
    {
        var path = WriteFile("G21\nG5 X10\nG1 X20 F100\n");
        try
        {
            var job = new GCodeJob();
            job.Parser.Dialect = Dialect.Grbl;

            var errors = new List<ParseErrorInfo>();
            Assert.True(job.LoadFile(path, error => { errors.Add(error); return true; }));

            var row = Assert.Single(errors);
            Assert.Equal(2, row.LineNumber); // the block that would have been row #2
            Assert.Equal("G5 X10", row.Block);
            Assert.False(string.IsNullOrEmpty(row.Message));

            Assert.Equal(2, job.Data.Rows.Count); // bad line skipped, both good lines kept
            Assert.Equal("G21", job.Data.Rows[0]["Data"]);
            Assert.Equal("G1X20F100", job.Data.Rows[1]["Data"]); // rows store the parser-normalized block (ioSender parity)
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Handler_can_abort_the_load()
    {
        var path = WriteFile("G21\nG5 X10\nG1 X20 F100\n");
        try
        {
            var job = new GCodeJob();
            job.Parser.Dialect = Dialect.Grbl;

            var errors = new List<ParseErrorInfo>();
            Assert.False(job.LoadFile(path, error => { errors.Add(error); return false; }));

            Assert.Single(errors);
            Assert.False(job.Loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Clean_file_never_invokes_handler_and_computes_limits()
    {
        var path = WriteFile("G21 G90\nG0 X10 Y20\nG1 X30 Y40 F100\n");
        try
        {
            var job = new GCodeJob();

            var called = false;
            Assert.True(job.LoadFile(path, error => { called = true; return true; }));

            Assert.False(called);
            Assert.Equal(3, job.Data.Rows.Count);
            Assert.Equal(10, job.BoundingBox.Min[0]);
            Assert.Equal(30, job.BoundingBox.Max[0]);
            Assert.Equal(20, job.BoundingBox.Min[1]);
            Assert.Equal(40, job.BoundingBox.Max[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}