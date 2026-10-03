using System.Data;
using System.Data.Common;
using Dm;
using W.EntityFrameworkCore.Dameng.Scaffolding.Internal;
using Xunit;

#pragma warning disable EF1001 // Tests exercise the provider's catalog contracts.

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengSequenceScaffoldingTests
{
    [Fact]
    public void CatalogQueryReadsAllSequencesInOnlyTheBoundSchema()
    {
        using var connection = new DmConnection("Server=database.example;Port=5236;User Id=app;Password=example");
        using var command = DamengDatabaseModelFactory.CreateSequenceCatalogCommand(connection, "Quoted'Schema");
        Assert.Single(command.Parameters.Cast<DbParameter>());
        Assert.Equal("schema", command.Parameters[0].ParameterName);
        Assert.Equal("Quoted'Schema", command.Parameters[0].Value);
        Assert.Contains("SEQUENCE_OWNER = :schema", command.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("Quoted'Schema", command.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("SEQUENCE_NAME IN", command.CommandText, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void ExactIntegralFacetsPreserveEfBoundaries(int increment)
    {
        using var table = CreateRow((decimal)increment, (decimal)long.MinValue, (decimal)long.MaxValue, "Y", "41");
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        var sequence = DamengDatabaseModelFactory.ReadSequenceRecord(reader, "APP");
        Assert.Equal("Seq", sequence.Name);
        Assert.Equal("APP", sequence.Schema);
        Assert.Equal(increment, sequence.IncrementBy);
        Assert.Equal(long.MinValue, sequence.MinValue);
        Assert.Equal(long.MaxValue, sequence.MaxValue);
        Assert.Equal(41L, sequence.StartValue);
        Assert.True(sequence.IsCyclic);
    }

    [Theory]
    [InlineData(1L, 1000L, 0L)]
    [InlineData(1L, 1000L, 1001L)]
    [InlineData(1000L, 1L, 41L)]
    [InlineData(41L, 41L, 41L)]
    public void InvalidSequenceBoundsOrContinuationCannotBeRecreated(long min, long max, long start)
    {
        using var table = CreateRow(1L, min, max, "N", start);
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        var error = Assert.Throws<NotSupportedException>(() => DamengDatabaseModelFactory.ReadSequenceRecord(reader, "APP"));
        Assert.Contains("Seq", error.Message, StringComparison.Ordinal);
        Assert.Contains("bounds", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(1000L)]
    public void SequenceContinuationAtEitherBoundIsStillRepresentable(long start)
    {
        using var table = CreateRow(1L, 1L, 1000L, "N", start);
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        Assert.Equal(start, DamengDatabaseModelFactory.ReadSequenceRecord(reader, "APP").StartValue);
    }

    [Theory]
    [InlineData(3L, 1001L, 1L)]
    [InlineData(-3L, -1L, 1000L)]
    public void CyclicContinuationOneStepPastTheBoundaryWraps(long increment, long start, long expected)
    {
        using var table = CreateRow(increment, 1L, 1000L, "Y", start);
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        Assert.Equal(expected, DamengDatabaseModelFactory.ReadSequenceRecord(reader, "APP").StartValue);
    }

    [Theory]
    [InlineData(3L, 0L)]
    [InlineData(-3L, 1001L)]
    [InlineData(3L, 2000L)]
    public void InconsistentCyclicContinuationIsNotSilentlyNormalized(long increment, long start)
    {
        using var table = CreateRow(increment, 1L, 1000L, "Y", start);
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        Assert.Throws<NotSupportedException>(() => DamengDatabaseModelFactory.ReadSequenceRecord(reader, "APP"));
    }

    public static TheoryData<int, object> InvalidFacets => new()
    {
        { 1, (long)int.MaxValue + 1 },
        { 1, (long)int.MinValue - 1 },
        { 1, 0 },
        { 1, 1.5m },
        { 1, 1.0d },
        { 1, DBNull.Value },
        { 1, "not-an-integer" },
        { 2, (decimal)long.MinValue - 1 },
        { 3, (decimal)long.MaxValue + 1 },
        { 5, (decimal)long.MaxValue + 1 },
        { 5, "9223372036854775808" },
        { 5, 41.5m },
        { 4, "UNKNOWN" },
        { 6, 2L },
        { 6, 50L },
        { 6, -1L },
        { 6, 0.5m },
        { 6, DBNull.Value },
        { 7, "Y" },
        { 7, "UNKNOWN" },
        { 7, DBNull.Value }
    };

    [Theory]
    [MemberData(nameof(InvalidFacets))]
    public void InvalidFacetsRejectTheSequenceInsteadOfTruncatingOrOmittingIt(int ordinal, object invalid)
    {
        using var table = CreateRow(3L, 1L, 1000L, "N", 41L);
        table.Rows[0][ordinal] = invalid;
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        var error = Assert.Throws<NotSupportedException>(() => DamengDatabaseModelFactory.ReadSequenceRecord(reader, "APP"));
        Assert.Contains("Seq", error.Message, StringComparison.Ordinal);
    }

    private static DataTable CreateRow(object increment, object min, object max, object cycle, object start)
    {
        var table = new DataTable();
        foreach (var column in new[] { "Name", "Increment", "Min", "Max", "Cycle", "Start", "Cache", "Order" })
        {
            table.Columns.Add(column, typeof(object));
        }

        table.Rows.Add("Seq", increment, min, max, cycle, start, 0L, "N");
        return table;
    }
}
