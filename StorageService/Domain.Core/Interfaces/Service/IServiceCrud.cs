﻿using StorageService.Domain.Core.Entity;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StorageService.Domain.Core.Interfaces.Service
{
    public interface IServiceCrud<TKey, TEntity> where TKey : struct where TEntity : BaseEntity<TKey>
    {
        Task<TEntity> AddAsync(TEntity obj);
        Task<TEntity> DeleteAsync(TKey id);
        Task<TEntity> GetAsync(TKey id);
        Task<IEnumerable<TEntity>> GetAllAsync();
        Task<TEntity> UpdateAsync(TEntity obj);
    }
}
