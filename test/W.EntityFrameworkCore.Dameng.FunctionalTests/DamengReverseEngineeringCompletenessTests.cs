using System.Data.Common;
using System.Globalization;
using System.Diagnostics;
using Xunit.Abstractions;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Scaffolding.Internal;
using Xunit;

#pragma warning disable EF1001 // Tests exercise the provider's reverse-engineering contracts.

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

public sealed class DamengReverseEngineeringCompletenessTests(ITestOutputHelper output)
{
    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullableUniquePrincipalsAreRejectedWithoutChangingUnreferencedColumns(bool composite)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var parent = "EF10_NUP_" + suffix;
        var child = "EF10_NUC_" + suffix;
        var created = new List<string>();
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        try
        {
            var principalColumns = composite ? "TENANT, CODE" : "CODE";
            await ExecuteAsync(connection, $"CREATE TABLE \"{parent}\" (ID INT NOT NULL, TENANT INT NOT NULL, CODE INT, CONSTRAINT \"PK_{suffix}\" NOT CLUSTER PRIMARY KEY(ID), CONSTRAINT \"UQ_{suffix}\" UNIQUE({principalColumns})) STORAGE(CLUSTERBTR)");
            created.Add(parent);
            await ExecuteAsync(connection, $"CREATE TABLE \"{child}\" (ID INT NOT NULL, TENANT INT NOT NULL, CODE INT, CONSTRAINT \"PK_C_{suffix}\" NOT CLUSTER PRIMARY KEY(ID), CONSTRAINT \"FK_{suffix}\" FOREIGN KEY({principalColumns}) REFERENCES \"{parent}\"({principalColumns})) STORAGE(CLUSTERBTR)");
            created.Add(child);
            await ExecuteAsync(connection, $"INSERT INTO \"{parent}\" VALUES (1, 7, NULL)");
            await using var query = connection.CreateCommand();
            query.CommandText = $"SELECT COUNT(*) FROM \"{parent}\" WHERE CODE IS NULL";
            Assert.Equal(1, Convert.ToInt32(await query.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
            var factory = CreateFactory();
            var error = Assert.Throws<NotSupportedException>(() => factory.Create(connection, new DatabaseModelFactoryOptions(tables: [parent, child])));
            Assert.Contains("FK_" + suffix, error.Message, StringComparison.Ordinal);
            Assert.Contains(parent + ".CODE", error.Message, StringComparison.Ordinal);
            var onlyParent = Assert.Single(factory.Create(connection, new DatabaseModelFactoryOptions(tables: [parent])).Tables);
            Assert.True(Assert.Single(onlyParent.Columns, column => column.Name == "CODE").IsNullable);
        }
        finally
        {
            foreach (var table in Enumerable.Reverse(created)) await ExecuteAsync(connection, $"DROP TABLE \"{table}\"");
        }
    }

    [DamengFact]
    public async Task MissingRequestedObjectsRejectEmptyAndPartialSelectionsWhileViewsRemainSelectable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var table = "EF10_SEL_" + suffix;
        var view = "view.with.dot_" + suffix;
        var missing = "EF10_MISSING_" + suffix;
        var tableCreated = false;
        var viewCreated = false;
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        try
        {
            await ExecuteAsync(connection, $"CREATE TABLE \"{table}\" (ID INT PRIMARY KEY)");
            tableCreated = true;
            await ExecuteAsync(connection, $"CREATE VIEW \"{view}\" AS SELECT ID FROM \"{table}\"");
            viewCreated = true;
            var factory = CreateFactory();
            foreach (var selected in new[] { new[] { missing }, new[] { table, missing } })
            {
                var error = Assert.Throws<NotSupportedException>(() => factory.Create(connection, new DatabaseModelFactoryOptions(tables: selected)));
                Assert.Contains(missing, error.Message, StringComparison.Ordinal);
            }
            Assert.Equal(2, factory.Create(connection, new DatabaseModelFactoryOptions(tables: [table, $"\"{view}\""])).Tables.Count);
        }
        finally
        {
            if (viewCreated) await ExecuteAsync(connection, $"DROP VIEW \"{view}\"");
            if (tableCreated) await ExecuteAsync(connection, $"DROP TABLE \"{table}\"");
        }
    }

