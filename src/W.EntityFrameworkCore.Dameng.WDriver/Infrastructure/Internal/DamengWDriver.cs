using System.Data.Common;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using W.Dm;
using W.EntityFrameworkCore.Dameng.Infrastructure;

namespace W.EntityFrameworkCore.Dameng.WDriver.Infrastructure.Internal;

/// <summary>
/// The <c>W.DamengProvider</c> driver used by <c>UseDamengWithWDriver</c>.
/// </summary>
/// <remarks>
/// Automatic replay is not offered: W reports unknown command and commit outcomes, and the
/// provider has no accepted classification that makes replay safe. Temporal parameters carry
/// fractional seconds only in <see cref="DbParameter.Scale" />, because the driver rejects a
/// non-zero <see cref="DbParameter.Precision" /> on every temporal parameter.
/// </remarks>
internal sealed class DamengWDriver : DamengDriver
{
    public static DamengWDriver Instance { get; } = new();

    private static readonly MethodInfo UseDamengWithWDriverMethod
        = typeof(DamengWDriverDbContextOptionsBuilderExtensions).GetRuntimeMethod(
            nameof(DamengWDriverDbContextOptionsBuilderExtensions.UseDamengWithWDriver),
            [
                typeof(DbContextOptionsBuilder),
                typeof(string),
                typeof(Action<DamengDbContextOptionsBuilder>)
            ])!;

    private DamengWDriver()
    {
    }

    public override string Name
        => "W";

    public override string ProviderName
        => typeof(DamengWDriver).Assembly.GetName().Name!;

    public override MethodInfo UseProviderMethod
        => UseDamengWithWDriverMethod;

    public override bool UsesTypedTemporalReaders
        => true;

    public override DbConnection CreateConnection(string connectionString)
        => new DmConnection(connectionString);

    public override bool IsDriverConnection(DbConnection connection)
        => connection is DmConnection;

    public override void SetParameterType(DbParameter parameter, DamengParameterType type)
    {
        if (parameter is not DmParameter dmParameter)
        {
            throw CreateForeignParameterException(parameter);
        }

        dmParameter.DmSqlType = type switch
        {
            DamengParameterType.Blob => DmDbType.Blob,
            DamengParameterType.Clob => DmDbType.Clob,
            DamengParameterType.Binary => DmDbType.Binary,
            DamengParameterType.VarBinary => DmDbType.VarBinary,
            DamengParameterType.IntervalDayToSecond => DmDbType.IntervalDayToSecond,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    public override void ConfigureTemporalParameter(DbParameter parameter, int fractionalSecondPrecision)
    {
        if (parameter is not DmParameter)
        {
            throw CreateForeignParameterException(parameter);
        }
        ArgumentOutOfRangeException.ThrowIfNegative(fractionalSecondPrecision);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fractionalSecondPrecision, 9);

        parameter.Precision = 0;
        parameter.Scale = (byte)fractionalSecondPrecision;
    }
}
