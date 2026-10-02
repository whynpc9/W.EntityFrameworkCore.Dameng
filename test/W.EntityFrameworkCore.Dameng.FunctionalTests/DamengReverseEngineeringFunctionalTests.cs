using System.Data.Common;
using System.Globalization;
using Dm;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
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
            var factory = new DamengDatabaseModelFactory();
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
            var factory = new DamengDatabaseModelFactory();
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

            var factory = new DamengDatabaseModelFactory();
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

            var factory = new DamengDatabaseModelFactory();
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

            var factory = new DamengDatabaseModelFactory();
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

            var factory = new DamengDatabaseModelFactory();

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

            var factory = new DamengDatabaseModelFactory();
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

            var factory = new DamengDatabaseModelFactory();
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

            var factory = new DamengDatabaseModelFactory();

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

            var factory = new DamengDatabaseModelFactory();
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

    [DamengFact]
    public async Task FactoryDropsExpressionIndexesWhole()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var tableName = $"EF10_REI_{suffix}";
        var plainIndexName = $"IDX_REIP_{suffix}";
        var expressionIndexName = $"IDX_REIE_{suffix}";
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();

        await using var setup = new DmConnection(connectionString);
        await setup.OpenAsync();
        var created = false;
        try
        {
            await ExecuteAsync(
                setup,
                $"CREATE TABLE \"{tableName}\" (\"ID\" INT NOT NULL PRIMARY KEY, \"NAME\" VARCHAR(30))");
            created = true;
            await ExecuteAsync(
                setup,
                $"CREATE INDEX \"{plainIndexName}\" ON \"{tableName}\" (\"NAME\" DESC)");
            await ExecuteAsync(
                setup,
                $"CREATE INDEX \"{expressionIndexName}\" ON \"{tableName}\" (UPPER(\"NAME\"))");

            var factory = new DamengDatabaseModelFactory();
            DatabaseModel model;
            await using (var connection = new DmConnection(connectionString))
            {
                model = factory.Create(connection, new DatabaseModelFactoryOptions());
            }

            // The catalog reports the expression index row with the base column name and
            // COLUMN_POSITION -1 (plus an unreliable DESCEND), so the index is dropped whole
            // instead of scaffolding a bogus descending column index.
            var table = Assert.Single(model.Tables, candidate => candidate.Name == tableName);
            var index = Assert.Single(table.Indexes);
            Assert.Equal(plainIndexName, index.Name);
            Assert.Equal(["NAME"], index.Columns.Select(column => column.Name).ToArray());
            Assert.Equal([true], index.IsDescending.ToArray());
        }
        finally
        {
            if (created)
            {
                await ExecuteAsync(setup, $"DROP TABLE \"{tableName}\"");
            }
        }
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
