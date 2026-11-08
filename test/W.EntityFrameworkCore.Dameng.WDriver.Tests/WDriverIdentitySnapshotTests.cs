using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using W.Dm;
using W.EntityFrameworkCore.Dameng.Infrastructure;
using W.EntityFrameworkCore.Dameng.Tests;
using Xunit;
using Xunit.Abstractions;
using static W.EntityFrameworkCore.Dameng.FunctionalTests.DamengCliTestHarness;

namespace W.EntityFrameworkCore.Dameng.WDriver.Tests;

public sealed class WDriverIdentitySnapshotTests(ITestOutputHelper output) : DamengIdentitySnapshotTestBase(output)
{
    private protected override string ConfigureMethod => nameof(DamengWDriverDbContextOptionsBuilderExtensions.UseDamengWithWDriver);
    private protected override string ProviderName => "W.EntityFrameworkCore.Dameng.WDriver";
    private protected override AssemblyIdentity[] ProviderAssemblies =>
    [
        CaptureAssembly(typeof(DamengDriver).Assembly, "W.EntityFrameworkCore.Dameng"),
        CaptureAssembly(typeof(DamengWDriverDbContextOptionsBuilderExtensions).Assembly, ProviderName)
    ];

    private protected override AssemblyIdentity PinnedDriver
    {
        get
        {
            using var manifest = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "w-damengprovider.manifest.json")));
            var driver = CaptureAssembly(typeof(DmConnection).Assembly, "W.DamengProvider");
            Assert.Equal(manifest.RootElement.GetProperty("assemblyMvid").GetGuid(), driver.Mvid);
            Assert.Equal(manifest.RootElement.GetProperty("assemblySha256").GetString(), driver.Sha256);
            return driver;
        }
    }
}
