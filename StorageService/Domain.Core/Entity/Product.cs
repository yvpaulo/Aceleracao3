using System;
using System.Collections.Generic;
using System.Text;

namespace StorageService.Domain.Core.Entity
{
    public class Product : BaseEntity<Guid>
    {
        public string Cod { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int Quant { get; set; }
        
    }
}
