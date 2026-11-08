using Microsoft.EntityFrameworkCore;
using W.EntityFrameworkCore.Dameng.Infrastructure;
using Xunit.Abstractions;
using static W.EntityFrameworkCore.Dameng.FunctionalTests.DamengCliTestHarness;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengIdentitySnapshotTests(ITestOutputHelper output) : DamengIdentitySnapshotTestBase(output)
{
    private protected override string ConfigureMethod => nameof(DamengDbContextOptionsBuilderExtensions.UseDameng);
    private protected override string ProviderName => "W.EntityFrameworkCore.Dameng";
    private protected override AssemblyIdentity[] ProviderAssemblies =>
        [CaptureAssembly(typeof(DamengDriver).Assembly, ProviderName)];
}
