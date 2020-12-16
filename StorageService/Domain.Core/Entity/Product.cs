using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace StorageService.Domain.Core.Entity
{
    public class Product : BaseEntity<Guid>
    {
        [MinLength(2)]
        [Required(AllowEmptyStrings =false)]
        public string Cod { get; set; }
        [Required]
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int Quant { get; set; }
        
    }
}
