using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace W.EntityFrameworkCore.Dameng.Storage.Internal;

internal sealed class DamengTypeMappingSource : RelationalTypeMappingSource
{
    internal const int MaxInlineLength = 32767;

    private static readonly Regex CharSemanticsStoreTypePattern = new(
        @"^(?<name>.+?)\(\s*(?<size>\d+)\s+CHAR\s*\)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly RelationalTypeMapping Bool = new BoolTypeMapping("BIT", DbType.Boolean);
    private static readonly RelationalTypeMapping Byte = CreateConvertedMapping(
        new ShortTypeMapping("SMALLINT", DbType.Int16),
        new ValueConverter<byte, short>(
            value => value,
            value => checked((byte)value)));
    private static readonly RelationalTypeMapping SByte = new SByteTypeMapping("TINYINT", DbType.SByte);
    private static readonly RelationalTypeMapping Short = new ShortTypeMapping("SMALLINT", DbType.Int16);
    private static readonly RelationalTypeMapping UShort = CreateConvertedMapping(
        new IntTypeMapping("INT", DbType.Int32),
        new ValueConverter<ushort, int>(
            value => value,
            value => checked((ushort)value)));
    private static readonly RelationalTypeMapping Int = new IntTypeMapping("INT", DbType.Int32);
    private static readonly RelationalTypeMapping UInt = CreateConvertedMapping(
        new LongTypeMapping("BIGINT", DbType.Int64),
        new ValueConverter<uint, long>(
            value => value,
            value => checked((uint)value)));
    private static readonly RelationalTypeMapping Long = new LongTypeMapping("BIGINT", DbType.Int64);
    private static readonly RelationalTypeMapping ULong = CreateConvertedMapping(
        new DamengDecimalTypeMapping("DECIMAL(20,0)", precision: 20, scale: 0),
        new ValueConverter<ulong, decimal>(
            value => value,
            value => checked((ulong)value)));
    private static readonly RelationalTypeMapping Float = new FloatTypeMapping("REAL", DbType.Single);
    private static readonly RelationalTypeMapping Double = new DoubleTypeMapping("FLOAT", DbType.Double);
    private static readonly RelationalTypeMapping Decimal =
        new DamengDecimalTypeMapping("DECIMAL");
    private static readonly RelationalTypeMapping Char = new DamengCharTypeMapping("NVARCHAR2(1)");
    private static readonly RelationalTypeMapping Guid = new GuidTypeMapping("CHAR(36)", DbType.Guid);
    private static readonly RelationalTypeMapping DateOnly = new DateOnlyTypeMapping("DATE", DbType.Date);
    private static readonly RelationalTypeMapping TimeOnly =
        new DamengTimeOnlyTypeMapping("TIME(6)")
            .WithPrecisionAndScale(6, scale: null);
    private static readonly RelationalTypeMapping TimeOnlyStoreType =
        new DamengTimeOnlyTypeMapping("TIME");
    private static readonly RelationalTypeMapping DateTime =
        new DateTimeTypeMapping("TIMESTAMP(7)", DbType.DateTime2)
            .WithPrecisionAndScale(7, scale: null);
    private static readonly RelationalTypeMapping DateTimeStoreType =
        new DateTimeTypeMapping("TIMESTAMP", DbType.DateTime2);
    private static readonly RelationalTypeMapping DateTimeOffset =
        new DamengDateTimeOffsetTypeMapping("DATETIME(7) WITH TIME ZONE")
            .WithPrecisionAndScale(7, scale: null);
    private static readonly RelationalTypeMapping DateTimeOffsetStoreType =
        new DamengDateTimeOffsetTypeMapping("DATETIME WITH TIME ZONE");
    private static readonly RelationalTypeMapping TimeSpan =
        new DamengTimeSpanTypeMapping("INTERVAL DAY(9) TO SECOND(6)");
    private static readonly RelationalTypeMapping JsonElement = DamengJsonTypeMapping.Default;
    private static readonly RelationalTypeMapping StructuralJson = DamengStructuralJsonTypeMapping.Default;

    private static readonly Dictionary<Type, RelationalTypeMapping> ClrTypeMappings =
        new Dictionary<Type, RelationalTypeMapping>
        {
            [typeof(bool)] = Bool,
            [typeof(byte)] = Byte,
            [typeof(sbyte)] = SByte,
            [typeof(short)] = Short,
            [typeof(ushort)] = UShort,
            [typeof(int)] = Int,
            [typeof(uint)] = UInt,
            [typeof(long)] = Long,
            [typeof(ulong)] = ULong,
            [typeof(float)] = Float,
            [typeof(double)] = Double,
            [typeof(decimal)] = Decimal,
            [typeof(char)] = Char,
            [typeof(Guid)] = Guid,
            [typeof(DateOnly)] = DateOnly,
            [typeof(TimeOnly)] = TimeOnly,
            [typeof(DateTime)] = DateTime,
            [typeof(DateTimeOffset)] = DateTimeOffset,
            [typeof(TimeSpan)] = TimeSpan,
            [typeof(JsonElement)] = JsonElement,
            [typeof(JsonTypePlaceholder)] = StructuralJson
        };

    private static readonly Dictionary<string, RelationalTypeMapping[]> StoreTypeMappings =
        new Dictionary<string, RelationalTypeMapping[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["BIT"] = [Bool],
            ["BYTE"] = [SByte],
            ["TINYINT"] = [SByte],
            ["SMALLINT"] = [Short, Byte],
            ["INT"] = [Int, UShort],
            ["INTEGER"] = [Int, UShort],
            ["BIGINT"] = [Long, UInt],
            ["DEC"] = [Decimal, ULong],
            ["DECIMAL"] = [Decimal, ULong],
            ["NUMERIC"] = [Decimal, ULong],
            ["NUMBER"] = [Decimal, ULong],
            ["MONEY"] = [Decimal],
            ["SMALLMONEY"] = [Decimal],
            ["REAL"] = [Float],
            ["FLOAT"] = [Double, Float],
            ["DOUBLE"] = [Double],
            ["DOUBLE PRECISION"] = [Double],
            ["CHAR"] = [CreateStoreString("CHAR", unicode: false, fixedLength: true), Char, Guid],
            ["CHARACTER"] = [CreateStoreString("CHARACTER", unicode: false, fixedLength: true), Char],
            ["NCHAR"] = [CreateStoreString("NCHAR", unicode: true, fixedLength: true), Char],
            ["NATIONAL CHAR"] = [CreateStoreString("NATIONAL CHAR", unicode: true, fixedLength: true), Char],
            ["NATIONAL CHARACTER"] = [CreateStoreString("NATIONAL CHARACTER", unicode: true, fixedLength: true), Char],
            ["VARCHAR"] = [CreateStoreString("VARCHAR", unicode: false)],
            ["VARCHAR2"] = [CreateStoreString("VARCHAR2", unicode: false)],
            ["CHAR VARYING"] = [CreateStoreString("CHAR VARYING", unicode: false)],
            ["CHARACTER VARYING"] = [CreateStoreString("CHARACTER VARYING", unicode: false)],
            ["NVARCHAR"] = [CreateStoreString("NVARCHAR", unicode: true)],
            ["NVARCHAR2"] = [CreateStoreString("NVARCHAR2", unicode: true)],
            ["NATIONAL CHAR VARYING"] = [CreateStoreString("NATIONAL CHAR VARYING", unicode: true)],
            ["NATIONAL CHARACTER VARYING"] = [CreateStoreString("NATIONAL CHARACTER VARYING", unicode: true)],
            ["CLOB"] = [CreateStoreString("CLOB", unicode: false, lob: true)],
            ["NCLOB"] = [CreateStoreString("NCLOB", unicode: true, lob: true)],
            ["TEXT"] = [CreateStoreString("TEXT", unicode: true, lob: true)],
            ["NTEXT"] = [CreateStoreString("NTEXT", unicode: true, lob: true)],
            ["LONG"] = [CreateStoreString("LONG", unicode: false, lob: true)],
            ["LONGVARCHAR"] = [CreateStoreString("LONGVARCHAR", unicode: false, lob: true)],
            ["ROWID"] = [CreateStoreString("ROWID", unicode: false)],
            ["BINARY"] = [CreateStoreBinary("BINARY", fixedLength: true)],
            ["VARBINARY"] = [CreateStoreBinary("VARBINARY")],
            ["BINARY VARYING"] = [CreateStoreBinary("BINARY VARYING")],
            ["BLOB"] = [CreateStoreBinary("BLOB", lob: true)],
            ["IMAGE"] = [CreateStoreBinary("IMAGE", lob: true)],
            ["LONGVARBINARY"] = [CreateStoreBinary("LONGVARBINARY", lob: true)],
            ["DATE"] = [DateOnly, new DateTimeTypeMapping("DATE", DbType.Date)],
            ["TIME"] = [TimeOnlyStoreType, new TimeSpanTypeMapping("TIME", DbType.Time)],
            ["DATETIME"] = [DateTimeStoreType],
            ["DATETIME2"] = [DateTimeStoreType],
            ["SMALLDATETIME"] = [DateTimeStoreType],
            ["TIMESTAMP"] = [DateTimeStoreType],
            ["DATETIME WITH TIME ZONE"] = [DateTimeOffsetStoreType],
            ["TIMESTAMP WITH TIME ZONE"] = [DateTimeOffsetStoreType],
            ["TIMESTAMP WITH LOCAL TIME ZONE"] = [DateTimeStoreType],
            ["INTERVAL DAY TO SECOND"] = [TimeSpan],
            ["JSON"] = [JsonElement, StructuralJson, CreateStoreString("JSON", unicode: true, lob: true)],
            ["JSONB"] = [JsonElement, StructuralJson, CreateStoreString("JSONB", unicode: true, lob: true)]
        };

    public DamengTypeMappingSource(
        TypeMappingSourceDependencies dependencies,
        RelationalTypeMappingSourceDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        var mapping = base.FindMapping(mappingInfo) ?? FindRawMapping(mappingInfo);

        return mappingInfo.StoreTypeName is null
            ? mapping
            : mapping?.WithTypeMappingInfo(mappingInfo);
    }

    protected override RelationalTypeMapping? FindCollectionMapping(
        RelationalTypeMappingInfo info,
        Type modelType,
        Type? providerType,
        CoreTypeMapping? elementMapping)
        => modelType == typeof(byte[])
            ? null
            : base.FindCollectionMapping(info, modelType, providerType, elementMapping);

    protected override string? ParseStoreTypeName(
        string? storeTypeName,
        ref bool? unicode,
        ref int? size,
        ref int? precision,
        ref int? scale)
    {
        if (storeTypeName is null)
        {
            return null;
        }

        var trimmedStoreType = storeTypeName.Trim();
        if (trimmedStoreType.StartsWith("INTERVAL ", StringComparison.OrdinalIgnoreCase)
            || trimmedStoreType.Contains(" TIME ZONE", StringComparison.OrdinalIgnoreCase)
            || IsSimpleTemporalPrecisionStoreType(trimmedStoreType))
        {
            return ParseQualifiedTemporalStoreType(trimmedStoreType, ref precision, ref scale);
        }

        var charSemanticsMatch = CharSemanticsStoreTypePattern.Match(trimmedStoreType);
        if (charSemanticsMatch.Success)
        {
            size = int.Parse(
                charSemanticsMatch.Groups["size"].Value,
                CultureInfo.InvariantCulture);
            return charSemanticsMatch.Groups["name"].Value;
        }

        return base.ParseStoreTypeName(trimmedStoreType, ref unicode, ref size, ref precision, ref scale);
    }

    private static bool IsSimpleTemporalPrecisionStoreType(string storeType)
    {
        var openParenthesis = storeType.IndexOf('(');
        if (openParenthesis < 0)
        {
            return false;
        }

        var storeTypeName = storeType[..openParenthesis].TrimEnd();
        return storeTypeName.Equals("TIME", StringComparison.OrdinalIgnoreCase)
            || storeTypeName.Equals("TIMESTAMP", StringComparison.OrdinalIgnoreCase)
            || storeTypeName.Equals("DATETIME", StringComparison.OrdinalIgnoreCase)
            || storeTypeName.Equals("DATETIME2", StringComparison.OrdinalIgnoreCase)
            || storeTypeName.Equals("SMALLDATETIME", StringComparison.OrdinalIgnoreCase);
    }

    private static RelationalTypeMapping? FindRawMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        var clrType = mappingInfo.ClrType;
        if (clrType == typeof(TimeOnly)
            && mappingInfo.Precision is int configuredTimePrecision)
        {
            ValidateTimePrecision(configuredTimePrecision);
        }

        if ((clrType == typeof(DateTime) || clrType == typeof(DateTimeOffset))
            && mappingInfo.Precision is int configuredTimestampPrecision)
        {
            ValidateTimestampPrecision(configuredTimestampPrecision);
        }

        if (clrType == typeof(TimeSpan)
            && mappingInfo.Scale is int configuredIntervalScale)
        {
            ValidateFractionalSecondPrecision(configuredIntervalScale, "INTERVAL");
        }

        if (clrType == typeof(decimal))
        {
            ValidateDecimalFacets(mappingInfo.Precision, mappingInfo.Scale);
        }

        var storeTypeName = mappingInfo.StoreTypeName;
        if (storeTypeName is not null)
        {
            var storeTypeNameBase = mappingInfo.StoreTypeNameBase ?? storeTypeName;
            if (StoreTypeMappings.TryGetValue(storeTypeName, out var mappings)
                || StoreTypeMappings.TryGetValue(storeTypeNameBase, out mappings))
            {
                if (clrType is null)
                {
                    return mappings[0];
                }

                return mappings.FirstOrDefault(candidate => candidate.ClrType == clrType);
            }

            return null;
        }

        if (clrType is null)
        {
            return null;
        }

        if (clrType == typeof(string))
        {
            return FindStringMapping(mappingInfo);
        }

        if (clrType == typeof(byte[]) && mappingInfo.ElementTypeMapping is null)
        {
            return FindBinaryMapping(mappingInfo);
        }

        if (clrType == typeof(decimal))
        {
            if (mappingInfo.Precision is not int precision)
            {
                return Decimal;
            }

            var scale = mappingInfo.Scale ?? 0;
            return new DamengDecimalTypeMapping(
                mappingInfo.Scale is null
                    ? $"DECIMAL({precision})"
                    : $"DECIMAL({precision},{scale})",
                precision: precision,
                scale: scale);
        }

        if (clrType == typeof(TimeOnly) && mappingInfo.Precision is int timePrecision)
        {
            return new DamengTimeOnlyTypeMapping($"TIME({timePrecision})")
                .WithPrecisionAndScale(timePrecision, scale: null);
        }

        if (clrType == typeof(DateTime) && mappingInfo.Precision is int dateTimePrecision)
        {
            return new DateTimeTypeMapping(
                    $"TIMESTAMP({dateTimePrecision})",
                    DbType.DateTime2)
                .WithPrecisionAndScale(dateTimePrecision, scale: null);
        }

        if (clrType == typeof(DateTimeOffset)
            && mappingInfo.Precision is int dateTimeOffsetPrecision)
        {
            return new DamengDateTimeOffsetTypeMapping(
                    $"DATETIME({dateTimeOffsetPrecision}) WITH TIME ZONE")
                .WithPrecisionAndScale(dateTimeOffsetPrecision, scale: null);
        }

        if (clrType == typeof(TimeSpan))
        {
            var precision = mappingInfo.Precision ?? 9;
            var scale = mappingInfo.Scale ?? 6;

            return new DamengTimeSpanTypeMapping(
                    $"INTERVAL DAY({precision}) TO SECOND({scale})")
                .WithPrecisionAndScale(precision, scale);
        }

        return ClrTypeMappings.GetValueOrDefault(clrType);
    }

