using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

[Trait("Plan", "013")]
public class DamengComplexRowVersionValidationTests
{
    [Theory]
    [InlineData("flat")]
    [InlineData("single")]
    [InlineData("nested")]
    public void ComplexRowVersionBaselineBuildsOrdinaryScalarModel(string shape)
        => AssertLegalModel(shape, "ordinary");

    [Theory]
    [InlineData("flat")]
    [InlineData("single")]
    [InlineData("nested")]
    public void ComplexRowVersionBaselineAcceptsApplicationManagedToken(string shape)
        => AssertLegalModel(shape, "application");

    [Theory]
    [InlineData("flat")]
    [InlineData("single")]
    [InlineData("nested")]
    public void ComputedComplexConcurrencyTokenUsesExistingExceptionRule(string shape)
        => AssertLegalModel(shape, "computed");

    [Theory]
    [InlineData("flat")]
    [InlineData("single")]
    [InlineData("nested")]
    public void TriggerDeclaredComplexConcurrencyTokenUsesOwningEntity(string shape)
        => AssertLegalModel(shape, "trigger");

    [Theory]
    [InlineData("ordinary")]
    [InlineData("application")]
    [InlineData("computed")]
    public void ViewOnlyFlatModelsPreserveComputedColumnException(string mode)
        => AssertLegalModel("flat", mode, viewOnly: true);

    [Fact]
    public void ViewOnlyRowVersionWithoutGenerationMechanismIsRejected()
        => AssertRejected("flat", "row", "Version", viewOnly: true);

    private static void AssertLegalModel(string shape, string mode, bool viewOnly = false)
    {
        using var context = Create(shape, mode, viewOnly);
        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(EntityClrType(shape))!;
        var property = shape == "flat" ? entity.FindProperty("Version")!
            : shape == "single" ? entity.FindComplexProperty("Data")!.ComplexType.FindProperty("Version")!
            : entity.FindComplexProperty("Data")!.ComplexType.FindComplexProperty("Inner")!.ComplexType.FindProperty("Version")!;
        var relational = model.GetRelationalModel();
        if (viewOnly)
        {
            Assert.Empty(relational.Tables);
            var view = Assert.Single(relational.Views);
            Assert.Equal("ReadRows", view.Name);
            Assert.Contains(view.Columns, c => c.PropertyMappings.Any(m => m.Property == property));
        }
        else
        {
            var table = relational.FindTable("Rows", null)!;
            Assert.Contains(table.Columns, c => c.PropertyMappings.Any(m => m.Property == property));
        }
        Assert.Equal(mode != "ordinary", property.IsConcurrencyToken);
        Assert.Equal(mode is "computed" or "trigger" ? ValueGenerated.OnAddOrUpdate : ValueGenerated.Never,
            property.ValueGenerated);
        Assert.Equal(mode == "computed" ? "NULL" : null, property.GetComputedColumnSql());
        Assert.Equal(mode == "trigger", entity.GetTriggers().Any());
    }

    [Theory]
    [InlineData("flat", "Version")]
    [InlineData("single", "Data.Version")]
    [InlineData("nested", "Data.Inner.Version")]
    public void UnsupportedRowVersionIsRejectedForFlatAndNestedProperties(string shape, string path)
        => AssertRejected(shape, "row", path);

    [Theory]
    [InlineData("single", "Data.Version")]
    [InlineData("nested", "Data.Inner.Version")]
    public void UnrelatedEntityTriggerDoesNotAllowComplexRowVersion(string shape, string path)
        => AssertRejected(shape, "unrelated", path);

    [Fact]
    public void TwoComplexInstancesAreValidatedIndependently()
        => AssertRejected("pair", "second", "Second.Version");

    private static void AssertRejected(string shape, string mode, string path, bool viewOnly = false)
    {
        using var context = Create(shape, mode, viewOnly);
        var error = Assert.Throws<InvalidOperationException>(() => context.GetService<IDesignTimeModel>().Model);
        Assert.Contains("rowversion", error.Message, StringComparison.Ordinal);
        Assert.Contains(EntityClrType(shape).Name, error.Message, StringComparison.Ordinal);
        Assert.Contains(path, error.Message, StringComparison.Ordinal);
    }

    private static Type EntityClrType(string shape) => shape switch
    {
        "flat" => typeof(Flat), "single" => typeof(Single), "nested" => typeof(Nested), _ => typeof(Pair)
    };

    private static VersionContext Create(string shape, string mode, bool viewOnly = false)
        => new(new DbContextOptionsBuilder<VersionContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .EnableServiceProviderCaching(false).Options, shape, mode, viewOnly);

    private sealed class VersionContext(DbContextOptions<VersionContext> options, string shape, string mode, bool viewOnly)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity(EntityClrType(shape));
            entity.HasKey("Id");
            entity.ToTable("Rows");
            if (viewOnly) entity.ToView("ReadRows").ToTable((string?)null);
            if (mode == "trigger") entity.ToTable("Rows", table => table.HasTrigger("VersionTrigger"));
            if (mode == "unrelated")
                modelBuilder.Entity<Other>().ToTable("OtherRows", table => table.HasTrigger("OtherTrigger"));
            if (shape == "flat")
            {
                var property = entity.Property<byte[]>("Version");
                if (mode != "ordinary") property.IsConcurrencyToken();
                if (mode is not ("ordinary" or "application")) property.IsRowVersion();
                if (mode == "computed") property.HasComputedColumnSql("NULL");
                return;
            }
            if (shape == "pair")
            {
                Configure(entity.ComplexProperty(typeof(Token), "First").IsRequired().Property<byte[]>("Version"), "application");
                Configure(entity.ComplexProperty(typeof(Token), "Second").IsRequired().Property<byte[]>("Version"), "row");
                return;
            }
            var complex = entity.ComplexProperty(shape == "single" ? typeof(Token) : typeof(Envelope), "Data").IsRequired();
            if (shape == "nested") complex = complex.ComplexProperty(typeof(Token), "Inner").IsRequired();
            Configure(complex.Property<byte[]>("Version"), mode);
        }

        private static void Configure(ComplexTypePropertyBuilder<byte[]> property, string mode)
        {
            if (mode != "ordinary") property.IsConcurrencyToken();
            if (mode is not ("ordinary" or "application")) property.IsRowVersion();
            if (mode == "computed") property.HasComputedColumnSql("NULL");
        }
    }
    private sealed class Flat { public int Id { get; set; } public byte[] Version { get; set; } = []; }
    private sealed class Single { public int Id { get; set; } public Token Data { get; set; } = new(); }
    private sealed class Nested { public int Id { get; set; } public Envelope Data { get; set; } = new(); }
    private sealed class Pair { public int Id { get; set; } public Token First { get; set; } = new(); public Token Second { get; set; } = new(); }
    private sealed class Envelope { public Token Inner { get; set; } = new(); }
    private sealed class Token { public byte[] Version { get; set; } = []; }
    private sealed class Other { public int Id { get; set; } }
}
