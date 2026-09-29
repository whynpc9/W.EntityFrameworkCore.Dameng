namespace W.EntityFrameworkCore.Dameng.TestData;

// Deterministic CLR-side fixtures for the query-translation work plan.
// Expected values describe CLR semantics or the explicit contract in the plan;
// they are not inferred from candidate Dameng SQL or server behavior.
internal static class QueryTranslationCases
{
    internal sealed record StringCase(
        int Id,
        string? Text,
        string? OtherText,
        string? ThirdText,
        string? FourthText,
        bool ExpectedIsNullOrEmpty,
        string ExpectedConcat2,
        string ExpectedConcat3,
        string ExpectedConcat4);

    internal static IReadOnlyList<StringCase> Strings { get; } = Array.AsReadOnly<StringCase>(
    [
        new(1, null, null, null, null, true, "", "", ""),
        new(2, null, "右", null, null, true, "右", "右", "右"),
        new(3, "左", null, null, null, false, "左", "左", "左"),
        new(4, "", "右", null, null, true, "右", "右", "右"),
        new(5, "   ", "中文", null, null, false, "   中文", "   中文", "   中文"),
        new(6, "雪🌙", "雨", "林", "🙂", false, "雪🌙雨", "雪🌙雨林", "雪🌙雨林🙂")
    ]);

    internal sealed record Int32MathCase(
        int Value,
        int? ExpectedAbs,
        bool AbsOverflows,
        int ExpectedSign);

    internal static IReadOnlyList<Int32MathCase> MathInt32 { get; } = Array.AsReadOnly<Int32MathCase>(
    [
        new(int.MinValue, null, true, -1),
        new(-7, 7, false, -1),
        new(0, 0, false, 0),
        new(9, 9, false, 1),
        new(int.MaxValue, int.MaxValue, false, 1)
    ]);

    internal sealed record Int64MathCase(
        long Value,
        long? ExpectedAbs,
        bool AbsOverflows,
        int ExpectedSign);

    internal static IReadOnlyList<Int64MathCase> MathInt64 { get; } = Array.AsReadOnly<Int64MathCase>(
    [
        new(long.MinValue, null, true, -1),
        new(-9_223_372_036_854_775_000L, 9_223_372_036_854_775_000L, false, -1),
        new(-1L, 1L, false, -1),
        new(0L, 0L, false, 0),
        new(1L, 1L, false, 1),
        new(long.MaxValue, long.MaxValue, false, 1)
    ]);

    internal sealed record DecimalMathCase(
        decimal Value,
        decimal ExpectedAbs,
        int ExpectedSign,
        decimal ExpectedFloor,
        decimal ExpectedCeiling);

    internal static IReadOnlyList<DecimalMathCase> MathDecimal { get; } = Array.AsReadOnly<DecimalMathCase>(
    [
        new(-12.75m, 12.75m, -1, -13m, -12m),
        new(-0.4m, 0.4m, -1, -1m, 0m),
        new(0m, 0m, 0, 0m, 0m),
        new(7.2m, 7.2m, 1, 7m, 8m),
        new(decimal.MinValue, decimal.MaxValue, -1, decimal.MinValue, decimal.MinValue),
        new(decimal.MaxValue, decimal.MaxValue, 1, decimal.MaxValue, decimal.MaxValue)
    ]);

    internal sealed record DoubleMathCase(
        double Value,
        double ExpectedAbs,
        int ExpectedSign,
        double ExpectedFloor,
        double ExpectedCeiling);

    internal static IReadOnlyList<DoubleMathCase> MathDouble { get; } = Array.AsReadOnly<DoubleMathCase>(
    [
        new(-12.75d, 12.75d, -1, -13d, -12d),
        new(-0.4d, 0.4d, -1, -1d, 0d),
        new(0d, 0d, 0, 0d, 0d),
        new(7.2d, 7.2d, 1, 7d, 8d),
        new(double.MaxValue, double.MaxValue, 1, double.MaxValue, double.MaxValue)
    ]);

    internal sealed record DoubleFunctionCase(
        double Value,
        double LogBase,
        double PowerExponent,
        double ExpectedExp,
        double ExpectedLog,
        double ExpectedLogWithBase,
        double ExpectedLog10,
        double ExpectedPower,
        double ExpectedSquareRoot);

