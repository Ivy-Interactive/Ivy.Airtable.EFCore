using Microsoft.EntityFrameworkCore.Query;

namespace Airtable.EFCore.Query.Internal;

internal sealed class FormulaQueryParameterExpression : FormulaExpression, IEquatable<FormulaQueryParameterExpression?>
{
    public FormulaQueryParameterExpression(string name, Type type) : base(type)
    {
        Name = name;
    }

    public string Name { get; }

    public override bool Equals(object? obj) => Equals(obj as FormulaQueryParameterExpression);
    public bool Equals(FormulaQueryParameterExpression? other) => other is not null && base.Equals(other) && Name == other.Name;
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Name);

    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Append($".FormulaQueryParameter({Name})");
    }
}
