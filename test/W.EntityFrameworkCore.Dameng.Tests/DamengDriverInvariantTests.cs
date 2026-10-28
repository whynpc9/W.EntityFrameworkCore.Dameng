#pragma warning disable EF1001
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Dm;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Infrastructure;
using W.EntityFrameworkCore.Dameng.Infrastructure.Internal;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

[Trait("AuditPlan", "009")]
public sealed class DamengDriverInvariantTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RuntimeConnectionRejectsForeignDriverBeforeMutation(bool owns)
    {
        using var original = new MemoryConnection();
        var builder = new DbContextOptionsBuilder();
        DamengDriverConfiguration.UseDriver(builder, MemoryDriver.Instance, original, owns, null);
        using (var context = new DbContext(builder.Options))
        {
            original.Open();
            using var foreign = new SqlConnection();
            var strategy = context.Database.CreateExecutionStrategy().GetType();
            var error = Assert.Throws<ArgumentException>(() => context.Database.SetDbConnection(foreign, !owns));
            Assert.Equal("Dameng:ForeignConnection", error.Data["Dameng:ErrorId"]);
            Assert.Same(original, context.Database.GetDbConnection());
            Assert.Equal(ConnectionState.Open, original.State);
            Assert.Equal(0, original.Disposals);
            Assert.Equal(strategy, context.Database.CreateExecutionStrategy().GetType());
            original.Close();
        }
        Assert.Equal(owns ? 1 : 0, original.Disposals);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RuntimeConnectionPreservesOwnershipAndNullSemantics(bool owns)
    {
        using var original = new MemoryConnection();
        using var replacement = new MemoryConnection();
        var builder = new DbContextOptionsBuilder();
        DamengDriverConfiguration.UseDriver(builder, MemoryDriver.Instance, original, owns, null);
        using (var context = new DbContext(builder.Options))
        {
            context.Database.SetDbConnection(original, owns);
            Assert.Equal(0, original.Disposals);
            context.Database.OpenConnection();
            if (owns) Assert.Throws<InvalidOperationException>(() => context.Database.SetDbConnection(replacement, owns));
            Assert.Same(original, context.Database.GetDbConnection());
            context.Database.CloseConnection();
            context.Database.SetDbConnection(replacement, owns);
            Assert.Equal(owns ? 1 : 0, original.Disposals);
            context.Database.SetDbConnection(null);
            Assert.Null(context.Database.GetConnectionString());
            Assert.Equal(owns ? 1 : 0, replacement.Disposals);
            context.Database.SetConnectionString("memory");
            Assert.IsType<MemoryConnection>(context.Database.GetDbConnection());
        }
    }

    [Fact]
    public void OfficialTemporalMappingsRejectForeignParameters()
    {
        using var context = new DbContext(new DbContextOptionsBuilder().UseDameng("Server=localhost").Options);
        var source = context.GetService<IRelationalTypeMappingSource>();
        foreach (var (type, store, value) in TemporalCases())
        {
            var mapping = source.FindMapping(type, store)!;
            foreach (var variant in new[] { mapping, mapping.WithPrecisionAndScale(3, null) })
            foreach (var input in new[] { value, null })
            {
                using var foreign = new SqlCommand();
                var error = Assert.Throws<InvalidOperationException>(() => variant.CreateParameter(foreign, "p", input));
                Assert.Equal("Dameng:ForeignParameter", error.Data["Dameng:ErrorId"]);
                using var official = new DmCommand();
                Assert.IsType<DmParameter>(variant.CreateParameter(official, "p", input));
            }
        }
        var parameter = new DmParameter { Precision = 4, Scale = 2 };
        context.GetService<DamengDriver>().ConfigureTemporalParameter(parameter, 7);
        Assert.Equal(4, parameter.Precision);
        Assert.Equal(2, parameter.Scale);
    }

    [Fact]
    public void SameDriverExplicitModelRemainsUsable()
    {
        var options = new DbContextOptionsBuilder<InvariantContext>().UseDameng("Server=localhost").Options;
        using var source = new InvariantContext(options);
        var annotations = source.Model.GetAnnotations().Select(a => (a.Name, a.Value)).ToArray();
        using var target = new InvariantContext(new DbContextOptionsBuilder<InvariantContext>()
            .UseDameng("Server=other").UseModel(source.Model).Options);
        Assert.Contains("SELECT", target.Set<Row>().Select(r => r.Offset).ToQueryString());
        var value = new DateTime(2026, 1, 1);
        Assert.Contains(":", target.Set<Row>().Where(r => r.When == value).ToQueryString());
        Assert.Equal(annotations, target.Model.GetAnnotations().Select(a => (a.Name, a.Value)).ToArray());
    }

    private static IEnumerable<(Type Type, string Store, object Value)> TemporalCases()
    {
        yield return (typeof(DateTime), "TIMESTAMP(6)", new DateTime(2026, 1, 1));
        yield return (typeof(DateTime), "DATE", new DateTime(2026, 1, 1));
        yield return (typeof(DateTimeOffset), "TIMESTAMP(6) WITH TIME ZONE", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        yield return (typeof(TimeOnly), "TIME(6)", new TimeOnly(1, 2, 3));
        yield return (typeof(TimeSpan), "TIME(6)", TimeSpan.FromSeconds(1));
    }

    private sealed class InvariantContext(DbContextOptions<InvariantContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Row>();
    }
    private sealed class Row
    {
        public int Id { get; set; }
        public DateTime When { get; set; }
        public DateTimeOffset Offset { get; set; }
    }

    private sealed class MemoryDriver : DamengDriver
    {
        public static MemoryDriver Instance { get; } = new();
        public override string Name => "InvariantMemory";
        public override string ProviderName => "W.EntityFrameworkCore.Dameng.Tests";
        public override MethodInfo UseProviderMethod => typeof(DamengDbContextOptionsBuilderExtensions).GetMethods().First();
        public override DbConnection CreateConnection(string connectionString) => new MemoryConnection();
        public override bool IsDriverConnection(DbConnection connection) => connection is MemoryConnection;
        public override void SetParameterType(DbParameter parameter, DamengParameterType type) => throw new NotSupportedException();
    }
    private sealed class MemoryConnection : DbConnection
    {
        private ConnectionState _state;
        public int Disposals { get; private set; }
        [AllowNull] public override string ConnectionString { get; set; } = "memory";
        public override string Database => "memory";
        public override string DataSource => "memory";
        public override string ServerVersion => "1";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Open() => _state = ConnectionState.Open;
        public override void Close() => _state = ConnectionState.Closed;
        protected override void Dispose(bool disposing) { if (disposing) { Disposals++; Close(); } base.Dispose(disposing); }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
