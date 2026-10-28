using System.Globalization;

namespace W.EntityFrameworkCore.Dameng.Storage.Internal;

/// <summary>Uses an explicit parser and result precision; bare TIMESTAMP literals parse at scale six.</summary>
internal static class DamengTemporalLiteralHelper
{
    public static string Generate(DateTime value, string storeType, int precision, bool dateOnly)
    {
        Validate(value.Ticks, precision, storeType);
        if (dateOnly)
        {
            if (value.TimeOfDay != TimeSpan.Zero)
            {
                throw new ArgumentException("The DateTime value cannot be represented without loss by Dameng DATE.", nameof(value));
            }

            return $"DATE '{value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}'";
        }

        return $"CAST(TO_TIMESTAMP('{value.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture)}', "
            + $"'YYYY-MM-DD HH24:MI:SS.FF7') AS {storeType})";
    }

    public static string Generate(DateTimeOffset value, string storeType, int precision)
    {
        Validate(value.Ticks, precision, storeType);
        return $"CAST(TO_TIMESTAMP_TZ('{value.ToString("yyyy-MM-dd HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)}', "
            + $"'YYYY-MM-DD HH24:MI:SS.FF7TZH:TZM') AS {storeType})";
    }

    private static void Validate(long ticks, int precision, string storeType)
    {
        var quantum = 1L;
        for (var digit = precision; digit < 7; digit++) quantum *= 10;
        if (ticks % quantum != 0)
        {
            throw new ArgumentException($"The temporal value cannot be represented without precision loss by Dameng {storeType}.", nameof(ticks));
        }
    }
}
