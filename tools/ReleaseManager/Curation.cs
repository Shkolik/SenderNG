/*
 * Curation.cs - bundle-copy rules for excluded files and host-owned assemblies
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

public static class Curation
{
    public static bool IsExcluded(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);

        if (normalized == "global.json")
            return true;
        if (normalized == "profiles" || normalized.StartsWith("profiles/"))
            return true;
        if (fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.StartsWith("Avalonia.DesignerSupport", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.StartsWith("Avalonia.Remote.Protocol", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.StartsWith("AvaloniaUI.DiagnosticsSupport", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static bool IsHostOwned(string fileName)
    {
        if (IsSdkAssembly(fileName) ||
            fileName == "SenderNG.Core.dll" ||
            fileName == "CNC.Core.dll")
            return true;
        return fileName.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSdkAssembly(string fileName)
    {
        if (fileName == "SenderNG.Modules.Common.dll" ||
            fileName == "SenderNG.Modules.Common.Render.dll")
            return true;
        return fileName.StartsWith("Silk.NET.", StringComparison.OrdinalIgnoreCase) &&
               fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    }

    public static void CopyBundle(string publishDir, string bundleDir, ILog log)
    {
        foreach (var source in Directory.EnumerateFiles(publishDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(publishDir, source);
            if (IsExcluded(relative))
            {
                log.Detail($"excluded: {relative}");
                continue;
            }

            var destination = Path.Combine(bundleDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }

    public static void StripModulesToSdk(string modulesDir)
    {
        if (!Directory.Exists(modulesDir))
            return;

        foreach (var child in Directory.EnumerateFileSystemEntries(modulesDir))
        {
            var name = Path.GetFileName(child);
            if (Directory.Exists(child))
            {
                if (name != "SenderNG.Modules.Common")
                {
                    Directory.Delete(child, recursive: true);
                }
            }
            else if (!IsSdkAssembly(name))
            {
                File.Delete(child);
            }
        }
    }
}
