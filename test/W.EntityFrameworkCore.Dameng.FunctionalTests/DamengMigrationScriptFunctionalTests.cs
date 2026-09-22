using System.Data.Common;
using System.Globalization;
using Dm;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

[Collection(DamengMigrationScriptSet.Name)]
public sealed class DamengMigrationScriptFunctionalTests
{
    private readonly DamengScriptTestDatabase _database;
    private readonly ITestOutputHelper _output;

    public DamengMigrationScriptFunctionalTests(
        DamengScriptTestDatabase database,
        ITestOutputHelper output)
    {
        _database = database;
        _output = output;
    }

    [DamengFact]
    public async Task GeneratedCreateScriptAppliesSupportedModel()
    {
        WriteServerFacts();
        var names = ScriptObjectNames.ForPrefix(NewPrefix());
        var options = new DbContextOptionsBuilder<ScriptBootstrapContext>()
            .UseDameng(_database.ConnectionString)
            .ReplaceService<IModelCacheKeyFactory, ScriptBootstrapCacheKeyFactory>()
            .Options;

        string script;
        await using (var context = new ScriptBootstrapContext(options, names))
        {
            script = context.Database.GenerateCreateScript();
        }

        Assert.Contains("IDENTITY(10,2)", script, StringComparison.Ordinal);
        Assert.Contains("NEXTVAL", script, StringComparison.Ordinal);
        Assert.Contains("UPPER(\"NAME\")", script, StringComparison.Ordinal);
        Assert.Contains("ON DELETE CASCADE", script, StringComparison.Ordinal);
        Assert.Contains("DESC", script, StringComparison.Ordinal);
        Assert.Contains("CREATE SCHEMA", script, StringComparison.Ordinal);
        Assert.Contains("SET IDENTITY_INSERT", script, StringComparison.Ordinal);
        Assert.DoesNotContain("@", script, StringComparison.Ordinal);

        await using var connection = await _database.OpenAsync();
        await DamengScriptExecutor.ExecuteAsync(connection, script, idempotent: false, _database.Redact);

        var seeded = await ReadParentAsync(connection, names.ParentTable, "NOTE", 20);
        Assert.Equal("种子", seeded.Name);
        Assert.Equal("O'Brien", seeded.Extra);

        var generatedId = await InsertParentAsync(connection, names.ParentTable, "新行");
        Assert.Equal(22L, generatedId);

        var childId = await InsertChildAsync(connection, names.ChildTable, parentId: 20);
        Assert.Equal(41L, childId);
        Assert.Equal("DAMENG", await ScalarStringAsync(
            connection,
            "SELECT \"NORMALIZED_NAME\" FROM \"" + names.ChildTable + "\" WHERE \"ID\" = :id",
            ("id", childId)));
        Assert.Equal(
            new byte[] { 0, 0xA5, 0xFF },
            await ScalarBytesAsync(
                connection,
                "SELECT \"PAYLOAD\" FROM \"" + names.ChildTable + "\" WHERE \"ID\" = :id",
                ("id", childId)));

        await ExecuteAsync(
            connection,
            "DELETE FROM \"" + names.ParentTable + "\" WHERE \"ID\" = :id",
            ("id", 20L));
        Assert.Equal(0L, await ScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM \"" + names.ChildTable + "\" WHERE \"PARENT_ID\" = :id",
            ("id", 20L)));
        Assert.Equal(1L, await ScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM \"" + names.ParentTable + "\" WHERE \"ID\" = :id",
            ("id", 22L)));

        await AssertIndexAsync(connection, names.Index, descendingColumn: "ID");
        await AssertConstraintAsync(connection, names.PrimaryKey);
        await AssertConstraintAsync(connection, names.UniqueConstraint);
        await AssertConstraintAsync(connection, names.CheckConstraint);
        await AssertConstraintAsync(connection, names.ForeignKey);
        Assert.Equal(0L, await ScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM \"" + names.Schema + "\".\"" + names.LookupTable + "\""));
        Assert.Equal(44L, await ScalarInt64Async(
            connection,
            "SELECT \"" + names.Sequence + "\".NEXTVAL FROM DUAL"));
    }

    [DamengFact]
    public async Task NonIdempotentMigrationScriptAppliesUpgrades()
    {
        var names = ScriptObjectNames.ForMigrationPrefix(NewPrefix());
        DamengScriptNames.Prefix = names.Prefix;
        string script;
        await using (var context = CreateMigrationContext(names.HistoryTable))
        {
            script = context.GetService<IMigrator>().GenerateScript();
        }

        AssertMigrationScript(script, names);
        Assert.Contains("ALTER TABLE", script, StringComparison.Ordinal);
        Assert.Contains("RENAME", script, StringComparison.Ordinal);
        Assert.Contains("INCREMENT BY 5", script, StringComparison.Ordinal);

        await using var connection = await _database.OpenAsync();
        await DamengScriptExecutor.ExecuteAsync(connection, script, idempotent: false, _database.Redact);
        await AssertUpgradedModelAsync(connection, names);

        // Altering INCREMENT BY before the first NEXTVAL makes Dameng apply the new
        // increment to the pre-start position (41 - 3), so the first value is 43.
        var childId = await InsertChildAsync(connection, names.ChildTable, parentId: 22);
        Assert.Equal(43L, childId);
        Assert.Equal(48L, await ScalarInt64Async(
            connection,
            "SELECT \"" + names.Sequence + "\".NEXTVAL FROM DUAL"));
    }

    [DamengFact]
    public async Task IdempotentMigrationScriptAppliesOnce()
    {
        var names = ScriptObjectNames.ForMigrationPrefix(NewPrefix());
        DamengScriptNames.Prefix = names.Prefix;
        string script;
        await using (var context = CreateMigrationContext(names.HistoryTable))
        {
            script = context.GetService<IMigrator>().GenerateScript(
                options: MigrationsSqlGenerationOptions.Idempotent);
        }

        Assert.Contains("EXECUTE IMMEDIATE", script, StringComparison.Ordinal);
        Assert.Contains("IF NOT EXISTS", script, StringComparison.Ordinal);
        Assert.Contains(Environment.NewLine + "/" + Environment.NewLine, script, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN TRANSACTION", script, StringComparison.Ordinal);

        await using var connection = await _database.OpenAsync();
        await DamengScriptExecutor.ExecuteAsync(connection, script, idempotent: true, _database.Redact);
        await DamengScriptExecutor.ExecuteAsync(connection, script, idempotent: true, _database.Redact);

        Assert.Equal(1L, await ScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM \"" + names.ParentTable + "\" WHERE \"ID\" = :id",
            ("id", 20L)));
        Assert.Equal(1L, await CountHistoryAsync(connection, names.HistoryTable, "202609220001_CreateScriptObjects"));
        Assert.Equal(1L, await CountHistoryAsync(connection, names.HistoryTable, "202609220002_AlterScriptObjects"));
    }

    [DamengFact]
    public async Task DatabaseMigrateAppliesUpgrades()
    {
        var names = ScriptObjectNames.ForMigrationPrefix(NewPrefix());
        DamengScriptNames.Prefix = names.Prefix;
        await using (var context = CreateMigrationContext(names.HistoryTable))
        {
            await context.Database.MigrateAsync();
        }

        await using var connection = await _database.OpenAsync();
        await AssertUpgradedModelAsync(connection, names);
    }

    private void WriteServerFacts()
    {
        _output.WriteLine("page=" + (_database.PageSize?.ToString(CultureInfo.InvariantCulture) ?? ""));
        _output.WriteLine("compatible_mode=" + _database.CompatibleMode);
        _output.WriteLine("banner=" + _database.ServerBanner);
        _output.WriteLine("instance=" + _database.InstanceVersion);
    }

    private DamengScriptMigrationContext CreateMigrationContext(string historyTable)
        => new(
            new DbContextOptionsBuilder<DamengScriptMigrationContext>()
                .UseDameng(
                    _database.ConnectionString,
                    dameng => dameng.MigrationsHistoryTable(historyTable))
                .Options);

    private static void AssertMigrationScript(string script, ScriptObjectNames names)
    {
        Assert.Contains("IDENTITY(10,2)", script, StringComparison.Ordinal);
        Assert.Contains(names.Sequence + "\".NEXTVAL", script, StringComparison.Ordinal);
        Assert.Contains("UPPER(\"NAME\")", script, StringComparison.Ordinal);
        Assert.Contains("ON DELETE CASCADE", script, StringComparison.Ordinal);
        Assert.Contains("DESC", script, StringComparison.Ordinal);
        Assert.Contains("CREATE SCHEMA \"" + names.Schema + "\"", script, StringComparison.Ordinal);
        Assert.Contains("SET IDENTITY_INSERT", script, StringComparison.Ordinal);
        Assert.DoesNotContain("@", script, StringComparison.Ordinal);
    }

    private static async Task AssertUpgradedModelAsync(DmConnection connection, ScriptObjectNames names)
    {
        var seeded = await ReadParentAsync(connection, names.ParentTable, "DISPLAY_NOTE", 20);
        Assert.Equal("种子", seeded.Name);
        Assert.Equal("O'Brien", seeded.Extra);
        Assert.Equal(1, await ScalarInt32Async(
            connection,
            "SELECT \"STATUS\" FROM \"" + names.ParentTable + "\" WHERE \"ID\" = :id",
            ("id", 20L)));

        Assert.Equal(22L, await InsertParentAsync(connection, names.ParentTable, "新行", includeStatus: true));
        Assert.Equal(1L, await CountHistoryAsync(connection, names.HistoryTable, "202609220001_CreateScriptObjects"));
        Assert.Equal(1L, await CountHistoryAsync(connection, names.HistoryTable, "202609220002_AlterScriptObjects"));
        Assert.Equal(0L, await ScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM USER_INDEXES WHERE INDEX_NAME = :name",
            ("name", names.Index)));
        await AssertIndexAsync(connection, names.RenamedIndex, descendingColumn: null);
        Assert.Equal(0L, await ScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM \"" + names.Schema + "\".\"" + names.RenamedLookupTable + "\""));
        Assert.Equal(0L, await CountColumnAsync(connection, names.RenamedLookupTable, "LABEL"));
    }

    private static async Task AssertIndexAsync(
        DmConnection connection,
        string indexName,
        string? descendingColumn)
    {
        Assert.Equal(1L, await ScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM USER_INDEXES WHERE INDEX_NAME = :name",
            ("name", indexName)));

        if (descendingColumn is null
            || await ScalarInt64Async(
                connection,
                "SELECT COUNT(*) FROM ALL_TAB_COLUMNS WHERE TABLE_NAME = 'USER_IND_COLUMNS' AND COLUMN_NAME = 'DESCEND'")
            == 0)
        {
            return;
        }

        Assert.Equal(1L, await ScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM USER_IND_COLUMNS WHERE INDEX_NAME = :name AND COLUMN_NAME = :column "
            + "AND UPPER(DESCEND) IN ('DESC', 'Y', 'YES')",
            ("name", indexName),
            ("column", descendingColumn)));
    }

    private static async Task AssertConstraintAsync(DmConnection connection, string constraintName)
        => Assert.Equal(
            1L,
            await ScalarInt64Async(
                connection,
                "SELECT COUNT(*) FROM USER_CONSTRAINTS WHERE CONSTRAINT_NAME = :name",
                ("name", constraintName)));

    private static async Task<long> CountColumnAsync(
        DmConnection connection,
        string tableName,
        string columnName)
        => await ScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :table_name AND COLUMN_NAME = :column_name",
            ("table_name", tableName),
            ("column_name", columnName));

    private static async Task<long> CountHistoryAsync(
        DmConnection connection,
        string historyTable,
        string migrationId)
        => await ScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM \"" + historyTable + "\" WHERE \"MigrationId\" = :id",
            ("id", migrationId));

    private static async Task<(string Name, string? Extra)> ReadParentAsync(
        DmConnection connection,
        string tableName,
        string extraColumn,
        long id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"NAME\", \"" + extraColumn + "\" FROM \"" + tableName + "\" WHERE \"ID\" = :id";
        AddParameter(command, "id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static async Task<long> InsertParentAsync(
        DmConnection connection,
        string tableName,
        string name,
        bool includeStatus = false)
    {
        if (includeStatus)
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO \"" + tableName + "\" (\"NAME\", \"STATUS\") VALUES (:name, :status)",
                ("name", name),
                ("status", 1));
        }
        else
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO \"" + tableName + "\" (\"NAME\") VALUES (:name)",
                ("name", name));
        }

        return await ScalarInt64Async(
            connection,
            "SELECT \"ID\" FROM \"" + tableName + "\" WHERE \"NAME\" = :name",
            ("name", name));
    }

    private static async Task<long> InsertChildAsync(
        DmConnection connection,
        string tableName,
        long parentId)
    {
        await ExecuteAsync(
            connection,
            "INSERT INTO \"" + tableName + "\" (\"PARENT_ID\", \"NAME\", \"PAYLOAD\") VALUES (:parent_id, :name, :payload)",
            ("parent_id", parentId),
            ("name", "dameng"),
            ("payload", new byte[] { 0, 0xA5, 0xFF }));

        return await ScalarInt64Async(
            connection,
            "SELECT \"ID\" FROM \"" + tableName + "\" WHERE \"NAME\" = :name",
            ("name", "dameng"));
    }

    private static async Task ExecuteAsync(
        DmConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            AddParameter(command, name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarInt64Async(
        DmConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            AddParameter(command, name, value);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<int> ScalarInt32Async(
        DmConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            AddParameter(command, name, value);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<string> ScalarStringAsync(
        DmConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            AddParameter(command, name, value);
        }

        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("Dameng returned no scalar value.");
    }

    private static async Task<byte[]> ScalarBytesAsync(
        DmConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            AddParameter(command, name, value);
        }

        var scalar = await command.ExecuteScalarAsync();
        return scalar as byte[] ?? throw new InvalidOperationException("Dameng did not return a byte array.");
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string NewPrefix()
        => Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
}

internal static class DamengScriptNames
{
    public static string Prefix { get; set; } = "";
}

internal sealed record ScriptObjectNames(
    string Prefix,
    string ParentTable,
    string ChildTable,
    string Sequence,
    string Schema,
    string LookupTable,
    string RenamedLookupTable,
    string Index,
    string RenamedIndex,
    string PrimaryKey,
    string UniqueConstraint,
    string CheckConstraint,
    string ForeignKey,
    string HistoryTable)
{
    public static ScriptObjectNames ForPrefix(string prefix)
        => new(
            prefix,
            "P_" + prefix,
            "C_" + prefix,
            "S_" + prefix,
            "A_" + prefix,
            "L_" + prefix,
            "L_" + prefix,
            "IX_" + prefix,
            "IX_" + prefix,
            "PK_" + prefix,
            "AK_" + prefix,
            "CK_" + prefix,
            "FK_" + prefix,
            "H_" + prefix);

    public static ScriptObjectNames ForMigrationPrefix(string prefix)
        => new(
            prefix,
            "MP_" + prefix,
            "MC_" + prefix,
            "MS_" + prefix,
            "MA_" + prefix,
            "ML_" + prefix,
            "MR_" + prefix,
            "IX_" + prefix,
            "IY_" + prefix,
            "PK_" + prefix,
            "AK_" + prefix,
            "CK_" + prefix,
            "FK_" + prefix,
            "H_" + prefix);
}

internal sealed class ScriptBootstrapContext(
    DbContextOptions<ScriptBootstrapContext> options,
    ScriptObjectNames names)
    : DbContext(options)
{
    public ScriptObjectNames Names { get; } = names;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(Names.Sequence)
            .StartsAt(41)
            .IncrementsBy(3)
            .HasMin(41)
            .HasMax(1000);

        modelBuilder.Entity<ScriptParent>(entity =>
        {
            entity.ToTable(Names.ParentTable);
            entity.HasKey(item => item.Id).HasName(Names.PrimaryKey);
            entity.HasAlternateKey(item => item.Name).HasName(Names.UniqueConstraint);
            entity.Property(item => item.Id)
                .HasColumnName("ID")
                .HasColumnType("BIGINT")
                .UseDamengIdentityColumn(seed: 10, increment: 2);
            entity.Property(item => item.Name)
                .HasColumnName("NAME")
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(item => item.Note)
                .HasColumnName("NOTE")
                .HasMaxLength(100);
            entity.HasData(new ScriptParent
            {
                Id = 20,
                Name = "种子",
                Note = "O'Brien"
            });
        });

        modelBuilder.Entity<ScriptChild>(entity =>
        {
            entity.ToTable(
                Names.ChildTable,
                table => table.HasCheckConstraint(Names.CheckConstraint, "LENGTH(\"NAME\") > 0"));
            entity.HasKey(item => item.Id).HasName("CPK_" + Names.Prefix);
            entity.Property(item => item.Id)
                .HasColumnName("ID")
                .HasColumnType("BIGINT")
                .UseDamengSequence(Names.Sequence);
            entity.Property(item => item.ParentId)
                .HasColumnName("PARENT_ID")
                .HasColumnType("BIGINT");
            entity.Property(item => item.Name)
                .HasColumnName("NAME")
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(item => item.NormalizedName)
                .HasColumnName("NORMALIZED_NAME")
                .HasComputedColumnSql("UPPER(\"NAME\")", stored: false);
            entity.Property(item => item.Payload)
                .HasColumnName("PAYLOAD")
                .HasMaxLength(8)
                .IsRequired();
            entity.HasIndex(item => new { item.Name, item.Id })
                .IsUnique()
                .IsDescending(false, true)
                .HasDatabaseName(Names.Index);
            entity.HasOne<ScriptParent>()
                .WithMany()
                .HasForeignKey(item => item.ParentId)
                .HasPrincipalKey(item => item.Id)
                .HasConstraintName(Names.ForeignKey)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScriptLookup>(entity =>
        {
            entity.ToTable(Names.LookupTable, Names.Schema);
            entity.HasKey(item => item.Id).HasName("LPK_" + Names.Prefix);
            entity.Property(item => item.Id)
                .HasColumnName("ID")
                .HasColumnType("INT")
                .ValueGeneratedNever();
            entity.Property(item => item.Label)
                .HasColumnName("LABEL")
                .HasMaxLength(20)
                .IsRequired();
        });
    }
}

internal sealed class ScriptBootstrapCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
        => context is ScriptBootstrapContext bootstrap
            ? (context.GetType(), bootstrap.Names.Prefix, designTime)
            : (context.GetType(), designTime);
}

internal sealed class ScriptParent
{
    public long Id { get; set; }

    public string Name { get; set; } = "";

    public string? Note { get; set; }
}

internal sealed class ScriptChild
{
    public long Id { get; set; }

    public long ParentId { get; set; }

    public string Name { get; set; } = "";

    public string? NormalizedName { get; set; }

    public byte[] Payload { get; set; } = [];
}

internal sealed class ScriptLookup
{
    public int Id { get; set; }

    public string Label { get; set; } = "";
}

public sealed class DamengScriptMigrationContext(DbContextOptions<DamengScriptMigrationContext> options)
    : DbContext(options);

[DbContext(typeof(DamengScriptMigrationContext))]
[Migration("202609220001_CreateScriptObjects")]
public sealed class CreateScriptObjectsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var names = ScriptObjectNames.ForMigrationPrefix(DamengScriptNames.Prefix);
        migrationBuilder.Operations.Add(new EnsureSchemaOperation { Name = names.Schema });
        migrationBuilder.CreateSequence<long>(
            name: names.Sequence,
            startValue: 41,
            incrementBy: 3,
            minValue: 41,
            maxValue: 1000,
            cyclic: false);

        migrationBuilder.Operations.Add(CreateParentTable(names));
        migrationBuilder.InsertData(
            table: names.ParentTable,
            columns: ["ID", "NAME", "NOTE"],
            columnTypes: ["BIGINT", "NVARCHAR2(64)", "NVARCHAR2(100)"],
            values: new object[] { 20L, "种子", "O'Brien" });
        migrationBuilder.Operations.Add(CreateChildTable(names));
        migrationBuilder.Operations.Add(new CreateIndexOperation
        {
            Name = names.Index,
            Table = names.ChildTable,
            Columns = ["NAME", "ID"],
            IsUnique = true,
            IsDescending = [false, true]
        });
        migrationBuilder.Operations.Add(CreateLookupTable(names));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Down is not covered by the script execution test.");

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
        => ScriptMigrationModel.Build(modelBuilder, altered: false);

    private static CreateTableOperation CreateParentTable(ScriptObjectNames names)
    {
        var id = Column("ID", "BIGINT", nullable: false);
        id["Dameng:ValueGenerationStrategy"] = DamengValueGenerationStrategy.IdentityColumn;
        id["Dameng:IdentitySeed"] = 10L;
        id["Dameng:IdentityIncrement"] = 2;

        var table = new CreateTableOperation
        {
            Name = names.ParentTable,
            PrimaryKey = Key(names.PrimaryKey, names.ParentTable, "ID")
        };
        table.Columns.Add(id);
        table.Columns.Add(Column("NAME", "NVARCHAR2(64)", nullable: false));
        table.Columns.Add(Column("NOTE", "NVARCHAR2(100)", nullable: true));
        table.UniqueConstraints.Add(new AddUniqueConstraintOperation
        {
            Name = names.UniqueConstraint,
            Table = names.ParentTable,
            Columns = ["NAME"]
        });
        return table;
    }

    private static CreateTableOperation CreateChildTable(ScriptObjectNames names)
    {
        var id = Column("ID", "BIGINT", nullable: false);
        id["Dameng:ValueGenerationStrategy"] = DamengValueGenerationStrategy.Sequence;
        id["Dameng:SequenceName"] = names.Sequence;

        var table = new CreateTableOperation
        {
            Name = names.ChildTable,
            PrimaryKey = Key("CPK_" + names.Prefix, names.ChildTable, "ID")
        };
        table.Columns.Add(id);
        table.Columns.Add(Column("PARENT_ID", "BIGINT", nullable: false));
        table.Columns.Add(Column("NAME", "NVARCHAR2(100)", nullable: false));
        table.Columns.Add(new AddColumnOperation
        {
            Name = "NORMALIZED_NAME",
            Table = names.ChildTable,
            ClrType = typeof(string),
            ColumnType = "NVARCHAR2(100)",
            IsNullable = true,
            ComputedColumnSql = "UPPER(\"NAME\")",
            IsStored = false
        });
        table.Columns.Add(new AddColumnOperation
        {
            Name = "PAYLOAD",
            Table = names.ChildTable,
            ClrType = typeof(byte[]),
            ColumnType = "VARBINARY(8)",
            IsNullable = false
        });
        table.CheckConstraints.Add(new AddCheckConstraintOperation
        {
            Name = names.CheckConstraint,
            Table = names.ChildTable,
            Sql = "LENGTH(\"NAME\") > 0"
        });
        table.ForeignKeys.Add(new AddForeignKeyOperation
        {
            Name = names.ForeignKey,
            Table = names.ChildTable,
            Columns = ["PARENT_ID"],
            PrincipalTable = names.ParentTable,
            PrincipalColumns = ["ID"],
            OnDelete = ReferentialAction.Cascade
        });
        return table;
    }

    private static CreateTableOperation CreateLookupTable(ScriptObjectNames names)
    {
        var table = new CreateTableOperation
        {
            Name = names.LookupTable,
            Schema = names.Schema,
            PrimaryKey = Key("LPK_" + names.Prefix, names.LookupTable, "ID", names.Schema)
        };
        table.Columns.Add(Column("ID", "INT", nullable: false));
        table.Columns.Add(Column("LABEL", "NVARCHAR2(20)", nullable: false));
        return table;
    }

    private static AddColumnOperation Column(string name, string columnType, bool nullable)
        => new()
        {
            Name = name,
            ClrType = columnType == "BIGINT" ? typeof(long) : columnType == "INT" ? typeof(int) : typeof(string),
            ColumnType = columnType,
            IsNullable = nullable
        };

    private static AddPrimaryKeyOperation Key(
        string name,
        string table,
        string column,
        string? schema = null)
        => new()
        {
            Name = name,
            Table = table,
            Schema = schema,
            Columns = [column]
        };
}

[DbContext(typeof(DamengScriptMigrationContext))]
[Migration("202609220002_AlterScriptObjects")]
public sealed class AlterScriptObjectsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var names = ScriptObjectNames.ForMigrationPrefix(DamengScriptNames.Prefix);
        migrationBuilder.RenameColumn(
            name: "NOTE",
            table: names.ParentTable,
            newName: "DISPLAY_NOTE");
        migrationBuilder.AddColumn<int>(
            name: "STATUS",
            table: names.ParentTable,
            type: "INT",
            nullable: false,
            defaultValue: 1);
        migrationBuilder.RenameIndex(
            name: names.Index,
            newName: names.RenamedIndex,
            table: names.ChildTable);
        migrationBuilder.AlterSequence(
            name: names.Sequence,
            incrementBy: 5,
            minValue: 41,
            maxValue: 1000,
            cyclic: false,
            oldIncrementBy: 3,
            oldMinValue: 41,
            oldMaxValue: 1000,
            oldCyclic: false);
        migrationBuilder.DropColumn(
            name: "LABEL",
            schema: names.Schema,
            table: names.LookupTable);
        migrationBuilder.RenameTable(
            name: names.LookupTable,
            schema: names.Schema,
            newName: names.RenamedLookupTable);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Down is not covered by the script execution test.");

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
        => ScriptMigrationModel.Build(modelBuilder, altered: true);
}

