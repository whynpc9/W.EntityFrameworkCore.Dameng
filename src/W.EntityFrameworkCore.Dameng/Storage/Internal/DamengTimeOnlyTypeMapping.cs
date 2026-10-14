using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Infrastructure;

namespace W.EntityFrameworkCore.Dameng.Storage.Internal;

internal sealed class DamengTimeOnlyTypeMapping : TimeOnlyTypeMapping, IDamengDriverBoundMapping
{
    public DamengTimeOnlyTypeMapping(string storeType)
        : base(storeType, System.Data.DbType.Time)
    {
    }

    private DamengTimeOnlyTypeMapping(RelationalTypeMappingParameters parameters, DamengDriver? driver)
        : base(parameters)
    {
        Driver = driver;
    }

    public DamengDriver? Driver { get; }

    public RelationalTypeMapping WithDriver(DamengDriver driver)
        => driver.Equals(Driver) ? this : new DamengTimeOnlyTypeMapping(Parameters, driver);

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new DamengTimeOnlyTypeMapping(parameters, Driver);

    protected override void ConfigureParameter(DbParameter parameter)
    {
        if (parameter.Value is TimeOnly value)
        {
            ValidatePrecision(value);
        }

        base.ConfigureParameter(parameter);
        var fractionalSecondPrecision = GetFractionalSecondPrecision();
        parameter.Scale = checked((byte)fractionalSecondPrecision);
        this.RequireDriver().ConfigureTemporalParameter(parameter, fractionalSecondPrecision);
    }

    protected override string GenerateNonNullSqlLiteral(object value)
    {
        var time = (TimeOnly)value;
        ValidatePrecision(time);

        var precision = GetFractionalSecondPrecision();
        var fraction = precision == 0
            ? string.Empty
            : $".{(time.Ticks % TimeSpan.TicksPerSecond)
                .ToString("D7", CultureInfo.InvariantCulture)[..precision]}";

        return FormattableString.Invariant(
            $"TIME '{time.Hour:D2}:{time.Minute:D2}:{time.Second:D2}{fraction}'");
    }

    private void ValidatePrecision(TimeOnly value)
    {
        var precision = GetFractionalSecondPrecision();
        var tickQuantum = Pow10(7 - precision);
        if (value.Ticks % tickQuantum != 0)
        {
            throw new ArgumentException(
                $"The TimeOnly value '{value:O}' cannot be represented without precision loss "
                + $"by Dameng TIME({precision}).",
                nameof(value));
        }
    }

    private int GetFractionalSecondPrecision()
        => Precision ?? 0;

    private static long Pow10(int exponent)
    {
        var result = 1L;
        for (var index = 0; index < exponent; index++)
        {
            result *= 10;
        }

        return result;
    }
}
