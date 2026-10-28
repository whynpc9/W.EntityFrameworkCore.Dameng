using Microsoft.EntityFrameworkCore;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengLobGuardTranslationTests
{
    [Fact]
    public void DistinctSetOperationsRejectLobOnEitherSide()
    {
        using var context = Create();
        for (var operation = 0; operation < 3; operation++)
        for (var side = 0; side < 2; side++)
        for (var binary = 0; binary < 2; binary++)
        {
            var left = side == 0; var right = side == 1;
            var error = binary == 0
                ? Record.Exception(() => Set(Text(context, left), Text(context, right), operation).ToQueryString())
                : Record.Exception(() => Set(Bytes(context, left), Bytes(context, right), operation).ToQueryString());
            Assert.NotNull(error);
            Assert.Contains(operation switch { 0 => "UNION", 1 => "INTERSECT", _ => "EXCEPT" }, error.ToString(), StringComparison.Ordinal);
            Assert.Contains(binary == 0 ? "NCLOB" : "BLOB", error.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OrderedStringAggregatesRejectBinaryLobKeys()
    {
        using var context = Create();
        for (var concat = 0; concat < 2; concat++)
        for (var descending = 0; descending < 2; descending++)
        for (var secondary = 0; secondary < 2; secondary++)
        {
            var error = Record.Exception(() => Aggregate(context, concat != 0, descending != 0, secondary != 0, true).ToQueryString());
            Assert.NotNull(error);
            Assert.Contains("order by a LOB", error.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SupportedSetAndAggregateShapesRemainTranslatable()
    {
        using var context = Create();
        for (var operation = 0; operation < 3; operation++)
        {
            Assert.Contains("SELECT", Set(Text(context, false), Text(context, false), operation).ToQueryString(), StringComparison.Ordinal);
            Assert.Contains("SELECT", Set(Bytes(context, false), Bytes(context, false), operation).ToQueryString(), StringComparison.Ordinal);
        }
        Assert.Contains("UNION ALL", Text(context, true).Concat(Text(context, true)).ToQueryString(), StringComparison.Ordinal);
        Assert.Contains("UNION ALL", Bytes(context, true).Concat(Bytes(context, true)).ToQueryString(), StringComparison.Ordinal);
        Assert.Contains("LISTAGG(", Aggregate(context, false, false, false, false).ToQueryString(), StringComparison.Ordinal);
        Assert.Contains("LISTAGG(", Aggregate(context, true, true, true, false).ToQueryString(), StringComparison.Ordinal);
    }

    private static IQueryable<T> Set<T>(IQueryable<T> left, IQueryable<T> right, int operation)
        => operation switch { 0 => left.Union(right), 1 => left.Intersect(right), _ => left.Except(right) };
    private static IQueryable<string> Text(Context context, bool lob)
        => lob ? context.Rows.Select(r => r.LongText) : context.Rows.Select(r => r.ShortText);
    private static IQueryable<byte[]> Bytes(Context context, bool lob)
        => lob ? context.Rows.Select(r => r.LargeBinary) : context.Rows.Select(r => r.SmallBinary);
    private static IQueryable<string> Aggregate(Context context, bool concat, bool descending, bool secondary, bool lob)
    {
        if (!lob)
            return concat ? context.Rows.GroupBy(r => r.GroupId).Select(g => string.Concat(g.OrderBy(r => r.Id).Select(r => r.ShortText)))
                : context.Rows.GroupBy(r => r.GroupId).Select(g => string.Join("|", g.OrderBy(r => r.Id).Select(r => r.ShortText)));
        if (secondary)
            return descending
                ? concat ? context.Rows.GroupBy(r => r.GroupId).Select(g => string.Concat(g.OrderBy(r => r.GroupId).ThenByDescending(r => r.LargeBinary).Select(r => r.ShortText)))
                    : context.Rows.GroupBy(r => r.GroupId).Select(g => string.Join("|", g.OrderBy(r => r.GroupId).ThenByDescending(r => r.LargeBinary).Select(r => r.ShortText)))
                : concat ? context.Rows.GroupBy(r => r.GroupId).Select(g => string.Concat(g.OrderBy(r => r.GroupId).ThenBy(r => r.LargeBinary).Select(r => r.ShortText)))
                    : context.Rows.GroupBy(r => r.GroupId).Select(g => string.Join("|", g.OrderBy(r => r.GroupId).ThenBy(r => r.LargeBinary).Select(r => r.ShortText)));
        return descending
            ? concat ? context.Rows.GroupBy(r => r.GroupId).Select(g => string.Concat(g.OrderByDescending(r => r.LargeBinary).Select(r => r.ShortText)))
                : context.Rows.GroupBy(r => r.GroupId).Select(g => string.Join("|", g.OrderByDescending(r => r.LargeBinary).Select(r => r.ShortText)))
            : concat ? context.Rows.GroupBy(r => r.GroupId).Select(g => string.Concat(g.OrderBy(r => r.LargeBinary).Select(r => r.ShortText)))
                : context.Rows.GroupBy(r => r.GroupId).Select(g => string.Join("|", g.OrderBy(r => r.LargeBinary).Select(r => r.ShortText)));
    }
    private static Context Create() => new(new DbContextOptionsBuilder<Context>()
        .UseDameng("Server=localhost;User=test;Password=test").Options);
    private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Row>(e => {
                e.ToTable("LOB_GUARD_CASES"); e.Property(r => r.Id).ValueGeneratedNever();
                e.Property(r => r.ShortText).HasMaxLength(200);
                e.Property(r => r.LongText).HasColumnType("NCLOB");
                e.Property(r => r.SmallBinary).HasMaxLength(16);
                e.Property(r => r.LargeBinary).HasColumnType("BLOB");
            });
    }
    private sealed class Row
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public string ShortText { get; set; } = "";
        public string LongText { get; set; } = "";
        public byte[] SmallBinary { get; set; } = [];
        public byte[] LargeBinary { get; set; } = [];
    }
}
