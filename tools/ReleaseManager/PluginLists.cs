/*
 * PluginLists.cs - plugin list loading, validation and selection resolution
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

public static class PluginLists
{
    public static IReadOnlyList<PluginEntry> Resolve(
        string repoRoot,
        ReleaseOptions options,
        ILog log,
        ReleasePlatform? platformOverride = null)
    {
        var platform = platformOverride ?? PlatformInfo.Current;
        var manifestPath = Path.Combine(repoRoot, "tools", "ReleaseManager", "plugins.json");
        var thirdParty = new List<PluginEntry>();
        if (File.Exists(manifestPath))
        {
            thirdParty.AddRange(PluginsManifest.Parse(File.ReadAllText(manifestPath)));
        }
        else
        {
            log.Detail($"no plugins.json at {manifestPath}; treating as empty");
        }

        foreach (var projectPath in options.AdHocPluginPaths)
        {
            thirdParty.Add(PluginsManifest.FromAdHoc(projectPath));
        }

        var all = FirstPartyPlugins.All.Concat(thirdParty).ToList();
        Validate(all);

        switch (options.Selection)
        {
            case PluginSelection.None:
                return [];
            case PluginSelection.Subset:
                var byId = all.ToDictionary(p => p.Id);
                foreach (var id in options.PluginIds)
                {
                    if (!byId.ContainsKey(id))
                    {
                        throw new CliException(
                            $"Unknown plugin id '{id}'. Known ids: {string.Join(", ", all.Select(p => p.Id))}.");
                    }
                }
                return all.Where(p => options.PluginIds.Contains(p.Id)).ToList();
            default:
                return all;
        }
    }

    private static void Validate(IReadOnlyList<PluginEntry> entries)
    {
        var firstPartyIds = FirstPartyPlugins.All.Select(p => p.Id).ToHashSet();
        var seen = new HashSet<string>();
        foreach (var entry in entries)
        {
            var isFirstParty = firstPartyIds.Contains(entry.Id);
            if (isFirstParty && seen.Contains(entry.Id))
            {
                throw new CliException($"Plugin id collision: '{entry.Id}' is both 1st-party and declared in plugins.json.");
            }
            if (!seen.Add(entry.Id))
            {
                throw new CliException($"Duplicate plugin id in the release set: '{entry.Id}'.");
            }
            if (!isFirstParty && !File.Exists(entry.Project.Replace('/', Path.DirectorySeparatorChar)))
            {
                throw new CliException($"Plugin project not found: '{entry.Project}'.");
            }
        }
    }
}