    private static DamengStringTypeMapping FindStringMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        var unicode = mappingInfo.IsUnicode ?? true;
        var fixedLength = mappingInfo.IsFixedLength == true;
        var size = mappingInfo.Size;

        if (fixedLength && size is < 0 or > MaxInlineLength)
        {
            throw new NotSupportedException(
                $"Dameng fixed-length character types require a size between 1 and {MaxInlineLength}; "
                + $"{size} cannot be mapped to a fixed-length LOB.");
        }

        if ((!fixedLength && !mappingInfo.IsKeyOrIndex && size is null)
            || size < 0
            || size > MaxInlineLength)
        {
            return new DamengStringTypeMapping(
                unicode ? "NCLOB" : "CLOB",
                unicode ? DbType.String : DbType.AnsiString,
                unicode,
                size: null,
                fixedLength: false,
                lob: true);
        }

        size ??= mappingInfo.IsKeyOrIndex
            ? unicode ? 450 : 900
            : 1;

        // Non-Unicode declarations are byte-sized unless qualified with CHAR, which truncates
        // multi-byte text on byte-semantics instances. The qualifier makes the server budget
        // four bytes per character (UTF-8), so only lengths whose worst-case byte count stays
        // inside the inline limit are declared with CHAR; larger ones fall back to CLOB
        // instead of declaring a length the server cannot hold.
        if (!unicode && size.Value * 4L > MaxInlineLength)
        {
            if (fixedLength)
            {
                throw new NotSupportedException(
                    $"Dameng fixed-length character semantics cannot hold {size} characters "
                    + $"within the {MaxInlineLength}-byte inline limit; use a variable-length column.");
            }

            return new DamengStringTypeMapping(
                "CLOB",
                DbType.AnsiString,
                unicode,
                size: null,
                fixedLength: false,
                lob: true);
        }

