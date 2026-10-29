using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Infrastructure;
using W.EntityFrameworkCore.Dameng.Scaffolding.Internal;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable EF1001
namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

[Trait("Category", "CatalogCompleteness")]
public sealed class DamengCatalogColumnCompletenessFunctionalTests(ITestOutputHelper output)
{
    [DamengFact]
    public async Task OrdinaryColumnsAndViewProjectionRemainComplete()
    {
        await InOwnedSchema(async (connection, schema) =>
        {
            await Execute(connection, "CREATE TABLE T (\"Id\" INT NOT NULL, \"Payload\" NVARCHAR2(40), \"with.dot\" INT, \"quote\"\"name\" INT, \"lower\" INT, \"UPPERED\" INT, CALC AS (\"Id\" + 1), CONSTRAINT PK_T NOT CLUSTER PRIMARY KEY(\"Id\")) STORAGE(CLUSTERBTR)");
            await Execute(connection, "CREATE VIEW V AS SELECT \"Id\" FROM T");
            await Execute(connection, "INSERT INTO T (\"Id\",\"Payload\",\"with.dot\",\"quote\"\"name\",\"lower\",\"UPPERED\") VALUES(1,'中文',2,3,4,5)");
            await Execute(connection, "INSERT INTO T (\"Id\",\"Payload\",\"with.dot\",\"quote\"\"name\",\"lower\",\"UPPERED\") VALUES(2,NULL,NULL,NULL,NULL,NULL)");
            var model = Create(connection, "T", "V");
            var table = Assert.Single(model.Tables, t => t.Name == "T");
            string[] expected = ["Id", "Payload", "with.dot", "quote\"name", "lower", "UPPERED", "CALC"];
            Assert.Equal(expected, table.Columns.Select(c => c.Name));
            Assert.Equal("Id", Assert.Single(Assert.IsType<DatabaseView>(Assert.Single(model.Tables, t => t.Name == "V")).Columns).Name);
            var native = await Read(connection,
                "SELECT C.NAME, C.COLID, C.INFO2, COALESCE(I.INFO1,0) FROM SYS.SYSCOLUMNS C INNER JOIN SYS.SYSOBJECTS T ON T.ID=C.ID INNER JOIN SYS.SYSOBJECTS S ON S.ID=T.SCHID LEFT JOIN SYS.SYSCOLINFOS I ON I.ID=C.ID AND I.COLID=C.COLID WHERE S.NAME=:schema AND T.NAME='T' ORDER BY C.COLID", schema);
            var compatible = await Read(connection, "SELECT COLUMN_NAME, COLUMN_ID, HIDDEN_COLUMN FROM ALL_TAB_COLS WHERE OWNER=:schema AND TABLE_NAME='T' ORDER BY COLUMN_ID", schema);
            output.WriteLine("Native column name/id/info2/generation: " + string.Join(";", native.Select(r => string.Join("|", r))));
            output.WriteLine("Compatible column name/id/hidden: " + string.Join(";", compatible.Select(r => string.Join("|", r))));
            Assert.Equal(expected, native.Select(r => r[0]));
            Assert.Equal(expected, compatible.Select(r => r[0]));
            Assert.All(compatible, r => Assert.Equal("NO", r[2]));
            var values = await Read(connection, "SELECT \"Id\",\"Payload\",\"with.dot\",\"quote\"\"name\",\"lower\",\"UPPERED\",CALC FROM T ORDER BY \"Id\"");
            Assert.Equal((string[])["1", "中文", "2", "3", "4", "5", "2"], values[0]);
            Assert.Equal((string[])["2", "NULL", "NULL", "NULL", "NULL", "NULL", "3"], values[1]);
        });
    }

    [DamengFact]
    public async Task SelectedTablesIgnoreUnselectedCatalogObjects()
    {
        await InOwnedSchema(async (connection, _) =>
        {
            await Execute(connection, "CREATE TABLE T (ID INT, PAYLOAD INT) STORAGE(CLUSTERBTR)");
            await Execute(connection, "CREATE TABLE EXCLUDED (ID INT, HIDDEN INT NOT VISIBLE) STORAGE(CLUSTERBTR)");
            var table = Assert.Single(Create(connection, "T").Tables);
            Assert.Equal((string[])["ID", "PAYLOAD"], table.Columns.Select(c => c.Name));
            Assert.Throws<NotSupportedException>(() => Create(connection, "EXCLUDED"));
        });
    }

