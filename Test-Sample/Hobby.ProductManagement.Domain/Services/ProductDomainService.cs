using Hobby.ProductManagement.Domain.Entity;

namespace Hobby.ProductManagement.Domain.Services
{
    public interface IProductDomainService
    {
        bool CanReduceStock(Product product, int quantity);
    }

    public class ProductDomainService : IProductDomainService
    {
        public bool CanReduceStock(Product product, int quantity) =>
            product.StockQuantity >= quantity;
    }
}
