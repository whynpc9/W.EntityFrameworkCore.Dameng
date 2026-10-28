using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Infrastructure;
using W.EntityFrameworkCore.Dameng.Infrastructure.Internal;

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

    public override void SetDbConnection(DbConnection? value, bool contextOwnsConnection)
    {
        // Validate before EF can dispose the old connection or change its ownership.
        if (value is not null && !_driver.IsDriverConnection(value))
        {
            throw DamengDriverErrors.ForeignConnection(_driver, value);
        }

        base.SetDbConnection(value, contextOwnsConnection);
    }

    protected override DbConnection CreateDbConnection()
        => _driver.CreateConnection(ConnectionString!);
}
