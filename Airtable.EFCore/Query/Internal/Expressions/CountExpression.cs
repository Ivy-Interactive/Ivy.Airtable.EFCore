using System.Linq.Expressions;

namespace Airtable.EFCore.Query.Internal;

internal sealed class CountExpression : Expression
{
    public CountExpression(Expression enumerable)
    {
        EnumerableExpression = enumerable;
    }

    public override ExpressionType NodeType => ExpressionType.Extension;
    public override Type Type => typeof(int);
    public Expression EnumerableExpression { get; private init; }
}
