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