    [DamengFact]
    public async Task EmptyAndSequenceOnlyOwnedSchemaRemainSupported()
    {
        await InOwnedSchema(async (connection, _) =>
        {
            var empty = Create(connection);
            Assert.Empty(empty.Tables);
            Assert.Empty(empty.Sequences);
            await Execute(connection, "CREATE SEQUENCE S START WITH 7 INCREMENT BY 2 NOCACHE NOORDER");
            var model = Create(connection);
            Assert.Empty(model.Tables);
            Assert.Equal("S", Assert.Single(model.Sequences).Name);
        });
    }

    private async Task InOwnedSchema(Func<DbConnection, string, Task> action)
    {
        var schema = "EF10_CC_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var original = (await Read(connection, "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM dual"))[0][0];
        Assert.Empty(await Read(connection, "SELECT NAME FROM SYS.SYSOBJECTS WHERE TYPE$='SCH' AND NAME=:schema", schema));
        var features = await Read(connection, "SELECT PAGE() FROM dual");
        output.WriteLine("Server=" + connection.ServerVersion + "; page=" + string.Join("/", features[0]));
        try
        {
            await Execute(connection, $"CREATE SCHEMA {Quote(schema)}");
            await Execute(connection, $"SET SCHEMA {Quote(schema)}");
            await action(connection, schema);
        }
        finally
        {
            await Cleanup();
        }

        async Task Cleanup()
        {
            // Register names before DDL; catalog-check exact names even after uncertain CREATE.
            var failures = new List<Exception>();
            foreach (var (kind, catalog, name) in new[] {
                ("VIEW", "ALL_VIEWS", "V"), ("TABLE", "ALL_TABLES", "EXCLUDED"),
                ("TABLE", "ALL_TABLES", "T"), ("SEQUENCE", "ALL_SEQUENCES", "S") })
            {
                try
                {
                    var column = kind == "VIEW" ? "VIEW_NAME" : kind == "SEQUENCE" ? "SEQUENCE_NAME" : "TABLE_NAME";
                    var owner = kind == "SEQUENCE" ? "SEQUENCE_OWNER" : "OWNER";
                    if ((await Read(connection, $"SELECT {column} FROM {catalog} WHERE {owner}=:schema AND {column}='{name}'", schema)).Count != 0)
                        await Execute(connection, $"DROP {kind} {Quote(schema)}.{Quote(name)}");
                    Assert.Empty(await Read(connection, $"SELECT {column} FROM {catalog} WHERE {owner}=:schema AND {column}='{name}'", schema));
                }
                catch (Exception error) { failures.Add(error); }
            }
            try
            {
                await Execute(connection, $"SET SCHEMA {Quote(original)}");
                if ((await Read(connection, "SELECT NAME FROM SYS.SYSOBJECTS WHERE TYPE$='SCH' AND NAME=:schema", schema)).Count != 0)
                    await Execute(connection, $"DROP SCHEMA {Quote(schema)}");
                Assert.Empty(await Read(connection, "SELECT NAME FROM SYS.SYSOBJECTS WHERE TYPE$='SCH' AND NAME=:schema", schema));
                output.WriteLine("Exact owned schema/object cleanup confirmed.");
            }
            catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException("Owned catalog fixture cleanup failed.", failures);
        }
    }

    private static DatabaseModel Create(DbConnection connection, params string[] tables)
    {
        using var context = new DbContext(new DbContextOptionsBuilder().UseDamengUnderTest(DamengTestEnvironment.GetRequiredConnectionString()).Options);
        return new DamengDatabaseModelFactory(context.GetService<IRelationalTypeMappingSource>(), context.GetService<ISqlGenerationHelper>(), context.GetService<DamengDriver>())
            .Create(connection, new DatabaseModelFactoryOptions(tables));
    }
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static async Task Execute(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<List<string[]>> Read(DbConnection connection, string sql, string? schema = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (schema is not null)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "schema";
            parameter.Value = schema;
            command.Parameters.Add(parameter);
        }
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string[]>();
        while (await reader.ReadAsync())
            rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "NULL" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)!).ToArray());
        return rows;
    }
}
