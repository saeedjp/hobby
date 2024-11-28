using Hobby.ProductManagement.Application.Services.Commands;
using Hobby.ProductManagement.Domain.Interface;
using Hobby.ProductManagement.Domain.Services;
using Hobby.ProductManagement.Infrastructure;
using Hobby.ProductManagement.Infrastructure.Repository;
using Microsoft.EntityFrameworkCore;

namespace Hobby.ProductManagement.WebApi.Extensions
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddDatabaseServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Configure database context
            services.AddDbContext<ProductDbContext>(options =>
                options.UseSqlServer(
                    configuration.GetConnectionString("DefaultConnection"),
                    b => b.MigrationsAssembly("ProductManagement.Infrastructure")
                )
            );

            // Register repositories
            services.AddScoped<IProductRepository, ProductRepository>();

            return services;
        }

        public static IServiceCollection AddDomainServices(
            this IServiceCollection services)
        {
            // Register domain-specific services
            services.AddScoped<IProductDomainService, ProductDomainService>();
            return services;
        }

        public static IServiceCollection AddApplicationServices(
            this IServiceCollection services)
        {
            // Register application services
            services.AddScoped<ProductApplicationService>();
            return services;
        }
    }

}
