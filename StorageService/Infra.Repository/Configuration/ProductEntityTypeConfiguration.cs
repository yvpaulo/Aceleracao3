using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StorageService.Domain.Core.Entity;

namespace StorageService.Infra.Repository.Configuration
{
    class ProductEntityTypeConfiguration : IEntityTypeConfiguration<Product>
    {
        public void Configure(EntityTypeBuilder<Product> builder)
        {
            builder.Property(o => o.Id).ValueGeneratedOnAdd().IsRequired();
            builder.Property(o => o.Cod).IsRequired();
            builder.Property(o => o.Name).IsRequired().HasMaxLength(3);
            builder.Property<decimal>(o => o.Price).IsRequired().HasPrecision(20,2);
            builder.Property(o => o.Quant).IsRequired();

        }
    }
}
