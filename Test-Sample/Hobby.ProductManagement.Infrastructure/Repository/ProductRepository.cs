using Hobby.ProductManagement.Domain.Entity;
using Hobby.ProductManagement.Domain.Interface;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hobby.ProductManagement.Infrastructure.Repository
{
    public class ProductRepository : IProductRepository
    {
        private readonly ProductDbContext _context;

        public ProductRepository(ProductDbContext context) =>
            _context = context;

        public async Task<Product> AddAsync(Product product)
        {
            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            return product;
        }

        public async Task<Product> GetByIdAsync(ProductId id)
        {
            return await _context.Products.FindAsync(id.Value);
        }

        public async Task<IEnumerable<Product>> GetAllAsync() =>
            await _context.Products.ToListAsync();
    }
}
