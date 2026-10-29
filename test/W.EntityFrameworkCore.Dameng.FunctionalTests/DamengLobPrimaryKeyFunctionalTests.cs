using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

[Trait("Category", "LobPrimaryKey")]
public sealed class DamengLobPrimaryKeyFunctionalTests(ITestOutputHelper output)
{
    [DamengTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public Task ExplicitNonclusteredServerControlRoundtrips(int kind)
        => DamengLobPrimaryKeyHarness.Roundtrip(kind, "raw", false, false, output);

    [DamengTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public Task UnannotatedServerDdlMatchesRecordedProfileOutcome(int kind)
        => DamengLobPrimaryKeyHarness.Roundtrip(kind, "raw", null, false, output);

    [DamengTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public Task ExistingGeneratedLobPathMatchesRecordedProfileContract(int kind)
        => DamengLobPrimaryKeyHarness.Roundtrip(kind, "script", null, false, output);

    [DamengTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public Task ExistingExplicitClusteredPathMatchesRecordedProfileContract(int kind)
        => DamengLobPrimaryKeyHarness.Roundtrip(kind, "manual", true, false, output);

    [DamengTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public Task ExistingMigrationPathRetainsServerProtection(int kind)
        => DamengLobPrimaryKeyHarness.Roundtrip(kind, "diff", null, false, output);

    [DamengTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public Task SharedTableGeneratedLobPathMatchesRecordedProfileContract(int kind)
        => DamengLobPrimaryKeyHarness.Roundtrip(kind, "shared", null, false, output);

    [DamengTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public Task AddPrimaryKeyRetainsVerifiedExistingProtection(int kind)
        => DamengLobPrimaryKeyHarness.Roundtrip(kind, "addpk", null, false, output);
}

// The recorded DM 8.1.5.60 server keeps LOB primary-key backing indexes NORMAL,
// even for an explicit CLUSTER request. SQL and annotations remain unmodified.
internal static class DamengLobPrimaryKeyHarness
{
    internal static async Task VerifyProfile(DbConnection connection, ITestOutputHelper output)
    {
        Assert.Equal("pk-cluster-1", Environment.GetEnvironmentVariable("DAMENG_TEST_PROFILE"));
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING")));
        Assert.Equal(1, Convert.ToInt32(await Scalar(connection,
            "SELECT PARA_VALUE FROM V$DM_INI WHERE PARA_NAME='PK_WITH_CLUSTER'"), CultureInfo.InvariantCulture));
        output.WriteLine("Server=" + connection.ServerVersion + "; PK_WITH_CLUSTER=1; page="
            + Convert.ToString(await Scalar(connection, "SELECT PAGE() FROM dual"), CultureInfo.InvariantCulture));
    }

    internal static async Task Roundtrip(int kind, string path, bool? clustered, bool profile, ITestOutputHelper output)
    {
        var tableName = "EF10_LP_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var connectionString = DamengTestEnvironment.GetRequiredConnectionString();
        await using var connection = new DmConnection(connectionString);
        await connection.OpenAsync();
        if (profile) await VerifyProfile(connection, output); // before any DDL, for every matrix entry
        Assert.False(await Exists(connection, tableName));
        using var context = new LobContext(new DbContextOptionsBuilder<LobContext>()
            .UseDamengUnderTest(connectionString).ReplaceService<IModelCacheKeyFactory, CacheKey>().Options,
            tableName, kind, path == "shared", clustered);
        var model = context.GetService<IDesignTimeModel>().Model;
        var relational = model.GetRelationalModel();
        var table = Assert.Single(relational.Tables);
        var payload = table.FindColumn("Payload")!;
        var expectedType = kind switch { 0 or 3 => "NCLOB", 1 or 4 => "BLOB", 2 => "CLOB", _ => "NVARCHAR2(40)" };
        Assert.Equal(expectedType, payload.StoreType);
        Assert.Equal(clustered, table.PrimaryKey!["Dameng:IsClustered"]);
        if (path == "shared") Assert.Equal(typeof(Part), Assert.Single(payload.PropertyMappings).Property.DeclaringType.ClrType);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        try
        {
            if (path == "raw")
            {
                var clusterSql = clustered is false ? "NOT CLUSTER " : clustered is true ? "CLUSTER " : "";
                await Execute(connection, $"CREATE TABLE \"{tableName}\" (\"Id\" INT NOT NULL, \"Payload\" {expectedType}, CONSTRAINT \"PK_{tableName}\" {clusterSql}PRIMARY KEY(\"Id\"))");
            }
            else if (path is "script" or "shared")
            {
                var sql = context.Database.GenerateCreateScript();
                AssertClusteringSql(sql, clustered);
                foreach (var statement in DamengScriptExecutor.SplitStatements(sql)) await Execute(connection, statement);
            }
            else
            {
                var operation = new CreateTableOperation { Name = tableName, PrimaryKey = new AddPrimaryKeyOperation { Table = tableName, Name = "PK_" + tableName, Columns = ["Id"] } };
                foreach (var column in table.Columns)
                    operation.Columns.Add(new AddColumnOperation { Table = tableName, Name = column.Name,
                        ColumnType = column.StoreType, ClrType = column.PropertyMappings[0].Property.ClrType,
                        IsNullable = column.IsNullable });
                if (clustered.HasValue) operation.PrimaryKey["Dameng:IsClustered"] = clustered.Value;
                IReadOnlyList<MigrationCommand> commands;
                if (path == "diff")
                {
                    var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, relational);
                    commands = generator.Generate(operations, model);
                }
                else if (path == "addpk")
                {
                    var key = operation.PrimaryKey;
                    operation.PrimaryKey = null;
                    foreach (var command in generator.Generate([operation], model)) await Execute(connection, command.CommandText);
                    commands = generator.Generate([key], model);
                }
                else commands = generator.Generate([operation], model);
                Assert.All(commands, command => Assert.True(command.TransactionSuppressed));
                AssertClusteringSql(string.Concat(commands.Select(c => c.CommandText)), clustered);
                foreach (var command in commands) await Execute(connection, command.CommandText);
            }
            Assert.True(await Exists(connection, tableName));
            var expectedIndex = kind == 5 && clustered is not false ? "CLUSTER" : "NORMAL";
            Assert.Equal(expectedIndex, await IndexType(connection, tableName));
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(expectedType)!;
            var binary = kind is 1 or 4;
            object initial = binary ? Enumerable.Range(0, 8193).Select(i => (byte)(i % 251)).ToArray()
                : kind == 5 ? "bounded" : "中文" + new string('x', 4097);
            if (kind == 2) initial = new string('c', 4099);
            object changed = binary ? new byte[] { 0, 1, 127, 255 } : "updated";
            await Write(connection, mapping, $"INSERT INTO \"{tableName}\" (\"Id\",\"Payload\") VALUES(1,:payload)", initial);
            await Write(connection, mapping, $"INSERT INTO \"{tableName}\" (\"Id\",\"Payload\") VALUES(2,:payload)", null);
            await AssertValue(connection, tableName, 1, initial, binary);
            await AssertValue(connection, tableName, 2, null, binary);
            await Write(connection, mapping, $"UPDATE \"{tableName}\" SET \"Payload\"=:payload WHERE \"Id\"=1", changed);
            await AssertValue(connection, tableName, 1, changed, binary);
            await Write(connection, mapping, $"UPDATE \"{tableName}\" SET \"Payload\"=:payload WHERE \"Id\"=2", changed);
            await AssertValue(connection, tableName, 2, changed, binary);
            await Write(connection, mapping, $"UPDATE \"{tableName}\" SET \"Payload\"=:payload WHERE \"Id\"=1", null);
            await AssertValue(connection, tableName, 1, null, binary);
            var duplicate = await Assert.ThrowsAnyAsync<DbException>(() => Write(connection, mapping,
                $"INSERT INTO \"{tableName}\" (\"Id\",\"Payload\") VALUES(2,:payload)", null));
            Assert.Contains("唯一", duplicate.Message, StringComparison.Ordinal);
            Assert.Equal(2, Convert.ToInt32(await Scalar(connection, $"SELECT COUNT(*) FROM \"{tableName}\""), CultureInfo.InvariantCulture));
            await AssertValue(connection, tableName, 2, changed, binary);
            Assert.Equal(expectedIndex, await IndexType(connection, tableName));
            output.WriteLine($"kind={kind}; path={path}; explicit={clustered?.ToString() ?? "unset"}; store={expectedType}; index={expectedIndex}; NULL/nonempty/update/duplicate verified.");
        }
        finally { await Cleanup(connectionString, tableName, output); }
    }

    private static void AssertClusteringSql(string sql, bool? clustered)
    {
        if (clustered is null) Assert.DoesNotContain("CLUSTER PRIMARY KEY", sql, StringComparison.Ordinal);
        else Assert.Contains(clustered.Value ? " CLUSTER PRIMARY KEY" : "NOT CLUSTER PRIMARY KEY", sql, StringComparison.Ordinal);
    }
    private static async Task Cleanup(string connectionString, string tableName, ITestOutputHelper output)
    {
        await using var cleanup = new DmConnection(connectionString);
        await cleanup.OpenAsync();
        if (await Exists(cleanup, tableName)) await Execute(cleanup, $"DROP TABLE \"{tableName}\"");
        Assert.False(await Exists(cleanup, tableName));
        output.WriteLine("Exact owned table absence confirmed by a fresh connection.");
    }
    private static async Task AssertValue(DbConnection connection, string tableName, int id, object? expected, bool binary)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"Payload\" FROM \"{tableName}\" WHERE \"Id\"={id}";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        if (expected is null) Assert.True(reader.IsDBNull(0));
        else if (binary) Assert.Equal((byte[])expected, reader.GetFieldValue<byte[]>(0));
        else Assert.Equal((string)expected, reader.GetString(0));
        Assert.False(await reader.ReadAsync());
    }
    private static async Task Write(DbConnection connection, RelationalTypeMapping mapping, string sql, object? value)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(mapping.CreateParameter(command, "payload", value, nullable: true));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }
    private static async Task<string> IndexType(DbConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT I.INDEX_TYPE FROM ALL_CONSTRAINTS C JOIN ALL_INDEXES I ON I.OWNER=C.OWNER AND I.INDEX_NAME=C.INDEX_NAME WHERE C.OWNER=SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) AND C.CONSTRAINT_TYPE='P' AND C.TABLE_NAME=:table";
        var parameter = command.CreateParameter(); parameter.ParameterName = "table"; parameter.Value = tableName; command.Parameters.Add(parameter);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }
    private static async Task<bool> Exists(DbConnection connection, string tableName)
        => Convert.ToInt32(await Scalar(connection, "SELECT COUNT(*) FROM ALL_TABLES WHERE OWNER=SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) AND TABLE_NAME='" + tableName + "'"), CultureInfo.InvariantCulture) != 0;
    private static async Task<object?> Scalar(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 15;
        return await command.ExecuteScalarAsync();
    }
    private static async Task Execute(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 15;
        await command.ExecuteNonQueryAsync();
    }
    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
        {
            var c = (LobContext)context;
            return (context.GetType(), c.TableName, c.Kind, c.Shared, c.Clustered, designTime);
        }
    }
    private sealed class LobContext(DbContextOptions<LobContext> options, string tableName, int kind, bool shared, bool? clustered) : DbContext(options)
    {
        public string TableName => tableName;
        public int Kind => kind;
        public bool Shared => shared;
        public bool? Clustered => clustered;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var owner = modelBuilder.Entity<Owner>(); owner.ToTable(tableName);
            owner.Property(e => e.Id).ValueGeneratedNever();
            var key = owner.HasKey(e => e.Id);
            if (clustered.HasValue) key.HasAnnotation("Dameng:IsClustered", clustered.Value);
            if (shared)
            {
                owner.Ignore(e => e.Text); owner.Ignore(e => e.Bytes);
                var part = modelBuilder.Entity<Part>(); part.ToTable(tableName);
                part.Property(e => e.Id).ValueGeneratedNever(); part.HasKey(e => e.Id);
                owner.HasOne(e => e.Part).WithOne().HasForeignKey<Part>(e => e.Id);
                owner.Navigation(e => e.Part).IsRequired();
                if (kind == 4) { part.Ignore(e => e.Text); part.Property(e => e.Bytes).HasColumnName("Payload").HasColumnType("BLOB"); }
                else { part.Ignore(e => e.Bytes); part.Property(e => e.Text).HasColumnName("Payload").HasColumnType(kind == 2 ? "CLOB" : "NCLOB"); }
            }
            else
            {
                owner.Ignore(e => e.Part);
                if (kind is 1 or 4)
                {
                    owner.Ignore(e => e.Text); var property = owner.Property(e => e.Bytes).HasColumnName("Payload");
                    if (kind == 4) property.HasColumnType("BLOB");
                }
                else
                {
                    owner.Ignore(e => e.Bytes); var property = owner.Property(e => e.Text).HasColumnName("Payload");
                    if (kind >= 2) property.HasColumnType(kind == 2 ? "CLOB" : kind == 3 ? "NCLOB" : "NVARCHAR2(40)");
                }
            }
        }
    }
    private sealed class Owner { public int Id { get; set; } public string? Text { get; set; } public byte[]? Bytes { get; set; } public Part? Part { get; set; } }
    private sealed class Part { public int Id { get; set; } public string? Text { get; set; } public byte[]? Bytes { get; set; } }
}
