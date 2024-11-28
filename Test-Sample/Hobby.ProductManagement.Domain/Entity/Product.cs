namespace Hobby.ProductManagement.Domain.Entity
{
    public record ProductId(int Value);

    public class Product
    {
        public ProductId Id { get; init; }
        public string Name { get; init; }
        public decimal Price { get; init; }
        public int StockQuantity { get; init; }

        public static Product Create(string name, decimal price, int stockQuantity)
        {
            if (price < 0 || stockQuantity < 0)
                throw new ArgumentException("Invalid product parameters");

            return new Product
            {
                Id = new ProductId(0),
                Name = name,
                Price = price,
                StockQuantity = stockQuantity
            };
        }
    }
}
