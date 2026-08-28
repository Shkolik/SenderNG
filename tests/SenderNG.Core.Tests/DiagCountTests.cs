using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using Xunit;

/// <summary>
/// Regression: RegisterModule used to register every built-in module twice under
/// IModule (generic inference from the IModule-typed parameter), doubling sidebar entries.
/// </summary>
public class HostModuleRegistrationTests
{
    [Fact]
    public void BuiltIn_modules_are_registered_exactly_once()
    {
        var host = SenderNG.Avalonia.HostSetup.BuildHost([]);
        var modules = host.Services.GetServices<IModule>().ToList();
        var ids = modules.Select(m => m.Id).OrderBy(i => i).ToList();
        Assert.True(modules.Count == modules.DistinctBy(m => m.Id).Count(),
            $"duplicates ({modules.Count}): {string.Join(",", ids)}");
        // 14 built-in + Dummy plugin discovered via ModuleCatalog assembly scan.
        Assert.Equal(15, modules.Count);
    }
}
