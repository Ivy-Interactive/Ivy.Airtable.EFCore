using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Airtable.EFCore.Infrastructure;

public class AirtableDbContextOptionsBuilder : RelationalDbContextOptionsBuilder<AirtableDbContextOptionsBuilder, AirtableOptionsExtension>
{
    public AirtableDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder) : base(optionsBuilder)
    {
    }
}
