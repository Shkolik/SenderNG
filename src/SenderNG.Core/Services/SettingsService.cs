
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

namespace CNC.Core.Services;

public class SettingsService
{
    private readonly string _dir;
    private AppSettings? _current;

    /// <summary>Raised at the end of every Save so UI can re-read live visibility/mode state.</summary>
    public event System.Action? Changed;

    public SettingsService(string? dir = null)
    {
        _dir = dir ?? DefaultDir();
    }

    /// <summary>Writable directory used by this service (companion files live here too).</summary>
    public string SettingsDir => _dir;

    // Writable base dir for all persisted app files (settings, demo machine data).
    public static string DefaultDir() =>
        IsWritable(AppContext.BaseDirectory)
            ? AppContext.BaseDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SenderNG");

    // All consumers share one AppSettings graph: the first Load() reads disk,
    // later calls return the same instance so per-module Saves never resurrect
    // stale snapshots and clobber sections written by other modules.
    public AppSettings Load()
    {
        if (_current is not null)
            return _current;

        var p = Path.Combine(_dir, "settings.json");
        _current = File.Exists(p)
            ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(p)) ?? new AppSettings()
            : new AppSettings();

        // Legacy flat settings.json (no per-mode sections): seed Mill with the
        // user's existing values and give Foam fresh defaults.
        if (_current.Modes.Count == 0)
        {
            _current.Modes[AppSettings.MillMode] = new ModeSettings
            {
                DemoAxes = _current.DemoAxes,
                Port = _current.Port,
                BaudRate = _current.BaudRate,
                ModulesVisibility = _current.ModulesVisibility,
            };
            _current.Modes[AppSettings.FoamMode] = new ModeSettings();
        }

        return _current;
    }

    public void Save(AppSettings s)
    {
        _current = s ?? throw new ArgumentNullException(nameof(s));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        Changed?.Invoke();
    }

    static bool IsWritable(string d)
    {
        try
        {
            var f = Path.Combine(d, ".w");
            File.WriteAllText(f, "");
            File.Delete(f);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