internal static class ScriptMigrationModel
{
    public static void Build(ModelBuilder modelBuilder, bool altered)
    {
        var names = ScriptObjectNames.ForMigrationPrefix(DamengScriptNames.Prefix);
        modelBuilder.HasAnnotation("ProductVersion", "10.0.12");
        modelBuilder.HasSequence<long>(names.Sequence)
            .StartsAt(41)
            .IncrementsBy(altered ? 5 : 3)
            .HasMin(41)
            .HasMax(1000);

        modelBuilder.Entity(
            "ScriptParent",
            entity =>
            {
                entity.ToTable(names.ParentTable);
                entity.Property<long>("ID").HasColumnType("BIGINT").UseDamengIdentityColumn(10, 2);
                entity.Property<string>("NAME").HasMaxLength(64).IsRequired();
                entity.Property<string>(altered ? "DISPLAY_NOTE" : "NOTE").HasMaxLength(100);
                if (altered)
                {
                    entity.Property<int>("STATUS").HasColumnType("INT").HasDefaultValue(1);
                }

                entity.HasKey("ID").HasName(names.PrimaryKey);
                entity.HasAlternateKey("NAME").HasName(names.UniqueConstraint);
            });

        modelBuilder.Entity(
            "ScriptChild",
            entity =>
            {
                entity.ToTable(names.ChildTable);
                entity.Property<long>("ID")
                    .HasColumnType("BIGINT")
                    .UseDamengSequence(names.Sequence);
                entity.Property<long>("PARENT_ID").HasColumnType("BIGINT");
                entity.Property<string>("NAME").HasMaxLength(100).IsRequired();
                entity.Property<string>("NORMALIZED_NAME")
                    .HasComputedColumnSql("UPPER(\"NAME\")", stored: false);
                entity.Property<byte[]>("PAYLOAD").HasMaxLength(8).IsRequired();
                entity.HasKey("ID");
                entity.HasIndex("NAME", "ID").IsUnique().HasDatabaseName(altered ? names.RenamedIndex : names.Index);
                entity.HasOne("ScriptParent")
                    .WithMany()
                    .HasForeignKey("PARENT_ID")
                    .HasConstraintName(names.ForeignKey)
                    .OnDelete(DeleteBehavior.Cascade);
            });

        modelBuilder.Entity(
            "ScriptLookup",
            entity =>
            {
                entity.ToTable(altered ? names.RenamedLookupTable : names.LookupTable, names.Schema);
                entity.Property<int>("ID").HasColumnType("INT").ValueGeneratedNever();
                if (!altered)
                {
                    entity.Property<string>("LABEL").HasMaxLength(20).IsRequired();
                }

                entity.HasKey("ID");
            });
    }
}
