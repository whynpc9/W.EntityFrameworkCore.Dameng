using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using W.EntityFrameworkCore.Dameng.Infrastructure;

namespace W.EntityFrameworkCore.Dameng.Storage.Internal;

internal sealed class DamengStringTypeMapping : StringTypeMapping, IDamengDriverBoundMapping
{
    private readonly bool _isLob;

    public DamengStringTypeMapping(
        string storeType,
        DbType dbType,
        bool unicode,
        int? size,
        bool fixedLength,
        bool lob = false)
        : base(
            new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(
                    typeof(string),
                    jsonValueReaderWriter: JsonStringReaderWriter.Instance),
                storeType,
                StoreTypePostfix.None,
                dbType,
                unicode,
                size,
                fixedLength))
    {
        _isLob = lob;
    }

    private DamengStringTypeMapping(RelationalTypeMappingParameters parameters, DamengDriver? driver)
        : base(parameters)
    {
        _isLob = IsLobStoreType(parameters.StoreType);
        Driver = driver;
    }

    public DamengDriver? Driver { get; }

    public RelationalTypeMapping WithDriver(DamengDriver driver)
        => driver.Equals(Driver) ? this : new DamengStringTypeMapping(Parameters, driver, _isLob);

    private DamengStringTypeMapping(RelationalTypeMappingParameters parameters, DamengDriver driver, bool isLob)
        : base(parameters)
    {
        _isLob = isLob;
        Driver = driver;
    }

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new DamengStringTypeMapping(parameters, Driver);

    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);

        var valueLength = parameter.Value is string value ? value.Length : 0;
        if (_isLob || IsLobStoreType(StoreType))
        {
            this.RequireDriver().SetParameterType(parameter, DamengParameterType.Clob);

            parameter.Size = valueLength > DamengTypeMappingSource.MaxInlineLength
                ? -1
                : DamengTypeMappingSource.MaxInlineLength;
            return;
        }

        var configuredSize = Size ?? DamengTypeMappingSource.MaxInlineLength;
        parameter.Size = Math.Max(configuredSize, valueLength);
    }

    private static bool IsLobStoreType(string storeType)
        => storeType.Equals("CLOB", StringComparison.OrdinalIgnoreCase)
            || storeType.Equals("NCLOB", StringComparison.OrdinalIgnoreCase)
            || storeType.Equals("TEXT", StringComparison.OrdinalIgnoreCase)
            || storeType.Equals("NTEXT", StringComparison.OrdinalIgnoreCase)
            || storeType.Equals("LONG", StringComparison.OrdinalIgnoreCase)
            || storeType.Equals("LONGVARCHAR", StringComparison.OrdinalIgnoreCase);
}
