using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Scaffolding.Internal;
using W.EntityFrameworkCore.Dameng.Tests.TestUtilities;
using Xunit;

#pragma warning disable EF1001
namespace W.EntityFrameworkCore.Dameng.Tests;

[Trait("Category", "CatalogCompleteness")]
public sealed class DamengCatalogColumnCompletenessTests
{
    private static DatabaseModel Create(DamengCatalogFixture fixture, params string[] tables)
    {
        using var context = new DbContext(new DbContextOptionsBuilder().UseDameng("Server=synthetic.invalid").Options);
        return new DamengDatabaseModelFactory(context.GetService<IRelationalTypeMappingSource>(),
            context.GetService<ISqlGenerationHelper>(), new DamengCatalogFixture.Driver())
            .Create(fixture, new DatabaseModelFactoryOptions(tables));
    }

    [Fact]
    public void MissingOrdinaryColumnIsRejectedBeforeReturningModel()
    {
        using var fixture = new DamengCatalogFixture { CompatibilityColumns = ["Id"], HasPrimaryKey = true };
        var error = Assert.Throws<NotSupportedException>(() => Create(fixture));
        Assert.Contains("table 'T'", error.Message);
        Assert.Contains("Missing columns: 'Payload'", error.Message);
        Assert.Equal(System.Data.ConnectionState.Closed, fixture.State);
    }

    [Fact]
    public void EmptyCompatibilityColumnSetIsRejectedForSelectedOrdinaryTable()
    {
        using var fixture = new DamengCatalogFixture { CompatibilityColumns = [] };
        var error = Assert.Throws<NotSupportedException>(() => Create(fixture));
        Assert.Contains("'Id', 'Payload'", error.Message);
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("Payload.with.dot")]
    [InlineData("quote\"name")]
    public void UnexpectedColumnDoesNotSubstituteForMissingOrdinalName(string unexpected)
    {
        using var fixture = new DamengCatalogFixture { CompatibilityColumns = ["Id", unexpected] };
        var error = Assert.Throws<NotSupportedException>(() => Create(fixture));
        Assert.Contains("Missing columns: 'Payload'", error.Message);
        Assert.Contains("unexpected columns: '" + unexpected + "'", error.Message);
    }

    [Fact]
    public void MissingNativeColumnCatalogCannotValidateAnEmptyTable()
    {
        using var fixture = new DamengCatalogFixture { NativeColumns = [], CompatibilityColumns = [] };
        Assert.Contains("no readable native column metadata", Assert.Throws<NotSupportedException>(() => Create(fixture)).Message);
    }

    [Fact]
    public void CompleteOrdinaryCatalogProducesEveryColumn()
    {
        string[] leading = ["Id", "Payload", "with.dot", "quote\"name", "lower", "LOWER"];
        var columns = leading.Concat(Enumerable.Range(0, 160).Select(i => "C" + i)).ToArray();
        using var fixture = new DamengCatalogFixture { NativeColumns = columns, CompatibilityColumns = columns };
        var table = Assert.Single(Create(fixture).Tables);
        Assert.Equal(columns, table.Columns.Select(c => c.Name));
        Assert.True(table.Columns[1].IsNullable);
    }

    [Fact]
    public void ExcludedTableDoesNotParticipateInCompletenessCheck()
    {
        using var fixture = new DamengCatalogFixture { Tables = ["T", "Excluded"], OmitExcludedCompatibilityColumns = true };
        Assert.Equal("T", Assert.Single(Create(fixture, "T").Tables).Name);
    }

    [Fact]
    public void ViewProjectionDoesNotRequireNativeOrdinaryTableColumns()
    {
        using var fixture = new DamengCatalogFixture { Tables = [], Views = ["V"], CompatibilityColumns = ["Id"] };
        Assert.IsType<DatabaseView>(Assert.Single(Create(fixture).Tables));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptySchemaAndSequenceOnlySchemaRemainSupported(bool hasSequence)
    {
        using var fixture = new DamengCatalogFixture { Tables = [], HasSequence = hasSequence };
        var model = Create(fixture);
        Assert.Empty(model.Tables);
        Assert.Equal(hasSequence ? 1 : 0, model.Sequences.Count);
    }

    [Fact]
    public void MissingVirtualColumnRetainsExistingRejection()
    {
        using var fixture = new DamengCatalogFixture { CompatibilityColumns = ["Id"], VirtualColumn = "Payload" };
        Assert.Contains("virtual column 'Payload'", Assert.Throws<NotSupportedException>(() => Create(fixture)).Message);
    }

    [Fact]
    public void MissingConstraintColumnRetainsExistingRejection()
    {
        using var fixture = new DamengCatalogFixture { NativeColumns = ["Payload"], CompatibilityColumns = ["Payload"], HasPrimaryKey = true };
        Assert.Contains("PK_T", Assert.Throws<NotSupportedException>(() => Create(fixture)).Message);
    }
}
