using Microsoft.EntityFrameworkCore;
using StorageService.Domain.Core.Entity;
using StorageService.Infra.Repository.Configuration;

namespace StorageService.Infra.Repository
{
    public class RepositoryContext : DbContext
    {
        public RepositoryContext(DbContextOptions<RepositoryContext> options) : base(options) { }

      // public DbSet<Product> Products { get; set; }
      /*  protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
      
           // optionsBuilder.UseSqlServer();
        }*/
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //new ProductEntityTypeConfiguration().Configure(modelBuilder.Entity<Product>());
           
           // modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProductEntityTypeConfiguration).Assembly);
            modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        }
    }
}
