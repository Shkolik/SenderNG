
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

using System.Reflection;
using CNC.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace SenderNG.Core.Modules;

/// <summary>
/// Discovers <see cref="IModule"/> implementations and registers them into the
/// application's single DI container. Modules are registered as singletons and each
/// module's own service registrations are applied.
/// </summary>
public static class ModuleCatalog
{
    /// <summary>
    /// Scans every <c>*.dll</c> in <paramref name="directory"/> for modules
    /// (the plugin drop-in path: OutputDir/Modules). A missing directory is a
    /// no-op; a broken or non-module DLL is skipped, never fatal. Assemblies
    /// are loaded into the LoadFrom context — plugin assemblies must not be
    /// referenced by the app at compile time or their types would be duplicated.
    /// Id collisions (two modules claiming the same id) are resolved first-wins
    /// across the whole directory; the rejected module never registers.
    /// </summary>
    public static IServiceCollection ScanDirectory(IServiceCollection services, string directory)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!Directory.Exists(directory))
            return services;

        // The drop-in folder keeps each plugin flat (Modules\Foo.dll) while its
        // package closure and locale live in Modules\Foo\ — scanning stays a
        // cheap FLAT *.dll pass (no native binaries / no third-party assemblies
        // to trip GetTypes), and adding/removing a plugin is one dll + one
        // folder. The LoadFrom'd main DLL does not natively probe subfolders for
        // its managed dependencies, so an AssemblyResolve probe serves them from
        // the plugin folder. Registered for the process lifetime: resolving the
        // module types during the scan is only the first touch — the closure
        // assemblies' own dependencies get JIT-resolved later (e.g. a plugin's
        // helper assembly touching a third-party library), and must find the same folder.
        // Native binaries are not assemblies; the managed DLLs that P/Invoke them are
        // LoadFrom'd from the same folder, whose own directory the loader probes
        // for unmanaged libraries.
        Interlocked.Exchange(ref _pluginFolder, directory);
        if (Interlocked.Exchange(ref _pluginFolderProbeRegistered, 1) == 0)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
                _pluginFolder.Length == 0 ? null : TryResolvePluginFolderDependency(_pluginFolder, e.Name);
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dll in Directory.GetFiles(directory, "*.dll"))
        {
            try
            {
                Scan(services, seenIds, Assembly.LoadFrom(dll));
            }
            catch (Exception)
            {
                // Broken plugin — skip and keep going.
            }
        }

        return services;
    }

    /// <summary>Serves a managed dependency from the plugin's own folder
    /// (<c>Modules\&lt;id&gt;\&lt;shortname&gt;.dll</c>) when the normal load
    /// path cannot resolve it. Returns null so resolution can continue.</summary>
    private static Assembly? TryResolvePluginFolderDependency(string rootDir, string assemblyFullName)
    {
        var shortName = new AssemblyName(assemblyFullName).Name ?? assemblyFullName;
        foreach (var folder in Directory.GetDirectories(rootDir))
        {
            var candidate = Path.Combine(folder, shortName + ".dll");
            if (File.Exists(candidate))
                return Assembly.LoadFrom(candidate);
        }
        return null;
    }

    private static string _pluginFolder = "";
    private static int _pluginFolderProbeRegistered;

    public static IServiceCollection Scan(IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in assemblies.Where(a => a is not null))
            Scan(services, seenIds, assembly);

        return services;
    }

    private static void Scan(IServiceCollection services, HashSet<string> seenIds, Assembly assembly)
    {
        foreach (var type in GetModuleTypes(assembly))
        {
            if (services.Any(d => d.ServiceType == type))
            {
                continue;
            }

            // Only auto-register modules with a public parameterless constructor;
            // test stubs or modules requiring ctor args are registered manually.
            if (type.GetConstructor(Type.EmptyTypes) is null)
                continue;

            var instance = (IModule)Activator.CreateInstance(type)!;

            // Id collision: first wins, second rejected.
            if (!seenIds.Add(instance.Id))
                continue;

            services.AddSingleton(type);
            services.AddSingleton<IModule>(sp => (IModule)sp.GetRequiredService(type));

            // Per-module error reporter keyed by the module's Id: a plugin
            // resolves its own reporter via
            // GetRequiredKeyedService&lt;IErrorReporter&gt;(Id). The implementation
            // comes lazily from the host's IErrorReporterFactory, bound to this
            // instance so the common handler knows which name to prefix.
            services.AddKeyedSingleton<IErrorReporter>(instance.Id,
                (sp, _) => sp.GetRequiredService<IErrorReporterFactory>().Create(instance));

            instance.RegisterServices(services);
        }
    }

    private static IEnumerable<Type> GetModuleTypes(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }

        return types.Where(t =>
            t.IsClass &&
            !t.IsAbstract &&
            !t.IsGenericTypeDefinition &&
            typeof(IModule).IsAssignableFrom(t));
    }
}
