using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using StorageService.Domain.Core.Entity;

namespace StorageService.WebAPI.Controllers
{
    [ApiController]
    public class ProductController : GenericControllerCrud<Guid, Product, Product>
    {
        public ProductController(IServiceProvider serviceProvider) : base(serviceProvider) { }
      
    }
}