    // Expected values use the .NET Math contract for inputs inside each function's domain.
    internal static IReadOnlyList<DoubleFunctionCase> MathDoubleFunctions { get; } = Array.AsReadOnly<DoubleFunctionCase>(
    [
        new(1d, 10d, 3d, Math.Exp(1d), Math.Log(1d), Math.Log(1d, 10d), Math.Log10(1d), Math.Pow(1d, 3d), Math.Sqrt(1d)),
        new(2d, 10d, 3d, Math.Exp(2d), Math.Log(2d), Math.Log(2d, 10d), Math.Log10(2d), Math.Pow(2d, 3d), Math.Sqrt(2d)),
        new(10d, 2d, 0.5d, Math.Exp(10d), Math.Log(10d), Math.Log(10d, 2d), Math.Log10(10d), Math.Pow(10d, 0.5d), Math.Sqrt(10d))
    ]);

    internal sealed record TrimCase(
        int Id,
        string Text,
        char Character,
        char[] Characters,
        string ExpectedTrimCharacter,
        string ExpectedTrimStartCharacter,
        string ExpectedTrimEndCharacter,
        string ExpectedTrimCharacters,
        string ExpectedTrimStartCharacters,
        string ExpectedTrimEndCharacters)
    {
        internal static TrimCase From(int id, string text, char character, char[] characters)
            => new(
                id,
                text,
                character,
                characters,
                text.Trim(character),
                text.TrimStart(character),
                text.TrimEnd(character),
                text.Trim(characters),
                text.TrimStart(characters),
                text.TrimEnd(characters));
    }

    internal static IReadOnlyList<TrimCase> Trim { get; } = Array.AsReadOnly<TrimCase>(
    [
        TrimCase.From(1, "***正文**尾*", '*', ['*']),
        TrimCase.From(2, "*.*正文*.*", '*', ['*', '.']),
        TrimCase.From(3, "雨雪文字雨", '雪', ['雪', '雨']),
        TrimCase.From(4, "'''引号'''", '\'', ['\'']),
        TrimCase.From(5, "\t\t数据\t", '\t', ['\t']),
        TrimCase.From(6, "中间*字符", '*', ['*']),
        TrimCase.From(7, "!!!", '!', ['!', '!']),
        TrimCase.From(8, "", 'x', ['x']),
        TrimCase.From(9, ".*.*正文*..*", '.', ['*', '.', '*']),
        TrimCase.From(10, "*雪🌙*", '*', ['*'])
    ]);

    internal enum TrimArrayProbeKind
    {
        NullArray,
        EmptyArray,
        CapturedArray
    }

    internal sealed record TrimArrayProbe(
        int Id,
        string Text,
        char[]? Characters,
        TrimArrayProbeKind Kind,
        bool ExpectedTranslationRejection);

    // Null and empty arrays follow .NET's default whitespace behavior. They are
    // separate translation-rejection probes, not aliases for an empty character set.
    internal static IReadOnlyList<TrimArrayProbe> TrimArrayProbes { get; } = Array.AsReadOnly<TrimArrayProbe>(
    [
        new(1, "\t 数据 ", null, TrimArrayProbeKind.NullArray, true),
        new(2, "\t 数据 ", [], TrimArrayProbeKind.EmptyArray, true),
        new(3, "*数据*", ['*'], TrimArrayProbeKind.CapturedArray, true)
    ]);

    internal enum JsonExpectedOutcome
    {
        Value,
        Null,
        Reject
    }

    internal enum JsonScalarKind
    {
        String,
        Int32
    }

    internal sealed record JsonCase(
        int Id,
        string Json,
        string Path,
        JsonScalarKind ScalarKind,
        JsonExpectedOutcome ExpectedOutcome,
        string? ExpectedString,
        int? ExpectedInt32);

