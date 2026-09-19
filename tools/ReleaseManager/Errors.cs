/*
 * Errors.cs - typed exceptions carrying process exit codes
 */

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

namespace ReleaseManager;

public class ReleaseManagerException : Exception
{
    public int ExitCode { get; }

    public ReleaseManagerException(string message) : base(message)
    {
        ExitCode = 1;
    }

    protected ReleaseManagerException(string message, int exitCode) : base(message)
    {
        ExitCode = exitCode;
    }
}

public sealed class CliException : ReleaseManagerException
{
    public CliException(string message) : base(message) { }
}

public sealed class BuildException : ReleaseManagerException
{
    public BuildException(string message) : base(message, 2) { }
}

public sealed class StagingException : ReleaseManagerException
{
    public StagingException(string message) : base(message, 3) { }
}

public sealed class UnsupportedPlatformException : ReleaseManagerException
{
    public UnsupportedPlatformException(string platform)
        : base($"Unsupported host OS: {platform}. ReleaseManager builds win64 on Windows and linux on Linux.", 4) { }
}
