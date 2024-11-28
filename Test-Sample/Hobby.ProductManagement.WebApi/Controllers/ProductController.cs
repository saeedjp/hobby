using Hobby.ProductManagement.Application.Services;
using Hobby.ProductManagement.Domain.Entity;
using Microsoft.AspNetCore.Mvc;

namespace Hobby.ProductManagement.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductController : ControllerBase
    {
        private readonly ProductApplicationService _productService;

        public ProductController(ProductApplicationService productService)
        {
            _productService = productService;
        }

        [HttpPost]
        public async Task<ActionResult<Product>> CreateProduct(
            [FromBody] CreateProductCommand command)
        {
            var product = await _productService.CreateProductAsync(command);
            return CreatedAtAction(
                nameof(GetProduct),
                new { id = product.Id.Value },
                product
            );
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Product>> GetProduct(int id)
        {
            var product = await _productService.GetProductByIdAsync(new ProductId(id));
            return product == null
                ? NotFound()
                : Ok(product);
        }

        [HttpPatch("{id}/stock")]
        public async Task<IActionResult> ReduceStock(
            int id,
            [FromBody] int quantity)
        {
            await _productService.ReduceProductStockAsync(
                new ProductId(id),
                quantity
            );
            return NoContent();
        }
    }

    // DTO for API Inputs
    public record CreateProductRequest(
        string Name,
        decimal Price,
        int StockQuantity
    );
}
