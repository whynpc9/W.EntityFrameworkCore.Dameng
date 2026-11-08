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
        : this(
            new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(
                    typeof(string),
                    jsonValueReaderWriter: JsonStringReaderWriter.Instance),
                storeType,
                StoreTypePostfix.None,
                dbType,
                unicode,
                size,
                fixedLength),
            driver: null,
            lob)
    {
    }

    public DamengDriver? Driver { get; }

    public RelationalTypeMapping WithDriver(DamengDriver driver)
        => driver.Equals(Driver) ? this : new DamengStringTypeMapping(Parameters, driver, _isLob);

    private DamengStringTypeMapping(
        RelationalTypeMappingParameters parameters, DamengDriver? driver, bool lob = false)
        : base(parameters)
    {
        _isLob = lob || DamengLobStoreType.Classify(parameters.StoreType) == DamengLobStoreType.Kind.Text;
        Driver = driver;
    }

    // Reclassify clones from their own type instead of inheriting the template flag.
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new DamengStringTypeMapping(parameters, Driver);

    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);

        var valueLength = parameter.Value is string value ? value.Length : 0;
        if (_isLob)
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
}
