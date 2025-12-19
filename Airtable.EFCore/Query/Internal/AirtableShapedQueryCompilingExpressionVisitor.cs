using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Airtable.EFCore.Metadata.Conventions;
using Airtable.EFCore.Storage.Internal;
using AirtableApiClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace Airtable.EFCore.Query.Internal;

internal sealed class AirtableShapedQueryCompilingExpressionVisitor : ShapedQueryCompilingExpressionVisitor
{
    private abstract class AirtableProjectionBindingRemovingVisitorBase : ExpressionVisitor
    {
        private static readonly MethodInfo _dictionaryTryGetValueMethod =
            typeof(IDictionary<string, object>)
                .GetMethod(
                    nameof(IDictionary<string, object>.TryGetValue))
                ?? throw new InvalidOperationException("Could not find method TryGetValue");

        private static readonly MethodInfo _visitorReadSingleValueMethod =
            typeof(AirtableProjectionBindingRemovingVisitorBase)
                .GetMethod(
                    nameof(AirtableProjectionBindingRemovingVisitorBase.ReadSingleValue),
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(JsonElement), typeof(JsonSerializerOptions) })
                ?? throw new InvalidOperationException("Could not find method ReadSingleValue");

        private static readonly MethodInfo _visitorReadRawMethod =
            typeof(AirtableProjectionBindingRemovingVisitorBase)
                .GetMethod(
                    nameof(AirtableProjectionBindingRemovingVisitorBase.ReadRaw),
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(JsonElement), typeof(JsonSerializerOptions) })
                ?? throw new InvalidOperationException("Could not find method ReadRaw");

        private static readonly MethodInfo _visitorReadSingleValueWithReaderWriterMethod =
            typeof(AirtableProjectionBindingRemovingVisitorBase)
                .GetMethod(
                    nameof(AirtableProjectionBindingRemovingVisitorBase.ReadSingleValue),
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(JsonElement), typeof(JsonValueReaderWriter) })
                ?? throw new InvalidOperationException("Could not find method ReadSingleValue");

        private static readonly MethodInfo _visitorReadRawWithReaderWriterMethod =
            typeof(AirtableProjectionBindingRemovingVisitorBase)
                .GetMethod(
                    nameof(AirtableProjectionBindingRemovingVisitorBase.ReadRaw),
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(JsonElement), typeof(JsonValueReaderWriter) })
                ?? throw new InvalidOperationException("Could not find method ReadRaw");

        private readonly ParameterExpression _recordParameter;
        private readonly bool _trackQueryResults;
        private readonly IDictionary<ParameterExpression, Expression> _materializationContextBindings
            = new Dictionary<ParameterExpression, Expression>();

        private static readonly Expression _jsonOptionsExpression = Expression.Constant(CreateOptions());

        private static JsonSerializerOptions CreateOptions()
        {
            return new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = {
                    new JsonStringEnumConverter(),
                },
            };
        }

        protected AirtableProjectionBindingRemovingVisitorBase(
            ParameterExpression recordParameter,
            bool trackQueryResults)
        {
            _recordParameter = recordParameter;
            _trackQueryResults = trackQueryResults;
        }

        protected override Expression VisitBinary(BinaryExpression binaryExpression)
        {
            if (binaryExpression.NodeType == ExpressionType.Assign)
            {
                if (binaryExpression.Left is ParameterExpression parameterExpression)
                {
                    if (parameterExpression.Type == typeof(AirtableRecord))
                    {
                        var projectionExpression = ((UnaryExpression)binaryExpression.Right).Operand;
                        if (projectionExpression is ProjectionBindingExpression projectionBindingExpression)
                        {
                            var projection = GetProjection(projectionBindingExpression);
                            projectionExpression = projection.Expression;
                        }
                        else if (projectionExpression is UnaryExpression convertExpression
                                 && convertExpression.NodeType == ExpressionType.Convert)
                        {
                            // Unwrap EntityProjectionExpression when the root entity is not projected
                            projectionExpression = ((UnaryExpression)convertExpression.Operand).Operand;
                        }

                        if (projectionExpression is EntityProjectionExpression entityProjectionExpression)
                        {
                            if (entityProjectionExpression.AccessExpression is RootReferenceExpression)
                            {
                                projectionExpression = _recordParameter;
                            }
                        }

                        return Expression.MakeBinary(ExpressionType.Assign, binaryExpression.Left, projectionExpression);
                    }

                    if (parameterExpression.Type == typeof(MaterializationContext))
                    {
                        var newExpression = (NewExpression)binaryExpression.Right;

                        EntityProjectionExpression entityProjectionExpression;
                        if (newExpression.Arguments[0] is ProjectionBindingExpression projectionBindingExpression)
                        {
                            var projection = GetProjection(projectionBindingExpression);
                            entityProjectionExpression = (EntityProjectionExpression)projection.Expression;
                        }
                        else
                        {
                            var projection = ((UnaryExpression)((UnaryExpression)newExpression.Arguments[0]).Operand).Operand;
                            entityProjectionExpression = (EntityProjectionExpression)projection;
                        }

                        _materializationContextBindings[parameterExpression] = entityProjectionExpression.AccessExpression;

                        var updatedExpression = Expression.New(
                            newExpression.Constructor ?? throw new InvalidOperationException("Expression has no constructor"),
                            Expression.Constant(ValueBuffer.Empty),
                            newExpression.Arguments[1]);

                        return Expression.MakeBinary(ExpressionType.Assign, binaryExpression.Left, updatedExpression);
                    }
                }
            }
            return base.VisitBinary(binaryExpression);
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is ProjectionBindingExpression projectionBindingExpression)
            {
                var projection = GetProjection(projectionBindingExpression);

                if (projection.Expression is TablePropertyReferenceExpression tableProperty)
                {
                    return CreateGetValueExpression(
                        null,
                        tableProperty.Name,
                        projectionBindingExpression.Type);
                }
                else if (projection.Expression is RecordIdPropertyReferenceExpression)
                {
                    return CreateGetRecordIdExpression();
                }

                throw new InvalidOperationException();
            }

            return base.VisitExtension(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
        {
            var method = methodCallExpression.Method;
            var genericMethod = method.IsGenericMethod ? method.GetGenericMethodDefinition() : null;
            if (genericMethod == Microsoft.EntityFrameworkCore.Infrastructure.ExpressionExtensions.ValueBufferTryReadValueMethod)
            {
                var property = methodCallExpression.Arguments[2].GetConstantValue<IProperty>();
                Expression innerExpression;
                if (methodCallExpression.Arguments[0] is ProjectionBindingExpression projectionBindingExpression)
                {
                    var projection = GetProjection(projectionBindingExpression);

                    innerExpression = Expression.Convert(
                        CreateReadRecordExpression(_recordParameter, projection.Alias),
                        typeof(AirtableRecord));
                }
                else
                {
                    innerExpression = _materializationContextBindings[
                        (ParameterExpression)((MethodCallExpression)methodCallExpression.Arguments[0]).Object];
                }

                return CreateGetValueExpression(property);
            }

            return base.VisitMethodCall(methodCallExpression);
        }

        private Expression CreateGetValueExpression(CoreTypeMapping? mapping, string name, Type type)
        {
            var resultVariable = Expression.Variable(type);
            var jsonElementObj = Expression.Variable(typeof(object));
            var fields = Expression.Variable(typeof(IDictionary<string, object>));
            var isArray = type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
            MethodInfo readMethod;
            var readerWriter = mapping?.JsonValueReaderWriter;
            if (readerWriter != null)
            {
                readMethod = isArray ? _visitorReadRawWithReaderWriterMethod : _visitorReadSingleValueWithReaderWriterMethod;
            }
            else
            {
                readMethod = isArray ? _visitorReadRawMethod : _visitorReadSingleValueMethod;
            }

            var block = new Expression[]
            {
                Expression.Assign(
                    fields,
                    Expression.Property(
                        _recordParameter,
                        nameof(AirtableRecord.Fields))),
                Expression.IfThenElse(
                    Expression.Call(
                        fields,
                        _dictionaryTryGetValueMethod,
                        Expression.Constant(name),
                        jsonElementObj
                        ),
                    Expression.Assign(
                        resultVariable,
                        Expression.Call(
                            readMethod.MakeGenericMethod(type),
                            Expression.Convert(
                                jsonElementObj,
                                typeof(JsonElement)),
                            readerWriter == null
                                ? _jsonOptionsExpression
                                : Expression.Constant(readerWriter))),
                    Expression.Assign(resultVariable, Expression.Default(type))),

                resultVariable
            };

            return Expression.Block(
                type,
                new[]
                {
                    jsonElementObj,
                    fields,
                    resultVariable
                },
                block);
        }

        private Expression CreateGetValueExpression(IProperty property)
        {
            if (property.IsPrimaryKey())
            {
                return CreateGetRecordIdExpression();
            }

            return CreateGetValueExpression(property.GetTypeMapping(), property.GetColumnName() ?? property.Name, property.ClrType);
        }

        private Expression CreateGetRecordIdExpression() => Expression.Property(_recordParameter, nameof(AirtableRecord.Id));

        [return: MaybeNull]
        private static T ReadRaw<T>(JsonElement jsonElement, JsonSerializerOptions jsonSerializerOptions)
        {
            return jsonElement.Deserialize<T>(jsonSerializerOptions);
        }

        [return: MaybeNull]
        private static T ReadSingleValue<T>(JsonElement jsonElement, JsonSerializerOptions jsonSerializerOptions)
        {
            if (jsonElement.ValueKind == JsonValueKind.Array)
            {
                if (jsonElement.GetArrayLength() == 0) return default;
                jsonElement = jsonElement[0];
            }

            if (jsonElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return default;
            }

            return jsonElement.Deserialize<T>(jsonSerializerOptions);
        }

        [return: MaybeNull]
        private static T ReadRaw<T>(JsonElement jsonElement, JsonValueReaderWriter readerWriter)
        {
            // Special handling for TimeSpan when Airtable sends it as a number (seconds)
            if (typeof(T) == typeof(TimeSpan) || typeof(T) == typeof(TimeSpan?))
            {
                if (jsonElement.ValueKind == JsonValueKind.Number)
                {
                    var seconds = jsonElement.GetDouble();
                    return (T?)(object?)TimeSpan.FromSeconds(seconds);
                }
            }

            var jsonString = JsonSerializer.Serialize(jsonElement);
            var readerManager = new Utf8JsonReaderManager(new JsonReaderData(Encoding.UTF8.GetBytes(jsonString)), null);
            readerManager.MoveNext();
            return (T?)readerWriter.FromJson(ref readerManager, null);
        }

        [return: MaybeNull]
        private static T ReadSingleValue<T>(JsonElement jsonElement, JsonValueReaderWriter readerWriter)
        {
            if (jsonElement.ValueKind == JsonValueKind.Array)
            {
                if (jsonElement.GetArrayLength() == 0) return default;
                jsonElement = jsonElement[0];
            }

            if (jsonElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return default;
            }

            return ReadRaw<T>(jsonElement, readerWriter);
        }

        private Expression CreateReadRecordExpression(ParameterExpression recordParameter, string alias)
        {
            throw new NotImplementedException();
        }

        protected abstract ProjectionExpression? GetProjection(ProjectionBindingExpression projectionBindingExpression);

    }

    private sealed class AirtableProjecttionBindingRemovingVisitor : AirtableProjectionBindingRemovingVisitorBase
    {
        private readonly SelectExpression _selectExpression;

        public AirtableProjecttionBindingRemovingVisitor(
            SelectExpression selectExpression,
            ParameterExpression recordParameter,
            bool trackQueryResults) : base(recordParameter, trackQueryResults)
        {
            _selectExpression = selectExpression;
        }

        protected override ProjectionExpression GetProjection(ProjectionBindingExpression projectionBindingExpression)
        => _selectExpression.Projection[GetProjectionIndex(projectionBindingExpression)];

        private int GetProjectionIndex(ProjectionBindingExpression projectionBindingExpression)
            => projectionBindingExpression.ProjectionMember != null
                ? _selectExpression.GetMappedProjection(projectionBindingExpression.ProjectionMember).GetConstantValue<int>()
                : (projectionBindingExpression.Index
                    ?? throw new InvalidOperationException(CoreStrings.TranslationFailed(projectionBindingExpression.Print())));
    }

    private sealed class AirtableRecordInjectingExpressionVisitor : ExpressionVisitor
    {
        private int _currentEntityIndex;

        [return: NotNullIfNotNull(nameof(node))]
        public override Expression? Visit(Expression? node)
        {
            if (node is StructuralTypeShaperExpression shaperExpression)
            {
                _currentEntityIndex++;

                var valueBufferExpression = shaperExpression.ValueBufferExpression;

                var recordVariable = Expression.Variable(
                    typeof(AirtableRecord),
                    "record" + _currentEntityIndex);
                var variables = new List<ParameterExpression> { recordVariable };

                var expressions = new List<Expression>
                    {
                        Expression.Assign(
                            recordVariable,
                            Expression.TypeAs(
                                valueBufferExpression,
                                typeof(AirtableRecord))),
                        Expression.Condition(
                            Expression.Equal(recordVariable, Expression.Constant(null, recordVariable.Type)),
                            Expression.Constant(null, shaperExpression.Type),
                            shaperExpression)
                    };

                return Expression.Block(
                    shaperExpression.Type,
                    variables,
                    expressions);
            }

            return base.Visit(node);
        }
    }

    private static readonly MethodInfo _navigationDataGetTableData =
        typeof(NavigationData)
            .GetMethod(
                nameof(NavigationData.GetTableData),
                BindingFlags.Public | BindingFlags.Instance,
                new[] { typeof(string) })
            ?? throw new InvalidOperationException("Could not find method GetTableData");

    private static readonly MethodInfo _tableDataGetEntityMappings =
        typeof(TableData<>)
            .GetMethod(
                nameof(TableDataBase.GetEntityMappings),
                BindingFlags.Public | BindingFlags.Instance,
                new[] { typeof(string), typeof(string) })
            ?? throw new InvalidOperationException("Could not find method GetEntityMappings");
    private static readonly MethodInfo _tableDataAddRecordId =
        typeof(TableDataBase)
            .GetMethod(
                nameof(TableDataBase.AddRecordId),
                BindingFlags.Public | BindingFlags.Instance,
                new[] { typeof(string) })
            ?? throw new InvalidOperationException("Could not find method AddRecordId");
    private static readonly MethodInfo _tableDataAddRecordIds =
        typeof(TableDataBase)
            .GetMethod(
                nameof(TableDataBase.AddRecordIds),
                BindingFlags.Public | BindingFlags.Instance,
                new[] { typeof(IEnumerable<string>) })
            ?? throw new InvalidOperationException("Could not find method AddRecordIds");
    private static readonly MethodInfo _tableDataMarkEntitiesAsLoaded =
        typeof(TableDataBase)
            .GetMethod(
                nameof(TableDataBase.MarkEntitiesAsLoaded),
                BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not find method MarkEntitiesAsLoaded");
    private static readonly MethodInfo _entityMappingListMarkEntityMappingsAsVisited =
        typeof(EntityMappingListBase)
            .GetMethod(
                nameof(EntityMappingListBase.MarkEntityMappingsAsVisited),
                BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not find MarkEntityMappingsAsVisited");

    public AirtableShapedQueryCompilingExpressionVisitor(
        ShapedQueryCompilingExpressionVisitorDependencies dependencies,
        QueryCompilationContext queryCompilationContext)
        : base(dependencies, queryCompilationContext)
    {
    }

    protected override Expression VisitShapedQuery(ShapedQueryExpression shapedQueryExpression)
    {
        var innerExpression = shapedQueryExpression.QueryExpression;
        var shouldGetCount = false;
        if (shapedQueryExpression.QueryExpression is CountExpression countExpression)
        {
            shouldGetCount = true;
            innerExpression = countExpression.EnumerableExpression;
        }

        if (innerExpression is SelectExpression selectExpression)
        {
            var shaperLambdaMapping = new Dictionary<string, Expression<Action<AirtableQueryContext, List<AirtableRecord>, NavigationData>>>();
            var recordParameter = Expression.Parameter(typeof(AirtableRecord), "record");
            var shaperBlock = BuildShaperBlock(selectExpression, shapedQueryExpression.ShaperExpression, recordParameter);
            var shaperLambda = Expression.Lambda(
                shaperBlock,
                QueryCompilationContext.QueryContextParameter,
                recordParameter);

            var entityType = selectExpression.EntityType;
            var navigationResolver = new NavigationResolver();
            foreach (var navigation in entityType.GetNavigations().Concat<INavigationBase>(entityType.GetSkipNavigations()))
            {
                VisitNavigation(shaperLambdaMapping, navigationResolver, entityType, navigation);
            }

            var shaperMapping = shaperLambdaMapping.ToDictionary(item => item.Key, item => item.Value.Compile());
            var enumerable = Expression.New(
                typeof(QueryingEnumerable<>).MakeGenericType(shaperLambda.ReturnType).GetConstructors().First(),
                Expression.Convert(QueryCompilationContext.QueryContextParameter, typeof(AirtableQueryContext)),
                Expression.Constant(selectExpression),
                Expression.Constant(shaperLambda.Compile()),
                Expression.Constant(shaperMapping),
                Expression.Constant(navigationResolver),
                Expression.Constant(
                        QueryCompilationContext.QueryTrackingBehavior == QueryTrackingBehavior.NoTrackingWithIdentityResolution)
                );

            shapedQueryExpression = shapedQueryExpression.UpdateQueryExpression(enumerable);
        }

        if (shouldGetCount)
        {
            shapedQueryExpression = shapedQueryExpression.UpdateQueryExpression(
                Expression.Call(
                    QueryCompilationContext.IsAsync
                        ? CountImplAsyncMethod.MakeGenericMethod(shapedQueryExpression.Type)
                        : CountImplMethod.MakeGenericMethod(shapedQueryExpression.Type),
                    shapedQueryExpression.QueryExpression));
        }

        return shapedQueryExpression.QueryExpression;
    }

    private BlockExpression BuildShaperBlock(SelectExpression selectExpression, Expression shaper, ParameterExpression record)
    {
        selectExpression.ApplyProjection();

        var entityType = selectExpression.EntityType;

        shaper = new AirtableRecordInjectingExpressionVisitor().Visit(shaper);
        shaper = InjectStructuralTypeMaterializers(shaper);
        shaper = new AirtableProjecttionBindingRemovingVisitor(
            selectExpression,
            record,
            QueryCompilationContext.QueryTrackingBehavior == QueryTrackingBehavior.TrackAll)
                .Visit(shaper);

        return Expression.Block(
            shaper.Type,
            Array.Empty<ParameterExpression>(),
            new[] { shaper });
    }

    private Expression<Action<AirtableQueryContext, List<AirtableRecord>, NavigationData>> BuildShaperLambda(SelectExpression selectExpression, Expression shaper, string tableName, Type targetEntityType)
    {
        var recordsParameter = Expression.Parameter(typeof(List<AirtableRecord>), "records");
        var navigationDataParameter = Expression.Parameter(typeof(NavigationData), "navigationData");

        var recordIndex = Expression.Variable(typeof(int), "recordIndex");
        var tableData = Expression.Variable(
            typeof(TableData<>)
                .MakeGenericType(targetEntityType),
            "tableData");
        var variables = new List<ParameterExpression> { tableData, recordIndex };

        var record = Expression.Variable(typeof(AirtableRecord), "record");
        var entity = Expression.Variable(targetEntityType, "entity");

        shaper = BuildShaperBlock(selectExpression, shaper, record);

        var tableDataAddEntity = typeof(TableData<>)
            .MakeGenericType(targetEntityType)
            .GetMethod(
                nameof(TableData<object>.AddEntity),
                BindingFlags.Public | BindingFlags.Instance,
                new[] { typeof(string), targetEntityType })
            ?? throw new InvalidOperationException("Could not find method AddEntity");

        var breakLabel = Expression.Label("BreakLabel");
        var expressions = new List<Expression>
            {
                Expression.Assign(
                    tableData,
                    Expression.Call(
                        navigationDataParameter,
                        _navigationDataGetTableData.MakeGenericMethod(targetEntityType),
                        new[] { Expression.Constant(tableName) })),
                Expression.Assign(
                    recordIndex,
                    Expression.Constant(0)),
                Expression.Loop(
                    Expression.Block(
                        new[] { record, entity },
                        Expression.IfThen(
                            Expression.GreaterThanOrEqual(
                                recordIndex,
                                Expression.Property(
                                    recordsParameter,
                                    nameof(List<object>.Count))),
                            Expression.Break(breakLabel)),
                        Expression.Assign(
                            record,
                            Expression.MakeIndex(
                                recordsParameter,
                                typeof(List<AirtableRecord>).GetProperty("Item"),
                                new[] { recordIndex })),
                        Expression.Assign(entity, shaper),

                        Expression.Call(
                            tableData,
                            tableDataAddEntity,
                            new Expression[]
                            {
                                Expression.Property(record, "Id"),
                                entity
                            }),

                        Expression.PostIncrementAssign(recordIndex)),
                    breakLabel),
                Expression.Call(
                    tableData,
                    _tableDataMarkEntitiesAsLoaded),
            };

        var block = Expression.Block(
            variables,
            expressions);

        return Expression.Lambda<Action<AirtableQueryContext, List<AirtableRecord>, NavigationData>>(
            block,
            QueryCompilationContext.QueryContextParameter,
            recordsParameter,
            navigationDataParameter);
    }

    private void VisitNavigation(Dictionary<string, Expression<Action<AirtableQueryContext, List<AirtableRecord>, NavigationData>>> shaperMapping, NavigationResolver navigationResolver, IEntityType declaringEntityType, INavigationBase navigation)
    {
        BuildNavigationLambdas(navigation, declaringEntityType, navigationResolver);

        var targetEntityType = navigation.TargetEntityType;
        if (targetEntityType.GetTableName() is not {} navigationTableName || shaperMapping.ContainsKey(navigationTableName))
        {
            return;
        }

        var navigationSelectExpression = new SelectExpression(targetEntityType);
        var shaper = new StructuralTypeShaperExpression(
            targetEntityType,
            new ProjectionBindingExpression(navigationSelectExpression, new ProjectionMember(), typeof(ValueBuffer)),
            false);

        // Avoid infinite recursion:
        shaperMapping.Add(navigationTableName, null!);

        var shaperLambda = BuildShaperLambda(navigationSelectExpression, shaper, navigationTableName, targetEntityType.ClrType);
        shaperMapping[navigationTableName] = shaperLambda;

        return;
    }

    private void BuildNavigationLambdas(INavigationBase navigation, IEntityType declaringEntityType, NavigationResolver navigationResolver)
    {
        var targetEntityType = navigation.TargetEntityType;

        IForeignKey foreignKey;
        string foreignKeyFieldName;
        switch (navigation)
        {
            case INavigation navigation_:
            {
                foreignKey = navigation_.ForeignKey;
                var foreignKeyProperties = foreignKey.Properties;
                if (foreignKeyProperties.Count != 1)
                {
                    return;
                }
                var foreignKeyProperty = foreignKeyProperties.First();
                if (foreignKeyProperty.GetFieldName() is not {} fkFieldName)
                {
                    return;
                }
                foreignKeyFieldName = fkFieldName;
                break;
            }
            case ISkipNavigation skipNavigation:
            {
                // Reverse the direction of the skip navigation.
                var inverse = skipNavigation.Inverse;
                (declaringEntityType, targetEntityType, skipNavigation, navigation) = (targetEntityType, declaringEntityType, inverse, inverse);

                foreignKey = skipNavigation.ForeignKey;
                var foreignKeyProperties = foreignKey.Properties;
                if (foreignKeyProperties.Count != 1)
                {
                    return;
                }

                if (foreignKey.FindAnnotation(AirtableAnnotationNames.LinkIdProperty)?.Value is not PropertyInfo idProperty)
                {
                    return;
                }

                foreignKeyFieldName = idProperty.Name;

                break;
            }
            default:
            {
                throw new InvalidOperationException("Found unrecognized navigation type");
            }
        }

        if (navigation.GetFieldName() is not {} backingFieldName
            || targetEntityType.GetTableName() is not {} tableName
            || declaringEntityType.GetTableName() is not {} referencingTableName
            || navigation.Inverse?.GetFieldName() is not {} inverseBackingFieldName)
        {
            return;
        }

        var entityMappingsType = typeof(EntityMappingList<>)
            .MakeGenericType(declaringEntityType.ClrType);
        var inverseEntityMappingsType = typeof(EntityMappingList<>)
            .MakeGenericType(targetEntityType.ClrType);
        var entityMappingType = typeof(EntityMapping<>)
            .MakeGenericType(declaringEntityType.ClrType);
        var inverseEntityMappingType = typeof(EntityMapping<>)
            .MakeGenericType(targetEntityType.ClrType);
        var referencingEntityListType = typeof(List<>)
            .MakeGenericType(declaringEntityType.ClrType);
        var referencedEntityListType = typeof(List<>)
            .MakeGenericType(targetEntityType.ClrType);

        var referencedTableData = Expression.Variable(
            typeof(TableData<>)
                .MakeGenericType(targetEntityType.ClrType),
            "referencedTableData");
        var referencingTableData = Expression.Variable(
            typeof(TableData<>)
                .MakeGenericType(declaringEntityType.ClrType),
            "referencingTableData");
        var entityMappings = Expression.Variable(entityMappingsType, "entityMappings");
        var inverseEntityMappings = Expression.Variable(inverseEntityMappingsType, "inverseEntityMappings");
        var referencedEntities = Expression.Variable(referencedEntityListType, "referencedEntities");
        var referencingEntities = Expression.Variable(referencingEntityListType, "referencingEntities");

        var referencingEntity = Expression.Variable(declaringEntityType.ClrType, "referencingEntity");
        var referencedEntity = Expression.Variable(targetEntityType.ClrType, "referencedEntity");

        var navigationDataParameter = Expression.Parameter(typeof(NavigationData), "navigationData");

        var referencedEntityCollectionType = typeof(ICollection<>)
            .MakeGenericType(targetEntityType.ClrType);
        var referencingEntityCollectionType = typeof(ICollection<>)
            .MakeGenericType(declaringEntityType.ClrType);

        var entityMappingListAddEntityMapping =
            entityMappingsType
                .GetMethod(
                    nameof(EntityMappingList<object>.AddEntityMapping),
                    BindingFlags.Public | BindingFlags.Instance,
                    new[] { declaringEntityType.ClrType, typeof(string) })
                ?? throw new InvalidOperationException("Could not find method AddEntityMapping");
        var entityMappingListAddEntityMappings =
            entityMappingsType
                .GetMethod(
                    nameof(EntityMappingList<object>.AddEntityMappings),
                    BindingFlags.Public | BindingFlags.Instance,
                    new[] { declaringEntityType.ClrType, typeof(IEnumerable<string>) })
                ?? throw new InvalidOperationException("Could not find method AddEntityMappings");

        var breakLabelNumber = 0;
        LabelTarget breakLabel;

        // Generate navigation collection lambdas
        {
            var referencedEntityCollectionClear =
                referencedEntityCollectionType
                    .GetMethod(
                        nameof(ICollection<object>.Clear),
                        BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("Could not find method Clear");
            var referencedEntityListConstructor = referencedEntityListType.GetConstructor(Type.EmptyTypes)
                ?? throw new InvalidOperationException("Could not find List<> constructor");

            var entityIndex = Expression.Variable(typeof(int), "entityIndex");
            var variables = new List<ParameterExpression> { referencedTableData, referencingTableData, referencingEntities, entityIndex, entityMappings, inverseEntityMappings };
            var navigationCollectionExpressions = new List<Expression>
                {
                    Expression.Assign(
                        referencedTableData,
                        Expression.Call(
                            navigationDataParameter,
                            _navigationDataGetTableData.MakeGenericMethod(targetEntityType.ClrType),
                            new[] { Expression.Constant(tableName) })),
                    Expression.Assign(
                        entityMappings,
                        Expression.Call(
                            referencedTableData,
                            _tableDataGetEntityMappings.MakeGenericMethod(declaringEntityType.ClrType),
                            new[] { Expression.Constant(referencingTableName), Expression.Constant(navigation.Name) })),
                    Expression.Assign(
                        referencingTableData,
                        Expression.Call(
                            navigationDataParameter,
                            _navigationDataGetTableData.MakeGenericMethod(declaringEntityType.ClrType),
                            new[] { Expression.Constant(referencingTableName) })),

                    Expression.Assign(
                        referencingEntities,
                        Expression.Field(referencingTableData, nameof(TableData<object>.Entities)))
                };

            breakLabel = Expression.Label($"BreakLabel{breakLabelNumber++}");
            navigationCollectionExpressions.AddRange(
                new Expression[]
                {
                    Expression.Assign(
                        entityIndex,
                        Expression.Property(
                            referencingTableData,
                            nameof(TableData<object>.FirstUnvisitedEntity))),
                    Expression.Loop(
                        Expression.Block(
                            new[] { referencingEntity },
                            Expression.IfThen(
                                Expression.GreaterThanOrEqual(
                                    entityIndex,
                                    Expression.Property(referencingEntities, nameof(List<object>.Count))),
                            Expression.Break(breakLabel)),

                            Expression.Assign(
                                referencingEntity,
                                Expression.MakeIndex(
                                    referencingEntities,
                                    referencingEntityListType.GetProperty("Item"),
                                    new[] { entityIndex })),
                            navigation is ISkipNavigation
                                ? Expression.Block(
                                    Expression.IfThen(
                                        Expression.Equal(
                                            Expression.Field(referencingEntity, backingFieldName),
                                            Expression.Constant(null, referencedEntityCollectionType)),
                                        Expression.Assign(
                                            Expression.Field(referencingEntity, backingFieldName),
                                            Expression.New(referencedEntityListConstructor))),
                                    Expression.Call(
                                        Expression.Field(referencingEntity, backingFieldName),
                                        referencedEntityCollectionClear))
                                : Expression.Assign(
                                    Expression.Field(referencingEntity, backingFieldName),
                                    Expression.Constant(null, targetEntityType.ClrType)),

                            Expression.PostIncrementAssign(entityIndex)),
                        breakLabel)
                });

            breakLabel = Expression.Label($"BreakLabel{breakLabelNumber++}");
            navigationCollectionExpressions.AddRange(
                new Expression[]
                {
                    Expression.Assign(
                        entityIndex,
                        Expression.Property(
                            referencingTableData,
                            nameof(TableData<object>.FirstUnvisitedEntity))),
                    Expression.Loop(
                        Expression.Block(
                            new[] { referencingEntity },
                            Expression.IfThen(
                                Expression.GreaterThanOrEqual(
                                    entityIndex,
                                    Expression.Property(referencingEntities, nameof(List<object>.Count))),
                            Expression.Break(breakLabel)),

                            Expression.Assign(
                                referencingEntity,
                                Expression.MakeIndex(
                                    referencingEntities,
                                    referencingEntityListType.GetProperty("Item"),
                                    new[] { entityIndex })),
                            Expression.IfThen(
                                Expression.NotEqual(
                                    Expression.PropertyOrField(referencingEntity, foreignKeyFieldName),
                                    Expression.Constant(null, navigation is ISkipNavigation ? typeof(IEnumerable<string>) : typeof(string))),
                                Expression.Call(
                                    entityMappings,
                                    navigation is ISkipNavigation
                                        ? entityMappingListAddEntityMappings
                                        : entityMappingListAddEntityMapping,
                                    new Expression[]
                                    {
                                        referencingEntity,
                                        Expression.PropertyOrField(referencingEntity, foreignKeyFieldName)
                                    })),

                            Expression.PostIncrementAssign(entityIndex)),
                        breakLabel)
                });

            var navigationCollectionLambda = Expression.Lambda<Action<NavigationData>>(
                Expression.Block(
                    variables,
                    navigationCollectionExpressions),
                navigationDataParameter);

            navigationResolver.NavigationCollectionLambdas.Add(navigationCollectionLambda.Compile());
        }

        // Generate inverse navigation clear lambdas
        {
            var referencingEntityCollectionClear =
                referencingEntityCollectionType
                    .GetMethod(
                        nameof(ICollection<object>.Clear),
                        BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("Could not find method Clear");
            var referencingEntityListConstructor = referencingEntityListType.GetConstructor(Type.EmptyTypes)
                ?? throw new InvalidOperationException("Could not find List<> constructor");

            var entityIndex = Expression.Variable(typeof(int), "entityIndex");
            var variables = new List<ParameterExpression> { referencedTableData, referencedEntities, entityIndex };
            var navigationCollectionExpressions = new List<Expression>
                {
                    Expression.Assign(
                        referencedTableData,
                        Expression.Call(
                            navigationDataParameter,
                            _navigationDataGetTableData.MakeGenericMethod(targetEntityType.ClrType),
                            new[] { Expression.Constant(tableName) })),

                    Expression.Assign(
                        referencedEntities,
                        Expression.Field(referencedTableData, nameof(TableData<object>.Entities)))
                };

            breakLabel = Expression.Label($"BreakLabel{breakLabelNumber++}");
            navigationCollectionExpressions.AddRange(
                new Expression[]
                {
                    Expression.Assign(
                        entityIndex,
                        Expression.Property(
                            referencedTableData,
                            nameof(TableData<object>.FirstUnvisitedEntity))),
                    Expression.Loop(
                        Expression.Block(
                            new[] { referencedEntity },
                            Expression.IfThen(
                                Expression.GreaterThanOrEqual(
                                    entityIndex,
                                    Expression.Property(referencedEntities, nameof(List<object>.Count))),
                            Expression.Break(breakLabel)),

                            Expression.Assign(
                                referencedEntity,
                                Expression.MakeIndex(
                                    referencedEntities,
                                    referencedEntityListType.GetProperty("Item"),
                                    new[] { entityIndex })),

                            Expression.IfThen(
                                Expression.Equal(
                                    Expression.Field(referencedEntity, inverseBackingFieldName),
                                    Expression.Constant(null, referencingEntityCollectionType)),
                                Expression.Assign(
                                    Expression.Field(referencedEntity, inverseBackingFieldName),
                                    Expression.New(referencingEntityListConstructor))),
                            Expression.Call(
                                Expression.Field(referencedEntity, inverseBackingFieldName),
                                referencingEntityCollectionClear),

                            Expression.PostIncrementAssign(entityIndex)),
                        breakLabel)
                });

            var navigationCollectionLambda = Expression.Lambda<Action<NavigationData>>(
                Expression.Block(
                    variables,
                    navigationCollectionExpressions),
                navigationDataParameter);

            navigationResolver.NavigationCollectionLambdas.Add(navigationCollectionLambda.Compile());
        }

        // Generate navigation fixup lambda
        {
            var entityMappingsListType = typeof(List<>)
                .MakeGenericType(entityMappingType);
            var entityMappingsList = Expression.Variable(entityMappingsListType, "entityMappingsList");

            var nextReferencingEntity = Expression.Variable(declaringEntityType.ClrType, "nextReferencingEntity");

            var referencedEntityCollectionAdd =
                referencedEntityCollectionType
                    .GetMethod(
                        nameof(ICollection<object>.Add),
                        BindingFlags.Public | BindingFlags.Instance,
                        new[] { targetEntityType.ClrType })
                    ?? throw new InvalidOperationException("Could not find method Add");
            var referencingEntityCollectionAdd =
                referencingEntityCollectionType
                    .GetMethod(
                        nameof(ICollection<object>.Add),
                        BindingFlags.Public | BindingFlags.Instance,
                        new[] { declaringEntityType.ClrType })
                    ?? throw new InvalidOperationException("Could not find method Add");

            var referencedEntityListConstructor = referencedEntityListType.GetConstructor(Type.EmptyTypes)
                ?? throw new InvalidOperationException("Could not find List<> constructor");

            var entityMappingIndex = Expression.Variable(typeof(int), "entityMappingIndex");
            var referencedRecordIndex = Expression.Variable(typeof(int), "referencedRecordIndex");
            var variables = new List<ParameterExpression> { referencedTableData, referencingTableData, entityMappings, entityMappingsList, referencedEntities, referencedEntity, referencingEntity, entityMappingIndex };
            if (navigation is ISkipNavigation)
            {
                variables.Add(nextReferencingEntity);
            }

            var entityMapping = Expression.Variable(entityMappingType, "entityMapping");

            var navigationFixupExpressions = new List<Expression>
                {
                    Expression.Assign(
                        referencedTableData,
                        Expression.Call(
                            navigationDataParameter,
                            _navigationDataGetTableData.MakeGenericMethod(targetEntityType.ClrType),
                            new[] { Expression.Constant(tableName) })),

                    Expression.Assign(
                        entityMappings,
                        Expression.Call(
                            referencedTableData,
                            _tableDataGetEntityMappings.MakeGenericMethod(declaringEntityType.ClrType),
                            new[]
                            {
                                Expression.Constant(referencingTableName),
                                Expression.Constant(navigation.Name)
                            })),

                    Expression.Assign(
                        entityMappingsList,
                        Expression.Field(entityMappings, nameof(EntityMappingList<object>.Mappings))),

                    Expression.Assign(
                        referencedEntities,
                        Expression.Field(referencedTableData, nameof(TableData<object>.Entities))),

                    Expression.Assign(
                        entityMappingIndex,
                        Expression.Property(
                            entityMappings,
                            nameof(EntityMappingListBase.FirstUnvisitedEntityMapping))),
                };

            if (navigation is ISkipNavigation)
            {
                navigationFixupExpressions.Add(Expression.Assign(referencingEntity, Expression.Constant(null, declaringEntityType.ClrType)));
            }

            breakLabel = Expression.Label($"BreakLabel{breakLabelNumber++}");
            var loopExpressions = new List<Expression>
                {
                    Expression.IfThen(
                        Expression.GreaterThanOrEqual(
                            entityMappingIndex,
                            Expression.Property(entityMappingsList, nameof(List<object>.Count))),
                        Expression.Break(breakLabel)),
                    Expression.Assign(
                        entityMapping,
                        Expression.MakeIndex(
                            entityMappingsList,
                            entityMappingsListType.GetProperty("Item"),
                            new[] { entityMappingIndex })),
                    Expression.Assign(
                        navigation is ISkipNavigation
                            ? nextReferencingEntity
                            : referencingEntity,
                        Expression.Field(
                            entityMapping,
                            nameof(EntityMapping<object>.ReferencingEntity))),
                };

            if (navigation is ISkipNavigation)
            {
                loopExpressions.Add(Expression.Assign(referencingEntity, nextReferencingEntity));
            }

            loopExpressions.Add(
                Expression.Assign(
                    referencedRecordIndex,
                    Expression.Field(
                        entityMapping,
                        nameof(EntityMapping<object>.ReferencedRecordIndex))));

            loopExpressions.Add(
                Expression.Assign(
                    referencedEntity,
                    Expression.MakeIndex(
                        referencedEntities,
                        referencedEntityListType.GetProperty("Item"),
                        new[] { referencedRecordIndex })));

            if (navigation is ISkipNavigation)
            {
                loopExpressions.Add(
                    Expression.Call(
                        Expression.Field(referencingEntity, backingFieldName),
                        referencedEntityCollectionAdd,
                        new[] { referencedEntity }));
            }
            else
            {
                loopExpressions.Add(
                    Expression.Assign(
                        Expression.Field(referencingEntity, backingFieldName),
                        referencedEntity));
            }

            loopExpressions.Add(
                Expression.Call(
                    Expression.Field(referencedEntity, inverseBackingFieldName),
                    referencingEntityCollectionAdd,
                    new[] { referencingEntity }));

            loopExpressions.Add(Expression.PostIncrementAssign(entityMappingIndex));
            navigationFixupExpressions.Add(
                Expression.Loop(
                    Expression.Block(
                        new[] { entityMapping, referencedRecordIndex },
                        loopExpressions),
                    breakLabel)
            );
            navigationFixupExpressions.Add(
                Expression.Call(
                    entityMappings,
                    _entityMappingListMarkEntityMappingsAsVisited));
            var navigationFixupLambda = Expression.Lambda<Action<NavigationData>>(
                Expression.Block(
                    variables,
                    navigationFixupExpressions),
                navigationDataParameter);

            navigationResolver.NavigationFixupLambdas.Add(navigationFixupLambda.Compile());
        }
    }

    private class NavigationResolver
    {
        public readonly List<Action<NavigationData>> NavigationCollectionLambdas = new();
        public readonly List<Action<NavigationData>> NavigationFixupLambdas = new();
    }

    private class NavigationData
    {
        public readonly Dictionary<string, TableDataBase> Tables = new();

        public TableData<Entity> GetTableData<Entity>(string tableName)
        {
            if (!Tables.TryGetValue(tableName, out var tableData))
            {
                tableData = new TableData<Entity>();
                Tables.Add(tableName, tableData);
            }

            if (tableData is not TableData<Entity> stronglyTypedTableData)
            {
                throw new InvalidOperationException("Tried to get table data as two different types.");
            }

            return stronglyTypedTableData;
        }
    }

    private abstract class TableDataBase
    {
        public Dictionary<(string ReferencingTable, string ReferencingField), EntityMappingListBase> EntityMappings = new();
        public Dictionary<string, int> RecordIndices = new();
        public List<string> RecordIds = new();

        public int FirstUnloadedEntity { get; protected set; } = 0;

        public abstract void MarkEntitiesAsVisited();
        public abstract void MarkEntitiesAsLoaded();

        public EntityMappingList<Entity> GetEntityMappings<Entity>(string referencingTableId, string referencingFieldId)
        {
            if (!EntityMappings.TryGetValue((referencingTableId, referencingFieldId), out var references))
            {
                references = new EntityMappingList<Entity>(this);
                EntityMappings.Add((referencingTableId, referencingFieldId), references);
            }

            if (references is not EntityMappingList<Entity> stronglyTypedReferences)
            {
                throw new InvalidOperationException("Tried to get list of referencing entities as two different types.");
            }

            return stronglyTypedReferences;
        }

        public int AddRecordId(string recordId)
        {
            if (RecordIndices.TryGetValue(recordId, out var recordIndex))
            {
                return recordIndex;
            }
            else
            {
                recordIndex = RecordIds.Count;
                RecordIds.Add(recordId);
                RecordIndices.Add(recordId, recordIndex);
                return recordIndex;
            }
        }

        public void AddRecordIds(IEnumerable<string> recordIds)
        {
            foreach (var recordId in recordIds)
            {
                AddRecordId(recordId);
            }
        }
    }

    private class TableData<Entity> : TableDataBase
    {
        public readonly List<Entity> Entities = new();
        public int FirstUnvisitedEntity { get; private set; } = 0;

        public override void MarkEntitiesAsVisited() => FirstUnvisitedEntity = Entities.Count;
        public override void MarkEntitiesAsLoaded()
        {
            for (var i = FirstUnloadedEntity; i < Entities.Count; i++)
            {
                if (Entities[i] == null)
                {
                    throw new InvalidOperationException("Not all entities loaded");
                }
            }
            FirstUnloadedEntity = Entities.Count;
        }

        public void AddEntity(string recordId, Entity entity)
        {
            var index = AddRecordId(recordId);
            if (index >= Entities.Count)
            {
                Entities.AddRange(Enumerable.Repeat<Entity?>(default(Entity?), index + 1 - Entities.Count));
            }
            if (Entities[index] != null)
            {
                throw new InvalidOperationException("Tried to overwrite existing entity");
            }
            Entities[index] = entity;
        }
    }

    private abstract class EntityMappingListBase(TableDataBase table)
    {
        public TableDataBase Table { get; } = table;

        public int FirstUnvisitedEntityMapping { get; protected set; } = 0;

        public abstract void MarkEntityMappingsAsVisited();
    }

    private class EntityMappingList<Entity>(TableDataBase table) : EntityMappingListBase(table)
    {
        public readonly List<EntityMapping<Entity>> Mappings = new();

        public void AddEntityMapping(Entity entity, string recordId)
        {
            var recordIndex = Table.AddRecordId(recordId);
            Mappings.Add(new EntityMapping<Entity>(entity, recordIndex));
        }

        public void AddEntityMappings(Entity entity, IEnumerable<string> recordIds)
        {
            foreach (var recordId in recordIds)
            {
                AddEntityMapping(entity, recordId);
            }
        }

        public override void MarkEntityMappingsAsVisited() => FirstUnvisitedEntityMapping = Mappings.Count;
    }

    private readonly struct EntityMapping<Entity>(Entity referencingEntity, int referencedRecordIndex)
    {
        public readonly Entity ReferencingEntity = referencingEntity;
        public readonly int ReferencedRecordIndex = referencedRecordIndex;
    }

    private sealed class QueryingEnumerable<T> : IAsyncEnumerable<T>
    {
        private readonly AirtableQueryContext _airtableQueryContext;
        private readonly SelectExpression _selectExpression;
        private readonly FormulaGenerator _formulaGenerator;
        private readonly Func<AirtableQueryContext, AirtableRecord, T> _shaper;
        private readonly Dictionary<string, Action<AirtableQueryContext, List<AirtableRecord>, NavigationData>> _shaperMapping;
        private readonly NavigationResolver _navigationResolver;
        private readonly NavigationData _navigationData;
        private readonly bool _standalone;
        private readonly IAirtableClient _base;

        public QueryingEnumerable(
            AirtableQueryContext airtableQueryContext,
            SelectExpression selectExpression,
            Func<AirtableQueryContext, AirtableRecord, T> shaper,
            Dictionary<string, Action<AirtableQueryContext, List<AirtableRecord>, NavigationData>> shaperMapping,
            NavigationResolver navigationResolver,
            bool standalone)
        {
            _airtableQueryContext = airtableQueryContext;
            _selectExpression = selectExpression;
            _formulaGenerator = new FormulaGenerator(airtableQueryContext.Parameters);
            _shaper = shaper;
            _shaperMapping = shaperMapping;
            _navigationResolver = navigationResolver;
            _navigationData = new();
            _standalone = standalone;
            _base = _airtableQueryContext.AirtableClient;
        }

        public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            _airtableQueryContext.InitializeStateManager(_standalone);

            var shouldResolveNavigations = _navigationResolver.NavigationCollectionLambdas.Count > 0;

            var formulaExpr = _selectExpression.FilterByFormula;

            string? singleRecordId = null;
            string? formula = null;

            if (formulaExpr is not null)
            {
                //User expects that if record is not in the view it should not be selected
                //So recordId optimization will not work for the views, as it will always return record
                if (_selectExpression.View is null)
                {
                    singleRecordId = _formulaGenerator.TryExtractSingleRecordId(formulaExpr);

                    //This optimizes call to single entity retrival operation
                    //This is also used by ReloadAsync call
                    if (singleRecordId is not null)
                    {
                        var record = await _base.GetRecord(_selectExpression.Table, singleRecordId);

                        if (record is null)
                            throw new InvalidOperationException("Airtable response is null");

                        if (!record.Success)
                            throw new InvalidOperationException("Airtable error", record.AirtableApiError);

                        if (shouldResolveNavigations)
                        {
                            await foreach (var entity in ProcessElements(new[] { record.Record }))
                            {
                                yield return entity;
                            }
                        }
                        else
                        {
                            yield return _shaper(_airtableQueryContext, record.Record);
                        }
                        yield break;
                    }
                }

                formula = _formulaGenerator.GetFormula(formulaExpr);
            }

            int? limit = null;
            if (_selectExpression.Limit != null)
            {
                var limitExpr = _selectExpression.Limit;
                if (limitExpr is ConstantExpression constant)
                {
                    limit = constant.GetConstantValue<int>();
                }
                else if (limitExpr is ParameterExpression param)
                {
                    limit = Convert.ToInt32(_airtableQueryContext.Parameters[param.Name!]);
                }
                else if (limitExpr.GetType().Name == "QueryParameterExpression")
                {
                    // Handle EF Core's QueryParameterExpression via reflection
                    var nameProperty = limitExpr.GetType().GetProperty("Name");
                    var name = nameProperty?.GetValue(limitExpr) as string ?? throw new InvalidOperationException("QueryParameterExpression must have a Name");
                    limit = Convert.ToInt32(_airtableQueryContext.Parameters[name]);
                }
                else
                {
                    throw new InvalidOperationException($"Failed to convert limit expression of type {limitExpr.GetType().Name}");
                }
            }

            var records = new List<AirtableRecord>();
            AirtableListRecordsResponse? response = null;
            List<Sort>? sortDescriptors = null;

            if (_selectExpression.Sort.Count > 0)
            {
                sortDescriptors = new List<Sort>();
                foreach (var (field, descending) in _selectExpression.Sort)
                {
                    sortDescriptors.Add(new Sort { Field = field.Name, Direction = descending ? SortDirection.Desc : SortDirection.Asc });
                }
            }

            //TODO: consider resolving dependencies and yielding after each request below.
            //this would reduce latency for large requests, at the expense of potentially a greater number of more
            //smaller requests to resolve navigations.
            do
            {
                var toGet = limit == null
                    ? null
                    : (limit - records.Count);

                if (toGet == 0) break;

                response = await _base.ListRecords(
                    _selectExpression.Table,
                    fields: _selectExpression.GetFields(),
                    maxRecords: toGet,
                    filterByFormula: formula,
                    view: _selectExpression.View,
                    offset: response?.Offset,
                    sort: sortDescriptors);

                if (response is null)
                    throw new InvalidOperationException("Airtable response is null");

                if (!response.Success)
                    throw new InvalidOperationException("Airtable error", response.AirtableApiError);

                if (shouldResolveNavigations)
                {
                    records.AddRange(response.Records);
                }
                else
                {
                    foreach (var record in response.Records)
                    {
                        yield return _shaper(_airtableQueryContext, record);
                    }
                }
            }
            while (response.Offset != null);

            if (shouldResolveNavigations)
            {
                await foreach (var entity in ProcessElements(records))
                {
                    yield return entity;
                }
            }
        }

        private async IAsyncEnumerable<T> ProcessElements(IEnumerable<AirtableRecord> records)
        {
            var originalTableData = _navigationData.GetTableData<T>(_selectExpression.Table);
            foreach (var record in records)
            {
                originalTableData.AddEntity(record.Id, _shaper(_airtableQueryContext, record));
            }
            originalTableData.MarkEntitiesAsLoaded();

            foreach (var collectNavigations in _navigationResolver.NavigationCollectionLambdas)
            {
                collectNavigations(_navigationData);
            }

            foreach (var table in _navigationData.Tables.Values)
            {
                table.MarkEntitiesAsVisited();
            }

            AirtableListRecordsResponse? response = null;
            var newRecords = new List<AirtableRecord>();
            var foundNewRecords = true;
            while (foundNewRecords)
            {
                foundNewRecords = false;
                foreach (var (tableName, tableData) in _navigationData.Tables)
                {
                    if (tableData.FirstUnloadedEntity < tableData.RecordIds.Count)
                    {
                        foundNewRecords = true;
                    }
                    else
                    {
                        continue;
                    }

                    var filter = new StringBuilder("OR(");
                    for (var index = tableData.FirstUnloadedEntity; index < tableData.RecordIds.Count; index++)
                    {
                        if (index > tableData.FirstUnloadedEntity)
                        {
                            filter.Append(", ");
                        }
                        filter.Append("RECORD_ID() = \"");
                        filter.Append(tableData.RecordIds[index]);
                        filter.Append('"');
                    }
                    filter.Append(')');

                    newRecords.Clear();

                    response = null;
                    do
                    {
                        response = await _base.ListRecords(
                            tableName,
                            filterByFormula: filter.ToString(),
                            offset: response?.Offset);

                        if (response is null)
                            throw new InvalidOperationException("Airtable response is null");

                        if (!response.Success)
                            throw new InvalidOperationException("Airtable error", response.AirtableApiError);

                        newRecords.AddRange(response.Records);
                    }
                    while (response.Offset != null);

                    _shaperMapping[tableName](_airtableQueryContext, newRecords, _navigationData);
                }

                foreach (var collectNavigations in _navigationResolver.NavigationCollectionLambdas)
                {
                    collectNavigations(_navigationData);
                }

                foreach (var table in _navigationData.Tables.Values)
                {
                    table.MarkEntitiesAsVisited();
                }
            }

            foreach (var fixupNavigations in _navigationResolver.NavigationFixupLambdas)
            {
                fixupNavigations(_navigationData);
            }

            foreach (var entity in originalTableData.Entities)
            {
                yield return entity;
            }
        }
    }

    private static async IAsyncEnumerable<int> CountImplAsync<T>(IAsyncEnumerable<T> values)
    {
        var count = 0;
        var enumerator = values.GetAsyncEnumerator();
        while (await enumerator.MoveNextAsync())
        {
            count++;
        }
        yield return count;
    }

    private static IEnumerable<int> CountImpl<T>(IEnumerable<T> values)
    {
        var count = 0;
        var enumerator = values.GetEnumerator();
        while (enumerator.MoveNext())
        {
            count++;
        }
        yield return count;
    }

    private static MethodInfo CountImplAsyncMethod = typeof(AirtableShapedQueryCompilingExpressionVisitor)
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .FirstOrDefault(m => m.Name == nameof(CountImplAsync) && m.IsGenericMethod)
        ?? throw new InvalidOperationException("CountImplAsync<T> method not found.");

    private static MethodInfo CountImplMethod = typeof(AirtableShapedQueryCompilingExpressionVisitor)
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .FirstOrDefault(m => m.Name == nameof(CountImpl) && m.IsGenericMethod)
        ?? throw new InvalidOperationException("CountImpl<T> method not found.");
}
