using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace PrintMerkez.Web.Data;
// Tooling only: never starts the web host or seeds a database.
public sealed class PrintDbContextFactory : IDesignTimeDbContextFactory<PrintDbContext>
{
    public PrintDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<PrintDbContext>()
        .UseSqlite("Data Source=printmerkez-design.db").Options);
}
