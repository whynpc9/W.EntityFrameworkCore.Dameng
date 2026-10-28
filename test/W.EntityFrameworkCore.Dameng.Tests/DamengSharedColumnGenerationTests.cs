using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.Tests;

[Trait("Plan", "012")]
public class DamengSharedColumnGenerationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedColumnBaselineAcceptsMatchingIdentity(bool reverse)
        => AssertMatchingGeneration("identity", reverse);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedColumnBaselineAcceptsConventionForeignKeyCopy(bool reverse)
        => AssertMatchingGeneration("copy", reverse);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedColumnAcceptsMatchingSequence(bool reverse)
        => AssertMatchingGeneration("sequence", reverse);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedColumnAcceptsEquivalentDefaultAndExplicitSequenceSchema(bool reverse)
        => AssertMatchingGeneration("schema", reverse);

    private void AssertMatchingGeneration(string mode, bool reverse)
    {
        using var context = Create(mode, reverse);
        var model = context.GetService<IDesignTimeModel>().Model;
        var table = Assert.Single(model.GetRelationalModel().Tables);
        var column = table.FindColumn("Id")!;
        Assert.Equal(2, column.PropertyMappings.Count);
        Assert.Equal(2, column.PropertyMappings.Select(m => m.Property.DeclaringType).Distinct().Count());
        var store = StoreObjectIdentifier.Table("Shared", "APP");
        var principal = model.FindEntityType(typeof(Principal))!.FindProperty("Id")!;
        var dependent = model.FindEntityType(typeof(Dependent))!.FindProperty("Id")!;
        var strategy = mode is "sequence" or "schema"
            ? DamengValueGenerationStrategy.Sequence : DamengValueGenerationStrategy.IdentityColumn;
        Assert.Equal(strategy, principal.GetDamengValueGenerationStrategy(store));
        Assert.Equal(mode == "copy" ? DamengValueGenerationStrategy.None : strategy,
            dependent.GetDamengValueGenerationStrategy(store));
        Assert.Equal(strategy, column["Dameng:ValueGenerationStrategy"]);
        var sql = context.Database.GenerateCreateScript();
        output.WriteLine(sql);
        if (strategy == DamengValueGenerationStrategy.IdentityColumn)
        {
            Assert.Equal(7L, column["Dameng:IdentitySeed"]);
            Assert.Equal(3, column["Dameng:IdentityIncrement"]);
            Assert.Contains("\"Id\" BIGINT IDENTITY(7,3) NOT NULL", sql, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal("Ids", column["Dameng:SequenceName"]);
            Assert.Equal("APP", column["Dameng:SequenceSchema"]);
            Assert.Contains("\"APP\".\"Ids\".NEXTVAL", sql, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("seed", false, "IdentitySeed")]
    [InlineData("seed", true, "IdentitySeed")]
    public void SharedColumnRejectsConflictingIdentitySeed(string mode, bool reverse, string facet)
        => AssertConflictingGeneration(mode, reverse, facet);

    [Theory]
    [InlineData("increment", false, "IdentityIncrement")]
    [InlineData("increment", true, "IdentityIncrement")]
    [InlineData("negative", false, "IdentityIncrement")]
    [InlineData("negative", true, "IdentityIncrement")]
    public void SharedColumnRejectsConflictingIdentityIncrement(string mode, bool reverse, string facet)
        => AssertConflictingGeneration(mode, reverse, facet);

    [Theory]
    [InlineData("mixed", false, "ValueGenerationStrategy")]
    [InlineData("mixed", true, "ValueGenerationStrategy")]
    public void SharedColumnRejectsIdentitySequenceConflict(string mode, bool reverse, string facet)
        => AssertConflictingGeneration(mode, reverse, facet);

    [Theory]
    [InlineData("name", false, "SequenceName")]
    [InlineData("name", true, "SequenceName")]
    [InlineData("differentSchema", false, "SequenceSchema")]
    [InlineData("differentSchema", true, "SequenceSchema")]
    public void SharedColumnRejectsDifferentSequenceNameOrSchema(string mode, bool reverse, string facet)
        => AssertConflictingGeneration(mode, reverse, facet);

    private static void AssertConflictingGeneration(string mode, bool reverse, string facet)
    {
        using var context = Create(mode, reverse);
        var exception = Assert.Throws<InvalidOperationException>(() => context.GetService<IDesignTimeModel>().Model);
        Assert.Contains(facet, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Principal.Id", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Dependent.Id", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Shared", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedColumnValidationPreservesTptAndTpcGeneration(bool tpc)
    {
        using var context = Create(tpc ? "tpc" : "tpt", false);
        var model = context.GetService<IDesignTimeModel>().Model;
        var property = model.FindEntityType(typeof(Root))!.FindProperty("Id")!;
        foreach (var table in model.GetRelationalModel().Tables)
        {
            var expected = tpc ? DamengValueGenerationStrategy.Sequence
                : table.Name == "Roots" ? DamengValueGenerationStrategy.IdentityColumn
                : DamengValueGenerationStrategy.None;
            Assert.Equal(expected, property.GetDamengValueGenerationStrategy(
                StoreObjectIdentifier.Table(table.Name, table.Schema)));
        }
        Assert.Equal(tpc ? 2 : 3, model.GetRelationalModel().Tables.Count());
        output.WriteLine(context.Database.GenerateCreateScript());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SharedColumnOutcomeDoesNotDependOnEntityRegistrationOrderOrNoneMapping(bool conflict, bool reverse)
    {
        using var context = Create(conflict ? "threeConflict" : "threeMatching", reverse);
        if (conflict)
        {
            var error = Assert.Throws<InvalidOperationException>(() => context.GetService<IDesignTimeModel>().Model);
            Assert.Contains("IdentitySeed", error.Message, StringComparison.Ordinal);
            Assert.Contains("Dependent.Id", error.Message, StringComparison.Ordinal);
            Assert.Contains("Third.Id", error.Message, StringComparison.Ordinal);
        }
        else
        {
            var model = context.GetService<IDesignTimeModel>().Model;
            var column = Assert.Single(model.GetRelationalModel().Tables).FindColumn("Id")!;
            Assert.Equal(3, column.PropertyMappings.Count);
            Assert.Equal(DamengValueGenerationStrategy.None,
                model.FindEntityType(typeof(Principal))!.FindProperty("Id")!.GetDamengValueGenerationStrategy());
            Assert.Equal(7L, column["Dameng:IdentitySeed"]);
        }
    }

    private static SharedContext Create(string mode, bool reverse)
        => new(new DbContextOptionsBuilder<SharedContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .EnableServiceProviderCaching(false).Options, mode, reverse);

    private sealed class SharedContext(DbContextOptions<SharedContext> options, string mode, bool reverse)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("APP");
            if (mode is "tpc" or "tpt")
            {
                var root = modelBuilder.Entity<Root>();
                if (mode == "tpc")
                {
                    root.UseTpcMappingStrategy();
                    root.Property(e => e.Id).UseDamengSequence("HierarchyIds");
                }
                else
                {
                    root.UseTptMappingStrategy();
                    root.ToTable("Roots");
                }
                modelBuilder.Entity<First>().ToTable("Firsts");
                modelBuilder.Entity<Second>().ToTable("Seconds");
                return;
            }
            if (reverse) modelBuilder.Entity<Dependent>();
            var principal = modelBuilder.Entity<Principal>();
            var dependent = modelBuilder.Entity<Dependent>();
            principal.ToTable("Shared");
            dependent.ToTable("Shared");
            principal.HasOne(e => e.Dependent).WithOne(e => e.Principal)
                .HasForeignKey<Dependent>(e => e.Id);
            principal.Navigation(e => e.Dependent).IsRequired();
            if (mode is not ("threeConflict" or "threeMatching")) dependent.Ignore(e => e.Third);
            var left = principal.Property(e => e.Id).HasColumnName("Id");
            var right = dependent.Property(e => e.Id).HasColumnName("Id");
            if (mode is "threeConflict" or "threeMatching")
            {
                left.ValueGeneratedNever();
                right.UseDamengIdentityColumn(7, 3);
                var third = modelBuilder.Entity<Third>();
                third.ToTable("Shared");
                dependent.HasOne(e => e.Third).WithOne(e => e.Dependent).HasForeignKey<Third>(e => e.Id);
                dependent.Navigation(e => e.Third).IsRequired();
                third.Property(e => e.Id).HasColumnName("Id").UseDamengIdentityColumn(mode == "threeConflict" ? 11 : 7, 3);
                return;
            }
            if (mode is "sequence" or "schema" or "name" or "differentSchema")
            {
                left.UseDamengSequence("Ids");
                right.UseDamengSequence(mode == "name" ? "OtherIds" : "Ids",
                    mode == "differentSchema" ? "OTHER" : mode == "schema" ? "APP" : null);
            }
            else
            {
                left.UseDamengIdentityColumn(7, 3);
                if (mode == "mixed") right.UseDamengSequence("Ids");
                else if (mode != "copy") right.UseDamengIdentityColumn(mode == "seed" ? 11 : 7,
                    mode == "increment" ? 2 : mode == "negative" ? -3 : 3);
            }
        }
    }
    private sealed class Principal { public long Id { get; set; } public Dependent Dependent { get; set; } = null!; }
    private sealed class Dependent { public long Id { get; set; } public Principal Principal { get; set; } = null!; public Third Third { get; set; } = null!; }
    private sealed class Third { public long Id { get; set; } public Dependent Dependent { get; set; } = null!; }
    private abstract class Root { public long Id { get; set; } }
    private sealed class First : Root { public int Value { get; set; } }
    private sealed class Second : Root { public int Value { get; set; } }
}
