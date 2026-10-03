using System.Reflection;
using PlantOps.Modules.Assets;
using PlantOps.Modules.Identity;
using PlantOps.Modules.Inventory;
using PlantOps.Modules.WorkOrders;

namespace PlantOps.Api.Tests;

// Note: Assembly.GetReferencedAssemblies() lists only assemblies the IL actually uses, so an unused
// <ProjectReference> will not trip these tests. The compiler keeps `internal` honest; these tests catch
// real code-level dependencies, which is what actually couples modules.
public class ArchitectureTests
{
    private const string Prefix = "PlantOps.Modules.";

    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(AssetsModule).Assembly,
        typeof(WorkOrdersModule).Assembly,
        typeof(InventoryModule).Assembly,
        typeof(IdentityModule).Assembly,
    ];

    private static bool IsModuleImplementation(AssemblyName name) =>
        name.Name is { } n
        && n.StartsWith(Prefix, StringComparison.Ordinal)
        && !n.EndsWith(".Contracts", StringComparison.Ordinal);

    [Fact]
    public void Modules_do_not_reference_other_module_implementations()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            var illegal = assembly.GetReferencedAssemblies()
                .Where(IsModuleImplementation)
                .Where(r => r.Name != assembly.GetName().Name)
                .Select(r => r.Name)
                .ToList();

            Assert.True(
                illegal.Count == 0,
                $"{assembly.GetName().Name} references {string.Join(", ", illegal)}. Modules may only talk to each other " +
                "through '.Contracts' assemblies (ADR-0002); depending on another module's implementation makes it " +
                "impossible to extract or change that module independently. Move the shared type into the other " +
                "module's Contracts project or use an integration event.");
        }
    }

    [Fact]
    public void Contracts_do_not_reference_module_implementations()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            var contracts = Assembly.Load(assembly.GetName().Name + ".Contracts");
            var illegal = contracts.GetReferencedAssemblies()
                .Where(IsModuleImplementation)
                .Select(r => r.Name)
                .ToList();

            Assert.True(
                illegal.Count == 0,
                $"{contracts.GetName().Name} references {string.Join(", ", illegal)}. Contracts are the public " +
                "surface other modules depend on; if they pointed back at an implementation, a consumer would " +
                "transitively depend on internals and could form a dependency cycle.");
        }
    }

    [Fact]
    public void Modules_expose_exactly_one_public_type_named_after_the_module()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            var moduleName = assembly.GetName().Name![Prefix.Length..];
            var publicTypes = assembly.GetExportedTypes().Select(t => t.Name).ToList();

            Assert.True(
                publicTypes.SequenceEqual([$"{moduleName}Module"]),
                $"{assembly.GetName().Name} exposes [{string.Join(", ", publicTypes)}] but must expose only " +
                $"'{moduleName}Module'. Everything else must be internal so other modules cannot reach into this " +
                "one; public contracts belong in the '.Contracts' project (ADR-0002).");
        }
    }

    [Fact]
    public void Building_blocks_do_not_reference_any_module()
    {
        // Building blocks are shared by every module; one that pointed back at a module would make that module
        // special and create a dependency cycle. They are not modules themselves, so the exported-types rule above
        // does not apply to them.
        foreach (var assembly in new[] { typeof(SharedKernel.DomainException).Assembly, typeof(BuildingBlocks.Infrastructure.ICurrentUser).Assembly })
        {
            var illegal = assembly.GetReferencedAssemblies()
                .Where(r => r.Name is { } n && n.StartsWith(Prefix, StringComparison.Ordinal))
                .Select(r => r.Name)
                .ToList();

            Assert.True(
                illegal.Count == 0,
                $"{assembly.GetName().Name} references {string.Join(", ", illegal)}. Building blocks must stay " +
                "module-agnostic: put the abstraction (e.g. ICurrentUser) in the building block and let the module implement it.");
        }
    }

    [Fact]
    public void SharedKernel_stays_free_of_EF_Core_and_ASP_NET()
    {
        var illegal = typeof(SharedKernel.DomainException).Assembly.GetReferencedAssemblies()
            .Where(r => r.Name is { } n && (n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) || n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)))
            .Select(r => r.Name)
            .ToList();

        Assert.True(
            illegal.Count == 0,
            $"SharedKernel references {string.Join(", ", illegal)}. The domain model must stay persistence- and web-free; " +
            "EF-aware code belongs in PlantOps.BuildingBlocks.Infrastructure.");
    }
}
