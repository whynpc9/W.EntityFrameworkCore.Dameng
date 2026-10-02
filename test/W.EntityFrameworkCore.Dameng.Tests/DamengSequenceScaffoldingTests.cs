using System.Data;
using System.Data.Common;
using Dm;
using W.EntityFrameworkCore.Dameng.Scaffolding.Internal;
using Xunit;

#pragma warning disable EF1001 // Tests exercise the provider's catalog contracts.

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengSequenceScaffoldingTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(500, 1)]
    [InlineData(501, 2)]
    [InlineData(1001, 3)]
    public void CatalogQueriesBoundParametersAndReadEachDistinctNameOnce(int count, int expectedBatches)
    {
        using var connection = new DmConnection("Server=database.example;Port=5236;User Id=app;Password=example");
        var names = Enumerable.Range(0, count).Select(index => $"SEQ_{index}").ToArray();
        if (count > 0)
        {
            names[0] = "Seq'$.Name";
        }

        var seen = new List<string>();
        var batches = 0;
        foreach (var command in DamengDatabaseModelFactory.CreateSequenceCatalogCommands(
            connection, "Quoted'Schema", names.Concat(names.Take(3))))
        {
            using (command)
            {
                batches++;
                Assert.InRange(command.Parameters.Count, 2, 501);
                Assert.Equal("Quoted'Schema", command.Parameters[0].Value);
                Assert.DoesNotContain("Quoted'Schema", command.CommandText, StringComparison.Ordinal);
                Assert.DoesNotContain("Seq'$.Name", command.CommandText, StringComparison.Ordinal);
                var parameters = command.Parameters.Cast<DbParameter>().ToArray();
                Assert.Equal(parameters.Length, parameters.Select(parameter => parameter.ParameterName).Distinct().Count());
                Assert.All(parameters, parameter => Assert.Contains(":" + parameter.ParameterName, command.CommandText, StringComparison.Ordinal));
                seen.AddRange(parameters.Skip(1).Select(parameter => Assert.IsType<string>(parameter.Value)));
            }
        }

        Assert.Equal(expectedBatches, batches);
        Assert.Equal(names.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void CatalogNameDeduplicationPreservesQuotedCase()
    {
        using var connection = new DmConnection("Server=database.example;Port=5236;User Id=app;Password=example");
        using var command = Assert.Single(DamengDatabaseModelFactory.CreateSequenceCatalogCommands(
            connection, "APP", ["Seq", "SEQ", "Seq"]));
        Assert.Equal(3, command.Parameters.Count);
        Assert.Equal("Seq", command.Parameters[1].Value);
        Assert.Equal("SEQ", command.Parameters[2].Value);
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
        { 4, "UNKNOWN" }
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
        foreach (var column in new[] { "Name", "Increment", "Min", "Max", "Cycle", "Start" })
        {
            table.Columns.Add(column, typeof(object));
        }

        table.Rows.Add("Seq", increment, min, max, cycle, start);
        return table;
    }
}
