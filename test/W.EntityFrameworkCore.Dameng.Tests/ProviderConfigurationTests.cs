using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class ProviderConfigurationTests
{
    [Fact]
    public void ProviderSetsDamengIdentifierLengthLimit()
    {
        using var context = new TestContext(
            new DbContextOptionsBuilder<TestContext>()
                .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
                .Options);

        Assert.Equal(128, context.Model.GetMaxIdentifierLength());
    }

    [Fact]
    public void UseDamengRegistersTheProviderAndConnection()
    {
        var options = new DbContextOptionsBuilder<TestContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .Options;

        using var context = new TestContext(options);

        Assert.Equal(
            "W.EntityFrameworkCore.Dameng",
            context.Database.ProviderName);
        Assert.IsType<DmConnection>(context.Database.GetDbConnection());
    }

    [Fact]
    public void SqlGenerationUsesColonParametersAndQuotedIdentifiers()
    {
        var options = new DbContextOptionsBuilder<TestContext>()
            .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
            .Options;

        using var context = new TestContext(options);
        var sql = context.Entities
            .Where(entity => entity.Name == "达梦")
            .OrderBy(entity => entity.Id)
            .Skip(1)
            .Take(2)
            .ToQueryString();

        Assert.Contains("\"Entities\"", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET :p ROWS FETCH NEXT :p1 ROWS ONLY", sql, StringComparison.Ordinal);
        Assert.Contains("-- :p='1'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionsBuilderExposesStandardRelationalSettings()
    {
        var options = new DbContextOptionsBuilder<TestContext>()
            .UseDameng(
                "Server=localhost;Port=5236;User=test;Password=test",
                dameng => dameng
                    .CommandTimeout(30)
                    .MigrationsAssembly(typeof(TestContext).Assembly.FullName))
            .Options;

        var relational = options.Extensions.OfType<RelationalOptionsExtension>().SingleOrDefault();

        Assert.NotNull(relational);
        Assert.Equal(30, relational.CommandTimeout);
        Assert.Equal(typeof(TestContext).Assembly.FullName, relational.MigrationsAssembly);
    }

    [Fact]
    public void ExistingConnectionOverloadsAcceptProviderOptionsWithoutOwnershipFlag()
    {
        using var typedConnection = new DmConnection();
        var typedOptions = new DbContextOptionsBuilder<TestContext>()
            .UseDameng(
                typedConnection,
                dameng => dameng.CommandTimeout(17))
            .Options;

        var typedRelational = typedOptions.Extensions
            .OfType<RelationalOptionsExtension>()
            .Single();

        Assert.Same(typedConnection, typedRelational.Connection);
        Assert.Equal(17, typedRelational.CommandTimeout);

        using var untypedConnection = new DmConnection();
        var untypedBuilder = new DbContextOptionsBuilder();
        untypedBuilder.UseDameng(
            untypedConnection,
            dameng => dameng.CommandTimeout(19));

        var untypedRelational = untypedBuilder.Options.Extensions
            .OfType<RelationalOptionsExtension>()
            .Single();

        Assert.Same(untypedConnection, untypedRelational.Connection);
        Assert.Equal(19, untypedRelational.CommandTimeout);
    }

    [Fact]
    public void DamengAndSqlServerPublicApisCoexistWithoutExtensionAmbiguity()
    {
        using var damengContext = new DamengPublicApiContext(
            new DbContextOptionsBuilder<DamengPublicApiContext>()
                .UseDameng("Server=localhost;Port=5236;User=test;Password=test")
                .Options);
        using var sqlServerContext = new SqlServerPublicApiContext(
            new DbContextOptionsBuilder<SqlServerPublicApiContext>()
                .UseSqlServer("Server=localhost;Database=test;Trusted_Connection=True")
                .Options);

        var damengId = damengContext.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(DamengIdentityEntity))!
            .FindProperty(nameof(DamengIdentityEntity.Id))!;
        var sqlServerId = sqlServerContext.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(SqlServerIdentityEntity))!
            .FindProperty(nameof(SqlServerIdentityEntity.Id))!;

        Assert.Equal(
            DamengValueGenerationStrategy.IdentityColumn,
            damengId.GetDamengValueGenerationStrategy());
        Assert.Equal(7L, damengId.GetDamengIdentitySeed());
        Assert.Equal(3, damengId.GetDamengIdentityIncrement());
        Assert.Equal(
            SqlServerValueGenerationStrategy.IdentityColumn,
            sqlServerId.GetValueGenerationStrategy());
        Assert.Equal(11L, sqlServerId.GetIdentitySeed());
        Assert.Equal(5, sqlServerId.GetIdentityIncrement());
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<TestEntity> Entities => Set<TestEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<TestEntity>(
                entity =>
                {
                    entity.ToTable("Entities");
                    entity.HasKey(item => item.Id);
                    entity.Property(item => item.Name).HasMaxLength(200);
                });
    }

    private sealed class TestEntity
    {
        public long Id { get; set; }

        public required string Name { get; set; }
    }

    private sealed class DamengPublicApiContext(
        DbContextOptions<DamengPublicApiContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseDamengIdentityColumns();
            modelBuilder.Entity<DamengIdentityEntity>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).UseDamengIdentityColumn(7, 3);
            });
        }
    }

    private sealed class SqlServerPublicApiContext(
        DbContextOptions<SqlServerPublicApiContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseIdentityColumns();
            modelBuilder.Entity<SqlServerIdentityEntity>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).UseIdentityColumn(11, 5);
            });
        }
    }

    private sealed class DamengIdentityEntity
    {
        public long Id { get; set; }
    }

    private sealed class SqlServerIdentityEntity
    {
        public long Id { get; set; }
    }
}
