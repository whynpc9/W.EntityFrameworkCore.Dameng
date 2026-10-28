#pragma warning disable EF1001
using System.Data.Common;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.WDriver.Tests;

[Trait("AuditPlan", "009")]
public sealed class WDriverInvariantTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RuntimeConnectionRejectsOtherDriverInBothDirections(bool w)
    {
        using DbConnection original = w ? (DbConnection)new W.Dm.DmConnection() : new global::Dm.DmConnection();
        using DbConnection foreign = w ? (DbConnection)new global::Dm.DmConnection() : new W.Dm.DmConnection();
        var builder = new DbContextOptionsBuilder();
        if (w) builder.UseDamengWithWDriver(original); else builder.UseDameng(original);
        using var context = new DbContext(builder.Options);
        var error = Assert.Throws<ArgumentException>(() => context.Database.SetDbConnection(foreign, true));
        Assert.Equal("Dameng:ForeignConnection", error.Data["Dameng:ErrorId"]);
        Assert.Same(original, context.Database.GetDbConnection());
        Assert.Equal(System.Data.ConnectionState.Closed, original.State);
        context.Database.SetDbConnection(original);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TemporalMappingsRejectForeignParametersIncludingNullAndClone(bool w)
    {
        using var context = new DbContext(Options(w).Options);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var cases = new (Type, string, object)[]
        {
            (typeof(DateTime), "TIMESTAMP(6)", new DateTime(2026, 1, 1)),
            (typeof(DateTimeOffset), "TIMESTAMP(6) WITH TIME ZONE", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            (typeof(TimeOnly), "TIME(6)", new TimeOnly(1, 2, 3)),
            (typeof(TimeSpan), "TIME(6)", TimeSpan.FromSeconds(1))
        };
        foreach (var (type, store, value) in cases)
        {
            var mapping = source.FindMapping(type, store)!;
            foreach (var variant in new[] { mapping, mapping.WithPrecisionAndScale(3, null) })
            foreach (var input in new[] { value, null })
            {
                using DbCommand foreign = w ? (DbCommand)new global::Dm.DmCommand() : new W.Dm.DmCommand();
                var error = Assert.Throws<InvalidOperationException>(() => variant.CreateParameter(foreign, "p", input));
                Assert.Equal("Dameng:ForeignParameter", error.Data["Dameng:ErrorId"]);
                using DbCommand own = w ? (DbCommand)new W.Dm.DmCommand() : new global::Dm.DmCommand();
                var parameter = variant.CreateParameter(own, "p", input);
                if (w) { Assert.Equal(0, parameter.Precision); Assert.Equal(variant.Precision ?? 6, parameter.Scale); }
            }
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void ExplicitModelRejectsOtherDriverForReadAndWrite(bool sourceW, bool neutral)
    {
        using var source = new InvariantContext(Options(sourceW).Options, neutral);
        var model = source.Model;
        foreach (var operation in new[] { "offset", "constant", "parameter", "write" })
        {
            var counter = new NoIo();
            using var target = new InvariantContext(Options(!sourceW).UseModel(model).AddInterceptors(counter).Options, neutral);
            var error = Assert.Throws<InvalidOperationException>(() =>
            {
                var query = target.Set<Row>();
                switch (operation)
                {
                    case "offset": _ = neutral ? query.Select(r => r.Id).ToQueryString() : query.Select(r => r.Offset).ToQueryString(); break;
                    case "constant": _ = query.Select(r => 1).ToList(); break;
                    case "parameter": var id = 4; _ = query.Where(r => r.Id == id).ToList(); break;
                    default: target.Add(new Row { Id = 1 }); target.SaveChanges(); break;
                }
            });
            Assert.Equal("Dameng:ForeignModel", error.Data["Dameng:ErrorId"]);
            Assert.Equal(0, counter.Opens);
            Assert.Equal(0, counter.Commands);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameDriverExplicitModelAndPooledContextsRemainUsable(bool w)
    {
        using var source = new InvariantContext(Options(w).Options);
        var model = source.Model;
        var annotations = model.GetAnnotations().Select(a => (a.Name, a.Value)).ToArray();
        using var services = new ServiceCollection().AddDbContextPool<PooledContext>(b =>
        {
            if (w) b.UseDamengWithWDriver("Server=other"); else b.UseDameng("Server=other");
            b.UseModel(model);
        }).BuildServiceProvider();
        for (var i = 0; i < 2; i++)
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PooledContext>();
            Assert.Contains("SELECT", context.Set<Row>().Select(r => r.Offset).ToQueryString());
            var value = new DateTime(2026, 1, 1);
            Assert.Contains(":", context.Set<Row>().Where(r => r.When == value).ToQueryString());
            Assert.Equal(annotations, context.Model.GetAnnotations().Select(a => (a.Name, a.Value)).ToArray());
            Assert.NotEmpty(context.Model.GetEntityTypes().SelectMany(e => e.GetComplexProperties()));
        }
    }

    [Fact]
    public async Task RuntimeIdentityIsAtomicAndDoesNotEnterPersistentAnnotations()
    {
        using var official = new InvariantContext(Options(false).Options, neutral: true);
        using var w = new InvariantContext(Options(true).Options, neutral: true);
        // A fresh finalized model has no driver identity or ModelDependencies yet.
        // Never remove an identity from the context's cached model in a concurrency test.
        var builder = new ModelBuilder(official.GetService<IConventionSetBuilder>().CreateConventionSet());
        builder.Entity<AtomicRow>();
        var model = builder.FinalizeModel();
        Assert.Null(model.ModelDependencies);
        var before = model.GetAnnotations().Select(a => (a.Name, a.Value)).ToArray();
        using var start = new Barrier(2);
        var results = await Task.WhenAll(new[] { official, w }.Select(context => Task.Run(() =>
        {
            start.SignalAndWait(TimeSpan.FromSeconds(10));
            return Record.Exception(() => context.GetService<IModelRuntimeInitializer>().Initialize(model));
        })));
        Assert.Single(results, e => e is null);
        var error = Assert.Single(results, e => e is not null)!;
        Assert.Equal("Dameng:ForeignModel", error.Data["Dameng:ErrorId"]);
        Assert.Equal(before, model.GetAnnotations().Select(a => (a.Name, a.Value)).ToArray());
        var winnerW = results[1] is null;
        var winner = winnerW ? w : official;
        Assert.Same(winner.GetService<ITypeMappingSource>(), model.ModelDependencies!.TypeMappingSource);
        using var usable = new DbContext(Options(winnerW).UseModel(model).Options);
        Assert.NotNull(usable.Model.GetRelationalModel());
        Assert.Contains("SELECT", usable.Set<AtomicRow>().ToQueryString());
    }

    [Fact]
    public void ConcurrentInitializerCannotClaimModelWhileDependenciesAreBeingPublished()
    {
        using var official = new InvariantContext(Options(false).Options, neutral: true);
        using var w = new InvariantContext(Options(true).Options, neutral: true);
        var builder = new ModelBuilder(official.GetService<IConventionSetBuilder>().CreateConventionSet());
        builder.Entity<AtomicRow>();
        var underlying = builder.FinalizeModel();
        var model = DispatchProxy.Create<IRuntimeModel, PausedModelProxy>();
        var proxy = (PausedModelProxy)(object)model;
        proxy.Target = underlying;
        using var secondEntered = new ManualResetEventSlim();
        using var secondFinished = new ManualResetEventSlim();
        var firstInitializer = official.GetService<IModelRuntimeInitializer>();
        var secondInitializer = w.GetService<IModelRuntimeInitializer>();
        IModel? initialized = null;
        Exception? firstError = null, secondError = null;
        var first = new Thread(() =>
        {
            try { initialized = firstInitializer.Initialize(model, designTime: false); }
            catch (Exception error) { firstError = error; }
        });
        var second = new Thread(() =>
        {
            secondEntered.Set();
            try { _ = secondInitializer.Initialize(model, designTime: false); }
            catch (Exception error) { secondError = error; }
            finally { secondFinished.Set(); }
        });
        first.Start();
        try
        {
            Assert.True(proxy.DependenciesPublished.Wait(TimeSpan.FromSeconds(10)));
            second.Start();
            Assert.True(secondEntered.Wait(TimeSpan.FromSeconds(10)));
            // Both implementations reach a deterministic state: the old one completes/claims;
            // the fixed one blocks on the provider gate, before touching the half-initialized model.
            Assert.True(SpinWait.SpinUntil(() => secondFinished.IsSet
                || (second.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(10)));
            Assert.Null(underlying.FindRuntimeAnnotation("Dameng:RuntimeDriver"));
        }
        finally
        {
            proxy.ContinueInitialization.Set();
            Assert.True(first.Join(TimeSpan.FromSeconds(10)));
            if ((second.ThreadState & ThreadState.Unstarted) == 0) Assert.True(second.Join(TimeSpan.FromSeconds(10)));
            proxy.DependenciesPublished.Dispose(); proxy.ContinueInitialization.Dispose();
        }
        Assert.Null(firstError);
        Assert.NotNull(initialized);
        Assert.Equal("Dameng:ForeignModel", secondError?.Data["Dameng:ErrorId"]);
        Assert.Same(official.GetService<ITypeMappingSource>(), initialized.ModelDependencies!.TypeMappingSource);
        var identity = initialized.FindRuntimeAnnotation("Dameng:RuntimeDriver")!.Value;
        var dependencies = initialized.ModelDependencies;
        using var target = new DbContext(Options(false).UseModel(initialized).Options);
        Assert.NotNull(target.Model.GetRelationalModel());
        Assert.Contains("SELECT", target.Set<AtomicRow>().ToQueryString());
        Assert.Same(identity, initialized.FindRuntimeAnnotation("Dameng:RuntimeDriver")!.Value);
        Assert.Same(dependencies, initialized.ModelDependencies);
    }

    // A test-only transparent IModel delegate fixes the exact EF publication window.
    // No driver internals, sockets, mutable production metadata hooks, or timing delay.
    public class PausedModelProxy : DispatchProxy
    {
        private int _paused;
        public IModel Target { get; set; } = null!;
        public ManualResetEventSlim DependenciesPublished { get; } = new();
        public ManualResetEventSlim ContinueInitialization { get; } = new();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            try
            {
                var result = targetMethod!.Invoke(Target, args);
                if (targetMethod.Name == "set_ModelDependencies" && Interlocked.Exchange(ref _paused, 1) == 0)
                {
                    DependenciesPublished.Set();
                    if (!ContinueInitialization.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Initialization test barrier expired.");
                }
                return result;
            }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnmarkedModelsStillValidateBoundMappingsBeforeTakingOwnership(bool sourceW)
    {
        using var source = new InvariantContext(Options(sourceW).Options);
        var model = source.Model;
        model.RemoveRuntimeAnnotation("Dameng:RuntimeDriver");
        using var target = new InvariantContext(Options(!sourceW).UseModel(model).Options);
        var error = Assert.Throws<InvalidOperationException>(() => _ = target.Model);
        Assert.Equal("Dameng:ForeignModel", error.Data["Dameng:ErrorId"]);
        Assert.Null(model.FindRuntimeAnnotation("Dameng:RuntimeDriver"));
    }

    private static DbContextOptionsBuilder Options(bool w)
    {
        var builder = new DbContextOptionsBuilder();
        if (w) builder.UseDamengWithWDriver("Server=localhost"); else builder.UseDameng("Server=localhost");
        return builder.ReplaceService<IModelCacheKeyFactory, KeyFactory>();
    }
    private sealed class KeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context.GetType(), (context as InvariantContext)?.Neutral ?? false, designTime);
    }
    private sealed class InvariantContext(DbContextOptions options, bool neutral = false) : DbContext(options)
    {
        public bool Neutral { get; } = neutral;
        protected override void OnModelCreating(ModelBuilder builder)
        {
            var entity = builder.Entity<Row>();
            if (Neutral) { entity.Ignore(r => r.When); entity.Ignore(r => r.Offset); entity.Ignore(r => r.Owned); entity.Ignore(r => r.Detail); }
            else { entity.OwnsOne(r => r.Owned); entity.ComplexProperty(r => r.Detail); }
        }
    }
    public sealed class PooledContext(DbContextOptions<PooledContext> options) : DbContext(options);
    private sealed class NoIo : DbConnectionInterceptor, IDbCommandInterceptor
    {
        public int Opens { get; private set; }
        public int Commands { get; private set; }
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        { Opens++; throw new InvalidOperationException("Unexpected Open"); }
        public InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        { Commands++; throw new InvalidOperationException("Unexpected Execute"); }
        public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        { Commands++; throw new InvalidOperationException("Unexpected Execute"); }
    }
    private sealed class Row
    {
        public int Id { get; set; }
        public DateTime When { get; set; }
        public DateTimeOffset Offset { get; set; }
        public Owned Owned { get; set; } = new();
        public Detail Detail { get; set; } = new();
    }
    private sealed class AtomicRow { public int Id { get; set; } }
    private sealed class Owned { public DateTimeOffset Offset { get; set; } }
    private sealed class Detail { public DateTime When { get; set; } }
}
