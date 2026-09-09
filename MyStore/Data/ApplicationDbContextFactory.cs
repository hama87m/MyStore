using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MyStore.Data;

namespace MyStore.Data
{
    // ئەم کڵاسە تەنها بۆ کاتی دیزاین و Migration بەکار دێت
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            // پێویستە Connection Stringـی ڕاستەقینەت لێرەدا دابنێیت بۆ کاتی دیزاین

            // ⚠️ ئەمە بگۆڕە بە Connection Stringـی ناو appsettings.json
            const string connectionString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=mystoredb;Integrated Security=True;";

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseSqlServer(connectionString);

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}