using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using W.EntityFrameworkCore.Dameng.Design.Internal;
using W.EntityFrameworkCore.Dameng.Scaffolding.Internal;
using Xunit;

#pragma warning disable EF1001 // Tests intentionally exercise EF/provider infrastructure contracts.

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengDesignTimeTests
{
    [Fact]
    public void ProviderAssemblyExposesDesignTimeServices()
    {
        var providerAssembly = typeof(DamengDbContextOptionsBuilderExtensions).Assembly;
        var attribute = providerAssembly.GetCustomAttribute<DesignTimeProviderServicesAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(typeof(DamengDesignTimeServices).FullName, attribute.TypeName);
        Assert.Equal(typeof(DamengDesignTimeServices), providerAssembly.GetType(attribute.TypeName));
    }

    [Fact]
    public void DesignTimeServicesRegisterProviderGenerators()
    {
        var services = new ServiceCollection();

        new DamengDesignTimeServices().ConfigureDesignTimeServices(services);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IAnnotationCodeGenerator)
                && descriptor.ImplementationType == typeof(DamengAnnotationCodeGenerator));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IProviderConfigurationCodeGenerator)
                && descriptor.ImplementationType == typeof(DamengCodeGenerator));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IDatabaseModelFactory));
    }

    [Fact]
    public void ProviderCodeGeneratorEmitsUseDameng()
    {
        var generator = new DamengCodeGenerator(new ProviderCodeGeneratorDependencies([]));

        var result = generator.GenerateUseProvider(
            "Server=database.example;Port=5236;User Id=app",
            providerOptions: null);

        Assert.Equal(nameof(DamengDbContextOptionsBuilderExtensions.UseDameng), result.Method);
        Assert.Equal(nameof(DamengDbContextOptionsBuilderExtensions), result.DeclaringType);
        Assert.Equal(
            "Server=database.example;Port=5236;User Id=app",
            Assert.Single(result.Arguments));
    }

    [Fact]
    public void AnnotationGeneratorEmitsPublicIdentityApis()
    {
        using var context = CreateContext();
        var generator = CreateAnnotationGenerator(context);
        var model = context.Model;
        var modelAnnotations = model.GetAnnotations().ToDictionary(annotation => annotation.Name);

        var modelCall = Assert.Single(generator.GenerateFluentApiCalls(model, modelAnnotations));

        Assert.Equal(
            nameof(DamengModelBuilderExtensions.UseDamengIdentityColumns),
            modelCall.Method);
        Assert.Collection(
            modelCall.Arguments,
            seed => Assert.Equal(5L, seed),
            increment => Assert.Equal(10, increment));

        var identityProperty = model.FindEntityType(typeof(IdentityEntity))!
            .FindProperty(nameof(IdentityEntity.Id))!;
        var propertyAnnotations = identityProperty.GetAnnotations().ToDictionary(annotation => annotation.Name);
        var propertyCall = Assert.Single(
            generator.GenerateFluentApiCalls(identityProperty, propertyAnnotations));

        Assert.Equal(
            nameof(DamengPropertyBuilderExtensions.UseDamengIdentityColumn),
            propertyCall.Method);
        Assert.Collection(
            propertyCall.Arguments,
            seed => Assert.Equal(7L, seed),
            increment => Assert.Equal(3, increment));
    }

    [Fact]
    public void AnnotationGeneratorEmitsPublicSequenceApi()
    {
        using var context = CreateContext();
        var generator = CreateAnnotationGenerator(context);
        var property = context.Model.FindEntityType(typeof(SequenceEntity))!
            .FindProperty(nameof(SequenceEntity.Id))!;
        var annotations = property.GetAnnotations().ToDictionary(annotation => annotation.Name);

        Assert.DoesNotContain(RelationalAnnotationNames.DefaultValueSql, annotations);
        var call = Assert.Single(generator.GenerateFluentApiCalls(property, annotations));

        Assert.Equal(
            nameof(DamengPropertyBuilderExtensions.UseDamengSequence),
            call.Method);
        Assert.Collection(
            call.Arguments,
            name => Assert.Equal("SequenceEntityNumbers", name),
            schema => Assert.Equal("SA", schema));
        Assert.DoesNotContain("Dameng:ValueGenerationStrategy", annotations);
        Assert.DoesNotContain("Dameng:SequenceName", annotations);
        Assert.DoesNotContain("Dameng:SequenceSchema", annotations);
    }

    private static DesignTimeContext CreateContext()
        => new(
            new DbContextOptionsBuilder<DesignTimeContext>()
                .UseDameng("Server=localhost;Port=5236;User Id=design-time")
                .Options);

    private static DamengAnnotationCodeGenerator CreateAnnotationGenerator(DbContext context)
        => new(
            new AnnotationCodeGeneratorDependencies(
                context.GetService<IRelationalTypeMappingSource>()));

    private sealed class DesignTimeContext(DbContextOptions<DesignTimeContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseDamengIdentityColumns(seed: 5, increment: 10);

            modelBuilder.Entity<IdentityEntity>()
                .Property(entity => entity.Id)
                .UseDamengIdentityColumn(seed: 7, increment: 3);

            modelBuilder.Entity<SequenceEntity>()
                .Property(entity => entity.Id)
                .UseDamengSequence("SequenceEntityNumbers", "SA");
        }
    }

    private sealed class IdentityEntity
    {
        public int Id { get; set; }
    }

    private sealed class SequenceEntity
    {
        public long Id { get; set; }
    }
}
