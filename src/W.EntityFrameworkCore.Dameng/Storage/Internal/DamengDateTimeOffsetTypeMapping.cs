using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Infrastructure;

namespace W.EntityFrameworkCore.Dameng.Storage.Internal;

internal sealed class DamengDateTimeOffsetTypeMapping
    : DateTimeOffsetTypeMapping, IDamengDriverBoundMapping
{
    private static readonly MethodInfo GetStringMethod =
        typeof(DbDataReader).GetRuntimeMethod(
            nameof(DbDataReader.GetString),
            [typeof(int)])!;

    private static readonly MethodInfo ParseMethod =
        typeof(DamengDateTimeOffsetTypeMapping).GetRuntimeMethod(
            nameof(Parse),
            [typeof(string)])!;

    public DamengDateTimeOffsetTypeMapping(string storeType)
        : base(storeType, System.Data.DbType.DateTimeOffset)
    {
    }

    private DamengDateTimeOffsetTypeMapping(
        RelationalTypeMappingParameters parameters,
        DamengDriver? driver)
        : base(parameters)
    {
        Driver = driver;
    }

    public DamengDriver? Driver { get; }

    public RelationalTypeMapping WithDriver(DamengDriver driver)
        => driver.Equals(Driver) ? this : new DamengDateTimeOffsetTypeMapping(Parameters, driver);

    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters)
        => new DamengDateTimeOffsetTypeMapping(parameters, Driver);

    // Drivers with exact typed getters read DateTimeOffset directly (ticks and offset intact);
    // the official driver keeps the text compatibility path.
    public override MethodInfo GetDataReaderMethod()
        => this.RequireDriver().UsesTypedTemporalReaders
            ? base.GetDataReaderMethod()
            : GetStringMethod;

    public override Expression CustomizeDataReaderExpression(
        Expression expression)
        => this.RequireDriver().UsesTypedTemporalReaders
            ? base.CustomizeDataReaderExpression(expression)
            : Expression.Call(ParseMethod, expression);

    protected override string GenerateNonNullSqlLiteral(object value)
        => DamengTemporalLiteralHelper.Generate((DateTimeOffset)value, StoreType, Precision ?? 6);

    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);
        this.RequireDriver().ConfigureTemporalParameter(parameter, Precision ?? 6);
    }

    public static DateTimeOffset Parse(string value)
        => DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces);
}
