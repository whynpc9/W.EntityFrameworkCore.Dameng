using System.Data.Common;

namespace W.EntityFrameworkCore.Dameng.Infrastructure.Internal;

/// <summary>
/// Creates the provider's driver-selection errors with stable identifiers.
/// </summary>
internal static class DamengDriverErrors
{
    public const string ErrorIdKey = "Dameng:ErrorId";

    public static string RetryNotSupportedId(DamengDriver driver)
        => $"Dameng:RetryNotSupportedFor{driver.Name}Driver";

    public static NotSupportedException RetryNotSupported(DamengDriver driver)
        => WithId(
            new NotSupportedException(
                $"{RetryNotSupportedId(driver)}: the Dameng '{driver.Name}' driver does not support "
                + "EnableRetryOnFailure or DamengRetryingExecutionStrategy. Automatic replay is not "
                + "available because a failed command, commit, or connection may have an unknown "
                + "outcome; verify the final state on an independent connection before deciding "
                + "whether to repeat the work."),
            RetryNotSupportedId(driver));

    public static InvalidOperationException ConflictingDriver(DamengDriver configured, DamengDriver requested)
        => WithId(
            new InvalidOperationException(
                $"Dameng:ConflictingDriver: the options are already configured for the Dameng "
                + $"'{configured.Name}' driver and cannot also select the '{requested.Name}' driver. "
                + "Configure each DbContext options instance for exactly one driver."),
            "Dameng:ConflictingDriver");

    public static ArgumentException ForeignConnection(DamengDriver driver, DbConnection connection)
        => WithId(
            new ArgumentException(
                $"Dameng:ForeignConnection: a connection of type '{connection.GetType().FullName}' "
                + $"cannot be used with the Dameng '{driver.Name}' driver. Pass a connection created "
                + "by that driver; wrapped connections are not supported because provider-specific "
                + "parameter types would be lost.",
                nameof(connection)),
            "Dameng:ForeignConnection");

    public static InvalidOperationException ForeignParameter(DamengDriver driver, DbParameter parameter)
        => WithId(
            new InvalidOperationException(
                $"Dameng:ForeignParameter: a parameter of type '{parameter.GetType().FullName}' "
                + $"cannot be configured by the Dameng '{driver.Name}' driver. A model or type "
                + "mapping created for one driver cannot be reused by a context of another driver."),
            "Dameng:ForeignParameter");

    public static InvalidOperationException ForeignModel(DamengDriver driver, DamengDriver bound, Type modelType)
        => WithId(
            new InvalidOperationException(
                $"Dameng:ForeignModel: model '{modelType.FullName}' belongs to the Dameng '{bound.Name}' "
                + $"driver and cannot be used with the '{driver.Name}' driver."),
            "Dameng:ForeignModel");

    public static InvalidOperationException UnboundMapping(string mappingType)
        => WithId(
            new InvalidOperationException(
                $"Dameng:UnboundTypeMapping: the type mapping '{mappingType}' was not obtained from "
                + "a Dameng type mapping source and has no driver."),
            "Dameng:UnboundTypeMapping");

    public static InvalidOperationException MismatchedServices(DamengDriver registered, DamengDriver configured)
        => WithId(
            new InvalidOperationException(
                $"Dameng:MismatchedDriverServices: the internal service provider registers the "
                + $"Dameng '{registered.Name}' driver services, but the DbContext options select the "
                + $"'{configured.Name}' driver. Register the services that match the options."),
            "Dameng:MismatchedDriverServices");

    public static InvalidOperationException ConflictingServices(IEnumerable<DamengDriver> drivers)
        => WithId(
            new InvalidOperationException(
                "Dameng:ConflictingDriverServices: the internal service provider registers services "
                + $"for more than one Dameng driver ({string.Join(", ", drivers.Select(driver => driver.Name))}). "
                + "Use a separate internal service provider for each driver."),
            "Dameng:ConflictingDriverServices");

    private static TException WithId<TException>(TException exception, string id)
        where TException : Exception
    {
        exception.Data[ErrorIdKey] = id;
        return exception;
    }
}
