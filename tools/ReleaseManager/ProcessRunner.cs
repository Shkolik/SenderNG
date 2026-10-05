/*
 * ProcessRunner.cs - spawn dotnet processes and capture output
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

using System.Diagnostics;

namespace ReleaseManager;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string workingDirectory, string fileName, CancellationToken ct, params string[] arguments);
}

public sealed record ProcessResult(int ExitCode, IReadOnlyList<string> StdOut, IReadOnlyList<string> StdErr)
{
    public bool Succeeded => ExitCode == 0;
}

public sealed class ProcessRunner : IProcessRunner
{
    internal bool LastProcessExited { get; private set; }

    public async Task<ProcessResult> RunAsync(string workingDirectory, string fileName, CancellationToken ct, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)
            ?? throw new StagingException($"Failed to start process: {fileName}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* the process already exited */ }
            process.WaitForExit();
            LastProcessExited = process.HasExited;
            try { await Task.WhenAll(stdoutTask, stderrTask); }
            catch { /* pipes closed with the process */ }
            throw;
        }

        LastProcessExited = process.HasExited;
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new ProcessResult(
            process.ExitCode,
            SplitLines(stdout),
            SplitLines(stderr));
    }

    private static string[] SplitLines(string text)
        => text.TrimEnd('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
