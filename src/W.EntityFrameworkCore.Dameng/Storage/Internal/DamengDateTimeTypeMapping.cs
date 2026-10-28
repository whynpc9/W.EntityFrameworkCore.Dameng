using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Infrastructure;

namespace W.EntityFrameworkCore.Dameng.Storage.Internal;

/// <summary>
/// Maps <see cref="DateTime" /> to Dameng <c>TIMESTAMP</c>/<c>DATE</c> and hands the mapping's
/// fractional-second precision to the driver; literals use an explicit precision-preserving parser.
/// </summary>
internal sealed class DamengDateTimeTypeMapping : DateTimeTypeMapping, IDamengDriverBoundMapping
{
    public DamengDateTimeTypeMapping(string storeType, DbType dbType)
        : base(storeType, dbType)
    {
    }

    private DamengDateTimeTypeMapping(RelationalTypeMappingParameters parameters, DamengDriver? driver)
        : base(parameters)
    {
        Driver = driver;
    }

    public DamengDriver? Driver { get; }

    public RelationalTypeMapping WithDriver(DamengDriver driver)
        => driver.Equals(Driver) ? this : new DamengDateTimeTypeMapping(Parameters, driver);

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new DamengDateTimeTypeMapping(parameters, Driver);

    protected override string GenerateNonNullSqlLiteral(object value)
        => DamengTemporalLiteralHelper.Generate((DateTime)value, StoreType,
            DbType == System.Data.DbType.Date ? 0 : Precision ?? 6, DbType == System.Data.DbType.Date);

    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);

        // A bare TIMESTAMP declares six fractional digits; DATE has none.
        this.RequireDriver().ConfigureTemporalParameter(
            parameter,
            DbType == System.Data.DbType.Date ? 0 : Precision ?? 6);
    }
}
