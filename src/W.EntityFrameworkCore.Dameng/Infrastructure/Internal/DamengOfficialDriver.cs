using System.Data.Common;
using System.Reflection;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using W.EntityFrameworkCore.Dameng.Storage.Internal;

namespace W.EntityFrameworkCore.Dameng.Infrastructure.Internal;

/// <summary>
/// The official <c>DM.DmProvider</c> driver used by <c>UseDameng</c>.
/// </summary>
internal sealed class DamengOfficialDriver : DamengDriver
{
    public static DamengOfficialDriver Instance { get; } = new();

    private static readonly MethodInfo UseDamengMethod
        = typeof(DamengDbContextOptionsBuilderExtensions).GetRuntimeMethod(
            nameof(DamengDbContextOptionsBuilderExtensions.UseDameng),
            [
                typeof(DbContextOptionsBuilder),
                typeof(string),
                typeof(Action<DamengDbContextOptionsBuilder>)
            ])!;

    private DamengOfficialDriver()
    {
    }

    public override string Name
        => "Official";

    public override string ProviderName
        => typeof(DamengOfficialDriver).Assembly.GetName().Name!;

    public override MethodInfo UseProviderMethod
        => UseDamengMethod;

    public override bool SupportsRetryOnFailure
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
    }

    public override bool ShouldRetryOn(Exception exception, IReadOnlySet<int>? additionalErrorNumbers)
        => (exception is DmException dmException
                && additionalErrorNumbers?.Contains(dmException.Number) == true)
            || DamengTransientExceptionDetector.ShouldRetryOn(exception);
}
