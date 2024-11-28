using Hobby.ProductManagement.Domain.Entity;

namespace Hobby.ProductManagement.Domain.Interface
{
    public interface IProductRepository
    {
        Task<Product> AddAsync(Product product);
        Task<Product> GetByIdAsync(ProductId id);
        Task<IEnumerable<Product>> GetAllAsync();
    }
}
