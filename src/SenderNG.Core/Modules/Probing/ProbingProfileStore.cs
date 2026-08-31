
/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

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

namespace SenderNG.Core.Modules.Probing;

/// <summary>
/// Loads/saves the named probing profile collection to its own JSON file
/// (<c>probing.json</c>), mirroring how ioSender keeps profiles in a separate
/// <c>ProbingProfiles.xml</c> rather than inside the main settings.
/// </summary>
public class ProbingProfileStore
{
    private readonly string _filePath;

    public ProbingProfileStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(SettingsService.DefaultDir(), "probing.json");
    }

    /// <summary>Path of the profiles file (the value persisted in the module settings).</summary>
    public string FilePath => _filePath;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Loads the profiles; missing/unreadable file yields ioSender's &lt;Default&gt; profile.</summary>
    public ProbingProfiles Load()
    {
        var profiles = new ProbingProfiles();
        try
        {
            if (File.Exists(_filePath))
            {
                var list = JsonSerializer.Deserialize<List<ProbingProfile>>(File.ReadAllText(_filePath));
                if (list is not null && list.Count > 0)
                {
                    foreach (var p in list)
                        profiles.Profiles.Add(p);
                }
            }
        }
        catch
        {
            // Unreadable/corrupt profiles fall back to defaults rather than crashing.
        }
        profiles.EnsureDefault();
        return profiles;
    }

    /// <summary>Saves the profiles; best-effort (a read-only location must not crash the UI).</summary>
    public bool Save(ProbingProfiles profiles)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(profiles.Profiles, JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
