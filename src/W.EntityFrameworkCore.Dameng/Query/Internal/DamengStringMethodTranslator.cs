using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

internal sealed class DamengStringMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
    : IMethodCallTranslator
{
    private static readonly MethodInfo IsNullOrEmpty
        = typeof(string).GetRuntimeMethod(nameof(string.IsNullOrEmpty), [typeof(string)])!;

    private static readonly MethodInfo Contains
        = typeof(string).GetRuntimeMethod(nameof(string.Contains), [typeof(string)])!;

    private static readonly MethodInfo StartsWith
        = typeof(string).GetRuntimeMethod(nameof(string.StartsWith), [typeof(string)])!;

    private static readonly MethodInfo EndsWith
        = typeof(string).GetRuntimeMethod(nameof(string.EndsWith), [typeof(string)])!;

    private static readonly MethodInfo IndexOf
        = typeof(string).GetRuntimeMethod(nameof(string.IndexOf), [typeof(string)])!;

    private static readonly MethodInfo IndexOfFrom
        = typeof(string).GetRuntimeMethod(nameof(string.IndexOf), [typeof(string), typeof(int)])!;

    private static readonly MethodInfo Replace
        = typeof(string).GetRuntimeMethod(nameof(string.Replace), [typeof(string), typeof(string)])!;

    private static readonly MethodInfo ToLower
        = typeof(string).GetRuntimeMethod(nameof(string.ToLower), Type.EmptyTypes)!;

    private static readonly MethodInfo ToUpper
        = typeof(string).GetRuntimeMethod(nameof(string.ToUpper), Type.EmptyTypes)!;

    private static readonly MethodInfo SubstringFrom
        = typeof(string).GetRuntimeMethod(nameof(string.Substring), [typeof(int)])!;

    private static readonly MethodInfo SubstringRange
        = typeof(string).GetRuntimeMethod(nameof(string.Substring), [typeof(int), typeof(int)])!;

    private static readonly MethodInfo Trim
        = typeof(string).GetRuntimeMethod(nameof(string.Trim), Type.EmptyTypes)!;

    private static readonly MethodInfo TrimStart
        = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), Type.EmptyTypes)!;

    private static readonly MethodInfo TrimEnd
        = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), Type.EmptyTypes)!;

    private static readonly MethodInfo TrimCharacter
        = typeof(string).GetRuntimeMethod(nameof(string.Trim), [typeof(char)])!;

    private static readonly MethodInfo TrimStartCharacter
        = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), [typeof(char)])!;

    private static readonly MethodInfo TrimEndCharacter
        = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), [typeof(char)])!;

    private static readonly MethodInfo TrimCharacters
        = typeof(string).GetRuntimeMethod(nameof(string.Trim), [typeof(char[])])!;

    private static readonly MethodInfo TrimStartCharacters
        = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), [typeof(char[])])!;

    private static readonly MethodInfo TrimEndCharacters
        = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), [typeof(char[])])!;

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method == IsNullOrEmpty)
        {
            var value = arguments[0];
            var length = Function(
                "LENGTH",
                [value],
                typeof(int),
                typeMapping: null,
                [true]);

            // Dameng's text equality can treat a string of spaces as equal to ''.
            // Length preserves the distinction required by string.IsNullOrEmpty.
            return sqlExpressionFactory.OrElse(
                sqlExpressionFactory.IsNull(value),
                sqlExpressionFactory.Equal(length, sqlExpressionFactory.Constant(0)));
        }

        if (instance is null)
        {
            return null;
        }

        if (method == TrimCharacter || method == TrimStartCharacter || method == TrimEndCharacter
            || method == TrimCharacters || method == TrimStartCharacters || method == TrimEndCharacters)
        {
            if (arguments.Count != 1 || !TryGetTrimCharacters(arguments[0], out var trimCharacters))
            {
                return null;
            }

            var characters = sqlExpressionFactory.Constant(trimCharacters);
            if (method == TrimStartCharacter || method == TrimStartCharacters)
            {
                return Function("LTRIM", [instance, characters], typeof(string),
                    instance.TypeMapping, [true, false]);
            }

            if (method == TrimEndCharacter || method == TrimEndCharacters)
            {
                return Function("RTRIM", [instance, characters], typeof(string),
                    instance.TypeMapping, [true, false]);
            }

            var leftTrimmed = Function("LTRIM", [instance, characters], typeof(string),
                instance.TypeMapping, [true, false]);
            return Function("RTRIM", [leftTrimmed, characters], typeof(string),
                instance.TypeMapping, [true, false]);
        }

        if (method == Contains || method == StartsWith || method == EndsWith)
        {
            var pattern = ApplyStringMapping(arguments[0], instance.TypeMapping);

            if (pattern is SqlConstantExpression { Value: "" })
            {
                return sqlExpressionFactory.IsNotNull(instance);
            }

            if (method == EndsWith)
            {
                var patternLength = Function(
                    "LENGTH",
                    [pattern],
                    typeof(int),
                    typeMapping: null,
                    [true]);
                var suffix = Function(
                    "RIGHT",
                    [instance, patternLength],
                    typeof(string),
                    instance.TypeMapping,
                    [true, true]);

                return sqlExpressionFactory.Equal(suffix, pattern);
            }

            var position = Function(
                "INSTR",
                [instance, pattern],
                typeof(int),
                typeMapping: null,
                [true, true]);

            return method == StartsWith
                ? sqlExpressionFactory.Equal(position, sqlExpressionFactory.Constant(1))
                : sqlExpressionFactory.GreaterThan(position, sqlExpressionFactory.Constant(0));
        }

        if (method == IndexOf || method == IndexOfFrom)
        {
            var pattern = ApplyStringMapping(arguments[0], instance.TypeMapping);
            var functionArguments = method == IndexOf
                ? new[] { instance, pattern }
                :
                [
                    instance,
                    pattern,
                    sqlExpressionFactory.Add(arguments[1], sqlExpressionFactory.Constant(1))
                ];
            var position = Function(
                "INSTR",
                functionArguments,
                typeof(int),
                typeMapping: null,
                Enumerable.Repeat(true, functionArguments.Length));

            return sqlExpressionFactory.Subtract(position, sqlExpressionFactory.Constant(1));
        }

        if (method == Replace)
        {
            return Function(
                "REPLACE",
                [
                    instance,
                    ApplyStringMapping(arguments[0], instance.TypeMapping),
                    ApplyStringMapping(arguments[1], instance.TypeMapping)
                ],
                typeof(string),
                instance.TypeMapping,
                [true, true, true]);
        }

        if (method == ToLower || method == ToUpper)
        {
            return Function(
                method == ToLower ? "LOWER" : "UPPER",
                [instance],
                typeof(string),
                instance.TypeMapping,
                [true]);
        }

        if (method == SubstringFrom || method == SubstringRange)
        {
            SqlExpression[] functionArguments = method == SubstringFrom
                ?
                [
                    instance,
                    sqlExpressionFactory.Add(arguments[0], sqlExpressionFactory.Constant(1))
                ]
                :
                [
                    instance,
                    sqlExpressionFactory.Add(arguments[0], sqlExpressionFactory.Constant(1)),
                    arguments[1]
                ];

            return Function(
                "SUBSTR",
                functionArguments,
                typeof(string),
                instance.TypeMapping,
                Enumerable.Repeat(true, functionArguments.Length));
        }

        if (method == Trim || method == TrimStart || method == TrimEnd)
        {
            return Function(
                method == Trim
                    ? "TRIM"
                    : method == TrimStart
                        ? "LTRIM"
                        : "RTRIM",
                [instance],
                typeof(string),
                instance.TypeMapping,
                [true]);
        }

        return null;
    }

    private SqlExpression Function(
        string name,
        IReadOnlyList<SqlExpression> arguments,
        Type returnType,
        RelationalTypeMapping? typeMapping,
        IEnumerable<bool> argumentsPropagateNullability)
        => sqlExpressionFactory.Function(
            name,
            arguments,
            nullable: true,
            argumentsPropagateNullability,
            returnType,
            typeMapping);

    private SqlExpression ApplyStringMapping(
        SqlExpression expression,
        RelationalTypeMapping? typeMapping)
        => sqlExpressionFactory.ApplyTypeMapping(expression, typeMapping);

    private static bool TryGetTrimCharacters(SqlExpression argument, out string characters)
    {
        characters = argument switch
        {
            SqlConstantExpression { Value: char character }
                when !char.IsSurrogate(character) => character.ToString(),
            SqlConstantExpression { Value: char[] values }
                when values.Length > 0 && !values.Any(char.IsSurrogate) => new string(values),
            _ => string.Empty
        };

        return characters.Length > 0;
    }
}
