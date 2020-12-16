using System;
using Microsoft.AspNetCore.Mvc;
using StorageService.Domain.Core.Entity;

namespace StorageService.WebAPI.Controllers
{
    public class ProductController : GenericControllerCrud<Guid, Product, Product>
    {
        public ProductController(IServiceProvider serviceProvider) : base(serviceProvider) { }
      
    }
}