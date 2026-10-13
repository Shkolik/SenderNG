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

namespace CNC.Core.Services;

/// <summary>Outcome of a restore: how many files landed, and which entries failed.</summary>
public sealed record AppSettingsRestoreResult(int Written, IReadOnlyList<string> Failed)
{
    /// <summary>True when every file in the bundle was written.</summary>
    public bool Ok => Failed.Count == 0;
}

/// <summary>
/// Writes a validated <see cref="AppSettingsBundle"/> into the settings directory.
/// Overwrite-only: files that exist on disk but are absent from the archive are left
/// exactly as they are (manual unzip-and-copy parity — extra profiles survive), and
/// every file is written as <c>&lt;path&gt;.tmp</c> then moved over the target, so a
/// crash or a full disk cannot leave a truncated JSON file behind.
/// <para>
/// "Validate before writing anything" is structural, not a caller obligation:
/// <see cref="Apply"/> validates the bundle itself and refuses an invalid one without
/// touching the disk, so a caller that skipped
/// <see cref="AppSettingsBundleValidator.Validate"/> cannot half-write an archive.
/// </para>
/// </summary>
public static class AppSettingsRestore
{
    /// <summary>Writes a validated bundle into <paramref name="settingsDir"/>. Never deletes bystander files.</summary>
    public static AppSettingsRestoreResult Apply(string settingsDir, AppSettingsBundle bundle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDir);
        ArgumentNullException.ThrowIfNull(bundle);

        // Structural guarantee: an archive that does not validate is never written,
        // in whole or in part. The reported failures are the validation errors.
        var validation = AppSettingsBundleValidator.Validate(bundle);
        if (!validation.Ok)
            return new AppSettingsRestoreResult(0, validation.Errors);

        var root = Path.GetFullPath(settingsDir);
        var written = 0;
        var failed = new List<string>();

        // Deterministic order (global.json first): the report and any partial
        // failure are reproducible.
        foreach (var pair in bundle.Files.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var target = TargetPath(root, pair.Key);
            if (target is null)
            {
                // Never write outside the settings directory, even for a hand-built bundle.
                failed.Add(pair.Key);
                continue;
            }

            var temp = target + ".tmp";
            try
            {
                var parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                File.WriteAllText(temp, pair.Value);
                File.Move(temp, target, overwrite: true);
                written++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                          or ArgumentException or NotSupportedException)
            {
                TryDelete(temp);
                failed.Add(pair.Key);
            }
        }

        return new AppSettingsRestoreResult(written, failed);
    }

    /// <summary>Absolute target inside <paramref name="root"/>, or null when the entry escapes it.</summary>
    private static string? TargetPath(string root, string key)
    {
        if (string.IsNullOrEmpty(key) || key.Contains('\\') || key.Contains("..", StringComparison.Ordinal))
            return null;
        if (Path.IsPathRooted(key) || key.Contains(':'))
            return null;
        var target = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(root, target);
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
            return null;
        return target;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort: a leftover .tmp is never loaded by the settings stores.
        }
    }
}
