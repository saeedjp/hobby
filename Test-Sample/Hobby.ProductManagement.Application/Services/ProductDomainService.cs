using Hobby.ProductManagement.Domain.Entity;
using Hobby.ProductManagement.Domain.Interface;
using Hobby.ProductManagement.Domain.Services;

namespace Hobby.ProductManagement.Application.Services
{
    public record CreateProductCommand(
    string Name,
    decimal Price,
    int StockQuantity
);

    public class ProductApplicationService
    {
        private readonly IProductRepository _repository;
        private readonly IProductDomainService _domainService;

        public ProductApplicationService(
            IProductRepository repository,
            IProductDomainService domainService)
        {
            _repository = repository;
            _domainService = domainService;
        }

        public async Task<Product> CreateProductAsync(CreateProductCommand command)
        {
            var product = Product.Create(
                command.Name,
                command.Price,
                command.StockQuantity
            );

            return await _repository.AddAsync(product);
        }
    }
}
