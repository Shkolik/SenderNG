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

using System.Text;

namespace CNC.Core.Services;

/// <summary>
/// Accumulates raw serial bytes into <c>'\n'</c>-terminated lines. Split out of
/// <see cref="SerialGrblTransport"/> so the framing rules can be tested without
/// a physical COM port — the transport is the only thing that talks to
/// <c>System.IO.Ports</c>, and nothing about framing needs it.
/// </summary>
public sealed class SerialLineBuffer
{
    /// <summary>Largest partial line held before it is treated as framing garbage.</summary>
    public const int MaxLineLength = 4096;

    private readonly StringBuilder _line = new();
    private bool _resyncing;

    /// <summary>Bytes currently buffered, terminator included.</summary>
    public int Length => _line.Length;

    /// <summary>Add a chunk of freshly read bytes.</summary>
    public void Append(string chunk)
    {
        if (_resyncing)
        {
            // Inside a discarded run: drop everything up to and including the
            // terminator that ends it, and keep whatever followed.
            var nl = chunk.IndexOf('\n');
            if (nl < 0)
                return;
            _resyncing = false;
            chunk = chunk[(nl + 1)..];
        }

        _line.Append(chunk);

        if (_line.Length < MaxLineLength)
            return;

        // A partial line this long is framing garbage — electrical noise or a
        // firmware debug dump. Drop it and resync on the next '\n'. Buffering
        // it instead is unbounded, and refusing to read at all is the wedge
        // this replaced: the reader stopped calling ReadExisting, so no
        // terminator could ever arrive and every later ok/error/[PRB:] line
        // was dropped for the life of the connection.
        if (_line.ToString().LastIndexOf('\n') < 0)
        {
            _line.Clear();
            _resyncing = true;
        }
    }

    public void Clear() => _line.Clear();

    /// <summary>Extract the first complete '\n'-terminated line, keeping any remainder buffered.</summary>
    public bool TryTakeLine(out string line)
    {
        line = string.Empty;
        var s = _line.ToString();
        var idx = s.IndexOf('\n');
        if (idx < 0)
            return false;

        line = s[..idx].TrimEnd('\r');
        _line.Clear();
        if (idx + 1 < s.Length)
            _line.Append(s[(idx + 1)..]); // more data arrived with the terminator
        return true;
    }
}
