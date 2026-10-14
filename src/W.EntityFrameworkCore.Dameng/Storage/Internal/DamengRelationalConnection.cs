using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Infrastructure;

namespace W.EntityFrameworkCore.Dameng.Storage.Internal;

internal sealed class DamengRelationalConnection : RelationalConnection
{
    private readonly DamengDriver _driver;

    public DamengRelationalConnection(
        RelationalConnectionDependencies dependencies,
        DamengDriver driver)
        : base(dependencies)
    {
        _driver = driver;
    }

    protected override DbConnection CreateDbConnection()
        => _driver.CreateConnection(ConnectionString!);
}