    internal static IReadOnlyList<JsonCase> Json { get; } = Array.AsReadOnly<JsonCase>(
    [
        new(1, "{\"profile\":{\"displayName\":\"林🌱\",\"age\":37}}", "$.profile.displayName", JsonScalarKind.String, JsonExpectedOutcome.Value, "林🌱", null),
        new(2, "{\"profile\":{\"displayName\":\"林🌱\",\"age\":37}}", "$.profile.age", JsonScalarKind.Int32, JsonExpectedOutcome.Value, null, 37),
        new(3, "{\"profile\":{\"age\":37}}", "$.profile.displayName", JsonScalarKind.String, JsonExpectedOutcome.Null, null, null),
        new(4, "{\"profile\":{\"displayName\":null,\"age\":null}}", "$.profile.displayName", JsonScalarKind.String, JsonExpectedOutcome.Null, null, null),
        new(5, "{\"profile\":{\"age\":2147483648}}", "$.profile.age", JsonScalarKind.Int32, JsonExpectedOutcome.Reject, null, null),
        new(6, "{\"profile\":{\"displayName\":{\"value\":\"x\"}}}", "$.profile.displayName", JsonScalarKind.String, JsonExpectedOutcome.Reject, null, null),
        new(7, "{\"profile\":{\"displayName\":[\"x\"]}}", "$.profile.displayName", JsonScalarKind.String, JsonExpectedOutcome.Reject, null, null)
    ]);

    internal sealed record DateBucketCase(
        int Id,
        DateTime Value,
        DateTime ExpectedHour,
        DateTime ExpectedMinute,
        DateTime ExpectedSecond,
        DateTime ExpectedIsoWeekStart,
        int ExpectedQuarter);

