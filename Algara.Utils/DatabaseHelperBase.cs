namespace Algara.Utils
{
    public abstract class DatabaseHelperBase : IDatabaseHelper
    {
        protected readonly string ConnectionString;

        protected DatabaseHelperBase(string connectionString)
        {
            ConnectionString = connectionString;
        }
        public Task EnsureTablesExist()
        {
            // Таблиците се управляват от EF Core миграции (ShopDbContext).
            return Task.CompletedTask;
        }
        public abstract Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null);
        public abstract Task<int> ExecuteAsync(string sql, object? parameters = null);
        public abstract Task<T?> QuerySingleAsync<T>(string sql, object? parameters = null);
    }
}