    [DamengFact]
    public async Task VirtualExpressionsKeepQuotedIdentifiersLiteralsAndTheirInsertUpdateBehaviorWhenRecreated()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var source = "EF10_VCS_" + suffix;
        var copy = "EF10_VCC_" + suffix;
        var created = new List<string>();
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        try
        {
            await ExecuteAsync(connection, $"CREATE TABLE \"{source}\" (ID INT NOT NULL, \"n.lower\" INT DEFAULT 4, \"raw.text\" NVARCHAR2(40), CALC AS (\"n.lower\" + 1), NORMALIZED AS (UPPER(\"raw.text\") || ';--'), CONSTRAINT \"PK_VC_{suffix}\" NOT CLUSTER PRIMARY KEY(ID)) STORAGE(CLUSTERBTR)");
            created.Add(source);
            var table = Assert.Single(CreateFactory().Create(connection, new DatabaseModelFactoryOptions(tables: [source])).Tables);
            var numeric = Assert.Single(table.Columns, column => column.Name == "CALC");
            var text = Assert.Single(table.Columns, column => column.Name == "NORMALIZED");
            Assert.Contains("\"n.lower\"", numeric.ComputedColumnSql!, StringComparison.Ordinal);
            Assert.Contains("';--'", text.ComputedColumnSql!, StringComparison.Ordinal);
            foreach (var column in new[] { numeric, text })
            {
                Assert.Null(column.DefaultValueSql);
                Assert.False(column.IsStored);
                Assert.Equal(ValueGenerated.OnAddOrUpdate, column.ValueGenerated);
            }
            Assert.NotNull(Assert.Single(table.Columns, column => column.Name == "n.lower").DefaultValueSql);
            using var context = new DbContext(new DbContextOptionsBuilder().UseDameng(DamengTestEnvironment.GetRequiredConnectionString()).Options);
            var operation = new CreateTableOperation { Name = copy };
            foreach (var annotation in table.GetAnnotations()) operation[annotation.Name] = annotation.Value;
            foreach (var column in table.Columns)
            {
                operation.Columns.Add(new AddColumnOperation
                {
                    Table = copy,
                    Name = column.Name,
                    ClrType = context.GetService<IRelationalTypeMappingSource>().FindMapping(column.StoreType!)!.ClrType,
                    ColumnType = column.StoreType,
                    IsNullable = column.IsNullable,
                    DefaultValueSql = column.DefaultValueSql,
                    ComputedColumnSql = column.ComputedColumnSql,
                    IsStored = column.IsStored
                });
            }
            operation.PrimaryKey = new AddPrimaryKeyOperation { Table = copy, Name = "PK_VCC_" + suffix, Columns = ["ID"] };
            foreach (var annotation in table.PrimaryKey!.GetAnnotations()) operation.PrimaryKey[annotation.Name] = annotation.Value;
            foreach (var command in context.GetService<IMigrationsSqlGenerator>().Generate([operation]))
                await ExecuteAsync(connection, command.CommandText);
            created.Add(copy);
            foreach (var name in new[] { source, copy })
            {
                await ExecuteAsync(connection, $"INSERT INTO \"{name}\" (ID,\"n.lower\",\"raw.text\") VALUES(1,7,'o''reilly')");
                await ExecuteAsync(connection, $"INSERT INTO \"{name}\" (ID,\"n.lower\",\"raw.text\") VALUES(2,NULL,NULL)");
            }
            var sourceValues = await ReadVirtualValuesAsync(connection, source);
            Assert.Equal(sourceValues, await ReadVirtualValuesAsync(connection, copy));
            Assert.Equal("8|O'REILLY;--", sourceValues[0]);
            foreach (var name in new[] { source, copy })
                await ExecuteAsync(connection, $"UPDATE \"{name}\" SET \"n.lower\"=-2, \"raw.text\"='hello' WHERE ID=1");
            sourceValues = await ReadVirtualValuesAsync(connection, source);
            Assert.Equal(sourceValues, await ReadVirtualValuesAsync(connection, copy));
            Assert.Equal("-1|HELLO;--", sourceValues[0]);
            var recreated = Assert.Single(CreateFactory().Create(connection, new DatabaseModelFactoryOptions(tables: [copy])).Tables);
            Assert.Equal(numeric.ComputedColumnSql, Assert.Single(recreated.Columns, column => column.Name == "CALC").ComputedColumnSql);
            Assert.Equal(text.ComputedColumnSql, Assert.Single(recreated.Columns, column => column.Name == "NORMALIZED").ComputedColumnSql);
        }
        finally
        {
            foreach (var name in Enumerable.Reverse(created)) await ExecuteAsync(connection, $"DROP TABLE \"{name}\"");
        }
    }

    private static async Task<string[]> ReadVirtualValuesAsync(DbConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT CALC,NORMALIZED FROM \"{table}\" ORDER BY ID";
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add(string.Join("|", Enumerable.Range(0, 2).Select(index => reader.IsDBNull(index) ? "NULL" : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture))));
        return values.ToArray();
    }

    [DamengFact]
    public async Task FilteredCatalogReadsFewerRowsAndIdentityFacetsUseOneCommand()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var schema = "EF10_QS_" + suffix;
        var names = Enumerable.Range(0, 12).Select(index => "EF10_QT_" + index).ToArray();
        var created = new List<string>();
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM dual";
        var originalSchema = Convert.ToString(await query.ExecuteScalarAsync(), CultureInfo.InvariantCulture)!;
        var schemaCreated = false;
        try
        {
            await ExecuteAsync(connection, $"CREATE SCHEMA \"{schema}\"");
            schemaCreated = true;
            await ExecuteAsync(connection, $"SET SCHEMA \"{schema}\"");
            foreach (var (name, index) in names.Select((name, index) => (name, index)))
            {
                await ExecuteAsync(connection, $"CREATE TABLE \"{name}\" (ID BIGINT IDENTITY({index + 7},3) NOT NULL, A INT, B NVARCHAR2(20), C INT, CONSTRAINT \"PK_{name}\" NOT CLUSTER PRIMARY KEY(ID)) STORAGE(CLUSTERBTR)");
                created.Add(name);
            }
            var factory = CreateFactory();
            using var recorder = new CatalogQueryRecorder(connection);
            var timer = Stopwatch.StartNew();
            var full = factory.Create(recorder, new DatabaseModelFactoryOptions());
            var fullMilliseconds = timer.ElapsedMilliseconds;
            var fullQueries = recorder.Queries.ToArray();
            var fullRows = await CountCatalogRowsAsync(connection, fullQueries);
            Assert.Equal(12, full.Tables.Count);
            Assert.Single(fullQueries, entry => entry.Sql.Contains("IDENT_SEED", StringComparison.Ordinal));
            recorder.Queries.Clear();
            timer.Restart();
            var filtered = factory.Create(recorder, new DatabaseModelFactoryOptions(tables: [names[0]]));
            var filteredMilliseconds = timer.ElapsedMilliseconds;
            var filteredQueries = recorder.Queries.ToArray();
            var filteredRows = await CountCatalogRowsAsync(connection, filteredQueries);
            Assert.True(filteredRows < fullRows, $"Filtered rows {filteredRows} must be below full rows {fullRows}.");
            var selected = Assert.Single(filtered.Tables);
            Assert.Equal(7L, selected.Columns[0]["Dameng:IdentitySeed"]);
            Assert.Equal(3, selected.Columns[0]["Dameng:IdentityIncrement"]);
            Assert.Equal(fullQueries.Length, filteredQueries.Length);
            Assert.Single(filteredQueries, entry => entry.Sql.Contains("IDENT_SEED", StringComparison.Ordinal));
            Assert.All(filteredQueries.Where(entry => entry.ReturnsRows && !entry.Sql.Contains("ALL_SEQUENCES", StringComparison.Ordinal) && !entry.Sql.Contains("IDENT_SEED", StringComparison.Ordinal)),
                entry => Assert.Contains(":table0", entry.Sql, StringComparison.Ordinal));
            output.WriteLine($"Catalog comparison: full commands={fullQueries.Length}, rows={fullRows}, elapsedMs={fullMilliseconds}; filtered commands={filteredQueries.Length}, rows={filteredRows}, elapsedMs={filteredMilliseconds}; identity commands=1 for 12 tables.");
        }
        finally
        {
            await ExecuteAsync(connection, $"SET SCHEMA \"{originalSchema.Replace("\"", "\"\"", StringComparison.Ordinal)}\"");
            foreach (var name in Enumerable.Reverse(created)) await ExecuteAsync(connection, $"DROP TABLE \"{schema}\".\"{name}\"");
            if (schemaCreated) await ExecuteAsync(connection, $"DROP SCHEMA \"{schema}\"");
        }
    }

    private static async Task<int> CountCatalogRowsAsync(DbConnection connection, IEnumerable<CatalogQueryRecorder.Query> queries)
    {
        var rows = 0;
        foreach (var entry in queries.Where(entry => entry.ReturnsRows))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = entry.Sql;
            foreach (var (name, value) in entry.Parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) rows++;
        }
        return rows;
    }

    private static DamengDatabaseModelFactory CreateFactory()
    {
        using var context = new DbContext(new DbContextOptionsBuilder().UseDameng(DamengTestEnvironment.GetRequiredConnectionString()).Options);
        return new DamengDatabaseModelFactory(context.GetService<IRelationalTypeMappingSource>(), context.GetService<ISqlGenerationHelper>());
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
