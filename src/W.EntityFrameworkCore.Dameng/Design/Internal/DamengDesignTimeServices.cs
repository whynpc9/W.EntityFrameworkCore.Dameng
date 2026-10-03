using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using W.EntityFrameworkCore.Dameng.Scaffolding.Internal;

[assembly: DesignTimeProviderServices(
    "W.EntityFrameworkCore.Dameng.Design.Internal.DamengDesignTimeServices")]

namespace W.EntityFrameworkCore.Dameng.Design.Internal;

/// <summary>
/// Registers the services used by Entity Framework Core tools for the Dameng provider.
/// </summary>
public sealed class DamengDesignTimeServices : IDesignTimeServices
{
    /// <inheritdoc />
    public void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.AddEntityFrameworkDameng();

        new EntityFrameworkRelationalDesignServicesBuilder(serviceCollection)
            .TryAdd<IAnnotationCodeGenerator, DamengAnnotationCodeGenerator>()
            .TryAdd<IProviderConfigurationCodeGenerator, DamengCodeGenerator>()
            .TryAdd<IDatabaseModelFactory, DamengDatabaseModelFactory>()
            .TryAddCoreServices();
    }
}
