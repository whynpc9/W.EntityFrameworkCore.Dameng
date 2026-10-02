using System.Data.Common;
using System.Globalization;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Metadata.Internal;
using W.EntityFrameworkCore.Dameng.Scaffolding.Internal;
using Xunit;

#pragma warning disable EF1001 // Tests intentionally exercise EF/provider infrastructure contracts.

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

/// <summary>
/// Reverse engineering (IDatabaseModelFactory) against a real Dameng schema.
/// </summary>
public sealed class DamengReverseEngineeringFunctionalTests
{
    [DamengTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task FactoryRejectsTriggersWithoutBlockingOrdinaryTableFilters(bool disabled, bool useView)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var source = $"EF10_TRSRC_{suffix}";
        var ordinary = $"EF10_TRSAFE_{suffix}";
        var view = $"EF10_TRVIEW_{suffix}";
        var trigger = $"EF10_TRIGGER_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var cleanup = new List<string>();
        try
        {
            foreach (var table in new[] { source, ordinary })
            {
                await ExecuteAsync(connection, $"CREATE TABLE \"{table}\" (ID INT PRIMARY KEY)");
                cleanup.Add($"DROP TABLE \"{table}\"");
            }

            if (useView)
            {
                await ExecuteAsync(connection, $"CREATE VIEW \"{view}\" AS SELECT ID FROM \"{source}\"");
                cleanup.Add($"DROP VIEW \"{view}\"");
            }

            var selected = useView ? view : source;
            var timing = useView ? "INSTEAD OF" : "AFTER";
            await ExecuteAsync(connection, $"CREATE TRIGGER \"{trigger}\" {timing} INSERT ON \"{selected}\" FOR EACH ROW BEGIN NULL; END;");
            cleanup.Add($"DROP TRIGGER \"{trigger}\"");
            if (disabled)
            {
                await ExecuteAsync(connection, $"ALTER TRIGGER \"{trigger}\" DISABLE");
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT TABLE_NAME, STATUS FROM ALL_TRIGGERS "
                    + "WHERE TABLE_OWNER = SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) AND TRIGGER_NAME = :name";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "name";
                parameter.Value = trigger;
                command.Parameters.Add(parameter);
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(selected, reader.GetString(0));
                Assert.Equal(disabled ? "N" : "Y", reader.GetString(1));
            }

            var factory = CreateFactory();
            var error = Assert.Throws<NotSupportedException>(() => factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [selected])));
            Assert.Contains(selected, error.Message, StringComparison.Ordinal);
            Assert.Contains(trigger, error.Message, StringComparison.Ordinal);
            Assert.Equal(ordinary, Assert.Single(factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [ordinary])).Tables).Name);
        }
        finally
        {
            foreach (var sql in Enumerable.Reverse(cleanup))
            {
                await ExecuteAsync(connection, sql);
            }
        }
    }

    [DamengTheory]
    [InlineData("BFILE", false)]
    [InlineData("TIME WITH TIME ZONE", false)]
    [InlineData("INTERVAL HOUR TO MINUTE", false)]
    [InlineData("BFILE", true)]
    public async Task FactoryRejectsUnmappedColumnsWithoutBlockingSupportedTableFilters(string storeType, bool useView)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var unsupported = $"EF10_UNMAP_{suffix}";
        var ordinary = $"EF10_MAP_{suffix}";
        var view = $"EF10_UNVIEW_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var cleanup = new List<string>();
        try
        {
            await ExecuteAsync(connection, $"CREATE TABLE \"{unsupported}\" (ID INT, N {storeType})");
            cleanup.Add($"DROP TABLE \"{unsupported}\"");
            await ExecuteAsync(connection, $"CREATE TABLE \"{ordinary}\" (ID INT PRIMARY KEY, N VARCHAR(20))");
            cleanup.Add($"DROP TABLE \"{ordinary}\"");
            if (useView)
            {
                await ExecuteAsync(connection, $"CREATE VIEW \"{view}\" AS SELECT ID, N FROM \"{unsupported}\"");
                cleanup.Add($"DROP VIEW \"{view}\"");
            }

            var selected = useView ? view : unsupported;
            var factory = CreateFactory();
            var error = Assert.Throws<NotSupportedException>(() => factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [selected])));
            Assert.Contains(selected, error.Message, StringComparison.Ordinal);
            Assert.Contains("'N'", error.Message, StringComparison.Ordinal);
            Assert.Contains(storeType, error.Message, StringComparison.Ordinal);
            var supported = Assert.Single(factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [ordinary])).Tables);
            Assert.Equal(ordinary, supported.Name);
            Assert.Equal(["ID", "N"], supported.Columns.Select(column => column.Name).ToArray());
        }
        finally
        {
            foreach (var sql in Enumerable.Reverse(cleanup))
            {
                await ExecuteAsync(connection, sql);
            }
        }
    }

    [DamengTheory]
    [InlineData("Chinese_PRC_CS_AS_KS_WS")]
    [InlineData("utf8mb4_bin")]
    [InlineData("utf8mb4_general_ci")]
    [InlineData("EF10_NONEXISTENT_COLLATION")]
    public async Task ColumnCollateSyntaxDoesNotPersistAColumnFacetOnReferenceServer(string collation)
    {
        var tableName = $"EF10_CDEF_{Guid.NewGuid():N}".ToUpperInvariant();
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = false;
        try
        {
            // The reference server accepts even an unknown column COLLATE name, but
            // does not persist or apply it. ORDER BY COLLATE is a separate SQL feature.
            await ExecuteAsync(connection, $"CREATE TABLE \"{tableName}\" (ID INT, N VARCHAR(20) COLLATE {collation}, M VARCHAR(20))");
            created = true;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT TABLEDEF(SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()), :name) FROM dual";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "name";
                parameter.Value = tableName;
                command.Parameters.Add(parameter);
                var definition = Assert.IsType<string>(await command.ExecuteScalarAsync());
                Assert.Contains("VARCHAR(20)", definition, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("COLLATE", definition, StringComparison.OrdinalIgnoreCase);
            }

            var values = new[] { "a", "A", "张", "李" };
            for (var index = 0; index < values.Length; index++)
            {
                await ExecuteAsync(connection, $"INSERT INTO \"{tableName}\" VALUES ({index}, '{values[index]}', '{values[index]}')");
            }

            async Task<string[]> ReadAsync(string sql)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                await using var reader = await command.ExecuteReaderAsync();
                var rows = new List<string>();
                while (await reader.ReadAsync())
                {
                    rows.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
                }

                return rows.ToArray();
            }

            Assert.Equal(await ReadAsync($"SELECT COUNT(*) FROM \"{tableName}\" WHERE M = 'a'"),
                await ReadAsync($"SELECT COUNT(*) FROM \"{tableName}\" WHERE N = 'a'"));
            Assert.Equal(await ReadAsync($"SELECT M FROM \"{tableName}\" ORDER BY M, ID"),
                await ReadAsync($"SELECT N FROM \"{tableName}\" ORDER BY N, ID"));
            Assert.Equal(["李", "张"], await ReadAsync(
                $"SELECT N FROM \"{tableName}\" WHERE ID >= 2 ORDER BY N COLLATE Chinese_PRC_CS_AS_KS_WS"));

            var table = Assert.Single(CreateFactory().Create(connection,
                new DatabaseModelFactoryOptions(tables: [tableName])).Tables);
            Assert.Equal(3, table.Columns.Count);
            Assert.All(table.Columns, column => Assert.Null(column.Collation));
            Assert.Equal(table.Columns[2].StoreType, table.Columns[1].StoreType);
        }
        finally
        {
            if (created)
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{tableName}\"");
            }
        }
    }

    [DamengFact]
    public async Task FactoryRejectsAutoIncrementWithoutChangingIdentityOrTableFilters()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var automatic = $"EF10_AUTO_{suffix}";
        var identity = $"EF10_IDENT_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = new List<string>();
        try
        {
            await ExecuteAsync(connection, $"CREATE TABLE \"{automatic}\" (ID INT PRIMARY KEY AUTO_INCREMENT, N INT) AUTO_INCREMENT = 20");
            created.Add(automatic);
            await ExecuteAsync(connection, $"CREATE TABLE \"{identity}\" (ID INT IDENTITY(7, 3) PRIMARY KEY, N INT)");
            created.Add(identity);
            foreach (var (name, kind) in new[] { (automatic, (byte)2), (identity, (byte)1) })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT C.INFO2, O.INFO6 FROM SYS.SYSCOLUMNS C "
                    + "JOIN SYS.SYSOBJECTS O ON O.ID = C.ID "
                    + "WHERE O.SCHID = CURRENT_SCHID() AND O.NAME = :name AND C.NAME = 'ID'";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "name";
                parameter.Value = name;
                command.Parameters.Add(parameter);
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1L, Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture) & 1L);
                var info6 = Assert.IsType<byte[]>(reader.GetValue(1));
                Assert.True(info6.Length >= 26);
                Assert.Equal(kind, info6[24]);
                Assert.Equal(0, info6[25]);
            }

            var factory = CreateFactory();
            var error = Assert.Throws<NotSupportedException>(() => factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [automatic])));
            Assert.Contains(automatic, error.Message, StringComparison.Ordinal);
            Assert.Contains("AUTO_INCREMENT", error.Message, StringComparison.Ordinal);
            var table = Assert.Single(factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [identity])).Tables);
            var column = Assert.Single(table.Columns, c => c.Name == "ID");
            Assert.Equal(DamengValueGenerationStrategy.IdentityColumn, column[DamengAnnotationNames.ValueGenerationStrategy]);
            Assert.Equal(7L, column[DamengAnnotationNames.IdentitySeed]);
            Assert.Equal(3, column[DamengAnnotationNames.IdentityIncrement]);
        }
        finally
        {
            foreach (var table in Enumerable.Reverse(created))
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{table}\"");
            }
        }
    }

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FactoryRejectsPartitionedTablesWithoutBlockingOrdinaryTableFilters(bool hash)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var partitioned = $"EF10_PART_{suffix}";
        var ordinary = $"EF10_PSAFE_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = new List<string>();
        try
        {
            var partition = hash
                ? "PARTITION BY HASH(ID) PARTITIONS 2"
                : $"PARTITION BY RANGE(ID) (PARTITION \"PL_{suffix}\" VALUES LESS THAN(100), PARTITION \"PH_{suffix}\" VALUES LESS THAN(MAXVALUE))";
            await ExecuteAsync(connection, $"CREATE TABLE \"{partitioned}\" (ID INT, N INT) {partition}");
            created.Add(partitioned);
            await ExecuteAsync(connection, $"CREATE TABLE \"{ordinary}\" (ID INT PRIMARY KEY)");
            created.Add(ordinary);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT TEMPORARY, PARTITIONED FROM USER_TABLES WHERE TABLE_NAME = :name";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "name";
                parameter.Value = partitioned;
                command.Parameters.Add(parameter);
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("N", reader.GetString(0));
                Assert.Equal("YES", reader.GetString(1));
            }

            var factory = CreateFactory();
            var error = Assert.Throws<NotSupportedException>(() => factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [partitioned])));
            Assert.Contains(partitioned, error.Message, StringComparison.Ordinal);
            Assert.Contains("partition definitions", error.Message, StringComparison.Ordinal);
            Assert.Equal(ordinary, Assert.Single(factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [ordinary])).Tables).Name);
        }
        finally
        {
            foreach (var table in Enumerable.Reverse(created))
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{table}\"");
            }
        }
    }

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignKeyBackingIndexesKeepTheirCatalogRole(bool physical)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var parent = $"EF10_VIP_{suffix}";
        var child = $"EF10_VIC_{suffix}";
        var foreignKey = $"EF10_VIFK_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = new List<string>();
        try
        {
            await ExecuteAsync(connection, $"CREATE TABLE \"{parent}\" (ID INT PRIMARY KEY)");
            created.Add(parent);
            var indexClause = physical ? " WITH INDEX" : "";
            await ExecuteAsync(connection, $"CREATE TABLE \"{child}\" (ID INT PRIMARY KEY, PID INT, CONSTRAINT \"{foreignKey}\" FOREIGN KEY(PID) REFERENCES \"{parent}\"(ID){indexClause})");
            created.Add(child);
            var backingIndexes = new List<string>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT I.INDEX_NAME, C.NAME, I.INDEX_TYPE FROM ALL_INDEXES I "
                    + "INNER JOIN SYS.SYSOBJECTS S ON S.NAME = I.OWNER AND S.TYPE$ = 'SCH' "
                    + "INNER JOIN SYS.SYSOBJECTS O ON O.SCHID = S.ID AND O.NAME = I.INDEX_NAME AND O.TYPE$ = 'TABOBJ' AND O.SUBTYPE$ = 'INDEX' "
                    + "LEFT JOIN SYS.SYSCONS K ON K.INDEXID = O.ID AND K.TABLEID = O.PID AND K.TYPE$ = 'F' "
                    + "LEFT JOIN SYS.SYSOBJECTS C ON C.ID = K.ID "
                    + "WHERE I.OWNER = SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) AND I.TABLE_NAME = :name AND K.ID IS NOT NULL";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "name";
                parameter.Value = child;
                command.Parameters.Add(parameter);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    backingIndexes.Add(reader.GetString(0));
                    Assert.Equal(physical ? "NORMAL" : "VIRTUAL", reader.GetString(2));
                    Assert.Equal(foreignKey, reader.IsDBNull(1) ? null : reader.GetString(1));
                }
            }

            Assert.Single(backingIndexes);
            var model = CreateFactory().Create(connection,
                new DatabaseModelFactoryOptions(tables: [parent, child]));
            var table = Assert.Single(model.Tables, table => table.Name == child);
            Assert.Equal(foreignKey, Assert.Single(table.ForeignKeys).Name);
            if (physical)
            {
                var index = Assert.Single(table.Indexes, index => backingIndexes.Contains(index.Name!));
                Assert.Equal("PID", Assert.Single(index.Columns).Name);
            }
            else
            {
                Assert.DoesNotContain(table.Indexes, index => backingIndexes.Contains(index.Name!));
            }
        }
        finally
        {
            foreach (var table in Enumerable.Reverse(created))
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{table}\"");
            }
        }
    }

    [DamengTheory]
    [InlineData("DELETE")]
    [InlineData("PRESERVE")]
    public async Task FactoryRejectsTemporaryTablesWithoutBlockingPermanentTableFilters(string duration)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var temporary = $"EF10_TMP_{suffix}";
        var permanent = $"EF10_PERM_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = new List<string>();
        try
        {
            await ExecuteAsync(connection, $"CREATE GLOBAL TEMPORARY TABLE \"{temporary}\" (ID INT) ON COMMIT {duration} ROWS");
            created.Add(temporary);
            await ExecuteAsync(connection, $"CREATE TABLE \"{permanent}\" (ID INT PRIMARY KEY)");
            created.Add(permanent);
            var factory = CreateFactory();
            var error = Assert.Throws<NotSupportedException>(() => factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [temporary])));
            Assert.Contains(temporary, error.Message, StringComparison.Ordinal);
            Assert.Contains("temporary-table lifetime", error.Message, StringComparison.Ordinal);
            Assert.Equal(permanent, Assert.Single(factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [permanent])).Tables).Name);
        }
        finally
        {
            foreach (var name in Enumerable.Reverse(created))
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{name}\"");
            }
        }
    }

    [DamengFact]
    public async Task FactoryRejectsBitmapIndexesAndStillReadsNormalIndexes()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var specialized = $"EF10_BMT_{suffix}";
        var ordinary = $"EF10_NMT_{suffix}";
        var bitmap = $"EF10_BMI_{suffix}";
        var normal = $"EF10_NMI_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = new List<string>();
        try
        {
            foreach (var table in new[] { specialized, ordinary })
            {
                await ExecuteAsync(connection, $"CREATE TABLE \"{table}\" (ID INT PRIMARY KEY, N INT)");
                created.Add(table);
            }

            await ExecuteAsync(connection, $"CREATE BITMAP INDEX \"{bitmap}\" ON \"{specialized}\"(N)");
            await ExecuteAsync(connection, $"CREATE INDEX \"{normal}\" ON \"{ordinary}\"(N DESC)");
            var factory = CreateFactory();
            var error = Assert.Throws<NotSupportedException>(() => factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [specialized])));
            Assert.Contains(bitmap, error.Message, StringComparison.Ordinal);
            Assert.Contains("BITMAP", error.Message, StringComparison.Ordinal);
            var model = factory.Create(connection, new DatabaseModelFactoryOptions(tables: [ordinary]));
            var index = Assert.Single(Assert.Single(model.Tables).Indexes);
            Assert.Equal(normal, index.Name);
            Assert.Equal("N", Assert.Single(index.Columns).Name);
            Assert.True(Assert.Single(index.IsDescending));
        }
        finally
        {
            foreach (var table in Enumerable.Reverse(created))
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{table}\"");
            }
        }
    }

    [DamengFact]
    public async Task FactoryRejectsUnrepresentableLocalSequenceWithoutBlockingOtherTables()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var sequence = $"EF10_BIGSEQ_{suffix}";
        var table = $"EF10_BIGST_{suffix}";
        var safe = $"EF10_BIGSAFE_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = new List<string>();
        var sequenceCreated = false;
        try
        {
            await ExecuteAsync(connection, $"CREATE SEQUENCE \"{sequence}\" START WITH 1 INCREMENT BY 2147483648 MAXVALUE 9223372036854775807");
            sequenceCreated = true;
            await ExecuteAsync(connection, $"CREATE TABLE \"{table}\" (ID INT PRIMARY KEY, N BIGINT DEFAULT \"{sequence}\".NEXTVAL)");
            created.Add(table);
            await ExecuteAsync(connection, $"CREATE TABLE \"{safe}\" (ID INT PRIMARY KEY)");
            created.Add(safe);

            var factory = CreateFactory();
            var error = Assert.Throws<NotSupportedException>(() => factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [table])));
            Assert.Contains(sequence, error.Message, StringComparison.Ordinal);
            Assert.Contains("cannot represent exactly", error.Message, StringComparison.Ordinal);
            Assert.Equal(safe, Assert.Single(factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [safe])).Tables).Name);
        }
        finally
        {
            foreach (var name in Enumerable.Reverse(created))
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{name}\"");
            }

            if (sequenceCreated)
            {
                await ExecuteAsync(connection, $"DROP SEQUENCE \"{sequence}\"");
            }
        }
    }

    [DamengFact]
    public async Task SequenceCatalogQueriesCrossBatchBoundariesWithoutLosingNames()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var names = Enumerable.Range(0, 1001).Select(index => $"EF10_BATCH_{suffix}_{index}").ToArray();
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = new List<string>();
        try
        {
            foreach (var index in new[] { 0, 499, 500, 1000 })
            {
                await ExecuteAsync(connection, $"CREATE SEQUENCE \"{names[index]}\" START WITH 41 INCREMENT BY 3 MAXVALUE 1000");
                created.Add(names[index]);
            }

            await using var schemaCommand = connection.CreateCommand();
            schemaCommand.CommandText = "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM dual";
            var schema = Convert.ToString(await schemaCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture)!;
            var found = new List<string>();
            var batches = 0;
            foreach (var command in DamengDatabaseModelFactory.CreateSequenceCatalogCommands(connection, schema, names.Concat(names.Take(10))))
            {
                using (command)
                {
                    batches++;
                    Assert.InRange(command.Parameters.Count, 2, 501);
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var sequence = DamengDatabaseModelFactory.ReadSequenceRecord(reader, schema);
                        found.Add(sequence.Name);
                        Assert.Equal(41L, sequence.StartValue);
                        Assert.Equal(3, sequence.IncrementBy);
                    }
                }
            }

            Assert.Equal(3, batches);
            Assert.Equal(created.Order(StringComparer.Ordinal), found.Order(StringComparer.Ordinal));
        }
        finally
        {
            foreach (var sequence in created)
            {
                await ExecuteAsync(connection, $"DROP SEQUENCE \"{sequence}\"");
            }
        }
    }

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FactoryRejectsUnrepresentedColumnGeneration(bool onUpdate)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var table = $"EF10_GEN_{suffix}";
        var safe = $"EF10_GSAFE_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = new List<string>();
        try
        {
            await ExecuteAsync(connection, $"CREATE TABLE \"{safe}\" (ID INT PRIMARY KEY, N INT NOT NULL DEFAULT 7)");
            created.Add(safe);
            var definition = onUpdate ? "N TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE NOW" : "N INT DEFAULT ON NULL 7";
            await ExecuteAsync(connection, $"CREATE TABLE \"{table}\" (ID INT PRIMARY KEY, {definition})");
            created.Add(table);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT DATA_DEFAULT FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :name AND COLUMN_NAME = 'N'";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "name";
                parameter.Value = table;
                command.Parameters.Add(parameter);
                var defaultSql = Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? "";
                Assert.DoesNotContain(onUpdate ? "ON UPDATE" : "ON NULL", defaultSql, StringComparison.OrdinalIgnoreCase);
            }

            var factory = CreateFactory();
            var error = Assert.Throws<NotSupportedException>(() => factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [table])));
            Assert.Contains(onUpdate ? "ON UPDATE" : "DEFAULT ON NULL", error.Message, StringComparison.Ordinal);
            Assert.Equal(safe, Assert.Single(factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [safe])).Tables).Name);
        }
        finally
        {
            foreach (var name in Enumerable.Reverse(created))
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{name}\"");
            }
        }
    }

    [DamengFact]
    public async Task FactoryPreservesFloatCatalogPrecisionOnRecreation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var source = $"EF10_FS_{suffix}";
        var target = $"EF10_FT_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = new List<string>();
        try
        {
            await ExecuteAsync(connection, $"CREATE TABLE \"{source}\" (ID INT PRIMARY KEY, F7 FLOAT(7), F24 FLOAT(24), F25 FLOAT(25), F53 FLOAT(53))");
            created.Add(source);
            var factory = CreateFactory();
            var table = Assert.Single(factory.Create(connection, new DatabaseModelFactoryOptions(tables: [source])).Tables);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COLUMN_NAME, DATA_PRECISION FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :name AND DATA_TYPE = 'FLOAT'";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "name";
                parameter.Value = source;
                command.Parameters.Add(parameter);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var column = Assert.Single(table.Columns, item => item.Name == reader.GetString(0));
                    Assert.Equal($"FLOAT({Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture)})", column.StoreType);
                }
            }

            var definitions = string.Join(", ", table.Columns.Select(column => $"\"{column.Name}\" {column.StoreType}"));
            await ExecuteAsync(connection, $"CREATE TABLE \"{target}\" ({definitions})");
            created.Add(target);
            var copy = Assert.Single(factory.Create(connection, new DatabaseModelFactoryOptions(tables: [target])).Tables);
            Assert.Equal(table.Columns.Select(column => column.StoreType), copy.Columns.Select(column => column.StoreType));
            Assert.Equal(await ReadNumericFacetsAsync(connection, source), await ReadNumericFacetsAsync(connection, target));
        }
        finally
        {
            foreach (var name in Enumerable.Reverse(created))
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{name}\"");
            }
        }
    }

    private static async Task<string[]> ReadNumericFacetsAsync(DbConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_NAME, DATA_TYPE, DATA_PRECISION, DATA_SCALE FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :name ORDER BY COLUMN_ID";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "name";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(string.Join(":", Enumerable.Range(0, 4).Select(i => Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture))));
        }

        Assert.Equal(5, rows.Count);
        return rows.ToArray();
    }

    [DamengFact]
    public async Task FactoryLoadsSequencesWithUnquotedDollarAndHashNames()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var sequence = $"EF10_S$Q#_{suffix}";
        var table = $"EF10_SQT_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var sequenceCreated = false;
        var tableCreated = false;
        try
        {
            await ExecuteAsync(connection, $"CREATE SEQUENCE {sequence} START WITH 41 INCREMENT BY 3 MAXVALUE 1000");
            sequenceCreated = true;
            await ExecuteAsync(connection, $"CREATE TABLE \"{table}\" (ID INT PRIMARY KEY, N INT DEFAULT {sequence}.NEXTVAL)");
            tableCreated = true;
            var model = CreateFactory().Create(connection, new DatabaseModelFactoryOptions(tables: [table]));
            var actual = Assert.Single(model.Sequences);
            Assert.Equal(sequence, actual.Name);
            Assert.Equal(41L, actual.StartValue);
            Assert.Equal(3, actual.IncrementBy);
            var column = Assert.Single(Assert.Single(model.Tables).Columns, column => column.Name == "N");
            Assert.Equal(sequence, column[DamengAnnotationNames.SequenceName]);
            Assert.Null(column.DefaultValueSql);
        }
        finally
        {
            if (tableCreated)
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{table}\"");
            }

            if (sequenceCreated)
            {
                await ExecuteAsync(connection, $"DROP SEQUENCE {sequence}");
            }
        }
    }

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FactoryRejectsNonPrimaryClustering(bool uniqueConstraint)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var table = $"EF10_NPC_{suffix}";
        var index = $"EF10_NPCI_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = false;
        try
        {
            var extra = uniqueConstraint ? $", CONSTRAINT \"{index}\" CLUSTER UNIQUE KEY(N)" : "";
            await ExecuteAsync(connection, $"CREATE TABLE \"{table}\" (ID INT NOT NULL, N INT, NOT CLUSTER PRIMARY KEY(ID){extra})");
            created = true;
            if (!uniqueConstraint)
            {
                await ExecuteAsync(connection, $"CREATE CLUSTER INDEX \"{index}\" ON \"{table}\"(N)");
            }

            var error = Assert.Throws<NotSupportedException>(() => CreateFactory().Create(
                connection, new DatabaseModelFactoryOptions(tables: [table])));
            Assert.Contains(index, error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (created)
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{table}\"");
            }
        }
    }

    [DamengTheory]
    [InlineData("check")]
    [InlineData("check_not_null")]
    [InlineData("disabled_check")]
    [InlineData("virtual")]
    public async Task FactoryRejectsUnsupportedTableSemanticsWithoutRejectingNotNull(string kind)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var safe = $"EF10_SSAFE_{suffix}";
        var table = $"EF10_SBAD_{suffix}";
        var constraint = $"EF10_SCHECK_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = new List<string>();
        try
        {
            await ExecuteAsync(connection, $"CREATE TABLE \"{safe}\" (ID INT NOT NULL PRIMARY KEY, N INT NOT NULL)");
            created.Add(safe);
            var extra = kind switch
            {
                "virtual" => "CALC AS (N + 1)",
                "check_not_null" => $"CONSTRAINT \"{constraint}\" CHECK (N IS NOT NULL)",
                _ => $"CONSTRAINT \"{constraint}\" CHECK (N > 0)"
            };
            await ExecuteAsync(connection, $"CREATE TABLE \"{table}\" (ID INT PRIMARY KEY, N INT, {extra})");
            created.Add(table);
            if (kind == "disabled_check")
            {
                await ExecuteAsync(connection, $"ALTER TABLE \"{table}\" DISABLE CONSTRAINT \"{constraint}\"");
            }

            var factory = CreateFactory();
            var error = Assert.Throws<NotSupportedException>(() => factory.Create(
                connection, new DatabaseModelFactoryOptions(tables: [table])));
            Assert.Contains(kind == "virtual" ? "virtual computed column" : "CHECK constraint", error.Message, StringComparison.Ordinal);
            var selected = Assert.Single(factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [safe])).Tables);
            Assert.Equal(safe, selected.Name);
            Assert.All(selected.Columns, column => Assert.False(column.IsNullable));
        }
        finally
        {
            foreach (var name in Enumerable.Reverse(created))
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{name}\"");
            }
        }
    }

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FactoryPreservesPrimaryKeyClusteringThroughGeneratedDdl(bool clustered)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var source = $"EF10_PKCS_{suffix}";
        var target = $"EF10_PKCT_{suffix}";
        var clause = clustered ? "CLUSTER PRIMARY KEY" : "NOT CLUSTER PRIMARY KEY";
        var lob = clustered ? "" : ", NOTE CLOB";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = new List<string>();
        try
        {
            await ExecuteAsync(connection, $"CREATE TABLE \"{source}\" (ID INT NOT NULL{lob}, {clause}(ID))");
            created.Add(source);
            var factory = CreateFactory();
            var table = Assert.Single(factory.Create(connection, new DatabaseModelFactoryOptions(tables: [source])).Tables);
            var actual = Assert.IsType<bool>(table.PrimaryKey![DamengAnnotationNames.IsClustered]);
            Assert.Equal(clustered, actual);
            await using var context = new ClusteringContext(target, actual);
            var sql = context.Database.GenerateCreateScript();
            Assert.Contains(clause, sql, StringComparison.Ordinal);
            if (clustered)
            {
                Assert.DoesNotContain("NOT CLUSTER PRIMARY KEY", sql, StringComparison.Ordinal);
            }

            await DamengScriptExecutor.ExecuteAsync(connection, sql, false, text => text);
            created.Add(target);
            var copy = Assert.Single(factory.Create(connection, new DatabaseModelFactoryOptions(tables: [target])).Tables);
            Assert.Equal(clustered, copy.PrimaryKey![DamengAnnotationNames.IsClustered]);
        }
        finally
        {
            foreach (var name in Enumerable.Reverse(created))
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{name}\"");
            }
        }
    }

    private sealed class ClusteringContext(string table, bool clustered) : DbContext
    {
        public string Table => table;
        public bool Clustered => clustered;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseDameng(DamengTestEnvironment.GetRequiredConnectionString())
                .ReplaceService<IModelCacheKeyFactory, ClusteringModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.SharedTypeEntity<Dictionary<string, object>>("Row", entity =>
            {
                entity.ToTable(table);
                entity.IndexerProperty<int>("ID").ValueGeneratedNever();
                entity.HasKey("ID").HasAnnotation(DamengAnnotationNames.IsClustered, clustered);
                if (!clustered)
                {
                    entity.IndexerProperty<string>("NOTE").HasColumnType("CLOB");
                }
            });
    }

    private sealed class ClusteringModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is ClusteringContext typed
                ? (context.GetType(), typed.Table, typed.Clustered, designTime)
                : (object)(context.GetType(), designTime);
    }

    [DamengTheory]
    [InlineData("P")]
    [InlineData("U")]
    [InlineData("R")]
    public async Task FactoryRejectsDisabledConstraintsOnlyOnSelectedTables(string kind)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var parent = $"EF10_CSP_{suffix}";
        var table = $"EF10_CST_{suffix}";
        var constraint = $"EF10_CSC_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = new List<string>();
        try
        {
            await ExecuteAsync(connection, $"CREATE TABLE \"{parent}\" (ID INT NOT NULL, NOT CLUSTER PRIMARY KEY(ID))");
            created.Add(parent);
            var definition = kind switch
            {
                "P" => "NOT CLUSTER PRIMARY KEY(ID)",
                "U" => "UNIQUE(ID)",
                _ => $"FOREIGN KEY(ID) REFERENCES \"{parent}\"(ID)"
            };
            await ExecuteAsync(connection, $"CREATE TABLE \"{table}\" (ID INT NOT NULL, CONSTRAINT \"{constraint}\" {definition})");
            created.Add(table);
            var factory = CreateFactory();
            Assert.Single(factory.Create(connection, new DatabaseModelFactoryOptions(tables: [table])).Tables);
            await ExecuteAsync(connection, $"ALTER TABLE \"{table}\" DISABLE CONSTRAINT \"{constraint}\"");

            var error = Assert.Throws<NotSupportedException>(() => factory.Create(
                connection, new DatabaseModelFactoryOptions(tables: [table])));
            Assert.Contains(constraint, error.Message, StringComparison.Ordinal);
            Assert.Contains("DISABLED", error.Message, StringComparison.Ordinal);
            Assert.Equal(parent, Assert.Single(factory.Create(
                connection, new DatabaseModelFactoryOptions(tables: [parent])).Tables).Name);

            await ExecuteAsync(connection, $"ALTER TABLE \"{table}\" ENABLE CONSTRAINT \"{constraint}\"");
            Assert.Single(factory.Create(connection, new DatabaseModelFactoryOptions(tables: [table])).Tables);
        }
        finally
        {
            foreach (var name in Enumerable.Reverse(created))
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{name}\"");
            }
        }
    }

    [DamengFact]
    public async Task FactoryNormalizesUnquotedFiltersAndPreservesQuotedCase()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var upper = $"EF10_FILTER_{suffix.ToUpperInvariant()}";
        var lower = $"ef10_quoted_{suffix}";
        var unquoted = upper.ToLowerInvariant();
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();
        await using var connection = new DmConnection(connectionString);
        await connection.OpenAsync();
        var created = new List<string>();
        try
        {
            foreach (var name in new[] { upper, lower })
            {
                await ExecuteAsync(connection, $"CREATE TABLE \"{name}\" (ID INT PRIMARY KEY)");
                created.Add(name);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM dual";
            var schema = Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture)!;
            var factory = CreateFactory();
            foreach (var filter in new[] { unquoted, schema.ToLowerInvariant() + "." + unquoted, $"\"{schema}\".{unquoted}" })
            {
                var model = factory.Create(connection, new DatabaseModelFactoryOptions(
                    tables: [filter], schemas: [schema.ToLowerInvariant()]));
                Assert.Equal(upper, Assert.Single(model.Tables).Name);
            }

            var quoted = factory.Create(connection, new DatabaseModelFactoryOptions(
                tables: [$"\"{lower}\""], schemas: [$"\"{schema}\""]));
            Assert.Equal(lower, Assert.Single(quoted.Tables).Name);
            Assert.Throws<NotSupportedException>(() => factory.Create(connection,
                new DatabaseModelFactoryOptions(schemas: [$"\"{schema.ToLowerInvariant()}\""])));
        }
        finally
        {
            foreach (var name in created)
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{name}\"");
            }
        }
    }

    [DamengFact]
    public async Task FactoryReadsTablesColumnsConstraintsIndexesCommentsAndValueGeneration()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var tableName = $"EF10_RE_{suffix}";
        var sequenceName = $"EF10_RESEQ_{suffix}";
        var primaryKeyName = $"PK_RE_{suffix}";
        var uniqueName = $"UQ_RE_{suffix}";
        var indexName = $"IDX_RE_{suffix}";
        var lowerTableName = $"ef10_relower_{suffix.ToLowerInvariant()}";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();

        await using var setup = new DmConnection(connectionString);
        await setup.OpenAsync();
        var sequenceCreated = false;
        var tableCreated = false;
        var lowerTableCreated = false;
        try
        {
            await ExecuteAsync(
                setup,
                $"CREATE SEQUENCE \"{sequenceName}\" START WITH 41 INCREMENT BY 3 MAXVALUE 1000000 CYCLE");
            sequenceCreated = true;
            await ExecuteAsync(
                setup,
                $"""
                CREATE TABLE "{tableName}" (
                    "ID" BIGINT IDENTITY(3, 2) NOT NULL,
                    "CODE" VARCHAR(30) NOT NULL,
                    "NAME" NVARCHAR2(50) NOT NULL,
                    "SEQ_NUM" INT DEFAULT "{sequenceName}".NEXTVAL,
                    "STATUS" VARCHAR(8) DEFAULT 'NEW',
                    "DURATION" INTERVAL DAY(4) TO SECOND(3),
                    "AMOUNT" DECIMAL(18, 3),
                    CONSTRAINT "{primaryKeyName}" PRIMARY KEY ("ID"),
                    CONSTRAINT "{uniqueName}" UNIQUE ("CODE", "NAME")
                )
                """);
            tableCreated = true;
            await ExecuteAsync(
                setup,
                $"CREATE INDEX \"{indexName}\" ON \"{tableName}\" (\"STATUS\" ASC, \"NAME\" DESC)");
            await ExecuteAsync(setup, $"COMMENT ON TABLE \"{tableName}\" IS '反向工程表注释'");
            await ExecuteAsync(setup, $"COMMENT ON COLUMN \"{tableName}\".\"NAME\" IS '名称注释'");

            await ExecuteAsync(
                setup,
                $"CREATE TABLE \"{lowerTableName}\" (\"id\" INT IDENTITY(7, 4) NOT NULL, CONSTRAINT \"pk_{lowerTableName}\" PRIMARY KEY (\"id\"))");
            lowerTableCreated = true;

            var factory = CreateFactory();
            DatabaseModel model;
            await using (var connection = new DmConnection(connectionString))
            {
                model = factory.Create(connection, new DatabaseModelFactoryOptions());
            }

            var currentSchema = model.DefaultSchema;
            Assert.False(string.IsNullOrEmpty(currentSchema));

            var table = Assert.Single(model.Tables, candidate => candidate.Name == tableName);
            Assert.Equal(currentSchema, table.Schema);
            Assert.Equal("反向工程表注释", table.Comment);

            Assert.Equal(
                ["ID", "CODE", "NAME", "SEQ_NUM", "STATUS", "DURATION", "AMOUNT"],
                table.Columns.Select(column => column.Name).ToArray());

            var id = table.Columns[0];
            Assert.Equal("BIGINT", id.StoreType);
            Assert.False(id.IsNullable);
            Assert.Equal(
                DamengValueGenerationStrategy.IdentityColumn,
                id[DamengAnnotationNames.ValueGenerationStrategy]);
            Assert.Equal(3L, id[DamengAnnotationNames.IdentitySeed]);
            Assert.Equal(2, id[DamengAnnotationNames.IdentityIncrement]);
            Assert.Equal(ValueGenerated.OnAdd, id.ValueGenerated);
            Assert.Null(id.DefaultValueSql);

            Assert.Equal("VARCHAR(30)", table.Columns[1].StoreType);

            var name = table.Columns[2];
            Assert.Equal("NVARCHAR2(50)", name.StoreType);
            Assert.Equal("名称注释", name.Comment);

            var sequenceColumn = table.Columns[3];
            Assert.Equal("INT", sequenceColumn.StoreType);
            Assert.Equal(
                DamengValueGenerationStrategy.Sequence,
                sequenceColumn[DamengAnnotationNames.ValueGenerationStrategy]);
            Assert.Equal(sequenceName, sequenceColumn[DamengAnnotationNames.SequenceName]);
            Assert.Null(sequenceColumn[DamengAnnotationNames.SequenceSchema]);
            Assert.Null(sequenceColumn.DefaultValueSql);
            Assert.Equal(ValueGenerated.OnAdd, sequenceColumn.ValueGenerated);

            // The referenced sequence lands in the model with its catalog facets, so a
            // recreated sequence keeps the same start/increment/min/max/cycle behavior.
            var sequence = Assert.Single(
                model.Sequences,
                candidate => candidate.Name == sequenceName);
            Assert.Equal(currentSchema, sequence.Schema);
            Assert.Equal(41L, sequence.StartValue);
            Assert.Equal(3, sequence.IncrementBy);
            Assert.Equal(1L, sequence.MinValue);
            Assert.Equal(1000000L, sequence.MaxValue);
            Assert.True(sequence.IsCyclic);

            // LAST_NUMBER is the next value to issue: after one NEXTVAL (41) the catalog
            // reports 44, and a re-scaffolded model starts the recreated sequence there.
            await ExecuteAsync(setup, $"SELECT \"{sequenceName}\".NEXTVAL FROM dual");
            DatabaseModel modelAfterConsume;
            await using (var connection = new DmConnection(connectionString))
            {
                modelAfterConsume = factory.Create(connection, new DatabaseModelFactoryOptions());
            }

            var consumedSequence = Assert.Single(
                modelAfterConsume.Sequences,
                candidate => candidate.Name == sequenceName);
            Assert.Equal(44L, consumedSequence.StartValue);

            Assert.Equal("VARCHAR(8)", table.Columns[4].StoreType);
            Assert.Equal("'NEW'", table.Columns[4].DefaultValueSql?.Trim());
            Assert.Equal("INTERVAL DAY(4) TO SECOND(3)", table.Columns[5].StoreType);
            Assert.Equal("DECIMAL(18,3)", table.Columns[6].StoreType);

            Assert.NotNull(table.PrimaryKey);
            Assert.Equal(primaryKeyName, table.PrimaryKey.Name);
            Assert.Equal(["ID"], table.PrimaryKey.Columns.Select(column => column.Name).ToArray());

            var uniqueConstraint = Assert.Single(table.UniqueConstraints);
            Assert.Equal(uniqueName, uniqueConstraint.Name);
            Assert.Equal(
                ["CODE", "NAME"],
                uniqueConstraint.Columns.Select(column => column.Name).ToArray());

            var index = Assert.Single(table.Indexes);
            Assert.Equal(indexName, index.Name);
            Assert.False(index.IsUnique);
            Assert.Equal(["STATUS", "NAME"], index.Columns.Select(column => column.Name).ToArray());
            Assert.Equal([false, true], index.IsDescending.ToArray());

            var lowerTable = Assert.Single(model.Tables, candidate => candidate.Name == lowerTableName);
            var lowerId = Assert.Single(lowerTable.Columns);
            Assert.Equal("id", lowerId.Name);
            Assert.Equal(
                DamengValueGenerationStrategy.IdentityColumn,
                lowerId[DamengAnnotationNames.ValueGenerationStrategy]);
            Assert.Equal(7L, lowerId[DamengAnnotationNames.IdentitySeed]);
            Assert.Equal(4, lowerId[DamengAnnotationNames.IdentityIncrement]);

            await using (var filtered = new DmConnection(connectionString))
            {
                var filteredModel = factory.Create(
                    filtered,
                    new DatabaseModelFactoryOptions(tables: [tableName]));
                Assert.Equal([tableName], filteredModel.Tables.Select(table => table.Name).ToArray());
            }

            await using (var otherSchema = new DmConnection(connectionString))
            {
                Assert.Throws<NotSupportedException>(
                    () => factory.Create(
                        otherSchema,
                        new DatabaseModelFactoryOptions(schemas: ["SYSDBA"])));
            }
        }
        finally
        {
            if (lowerTableCreated)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{lowerTableName}\"");
            }

            if (tableCreated)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{tableName}\"");
            }

            if (sequenceCreated)
            {
                await ExecuteAsync(setup, $"DROP SEQUENCE \"{sequenceName}\"");
            }
        }
    }

    [DamengFact]
    public async Task FactoryReadsViewsAndForeignKeys()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var parentTable = $"EF10_RP_{suffix}";
        var compositeParentTable = $"EF10_RP2_{suffix}";
        var childTable = $"EF10_RC_{suffix}";
        var viewName = $"EF10_RV_{suffix}";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();

        await using var setup = new DmConnection(connectionString);
        await setup.OpenAsync();
        var parentCreated = false;
        var compositeParentCreated = false;
        var childCreated = false;
        var viewCreated = false;
        try
        {
            await ExecuteAsync(
                setup,
                $"CREATE TABLE \"{parentTable}\" (\"ID\" INT PRIMARY KEY, \"CODE\" INT UNIQUE, \"NAME\" NVARCHAR2(20))");
            parentCreated = true;
            await ExecuteAsync(
                setup,
                $"CREATE TABLE \"{compositeParentTable}\" (\"A\" INT NOT NULL, \"B\" INT NOT NULL, CONSTRAINT \"PK_RP2_{suffix}\" PRIMARY KEY (\"A\", \"B\"))");
            compositeParentCreated = true;
            await ExecuteAsync(
                setup,
                $"""
                CREATE TABLE "{childTable}" (
                    "ID" INT PRIMARY KEY,
                    "PID_CASCADE" INT REFERENCES "{parentTable}" ("ID") ON DELETE CASCADE,
                    "PID_SETNULL" INT REFERENCES "{parentTable}" ("ID") ON DELETE SET NULL,
                    "PID_DEFAULT" INT REFERENCES "{parentTable}" ("ID"),
                    "PCODE" INT REFERENCES "{parentTable}" ("CODE") ON DELETE CASCADE,
                    "CA" INT,
                    "CB" INT,
                    CONSTRAINT "FK_RC_CMP_{suffix}" FOREIGN KEY ("CA", "CB") REFERENCES "{compositeParentTable}" ("A", "B") ON DELETE CASCADE
                )
                """);
            childCreated = true;
            await ExecuteAsync(
                setup,
                $"CREATE VIEW \"{viewName}\" AS SELECT \"ID\", \"NAME\" FROM \"{parentTable}\"");
            viewCreated = true;

            var factory = CreateFactory();
            DatabaseModel model;
            await using (var connection = new DmConnection(connectionString))
            {
                model = factory.Create(connection, new DatabaseModelFactoryOptions());
            }

            var view = Assert.Single(model.Tables, candidate => candidate.Name == viewName);
            Assert.IsType<DatabaseView>(view);
            Assert.Equal(["ID", "NAME"], view.Columns.Select(column => column.Name).ToArray());
            Assert.Equal("INT", view.Columns[0].StoreType);
            Assert.Equal("NVARCHAR2(20)", view.Columns[1].StoreType);

            var child = Assert.Single(model.Tables, candidate => candidate.Name == childTable);
            Assert.Equal(5, child.ForeignKeys.Count);

            var cascade = Assert.Single(
                child.ForeignKeys,
                foreignKey => foreignKey.Columns is [{ Name: "PID_CASCADE" }]);
            Assert.Equal(ReferentialAction.Cascade, cascade.OnDelete);
            Assert.Equal(parentTable, cascade.PrincipalTable.Name);
            Assert.Equal(["ID"], cascade.PrincipalColumns.Select(column => column.Name).ToArray());

            var setNull = Assert.Single(
                child.ForeignKeys,
                foreignKey => foreignKey.Columns is [{ Name: "PID_SETNULL" }]);
            Assert.Equal(ReferentialAction.SetNull, setNull.OnDelete);

            var noAction = Assert.Single(
                child.ForeignKeys,
                foreignKey => foreignKey.Columns is [{ Name: "PID_DEFAULT" }]);
            Assert.Equal(ReferentialAction.NoAction, noAction.OnDelete);

            var toUnique = Assert.Single(
                child.ForeignKeys,
                foreignKey => foreignKey.Columns is [{ Name: "PCODE" }]);
            Assert.Equal(
                ["CODE"],
                toUnique.PrincipalColumns.Select(column => column.Name).ToArray());

            var composite = Assert.Single(
                child.ForeignKeys,
                foreignKey => foreignKey.Name == $"FK_RC_CMP_{suffix}");
            Assert.Equal(
                ["CA", "CB"],
                composite.Columns.Select(column => column.Name).ToArray());
            Assert.Equal(
                ["A", "B"],
                composite.PrincipalColumns.Select(column => column.Name).ToArray());
            Assert.Equal(compositeParentTable, composite.PrincipalTable.Name);

            var parent = Assert.Single(model.Tables, candidate => candidate.Name == parentTable);
            Assert.Empty(parent.ForeignKeys);
        }
        finally
        {
            if (viewCreated)
            {
                await ExecuteAsync(setup, $"DROP VIEW \"{viewName}\"");
            }

            if (childCreated)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{childTable}\"");
            }

            if (compositeParentCreated)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{compositeParentTable}\"");
            }

            if (parentCreated)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{parentTable}\"");
            }
        }
    }

    [DamengFact]
    public async Task FactorySkipsCrossSchemaForeignKeysAndKeepsSelfReferences()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var otherSchema = $"EF10_RO_{suffix}";
        var sharedName = $"EF10_SHARED_{suffix}";
        var childTable = $"EF10_RX_{suffix}";
        var selfTable = $"EF10_SELF_{suffix}";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();

        await using var setup = new DmConnection(connectionString);
        await setup.OpenAsync();
        var schemaCreated = false;
        var otherTableCreated = false;
        var sharedCreated = false;
        var childCreated = false;
        var selfCreated = false;
        try
        {
            await ExecuteAsync(setup, $"CREATE SCHEMA \"{otherSchema}\"");
            schemaCreated = true;
            await ExecuteAsync(
                setup,
                $"CREATE TABLE \"{otherSchema}\".\"{sharedName}\" (\"ID\" INT PRIMARY KEY)");
            otherTableCreated = true;
            await ExecuteAsync(
                setup,
                $"CREATE TABLE \"{sharedName}\" (\"ID\" INT PRIMARY KEY)");
            sharedCreated = true;
            await ExecuteAsync(
                setup,
                $"""
                CREATE TABLE "{childTable}" (
                    "ID" INT PRIMARY KEY,
                    "PID" INT REFERENCES "{otherSchema}"."{sharedName}" ("ID")
                )
                """);
            childCreated = true;
            await ExecuteAsync(
                setup,
                $"CREATE TABLE \"{selfTable}\" (\"ID\" INT PRIMARY KEY, \"PARENT_ID\" INT REFERENCES \"{selfTable}\" (\"ID\"))");
            selfCreated = true;

            var factory = CreateFactory();
            DatabaseModel model;
            await using (var connection = new DmConnection(connectionString))
            {
                model = factory.Create(connection, new DatabaseModelFactoryOptions());
            }

            // The principal lives in another schema; the local table with the same name must
            // not become the principal, and the foreign key must be skipped entirely.
            var child = Assert.Single(model.Tables, candidate => candidate.Name == childTable);
            Assert.Empty(child.ForeignKeys);

            var self = Assert.Single(model.Tables, candidate => candidate.Name == selfTable);
            var selfReference = Assert.Single(self.ForeignKeys);
            Assert.Same(self, selfReference.PrincipalTable);
            Assert.Equal(["PARENT_ID"], selfReference.Columns.Select(column => column.Name).ToArray());
            Assert.Equal(["ID"], selfReference.PrincipalColumns.Select(column => column.Name).ToArray());
        }
        finally
        {
            if (selfCreated)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{selfTable}\"");
            }

            if (childCreated)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{childTable}\"");
            }

            if (sharedCreated)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{sharedName}\"");
            }

            if (otherTableCreated)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{otherSchema}\".\"{sharedName}\"");
            }

            if (schemaCreated)
            {
                await ExecuteAsync(setup, $"DROP SCHEMA \"{otherSchema}\"");
            }
        }
    }

    [DamengFact]
    public async Task FactoryReadsIdentityFacetsFromTheSessionSchemaNotTheLoginSchema()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var secondSchema = $"EF10_RS_{suffix}";
        var tableName = $"EF10_DUP_{suffix}";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();

        await using var setup = new DmConnection(connectionString);
        await setup.OpenAsync();
        var loginTableCreated = false;
        var schemaCreated = false;
        var sessionTableCreated = false;
        try
        {
            await ExecuteAsync(
                setup,
                $"CREATE TABLE \"{tableName}\" (\"ID\" INT IDENTITY(100, 5) NOT NULL PRIMARY KEY)");
            loginTableCreated = true;
            await ExecuteAsync(setup, $"CREATE SCHEMA \"{secondSchema}\"");
            schemaCreated = true;
            await ExecuteAsync(
                setup,
                $"CREATE TABLE \"{secondSchema}\".\"{tableName}\" (\"ID\" INT IDENTITY(3, 2) NOT NULL PRIMARY KEY)");
            sessionTableCreated = true;

            var factory = CreateFactory();

            DatabaseModel loginModel;
            await using (var connection = new DmConnection(connectionString))
            {
                loginModel = factory.Create(connection, new DatabaseModelFactoryOptions());
            }

            var loginTable = Assert.Single(
                loginModel.Tables,
                candidate => candidate.Name == tableName);
            Assert.Equal(100L, loginTable.Columns[0][DamengAnnotationNames.IdentitySeed]);
            Assert.Equal(5, loginTable.Columns[0][DamengAnnotationNames.IdentityIncrement]);

            DatabaseModel sessionModel;
            await using (var connection = new DmConnection(connectionString))
            {
                await connection.OpenAsync();
                await ExecuteAsync(connection, $"SET SCHEMA \"{secondSchema}\"");
                sessionModel = factory.Create(connection, new DatabaseModelFactoryOptions());
            }

            Assert.Equal(secondSchema, sessionModel.DefaultSchema);
            var sessionTable = Assert.Single(
                sessionModel.Tables,
                candidate => candidate.Name == tableName);
            Assert.Equal(secondSchema, sessionTable.Schema);
            Assert.Equal(3L, sessionTable.Columns[0][DamengAnnotationNames.IdentitySeed]);
            Assert.Equal(2, sessionTable.Columns[0][DamengAnnotationNames.IdentityIncrement]);
        }
        finally
        {
            if (sessionTableCreated)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{secondSchema}\".\"{tableName}\"");
            }

            if (loginTableCreated)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{tableName}\"");
            }

            if (schemaCreated)
            {
                await ExecuteAsync(setup, $"DROP SCHEMA \"{secondSchema}\"");
            }
        }
    }

    [DamengFact]
    public async Task FactoryReadsIdentityFacetsForDelimitedNamesContainingDots()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var tableName = $"EF10_WD.{suffix}";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();

        await using var setup = new DmConnection(connectionString);
        await setup.OpenAsync();
        var created = false;
        try
        {
            await ExecuteAsync(
                setup,
                $"CREATE TABLE \"{tableName}\" (\"ID\" INT IDENTITY(9, 7) NOT NULL PRIMARY KEY)");
            created = true;

            var factory = CreateFactory();
            DatabaseModel model;
            await using (var connection = new DmConnection(connectionString))
            {
                model = factory.Create(connection, new DatabaseModelFactoryOptions());
            }

            var table = Assert.Single(model.Tables, candidate => candidate.Name == tableName);
            var id = Assert.Single(table.Columns);
            Assert.Equal(
                DamengValueGenerationStrategy.IdentityColumn,
                id[DamengAnnotationNames.ValueGenerationStrategy]);
            Assert.Equal(9L, id[DamengAnnotationNames.IdentitySeed]);
            Assert.Equal(7, id[DamengAnnotationNames.IdentityIncrement]);
        }
        finally
        {
            if (created)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{tableName}\"");
            }
        }
    }

    [DamengFact]
    public async Task FactoryPreservesZeroFractionalSecondPrecision()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var tableName = $"EF10_RT_{suffix}";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();

        await using var setup = new DmConnection(connectionString);
        await setup.OpenAsync();
        var created = false;
        try
        {
            await ExecuteAsync(
                setup,
                $"""
                CREATE TABLE "{tableName}" (
                    "T0" TIME(0),
                    "T" TIME,
                    "TS0" TIMESTAMP(0),
                    "TS" TIMESTAMP,
                    "DT0" DATETIME(0),
                    "DT" DATETIME,
                    "DTZ0" DATETIME(0) WITH TIME ZONE,
                    "LTZ" TIMESTAMP WITH LOCAL TIME ZONE,
                    "LTZ0" TIMESTAMP(0) WITH LOCAL TIME ZONE,
                    "LTZ3" TIMESTAMP(3) WITH LOCAL TIME ZONE
                )
                """);
            created = true;

            var factory = CreateFactory();
            DatabaseModel model;
            await using (var connection = new DmConnection(connectionString))
            {
                model = factory.Create(connection, new DatabaseModelFactoryOptions());
            }

            var table = Assert.Single(model.Tables, candidate => candidate.Name == tableName);
            var storeTypes = table.Columns.ToDictionary(column => column.Name, column => column.StoreType, StringComparer.Ordinal);
            Assert.Equal("TIME(0)", storeTypes["T0"]);
            Assert.Equal("TIMESTAMP(0)", storeTypes["TS0"]);
            Assert.Equal("DATETIME(0)", storeTypes["DT0"]);
            Assert.Equal("DATETIME(0) WITH TIME ZONE", storeTypes["DTZ0"]);

            // The catalog reports the server's default scale for unqualified types; emitting it
            // explicitly recreates the same facet instead of depending on the default again.
            Assert.Equal("TIME(0)", storeTypes["T"]);
            Assert.Equal("TIMESTAMP(6)", storeTypes["TS"]);
            Assert.Equal("DATETIME(6)", storeTypes["DT"]);

            // LOCAL TIME ZONE columns offset DATA_SCALE by 4096 in the catalog.
            Assert.Equal("TIMESTAMP(6) WITH LOCAL TIME ZONE", storeTypes["LTZ"]);
            Assert.Equal("TIMESTAMP(0) WITH LOCAL TIME ZONE", storeTypes["LTZ0"]);
            Assert.Equal("TIMESTAMP(3) WITH LOCAL TIME ZONE", storeTypes["LTZ3"]);
        }
        finally
        {
            if (created)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{tableName}\"");
            }
        }
    }

    [DamengFact]
    public async Task FactoryMatchesDelimitedTableFiltersContainingDots()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var tableName = $"EF10_WF.{suffix}";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();
        var currentSchema = string.Empty;

        await using var setup = new DmConnection(connectionString);
        await setup.OpenAsync();
        var created = false;
        try
        {
            await ExecuteAsync(
                setup,
                $"CREATE TABLE \"{tableName}\" (\"ID\" INT NOT NULL PRIMARY KEY)");
            created = true;

            await using (var schemaCommand = setup.CreateCommand())
            {
                schemaCommand.CommandText = "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM dual";
                currentSchema = Convert.ToString(await schemaCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture)!;
            }

            var factory = CreateFactory();

            // A qualified filter whose table component is delimited and contains a dot.
            await using (var connection = new DmConnection(connectionString))
            {
                var model = factory.Create(
                    connection,
                    new DatabaseModelFactoryOptions(
                        tables: [$"{currentSchema}.\"{tableName}\""]));
                Assert.Equal([tableName], model.Tables.Select(table => table.Name).ToArray());
            }

            // The same table selected by its delimited component alone.
            await using (var connection = new DmConnection(connectionString))
            {
                var model = factory.Create(
                    connection,
                    new DatabaseModelFactoryOptions(tables: [$"\"{tableName}\""]));
                Assert.Equal([tableName], model.Tables.Select(table => table.Name).ToArray());
            }
        }
        finally
        {
            if (created)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{tableName}\"");
            }
        }
    }

    [DamengFact]
    public async Task FactoryKeepsRawDefaultForCrossSchemaSequenceReferences()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var otherSchema = $"EF10_RQ_{suffix}";
        var sequenceName = $"EF10_RQS_{suffix}";
        var tableName = $"EF10_RQT_{suffix}";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();

        await using var setup = new DmConnection(connectionString);
        await setup.OpenAsync();
        var schemaCreated = false;
        var sequenceCreated = false;
        var tableCreated = false;
        try
        {
            await ExecuteAsync(setup, $"CREATE SCHEMA \"{otherSchema}\"");
            schemaCreated = true;
            await ExecuteAsync(
                setup,
                $"CREATE SEQUENCE \"{otherSchema}\".\"{sequenceName}\" START WITH 7 INCREMENT BY 2");
            sequenceCreated = true;
            await ExecuteAsync(
                setup,
                $"CREATE TABLE \"{tableName}\" (\"ID\" INT NOT NULL PRIMARY KEY, \"N\" INT DEFAULT \"{otherSchema}\".\"{sequenceName}\".NEXTVAL)");
            tableCreated = true;

            var factory = CreateFactory();
            DatabaseModel model;
            await using (var connection = new DmConnection(connectionString))
            {
                model = factory.Create(connection, new DatabaseModelFactoryOptions());
            }

            var table = Assert.Single(model.Tables, candidate => candidate.Name == tableName);
            var column = Assert.Single(table.Columns, candidate => candidate.Name == "N");

            // The referenced sequence is outside the current-schema scope: keep the raw default
            // rather than scaffolding an EF sequence with invented facets.
            Assert.Null(column[DamengAnnotationNames.ValueGenerationStrategy]);
            Assert.Null(column[DamengAnnotationNames.SequenceName]);
            Assert.NotNull(column.DefaultValueSql);
            Assert.Contains(sequenceName, column.DefaultValueSql, StringComparison.Ordinal);
            Assert.Contains("NEXTVAL", column.DefaultValueSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(model.Sequences, candidate => candidate.Name == sequenceName);
        }
        finally
        {
            if (tableCreated)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{tableName}\"");
            }

            if (sequenceCreated)
            {
                await ExecuteAsync(setup, $"DROP SEQUENCE \"{otherSchema}\".\"{sequenceName}\"");
            }

            if (schemaCreated)
            {
                await ExecuteAsync(setup, $"DROP SCHEMA \"{otherSchema}\"");
            }
        }
    }

    [DamengTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FactoryRejectsExpressionIndexesAndPreservesFilteredOrdinaryIndexes(bool unique)
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var tableName = $"EF10_REI_{suffix}";
        var ordinary = $"EF10_REO_{suffix}";
        var plainIndexName = $"IDX_REIP_{suffix}";
        var expressionIndexName = $"IDX_REIE_{suffix}";
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = new List<string>();
        try
        {
            foreach (var table in new[] { tableName, ordinary })
            {
                await ExecuteAsync(connection, $"CREATE TABLE \"{table}\" (ID INT PRIMARY KEY, NAME VARCHAR(30))");
                created.Add(table);
            }

            await ExecuteAsync(connection, $"CREATE INDEX \"{plainIndexName}\" ON \"{ordinary}\" (NAME DESC)");
            var uniqueSql = unique ? "UNIQUE " : "";
            await ExecuteAsync(connection, $"CREATE {uniqueSql}INDEX \"{expressionIndexName}\" ON \"{tableName}\" (UPPER(NAME))");
            var factory = CreateFactory();
            var error = Assert.Throws<NotSupportedException>(() => factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [tableName])));
            Assert.Contains(tableName, error.Message, StringComparison.Ordinal);
            Assert.Contains(expressionIndexName, error.Message, StringComparison.Ordinal);
            var tableModel = Assert.Single(factory.Create(connection,
                new DatabaseModelFactoryOptions(tables: [ordinary])).Tables);
            var index = Assert.Single(tableModel.Indexes);
            Assert.Equal(plainIndexName, index.Name);
            Assert.Equal(["NAME"], index.Columns.Select(column => column.Name).ToArray());
            Assert.Equal([true], index.IsDescending.ToArray());
        }
        finally
        {
            foreach (var table in Enumerable.Reverse(created))
            {
                await ExecuteAsync(connection, $"DROP TABLE \"{table}\"");
            }
        }
    }

    private static DamengDatabaseModelFactory CreateFactory()
    {
        using var context = new DbContext(new DbContextOptionsBuilder()
            .UseDameng(DamengTestEnvironment.GetRequiredConnectionString()).Options);
        return new DamengDatabaseModelFactory(context.GetService<IRelationalTypeMappingSource>());
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
