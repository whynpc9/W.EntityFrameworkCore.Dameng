using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

internal sealed class DamengMathTranslator(ISqlExpressionFactory sqlExpressionFactory)
    : IMethodCallTranslator
{
    private static readonly HashSet<MethodInfo> AbsMethods = GetMethods(nameof(Math.Abs),
        typeof(int), typeof(long), typeof(decimal), typeof(double));

    private static readonly HashSet<MethodInfo> SignMethods = GetMethods(nameof(Math.Sign),
        typeof(int), typeof(long), typeof(decimal), typeof(double));

    private static readonly HashSet<MethodInfo> FloorMethods = GetMethods(nameof(Math.Floor),
        typeof(decimal), typeof(double));

    private static readonly HashSet<MethodInfo> CeilingMethods = GetMethods(nameof(Math.Ceiling),
        typeof(decimal), typeof(double));

    private static readonly MethodInfo ExpMethod = GetDoubleMethod(nameof(Math.Exp));
    private static readonly MethodInfo LogMethod = GetDoubleMethod(nameof(Math.Log));
    private static readonly MethodInfo LogWithBaseMethod = typeof(Math).GetRuntimeMethod(
        nameof(Math.Log), [typeof(double), typeof(double)])!;
    private static readonly MethodInfo Log10Method = GetDoubleMethod(nameof(Math.Log10));
    private static readonly MethodInfo PowMethod = typeof(Math).GetRuntimeMethod(
        nameof(Math.Pow), [typeof(double), typeof(double)])!;
    private static readonly MethodInfo SqrtMethod = GetDoubleMethod(nameof(Math.Sqrt));

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is not null)
        {
            return null;
        }

        if (arguments.Count == 2)
        {
            if (method == LogWithBaseMethod)
            {
                // Math.Log(value, base) reverses Dameng LOG(base, value).
                return Function("LOG", [arguments[1], arguments[0]],
                    typeof(double), arguments[0].TypeMapping ?? arguments[1].TypeMapping);
            }

            return method == PowMethod
                ? Function("POWER", arguments, typeof(double),
                    arguments[0].TypeMapping ?? arguments[1].TypeMapping)
                : null;
        }

        if (arguments.Count != 1)
        {
            return null;
        }

        var argument = arguments[0];
        if (AbsMethods.Contains(method))
        {
            if (method.ReturnType == typeof(int))
            {
                // Dameng widens ABS(INT) to BIGINT. CAST restores the CLR int range,
                // including a server-side overflow for int.MinValue in any query shape.
                var widened = Function("ABS", argument, typeof(long), typeMapping: null);
                return sqlExpressionFactory.Convert(widened, typeof(int), argument.TypeMapping);
            }

            return Function("ABS", argument, method.ReturnType, argument.TypeMapping);
        }

        if (SignMethods.Contains(method))
        {
            return Function("SIGN", argument, typeof(int), typeMapping: null);
        }

        if (FloorMethods.Contains(method))
        {
            return Function("FLOOR", argument, method.ReturnType, argument.TypeMapping);
        }

        if (CeilingMethods.Contains(method))
        {
            return Function("CEIL", argument, method.ReturnType, argument.TypeMapping);
        }

        var doubleFunction = method == ExpMethod
            ? "EXP"
            : method == LogMethod
                ? "LN"
                : method == Log10Method
                    ? "LOG10"
                    : method == SqrtMethod
                        ? "SQRT"
                        : null;
        return doubleFunction is null
            ? null
            : Function(doubleFunction, argument, typeof(double), argument.TypeMapping);
    }

    private SqlExpression Function(
        string name,
        SqlExpression argument,
        Type returnType,
        RelationalTypeMapping? typeMapping)
        => Function(name, [argument], returnType, typeMapping);

    private SqlExpression Function(
        string name,
        IReadOnlyList<SqlExpression> arguments,
        Type returnType,
        RelationalTypeMapping? typeMapping)
        => sqlExpressionFactory.Function(
            name,
            arguments,
            nullable: true,
            argumentsPropagateNullability: arguments.Count == 1 ? [true] : [true, true],
            returnType,
            typeMapping);

    private static MethodInfo GetDoubleMethod(string name)
        => typeof(Math).GetRuntimeMethod(name, [typeof(double)])!;

    private static HashSet<MethodInfo> GetMethods(string name, params Type[] argumentTypes)
        => argumentTypes
            .Select(type => typeof(Math).GetRuntimeMethod(name, [type])!)
            .ToHashSet();
}