        var storeTypeName = unicode
            ? fixedLength ? "NCHAR" : "NVARCHAR2"
            : fixedLength ? "CHAR" : "VARCHAR2";
        var dbType = unicode
            ? fixedLength ? DbType.StringFixedLength : DbType.String
            : fixedLength ? DbType.AnsiStringFixedLength : DbType.AnsiString;

        var lengthQualifier = unicode ? "" : " CHAR";

        return new DamengStringTypeMapping(
            $"{storeTypeName}({size}{lengthQualifier})",
            dbType,
            unicode,
            size,
            fixedLength);
    }

    private static DamengByteArrayTypeMapping FindBinaryMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        if (mappingInfo.IsRowVersion == true)
        {
            return new DamengByteArrayTypeMapping("BINARY(8)", size: 8, fixedLength: true);
        }

        var fixedLength = mappingInfo.IsFixedLength == true;
        var size = mappingInfo.Size;

        if (fixedLength && size is < 0 or > MaxInlineLength)
        {
            throw new NotSupportedException(
                $"Dameng fixed-length binary types require a size between 1 and {MaxInlineLength}; "
                + $"{size} cannot be mapped to a fixed-length LOB.");
        }

        if ((!fixedLength && !mappingInfo.IsKeyOrIndex && size is null)
            || size < 0
            || size > MaxInlineLength)
        {
            return new DamengByteArrayTypeMapping("BLOB", lob: true);
        }

        size ??= mappingInfo.IsKeyOrIndex ? 900 : 1;

        return new DamengByteArrayTypeMapping(
            $"{(fixedLength ? "BINARY" : "VARBINARY")}({size})",
            size,
            fixedLength);
    }

    private static void ValidateTimePrecision(int precision)
    {
        ValidateFractionalSecondPrecision(precision, "TIME");
    }

    private static void ValidateTimestampPrecision(int precision)
    {
        if (precision is < 0 or > 9)
        {
            throw new InvalidOperationException(
                $"Dameng timestamp precision must be between 0 and 9, but {precision} was configured.");
        }
    }

    private static void ValidateFractionalSecondPrecision(
        int precision,
        string storeType)
    {
        if (precision is < 0 or > 6)
        {
            throw new InvalidOperationException(
                $"Dameng {storeType} fractional-second precision must be between 0 and 6, "
                + $"but {precision} was configured.");
        }
    }

    private static void ValidateDecimalFacets(int? precision, int? scale)
    {
        if (precision is null && scale is null)
        {
            return;
        }

        if (precision is null or < 1 or > 38)
        {
            throw new InvalidOperationException(
                $"Dameng DECIMAL precision must be between 1 and 38, but {precision} was configured.");
        }

        if (scale is int configuredScale
            && configuredScale > precision.Value)
        {
            throw new InvalidOperationException(
                $"Dameng DECIMAL scale must not exceed precision {precision}, "
                + $"but {scale} was configured.");
        }
    }

    private static RelationalTypeMapping CreateConvertedMapping<TModel, TProvider>(
        RelationalTypeMapping mapping,
        ValueConverter<TModel, TProvider> converter)
        => (RelationalTypeMapping)mapping.WithComposedConverter(converter);

    private static DamengStringTypeMapping CreateStoreString(
        string storeType,
        bool unicode,
        bool fixedLength = false,
        bool lob = false)
        => new DamengStringTypeMapping(
            storeType,
            unicode
                ? fixedLength ? DbType.StringFixedLength : DbType.String
                : fixedLength ? DbType.AnsiStringFixedLength : DbType.AnsiString,
            unicode,
            size: null,
            fixedLength,
            lob);

    private static DamengByteArrayTypeMapping CreateStoreBinary(
        string storeType,
        bool fixedLength = false,
        bool lob = false)
        => new DamengByteArrayTypeMapping(storeType, size: null, fixedLength, lob);

    private static string ParseQualifiedTemporalStoreType(
        string storeType,
        ref int? precision,
        ref int? scale)
    {
        var normalized = new StringBuilder(storeType.Length);
        var parsedPrecision = precision;
        var parsedScale = scale;

        for (var index = 0; index < storeType.Length; index++)
        {
            if (storeType[index] != '(')
            {
                normalized.Append(storeType[index]);
                continue;
            }

            var closeIndex = storeType.IndexOf(')', index + 1);
            if (closeIndex < 0)
            {
                return storeType;
            }

            var facets = storeType[(index + 1)..closeIndex]
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (facets.Length is < 1 or > 2
                || !int.TryParse(facets[0], out var facet))
            {
                return storeType;
            }

            var prefix = storeType[..index].TrimEnd();
            var isTrailingIntervalSecond =
                prefix.StartsWith("INTERVAL ", StringComparison.OrdinalIgnoreCase)
                && prefix.EndsWith(" SECOND", StringComparison.OrdinalIgnoreCase)
                && prefix.Contains(" TO ", StringComparison.OrdinalIgnoreCase);

            if (facets.Length == 2)
            {
                parsedPrecision = facet;
                if (!int.TryParse(facets[1], out var secondFacet))
                {
                    return storeType;
                }

                parsedScale = secondFacet;
            }
            else if (isTrailingIntervalSecond)
            {
                parsedScale = facet;
            }
            else
            {
                parsedPrecision = facet;
            }

            index = closeIndex;
        }

        precision = parsedPrecision;
        scale = parsedScale;

        return normalized.ToString();
    }
}
