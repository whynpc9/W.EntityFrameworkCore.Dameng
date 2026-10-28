#pragma warning disable EF1001
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using W.EntityFrameworkCore.Dameng.Storage.Internal;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

[Trait("AuditPlan", "011")]
public sealed class DamengDatabaseCreatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12)]
    public async Task HasTablesInterpretsZeroAndPositiveCounts(int count)
    {
        using var fixture = new Fixture();
        fixture.Command.Result.TrySetResult(count);
        Assert.Equal(count > 0, fixture.Creator.HasTables());
        Assert.Equal(count > 0, await fixture.Creator.HasTablesAsync());
        Assert.Equal(1, fixture.Command.SyncCalls); Assert.Equal(1, fixture.Command.AsyncCalls);
        Assert.Equal(0, fixture.Connection.RawCalls);
    }

    [Fact]
    public async Task HasTablesAsyncReturnsBeforeControlledCommandCompletes()
    {
        using var fixture = new Fixture();
        fixture.Command.RejectSync = true;
        var pending = fixture.Creator.HasTablesAsync();
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, fixture.Command.SyncCalls); Assert.Equal(1, fixture.Command.AsyncCalls);
        fixture.Command.Result.TrySetResult(1);
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, fixture.Connection.RawCalls);
    }

    [Fact]
    public async Task HasTablesAsyncPassesCancellationAndPreservesTheOriginalToken()
    {
        using (var fixture = new Fixture())
        {
            using var canceled = new CancellationTokenSource(); await canceled.CancelAsync();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Creator.HasTablesAsync(canceled.Token));
            Assert.Equal(canceled.Token, error.CancellationToken);
            Assert.Equal(0, fixture.Command.AsyncCalls); Assert.Equal(0, fixture.Command.SyncCalls);
            Assert.Equal(0, fixture.Builder.BuildCalls); Assert.Equal(0, fixture.Connection.RawCalls);
        }
        using (var fixture = new Fixture())
        {
            using var cancel = new CancellationTokenSource();
            var pending = fixture.Creator.HasTablesAsync(cancel.Token);
            Assert.False(pending.IsCompleted);
            Assert.Equal(cancel.Token, fixture.Command.Token);
            await cancel.CancelAsync();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(cancel.Token, error.CancellationToken);
            Assert.Equal(0, fixture.Command.SyncCalls); Assert.Equal(0, fixture.Connection.RawCalls);
        }
    }

    [Fact]
    public async Task HasTablesUsesTheCurrentContextConnectionAndCommandDiagnostics()
    {
        using var fixture = new Fixture();
        fixture.Command.Result.TrySetResult(1);
        foreach (var async in new[] { false, true })
        {
            Assert.True(async ? await fixture.Creator.HasTablesAsync() : fixture.Creator.HasTables());
            var parameters = fixture.Command.LastParameters;
            Assert.Same(fixture.Connection, parameters.Connection);
            Assert.Same(fixture.Context, parameters.Context);
            Assert.Same(fixture.Dependencies.CommandLogger, parameters.Logger);
            Assert.Equal(CommandSource.Migrations, parameters.CommandSource);
            Assert.Null(parameters.ParameterValues); Assert.Null(parameters.ReaderColumns);
            Assert.Equal("SELECT COUNT(*) FROM USER_TABLES", fixture.Builder.LastSql);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _services;
        public DbContext Context { get; } = new(new DbContextOptionsBuilder().UseDameng("Server=unused").Options);
        public MemoryRelationalConnection Connection { get; }
        public RelationalDatabaseCreatorDependencies Dependencies { get; }
        public ControlledCommand Command { get; }
        public ControlledBuilder Builder { get; }
        public DamengDatabaseCreator Creator { get; }
        public Fixture()
        {
            Connection = new MemoryRelationalConnection(Context.GetService<RelationalConnectionDependencies>());
            Dependencies = Context.GetService<RelationalDatabaseCreatorDependencies>() with { Connection = Connection };
            Command = new ControlledCommand(Context.GetService<RelationalCommandBuilderDependencies>());
            Builder = new ControlledBuilder(Command);
            _services = new ServiceCollection().AddSingleton<IRawSqlCommandBuilder>(Builder).BuildServiceProvider();
            // Baseline has one constructor parameter, fixed code additionally resolves the controlled builder.
            Creator = ActivatorUtilities.CreateInstance<DamengDatabaseCreator>(_services, Dependencies);
        }
        public void Dispose() { Connection.Dispose(); Context.Dispose(); _services.Dispose(); }
    }
    private sealed class ControlledBuilder(ControlledCommand command) : IRawSqlCommandBuilder
    {
        public int BuildCalls { get; private set; }
        public string? LastSql { get; private set; }
        public IRelationalCommand Build(string sql) { BuildCalls++; LastSql = sql; return command; }
        public RawSqlCommand Build(string sql, IEnumerable<object?> parameters) => throw new NotSupportedException();
        public RawSqlCommand Build(string sql, IEnumerable<object?> parameters, IModel? model) => throw new NotSupportedException();
    }
    private sealed class ControlledCommand(RelationalCommandBuilderDependencies dependencies)
        : RelationalCommand(dependencies, "controlled", "controlled", [])
    {
        public TaskCompletionSource<object?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SyncCalls { get; private set; }
        public int AsyncCalls { get; private set; }
        public bool RejectSync { get; set; }
        public CancellationToken Token { get; private set; }
        public RelationalCommandParameterObject LastParameters { get; private set; }
        public override object? ExecuteScalar(RelationalCommandParameterObject parameterObject)
        {
            SyncCalls++; LastParameters = parameterObject;
            if (RejectSync || !Result.Task.IsCompletedSuccessfully) throw new InvalidOperationException("Unexpected synchronous scalar execution.");
            return Result.Task.GetAwaiter().GetResult();
        }
        public override async Task<object?> ExecuteScalarAsync(RelationalCommandParameterObject parameterObject, CancellationToken cancellationToken = default)
        {
            AsyncCalls++; LastParameters = parameterObject; Token = cancellationToken;
            return await Result.Task.WaitAsync(cancellationToken);
        }
    }
    private sealed class MemoryRelationalConnection(RelationalConnectionDependencies dependencies) : RelationalConnection(dependencies)
    {
        public int RawCalls { get; set; }
        protected override DbConnection CreateDbConnection() => new MemoryConnection(this);
    }
    private sealed class MemoryConnection(MemoryRelationalConnection owner) : DbConnection
    {
        private ConnectionState _state;
        [AllowNull] public override string ConnectionString { get; set; } = "memory";
        public override string Database => "memory";
        public override string DataSource => "memory";
        public override string ServerVersion => "1";
        public override ConnectionState State => _state;
        public override void Open() => _state = ConnectionState.Open;
        public override void Close() => _state = ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new RawSentinelCommand(owner);
    }
    private sealed class RawSentinelCommand(MemoryRelationalConnection owner) : DbCommand
    {
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
        public override void Cancel() => throw new NotSupportedException();
        public override void Prepare() => throw new NotSupportedException();
        public override object ExecuteScalar() { owner.RawCalls++; throw new InvalidOperationException("Raw ADO sentinel: EF command chain was bypassed."); }
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }
}
