/*
 * PluginsManifest.cs - 3rd-party plugin manifest parsing and ad-hoc entry derivation
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

using System.Text.Json;

namespace ReleaseManager;

public sealed record PluginEntry(string Id, string Project, string? Version);

public static class PluginsManifest
{
    public static IReadOnlyList<PluginEntry> Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Null)
                return [];

            var plugins = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("plugins", out var pluginsProp)
                ? pluginsProp
                : default;
            if (plugins.ValueKind == JsonValueKind.Undefined)
                return [];
            if (plugins.ValueKind != JsonValueKind.Array)
                throw new JsonException("'plugins' must be an array");

            var result = new List<PluginEntry>();
            foreach (var element in plugins.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                    throw new JsonException("each plugin entry must be a JSON object");
                var id = element.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var project = element.TryGetProperty("project", out var projProp) ? projProp.GetString() : null;
                var version = element.TryGetProperty("version", out var verProp) ? verProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(project))
                    throw new JsonException("each plugin needs a non-empty 'id' and 'project'");
                result.Add(new PluginEntry(id, project, string.IsNullOrWhiteSpace(version) ? null : version));
            }
            return result;
        }
        catch (JsonException ex)
        {
            throw new CliException($"Malformed plugins.json: {ex.Message}");
        }
    }

    public static PluginEntry FromAdHoc(string projectPath)
    {
        var id = Path.GetFileNameWithoutExtension(projectPath);
        return new PluginEntry(id, projectPath, null);
    }
}
