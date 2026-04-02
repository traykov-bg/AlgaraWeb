using Algara.Web.Models;
using Algara.Utils;
using Dapper;

namespace Algara.Web.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly IDatabaseHelper _dbHelper;

        public ProductRepository(IDatabaseHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        public async Task<IEnumerable<Product>> GetAllAsync()
        {
            return await _dbHelper.QueryAsync<Product>("SELECT * FROM Products");
        }

        public async Task<Product?> GetByIdAsync(int id)
        {
            return await _dbHelper.QuerySingleAsync<Product>("SELECT * FROM Products WHERE Id = @Id", new { Id = id });
        }

        public async Task AddAsync(Product product)
        {
            await _dbHelper.ExecuteAsync("INSERT INTO Products (Name, Description, Price, ImageUrl, IsCustomizable) VALUES (@Name, @Description, @Price, @ImageUrl, @IsCustomizable)",
                product);
        }

        public async Task UpdateAsync(Product product)
        {
            await _dbHelper.ExecuteAsync("UPDATE Products SET Name = @Name, Description = @Description, Price = @Price, ImageUrl = @ImageUrl, IsCustomizable = @IsCustomizable WHERE Id = @Id",
                product);
        }

        public async Task DeleteAsync(int id)
        {
            await _dbHelper.ExecuteAsync("DELETE FROM Products WHERE Id = @Id", new { Id = id });
        }
    }
}