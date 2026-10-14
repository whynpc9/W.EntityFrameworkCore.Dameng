using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Infrastructure;

namespace W.EntityFrameworkCore.Dameng.Storage.Internal;

internal sealed class DamengTimeSpanTypeMapping : TimeSpanTypeMapping, IDamengDriverBoundMapping
{
    private static readonly MethodInfo GetStringMethod =
        typeof(DbDataReader).GetRuntimeMethod(
            nameof(DbDataReader.GetString),
            [typeof(int)])!;

    private static readonly MethodInfo ParseMethod =
        typeof(DamengTimeSpanTypeMapping).GetRuntimeMethod(
            nameof(Parse),
            [typeof(string)])!;

    public DamengTimeSpanTypeMapping(string storeType)
        : base(storeType, dbType: null)
    {
    }

    private DamengTimeSpanTypeMapping(RelationalTypeMappingParameters parameters, DamengDriver? driver)
        : base(parameters)
    {
        Driver = driver;
    }

    public DamengDriver? Driver { get; }

    public RelationalTypeMapping WithDriver(DamengDriver driver)
        => driver.Equals(Driver) ? this : new DamengTimeSpanTypeMapping(Parameters, driver);

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new DamengTimeSpanTypeMapping(parameters, Driver);

    // Drivers with exact typed interval getters read TimeSpan directly; the official driver
    // keeps the interval text parser.
    public override MethodInfo GetDataReaderMethod()
        => this.RequireDriver().UsesTypedTemporalReaders
            ? base.GetDataReaderMethod()
            : GetStringMethod;

    public override Expression CustomizeDataReaderExpression(
        Expression expression)
        => this.RequireDriver().UsesTypedTemporalReaders
            ? base.CustomizeDataReaderExpression(expression)
            : Expression.Call(ParseMethod, expression);

    protected override void ConfigureParameter(DbParameter parameter)
    {
        if (parameter.Value is TimeSpan value)
        {
            ValidateFractionalSecondPrecision(value);
        }

        base.ConfigureParameter(parameter);

        var driver = this.RequireDriver();
        driver.SetParameterType(parameter, DamengParameterType.IntervalDayToSecond);

        var fractionalSecondPrecision = Math.Clamp(Scale ?? 6, 0, 6);
        parameter.Scale = (byte)fractionalSecondPrecision;
        driver.ConfigureTemporalParameter(parameter, fractionalSecondPrecision);
    }

    protected override string GenerateNonNullSqlLiteral(object value)
    {
        var timeSpan = (TimeSpan)value;
        ValidateFractionalSecondPrecision(timeSpan);
        var absoluteTicks = decimal.Abs(timeSpan.Ticks);
        var days = decimal.ToInt64(decimal.Truncate(absoluteTicks / TimeSpan.TicksPerDay));
        absoluteTicks %= TimeSpan.TicksPerDay;
        var hours = decimal.ToInt32(decimal.Truncate(absoluteTicks / TimeSpan.TicksPerHour));
        absoluteTicks %= TimeSpan.TicksPerHour;
        var minutes = decimal.ToInt32(decimal.Truncate(absoluteTicks / TimeSpan.TicksPerMinute));
        absoluteTicks %= TimeSpan.TicksPerMinute;
        var seconds = decimal.ToInt32(decimal.Truncate(absoluteTicks / TimeSpan.TicksPerSecond));
        var microseconds = decimal.ToInt32(
            decimal.Truncate((absoluteTicks % TimeSpan.TicksPerSecond) / TimeSpan.TicksPerMicrosecond));
        var fraction = microseconds == 0
            ? string.Empty
            : $".{microseconds.ToString("D6", CultureInfo.InvariantCulture).TrimEnd('0')}";
        var sign = timeSpan < TimeSpan.Zero ? "-" : string.Empty;
        var qualifier = StoreType.StartsWith(
            "INTERVAL ",
            StringComparison.OrdinalIgnoreCase)
                ? StoreType["INTERVAL ".Length..]
                : "DAY(9) TO SECOND(6)";

        return FormattableString.Invariant(
            $"INTERVAL '{sign}{days} {hours:D2}:{minutes:D2}:{seconds:D2}{fraction}' {qualifier}");
    }

    public static TimeSpan Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var firstQuote = value.IndexOf('\'');
        var lastQuote = value.LastIndexOf('\'');
        if (firstQuote < 0 || lastQuote <= firstQuote)
        {
            throw new FormatException(
                $"The Dameng interval value '{value}' has an invalid format.");
        }

        var payload = value[(firstQuote + 1)..lastQuote].Trim();
        var negative = payload.StartsWith('-');
        if (negative || payload.StartsWith('+'))
        {
            payload = payload[1..].TrimStart();
        }

        var parts = payload.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !long.TryParse(
                parts[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var days))
        {
            throw new FormatException(
                $"The Dameng interval value '{value}' has an invalid day component.");
        }

        var timeParts = parts[1].Split(':');
        if (timeParts.Length != 3
            || !int.TryParse(
                timeParts[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var hours)
            || !int.TryParse(
                timeParts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var minutes))
        {
            throw new FormatException(
                $"The Dameng interval value '{value}' has an invalid time component.");
        }

        var secondParts = timeParts[2].Split('.');
        if (secondParts.Length is < 1 or > 2
            || !int.TryParse(
                secondParts[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seconds))
        {
            throw new FormatException(
                $"The Dameng interval value '{value}' has an invalid seconds component.");
        }

        var fraction = secondParts.Length == 2
            ? secondParts[1]
            : string.Empty;
        if (fraction.Length > 7
            || fraction.Any(character => character is < '0' or > '9'))
        {
            throw new FormatException(
                $"The Dameng interval value '{value}' has an invalid fractional component.");
        }

        var fractionTicks = fraction.Length == 0
            ? 0L
            : long.Parse(
                fraction.PadRight(7, '0'),
                NumberStyles.None,
                CultureInfo.InvariantCulture);
        var ticks = checked(
            days * TimeSpan.TicksPerDay
            + hours * TimeSpan.TicksPerHour
            + minutes * TimeSpan.TicksPerMinute
            + seconds * TimeSpan.TicksPerSecond
            + fractionTicks);

        return TimeSpan.FromTicks(negative ? checked(-ticks) : ticks);
    }

    private void ValidateFractionalSecondPrecision(TimeSpan value)
    {
        var scale = Scale ?? 6;
        var tickQuantum = Pow10(7 - scale);
        if (value.Ticks % tickQuantum != 0)
        {
            throw new ArgumentException(
                $"The TimeSpan value '{value}' cannot be represented without precision loss "
                + $"by a Dameng INTERVAL with fractional-second precision {scale}.",
                nameof(value));
        }
    }

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
