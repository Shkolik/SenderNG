
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
using Microsoft.Extensions.DependencyInjection;

namespace SenderNG.Core.Modules;

/// <summary>
/// Discovers <see cref="IModule"/> implementations and registers them into the
/// application's single DI container. Modules are registered as singletons and each
/// module's own service registrations are applied.
/// </summary>
public static class ModuleCatalog
{
    public static IServiceCollection Scan(IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        var moduleTypes = assemblies
            .Where(a => a is not null)
            .SelectMany(GetModuleTypes)
            .Distinct();

        foreach (var type in moduleTypes)
        {
            if (services.Any(d => d.ServiceType == type))
            {
                continue;
            }

            // Only auto-register modules with a public parameterless constructor;
            // test stubs or modules requiring ctor args are registered manually.
            if (type.GetConstructor(Type.EmptyTypes) is null)
                continue;

            services.AddSingleton(type);
            services.AddSingleton<IModule>(sp => (IModule)sp.GetRequiredService(type));

            var instance = (IModule)Activator.CreateInstance(type)!;
            instance.RegisterServices(services);
        }

        return services;
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
