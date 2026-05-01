using Algara.Data.Models;

namespace Algara.Data.Repositories
{
    public interface IOrderRepository
    {
        Task<IEnumerable<Order>> GetAllAsync();
        Task<Order?> GetByNAsync(int n);
        Task<IEnumerable<Order>> GetByUserNAsync(int userN);
        Task<Order?> GetByNForUserAsync(int n, int userN);
        Task UpdateStatusAsync(int n, OrderStatus status);
        Task<int> GetCountByStatusAsync(OrderStatus status);
    }
}
