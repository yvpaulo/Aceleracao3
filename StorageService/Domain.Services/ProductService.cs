using StorageService.Domain.Core.Entity;
using StorageService.Domain.Core.Interfaces.Repository;
using System;
using System.Collections.Generic;
using System.Text;

namespace StorageService.Domain.Services
{
    public class ProductService : GenericServiceCrud<Guid, Product>
    {
       // private readonly IPublisher _Publisher;

        public ProductService(
            IRepositoryCrud<Guid, Product> repository,
           // IPublisher publisher,
            IUnitOfWork unitOfWork) : base(repository, unitOfWork)
        {
           // _Publisher = publisher;
        }
    }
}
