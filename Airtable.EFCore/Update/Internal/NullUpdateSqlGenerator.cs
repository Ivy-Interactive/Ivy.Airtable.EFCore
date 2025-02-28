using Microsoft.EntityFrameworkCore.Update;

namespace Airtable.EFCore.Update.Internal;

public class NullUpdateSqlGenerator : UpdateSqlGenerator
{
    public NullUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies) : base(dependencies)
    {
    }

}
