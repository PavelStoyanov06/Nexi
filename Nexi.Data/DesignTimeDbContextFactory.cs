using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Nexi.Data.Context;

namespace Nexi.Data
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NexiDbContext>
    {
        public NexiDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<NexiDbContext>();
            optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=NexiDb;Trusted_Connection=True;MultipleActiveResultSets=true");

            return new NexiDbContext(optionsBuilder.Options);
        }
    }
}