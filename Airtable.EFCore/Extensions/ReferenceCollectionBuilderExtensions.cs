using System.Linq.Expressions;
using Airtable.EFCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.EntityFrameworkCore;

public static class ReferenceCollectionBuilderExtensions
{
    public static ReferenceCollectionBuilder<TEntity, TRelatedEntity> HasLinkIdsProperty<TEntity, TRelatedEntity>(this ReferenceCollectionBuilder<TEntity, TRelatedEntity> builder, Expression<Func<TEntity, IEnumerable<string>?>> idsExpression)
        where TEntity: class
        where TRelatedEntity: class
    => builder.HasAnnotation(AirtableAnnotationNames.LinkIdProperty, idsExpression.GetMemberAccess());
}