    internal static IReadOnlyList<DateBucketCase> DateBuckets { get; } = Array.AsReadOnly<DateBucketCase>(
    [
        new(
            1,
            new DateTime(2020, 12, 31, 23, 59, 59, DateTimeKind.Unspecified).AddTicks(9_876_543),
            new DateTime(2020, 12, 31, 23, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2020, 12, 31, 23, 59, 0, DateTimeKind.Unspecified),
            new DateTime(2020, 12, 31, 23, 59, 59, DateTimeKind.Unspecified),
            new DateTime(2020, 12, 28, 0, 0, 0, DateTimeKind.Unspecified),
            4),
        new(
            2,
            new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddTicks(1),
            new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2020, 12, 28, 0, 0, 0, DateTimeKind.Unspecified),
            1),
        new(
            3,
            new DateTime(2024, 2, 29, 12, 34, 56, DateTimeKind.Unspecified).AddTicks(7_654_321),
            new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2024, 2, 29, 12, 34, 0, DateTimeKind.Unspecified),
            new DateTime(2024, 2, 29, 12, 34, 56, DateTimeKind.Unspecified),
            new DateTime(2024, 2, 26, 0, 0, 0, DateTimeKind.Unspecified),
            1),
        new(
            4,
            new DateTime(2024, 1, 7, 0, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2024, 1, 7, 0, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2024, 1, 7, 0, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2024, 1, 7, 0, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            1)
    ]);

    internal sealed record DateTimeOffsetCase(
        int Id,
        DateTimeOffset Value,
        DateTimeOffset ExpectedRoundtrip,
        DateTime ExpectedUtcDateTime);

    internal static IReadOnlyList<DateTimeOffsetCase> DateTimeOffsets { get; } = Array.AsReadOnly<DateTimeOffsetCase>(
    [
        OffsetCase(1, new DateTimeOffset(2024, 4, 4, 12, 0, 0, TimeSpan.FromHours(8))),
        OffsetCase(2, new DateTimeOffset(2024, 4, 4, 4, 0, 0, TimeSpan.Zero)),
        OffsetCase(3, new DateTimeOffset(2024, 4, 3, 23, 0, 0, TimeSpan.FromHours(-5))),
        OffsetCase(4, new DateTimeOffset(2024, 4, 4, 9, 30, 0, TimeSpan.FromMinutes(330))),
        OffsetCase(5, new DateTimeOffset(2024, 4, 4, 4, 0, 0, TimeSpan.Zero).AddTicks(1))
    ]);

    internal sealed record AggregateCase(
        int Id,
        string?[] ValuesInOrder,
        string Separator,
        string ExpectedJoin,
        string ExpectedConcat);

    internal static IReadOnlyList<AggregateCase> Aggregates { get; } = Array.AsReadOnly<AggregateCase>(
    [
        new(1, ["甲", null, "乙", ""], "|", "甲||乙|", "甲乙"),
        new(2, [null, null], ",", ",", ""),
        new(3, ["甲", "乙", "甲"], "", "甲乙甲", "甲乙甲"),
        new(4, [], "|", "", "")
    ]);

    internal sealed record AggregateOrderingCase(
        int Id,
        int? NullableSortKey,
        int SecondarySortKey,
        string Value);

    // CLR OrderBy places null keys first; OrderByDescending places them last.
    // SecondarySortKey makes each primary-key group deterministic for ordered aggregates.
    internal static IReadOnlyList<AggregateOrderingCase> AggregateOrderingCases { get; } = Array.AsReadOnly<AggregateOrderingCase>(
    [
        new(1, null, 20, "A"),
        new(2, null, 10, "B"),
        new(3, 1, 20, "C"),
        new(4, 1, 10, "D"),
        new(5, 2, 20, "E"),
        new(6, 2, 10, "F"),
        new(7, -1, 20, "G"),
        new(8, -1, 10, "H")
    ]);

    internal static IReadOnlyList<AggregateOrderingCase> ExpectedAggregateOrderingAscending { get; } = Array.AsReadOnly(
        AggregateOrderingCases
            .OrderBy(row => row.NullableSortKey)
            .ThenBy(row => row.SecondarySortKey)
            .ToArray());

    internal static IReadOnlyList<AggregateOrderingCase> ExpectedAggregateOrderingDescending { get; } = Array.AsReadOnly(
        AggregateOrderingCases
            .OrderByDescending(row => row.NullableSortKey)
            .ThenBy(row => row.SecondarySortKey)
            .ToArray());

    internal sealed record ContainsCase(
        int Id,
        int?[] Values,
        int? SearchValue,
        bool ExpectedContains);

    internal static IReadOnlyList<ContainsCase> ContainsCases { get; } = Array.AsReadOnly<ContainsCase>(
    [
        new(1, [1, 1, -1, 2], 1, true),
        new(2, [1, 1, -1, 2], 3, false),
        new(3, [null, 1, 2], null, true),
        new(4, [1, 2, 3], null, false)
    ]);

    internal sealed record ContainsCardinalityCase(
        int Count,
        int[] Values,
        int SearchValue,
        bool ExpectedContains);

    internal static IReadOnlyList<ContainsCardinalityCase> ContainsCardinalities { get; } = Array.AsReadOnly(
        new[] { 0, 1, 499, 500, 501, 998, 999, 1000, 2000 }
            .Select(count => new ContainsCardinalityCase(
                count,
                Enumerable.Range(0, count).ToArray(),
                count == 0 ? -1 : count - 1,
                count > 0))
            .ToArray());

    internal sealed record FractionalDateAddCase(
        int Id,
        DateTime Start,
        double DeltaSeconds,
        DateTime? ExpectedAddSeconds,
        bool IsParameterized,
        bool ExpectedOutOfRange);

    internal static IReadOnlyList<FractionalDateAddCase> FractionalDateAdds { get; } = Array.AsReadOnly<FractionalDateAddCase>(
    [
        new(
            1,
            new DateTime(2024, 2, 28, 23, 59, 59, DateTimeKind.Unspecified).AddMilliseconds(600),
            0.5d,
            new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Unspecified).AddMilliseconds(100),
            true,
            false),
        new(
            2,
            new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Unspecified),
            -0.25d,
            new DateTime(2024, 2, 29, 11, 59, 59, DateTimeKind.Unspecified).AddMilliseconds(750),
            false,
            false),
        new(
            3,
            new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Unspecified),
            0.0000002d,
            new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Unspecified).AddTicks(2),
            true,
            false),
        new(
            4,
            DateTime.MaxValue.AddSeconds(-2),
            5d,
            null,
            false,
            true)
    ]);

    private static DateTimeOffsetCase OffsetCase(int id, DateTimeOffset value)
        => new(id, value, value, value.UtcDateTime);
}
