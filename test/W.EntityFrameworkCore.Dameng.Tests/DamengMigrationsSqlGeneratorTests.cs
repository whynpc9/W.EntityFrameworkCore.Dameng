using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengMigrationsSqlGeneratorTests
{
    [Fact]
    public void InvalidPrimaryKeyClusteringAnnotationIsRejected()
    {
        using var context = CreateContext();
        var operation = new AddPrimaryKeyOperation { Table = "T", Name = "PK_T", Columns = ["ID"] };
        operation["Dameng:IsClustered"] = "yes";
        Assert.Throws<NotSupportedException>(() => GenerateSql(context, operation));
    }

    [Theory]
    [InlineData(true, "CLUSTER PRIMARY KEY")]
    [InlineData(false, "NOT CLUSTER PRIMARY KEY")]
    public void PrimaryKeyClusteringAnnotationIsExplicitInSql(bool clustered, string expected)
    {
        using var context = CreateContext();
        var operation = new AddPrimaryKeyOperation { Table = "T", Name = "PK_T", Columns = ["ID"] };
        operation["Dameng:IsClustered"] = clustered;
        var sql = GenerateSql(context, operation);
        Assert.Contains(expected + " (\"ID\")", sql, StringComparison.Ordinal);
        if (clustered)
        {
            Assert.DoesNotContain("NOT CLUSTER PRIMARY KEY", sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CreateTableGeneratesIdentityComputedAndRelationalConstraints()
    {
        using var context = CreateContext();
        var operation = new CreateTableOperation
        {
            Name = "Children",
            Schema = "app"
        };

        var id = new AddColumnOperation
        {
            Name = "Id",
            Table = operation.Name,
            Schema = operation.Schema,
            ClrType = typeof(long),
            ColumnType = "BIGINT",
            IsNullable = false
        };
        id["Dameng:ValueGenerationStrategy"] = DamengValueGenerationStrategy.IdentityColumn;
        id["Dameng:IdentitySeed"] = 10L;
        id["Dameng:IdentityIncrement"] = 2;

        operation.Columns.Add(id);
        operation.Columns.Add(
            new AddColumnOperation
            {
                Name = "ParentId",
                Table = operation.Name,
                Schema = operation.Schema,
                ClrType = typeof(long),
                ColumnType = "BIGINT",
                IsNullable = false
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Name = "Name",
                Table = operation.Name,
                Schema = operation.Schema,
                ClrType = typeof(string),
                ColumnType = "NVARCHAR2(100)",
                IsNullable = false
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Name = "NormalizedName",
                Table = operation.Name,
                Schema = operation.Schema,
                ClrType = typeof(string),
                ColumnType = "NVARCHAR2(100)",
                ComputedColumnSql = "UPPER(\"Name\")",
                IsStored = false,
                IsNullable = true
            });

        operation.PrimaryKey = new AddPrimaryKeyOperation
        {
            Name = "PK_Children",
            Table = operation.Name,
            Schema = operation.Schema,
            Columns = ["Id"]
        };
        operation.UniqueConstraints.Add(
            new AddUniqueConstraintOperation
            {
                Name = "AK_Children_Name",
                Table = operation.Name,
                Schema = operation.Schema,
                Columns = ["Name"]
            });
        operation.CheckConstraints.Add(
            new AddCheckConstraintOperation
            {
                Name = "CK_Children_Name",
                Table = operation.Name,
                Schema = operation.Schema,
                Sql = "LENGTH(\"Name\") > 0"
            });
        operation.ForeignKeys.Add(
            new AddForeignKeyOperation
            {
                Name = "FK_Children_Parents",
                Table = operation.Name,
                Schema = operation.Schema,
                Columns = ["ParentId"],
                PrincipalTable = "Parents",
                PrincipalSchema = "app",
                PrincipalColumns = ["Id"],
                OnDelete = ReferentialAction.Cascade
            });

        var sql = GenerateSql(context, operation);

        Assert.Contains("\"Id\" BIGINT IDENTITY(10,2) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"NormalizedName\" AS (UPPER(\"Name\"))", sql, StringComparison.Ordinal);
        Assert.Contains(
            "CONSTRAINT \"PK_Children\" PRIMARY KEY (\"Id\")",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CONSTRAINT \"AK_Children_Name\" UNIQUE (\"Name\")",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CONSTRAINT \"CK_Children_Name\" CHECK (LENGTH(\"Name\") > 0)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CONSTRAINT \"FK_Children_Parents\" FOREIGN KEY (\"ParentId\") "
            + "REFERENCES \"app\".\"Parents\" (\"Id\") ON DELETE CASCADE",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SequenceColumnAnnotationGeneratesDamengNextvalDefault()
    {
        using var context = CreateContext();
        var operation = new CreateTableOperation
        {
            Name = "Orders",
            Schema = "app"
        };
        var id = new AddColumnOperation
        {
            Name = "Id",
            Table = operation.Name,
            Schema = operation.Schema,
            ClrType = typeof(long),
            ColumnType = "BIGINT",
            IsNullable = false
        };
        id["Dameng:ValueGenerationStrategy"] = DamengValueGenerationStrategy.Sequence;
        id["Dameng:SequenceName"] = "OrderIds";
        id["Dameng:SequenceSchema"] = "numbers";
        operation.Columns.Add(id);

        var sql = GenerateSql(context, operation);

        Assert.Contains(
            "\"Id\" BIGINT NOT NULL DEFAULT (\"numbers\".\"OrderIds\".NEXTVAL)",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void IndexAndRenameOperationsUseDamengSyntax()
    {
        using var context = CreateContext();

        var createIndexSql = GenerateSql(
            context,
            new CreateIndexOperation
            {
                Name = "IX_Children_Name_Id",
                Schema = "app",
                Table = "Children",
                Columns = ["Name", "Id"],
                IsDescending = [false, true],
                IsUnique = true
            });
        var dropIndexSql = GenerateSql(
            context,
            new DropIndexOperation
            {
                Name = "IX_Children_Name_Id",
                Schema = "app",
                Table = "Children"
            });
        var renameIndexSql = GenerateSql(
            context,
            new RenameIndexOperation
            {
                Name = "IX_Children_Name_Id",
                NewName = "IX_Children_Name",
                Schema = "app",
                Table = "Children"
            });
        var renameTableSql = GenerateSql(
            context,
            new RenameTableOperation
            {
                Name = "Children",
                Schema = "app",
                NewName = "Dependents",
                NewSchema = "app"
            });
        var renameColumnSql = GenerateSql(
            context,
            new RenameColumnOperation
            {
                Name = "Name",
                NewName = "DisplayName",
                Table = "Dependents",
                Schema = "app"
            });
        var dropColumnAndTableSql = GenerateSql(
            context,
            new DropColumnOperation
            {
                Name = "DisplayName",
                Table = "Dependents",
                Schema = "app"
            },
            new DropTableOperation
            {
                Name = "Dependents",
                Schema = "app"
            });

        Assert.Equal(
            "CREATE UNIQUE INDEX \"IX_Children_Name_Id\" "
            + "ON \"app\".\"Children\" (\"Name\", \"Id\" DESC);\n",
            createIndexSql);
        Assert.Equal("DROP INDEX \"app\".\"IX_Children_Name_Id\";\n", dropIndexSql);
        Assert.Equal(
            "ALTER INDEX \"app\".\"IX_Children_Name_Id\" RENAME TO \"IX_Children_Name\";\n",
            renameIndexSql);
        Assert.Equal(
            "ALTER TABLE \"app\".\"Children\" RENAME TO \"Dependents\";\n",
            renameTableSql);
        Assert.Equal(
            "ALTER TABLE \"app\".\"Dependents\" RENAME COLUMN \"Name\" TO \"DisplayName\";\n",
            renameColumnSql);
        Assert.Equal(
            "ALTER TABLE \"app\".\"Dependents\" DROP COLUMN \"DisplayName\";\n"
            + "DROP TABLE \"app\".\"Dependents\";\n",
            dropColumnAndTableSql);
    }

    [Fact]
    public void SchemaAndSequenceOperationsUseDamengSyntax()
    {
        using var context = CreateContext();

        var sql = GenerateSql(
            context,
            new EnsureSchemaOperation { Name = "app" },
            new CreateSequenceOperation
            {
                Name = "OrderSequence",
                Schema = "app",
                ClrType = typeof(int),
                StartValue = 10,
                IncrementBy = 5,
                MinValue = 10,
                MaxValue = 100,
                IsCyclic = true
            },
            new AlterSequenceOperation
            {
                Name = "OrderSequence",
                Schema = "app",
                IncrementBy = 2,
                IsCyclic = false
            },
            new RestartSequenceOperation
            {
                Name = "OrderSequence",
                Schema = "app",
                StartValue = 20
            },
            new RenameSequenceOperation
            {
                Name = "OrderSequence",
                Schema = "app",
                NewName = "InvoiceSequence",
                NewSchema = "app"
            },
            new DropSequenceOperation
            {
                Name = "InvoiceSequence",
                Schema = "app"
            },
            new DropSchemaOperation { Name = "app" });

        Assert.Contains(
            "IF NOT EXISTS (" + Environment.NewLine
            + "        SELECT 1" + Environment.NewLine
            + "        FROM SYS.SYSOBJECTS" + Environment.NewLine
            + "        WHERE TYPE$ = 'SCH' AND NAME = 'app'",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "EXECUTE IMMEDIATE 'CREATE SCHEMA \"app\"';",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CREATE SEQUENCE \"app\".\"OrderSequence\" START WITH 10 "
            + "INCREMENT BY 5 MINVALUE 10 MAXVALUE 100 CYCLE;\n",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ALTER SEQUENCE \"app\".\"OrderSequence\" INCREMENT BY 2 "
            + "NOMINVALUE NOMAXVALUE NOCYCLE;\n",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ALTER SEQUENCE \"app\".\"OrderSequence\" CURRENT VALUE 20;\n",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ALTER SEQUENCE \"app\".\"OrderSequence\" RENAME TO \"InvoiceSequence\";\n",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("DROP SEQUENCE \"app\".\"InvoiceSequence\";\n", sql, StringComparison.Ordinal);
        Assert.Contains("DROP SCHEMA \"app\";\n", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" AS INT", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAndAlterColumnGenerateSequenceDefaultAndDefaultRemoval()
    {
        using var context = CreateContext();

        var addSql = GenerateSql(
            context,
            new AddColumnOperation
            {
                Name = "Number",
                Table = "Orders",
                Schema = "app",
                ClrType = typeof(long),
                ColumnType = "BIGINT",
                IsNullable = false,
                DefaultValueSql = "\"app\".\"OrderSequence\".NEXTVAL"
            });

        var alter = new AlterColumnOperation
        {
            Name = "Status",
            Table = "Orders",
            Schema = "app",
            ClrType = typeof(int),
            ColumnType = "INT",
            IsNullable = false,
            OldColumn = new AddColumnOperation
            {
                Name = "Status",
                Table = "Orders",
                Schema = "app",
                ClrType = typeof(int),
                ColumnType = "INT",
                IsNullable = false,
                DefaultValue = 1
            }
        };

        var alterSql = GenerateSql(context, alter);
        var removeSequence = new AlterColumnOperation
        {
            Name = "Number",
            Table = "Orders",
            Schema = "app",
            ClrType = typeof(long),
            ColumnType = "BIGINT",
            IsNullable = false,
            OldColumn = new AddColumnOperation
            {
                Name = "Number",
                Table = "Orders",
                Schema = "app",
                ClrType = typeof(long),
                ColumnType = "BIGINT",
                IsNullable = false
            }
        };
        removeSequence.OldColumn["Dameng:ValueGenerationStrategy"]
            = DamengValueGenerationStrategy.Sequence;
        removeSequence.OldColumn["Dameng:SequenceName"] = "OrderSequence";

        var removeSequenceSql = GenerateSql(context, removeSequence);

        Assert.Equal(
            "ALTER TABLE \"app\".\"Orders\" ADD \"Number\" BIGINT NOT NULL "
            + "DEFAULT (\"app\".\"OrderSequence\".NEXTVAL);\n",
            addSql);
        Assert.Equal(
            "ALTER TABLE \"app\".\"Orders\" ALTER COLUMN \"Status\" DROP DEFAULT;\n"
            + "ALTER TABLE \"app\".\"Orders\" MODIFY \"Status\" INT NOT NULL;\n",
            alterSql);
        Assert.Equal(
            "ALTER TABLE \"app\".\"Orders\" ALTER COLUMN \"Number\" DROP DEFAULT;\n"
            + "ALTER TABLE \"app\".\"Orders\" MODIFY \"Number\" BIGINT NOT NULL;\n",
            removeSequenceSql);
    }

    [Theory]
    [InlineData(1L, 1, 2L, 1)]
    [InlineData(1L, 1, 1L, 2)]
    public void AlterIdentitySeedOrIncrementIsRejected(
        long oldSeed,
        int oldIncrement,
        long newSeed,
        int newIncrement)
    {
        using var context = CreateContext();
        var operation = new AlterColumnOperation
        {
            Name = "Id",
            Table = "Orders",
            ClrType = typeof(long),
            ColumnType = "BIGINT",
            IsNullable = false,
            OldColumn = new AddColumnOperation
            {
                Name = "Id",
                Table = "Orders",
                ClrType = typeof(long),
                ColumnType = "BIGINT",
                IsNullable = false
            }
        };
        operation["Dameng:ValueGenerationStrategy"]
            = DamengValueGenerationStrategy.IdentityColumn;
        operation["Dameng:IdentitySeed"] = newSeed;
        operation["Dameng:IdentityIncrement"] = newIncrement;
        operation.OldColumn["Dameng:ValueGenerationStrategy"]
            = DamengValueGenerationStrategy.IdentityColumn;
        operation.OldColumn["Dameng:IdentitySeed"] = oldSeed;
        operation.OldColumn["Dameng:IdentityIncrement"] = oldIncrement;

        var exception = Assert.Throws<NotSupportedException>(
            () => GenerateSql(context, operation));

        Assert.Contains("seed or increment", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Drop and recreate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTableGeneratesCommentStatements()
    {
        using var context = CreateContext();
        var operation = new CreateTableOperation
        {
            Name = "Orders",
            Schema = "app",
            Comment = "订单表 'v2'"
        };
        operation.Columns.Add(
            new AddColumnOperation
            {
                Name = "Id",
                Table = operation.Name,
                Schema = operation.Schema,
                ClrType = typeof(long),
                ColumnType = "BIGINT",
                IsNullable = false
            });
        operation.Columns.Add(
            new AddColumnOperation
            {
                Name = "Name",
                Table = operation.Name,
                Schema = operation.Schema,
                ClrType = typeof(string),
                ColumnType = "NVARCHAR2(100)",
                IsNullable = false,
                Comment = "名称"
            });

        var sql = GenerateSql(context, operation);

        Assert.Contains(
            "COMMENT ON TABLE \"app\".\"Orders\" IS '订单表 ''v2''';\n",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "COMMENT ON COLUMN \"app\".\"Orders\".\"Name\" IS '名称';\n",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "COMMENT ON COLUMN \"app\".\"Orders\".\"Id\"",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AlterTableGeneratesCommentOnlyWhenChanged()
    {
        using var context = CreateContext();

        var changedSql = GenerateSql(
            context,
            new AlterTableOperation
            {
                Name = "Orders",
                Schema = "app",
                Comment = "新注释",
                OldTable = new CreateTableOperation { Comment = "旧注释" }
            });
        var clearedSql = GenerateSql(
            context,
            new AlterTableOperation
            {
                Name = "Orders",
                Schema = "app",
                OldTable = new CreateTableOperation { Comment = "旧注释" }
            });
        var unchangedSql = GenerateSql(
            context,
            new AlterTableOperation
            {
                Name = "Orders",
                Schema = "app",
                Comment = "同一条注释",
                OldTable = new CreateTableOperation { Comment = "同一条注释" }
            });

        Assert.Equal("COMMENT ON TABLE \"app\".\"Orders\" IS '新注释';\n", changedSql);
        Assert.Equal("COMMENT ON TABLE \"app\".\"Orders\" IS '';\n", clearedSql);
        Assert.Equal(string.Empty, unchangedSql);
    }

    [Fact]
    public void AddAndAlterColumnGenerateCommentChanges()
    {
        using var context = CreateContext();

        var addSql = GenerateSql(
            context,
            new AddColumnOperation
            {
                Name = "Note",
                Table = "Orders",
                Schema = "app",
                ClrType = typeof(string),
                ColumnType = "NVARCHAR2(100)",
                IsNullable = true,
                Comment = "备注"
            });

        var alter = new AlterColumnOperation
        {
            Name = "Note",
            Table = "Orders",
            Schema = "app",
            ClrType = typeof(string),
            ColumnType = "NVARCHAR2(100)",
            IsNullable = true,
            OldColumn = new AddColumnOperation
            {
                Name = "Note",
                Table = "Orders",
                Schema = "app",
                ClrType = typeof(string),
                ColumnType = "NVARCHAR2(100)",
                IsNullable = true,
                Comment = "旧备注"
            }
        };
        var alterSql = GenerateSql(context, alter);

        var alterUnchanged = new AlterColumnOperation
        {
            Name = "Note",
            Table = "Orders",
            Schema = "app",
            ClrType = typeof(string),
            ColumnType = "NVARCHAR2(200)",
            IsNullable = true,
            Comment = "保留备注",
            OldColumn = new AddColumnOperation
            {
                Name = "Note",
                Table = "Orders",
                Schema = "app",
                ClrType = typeof(string),
                ColumnType = "NVARCHAR2(100)",
                IsNullable = true,
                Comment = "保留备注"
            }
        };
        var alterUnchangedSql = GenerateSql(context, alterUnchanged);

        Assert.Equal(
            "ALTER TABLE \"app\".\"Orders\" ADD \"Note\" NVARCHAR2(100) NULL;\n"
            + "COMMENT ON COLUMN \"app\".\"Orders\".\"Note\" IS '备注';\n",
            addSql);
        Assert.Equal(
            "ALTER TABLE \"app\".\"Orders\" MODIFY \"Note\" NVARCHAR2(100) NULL;\n"
            + "COMMENT ON COLUMN \"app\".\"Orders\".\"Note\" IS '';\n",
            alterSql);
        Assert.Equal(
            "ALTER TABLE \"app\".\"Orders\" MODIFY \"Note\" NVARCHAR2(200) NULL;\n",
            alterUnchangedSql);
    }

    [Fact]
    public void EnsureSchemaUsesCatalogGuardAndStaysUnwrappedInIdempotentScripts()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var command = Assert.Single(
            generator.Generate([new EnsureSchemaOperation { Name = "app" }]));

        Assert.Equal(
            "BEGIN" + Environment.NewLine
            + "    IF NOT EXISTS (" + Environment.NewLine
            + "        SELECT 1" + Environment.NewLine
            + "        FROM SYS.SYSOBJECTS" + Environment.NewLine
            + "        WHERE TYPE$ = 'SCH' AND NAME = 'app'" + Environment.NewLine
            + "    ) THEN" + Environment.NewLine
            + "        EXECUTE IMMEDIATE 'CREATE SCHEMA \"app\"';" + Environment.NewLine
            + "    END IF;" + Environment.NewLine
            + "END;",
            command.CommandText);

        var idempotentCommand = Assert.Single(
            generator.Generate(
                [new EnsureSchemaOperation { Name = "app" }],
                options: MigrationsSqlGenerationOptions.Script
                    | MigrationsSqlGenerationOptions.Idempotent));

        Assert.StartsWith("BEGIN", idempotentCommand.CommandText, StringComparison.Ordinal);
        Assert.Contains(
            "EXECUTE IMMEDIATE 'CREATE SCHEMA \"app\"';",
            idempotentCommand.CommandText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "EXECUTE IMMEDIATE 'BEGIN",
            idempotentCommand.CommandText,
            StringComparison.Ordinal);
    }

    [Theory]
    // Baseline uppercase block.
    [InlineData("BEGIN\n    NULL;\nEND;")]
    // Leading whitespace/newlines and lowercase keywords are the same anonymous block.
    [InlineData("\n  \nbegin\n    null;\n  end;")]
    [InlineData("  BEGIN\n    NULL;\n  END;")]
    // DECLARE opens the block form with declarations.
    [InlineData("DECLARE\n    v INT;\nBEGIN\n    v := 1;\nEND;")]
    [InlineData("declare v int;\nbegin\n    null;\nend;")]
    [InlineData("-- ensure lookup\nbegin null; end;")]
    [InlineData("/* BEGIN ' */ /* second */ DECLARE v INT; BEGIN v := 1; END;")]
    [InlineData("-- comment\rBEGIN NULL; END;")]
    public void IdempotentGenerationPassesAnonymousBlocksThroughUnwrapped(string blockSql)
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var command = Assert.Single(
            generator.Generate(
                [new SqlOperation { Sql = blockSql, SuppressTransaction = true }],
                options: MigrationsSqlGenerationOptions.Script
                    | MigrationsSqlGenerationOptions.Idempotent));

        Assert.Equal(blockSql.TrimEnd(), command.CommandText);
    }

    [Theory]
    [InlineData("BEGINNING")]
    [InlineData("DECLARES")]
    [InlineData("/* BEGIN */ SELECT 1 FROM dual")]
    [InlineData("-- DECLARE\nSELECT 1 FROM dual")]
    public void IdempotentGenerationWrapsTextThatMerelySharesTheBlockKeywordPrefix(string sql)
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        // The keyword prefix alone must not trigger block passthrough (word boundary).
        var command = Assert.Single(
            generator.Generate(
                [new SqlOperation { Sql = sql, SuppressTransaction = true }],
                options: MigrationsSqlGenerationOptions.Script
                    | MigrationsSqlGenerationOptions.Idempotent));

        Assert.StartsWith("EXECUTE IMMEDIATE '", command.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void SeedDataOperationsGenerateInsertUpdateAndDelete()
    {
        using var context = CreateContext();

        var sql = GenerateSql(
            context,
            new InsertDataOperation
            {
                Table = "Statuses",
                Schema = "app",
                Columns = ["Id", "Name"],
                ColumnTypes = ["INT", "NVARCHAR2(20)"],
                Values = new object[,]
                {
                    { 1, "新建" },
                    { 2, "完成" }
                }
            },
            new UpdateDataOperation
            {
                Table = "Statuses",
                Schema = "app",
                KeyColumns = ["Id"],
                KeyColumnTypes = ["INT"],
                KeyValues = new object[,] { { 2 } },
                Columns = ["Name"],
                ColumnTypes = ["NVARCHAR2(20)"],
                Values = new object[,] { { "已完成" } }
            },
            new DeleteDataOperation
            {
                Table = "Statuses",
                Schema = "app",
                KeyColumns = ["Id"],
                KeyColumnTypes = ["INT"],
                KeyValues = new object[,] { { 1 } }
            });

        Assert.Contains(
            "INSERT INTO \"app\".\"Statuses\" (\"Id\", \"Name\")",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("VALUES (1, '新建')", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (2, '完成')", sql, StringComparison.Ordinal);
        Assert.Contains(
            "UPDATE \"app\".\"Statuses\" SET \"Name\" = '已完成'",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("WHERE \"Id\" = 2", sql, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM \"app\".\"Statuses\"", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE \"Id\" = 1", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void IdentitySeedDataUsesSeparateSessionCommands()
    {
        var options = new DbContextOptionsBuilder<IdentitySeedContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .Options;
        using var context = new IdentitySeedContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(source: null, model.GetRelationalModel());
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var commands = generator.Generate(operations, model);

        Assert.Contains(
            commands,
            command => command.CommandText
                == "SET IDENTITY_INSERT \"SeededIdentity\" ON;\n");
        Assert.Contains(
            commands,
            command => command.CommandText
                == "SET IDENTITY_INSERT \"SeededIdentity\" OFF;\n");
        Assert.Contains(
            commands,
            command => command.CommandText.StartsWith(
                "INSERT INTO \"SeededIdentity\"",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            commands,
            command => command.CommandText.Contains(
                    "IDENTITY_INSERT",
                    StringComparison.Ordinal)
                && command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal));

        var idempotentCommands = generator.Generate(
            operations,
            model,
            MigrationsSqlGenerationOptions.Idempotent);

        Assert.Contains(
            idempotentCommands,
            command => command.CommandText.Contains(
                "SET IDENTITY_INSERT \"SeededIdentity\" ON;",
                StringComparison.Ordinal));
        Assert.Contains(
            idempotentCommands,
            command => command.CommandText.Contains(
                "SET IDENTITY_INSERT \"SeededIdentity\" OFF;",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            idempotentCommands,
            command => command.CommandText.Contains(" ON;", StringComparison.Ordinal)
                && command.CommandText.Contains(" OFF;", StringComparison.Ordinal));
    }

    [Fact]
    public void IdempotentGenerationWrapsTheWholeCommandInEscapedDynamicSql()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var command = Assert.Single(
            generator.Generate(
                [
                    new SqlOperation
                    {
                        Sql = "UPDATE \"Statuses\" SET \"Name\" = 'O''Brien';"
                            + Environment.NewLine
                            + "INSERT INTO \"Statuses\" (\"Name\") VALUES ('新建');",
                        SuppressTransaction = true
                    }
                ],
                options: MigrationsSqlGenerationOptions.Script
                    | MigrationsSqlGenerationOptions.Idempotent));

        Assert.Equal(
            "EXECUTE IMMEDIATE 'UPDATE \"Statuses\" SET \"Name\" = ''O''''Brien'';"
            + Environment.NewLine
            + "INSERT INTO \"Statuses\" (\"Name\") VALUES (''新建'');';"
            + Environment.NewLine,
            command.CommandText);
        Assert.True(command.TransactionSuppressed);
    }

    [Fact]
    public void IdempotentGenerationRejectsCommandsAboveTheDynamicSqlLiteralLimit()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var exception = Assert.Throws<NotSupportedException>(
            () => generator.Generate(
                [new SqlOperation { Sql = new string('X', 40_000) }],
                options: MigrationsSqlGenerationOptions.Script
                    | MigrationsSqlGenerationOptions.Idempotent));

        Assert.Contains("32767-byte", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Split", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlOperationRejectsDisqlBatchTerminatorButNotSlashInsideLiteral()
    {
        using var context = CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        var exception = Assert.Throws<NotSupportedException>(
            () => generator.Generate(
                [
                    new SqlOperation
                    {
                        Sql = "BEGIN"
                            + Environment.NewLine
                            + "    NULL;"
                            + Environment.NewLine
                            + "END;"
                            + Environment.NewLine
                            + "/"
                    }
                ]));

        Assert.Contains("DIsql '/' batch terminator", exception.Message, StringComparison.Ordinal);

        var command = Assert.Single(
            generator.Generate(
                [
                    new SqlOperation
                    {
                        Sql = "INSERT INTO \"Values\" (\"Text\") VALUES ('line 1"
                            + Environment.NewLine
                            + "/"
                            + Environment.NewLine
                            + "line 3');"
                    }
                ]));

        Assert.Contains(
            "VALUES ('line 1" + Environment.NewLine + "/" + Environment.NewLine + "line 3')",
            command.CommandText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryRepositoryGeneratesDamengIdempotencyBlocks()
    {
        using var context = CreateContext();
        var historyRepository = context.GetService<IHistoryRepository>();

        var beginIfNotExists = historyRepository.GetBeginIfNotExistsScript("20260724_O'Brien");
        var beginIfExists = historyRepository.GetBeginIfExistsScript("20260724_O'Brien");

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "BEGIN",
                "    IF NOT EXISTS (",
                "        SELECT 1",
                "        FROM \"__EFMigrationsHistory\"",
                "        WHERE \"MigrationId\" = '20260724_O''Brien'",
                "    ) THEN"),
            beginIfNotExists);
        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "BEGIN",
                "    IF EXISTS (",
                "        SELECT 1",
                "        FROM \"__EFMigrationsHistory\"",
                "        WHERE \"MigrationId\" = '20260724_O''Brien'",
                "    ) THEN"),
            beginIfExists);
        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "    END IF;",
                "END;",
                "/"),
            historyRepository.GetEndIfScript());
    }

    [Fact]
    public void SqlGenerationHelperUsesImplicitDamengTransactionStart()
    {
        using var context = CreateContext();

        var sqlGenerationHelper = context.GetService<ISqlGenerationHelper>();

        Assert.Empty(sqlGenerationHelper.StartTransactionStatement);
        Assert.Equal("COMMIT;", sqlGenerationHelper.CommitTransactionStatement);
    }

    [Fact]
    public void MigratorComposesExecutableDamengIdempotentScript()
    {
        var options = new DbContextOptionsBuilder<DamengIdempotentScriptContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .Options;
        using var context = new DamengIdempotentScriptContext(options);

        var script = context.GetService<IMigrator>().GenerateScript(
            options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Contains("CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\"", script);
        Assert.Contains(
            "IF NOT EXISTS (" + Environment.NewLine
            + "        SELECT 1" + Environment.NewLine
            + "        FROM \"__EFMigrationsHistory\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "EXECUTE IMMEDIATE 'CREATE TABLE \"ScriptItems\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "EXECUTE IMMEDIATE 'INSERT INTO \"ScriptItems\" (\"Id\", \"Name\")",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "VALUES (1, ''O''''Brien'');",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            Environment.NewLine + "/" + Environment.NewLine,
            script,
            StringComparison.Ordinal);
        Assert.Contains("COMMIT;", script, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN TRANSACTION", script, StringComparison.Ordinal);
        Assert.DoesNotContain("START TRANSACTION", script, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedMigrationShapesFailExplicitly()
    {
        using var context = CreateContext();

        var filteredIndex = new CreateIndexOperation
        {
            Name = "IX_Children_Name",
            Table = "Children",
            Columns = ["Name"],
            Filter = "\"Name\" IS NOT NULL"
        };
        var storedComputed = new AddColumnOperation
        {
            Name = "NormalizedName",
            Table = "Children",
            ClrType = typeof(string),
            ColumnType = "NVARCHAR2(100)",
            ComputedColumnSql = "UPPER(\"Name\")",
            IsStored = true
        };
        var crossSchemaTable = new RenameTableOperation
        {
            Name = "Children",
            Schema = "app",
            NewName = "Children",
            NewSchema = "archive"
        };
        var crossSchemaSequence = new RenameSequenceOperation
        {
            Name = "OrderSequence",
            Schema = "app",
            NewName = "OrderSequence",
            NewSchema = "archive"
        };

        Assert.Throws<NotSupportedException>(() => GenerateSql(context, filteredIndex));
        Assert.Throws<NotSupportedException>(() => GenerateSql(context, storedComputed));
        Assert.Throws<NotSupportedException>(() => GenerateSql(context, crossSchemaTable));
        Assert.Throws<NotSupportedException>(() => GenerateSql(context, crossSchemaSequence));
    }

    private static TestContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .Options;

        return new TestContext(options);
    }

    private static string GenerateSql(
        TestContext context,
        params MigrationOperation[] operations)
        => string.Concat(
            context.GetService<IMigrationsSqlGenerator>()
                .Generate(operations)
                .Select(command => command.CommandText));

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);

    private sealed class IdentitySeedContext(DbContextOptions<IdentitySeedContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<IdentitySeedEntity>(
                entity =>
                {
                    entity.ToTable("SeededIdentity");
                    entity.HasKey(item => item.Id);
                    entity.Property(item => item.Id)
                        .UseDamengIdentityColumn();
                    entity.Property(item => item.Name)
                        .HasMaxLength(40);
                    entity.HasData(
                        new IdentitySeedEntity
                        {
                            Id = 20,
                            Name = "种子"
                        });
                });
    }

    private sealed class IdentitySeedEntity
    {
        public int Id { get; set; }

        public required string Name { get; set; }
    }
}

[DbContext(typeof(DamengIdempotentScriptContext))]
[Migration("202607240001_IdempotentScript")]
public sealed class DamengIdempotentScriptMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ScriptItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "INT", nullable: false),
                Name = table.Column<string>(type: "NVARCHAR2(100)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ScriptItems", value => value.Id));

        migrationBuilder.Operations.Add(
            new InsertDataOperation
            {
                Table = "ScriptItems",
                Columns = ["Id", "Name"],
                ColumnTypes = ["INT", "NVARCHAR2(100)"],
                Values = new object[,] { { 1, "O'Brien" } }
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "ScriptItems");
}

public sealed class DamengIdempotentScriptContext(
    DbContextOptions<DamengIdempotentScriptContext> options)
    : DbContext(options);
