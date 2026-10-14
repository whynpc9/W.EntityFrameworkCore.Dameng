using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Infrastructure;

namespace W.EntityFrameworkCore.Dameng.Storage.Internal;

internal sealed class DamengByteArrayTypeMapping : ByteArrayTypeMapping, IDamengDriverBoundMapping
{
    private readonly bool _isLob;

    public DamengByteArrayTypeMapping(
        string storeType,
        int? size = null,
        bool fixedLength = false,
        bool lob = false)
        : base(
            new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(typeof(byte[])),
                storeType,
                StoreTypePostfix.None,
                System.Data.DbType.Binary,
                unicode: false,
                size,
                fixedLength))
    {
        _isLob = lob;
    }

    private DamengByteArrayTypeMapping(RelationalTypeMappingParameters parameters, DamengDriver? driver)
        : base(parameters)
    {
        _isLob = IsLobStoreType(parameters.StoreType);
        Driver = driver;
    }

    private DamengByteArrayTypeMapping(RelationalTypeMappingParameters parameters, DamengDriver driver, bool isLob)
        : base(parameters)
    {
        _isLob = isLob;
        Driver = driver;
    }

    public DamengDriver? Driver { get; }

    public RelationalTypeMapping WithDriver(DamengDriver driver)
        => driver.Equals(Driver) ? this : new DamengByteArrayTypeMapping(Parameters, driver, _isLob);

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new DamengByteArrayTypeMapping(parameters, Driver);

    protected override string GenerateNonNullSqlLiteral(object value)
        => "0x" + Convert.ToHexString((byte[])value);

    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);

        var valueLength = parameter.Value is byte[] value ? value.Length : 0;
        this.RequireDriver().SetParameterType(
            parameter,
            _isLob || IsLobStoreType(StoreType)
                ? DamengParameterType.Blob
                : IsFixedLength ? DamengParameterType.Binary : DamengParameterType.VarBinary);

        if (_isLob || IsLobStoreType(StoreType))
        {
            parameter.Size = valueLength > DamengTypeMappingSource.MaxInlineLength
                ? -1
                : DamengTypeMappingSource.MaxInlineLength;
            return;
        }

        var configuredSize = Size ?? DamengTypeMappingSource.MaxInlineLength;
        parameter.Size = Math.Max(configuredSize, valueLength);
    }

    private static bool IsLobStoreType(string storeType)
        => storeType.Equals("BLOB", StringComparison.OrdinalIgnoreCase)
            || storeType.Equals("IMAGE", StringComparison.OrdinalIgnoreCase)
            || storeType.Equals("LONGVARBINARY", StringComparison.OrdinalIgnoreCase)
            || storeType.Equals("BFILE", StringComparison.OrdinalIgnoreCase);
}
