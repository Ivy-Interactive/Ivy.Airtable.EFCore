using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Airtable.EFCore.Query.Internal.MethodTranslators;

internal class AirtableEnumerableMethodTranslator : IMethodCallTranslator
{
    private readonly IFormulaExpressionFactory _formulaExpressionFactory;

    public AirtableEnumerableMethodTranslator(IFormulaExpressionFactory formulaExpressionFactory)
    {
        _formulaExpressionFactory = formulaExpressionFactory;
    }

    public FormulaExpression? Translate(
        IModel model,
        FormulaExpression? instance,
        MethodInfo method,
        IReadOnlyList<FormulaExpression> arguments)
    {
        // Handle Contains method on collections (e.g., array.Contains(item))
        if (method.Name == nameof(Enumerable.Contains) && instance != null)
        {
            // Check if the instance is a collection/array type
            if (typeof(IEnumerable).IsAssignableFrom(instance.Type) && instance.Type != typeof(string))
            {
                return TranslateContains(instance, arguments[0]);
            }
        }

        // Handle Enumerable.Contains static method (e.g., Enumerable.Contains(array, item))
        if (method.Name == nameof(Enumerable.Contains)
            && method.DeclaringType == typeof(Enumerable)
            && arguments.Count == 2)
        {
            return TranslateContains(arguments[0], arguments[1]);
        }

        return null;
    }

    private FormulaExpression TranslateContains(FormulaExpression collection, FormulaExpression item)
    {
        // In Airtable, we need to check if an array field contains a value
        // We can use FIND(item, ARRAYJOIN(array, ","))
        // This converts the array to a comma-separated string and searches for the item

        // For linked record fields (which are arrays of record IDs), we need to search within the array
        // FIND(item, ARRAYJOIN(array))
        var joinedArray = _formulaExpressionFactory.MakeCall("ARRAYJOIN", collection);
        var findResult = _formulaExpressionFactory.MakeCall("FIND", item, joinedArray);

        // FIND returns 0 if not found, or the position if found
        // So we check if the result is > 0
        return _formulaExpressionFactory.MakeBinary(
            ExpressionType.GreaterThan,
            findResult,
            _formulaExpressionFactory.MakeConstant(0),
            null);
    }
}
