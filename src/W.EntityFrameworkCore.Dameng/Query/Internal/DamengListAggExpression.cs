using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

/// <summary>
/// Represents LISTAGG(value, separator) with optional WITHIN GROUP ordering.
/// </summary>
internal sealed class DamengListAggExpression : SqlExpression
{
    private static readonly System.Reflection.ConstructorInfo QuotingConstructor =
        typeof(DamengListAggExpression).GetConstructor(
        [typeof(SqlExpression), typeof(SqlExpression), typeof(IReadOnlyList<OrderingExpression>), typeof(RelationalTypeMapping)])!;

    public DamengListAggExpression(
        SqlExpression value,
        SqlExpression separator,
        IReadOnlyList<OrderingExpression> orderings,
        RelationalTypeMapping typeMapping)
        : base(typeof(string), typeMapping)
    {
        Value = value;
        Separator = separator;
        Orderings = orderings;
    }

    public SqlExpression Value { get; }

    public SqlExpression Separator { get; }

    public IReadOnlyList<OrderingExpression> Orderings { get; }

    public DamengListAggExpression Update(
        SqlExpression value,
        SqlExpression separator,
        IReadOnlyList<OrderingExpression> orderings)
        => value == Value && separator == Separator && orderings.SequenceEqual(Orderings)
            ? this
            : new DamengListAggExpression(value, separator, orderings, TypeMapping!);

    public DamengListAggExpression ApplyTypeMapping(RelationalTypeMapping typeMapping)
        => typeMapping == TypeMapping
            ? this
            : new DamengListAggExpression(Value, Separator, Orderings, typeMapping);

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var value = (SqlExpression)visitor.Visit(Value)!;
        var separator = (SqlExpression)visitor.Visit(Separator)!;
        var orderings = Orderings
            .Select(ordering => (OrderingExpression)visitor.Visit(ordering)!)
            .ToArray();

        return Update(value, separator, orderings);
    }

    #pragma warning disable EF9100 // EF Core's type-mapping quoting utility is required for provider expressions.
    public override Expression Quote()
        => Expression.New(
            QuotingConstructor,
            Value.Quote(),
            Separator.Quote(),
            Expression.NewArrayInit(typeof(OrderingExpression), Orderings.Select(ordering => ordering.Quote())),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));
    #pragma warning restore EF9100

    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Append("LISTAGG(");
        expressionPrinter.Visit(Value);
        expressionPrinter.Append(", ");
        expressionPrinter.Visit(Separator);
        expressionPrinter.Append(")");
        if (Orderings.Count > 0)
        {
            expressionPrinter.Append(" WITHIN GROUP (ORDER BY ");
            expressionPrinter.VisitCollection(Orderings);
            expressionPrinter.Append(")");
        }
    }

    public override bool Equals(object? obj)
        => obj is DamengListAggExpression other
            && (ReferenceEquals(this, other)
                || base.Equals(other)
                && Value.Equals(other.Value)
                && Separator.Equals(other.Separator)
                && Orderings.SequenceEqual(other.Orderings));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        hash.Add(Value);
        hash.Add(Separator);
        foreach (var ordering in Orderings)
        {
            hash.Add(ordering);
        }

        return hash.ToHashCode();
    }
}
